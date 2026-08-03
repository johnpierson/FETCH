using System;
using System.IO;
using System.Text;
using Fetch.Diagnostics;
using Xunit;

namespace Fetch.Tests
{
    /// <summary>
    /// The logger is the only durable record of what FETCH replaced on a given startup, so these
    /// tests cover both the rendered content and the guarantee that logging never throws.
    /// </summary>
    [Collection("LogState")]
    public class LogTests
    {
        [Fact]
        public void FormatEntry_renders_timestamp_level_and_message()
        {
            string entry = Log.FormatEntry(
                new DateTime(2026, 8, 2, 9, 5, 3, 42, DateTimeKind.Utc),
                "INFO",
                "Sync completed.",
                null);

            Assert.Equal("2026-08-02 09:05:03.042Z [INFO] Sync completed.", entry);
        }

        [Fact]
        public void FormatEntry_appends_the_exception_type_and_message()
        {
            string entry = Log.FormatEntry(
                new DateTime(2026, 8, 2, 9, 5, 3, 42, DateTimeKind.Utc),
                "ERROR",
                "Sync failed.",
                new InvalidOperationException("packages folder is locked"));

            Assert.Equal(
                "2026-08-02 09:05:03.042Z [ERROR] Sync failed. | InvalidOperationException: packages folder is locked",
                entry);
        }

        [Fact]
        public void Write_appends_each_entry_to_the_configured_file()
        {
            using (var temp = new TempDirectory())
            {
                string logPath = Path.Combine(temp.Path, "FetchLog.txt");
                Log.UseFile(logPath);

                Log.Info("first");
                Log.Warning("second");
                Log.Error("third", new InvalidOperationException("boom"));

                string[] lines = File.ReadAllLines(logPath);

                Assert.Equal(3, lines.Length);
                Assert.Contains("[INFO] first", lines[0]);
                Assert.Contains("[WARN] second", lines[1]);
                Assert.Contains("[ERROR] third | InvalidOperationException: boom", lines[2]);
            }
        }

        [Fact]
        public void Write_creates_the_log_directory_when_it_does_not_exist()
        {
            using (var temp = new TempDirectory())
            {
                string logPath = Path.Combine(temp.Path, "nested", "folder", "FetchLog.txt");
                Log.UseFile(logPath);

                Log.Info("created on demand");

                Assert.True(File.Exists(logPath));
            }
        }

        [Fact]
        public void Write_never_throws_when_the_destination_is_unusable()
        {
            // A path under a file (rather than a directory) can never be created.
            using (var temp = new TempDirectory())
            {
                string blocker = temp.CreateFile("blocker.txt");
                Log.UseFile(Path.Combine(blocker, "FetchLog.txt"));

                Log.Info("swallowed");
                Log.Error("also swallowed", new InvalidOperationException("boom"));
            }
        }

        [Fact]
        public void RollIfNeeded_leaves_a_small_log_in_place()
        {
            using (var temp = new TempDirectory())
            {
                string logPath = Path.Combine(temp.Path, "FetchLog.txt");
                File.WriteAllText(logPath, "small");

                Log.RollIfNeeded(logPath);

                Assert.True(File.Exists(logPath));
                Assert.False(File.Exists(Path.Combine(temp.Path, "FetchLog.previous.txt")));
            }
        }

        [Fact]
        public void RollIfNeeded_moves_an_oversized_log_to_a_single_previous_generation()
        {
            using (var temp = new TempDirectory())
            {
                string logPath = Path.Combine(temp.Path, "FetchLog.txt");
                string previousPath = Path.Combine(temp.Path, "FetchLog.previous.txt");

                File.WriteAllText(previousPath, "stale generation");
                File.WriteAllText(logPath, new string('x', (int)Log.MaxLogBytes + 1), Encoding.ASCII);

                Log.RollIfNeeded(logPath);

                Assert.False(File.Exists(logPath));
                Assert.True(File.Exists(previousPath));
                Assert.DoesNotContain("stale generation", File.ReadAllText(previousPath));
            }
        }

        [Fact]
        public void Write_rolls_the_log_once_it_exceeds_the_size_limit()
        {
            using (var temp = new TempDirectory())
            {
                string logPath = Path.Combine(temp.Path, "FetchLog.txt");
                File.WriteAllText(logPath, new string('x', (int)Log.MaxLogBytes + 1), Encoding.ASCII);
                Log.UseFile(logPath);

                Log.Info("after roll");

                Assert.True(File.Exists(Path.Combine(temp.Path, "FetchLog.previous.txt")));
                Assert.Contains("[INFO] after roll", File.ReadAllText(logPath));
            }
        }
    }
}
