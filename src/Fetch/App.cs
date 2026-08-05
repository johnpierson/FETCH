using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Fetch.Configuration;
using Fetch.Diagnostics;
using Fetch.Dynamo;
using Fetch.Sources;
using Fetch.Sync;

namespace Fetch
{
    internal class App : IExternalApplication
    {
        private readonly object messageLock = new object();

        private UIControlledApplication uiApplication;
        private FetchContext context;
        private string pendingMessage;
        private volatile bool syncFinished;

        public Result OnStartup(UIControlledApplication application)
        {
            string revitVersion = application.ControlledApplication.VersionNumber;

            Log.Info($"FETCH {Assembly.GetExecutingAssembly().GetName().Version} starting for Revit {revitVersion}.");

            DynamoInstallation dynamo = DynamoInstallationResolver.Resolve(
                DynamoInstallationResolver.FindLoadedSelectorAssembly(),
                revitVersion,
                FetchPaths.UserRoaming);

            if (dynamo == null)
            {
                Log.Warning("No Dynamo version candidates were found; skipping package sync.");
                return Result.Failed;
            }

            Log.Info(dynamo.PackagePathExists
                ? $"Resolved Dynamo {dynamo.Version}; package folder '{dynamo.PackagePath}'."
                : $"No installed Dynamo package folder found; assuming Dynamo {dynamo.Version} at '{dynamo.PackagePath}'.");

            FetchSettings settings = SettingsReader.Read(FetchPaths.ExecutingDirectory, revitVersion);
            context = new FetchContext(revitVersion, dynamo, settings);

            Log.Info($"Settings '{settings.SettingsPath}'; source '{settings.PackageSource}'; graph root '{settings.GraphRootPath}'.");

            // The sync is network and file I/O with no Revit API calls, so it runs off the startup
            // thread. Revit finishes launching immediately; results are reported on the first idle.
            StartBackgroundSync(application);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            DetachIdling();
            return Result.Succeeded;
        }

        private void StartBackgroundSync(UIControlledApplication application)
        {
            uiApplication = application;
            application.Idling += OnIdling;

            Task.Run(() =>
            {
                try
                {
                    RunSync();
                }
                catch (Exception ex)
                {
                    // Nothing may escape this task: an unobserved exception here would be raised on
                    // a finalizer thread rather than reported to the user.
                    Log.Error("Package sync failed.", ex);

                    if (SyncFailureClassifier.IsLikelyOffline(ex))
                    {
                        // Starting Revit away from the network is routine; do not nag about it.
                        Log.Warning("The package source could not be reached; leaving the installed packages in place.");
                    }
                    else
                    {
                        SetPendingMessage(
                            $"Dynamo packages were not updated.\n\n{ex.Message}\n\nSettings file:\n{context.Settings.SettingsPath}\n\nThere is more detail in:\n{Log.FilePath}");
                    }
                }
                finally
                {
                    syncFinished = true;
                }
            });
        }

        private void RunSync()
        {
            // Checked here rather than at startup so the answer reflects the moment the sync would
            // actually touch the folder.
            if (DynamoSessionDetector.IsDynamoLoadedInAnotherSession(context.RevitVersion))
            {
                Log.Warning("Another Revit session has Dynamo loaded; skipping the sync to avoid replacing package files that are in use.");
                SetPendingMessage(
                    "Dynamo packages were not updated because another Revit session currently has Dynamo open, "
                    + "and its package files are in use.\n\nClose the other Revit session and restart Revit to update the packages.");
                return;
            }

            SyncService syncService = new SyncService(context);
            string packageSource = context.Settings.PackageSource;

            if (Directory.Exists(packageSource))
            {
                syncService.SyncFromFolder(packageSource);
                Log.Info("Local package sync completed.");
                return;
            }

            if (!PackageSourcePolicy.TryValidateUrl(
                    packageSource,
                    context.Settings.AllowInsecureHttp,
                    out Uri packageUri,
                    out string packageSourceError))
            {
                Log.Warning($"Package source '{packageSource}' rejected: {packageSourceError}");
                SetPendingMessage(
                    $"{packageSourceError}\n\nSettings file:\n{context.Settings.SettingsPath}\n\nPath value:\n{packageSource}");
                return;
            }

            syncService.SyncFromUrl(packageUri);
            Log.Info("Package download and sync completed.");
        }

        private void SetPendingMessage(string message)
        {
            lock (messageLock)
            {
                pendingMessage = message;
            }
        }

        /// <summary>
        /// Reports the outcome once Revit is idle. A successful sync says nothing at all; only a
        /// problem the user can act on raises a dialog, and only once.
        /// </summary>
        private void OnIdling(object sender, IdlingEventArgs e)
        {
            if (!syncFinished)
            {
                return;
            }

            string message;
            lock (messageLock)
            {
                message = pendingMessage;
                pendingMessage = null;
            }

            DetachIdling();

            if (!string.IsNullOrEmpty(message))
            {
                TaskDialog.Show("Fetch", message);
            }
        }

        private void DetachIdling()
        {
            UIControlledApplication application = uiApplication;
            if (application == null)
            {
                return;
            }

            uiApplication = null;

            try
            {
                application.Idling -= OnIdling;
            }
            catch (Exception ex)
            {
                Log.Warning($"Could not detach the idling handler: {ex.Message}");
            }
        }
    }
}
