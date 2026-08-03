using System;
using System.IO;
using Fetch.Sync;
using Xunit;

namespace Fetch.Tests
{
    /// <summary>
    /// The sync replaces user content on every Revit startup, so the central guarantee under test
    /// is that a failure at any point leaves the existing content exactly as it was.
    /// </summary>
    [Collection("LogState")]
    public class PackageSyncTests
    {
        /// <summary>Builds a minimal but valid Dynamo package folder.</summary>
        private static string CreatePackageSource(TempDirectory temp, string name, string packageName = "SamplePackage")
        {
            string source = temp.CreateDirectory(name);
            string package = Path.Combine(source, packageName);
            Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "pkg.json"), "{\"name\":\"" + packageName + "\"}");
            File.WriteAllText(Path.Combine(package, "content.txt"), "new content");
            return source;
        }

        private static PackageSyncRequest RequestFor(string source, string destination, bool explicitSource = true)
        {
            return new PackageSyncRequest
            {
                PackageSourceDirectory = source,
                PackageSourceIsExplicit = explicitSource,
                PackageDestinationDirectory = destination
            };
        }

        [Fact]
        public void Execute_installs_packages_when_the_destination_does_not_exist()
        {
            using (var temp = new TempDirectory())
            {
                string source = CreatePackageSource(temp, "source");
                string destination = Path.Combine(temp.Path, "packages");

                PackageSync.Execute(RequestFor(source, destination));

                Assert.True(File.Exists(Path.Combine(destination, "SamplePackage", "pkg.json")));
                Assert.Equal("new content", File.ReadAllText(Path.Combine(destination, "SamplePackage", "content.txt")));
            }
        }

        [Fact]
        public void Execute_replaces_existing_package_content()
        {
            using (var temp = new TempDirectory())
            {
                string source = CreatePackageSource(temp, "source");
                string destination = temp.CreateDirectory("packages");
                File.WriteAllText(Path.Combine(destination, "stale.txt"), "stale");

                PackageSync.Execute(RequestFor(source, destination));

                Assert.False(File.Exists(Path.Combine(destination, "stale.txt")));
                Assert.True(File.Exists(Path.Combine(destination, "SamplePackage", "pkg.json")));
            }
        }

        [Fact]
        public void Execute_leaves_no_staging_or_backup_folders_behind_on_success()
        {
            using (var temp = new TempDirectory())
            {
                string source = CreatePackageSource(temp, "source");
                string destination = temp.CreateDirectory("packages");

                PackageSync.Execute(RequestFor(source, destination));

                Assert.Empty(Directory.GetDirectories(temp.Path, "*" + PackageSync.StagingSuffix + "*"));
                Assert.Empty(Directory.GetDirectories(temp.Path, "*" + PackageSync.BackupSuffix + "*"));
            }
        }

        // The central data-loss guarantee (audit finding F1).
        [Fact]
        public void Execute_leaves_existing_packages_intact_when_the_source_disappears_mid_copy()
        {
            using (var temp = new TempDirectory())
            {
                string source = CreatePackageSource(temp, "source");
                string destination = temp.CreateDirectory("packages");
                File.WriteAllText(Path.Combine(destination, "existing.txt"), "precious");

                // Hold a file open with no sharing so the staging copy fails part-way through.
                string blocked = Path.Combine(source, "SamplePackage", "locked.bin");
                File.WriteAllText(blocked, "locked");

                using (new FileStream(blocked, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Assert.ThrowsAny<IOException>(() => PackageSync.Execute(RequestFor(source, destination)));
                }

                Assert.Equal("precious", File.ReadAllText(Path.Combine(destination, "existing.txt")));
                Assert.Empty(Directory.GetDirectories(temp.Path, "*" + PackageSync.StagingSuffix + "*"));
            }
        }

        [Fact]
        public void Execute_leaves_existing_packages_intact_when_the_source_is_missing()
        {
            using (var temp = new TempDirectory())
            {
                string destination = temp.CreateDirectory("packages");
                File.WriteAllText(Path.Combine(destination, "existing.txt"), "precious");

                Assert.Throws<DirectoryNotFoundException>(
                    () => PackageSync.Execute(RequestFor(Path.Combine(temp.Path, "does-not-exist"), destination)));

                Assert.Equal("precious", File.ReadAllText(Path.Combine(destination, "existing.txt")));
            }
        }

        // Audit finding F3: a guessed package source must be refused before anything is replaced.
        [Fact]
        public void Execute_refuses_a_guessed_source_that_is_not_a_package_folder()
        {
            using (var temp = new TempDirectory())
            {
                string source = temp.CreateDirectory("random");
                Directory.CreateDirectory(Path.Combine(source, "NotAPackage"));
                File.WriteAllText(Path.Combine(source, "readme.txt"), "nothing to see");

                string destination = temp.CreateDirectory("packages");
                File.WriteAllText(Path.Combine(destination, "existing.txt"), "precious");

                Assert.Throws<InvalidDataException>(
                    () => PackageSync.Execute(RequestFor(source, destination, explicitSource: false)));

                Assert.Equal("precious", File.ReadAllText(Path.Combine(destination, "existing.txt")));
            }
        }

        [Fact]
        public void Execute_accepts_a_guessed_source_that_contains_a_dynamo_package()
        {
            using (var temp = new TempDirectory())
            {
                string source = CreatePackageSource(temp, "source");
                string destination = Path.Combine(temp.Path, "packages");

                PackageSync.Execute(RequestFor(source, destination, explicitSource: false));

                Assert.True(File.Exists(Path.Combine(destination, "SamplePackage", "pkg.json")));
            }
        }

        [Fact]
        public void Execute_accepts_an_explicit_package_folder_without_content_checks()
        {
            using (var temp = new TempDirectory())
            {
                // An explicitly named Packages folder is trusted, even when empty, so that existing
                // deployments that intentionally ship nothing keep working.
                string source = temp.CreateDirectory("Packages");
                string destination = Path.Combine(temp.Path, "packages-dest");

                PackageSync.Execute(RequestFor(source, destination));

                Assert.True(Directory.Exists(destination));
            }
        }

        // Audit finding F6: read-only content must not poison subsequent syncs.
        [Fact]
        public void Execute_succeeds_twice_when_the_source_contains_a_read_only_file()
        {
            using (var temp = new TempDirectory())
            {
                string source = CreatePackageSource(temp, "source");
                string readOnly = Path.Combine(source, "SamplePackage", "locked.txt");
                File.WriteAllText(readOnly, "read only");
                File.SetAttributes(readOnly, FileAttributes.ReadOnly);

                string destination = Path.Combine(temp.Path, "packages");

                PackageSync.Execute(RequestFor(source, destination));
                PackageSync.Execute(RequestFor(source, destination));

                Assert.True(File.Exists(Path.Combine(destination, "SamplePackage", "locked.txt")));
            }
        }

        // Audit finding F2: graph folders may hold user edits, so the previous copy is retained.
        [Fact]
        public void Execute_retains_a_backup_of_the_previous_graph_folder()
        {
            using (var temp = new TempDirectory())
            {
                string packageSource = CreatePackageSource(temp, "source");
                string graphSource = temp.CreateDirectory("graph-source");
                File.WriteAllText(Path.Combine(graphSource, "deployed.dyn"), "deployed");

                string graphDestination = temp.CreateDirectory("graphs", "2025");
                File.WriteAllText(Path.Combine(graphDestination, "my-edit.dyn"), "user work");

                PackageSync.Execute(new PackageSyncRequest
                {
                    PackageSourceDirectory = packageSource,
                    PackageSourceIsExplicit = true,
                    PackageDestinationDirectory = Path.Combine(temp.Path, "packages"),
                    GraphSourceDirectory = graphSource,
                    GraphDestinationDirectory = graphDestination
                });

                Assert.True(File.Exists(Path.Combine(graphDestination, "deployed.dyn")));

                string backup = graphDestination + PackageSync.BackupSuffix;
                Assert.True(Directory.Exists(backup));
                Assert.Equal("user work", File.ReadAllText(Path.Combine(backup, "my-edit.dyn")));
            }
        }

        // Audit finding F5: an unvalidated graph root must not drive a recursive replacement.
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("relative\\path")]
        public void ValidateDestination_rejects_unusable_destinations(string destination)
        {
            Assert.Throws<ArgumentException>(() => PackageSync.ValidateDestination(destination));
        }

        [Fact]
        public void ValidateDestination_rejects_a_drive_root()
        {
            string root = Path.GetPathRoot(Path.GetTempPath());

            Assert.Throws<ArgumentException>(() => PackageSync.ValidateDestination(root));
        }

        [Fact]
        public void ValidateDestination_rejects_the_documents_folder()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            Assert.Throws<ArgumentException>(() => PackageSync.ValidateDestination(documents));
        }

        [Fact]
        public void ValidateDestination_accepts_a_normal_subfolder()
        {
            PackageSync.ValidateDestination(Path.Combine(Path.GetTempPath(), "FETCH Dynamo Graphs", "2025"));
        }

        [Fact]
        public void GetProcessLockName_is_stable_and_case_insensitive_for_a_path()
        {
            string first = PackageSync.GetProcessLockName(@"C:\Users\test\AppData\Roaming\Dynamo\packages");
            string second = PackageSync.GetProcessLockName(@"c:\users\test\appdata\roaming\dynamo\PACKAGES");

            Assert.Equal(first, second);
            Assert.StartsWith("Local\\FETCH-PackageSync-", first);
            Assert.DoesNotContain("/", first.Substring("Local\\".Length));
        }

        [Fact]
        public void GetProcessLockName_differs_between_destinations()
        {
            Assert.NotEqual(
                PackageSync.GetProcessLockName(@"C:\one\packages"),
                PackageSync.GetProcessLockName(@"C:\two\packages"));
        }

        // A backup left behind by an earlier sync can be undeletable if its files are still in use.
        // It must be moved out of the way rather than wedging every future sync.
        [Fact]
        public void Execute_succeeds_when_a_previous_backup_cannot_be_deleted()
        {
            using (var temp = new TempDirectory())
            {
                string source = CreatePackageSource(temp, "source");
                string destination = temp.CreateDirectory("packages");
                File.WriteAllText(Path.Combine(destination, "existing.txt"), "existing");

                string staleBackup = destination + PackageSync.BackupSuffix;
                Directory.CreateDirectory(staleBackup);
                string lockedFile = Path.Combine(staleBackup, "in-use.dll");
                File.WriteAllText(lockedFile, "loaded assembly");

                using (new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    PackageSync.Execute(RequestFor(source, destination));
                }

                Assert.True(File.Exists(Path.Combine(destination, "SamplePackage", "pkg.json")));
            }
        }

        [Fact]
        public void GetDestinationPath_maps_nested_paths_onto_the_destination_root()
        {
            string source = Path.Combine(Path.GetTempPath(), "source");
            string destination = Path.Combine(Path.GetTempPath(), "destination");
            string sourceFile = Path.Combine(source, "sub", "package.dll");

            string result = PackageSync.GetDestinationPath(source, destination, sourceFile);

            Assert.Equal(Path.Combine(destination, "sub", "package.dll"), result);
        }

        [Fact]
        public void GetDestinationPath_tolerates_a_trailing_separator_on_the_source_root()
        {
            string source = Path.Combine(Path.GetTempPath(), "source") + Path.DirectorySeparatorChar;
            string destination = Path.Combine(Path.GetTempPath(), "destination");
            string sourceFile = Path.Combine(Path.GetTempPath(), "source", "package.dll");

            string result = PackageSync.GetDestinationPath(source, destination, sourceFile);

            Assert.Equal(Path.Combine(destination, "package.dll"), result);
        }

        [Fact]
        public void Run_acquires_the_process_lock_and_completes()
        {
            using (var temp = new TempDirectory())
            {
                string source = CreatePackageSource(temp, "source");
                string destination = Path.Combine(temp.Path, "packages");

                PackageSync.Run(RequestFor(source, destination));

                Assert.True(File.Exists(Path.Combine(destination, "SamplePackage", "pkg.json")));
            }
        }
    }
}
