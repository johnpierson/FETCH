using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.Revit.UI;

namespace PiggyBack
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

            Commands.Commands.DownloadPackages();

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
