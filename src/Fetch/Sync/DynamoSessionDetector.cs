using System;
using System.Diagnostics;
using Fetch.Diagnostics;

namespace Fetch.Sync
{
    /// <summary>
    /// Detects whether another Revit session of the same version already has Dynamo loaded.
    ///
    /// When it does, the package assemblies in the target folder are mapped into that process, and
    /// replacing the folder underneath it is both likely to fail and liable to destabilise the
    /// running session. The sync is skipped instead.
    ///
    /// Inspecting other processes is inherently failure-prone: a process can exit between being
    /// listed and being queried, and one running elevated or in another session cannot be queried at
    /// all. Every such failure is treated as "cannot tell", never as an error, because this runs on
    /// the path that loads the add-in.
    /// </summary>
    internal static class DynamoSessionDetector
    {
        internal const string RevitProcessName = "revit";

        internal static bool IsDynamoLoadedInAnotherSession(string revitVersion)
        {
            int currentProcessId = TryGetCurrentProcessId();
            Process[] processes;

            try
            {
                processes = Process.GetProcessesByName(RevitProcessName);
            }
            catch (Exception ex)
            {
                Log.Warning($"Could not enumerate Revit processes: {ex.Message}");
                return false;
            }

            try
            {
                foreach (Process process in processes)
                {
                    // The current session is the one asking; only other sessions hold locks we care about.
                    if (process.Id == currentProcessId)
                    {
                        continue;
                    }

                    if (!IsMatchingRevitProcess(TryGetMainModulePath(process), revitVersion))
                    {
                        continue;
                    }

                    if (HasDynamoModule(process))
                    {
                        Log.Info($"Revit process {process.Id} has Dynamo loaded.");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Could not inspect Revit processes: {ex.Message}");
            }
            finally
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        process.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            return false;
        }

        internal static bool IsMatchingRevitProcess(string mainModulePath, string revitVersion)
        {
            if (string.IsNullOrEmpty(mainModulePath) || string.IsNullOrEmpty(revitVersion))
            {
                return false;
            }

            return mainModulePath.IndexOf(revitVersion, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsDynamoModule(string moduleFileName)
        {
            return !string.IsNullOrEmpty(moduleFileName) &&
                   moduleFileName.IndexOf("dynamo", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int TryGetCurrentProcessId()
        {
            try
            {
                using (Process current = Process.GetCurrentProcess())
                {
                    return current.Id;
                }
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private static string TryGetMainModulePath(Process process)
        {
            try
            {
                // Throws Win32Exception when the process cannot be queried (elevated or another
                // session) and InvalidOperationException when it has already exited.
                return process.MainModule?.FileName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool HasDynamoModule(Process process)
        {
            try
            {
                foreach (ProcessModule module in process.Modules)
                {
                    try
                    {
                        if (IsDynamoModule(module.FileName))
                        {
                            return true;
                        }
                    }
                    finally
                    {
                        module.Dispose();
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }
    }
}
