using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Fetch.Diagnostics
{
    /// <summary>
    /// Minimal rolling file log. FETCH replaces user content on every Revit startup, so each run
    /// leaves a record of what it resolved, what it changed, and why it stopped.
    /// Logging is best effort: no call here may throw or otherwise disturb Revit startup.
    /// </summary>
    internal static class Log
    {
        internal const long MaxLogBytes = 1024 * 1024;
        internal const string LogFileName = "FetchLog.txt";

        private static readonly object SyncRoot = new object();
        private static string filePath;

        /// <summary>The resolved log file path, chosen on first use.</summary>
        internal static string FilePath
        {
            get
            {
                lock (SyncRoot)
                {
                    return filePath ?? (filePath = ResolveDefaultFilePath());
                }
            }
        }

        /// <summary>Overrides the log destination. Used by tests and by explicit configuration.</summary>
        internal static void UseFile(string path)
        {
            lock (SyncRoot)
            {
                filePath = path;
            }
        }

        internal static void Info(string message)
        {
            Write("INFO", message, null);
        }

        internal static void Warning(string message)
        {
            Write("WARN", message, null);
        }

        internal static void Error(string message, Exception exception = null)
        {
            Write("ERROR", message, exception);
        }

        /// <summary>Renders a single log line. Pure; separated from I/O so it can be asserted directly.</summary>
        internal static string FormatEntry(DateTime timestampUtc, string level, string message, Exception exception)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(timestampUtc.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            builder.Append("Z [");
            builder.Append(level);
            builder.Append("] ");
            builder.Append(message);

            if (exception != null)
            {
                builder.Append(" | ");
                builder.Append(exception.GetType().Name);
                builder.Append(": ");
                builder.Append(exception.Message);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Moves the current log aside once it exceeds <see cref="MaxLogBytes"/>, keeping one
        /// previous generation so a single run can never grow the folder without bound.
        /// </summary>
        internal static void RollIfNeeded(string path)
        {
            FileInfo current = new FileInfo(path);
            if (!current.Exists || current.Length < MaxLogBytes)
            {
                return;
            }

            string directory = current.DirectoryName ?? string.Empty;
            string previous = Path.Combine(
                directory,
                Path.GetFileNameWithoutExtension(path) + ".previous" + current.Extension);

            if (File.Exists(previous))
            {
                File.Delete(previous);
            }

            File.Move(path, previous);
        }

        private static void Write(string level, string message, Exception exception)
        {
            try
            {
                lock (SyncRoot)
                {
                    string path = FilePath;
                    string directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    RollIfNeeded(path);
                    File.AppendAllText(
                        path,
                        FormatEntry(DateTime.UtcNow, level, message, exception) + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch
            {
                // A failed log write must never break the add-in.
            }
        }

        private static string ResolveDefaultFilePath()
        {
            // Preferred location is beside the installed add-in and its settings file, so that
            // whoever configured the deployment finds the log next to the INI they edited.
            string executingPath = Configuration.FetchPaths.ExecutingDirectory;
            if (!string.IsNullOrWhiteSpace(executingPath))
            {
                string candidate = Path.Combine(executingPath, LogFileName);
                if (IsWritable(candidate))
                {
                    return candidate;
                }
            }

            // Machine-wide or read-only installations fall back to the user's local app data.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FETCH",
                LogFileName);
        }

        private static bool IsWritable(string path)
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
