using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Fetch.Diagnostics;

namespace Fetch.Dynamo
{
    /// <summary>How sure we are that a candidate package folder is the one Dynamo actually reads.</summary>
    internal enum DynamoPathConfidence
    {
        /// <summary>Nothing on disk; the path is inferred from the version map.</summary>
        Guessed = 0,

        /// <summary>A packages folder exists, but it may be one FETCH created on an earlier run.</summary>
        PackagesFolderExists = 1,

        /// <summary>Dynamo itself has written to this version folder, so this version really runs here.</summary>
        DynamoProfileExists = 2
    }

    /// <summary>
    /// Chooses which Dynamo installation to sync into.
    ///
    /// Previously this lived in the Revit application class and published its answer by assigning
    /// statics, which made the selection policy impossible to exercise without Revit.
    /// </summary>
    internal static class DynamoInstallationResolver
    {
        internal const string SelectorAssemblyName = "DynamoRevitVersionSelector";

        /// <summary>Filesystem questions the resolver asks, separated so it can be tested.</summary>
        internal interface IDynamoProbe
        {
            bool DirectoryExists(string path);

            /// <summary>True when the folder contains files, as opposed to only subfolders.</summary>
            bool DirectoryHasFiles(string path);
        }

        private sealed class FileSystemProbe : IDynamoProbe
        {
            public bool DirectoryExists(string path)
            {
                try
                {
                    return Directory.Exists(path);
                }
                catch (Exception)
                {
                    return false;
                }
            }

            public bool DirectoryHasFiles(string path)
            {
                try
                {
                    return Directory.Exists(path) && Directory.EnumerateFiles(path).Any();
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>Finds the loaded Dynamo version selector, if Dynamo is already in this process.</summary>
        internal static Assembly FindLoadedSelectorAssembly()
        {
            try
            {
                return AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(assembly => assembly.FullName != null &&
                                                assembly.FullName.Contains(SelectorAssemblyName));
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static DynamoInstallation Resolve(Assembly selectorAssembly, string revitVersion, string userRoaming)
        {
            return Resolve(selectorAssembly?.GetName().Version, revitVersion, userRoaming, new FileSystemProbe());
        }

        /// <summary>
        /// Picks the candidate with the strongest evidence that Dynamo really uses it, rather than
        /// the first one that merely exists. That distinction matters because a folder FETCH itself
        /// created on an earlier run would otherwise outrank the folder Dynamo actually reads, and
        /// keep doing so on every subsequent startup.
        /// </summary>
        internal static DynamoInstallation Resolve(
            Version selectorAssemblyVersion,
            string revitVersion,
            string userRoaming,
            IDynamoProbe probe)
        {
            List<Version> candidates = DynamoVersionResolver
                .GetVersionCandidates(selectorAssemblyVersion, revitVersion)
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            Version bestVersion = null;
            string bestPath = null;
            DynamoPathConfidence bestConfidence = DynamoPathConfidence.Guessed;

            foreach (Version candidate in candidates)
            {
                foreach (string packagePath in DynamoVersionResolver.GetPackagePathCandidates(candidate, userRoaming, revitVersion))
                {
                    DynamoPathConfidence confidence = Assess(packagePath, probe);

                    // Strictly greater keeps the map's ordering as the tie-breaker.
                    if (bestPath == null || confidence > bestConfidence)
                    {
                        bestVersion = candidate;
                        bestPath = packagePath;
                        bestConfidence = confidence;
                    }

                    if (bestConfidence == DynamoPathConfidence.DynamoProfileExists)
                    {
                        return Build(bestVersion, bestPath, bestConfidence, revitVersion);
                    }
                }
            }

            return Build(bestVersion, bestPath, bestConfidence, revitVersion);
        }

        private static DynamoInstallation Build(
            Version version,
            string packagePath,
            DynamoPathConfidence confidence,
            string revitVersion)
        {
            if (confidence != DynamoPathConfidence.DynamoProfileExists &&
                DynamoVersionResolver.IsVersionSharedAcrossRevitVersions(version))
            {
                // Several Revit releases can run the same Dynamo version and therefore share one
                // packages folder; per-version deployment sources will overwrite each other there.
                Log.Warning(
                    $"Dynamo {version.Major}.{version.Minor} is used by more than one supported Revit release, so '{packagePath}' "
                    + $"may be shared with another Revit version. Packages synced for Revit {revitVersion} can be replaced by another version's sync.");
            }

            return new DynamoInstallation(version, packagePath, confidence);
        }

        private static DynamoPathConfidence Assess(string packagePath, IDynamoProbe probe)
        {
            // Dynamo writes its own settings into the version folder when it runs; FETCH only ever
            // creates the packages subfolder, so files here mean Dynamo genuinely uses this version.
            string versionFolder = TryGetParent(packagePath);

            if (versionFolder != null && probe.DirectoryHasFiles(versionFolder))
            {
                return DynamoPathConfidence.DynamoProfileExists;
            }

            return probe.DirectoryExists(packagePath)
                ? DynamoPathConfidence.PackagesFolderExists
                : DynamoPathConfidence.Guessed;
        }

        private static string TryGetParent(string path)
        {
            try
            {
                return Path.GetDirectoryName(path);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
