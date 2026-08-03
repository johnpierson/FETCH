using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fetch.Sources
{
    internal enum GitHubReleaseAddressKind
    {
        /// <summary>Not a supported GitHub release page or asset URL.</summary>
        Unsupported,

        /// <summary>Already a direct release asset URL; download it as-is.</summary>
        DirectAsset,

        /// <summary>A release page; the asset must be resolved through the GitHub API.</summary>
        ApiLookup
    }

    /// <summary>
    /// Pure URL classification and rewriting for the supported package sources.
    /// Contains no I/O: callers inject redirect resolution and release-metadata fetching.
    /// </summary>
    internal static class SourceUrlResolver
    {
        internal const string GoogleDriveDomain = "drive.google.com";
        internal const string OneDriveDomain = "onedrive.live.com";
        internal const string OneDriveShortDomain = "1drv.ms";
        internal const string SharePointDomainSuffix = ".sharepoint.com";
        internal const string GitHubDomain = "github.com";
        internal const string GitHubApiBaseAddress = "https://api.github.com/repos/";

        internal static bool IsGoogleDriveAddress(string address)
        {
            Uri uri;
            return Uri.TryCreate(address, UriKind.Absolute, out uri) &&
                   string.Equals(uri.Host, GoogleDriveDomain, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsOneDriveAddress(string address)
        {
            Uri uri;
            return Uri.TryCreate(address, UriKind.Absolute, out uri) &&
                   (string.Equals(uri.Host, OneDriveDomain, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Host, OneDriveShortDomain, StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.EndsWith(SharePointDomainSuffix, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsGitHubReleaseAddress(string address)
        {
            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri) ||
                !string.Equals(uri.Host, GitHubDomain, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string[] pathParts = GetPathParts(uri);
            return pathParts.Length >= 4 &&
                   string.Equals(pathParts[2], "releases", StringComparison.OrdinalIgnoreCase);
        }

        // Handles the following formats:
        // - drive.google.com/open?id=FILEID&resourcekey=RESOURCEKEY
        // - drive.google.com/file/d/FILEID/view?usp=sharing&resourcekey=RESOURCEKEY
        // - drive.google.com/uc?id=FILEID&export=download&resourcekey=RESOURCEKEY
        internal static string GetGoogleDriveDownloadAddress(string address)
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

        /// <summary>
        /// Extracts the Google Drive virus-scan confirmation link from a returned HTML page.
        /// Returns null when the page carries no legacy confirmation link.
        /// </summary>
        internal static string GetDriveConfirmationAddress(string content)
        {
            int linkIndex = content.LastIndexOf("href=\"/uc?", StringComparison.OrdinalIgnoreCase);
            if (linkIndex >= 0)
            {
                linkIndex += 6;
                int linkEnd = content.IndexOf('"', linkIndex);
                if (linkEnd >= 0)
                {
                    return "https://drive.google.com" + content.Substring(linkIndex, linkEnd - linkIndex).Replace("&amp;", "&");
                }
            }

            return null;
        }

        /// <summary>
        /// Rewrites a OneDrive/SharePoint share link into a direct download link.
        /// <paramref name="resolveRedirect"/> follows a single 1drv.ms redirect hop.
        /// </summary>
        internal static string GetOneDriveDownloadAddress(string address, Func<string, string> resolveRedirect)
        {
            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri))
                return string.Empty;

            if (string.Equals(uri.Host, OneDriveShortDomain, StringComparison.OrdinalIgnoreCase))
            {
                string resolvedAddress = resolveRedirect(address);
                if (string.Equals(address, resolvedAddress, StringComparison.OrdinalIgnoreCase))
                    return string.Empty;

                return GetOneDriveDownloadAddress(resolvedAddress, resolveRedirect);
            }

            if (string.Equals(uri.Host, OneDriveDomain, StringComparison.OrdinalIgnoreCase))
                return GetPersonalOneDriveDownloadAddress(uri);

            if (uri.Host.EndsWith(SharePointDomainSuffix, StringComparison.OrdinalIgnoreCase))
                return AddOrReplaceQueryParameter(uri, "download", "1");

            return string.Empty;
        }

        internal static string GetPersonalOneDriveDownloadAddress(Uri uri)
        {
            string query = uri.Query.TrimStart('?');
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            return string.Concat(uri.Scheme, "://", uri.Host, "/download?", query);
        }

        internal static string AddOrReplaceQueryParameter(Uri uri, string key, string value)
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

        /// <summary>
        /// Classifies a GitHub releases URL and, for release pages, produces the API address
        /// whose response should be handed to <see cref="SelectReleaseAssetUrl"/>.
        /// </summary>
        internal static GitHubReleaseAddressKind ClassifyGitHubReleaseAddress(string address, out string apiAddress)
        {
            apiAddress = null;

            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri))
                return GitHubReleaseAddressKind.Unsupported;

            string[] pathParts = GetPathParts(uri);
            if (pathParts.Length < 4 ||
                !string.Equals(pathParts[2], "releases", StringComparison.OrdinalIgnoreCase))
            {
                return GitHubReleaseAddressKind.Unsupported;
            }

            string releaseEndpoint;
            if (string.Equals(pathParts[3], "download", StringComparison.OrdinalIgnoreCase))
            {
                return GitHubReleaseAddressKind.DirectAsset;
            }
            else if (string.Equals(pathParts[3], "latest", StringComparison.OrdinalIgnoreCase))
            {
                releaseEndpoint = "latest";
            }
            else if (pathParts.Length >= 5 &&
                     string.Equals(pathParts[3], "tag", StringComparison.OrdinalIgnoreCase))
            {
                string tagName = Uri.EscapeDataString(Uri.UnescapeDataString(pathParts[4]));
                releaseEndpoint = $"tags/{tagName}";
            }
            else
            {
                return GitHubReleaseAddressKind.Unsupported;
            }

            apiAddress = string.Concat(
                GitHubApiBaseAddress,
                Uri.EscapeDataString(pathParts[0]),
                "/",
                Uri.EscapeDataString(pathParts[1]),
                "/releases/",
                releaseEndpoint);

            return GitHubReleaseAddressKind.ApiLookup;
        }

        /// <summary>
        /// Picks the first .zip asset from a GitHub release API response.
        /// </summary>
        internal static string SelectReleaseAssetUrl(string releaseJson)
        {
            GitHubRelease release = JsonSerializer.Deserialize<GitHubRelease>(releaseJson);
            GitHubReleaseAsset asset = SelectGitHubReleaseAsset(release);

            if (asset == null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
                throw new InvalidOperationException("The GitHub release does not contain a downloadable .zip asset.");

            return asset.BrowserDownloadUrl;
        }

        internal static GitHubReleaseAsset SelectGitHubReleaseAsset(GitHubRelease release)
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

        internal static string[] GetPathParts(Uri uri)
        {
            return uri.AbsolutePath.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        }

        internal class GitHubRelease
        {
            [JsonPropertyName("assets")]
            public GitHubReleaseAsset[] Assets { get; set; }
        }

        internal class GitHubReleaseAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; }
        }
    }
}
