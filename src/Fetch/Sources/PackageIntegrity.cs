using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Fetch.Sources
{
    /// <summary>
    /// Checks a downloaded package archive before its contents are trusted.
    ///
    /// Two problems are covered: a server returning something that is not an archive at all
    /// (typically a sign-in or error page served with HTTP 200), and an archive whose contents are
    /// not the ones the deployment intended, which an optional SHA-256 in the settings detects.
    /// </summary>
    internal static class PackageIntegrity
    {
        private const int HeaderLength = 4;

        // Local file header, and the end-of-central-directory header used by an empty archive.
        private static readonly byte[][] ZipSignatures =
        {
            new byte[] { 0x50, 0x4B, 0x03, 0x04 },
            new byte[] { 0x50, 0x4B, 0x05, 0x06 }
        };

        internal static void VerifyDownloadedArchive(string filePath, string expectedSha256)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("The package zip was not downloaded.", filePath);
            }

            byte[] header = ReadHeader(filePath, HeaderLength);

            if (!HasZipSignature(header))
            {
                throw new InvalidDataException(
                    LooksLikeMarkup(header)
                        ? "The package source returned a web page instead of a zip file. The link is most likely a sharing page or requires sign-in; use a direct, publicly accessible download link."
                        : "The package source did not return a zip file.");
            }

            if (string.IsNullOrWhiteSpace(expectedSha256))
            {
                return;
            }

            string expected = NormalizeHash(expectedSha256);

            if (!IsValidSha256(expected))
            {
                throw new InvalidDataException(
                    "The Sha256 value in the [Settings] section of FetchSettings.ini is not a valid SHA-256 hash (expected 64 hexadecimal characters).");
            }

            string actual = ComputeSha256(filePath);

            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The downloaded packages failed their integrity check and were not installed. Expected SHA-256 {expected} but the download was {actual}.");
            }
        }

        internal static byte[] ReadHeader(string filePath, int count)
        {
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] buffer = new byte[count];
                int read = stream.Read(buffer, 0, count);

                if (read == count)
                {
                    return buffer;
                }

                byte[] partial = new byte[read < 0 ? 0 : read];
                Array.Copy(buffer, partial, partial.Length);
                return partial;
            }
        }

        internal static bool HasZipSignature(byte[] header)
        {
            if (header == null)
            {
                return false;
            }

            foreach (byte[] signature in ZipSignatures)
            {
                if (header.Length < signature.Length)
                {
                    continue;
                }

                bool matches = true;
                for (int i = 0; i < signature.Length; i++)
                {
                    if (header[i] != signature[i])
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when the response opens like HTML or XML, i.e. a page rather than an archive.</summary>
        internal static bool LooksLikeMarkup(byte[] header)
        {
            if (header == null || header.Length == 0)
            {
                return false;
            }

            int index = 0;
            while (index < header.Length && (header[index] == 0x20 || header[index] == 0x09 ||
                                             header[index] == 0x0D || header[index] == 0x0A ||
                                             header[index] == 0xEF || header[index] == 0xBB || header[index] == 0xBF))
            {
                index++;
            }

            return index < header.Length && header[index] == (byte)'<';
        }

        internal static string ComputeSha256(string filePath)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(hash.Length * 2);

                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        /// <summary>Accepts the common ways a hash gets pasted: spaces, dashes, colons, a sha256: prefix.</summary>
        internal static string NormalizeHash(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();

            if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring("sha256:".Length);
            }

            StringBuilder builder = new StringBuilder(trimmed.Length);

            foreach (char character in trimmed)
            {
                if (character == ' ' || character == '-' || character == ':' || character == '\t')
                {
                    continue;
                }

                builder.Append(char.ToLowerInvariant(character));
            }

            return builder.ToString();
        }

        internal static bool IsValidSha256(string normalizedHash)
        {
            if (string.IsNullOrEmpty(normalizedHash) || normalizedHash.Length != 64)
            {
                return false;
            }

            foreach (char character in normalizedHash)
            {
                bool isHex = (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
