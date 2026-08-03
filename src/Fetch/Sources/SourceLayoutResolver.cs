using System;
using System.IO;

namespace Fetch.Sources
{
    /// <summary>
    /// Describes the package and graph folders located inside a deployment source.
    /// </summary>
    internal class SourceLayout
    {
        public string PackageSourceDirectory { get; set; }
        public string GraphSourceDirectory { get; set; }

        /// <summary>
        /// True when the package folder was identified positively (a version folder's Packages
        /// child, or a Packages child at the root). False when the resolver fell back to treating
        /// the source root itself as the package folder, which is a guess and must be validated
        /// before anything is replaced.
        /// </summary>
        public bool PackageSourceIsExplicit { get; set; }
    }

    /// <summary>
    /// Resolves the package and graph folders inside a deployment source folder or extracted zip.
    /// Filesystem reads only; no mutation, no Revit dependency.
    /// </summary>
    internal static class SourceLayoutResolver
    {
        internal static SourceLayout Resolve(string sourceDirectory, string revitVersion)
        {
            string normalizedSourceDirectory = Path.GetFullPath(sourceDirectory);
            string layoutRoot = NormalizeExtractedRoot(normalizedSourceDirectory, revitVersion);
            string versionDirectory = FindVersionDirectory(layoutRoot, revitVersion);

            if (versionDirectory != null)
            {
                return new SourceLayout
                {
                    PackageSourceDirectory = FindNamedChildDirectory(versionDirectory, "Packages", "Package"),
                    GraphSourceDirectory = FindNamedChildDirectory(versionDirectory, "Dynamo Graphs", "DynamoGraphs", "Graphs"),
                    PackageSourceIsExplicit = true
                };
            }

            if (ContainsVersionDirectory(layoutRoot))
            {
                throw new DirectoryNotFoundException(
                    $"The deployment does not contain a folder for Revit {revitVersion}: {layoutRoot}");
            }

            string packageDirectory = FindNamedChildDirectory(layoutRoot, "Packages", "Package");

            return new SourceLayout
            {
                // Preserve support for older sources that point directly at a package folder.
                PackageSourceDirectory = packageDirectory ?? layoutRoot,
                GraphSourceDirectory = FindNamedChildDirectory(layoutRoot, "Dynamo Graphs", "DynamoGraphs", "Graphs"),
                PackageSourceIsExplicit = packageDirectory != null
            };
        }

        /// <summary>
        /// Maximum number of single-child wrapper folders to descend through. Archives are commonly
        /// re-zipped one or more times, nesting the real deployment under wrapper folders.
        /// </summary>
        internal const int MaxWrapperDepth = 8;

        internal static string NormalizeExtractedRoot(string sourceDirectory, string revitVersion)
        {
            string current = sourceDirectory;

            for (int depth = 0; depth <= MaxWrapperDepth; depth++)
            {
                if (FindVersionDirectory(current, revitVersion) != null ||
                    ContainsVersionDirectory(current) ||
                    ContainsExpectedSourceFolder(current))
                {
                    return current;
                }

                string[] childDirectories = Directory.GetDirectories(current);
                if (childDirectories.Length != 1)
                {
                    break;
                }

                current = childDirectories[0];
            }

            return sourceDirectory;
        }

        internal static bool ContainsExpectedSourceFolder(string sourceDirectory)
        {
            return FindNamedChildDirectory(sourceDirectory, "Packages", "Package", "Dynamo Graphs", "DynamoGraphs", "Graphs") != null;
        }

        internal static string FindVersionDirectory(string sourceDirectory, string revitVersion)
        {
            if (IsVersionDirectoryName(new DirectoryInfo(sourceDirectory).Name, revitVersion))
            {
                return sourceDirectory;
            }

            foreach (string childDirectory in Directory.GetDirectories(sourceDirectory))
            {
                if (IsVersionDirectoryName(new DirectoryInfo(childDirectory).Name, revitVersion))
                {
                    return childDirectory;
                }
            }

            return null;
        }

        internal static bool ContainsVersionDirectory(string sourceDirectory)
        {
            foreach (string childDirectory in Directory.GetDirectories(sourceDirectory))
            {
                if (IsAnyRevitVersionDirectoryName(new DirectoryInfo(childDirectory).Name))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsVersionDirectoryName(string directoryName, string revitVersion)
        {
            string normalizedName = directoryName.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
            string shortVersion = revitVersion.Length == 4 ? revitVersion.Substring(2) : revitVersion;

            return normalizedName == revitVersion ||
                   normalizedName == $"revit{revitVersion}" ||
                   normalizedName == $"r{shortVersion}";
        }

        internal static bool IsAnyRevitVersionDirectoryName(string directoryName)
        {
            string normalizedName = directoryName.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
            if (normalizedName.StartsWith("revit", StringComparison.Ordinal))
            {
                normalizedName = normalizedName.Substring(5);
            }
            else if (normalizedName.StartsWith("r", StringComparison.Ordinal))
            {
                normalizedName = normalizedName.Substring(1);
            }

            if (!int.TryParse(normalizedName, out int version))
            {
                return false;
            }

            return (version >= 2000 && version <= 2099) ||
                   (version >= 0 && version <= 99);
        }

        internal static string FindNamedChildDirectory(string sourceDirectory, params string[] directoryNames)
        {
            foreach (string childDirectory in Directory.GetDirectories(sourceDirectory))
            {
                string childName = new DirectoryInfo(childDirectory).Name;
                foreach (string directoryName in directoryNames)
                {
                    if (string.Equals(childName, directoryName, StringComparison.OrdinalIgnoreCase))
                    {
                        return childDirectory;
                    }
                }
            }

            return null;
        }
    }
}
