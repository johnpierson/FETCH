using System;
using System.IO;

namespace Fetch.Tests
{
    /// <summary>
    /// Creates a throwaway directory tree under %TEMP% for filesystem-shaped tests.
    /// </summary>
    internal sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fetch_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        /// <summary>Creates a nested directory from path segments and returns its full path.</summary>
        public string CreateDirectory(params string[] segments)
        {
            string full = Path;
            foreach (string segment in segments)
            {
                full = System.IO.Path.Combine(full, segment);
            }

            Directory.CreateDirectory(full);
            return full;
        }

        /// <summary>Creates an empty file, creating parent directories as needed.</summary>
        public string CreateFile(string relativePath)
        {
            string full = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            File.WriteAllText(full, string.Empty);
            return full;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
            catch (IOException)
            {
                // Best effort: a leaked temp folder must never fail a test run.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
