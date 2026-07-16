using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Fetch
{
    internal static class DynamoVersionResolver
    {
        private static readonly Dictionary<string, string[]> RevitDynamoVersionMap = new Dictionary<string, string[]>
        {
            { "2027", new[] { "27.0", "4.1", "4.0" } },
            { "2026", new[] { "4.0", "3.6", "3.5", "3.4" } },
            { "2025", new[] { "3.3", "3.2", "3.0" } }
        };

        internal static IEnumerable<Version> GetVersionCandidates(Assembly dynamoRevit, string revitVersion)
        {
            var versions = new List<Version>();

            if (dynamoRevit != null)
            {
                Version assemblyVersion = dynamoRevit.GetName().Version;
                versions.Add(new Version(assemblyVersion.Major, assemblyVersion.Minor));
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

        private static bool IsRevitSelectorVersion(Version selectorVersion, string revitVersion)
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
