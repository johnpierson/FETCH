using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.UI;

namespace Fetch
{
    internal class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            if (!FindDynamoVersions())
            {
                return Result.Failed;
            }

            Globals.TaskbarManager = Microsoft.WindowsAPICodePack.Taskbar.TaskbarManager.Instance;
            Globals.TaskbarManager.SetProgressValue(0, 3);

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

      
    }
}
