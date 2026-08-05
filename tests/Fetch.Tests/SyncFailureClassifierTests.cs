using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using Fetch.Sync;
using Xunit;

namespace Fetch.Tests
{
    /// <summary>
    /// Being offline is routine and must stay quiet; a broken configuration must not (audit
    /// findings R5 and R6).
    /// </summary>
    public class SyncFailureClassifierTests
    {
        [Theory]
        [InlineData(SocketError.HostNotFound)]
        [InlineData(SocketError.NetworkUnreachable)]
        [InlineData(SocketError.HostUnreachable)]
        [InlineData(SocketError.NetworkDown)]
        [InlineData(SocketError.ConnectionRefused)]
        [InlineData(SocketError.TimedOut)]
        public void Unreachable_network_errors_are_treated_as_offline(SocketError error)
        {
            var exception = new HttpRequestException("failed", new SocketException((int)error));

            Assert.True(SyncFailureClassifier.IsLikelyOffline(exception));
        }

        [Fact]
        public void A_bare_http_request_exception_is_treated_as_offline()
        {
            Assert.True(SyncFailureClassifier.IsLikelyOffline(new HttpRequestException("name resolution failed")));
        }

        [Fact]
        public void A_nested_socket_error_is_found_through_inner_exceptions()
        {
            var exception = new InvalidOperationException(
                "outer",
                new HttpRequestException("inner", new SocketException((int)SocketError.HostNotFound)));

            Assert.True(SyncFailureClassifier.IsLikelyOffline(exception));
        }

        [Fact]
        public void A_failed_integrity_check_is_not_offline()
        {
            Assert.False(SyncFailureClassifier.IsLikelyOffline(
                new InvalidDataException("The downloaded packages failed their integrity check.")));
        }

        [Fact]
        public void A_missing_package_folder_is_not_offline()
        {
            Assert.False(SyncFailureClassifier.IsLikelyOffline(
                new DirectoryNotFoundException("Package source folder not found: X")));
        }

        [Fact]
        public void A_stalled_download_is_not_offline()
        {
            Assert.False(SyncFailureClassifier.IsLikelyOffline(
                new TimeoutException("The package download stopped making progress.")));
        }

        [Fact]
        public void A_connection_reset_is_not_treated_as_offline()
        {
            // The host answered, so this is a transfer problem worth telling the user about.
            var exception = new HttpRequestException("reset", new SocketException((int)SocketError.ConnectionReset));

            Assert.False(SyncFailureClassifier.IsLikelyOffline(exception));
        }

        [Fact]
        public void A_null_exception_is_not_offline()
        {
            Assert.False(SyncFailureClassifier.IsLikelyOffline(null));
        }
    }
}
