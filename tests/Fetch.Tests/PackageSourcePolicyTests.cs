using System;
using Fetch.Sources;
using Xunit;

namespace Fetch.Tests
{
    /// <summary>
    /// A package zip becomes code running inside Revit, so the transport it arrives over is a
    /// security boundary (audit finding S1).
    /// </summary>
    public class PackageSourcePolicyTests
    {
        [Fact]
        public void Https_sources_are_accepted()
        {
            bool valid = PackageSourcePolicy.TryValidateUrl(
                "https://example.com/packages.zip", false, out Uri uri, out string error);

            Assert.True(valid);
            Assert.Null(error);
            Assert.Equal("https", uri.Scheme);
        }

        [Fact]
        public void Plain_http_sources_are_rejected_by_default()
        {
            bool valid = PackageSourcePolicy.TryValidateUrl(
                "http://example.com/packages.zip", false, out Uri uri, out string error);

            Assert.False(valid);
            Assert.Null(uri);
            Assert.Contains("https://", error);
            Assert.Contains("AllowInsecureHttp", error);
        }

        [Fact]
        public void Plain_http_sources_are_accepted_when_explicitly_opted_in()
        {
            bool valid = PackageSourcePolicy.TryValidateUrl(
                "http://example.com/packages.zip", true, out Uri uri, out string error);

            Assert.True(valid);
            Assert.Null(error);
            Assert.Equal("http", uri.Scheme);
        }

        [Theory]
        [InlineData("http://localhost:8080/packages.zip")]
        [InlineData("http://127.0.0.1/packages.zip")]
        public void Loopback_http_sources_are_accepted_without_opt_in(string address)
        {
            bool valid = PackageSourcePolicy.TryValidateUrl(address, false, out Uri uri, out string error);

            Assert.True(valid);
            Assert.Null(error);
            Assert.NotNull(uri);
        }

        [Theory]
        [InlineData("ftp://example.com/packages.zip")]
        [InlineData("file://server/share/packages.zip")]
        public void Other_schemes_are_rejected(string address)
        {
            bool valid = PackageSourcePolicy.TryValidateUrl(address, true, out Uri uri, out string error);

            Assert.False(valid);
            Assert.Null(uri);
            Assert.Contains("unsupported scheme", error, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void A_missing_source_reports_that_nothing_is_configured(string address)
        {
            bool valid = PackageSourcePolicy.TryValidateUrl(address, false, out Uri uri, out string error);

            Assert.False(valid);
            Assert.Null(uri);
            Assert.Contains("No package source is configured", error);
        }

        [Fact]
        public void A_value_that_is_neither_folder_nor_url_is_reported_clearly()
        {
            bool valid = PackageSourcePolicy.TryValidateUrl("not a url", false, out Uri uri, out string error);

            Assert.False(valid);
            Assert.Null(uri);
            Assert.Contains("neither an existing folder nor a valid URL", error);
        }
    }
}
