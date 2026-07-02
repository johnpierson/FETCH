using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.UI;

namespace Fetch
{
    internal class App : IExternalApplication
    {
        private static readonly Dictionary<string, string[]> RevitDynamoVersionMap = new Dictionary<string, string[]>
        {
            { "2027", new[] { "4.1", "4.0" } },
            { "2026", new[] { "3.6", "3.5", "3.4" } },
            { "2025", new[] { "3.3", "3.2", "3.0" } }
        };

        public Result OnStartup(UIControlledApplication application)
        {
            //store revit version 
            Globals.RevitVersion = application.ControlledApplication.VersionNumber;

            //try to find the dynamo version and see if it is running already in a revit session with the same version
            if (!FindDynamoVersions())
            {
                return Result.Failed;
            }

            VerifyDynamoLoaded();

            //if the URL is local, then we do a copy
            Commands.Commands.ReadIni();
            if (Directory.Exists(Globals.PackageURL))
            {
                try
                {
                    Commands.Commands.SyncPackagesFromLocalPath();
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Fetch", $"Unable to sync Dynamo packages.\n\n{ex.Message}");
                    return Result.Failed;
                }
            }

            if (!Uri.TryCreate(Globals.PackageURL, UriKind.Absolute, out Uri packageUri) ||
                (packageUri.Scheme != Uri.UriSchemeHttp && packageUri.Scheme != Uri.UriSchemeHttps))
            {
                TaskDialog.Show(
                    "Fetch",
                    $"FetchSettings.ini must point to an existing local folder or a supported HTTP/HTTPS package source URL.\n\nSettings file:\n{Globals.SettingsPath}\n\nPath value:\n{Globals.PackageURL}");
                return Result.Cancelled;
            }

            if (!Utilities.Utilities.CheckForInternetConnection())
            {
                return Result.Cancelled;
            }

            try
            {
                Commands.Commands.DownloadAndUnzipPackages(packageUri);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Fetch", $"Unable to download Dynamo packages.\n\n{ex.Message}");
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        public bool FindDynamoVersions()
        {
            var dynamoRevit = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.FullName.Contains("DynamoRevitVersionSelector"));

            List<Version> dynamoVersions = GetDynamoVersionCandidates(dynamoRevit).ToList();
            if (dynamoVersions.Count == 0)
            {
                return false;
            }

            foreach (Version dynamoVersion in dynamoVersions)
            {
                foreach (string packagePath in GetDynamoPackagePathCandidates(dynamoVersion))
                {
                    if (!Directory.Exists(packagePath))
                    {
                        continue;
                    }

                    Globals.DynamoVersion = dynamoVersion;
                    Globals.DefaultDynamoPackagePath = packagePath;
                    return true;
                }
            }

            Globals.DynamoVersion = dynamoVersions[0];
            Globals.DefaultDynamoPackagePath = GetDynamoPackagePathCandidates(Globals.DynamoVersion).First();

            return true;
        }

        private static IEnumerable<Version> GetDynamoVersionCandidates(System.Reflection.Assembly dynamoRevit)
        {
            var versions = new List<Version>();

            if (dynamoRevit != null)
            {
                Version assemblyVersion = dynamoRevit.GetName().Version;
                versions.Add(new Version(assemblyVersion.Major, assemblyVersion.Minor));
            }

            if (RevitDynamoVersionMap.TryGetValue(Globals.RevitVersion, out string[] mappedVersions))
            {
                versions.AddRange(mappedVersions.Select(Version.Parse));
            }

            return versions
                .GroupBy(v => $"{v.Major}.{v.Minor}")
                .Select(g => g.First());
        }

        private static IEnumerable<string> GetDynamoPackagePathCandidates(Version dynamoVersion)
        {
            string versionFolder = $"{dynamoVersion.Major}.{dynamoVersion.Minor}";

            if (dynamoVersion.Major >= 4)
            {
                yield return Path.Combine(Globals.UserRoaming, "Dynamo", "Dynamo Core", versionFolder, "packages");
                yield return Path.Combine(Globals.UserRoaming, "Dynamo", "Dynamo Revit", versionFolder, "packages");
            }
            else
            {
                yield return Path.Combine(Globals.UserRoaming, "Dynamo", "Dynamo Revit", versionFolder, "packages");
                yield return Path.Combine(Globals.UserRoaming, "Dynamo", "Dynamo Core", versionFolder, "packages");
            }
        }

        /// <summary>
        /// Gets all processes running on the machine then checks if they are "Revit" based. Then if it does find any it checks if Dynamo dll's are in memory.
        /// If yes it changes Global "IsDynamoInMemory" to True
        /// </summary>
        public static void VerifyDynamoLoaded()
        {
            List<bool> tempBoolList = new List<bool>();

            foreach (Process process in Process.GetProcessesByName("revit"))
            {
                if (process.MainModule.FileName.Contains(Globals.RevitVersion))
                {
                    foreach (ProcessModule module in process.Modules)
                    {
                        if (module.FileName.ToLower().Contains("dynamo"))
                        {
                            tempBoolList.Add(true);
                        }
                    }
                }
            }
            if (tempBoolList.Count() > 0)
            {
                Globals.IsDynamoInMemory = true;
            }
            else
            {
                Globals.IsDynamoInMemory = false;
            }
        }

    }
}
