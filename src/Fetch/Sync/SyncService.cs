using System;
using System.IO;
using System.IO.Compression;
using Fetch.Configuration;
using Fetch.Diagnostics;
using Fetch.Sources;

namespace Fetch.Sync
{
    /// <summary>
    /// Runs a deployment end to end: acquire the source (a folder, or a downloaded and verified
    /// zip), work out its layout, and hand it to <see cref="PackageSync"/> to install.
    ///
    /// This was previously a static class that also read settings and showed dialogs.
    /// </summary>
    internal sealed class SyncService
    {
        private readonly FetchContext context;

        internal SyncService(FetchContext context)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        internal void SyncFromFolder(string sourceDirectory)
        {
            InstallFrom(sourceDirectory);
        }

        /// <summary>Downloads a package zip and installs its contents.</summary>
        internal void SyncFromUrl(Uri packageUri)
        {
            string downloadPath = Path.Combine(FetchPaths.TempPath, $"fetch_packages_{Guid.NewGuid():N}.zip");
            string extractPath = Path.Combine(FetchPaths.TempPath, $"fetch_packages_{Guid.NewGuid():N}");

            try
            {
                Log.Info($"Downloading packages from '{packageUri.AbsoluteUri}'.");

                using (FileDownloader fileDownloader = new FileDownloader())
                {
                    fileDownloader.DownloadFile(packageUri.AbsoluteUri, downloadPath);
                }

                Log.Info($"Downloaded {(File.Exists(downloadPath) ? new FileInfo(downloadPath).Length : 0L)} bytes; verifying.");

                // Verified before extracting so a wrong or tampered download never reaches the
                // packages folder, and so the user gets a specific reason instead of a zip error.
                PackageIntegrity.VerifyDownloadedArchive(downloadPath, context.Settings.PackageSha256);
                Log.Info(string.IsNullOrWhiteSpace(context.Settings.PackageSha256)
                    ? "Archive signature verified; extracting."
                    : "Archive signature and SHA-256 verified; extracting.");

                Extract(downloadPath, extractPath);
                InstallFrom(extractPath);
            }
            finally
            {
                DeleteTemporaryFile(downloadPath);
                DeleteTemporaryDirectory(extractPath);
            }
        }

        private void InstallFrom(string sourceDirectory)
        {
            SourceLayout layout = SourceLayoutResolver.Resolve(sourceDirectory, context.RevitVersion);

            Log.Info($"Syncing packages from '{layout.PackageSourceDirectory}' to '{context.Dynamo.PackagePath}'.");

            PackageSync.Run(new PackageSyncRequest
            {
                PackageSourceDirectory = layout.PackageSourceDirectory,
                PackageSourceIsExplicit = layout.PackageSourceIsExplicit,
                PackageDestinationDirectory = context.Dynamo.PackagePath,
                GraphSourceDirectory = layout.GraphSourceDirectory,
                GraphDestinationDirectory = context.GraphDestination
            });
        }

        private static void Extract(string downloadPath, string extractPath)
        {
            if (!File.Exists(downloadPath))
            {
                throw new FileNotFoundException("The package zip was not downloaded.", downloadPath);
            }

            Directory.CreateDirectory(extractPath);
            ZipFile.ExtractToDirectory(downloadPath, extractPath);
        }

        private static void DeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                // Cleanup must never replace the exception that explains the actual failure.
                Log.Warning($"Could not remove the temporary download '{path}': {ex.Message}");
            }
        }

        private static void DeleteTemporaryDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Could not remove the temporary folder '{path}': {ex.Message}");
            }
        }
    }
}
