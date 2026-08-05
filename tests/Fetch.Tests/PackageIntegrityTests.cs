using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Fetch.Sources;
using Xunit;

namespace Fetch.Tests
{
    /// <summary>
    /// Covers the checks that stand between a downloaded file and the user's packages folder
    /// (audit findings F4 and R8).
    /// </summary>
    public class PackageIntegrityTests
    {
        private static string CreateZip(TempDirectory temp, string name = "packages.zip")
        {
            string path = Path.Combine(temp.Path, name);
            string contentRoot = temp.CreateDirectory("zip-content");
            File.WriteAllText(Path.Combine(contentRoot, "file.txt"), "content");
            ZipFile.CreateFromDirectory(contentRoot, path);
            return path;
        }

        [Fact]
        public void A_real_zip_passes_verification()
        {
            using (var temp = new TempDirectory())
            {
                string zip = CreateZip(temp);

                PackageIntegrity.VerifyDownloadedArchive(zip, null);
            }
        }

        [Fact]
        public void An_html_sign_in_page_is_reported_as_a_web_page()
        {
            using (var temp = new TempDirectory())
            {
                string path = Path.Combine(temp.Path, "packages.zip");
                File.WriteAllText(path, "<!DOCTYPE html><html><body>Sign in to continue</body></html>");

                var exception = Assert.Throws<InvalidDataException>(
                    () => PackageIntegrity.VerifyDownloadedArchive(path, null));

                Assert.Contains("web page", exception.Message);
                Assert.Contains("sign-in", exception.Message);
            }
        }

        [Fact]
        public void A_non_zip_binary_is_rejected()
        {
            using (var temp = new TempDirectory())
            {
                string path = Path.Combine(temp.Path, "packages.zip");
                File.WriteAllBytes(path, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 });

                var exception = Assert.Throws<InvalidDataException>(
                    () => PackageIntegrity.VerifyDownloadedArchive(path, null));

                Assert.Contains("did not return a zip file", exception.Message);
            }
        }

        [Fact]
        public void A_missing_download_is_reported_as_not_downloaded()
        {
            using (var temp = new TempDirectory())
            {
                Assert.Throws<FileNotFoundException>(
                    () => PackageIntegrity.VerifyDownloadedArchive(Path.Combine(temp.Path, "absent.zip"), null));
            }
        }

        [Fact]
        public void An_empty_file_is_rejected()
        {
            using (var temp = new TempDirectory())
            {
                string path = Path.Combine(temp.Path, "packages.zip");
                File.WriteAllBytes(path, new byte[0]);

                Assert.Throws<InvalidDataException>(() => PackageIntegrity.VerifyDownloadedArchive(path, null));
            }
        }

        [Fact]
        public void A_matching_sha256_passes()
        {
            using (var temp = new TempDirectory())
            {
                string zip = CreateZip(temp);
                string hash = PackageIntegrity.ComputeSha256(zip);

                PackageIntegrity.VerifyDownloadedArchive(zip, hash);
            }
        }

        [Fact]
        public void A_mismatched_sha256_is_rejected_with_both_hashes()
        {
            using (var temp = new TempDirectory())
            {
                string zip = CreateZip(temp);
                string wrong = new string('a', 64);

                var exception = Assert.Throws<InvalidDataException>(
                    () => PackageIntegrity.VerifyDownloadedArchive(zip, wrong));

                Assert.Contains("integrity check", exception.Message);
                Assert.Contains(wrong, exception.Message);
                Assert.Contains(PackageIntegrity.ComputeSha256(zip), exception.Message);
            }
        }

        [Fact]
        public void A_malformed_sha256_setting_is_reported_as_a_configuration_problem()
        {
            using (var temp = new TempDirectory())
            {
                string zip = CreateZip(temp);

                var exception = Assert.Throws<InvalidDataException>(
                    () => PackageIntegrity.VerifyDownloadedArchive(zip, "not-a-hash"));

                Assert.Contains("FetchSettings.ini", exception.Message);
            }
        }

        [Theory]
        [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789", "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789")]
        [InlineData("sha256:ABCDEF01", "abcdef01")]
        [InlineData("ab cd-ef:01", "abcdef01")]
        public void NormalizeHash_accepts_common_pasted_formats(string input, string expected)
        {
            Assert.Equal(expected, PackageIntegrity.NormalizeHash(input));
        }

        [Fact]
        public void A_sha256_is_matched_case_insensitively_and_with_separators()
        {
            using (var temp = new TempDirectory())
            {
                string zip = CreateZip(temp);
                string hash = PackageIntegrity.ComputeSha256(zip).ToUpperInvariant();

                PackageIntegrity.VerifyDownloadedArchive(zip, "sha256:" + hash);
            }
        }

        [Fact]
        public void An_empty_archive_is_accepted()
        {
            using (var temp = new TempDirectory())
            {
                string path = Path.Combine(temp.Path, "empty.zip");
                string emptyRoot = temp.CreateDirectory("empty-content");
                ZipFile.CreateFromDirectory(emptyRoot, path);

                PackageIntegrity.VerifyDownloadedArchive(path, null);
            }
        }

        [Theory]
        [InlineData("<!DOCTYPE html>", true)]
        [InlineData("  <html>", true)]
        [InlineData("<?xml version", true)]
        [InlineData("PK", false)]
        [InlineData("plain text", false)]
        public void LooksLikeMarkup_detects_pages(string content, bool expected)
        {
            byte[] header = Encoding.ASCII.GetBytes(content);
            byte[] first = new byte[Math.Min(4, header.Length)];
            Array.Copy(header, first, first.Length);

            Assert.Equal(expected, PackageIntegrity.LooksLikeMarkup(first));
        }
    }
}
