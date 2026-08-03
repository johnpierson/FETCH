using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Fetch;
using Xunit;

namespace Fetch.Tests
{
    /// <summary>
    /// Exercises the download loop against a loopback server. Audit finding R2: a stalled transfer
    /// used to have no bound at all, because HttpClient.Timeout does not cover the response body
    /// when the request is issued with ResponseHeadersRead.
    /// </summary>
    public class FileDownloaderTests
    {
        [Fact]
        public void A_download_writes_the_response_body_to_disk()
        {
            byte[] payload = Encoding.ASCII.GetBytes("PK pretend this is a zip");

            using (var server = StubHttpServer.ServingBytes(payload))
            using (var temp = new TempDirectory())
            using (var downloader = new FileDownloader())
            {
                string target = Path.Combine(temp.Path, "packages.zip");

                downloader.DownloadFile(server.BaseAddress + "packages.zip", target);

                Assert.Equal(payload, File.ReadAllBytes(target));
            }
        }

        [Fact]
        public void A_stalled_download_is_abandoned_once_it_stops_making_progress()
        {
            using (var server = StubHttpServer.Stalling())
            using (var temp = new TempDirectory())
            using (var downloader = new FileDownloader())
            {
                downloader.IdleTimeout = TimeSpan.FromSeconds(1);
                string target = Path.Combine(temp.Path, "packages.zip");

                var stopwatch = Stopwatch.StartNew();
                var exception = Assert.Throws<TimeoutException>(
                    () => downloader.DownloadFile(server.BaseAddress + "packages.zip", target));
                stopwatch.Stop();

                Assert.Contains("stopped making progress", exception.Message);

                // The point of the fix: it gives up promptly instead of hanging indefinitely.
                Assert.True(
                    stopwatch.Elapsed < TimeSpan.FromSeconds(30),
                    $"Expected the stalled download to be abandoned quickly, took {stopwatch.Elapsed}.");
            }
        }

        [Fact]
        public void A_download_from_a_closed_port_reports_a_network_failure()
        {
            string address;

            using (var server = StubHttpServer.ServingBytes(new byte[] { 1, 2, 3 }))
            {
                // Capture a port that is guaranteed to be closed once the server is disposed.
                address = server.BaseAddress + "packages.zip";
            }

            using (var temp = new TempDirectory())
            using (var downloader = new FileDownloader())
            {
                downloader.IdleTimeout = TimeSpan.FromSeconds(5);
                string target = Path.Combine(temp.Path, "packages.zip");

                Exception exception = Record.Exception(() => downloader.DownloadFile(address, target));

                Assert.NotNull(exception);
                Assert.True(
                    Fetch.Sync.SyncFailureClassifier.IsLikelyOffline(exception) || exception is TimeoutException,
                    $"A refused connection should read as unreachable, got: {exception}");
            }
        }
    }
}
