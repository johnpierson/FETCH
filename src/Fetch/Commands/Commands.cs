using System;
using System.IO;
using System.IO.Compression;
using Autodesk.Revit.UI;
using Fetch.Classes;

namespace Fetch.Commands
{
    internal class Commands
    {
        public static void ReadIni()
        {
            var fetchIni = new FetchIniFile();
            Globals.PackageURL = fetchIni.Read("Path", "Settings");
            string graphRootPath = fetchIni.Read("GraphPath", "Settings");
            Globals.DynamoGraphRootPath = string.IsNullOrWhiteSpace(graphRootPath)
                ? Globals.DefaultDynamoGraphRootPath
                : graphRootPath;
        }

        /// <summary>
        /// Downloads a package zip and replaces the current Dynamo package folder with its contents.
        /// </summary>
        public static void DownloadAndUnzipPackages(Uri packageUri)
        {
            string downloadPath = Path.Combine(Globals.TempPath, $"fetch_packages_{Guid.NewGuid():N}.zip");
            string extractPath = Path.Combine(Globals.TempPath, $"fetch_packages_{Guid.NewGuid():N}");

            try
            {
                using (var fileDownloader = new FileDownloader())
                {
                    fileDownloader.DownloadFile(packageUri.AbsoluteUri, downloadPath);
                }

                ExtractPackages(downloadPath, extractPath);
                SyncFromSourceDirectory(extractPath);
                ShowNotification();
            }
            finally
            {
                DeleteTemporaryFile(downloadPath);
                DeleteTemporaryDirectory(extractPath);
            }
        }

        private static void ExtractPackages(string downloadPath, string extractPath)
        {
            if (!File.Exists(downloadPath))
            {
                throw new FileNotFoundException("The package zip was not downloaded.", downloadPath);
            }

            Directory.CreateDirectory(extractPath);
            ZipFile.ExtractToDirectory(downloadPath, extractPath);
        }

        private static void DeleteOldPackages()
        {
            Directory.CreateDirectory(Globals.DefaultDynamoPackagePath);
            DirectoryInfo di = new DirectoryInfo(Globals.DefaultDynamoPackagePath);

            foreach (FileInfo file in di.GetFiles())
            {
                file.Delete();
            }

            foreach (DirectoryInfo dir in di.GetDirectories())
            {
                dir.Delete(true);
            }
        }

        internal static void SyncPackagesFromLocalPath()
        {
            SyncFromSourceDirectory(Globals.PackageURL);
            ShowNotification();
        }

        private static void SyncFromSourceDirectory(string sourceDirectory)
        {
            SourceLayout sourceLayout = ResolveSourceLayout(sourceDirectory);

            if (!Directory.Exists(sourceLayout.PackageSourceDirectory))
            {
                throw new DirectoryNotFoundException($"Package source folder not found: {sourceLayout.PackageSourceDirectory}");
            }

            DeleteOldPackages();
            CopyDirectory(sourceLayout.PackageSourceDirectory, Globals.DefaultDynamoPackagePath);

            if (!string.IsNullOrWhiteSpace(sourceLayout.GraphSourceDirectory) &&
                Directory.Exists(sourceLayout.GraphSourceDirectory))
            {
                string graphDestination = Path.Combine(Globals.DynamoGraphRootPath, Globals.RevitVersion);
                ReplaceDirectory(sourceLayout.GraphSourceDirectory, graphDestination);
            }
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (string dirPath in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(GetDestinationPath(sourceDirectory, destinationDirectory, dirPath));
            }

            foreach (string newPath in Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(newPath, GetDestinationPath(sourceDirectory, destinationDirectory, newPath), true);
            }
        }

        private static void ReplaceDirectory(string sourceDirectory, string destinationDirectory)
        {
            if (Directory.Exists(destinationDirectory))
            {
                Directory.Delete(destinationDirectory, true);
            }

            CopyDirectory(sourceDirectory, destinationDirectory);
        }

        private static SourceLayout ResolveSourceLayout(string sourceDirectory)
        {
            string normalizedSourceDirectory = Path.GetFullPath(sourceDirectory);
            string layoutRoot = NormalizeExtractedRoot(normalizedSourceDirectory);
            string versionDirectory = FindVersionDirectory(layoutRoot);

            if (versionDirectory == null)
            {
                return new SourceLayout
                {
                    PackageSourceDirectory = layoutRoot,
                    GraphSourceDirectory = FindNamedChildDirectory(layoutRoot, "Dynamo Graphs", "DynamoGraphs", "Graphs")
                };
            }

            string packageDirectory = FindNamedChildDirectory(versionDirectory, "Packages", "Package");
            string graphDirectory = FindNamedChildDirectory(versionDirectory, "Dynamo Graphs", "DynamoGraphs", "Graphs");

            return new SourceLayout
            {
                PackageSourceDirectory = packageDirectory,
                GraphSourceDirectory = graphDirectory
            };
        }

        private static string NormalizeExtractedRoot(string sourceDirectory)
        {
            string versionDirectory = FindVersionDirectory(sourceDirectory);
            if (versionDirectory != null || ContainsExpectedSourceFolder(sourceDirectory))
            {
                return sourceDirectory;
            }

            string[] childDirectories = Directory.GetDirectories(sourceDirectory);
            if (childDirectories.Length == 1)
            {
                string childDirectory = childDirectories[0];
                if (FindVersionDirectory(childDirectory) != null || ContainsExpectedSourceFolder(childDirectory))
                {
                    return childDirectory;
                }
            }

            return sourceDirectory;
        }

        private static bool ContainsExpectedSourceFolder(string sourceDirectory)
        {
            return FindNamedChildDirectory(sourceDirectory, "Packages", "Package", "Dynamo Graphs", "DynamoGraphs", "Graphs") != null;
        }

        private static string FindVersionDirectory(string sourceDirectory)
        {
            if (IsVersionDirectoryName(new DirectoryInfo(sourceDirectory).Name))
            {
                return sourceDirectory;
            }

            foreach (string childDirectory in Directory.GetDirectories(sourceDirectory))
            {
                if (IsVersionDirectoryName(new DirectoryInfo(childDirectory).Name))
                {
                    return childDirectory;
                }
            }

            return null;
        }

        private static bool IsVersionDirectoryName(string directoryName)
        {
            string normalizedName = directoryName.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
            string revitVersion = Globals.RevitVersion;
            string shortVersion = revitVersion.Length == 4 ? revitVersion.Substring(2) : revitVersion;

            return normalizedName == revitVersion ||
                   normalizedName == $"revit{revitVersion}" ||
                   normalizedName == $"r{shortVersion}";
        }

        private static string FindNamedChildDirectory(string sourceDirectory, params string[] directoryNames)
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

        private static string GetDestinationPath(string sourceDirectory, string destinationDirectory, string sourcePath)
        {
            string sourceRoot = Path.GetFullPath(sourceDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullSourcePath = Path.GetFullPath(sourcePath);
            string relativePath = fullSourcePath.Substring(sourceRoot.Length);
            return Path.Combine(destinationDirectory, relativePath);
        }

        private static void DeleteTemporaryFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void DeleteTemporaryDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        internal static void ShowNotification()
        {
            TaskDialog.Show("Fetch", $"Current packages fetched for Dynamo {Globals.DynamoVersion}");
        }

        private class SourceLayout
        {
            public string PackageSourceDirectory { get; set; }
            public string GraphSourceDirectory { get; set; }
        }
    }
}
