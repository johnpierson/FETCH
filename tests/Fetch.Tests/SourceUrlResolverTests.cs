using System;
using Fetch.Sources;
using Xunit;

namespace Fetch.Tests
{
    /// <summary>
    /// Characterization tests for the package-source URL rewriting rules.
    /// Tests marked BASELINE document behavior the audit flagged as stale or lenient.
    /// </summary>
    public class SourceUrlResolverTests
    {
        [Theory]
        [InlineData("https://drive.google.com/file/d/ABC/view", true)]
        [InlineData("https://DRIVE.GOOGLE.COM/open?id=ABC", true)]
        [InlineData("https://example.com/packages.zip", false)]
        [InlineData("not a url", false)]
        public void IsGoogleDriveAddress_matches_only_drive_hosts(string address, bool expected)
        {
            Assert.Equal(expected, SourceUrlResolver.IsGoogleDriveAddress(address));
        }

        [Theory]
        [InlineData("https://onedrive.live.com/?id=ABC", true)]
        [InlineData("https://1drv.ms/u/s!ABC", true)]
        [InlineData("https://contoso.sharepoint.com/:u:/g/ABC", true)]
        [InlineData("https://example.com/packages.zip", false)]
        public void IsOneDriveAddress_matches_onedrive_and_sharepoint_hosts(string address, bool expected)
        {
            Assert.Equal(expected, SourceUrlResolver.IsOneDriveAddress(address));
        }

        [Theory]
        [InlineData("https://github.com/org/repo/releases/latest", true)]
        [InlineData("https://github.com/org/repo/releases/tag/v1.0.0", true)]
        [InlineData("https://github.com/org/repo/releases/download/v1.0.0/packages.zip", true)]
        [InlineData("https://github.com/org/repo", false)]
        [InlineData("https://gitlab.com/org/repo/releases/latest", false)]
        public void IsGitHubReleaseAddress_requires_a_releases_path(string address, bool expected)
        {
            Assert.Equal(expected, SourceUrlResolver.IsGitHubReleaseAddress(address));
        }

        [Fact]
        public void GetGoogleDriveDownloadAddress_rewrites_an_open_id_link()
        {
            string result = SourceUrlResolver.GetGoogleDriveDownloadAddress("https://drive.google.com/open?id=FILEID");

            Assert.Equal("https://drive.google.com/uc?id=FILEID&export=download&confirm=t", result);
        }

        [Fact]
        public void GetGoogleDriveDownloadAddress_rewrites_a_file_d_link()
        {
            string result = SourceUrlResolver.GetGoogleDriveDownloadAddress("https://drive.google.com/file/d/FILEID/view?usp=sharing");

            Assert.Equal("https://drive.google.com/uc?id=FILEID&export=download&confirm=t", result);
        }

        [Fact]
        public void GetGoogleDriveDownloadAddress_preserves_a_resource_key()
        {
            string result = SourceUrlResolver.GetGoogleDriveDownloadAddress("https://drive.google.com/open?id=FILEID&resourcekey=KEY");

            Assert.Equal("https://drive.google.com/uc?id=FILEID&export=download&resourcekey=KEY&confirm=t", result);
        }

        [Fact]
        public void GetGoogleDriveDownloadAddress_returns_empty_for_an_unsupported_link()
        {
            Assert.Equal(string.Empty, SourceUrlResolver.GetGoogleDriveDownloadAddress("https://drive.google.com/drive/folders/ABC"));
        }

        // BASELINE (audit finding R8): only the legacy href="/uc?" confirmation markup is recognized.
        // Google now serves a form posting to drive.usercontent.google.com, which this misses.
        [Fact]
        public void BASELINE_GetDriveConfirmationAddress_reads_only_the_legacy_uc_link()
        {
            string legacy = "<html><a href=\"/uc?export=download&amp;id=FILEID&amp;confirm=abc\">Download anyway</a></html>";

            Assert.Equal(
                "https://drive.google.com/uc?export=download&id=FILEID&confirm=abc",
                SourceUrlResolver.GetDriveConfirmationAddress(legacy));
        }

        [Fact]
        public void BASELINE_GetDriveConfirmationAddress_returns_null_for_the_modern_form_page()
        {
            string modern = "<html><form id=\"download-form\" action=\"https://drive.usercontent.google.com/download\">"
                            + "<input name=\"id\" value=\"FILEID\"></form></html>";

            Assert.Null(SourceUrlResolver.GetDriveConfirmationAddress(modern));
        }

        [Fact]
        public void GetOneDriveDownloadAddress_rewrites_a_personal_onedrive_link()
        {
            string result = SourceUrlResolver.GetOneDriveDownloadAddress(
                "https://onedrive.live.com/?cid=ABC&id=DEF",
                _ => throw new InvalidOperationException("no redirect expected"));

            Assert.Equal("https://onedrive.live.com/download?cid=ABC&id=DEF", result);
        }

        [Fact]
        public void GetOneDriveDownloadAddress_adds_the_download_flag_for_sharepoint()
        {
            string result = SourceUrlResolver.GetOneDriveDownloadAddress(
                "https://contoso.sharepoint.com/:u:/g/personal/file?e=abc",
                _ => throw new InvalidOperationException("no redirect expected"));

            Assert.Contains("download=1", result);
            Assert.Contains("e=abc", result);
        }

        [Fact]
        public void GetOneDriveDownloadAddress_replaces_an_existing_download_flag()
        {
            string result = SourceUrlResolver.GetOneDriveDownloadAddress(
                "https://contoso.sharepoint.com/file?download=0",
                _ => throw new InvalidOperationException("no redirect expected"));

            Assert.Contains("download=1", result);
            Assert.DoesNotContain("download=0", result);
        }

        [Fact]
        public void GetOneDriveDownloadAddress_follows_a_short_link_redirect()
        {
            string result = SourceUrlResolver.GetOneDriveDownloadAddress(
                "https://1drv.ms/u/s!ABC",
                _ => "https://contoso.sharepoint.com/file?e=abc");

            Assert.Contains("download=1", result);
        }

        [Fact]
        public void GetOneDriveDownloadAddress_returns_empty_when_the_short_link_does_not_redirect()
        {
            const string address = "https://1drv.ms/u/s!ABC";

            Assert.Equal(string.Empty, SourceUrlResolver.GetOneDriveDownloadAddress(address, _ => address));
        }

        [Fact]
        public void ClassifyGitHubReleaseAddress_maps_latest_to_the_api()
        {
            var kind = SourceUrlResolver.ClassifyGitHubReleaseAddress(
                "https://github.com/org/repo/releases/latest", out string apiAddress);

            Assert.Equal(GitHubReleaseAddressKind.ApiLookup, kind);
            Assert.Equal("https://api.github.com/repos/org/repo/releases/latest", apiAddress);
        }

        [Fact]
        public void ClassifyGitHubReleaseAddress_maps_a_tag_to_the_api()
        {
            var kind = SourceUrlResolver.ClassifyGitHubReleaseAddress(
                "https://github.com/org/repo/releases/tag/v1.0.0", out string apiAddress);

            Assert.Equal(GitHubReleaseAddressKind.ApiLookup, kind);
            Assert.Equal("https://api.github.com/repos/org/repo/releases/tags/v1.0.0", apiAddress);
        }

        [Fact]
        public void ClassifyGitHubReleaseAddress_treats_an_asset_url_as_a_direct_download()
        {
            var kind = SourceUrlResolver.ClassifyGitHubReleaseAddress(
                "https://github.com/org/repo/releases/download/v1.0.0/packages.zip", out string apiAddress);

            Assert.Equal(GitHubReleaseAddressKind.DirectAsset, kind);
            Assert.Null(apiAddress);
        }

        [Theory]
        [InlineData("https://github.com/org/repo")]
        [InlineData("https://github.com/org/repo/releases/something")]
        [InlineData("not a url")]
        public void ClassifyGitHubReleaseAddress_rejects_unsupported_addresses(string address)
        {
            var kind = SourceUrlResolver.ClassifyGitHubReleaseAddress(address, out string apiAddress);

            Assert.Equal(GitHubReleaseAddressKind.Unsupported, kind);
            Assert.Null(apiAddress);
        }

        [Fact]
        public void SelectReleaseAssetUrl_picks_the_first_zip_asset()
        {
            const string json = @"{""assets"":[
                {""name"":""notes.txt"",""browser_download_url"":""https://example.com/notes.txt""},
                {""name"":""packages.zip"",""browser_download_url"":""https://example.com/packages.zip""},
                {""name"":""other.zip"",""browser_download_url"":""https://example.com/other.zip""}]}";

            Assert.Equal("https://example.com/packages.zip", SourceUrlResolver.SelectReleaseAssetUrl(json));
        }

        [Fact]
        public void SelectReleaseAssetUrl_throws_when_no_zip_asset_exists()
        {
            const string json = @"{""assets"":[{""name"":""notes.txt"",""browser_download_url"":""https://example.com/notes.txt""}]}";

            Assert.Throws<InvalidOperationException>(() => SourceUrlResolver.SelectReleaseAssetUrl(json));
        }

        [Fact]
        public void SelectReleaseAssetUrl_throws_when_the_release_has_no_assets()
        {
            Assert.Throws<InvalidOperationException>(() => SourceUrlResolver.SelectReleaseAssetUrl(@"{""assets"":[]}"));
        }
    }
}
