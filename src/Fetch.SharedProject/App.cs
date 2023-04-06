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
                catch (Exception)
                {
                    return Result.Failed;
                }
            }

            //TODO: Re-Implement the cloud downloading ability. For now, we support local paths.
            return Result.Cancelled;

            //if the URL is a google drive link, try to download
            //check if there is an internet connection first
            if (!Utilities.Utilities.CheckForInternetConnection())
            {
                return Result.Cancelled;
            }

            Commands.Commands.DownloadAndUnzipPackages();

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        public bool FindDynamoVersions()
        {
            var dynamoRevit = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.FullName.Contains("DynamoRevitVersionSelector"));

            if (dynamoRevit is null) return false;
  

            Globals.DynamoVersion = dynamoRevit.GetName().Version;
            //now set the package path location
            Globals.DefaultDynamoPackagePath =
                $"{Globals.UserRoaming}\\Dynamo\\Dynamo Revit\\{Globals.DynamoVersion.Major}.{Globals.DynamoVersion.Minor}\\packages";

            return true;
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
