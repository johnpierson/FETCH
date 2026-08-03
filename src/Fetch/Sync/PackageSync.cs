using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Fetch.Diagnostics;

namespace Fetch.Sync
{
    internal sealed class PackageSyncRequest
    {
        public string PackageSourceDirectory { get; set; }
        public bool PackageSourceIsExplicit { get; set; }
        public string PackageDestinationDirectory { get; set; }
        public string GraphSourceDirectory { get; set; }
        public string GraphDestinationDirectory { get; set; }
    }

    /// <summary>
    /// Replaces the Dynamo package folder (and optionally the deployed graph folder) from a
    /// resolved deployment source.
    ///
    /// The content is staged into a sibling folder and only swapped into place once the copy has
    /// fully succeeded, so a failure part-way through leaves the existing content untouched. The
    /// previous content is moved aside rather than deleted, and for graphs that backup is retained
    /// because those files may contain user edits.
    /// </summary>
    internal static class PackageSync
    {
        internal const string StagingSuffix = ".fetch-staging";
        internal const string BackupSuffix = ".fetch-backup";

        /// <summary>How long to wait for another Revit session that is already syncing.</summary>
        internal static readonly TimeSpan ProcessLockTimeout = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Runs the sync, serialized against other processes targeting the same package folder.
        /// </summary>
        internal static void Run(PackageSyncRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string mutexName = GetProcessLockName(request.PackageDestinationDirectory);

            using (Mutex processLock = new Mutex(false, mutexName))
            {
                bool acquired = false;
                try
                {
                    try
                    {
                        acquired = processLock.WaitOne(ProcessLockTimeout);
                    }
                    catch (AbandonedMutexException)
                    {
                        // The previous holder died mid-sync. We own the lock now; the staged-swap
                        // design means its partial work is confined to a staging folder.
                        acquired = true;
                        Log.Warning("A previous sync did not release its lock cleanly; continuing.");
                    }

                    if (!acquired)
                    {
                        throw new TimeoutException(
                            "Another Revit session is already syncing Dynamo packages. Close the other session and restart Revit to retry.");
                    }

                    Execute(request);
                }
                finally
                {
                    if (acquired)
                    {
                        processLock.ReleaseMutex();
                    }
                }
            }
        }

        /// <summary>
        /// Runs the sync without acquiring the cross-process lock. Separated so the filesystem
        /// semantics can be exercised directly by tests.
        /// </summary>
        internal static void Execute(PackageSyncRequest request)
        {
            ValidatePackageSource(request);
            ReplaceDirectory(request.PackageSourceDirectory, request.PackageDestinationDirectory, retainBackup: false);

            if (!string.IsNullOrWhiteSpace(request.GraphSourceDirectory) &&
                Directory.Exists(request.GraphSourceDirectory))
            {
                // Graph folders can contain user edits, so the previous generation is kept.
                ReplaceDirectory(request.GraphSourceDirectory, request.GraphDestinationDirectory, retainBackup: true);
            }
        }

        /// <summary>
        /// Rejects a source that cannot be trusted to be a package folder, before anything is
        /// replaced. Only the guessed fallback is content-checked; an explicitly named Packages
        /// folder is taken at face value so existing deployments keep working.
        /// </summary>
        internal static void ValidatePackageSource(PackageSyncRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.PackageSourceDirectory))
            {
                throw new DirectoryNotFoundException(
                    "The deployment does not contain a Packages folder for this Revit version.");
            }

            if (!Directory.Exists(request.PackageSourceDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Package source folder not found: {request.PackageSourceDirectory}");
            }

            if (!request.PackageSourceIsExplicit && !ContainsDynamoPackage(request.PackageSourceDirectory))
            {
                throw new InvalidDataException(
                    $"The package source does not look like a Dynamo package folder (no package containing pkg.json was found): {request.PackageSourceDirectory}. " +
                    "Check that the configured source points at a deployment with a 'Packages' folder.");
            }
        }

        /// <summary>A Dynamo package is a folder containing a pkg.json manifest.</summary>
        internal static bool ContainsDynamoPackage(string directory)
        {
            foreach (string child in Directory.GetDirectories(directory))
            {
                if (File.Exists(Path.Combine(child, "pkg.json")))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Refuses destinations that would put a recursive replacement somewhere dangerous, such as
        /// a drive root or a well-known user folder. Also requires an absolute path, because a
        /// relative one would resolve against the host process's working directory.
        /// </summary>
        internal static void ValidateDestination(string destination)
        {
            if (string.IsNullOrWhiteSpace(destination))
            {
                throw new ArgumentException("The sync destination folder is not configured.", nameof(destination));
            }

            if (!Path.IsPathRooted(destination))
            {
                throw new ArgumentException(
                    $"The sync destination must be an absolute path: {destination}", nameof(destination));
            }

            string full = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.IsNullOrEmpty(full) ||
                string.Equals(full, Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Refusing to replace a drive root: {destination}", nameof(destination));
            }

            foreach (Environment.SpecialFolder folder in ProtectedFolders)
            {
                string protectedPath = Environment.GetFolderPath(folder);
                if (string.IsNullOrEmpty(protectedPath))
                {
                    continue;
                }

                protectedPath = Path.GetFullPath(protectedPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (string.Equals(full, protectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"Refusing to replace a protected system or user folder: {destination}", nameof(destination));
                }
            }
        }

        private static readonly Environment.SpecialFolder[] ProtectedFolders =
        {
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.System,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.CommonApplicationData
        };

        /// <summary>
        /// Copies <paramref name="source"/> over <paramref name="destination"/> transactionally:
        /// stage, swap, then discard (or retain) the previous content.
        /// </summary>
        internal static void ReplaceDirectory(string source, string destination, bool retainBackup)
        {
            ValidateDestination(destination);

            string parent = Path.GetDirectoryName(Path.GetFullPath(destination));
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            string staging = destination + StagingSuffix + "-" + Guid.NewGuid().ToString("N");
            string backup = null;
            bool movedAside = false;

            Log.Info($"Staging '{source}' for '{destination}'.");

            // Clear leftovers from an earlier run whose files are no longer in use.
            PruneStaleBackups(destination);

            try
            {
                CopyDirectory(source, staging);

                if (Directory.Exists(destination))
                {
                    backup = ResolveBackupPath(destination);
                    Directory.Move(destination, backup);
                    movedAside = true;
                }

                try
                {
                    Directory.Move(staging, destination);
                }
                catch
                {
                    if (movedAside)
                    {
                        // Put the original content back so the failure costs the user nothing.
                        Directory.Move(backup, destination);
                        movedAside = false;
                    }

                    throw;
                }

                if (movedAside && !retainBackup)
                {
                    // The swap already succeeded, so a backup that cannot be removed (locked by a
                    // virus scanner, say) must not turn a completed sync into a reported failure.
                    DeleteDirectoryQuietly(backup);
                }
                else if (movedAside)
                {
                    Log.Info($"Previous content retained at '{backup}'.");
                }

                Log.Info($"Replaced '{destination}'.");
            }
            finally
            {
                DeleteDirectoryQuietly(staging);
            }
        }

        /// <summary>
        /// Picks where the outgoing content is moved. Normally the canonical backup name, reusing it
        /// after removing the previous generation. A backup whose files are still in use cannot be
        /// removed (or renamed), so rather than let one stuck folder wedge every future sync, this
        /// run simply uses a distinct name.
        /// </summary>
        private static string ResolveBackupPath(string destination)
        {
            string canonical = destination + BackupSuffix;

            if (!Directory.Exists(canonical))
            {
                return canonical;
            }

            try
            {
                DeleteDirectory(canonical);
                return canonical;
            }
            catch (Exception ex)
            {
                string unique = canonical + "-" + Guid.NewGuid().ToString("N");
                Log.Warning($"The previous backup '{canonical}' is in use ({ex.Message}); keeping this run's backup at '{unique}'.");
                return unique;
            }
        }

        /// <summary>
        /// Best-effort removal of backups left by earlier runs that could not be cleaned up at the
        /// time. They become deletable once whatever held them open has closed.
        /// </summary>
        private static void PruneStaleBackups(string destination)
        {
            try
            {
                string parent = Path.GetDirectoryName(Path.GetFullPath(destination));
                if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                {
                    return;
                }

                string pattern = Path.GetFileName(destination) + BackupSuffix + "-*";

                foreach (string stale in Directory.GetDirectories(parent, pattern))
                {
                    DeleteDirectoryQuietly(stale);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Could not check for stale backups next to '{destination}': {ex.Message}");
            }
        }

        internal static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(GetDestinationPath(source, destination, directory));
            }

            foreach (string file in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
            {
                string target = GetDestinationPath(source, destination, file);
                File.Copy(file, target, true);

                // File.Copy carries the read-only attribute across. Left in place it would make the
                // next sync unable to overwrite or delete this file.
                ClearReadOnly(target);
            }
        }

        internal static string GetDestinationPath(string sourceDirectory, string destinationDirectory, string sourcePath)
        {
            string sourceRoot = Path.GetFullPath(sourceDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullSourcePath = Path.GetFullPath(sourcePath);
            string relativePath = fullSourcePath.Substring(sourceRoot.Length);
            return Path.Combine(destinationDirectory, relativePath);
        }

        /// <summary>Deletes a tree, clearing read-only attributes that would otherwise block it.</summary>
        internal static void DeleteDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            ClearReadOnlyRecursive(path);
            Directory.Delete(path, true);
        }

        private static void DeleteDirectoryQuietly(string path)
        {
            try
            {
                DeleteDirectory(path);
            }
            catch (Exception ex)
            {
                // Never let cleanup replace the exception that actually explains the failure.
                Log.Warning($"Could not remove temporary folder '{path}': {ex.Message}");
            }
        }

        internal static void ClearReadOnlyRecursive(string path)
        {
            DirectoryInfo root = new DirectoryInfo(path);
            if (!root.Exists)
            {
                return;
            }

            if ((root.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                root.Attributes &= ~FileAttributes.ReadOnly;
            }

            foreach (FileInfo file in root.GetFiles("*", SearchOption.AllDirectories))
            {
                if ((file.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    file.Attributes &= ~FileAttributes.ReadOnly;
                }
            }

            foreach (DirectoryInfo directory in root.GetDirectories("*", SearchOption.AllDirectories))
            {
                if ((directory.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    directory.Attributes &= ~FileAttributes.ReadOnly;
                }
            }
        }

        internal static void ClearReadOnly(string filePath)
        {
            FileInfo file = new FileInfo(filePath);
            if (file.Exists && (file.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                file.Attributes &= ~FileAttributes.ReadOnly;
            }
        }

        /// <summary>
        /// Builds a stable, filesystem-safe mutex name for a destination folder. Session-local:
        /// package folders are per user, so users never block each other.
        /// </summary>
        internal static string GetProcessLockName(string destination)
        {
            string key = string.IsNullOrWhiteSpace(destination)
                ? string.Empty
                : Path.GetFullPath(destination).ToLowerInvariant();

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                StringBuilder builder = new StringBuilder("Local\\FETCH-PackageSync-");

                for (int i = 0; i < 16; i++)
                {
                    builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }
    }
}
