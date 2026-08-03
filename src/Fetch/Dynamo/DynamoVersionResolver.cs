using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Fetch.Dynamo
{
    internal static class DynamoVersionResolver
    {
        /// <summary>
        /// Dynamo versions each Revit release may be running, newest first. Point releases ship
        /// newer Dynamo than the original release did, so each year lists every version seen in
        /// that line: Revit 2025 shipped 3.0 and reached 3.4 by 2025.4, and Revit 2026 shipped
        /// 3.4.1 and reached 3.5 by 2026.1.
        ///
        /// This is a fallback ranking, not a source of truth: what is actually installed is
        /// discovered from the filesystem first. Order only breaks ties between candidates that
        /// look equally real.
        /// </summary>
        private static readonly Dictionary<string, string[]> RevitDynamoVersionMap = new Dictionary<string, string[]>
        {
            { "2027", new[] { "4.1", "4.0" } },
            { "2026", new[] { "4.0", "3.6", "3.5", "3.4" } },
            { "2025", new[] { "3.4", "3.3", "3.2", "3.0" } }
        };

        /// <summary>Revit versions this map covers, used to warn about shared package folders.</summary>
        internal static IEnumerable<string> MappedRevitVersions => RevitDynamoVersionMap.Keys;

        internal static bool IsVersionSharedAcrossRevitVersions(Version dynamoVersion)
        {
            string wanted = $"{dynamoVersion.Major}.{dynamoVersion.Minor}";
            int count = 0;

            foreach (string[] versions in RevitDynamoVersionMap.Values)
            {
                foreach (string version in versions)
                {
                    if (version == wanted)
                    {
                        count++;
                        break;
                    }
                }
            }

            return count > 1;
        }

        internal static IEnumerable<Version> GetVersionCandidates(Assembly dynamoRevit, string revitVersion)
        {
            return GetVersionCandidates(dynamoRevit?.GetName().Version, revitVersion);
        }

        internal static IEnumerable<Version> GetVersionCandidates(Version selectorAssemblyVersion, string revitVersion)
        {
            var versions = new List<Version>();

            if (selectorAssemblyVersion != null)
            {
                versions.Add(new Version(selectorAssemblyVersion.Major, selectorAssemblyVersion.Minor));
            }

            if (RevitDynamoVersionMap.TryGetValue(revitVersion, out string[] mappedVersions))
            {
                versions.AddRange(mappedVersions.Select(Version.Parse));
            }

            return versions
                .GroupBy(v => $"{v.Major}.{v.Minor}")
                .Select(g => g.First());
        }

        internal static IEnumerable<string> GetPackagePathCandidates(
            Version dynamoVersion,
            string userRoaming,
            string revitVersion)
        {
            string versionFolder = $"{dynamoVersion.Major}.{dynamoVersion.Minor}";

            if (IsRevitSelectorVersion(dynamoVersion, revitVersion))
            {
                yield return Path.Combine(userRoaming, "Dynamo", "Dynamo Revit", versionFolder, "packages");
                yield break;
            }

            if (dynamoVersion.Major >= 4)
            {
                yield return Path.Combine(userRoaming, "Dynamo", "Dynamo Core", versionFolder, "packages");
                yield return Path.Combine(userRoaming, "Dynamo", "Dynamo Revit", versionFolder, "packages");
            }
            else
            {
                yield return Path.Combine(userRoaming, "Dynamo", "Dynamo Revit", versionFolder, "packages");
                yield return Path.Combine(userRoaming, "Dynamo", "Dynamo Core", versionFolder, "packages");
            }
        }

        internal static bool IsRevitSelectorVersion(Version selectorVersion, string revitVersion)
        {
            if (!int.TryParse(revitVersion, out int revitYear))
            {
                return false;
            }

            int shortRevitVersion = revitYear % 2000;
            return selectorVersion.Major == revitYear ||
                   selectorVersion.Major == shortRevitVersion;
        }
    }
}
