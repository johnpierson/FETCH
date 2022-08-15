using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.IO.Packaging;
using System.Net;
using System.Text;
using System.Windows.Media;
using Autodesk.Internal.InfoCenter;
using Autodesk.Internal.Windows;
using Autodesk.Windows;
using Fetch.Classes;
using Microsoft.WindowsAPICodePack.Taskbar;
using UIFramework;
using FontFamily = System.Windows.Media.FontFamily;

namespace Fetch.Commands
{
    internal class Commands
    {
        internal static string TestURl2 =
            "https://drive.google.com/file/d/1O5kW_sThjwVK6BoAWZ3jBMFeOMggXXT_/view?usp=sharing";

        internal static string TestUrl3 = "https://drive.google.com/uc?export=download&id=1O5kW_sThjwVK6BoAWZ3jBMFeOMggXXT_";

        internal static string DownloadPath = Path.Combine(Globals.TempPath,$"{DateTime.Now:yyyyMMdd}_packages.zip");

        internal static TaskbarManager TaskbarManager;


        public static void ReadIni()
        {
            var fetchIni = new FetchIniFile();
            Globals.PackageURL = fetchIni.Read("Path","Settings");
        }

        /// <summary>
        /// Download package from google drive location and unzip into the default directory
        /// </summary>
        public static void DownloadAndUnzipPackages()
        {
            ReadIni();
            WebClient client = new WebClient();

            Uri uri = new Uri(Globals.PackageURL);

            client.DownloadFileCompleted += new AsyncCompletedEventHandler(DownloadFileCallback2);

            client.DownloadFileAsync(uri,DownloadPath);
            
            //FileDownloader fd = new FileDownloader();
            //fd.DownloadFileCompleted += FdOnDownloadFileCompleted;
            //fd.DownloadFile(TestURl2, DownloadPath);
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

        internal static void SyncPackagesFromLocalPath()
        {
            DeleteOldPackages();

            //Now Create all of the directories
            foreach (string dirPath in Directory.GetDirectories(Globals.PackageURL, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(Globals.PackageURL, Globals.DefaultDynamoPackagePath));
            }

            //Copy all the files & Replaces any files with the same name
            foreach (string newPath in Directory.GetFiles(Globals.PackageURL, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(newPath, newPath.Replace(Globals.PackageURL, Globals.DefaultDynamoPackagePath), true);
            }

            ShowNotification();
        }


        internal static void ShowNotification()
        {
            ResultItem result = new ResultItem
            {
                Title = $"Current packages fetched for Dynamo {Globals.DynamoVersion}",
                Category = "Fetch",
                IsNew = true,
                Timestamp = DateTime.Now
            };

            ComponentManager.InfoCenterPaletteManager.ShowBalloon(result);
            //ComponentManager.FontSettings.ComponentFontFamily = new FontFamily("Comic Sans MS");
        }

        #region EventHandlers
        private static void DownloadFileCallback2(object sender, AsyncCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                Console.WriteLine("File download cancelled.");
                return;
            }

            if (e.Error != null)
            {
                Console.WriteLine(e.Error.ToString());
                return;
            }

            Globals.TaskbarManager.SetProgressValue(1, 3);

            UnzipPackages();
        }

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
