using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.UI;

namespace Fetch
{
    internal class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            //check if there is an internet connection first
            if (!Utilities.Utilities.CheckForInternetConnection())
            {
                return Result.Cancelled;
            }

            Globals.TaskbarManager = Microsoft.WindowsAPICodePack.Taskbar.TaskbarManager.Instance;
            Globals.TaskbarManager.SetProgressValue(0, 3);

            if (FindDynamoVersions())
            {
                Commands.Commands.DownloadAndUnzipPackages();

                return Result.Succeeded;
            }

            return Result.Failed;
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
