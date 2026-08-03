using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Fetch.Net;
using Fetch.Sources;

namespace Fetch
{
    public class FileDownloader : IDisposable
    {
        private const int GOOGLE_DRIVE_MAX_DOWNLOAD_ATTEMPT = 3;

        private readonly HttpMessageHandler httpClientHandler;
        private readonly HttpClient httpClient;
        private readonly DownloadProgress downloadProgress = new DownloadProgress();

        private Uri downloadAddress;
        private string downloadPath;
        private bool downloadingDriveFile;
        private int driveDownloadAttempt;

        public FileDownloader()
            : this(FetchHttpClientFactory.CreateDownloadHandler())
        {
        }

        /// <summary>Accepts a handler so the transport can be substituted in tests.</summary>
        internal FileDownloader(HttpMessageHandler handler)
        {
            httpClientHandler = handler;
            httpClient = FetchHttpClientFactory.CreateDownloadClient(handler);
        }

        /// <summary>
        /// How long the transfer may make no progress before it is abandoned. A slow but advancing
        /// download is allowed to take as long as it needs; a stalled one does not hang Revit.
        /// </summary>
        internal TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(100);

        /// <summary>Bound for the small metadata requests (redirect resolution, release lookup).</summary>
        internal TimeSpan MetadataTimeout { get; set; } = TimeSpan.FromSeconds(30);

        public void DownloadFile(string address, string fileName)
        {
            PrepareDownload(address, fileName);
            DownloadFileWithDriveConfirmationAsync().GetAwaiter().GetResult();
        }

        private void PrepareDownload(string address, string fileName)
        {
            downloadingDriveFile = SourceUrlResolver.IsGoogleDriveAddress(address);
            if (downloadingDriveFile)
            {
                address = SourceUrlResolver.GetGoogleDriveDownloadAddress(address);
                if (string.IsNullOrWhiteSpace(address))
                    throw new ArgumentException("The Google Drive URL is not a supported file download link.", nameof(address));

                driveDownloadAttempt = 1;
            }
            else if (SourceUrlResolver.IsOneDriveAddress(address))
            {
                address = SourceUrlResolver.GetOneDriveDownloadAddress(address, ResolveRedirect);
                if (string.IsNullOrWhiteSpace(address))
                    throw new ArgumentException("The OneDrive URL is not a supported file download link.", nameof(address));
            }
            else if (SourceUrlResolver.IsGitHubReleaseAddress(address))
            {
                address = GetGitHubReleaseDownloadAddress(address);
                if (string.IsNullOrWhiteSpace(address))
                    throw new ArgumentException("The GitHub release URL is not a supported release page or release zip asset.", nameof(address));
            }

            downloadAddress = new Uri(address);
            downloadPath = fileName;
            downloadProgress.BytesReceived = 0L;
            downloadProgress.TotalBytesToReceive = -1L;
        }

        private async Task DownloadFileWithDriveConfirmationAsync()
        {
            while (true)
            {
                await DownloadFileInternalAsync().ConfigureAwait(false);

                if (!downloadingDriveFile ||
                    driveDownloadAttempt >= GOOGLE_DRIVE_MAX_DOWNLOAD_ATTEMPT ||
                    ProcessDriveDownload())
                {
                    return;
                }

                driveDownloadAttempt++;
            }
        }

        private async Task DownloadFileInternalAsync()
        {
            using (CancellationTokenSource idleWatchdog = new CancellationTokenSource(IdleTimeout))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, downloadAddress))
            {
                if (downloadingDriveFile)
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, null);

                try
                {
                    using (HttpResponseMessage response = await httpClient
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idleWatchdog.Token)
                        .ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        SetTotalBytes(response);

                        using (Stream sourceStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (FileStream destinationStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await CopyToFileAsync(sourceStream, destinationStream, idleWatchdog).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException(
                        $"The package download stopped making progress for {IdleTimeout.TotalSeconds:0} seconds and was cancelled. " +
                        "Check the package source and the network connection.");
                }
            }
        }

        private void SetTotalBytes(HttpResponseMessage response)
        {
            if (response.Content.Headers.ContentRange != null && response.Content.Headers.ContentRange.Length.HasValue)
            {
                downloadProgress.TotalBytesToReceive = response.Content.Headers.ContentRange.Length.Value;
            }
            else if (response.Content.Headers.ContentLength.HasValue)
            {
                downloadProgress.TotalBytesToReceive = response.Content.Headers.ContentLength.Value;
            }
        }

        private async Task CopyToFileAsync(Stream sourceStream, Stream destinationStream, CancellationTokenSource idleWatchdog)
        {
            byte[] buffer = new byte[81920];
            int bytesRead;

            while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, idleWatchdog.Token).ConfigureAwait(false)) > 0)
            {
                await destinationStream.WriteAsync(buffer, 0, bytesRead, idleWatchdog.Token).ConfigureAwait(false);
                downloadProgress.BytesReceived += bytesRead;

                // Progress refreshes the deadline, so only a stall trips it.
                idleWatchdog.CancelAfter(IdleTimeout);
            }
        }

        // Downloading large files from Google Drive can return a confirmation page.
        // When that happens, extract the confirmation link and retry the download.
        private bool ProcessDriveDownload()
        {
            FileInfo downloadedFile = new FileInfo(downloadPath);
            if (!downloadedFile.Exists)
                return true;

            if (downloadedFile.Length > 60000L)
                return true;

            string content;
            using (StreamReader reader = downloadedFile.OpenText())
            {
                char[] header = new char[20];
                int readCount = reader.ReadBlock(header, 0, 20);
                if (readCount < 20 || !(new string(header).Contains("<!DOCTYPE html>")))
                    return true;

                content = reader.ReadToEnd();
            }

            string confirmationAddress = SourceUrlResolver.GetDriveConfirmationAddress(content);
            if (confirmationAddress != null)
            {
                downloadAddress = new Uri(confirmationAddress);
                return false;
            }

            return true;
        }

        private string ResolveRedirect(string address)
        {
            using (HttpClient redirectClient = FetchHttpClientFactory.CreateRedirectProbeClient(MetadataTimeout, out HttpMessageHandler handler))
            using (handler)
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, address))
            using (HttpResponseMessage response = redirectClient.SendAsync(request).GetAwaiter().GetResult())
            {
                if (response.Headers.Location == null)
                    return address;

                if (response.Headers.Location.IsAbsoluteUri)
                    return response.Headers.Location.AbsoluteUri;

                return new Uri(new Uri(address), response.Headers.Location).AbsoluteUri;
            }
        }

        private string GetGitHubReleaseDownloadAddress(string address)
        {
            string apiAddress;
            switch (SourceUrlResolver.ClassifyGitHubReleaseAddress(address, out apiAddress))
            {
                case GitHubReleaseAddressKind.DirectAsset:
                    return address;

                case GitHubReleaseAddressKind.ApiLookup:
                    using (CancellationTokenSource metadataTimeout = new CancellationTokenSource(MetadataTimeout))
                    using (HttpResponseMessage response = httpClient.GetAsync(apiAddress, metadataTimeout.Token).GetAwaiter().GetResult())
                    {
                        response.EnsureSuccessStatusCode();
                        string releaseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        return SourceUrlResolver.SelectReleaseAssetUrl(releaseJson);
                    }

                default:
                    return string.Empty;
            }
        }

        public void Dispose()
        {
            httpClient.Dispose();
            httpClientHandler.Dispose();
        }

        /// <summary>Byte counters for the transfer in progress, reported in the log.</summary>
        internal sealed class DownloadProgress
        {
            public long BytesReceived;
            public long TotalBytesToReceive;
        }
    }
}
