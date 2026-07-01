using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Fetch
{
    public class FileDownloader : IDisposable
    {
        private const string GOOGLE_DRIVE_DOMAIN = "drive.google.com";
        private const string ONEDRIVE_DOMAIN = "onedrive.live.com";
        private const string ONEDRIVE_SHORT_DOMAIN = "1drv.ms";
        private const string SHAREPOINT_DOMAIN_SUFFIX = ".sharepoint.com";
        private const string GITHUB_DOMAIN = "github.com";
        private const string GITHUB_API_BASE_ADDRESS = "https://api.github.com/repos/";
        private const int GOOGLE_DRIVE_MAX_DOWNLOAD_ATTEMPT = 3;

        private readonly HttpClientHandler httpClientHandler;
        private readonly HttpClient httpClient;
        private readonly DownloadProgress downloadProgress = new DownloadProgress();

        private Uri downloadAddress;
        private string downloadPath;
        private bool downloadingDriveFile;
        private int driveDownloadAttempt;

        public delegate void DownloadProgressChangedEventHandler(object sender, DownloadProgress progress);

        public event DownloadProgressChangedEventHandler DownloadProgressChanged;
        public event AsyncCompletedEventHandler DownloadFileCompleted;

        public FileDownloader()
        {
            httpClientHandler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                CookieContainer = new CookieContainer(),
                UseCookies = true
            };

            httpClient = new HttpClient(httpClientHandler);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FETCH-Revit");
        }

        public void DownloadFile(string address, string fileName)
        {
            try
            {
                PrepareDownload(address, fileName, null);
                DownloadFileWithDriveConfirmationAsync().GetAwaiter().GetResult();
                OnDownloadFileCompleted(null, false, null);
            }
            catch (Exception ex)
            {
                OnDownloadFileCompleted(ex, false, null);
                throw;
            }
        }

        public void DownloadFileAsync(string address, string fileName, object userToken = null)
        {
            Task.Run(() =>
            {
                try
                {
                    PrepareDownload(address, fileName, userToken);
                    DownloadFileWithDriveConfirmationAsync().GetAwaiter().GetResult();
                    OnDownloadFileCompleted(null, false, userToken);
                }
                catch (Exception ex)
                {
                    OnDownloadFileCompleted(ex, false, userToken);
                }
            });
        }

        private void PrepareDownload(string address, string fileName, object userToken)
        {
            downloadingDriveFile = IsGoogleDriveAddress(address);
            if (downloadingDriveFile)
            {
                address = GetGoogleDriveDownloadAddress(address);
                if (string.IsNullOrWhiteSpace(address))
                    throw new ArgumentException("The Google Drive URL is not a supported file download link.", nameof(address));

                driveDownloadAttempt = 1;
            }
            else if (IsOneDriveAddress(address))
            {
                address = GetOneDriveDownloadAddress(address);
                if (string.IsNullOrWhiteSpace(address))
                    throw new ArgumentException("The OneDrive URL is not a supported file download link.", nameof(address));
            }
            else if (IsGitHubReleaseAddress(address))
            {
                address = GetGitHubReleaseDownloadAddress(address);
                if (string.IsNullOrWhiteSpace(address))
                    throw new ArgumentException("The GitHub release URL is not a supported release asset link.", nameof(address));
            }

            downloadAddress = new Uri(address);
            downloadPath = fileName;
            downloadProgress.BytesReceived = 0L;
            downloadProgress.TotalBytesToReceive = -1L;
            downloadProgress.UserState = userToken;
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
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, downloadAddress))
            {
                if (downloadingDriveFile)
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, null);

                using (HttpResponseMessage response = await httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    SetTotalBytes(response);

                    using (Stream sourceStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (FileStream destinationStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await CopyToFileAsync(sourceStream, destinationStream).ConfigureAwait(false);
                    }
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

        private async Task CopyToFileAsync(Stream sourceStream, Stream destinationStream)
        {
            byte[] buffer = new byte[81920];
            int bytesRead;

            while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                await destinationStream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
                downloadProgress.BytesReceived += bytesRead;
                OnDownloadProgressChanged();
            }
        }

        private void OnDownloadProgressChanged()
        {
            DownloadProgressChangedEventHandler handler = DownloadProgressChanged;
            if (handler != null)
                handler(this, downloadProgress);
        }

        private void OnDownloadFileCompleted(Exception error, bool cancelled, object userToken)
        {
            AsyncCompletedEventHandler handler = DownloadFileCompleted;
            if (handler != null)
                handler(this, new AsyncCompletedEventArgs(error, cancelled, userToken));
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

            int linkIndex = content.LastIndexOf("href=\"/uc?", StringComparison.OrdinalIgnoreCase);
            if (linkIndex >= 0)
            {
                linkIndex += 6;
                int linkEnd = content.IndexOf('"', linkIndex);
                if (linkEnd >= 0)
                {
                    downloadAddress = new Uri("https://drive.google.com" + content.Substring(linkIndex, linkEnd - linkIndex).Replace("&amp;", "&"));
                    return false;
                }
            }

            return true;
        }

        // Handles the following formats:
        // - drive.google.com/open?id=FILEID&resourcekey=RESOURCEKEY
        // - drive.google.com/file/d/FILEID/view?usp=sharing&resourcekey=RESOURCEKEY
        // - drive.google.com/uc?id=FILEID&export=download&resourcekey=RESOURCEKEY
        private string GetGoogleDriveDownloadAddress(string address)
        {
            int index = address.IndexOf("id=", StringComparison.OrdinalIgnoreCase);
            int closingIndex;
            if (index > 0)
            {
                index += 3;
                closingIndex = address.IndexOf('&', index);
                if (closingIndex < 0)
                    closingIndex = address.Length;
            }
            else
            {
                index = address.IndexOf("file/d/", StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    return string.Empty;

                index += 7;
                closingIndex = address.IndexOf('/', index);
                if (closingIndex < 0)
                {
                    closingIndex = address.IndexOf('?', index);
                    if (closingIndex < 0)
                        closingIndex = address.Length;
                }
            }

            string fileID = address.Substring(index, closingIndex - index);

            index = address.IndexOf("resourcekey=", StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                index += 12;
                closingIndex = address.IndexOf('&', index);
                if (closingIndex < 0)
                    closingIndex = address.Length;

                string resourceKey = address.Substring(index, closingIndex - index);
                return string.Concat("https://drive.google.com/uc?id=", fileID, "&export=download&resourcekey=", resourceKey, "&confirm=t");
            }

            return string.Concat("https://drive.google.com/uc?id=", fileID, "&export=download&confirm=t");
        }

        private bool IsGoogleDriveAddress(string address)
        {
            Uri uri;
            return Uri.TryCreate(address, UriKind.Absolute, out uri) &&
                   string.Equals(uri.Host, GOOGLE_DRIVE_DOMAIN, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsOneDriveAddress(string address)
        {
            Uri uri;
            return Uri.TryCreate(address, UriKind.Absolute, out uri) &&
                   (string.Equals(uri.Host, ONEDRIVE_DOMAIN, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Host, ONEDRIVE_SHORT_DOMAIN, StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.EndsWith(SHAREPOINT_DOMAIN_SUFFIX, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsGitHubReleaseAddress(string address)
        {
            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri) ||
                !string.Equals(uri.Host, GITHUB_DOMAIN, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string[] pathParts = GetPathParts(uri);
            return pathParts.Length >= 4 &&
                   string.Equals(pathParts[2], "releases", StringComparison.OrdinalIgnoreCase);
        }

        private string GetOneDriveDownloadAddress(string address)
        {
            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri))
                return string.Empty;

            if (string.Equals(uri.Host, ONEDRIVE_SHORT_DOMAIN, StringComparison.OrdinalIgnoreCase))
            {
                string resolvedAddress = ResolveRedirect(address);
                if (string.Equals(address, resolvedAddress, StringComparison.OrdinalIgnoreCase))
                    return string.Empty;

                return GetOneDriveDownloadAddress(resolvedAddress);
            }

            if (string.Equals(uri.Host, ONEDRIVE_DOMAIN, StringComparison.OrdinalIgnoreCase))
                return GetPersonalOneDriveDownloadAddress(uri);

            if (uri.Host.EndsWith(SHAREPOINT_DOMAIN_SUFFIX, StringComparison.OrdinalIgnoreCase))
                return AddOrReplaceQueryParameter(uri, "download", "1");

            return string.Empty;
        }

        private string ResolveRedirect(string address)
        {
            using (HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = false })
            using (HttpClient redirectClient = new HttpClient(handler))
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

        private string GetPersonalOneDriveDownloadAddress(Uri uri)
        {
            string query = uri.Query.TrimStart('?');
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            return string.Concat(uri.Scheme, "://", uri.Host, "/download?", query);
        }

        private string AddOrReplaceQueryParameter(Uri uri, string key, string value)
        {
            string query = uri.Query.TrimStart('?');
            string[] parts = string.IsNullOrWhiteSpace(query)
                ? new string[0]
                : query.Split('&');
            StringBuilder builder = new StringBuilder();
            bool replaced = false;

            foreach (string part in parts)
            {
                if (string.IsNullOrWhiteSpace(part))
                    continue;

                int equalsIndex = part.IndexOf('=');
                string partKey = equalsIndex >= 0 ? part.Substring(0, equalsIndex) : part;
                string partValue = equalsIndex >= 0 ? part.Substring(equalsIndex + 1) : string.Empty;

                if (builder.Length > 0)
                    builder.Append('&');

                if (string.Equals(partKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    builder.Append(Uri.EscapeDataString(key));
                    builder.Append('=');
                    builder.Append(Uri.EscapeDataString(value));
                    replaced = true;
                }
                else
                {
                    builder.Append(partKey);
                    if (equalsIndex >= 0)
                    {
                        builder.Append('=');
                        builder.Append(partValue);
                    }
                }
            }

            if (!replaced)
            {
                if (builder.Length > 0)
                    builder.Append('&');

                builder.Append(Uri.EscapeDataString(key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(value));
            }

            UriBuilder uriBuilder = new UriBuilder(uri)
            {
                Query = builder.ToString()
            };

            return uriBuilder.Uri.AbsoluteUri;
        }

        private string GetGitHubReleaseDownloadAddress(string address)
        {
            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri))
                return string.Empty;

            string[] pathParts = GetPathParts(uri);
            if (pathParts.Length < 4 ||
                !string.Equals(pathParts[2], "releases", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (string.Equals(pathParts[3], "download", StringComparison.OrdinalIgnoreCase))
                return address;

            string releaseEndpoint;
            if (string.Equals(pathParts[3], "latest", StringComparison.OrdinalIgnoreCase))
            {
                releaseEndpoint = "latest";
            }
            else if (string.Equals(pathParts[3], "tag", StringComparison.OrdinalIgnoreCase) && pathParts.Length >= 5)
            {
                string tagName = Uri.EscapeDataString(Uri.UnescapeDataString(pathParts[4]));
                releaseEndpoint = $"tags/{tagName}";
            }
            else
            {
                return string.Empty;
            }

            string apiAddress = string.Concat(
                GITHUB_API_BASE_ADDRESS,
                Uri.EscapeDataString(pathParts[0]),
                "/",
                Uri.EscapeDataString(pathParts[1]),
                "/releases/",
                releaseEndpoint);

            using (HttpResponseMessage response = httpClient.GetAsync(apiAddress).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();
                string releaseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                GitHubRelease release = JsonSerializer.Deserialize<GitHubRelease>(releaseJson);
                GitHubReleaseAsset asset = SelectGitHubReleaseAsset(release);

                if (asset == null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
                    throw new InvalidOperationException("The GitHub release does not contain a downloadable .zip asset.");

                return asset.BrowserDownloadUrl;
            }
        }

        private GitHubReleaseAsset SelectGitHubReleaseAsset(GitHubRelease release)
        {
            if (release == null || release.Assets == null || release.Assets.Length == 0)
                return null;

            foreach (GitHubReleaseAsset asset in release.Assets)
            {
                if (asset != null &&
                    !string.IsNullOrWhiteSpace(asset.Name) &&
                    asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    return asset;
                }
            }

            return null;
        }

        private string[] GetPathParts(Uri uri)
        {
            return uri.AbsolutePath.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public void Dispose()
        {
            httpClient.Dispose();
            httpClientHandler.Dispose();
        }

        public class DownloadProgress
        {
            public long BytesReceived, TotalBytesToReceive;
            public object UserState;

            public int ProgressPercentage
            {
                get
                {
                    if (TotalBytesToReceive > 0L)
                        return (int)(((double)BytesReceived / TotalBytesToReceive) * 100);

                    return 0;
                }
            }
        }

        private class GitHubRelease
        {
            [JsonPropertyName("assets")]
            public GitHubReleaseAsset[] Assets { get; set; }
        }

        private class GitHubReleaseAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; }
        }
    }
}
