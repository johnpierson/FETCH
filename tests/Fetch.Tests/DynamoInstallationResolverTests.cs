using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fetch.Dynamo;
using Xunit;

namespace Fetch.Tests
{
    /// <summary>
    /// Dynamo selection used to live inside the Revit application class and publish its answer by
    /// assigning statics, so it could not be exercised without Revit (audit finding A6). These
    /// tests drive it directly through an injected probe.
    /// </summary>
    [Collection("LogState")]
    public class DynamoInstallationResolverTests
    {
        private const string Roaming = @"C:\Users\test\AppData\Roaming";

        private sealed class FakeProbe : DynamoInstallationResolver.IDynamoProbe
        {
            private readonly HashSet<string> directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> withFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            internal FakeProbe WithPackagesFolder(params string[] paths)
            {
                foreach (string path in paths)
                {
                    directories.Add(path);
                }

                return this;
            }

            /// <summary>Marks a version folder as one Dynamo itself has written into.</summary>
            internal FakeProbe WithDynamoProfile(params string[] packagePaths)
            {
                foreach (string path in packagePaths)
                {
                    directories.Add(path);
                    withFiles.Add(Path.GetDirectoryName(path));
                }

                return this;
            }

            public bool DirectoryExists(string path) => directories.Contains(path);

            public bool DirectoryHasFiles(string path) => withFiles.Contains(path);
        }

        private static string RevitPackages(string version) =>
            Path.Combine(Roaming, "Dynamo", "Dynamo Revit", version, "packages");

        private static string CorePackages(string version) =>
            Path.Combine(Roaming, "Dynamo", "Dynamo Core", version, "packages");

        [Fact]
        public void A_folder_dynamo_has_written_to_is_selected()
        {
            DynamoInstallation installation = DynamoInstallationResolver.Resolve(
                null, "2025", Roaming, new FakeProbe().WithDynamoProfile(RevitPackages("3.2")));

            Assert.Equal(new Version(3, 2), installation.Version);
            Assert.Equal(RevitPackages("3.2"), installation.PackagePath);
            Assert.Equal(DynamoPathConfidence.DynamoProfileExists, installation.Confidence);
        }

        // Audit finding V2: a folder FETCH created on an earlier run must not outrank the folder
        // Dynamo actually uses, or the wrong choice repeats on every startup forever.
        [Fact]
        public void A_real_dynamo_profile_outranks_a_bare_packages_folder_from_an_earlier_sync()
        {
            FakeProbe probe = new FakeProbe()
                .WithPackagesFolder(RevitPackages("3.4"))   // higher priority in the map, but only a folder
                .WithDynamoProfile(RevitPackages("3.0"));   // lower priority, but Dynamo really runs here

            DynamoInstallation installation = DynamoInstallationResolver.Resolve(null, "2025", Roaming, probe);

            Assert.Equal(new Version(3, 0), installation.Version);
            Assert.Equal(DynamoPathConfidence.DynamoProfileExists, installation.Confidence);
        }

        [Fact]
        public void Map_order_breaks_ties_between_equally_plausible_candidates()
        {
            FakeProbe probe = new FakeProbe().WithPackagesFolder(RevitPackages("3.3"), RevitPackages("3.0"));

            DynamoInstallation installation = DynamoInstallationResolver.Resolve(null, "2025", Roaming, probe);

            Assert.Equal(new Version(3, 3), installation.Version);
        }

        [Fact]
        public void A_detected_selector_version_outranks_the_hardcoded_map()
        {
            FakeProbe probe = new FakeProbe().WithDynamoProfile(CorePackages("3.5"), CorePackages("4.0"));

            DynamoInstallation installation = DynamoInstallationResolver.Resolve(
                new Version(3, 5, 1, 0), "2026", Roaming, probe);

            Assert.Equal(new Version(3, 5), installation.Version);
        }

        [Fact]
        public void With_nothing_installed_the_first_candidate_is_guessed_and_flagged()
        {
            DynamoInstallation installation = DynamoInstallationResolver.Resolve(
                null, "2025", Roaming, new FakeProbe());

            Assert.Equal(new Version(3, 4), installation.Version);
            Assert.Equal(DynamoPathConfidence.Guessed, installation.Confidence);
            Assert.False(installation.PackagePathExists);
        }

        [Fact]
        public void An_unsupported_revit_version_resolves_to_nothing()
        {
            Assert.Null(DynamoInstallationResolver.Resolve(null, "2024", Roaming, new FakeProbe()));
        }

        [Fact]
        public void A_selector_version_alone_is_enough_for_an_unmapped_revit_version()
        {
            FakeProbe probe = new FakeProbe().WithDynamoProfile(RevitPackages("3.0"));

            DynamoInstallation installation = DynamoInstallationResolver.Resolve(
                new Version(3, 0, 3, 0), "2024", Roaming, probe);

            Assert.Equal(new Version(3, 0), installation.Version);
            Assert.True(installation.PackagePathExists);
        }

        // Audit finding V1: Revit 2025.4 ships Dynamo 3.4, which the original map omitted.
        [Fact]
        public void Revit_2025_includes_dynamo_34()
        {
            Assert.Contains(
                DynamoVersionResolver.GetVersionCandidates((Version)null, "2025"),
                version => version == new Version(3, 4));
        }

        // Audit finding V1: "27.0" was not a real Dynamo version.
        [Fact]
        public void Revit_2027_no_longer_offers_a_year_style_dynamo_version()
        {
            Assert.DoesNotContain(
                DynamoVersionResolver.GetVersionCandidates((Version)null, "2027"),
                version => version.Major == 27);
        }

        // Audit finding R10: Revit 2026 and 2027 can both land on Dynamo Core 4.0.
        [Fact]
        public void A_dynamo_version_used_by_several_revit_releases_is_recognised_as_shared()
        {
            Assert.True(DynamoVersionResolver.IsVersionSharedAcrossRevitVersions(new Version(4, 0)));
            Assert.False(DynamoVersionResolver.IsVersionSharedAcrossRevitVersions(new Version(3, 0)));
        }
    }
}
