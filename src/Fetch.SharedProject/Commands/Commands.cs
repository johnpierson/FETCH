using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Packaging;
using System.Text;
using Fetch.Classes;
using Microsoft.WindowsAPICodePack.Taskbar;
using UIFramework;

namespace Fetch.Commands
{
    internal class Commands
    {
        internal static string TestURl = string.Empty;

        internal static string TestURl2 =
            "https://drive.google.com/file/d/1O5kW_sThjwVK6BoAWZ3jBMFeOMggXXT_/view?usp=sharing";

        internal static string DownloadPath = Path.Combine(Globals.TempPath,$"{DateTime.Now:yyyyMMdd}_packages.zip");

        internal static TaskbarManager TaskbarManager;


        public static void ReadINI()
        {
            var fetchIni = new FetchIniFile();
            TestURl = fetchIni.Read("Path","Settings");
        }

        /// <summary>
        /// Download package from google drive location and unzip into the default directory
        /// </summary>
        public static void DownloadAndUnzipPackages()
        {
            ReadINI();

            FileDownloader fd = new FileDownloader();
            fd.DownloadFileCompleted += FdOnDownloadFileCompleted;
            fd.DownloadFile(TestURl, DownloadPath);
        }

        private static void UnzipPackages()
        {
            DeleteOldPackages();
            System.IO.Compression.ZipFile.ExtractToDirectory(DownloadPath, Globals.DefaultDynamoPackagePath);
            Globals.TaskbarManager.SetProgressValue(3, 3);
            Globals.TaskbarManager.SetProgressState(TaskbarProgressBarState.NoProgress);
            
            ShowNotification();
        }

        private static void DeleteOldPackages()
        {
            System.IO.DirectoryInfo di = new DirectoryInfo(Globals.DefaultDynamoPackagePath);

            foreach (FileInfo file in di.GetFiles())
            {
                file.Delete();
            }
            foreach (DirectoryInfo dir in di.GetDirectories())
            {
                dir.Delete(true);
            }
            Globals.TaskbarManager.SetProgressValue(2, 3);
        }


        private static void ShowNotification()
        {
            var notification = new System.Windows.Forms.NotifyIcon()
            {
                Visible = true,
                Icon = System.Drawing.SystemIcons.Information,
                BalloonTipText = $"Packages fetched for Dynamo {Globals.DynamoVersion}",
                BalloonTipTitle = "Fetch"
            };

            notification.ShowBalloonTip(1000);
        }

        #region EventHandlers
        /// <summary>
        /// Show a notification regarding download of zip completed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void FdOnDownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            Globals.TaskbarManager.SetProgressValue(1,3);

            UnzipPackages();
        }

        private static void PackageWatcherOnCreated(object sender, FileSystemEventArgs e)
        {
            TaskbarManager.SetProgressValue(10,100);
        }

        #endregion
    }
}
