using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using UIFramework;

namespace PiggyBack.Commands
{
    internal class Commands
    {
        internal static string TestURl =
            "https://drive.google.com/file/d/0B3oLhdhnXmutc3R5M3RjWTVlaFU/view?usp=sharing&resourcekey=0-MyL3EyPc2NoIizNbAXMyzg";

        internal static string TestPath = @"C:\Users\johnpierson\Desktop\lighting.zip";

        public static void DownloadPackages()
        {
            FileDownloader fd = new FileDownloader();
            fd.DownloadFileCompleted += FdOnDownloadFileCompleted;
            fd.DownloadFile(TestURl, TestPath);

        }

        //show windows notification about packages being done syncing
        private static void FdOnDownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            var notification = new System.Windows.Forms.NotifyIcon()
            {
                Visible = true,
                Icon = System.Drawing.SystemIcons.Information,
                BalloonTipText = "Packages synced",
            };
            notification.ShowBalloonTip(1000);

        }
    }
}
