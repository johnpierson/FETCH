using System;
using System.IO;
using System.Linq;
using Fetch.Dynamo;
using Xunit;

namespace Fetch.Tests
{
    /// <summary>
    /// Characterization tests for Revit-to-Dynamo version selection.
    /// Tests marked BASELINE document behavior the audit flagged for change.
    /// </summary>
    public class DynamoVersionResolverTests
    {
        private static readonly string Roaming = Path.Combine("C:", "Users", "test", "AppData", "Roaming");

        [Theory]
        [InlineData("2025", new[] { "3.4", "3.3", "3.2", "3.0" })]
        [InlineData("2026", new[] { "4.0", "3.6", "3.5", "3.4" })]
        [InlineData("2027", new[] { "4.1", "4.0" })]
        public void GetVersionCandidates_returns_the_mapped_versions_in_order(string revitVersion, string[] expected)
        {
            var candidates = DynamoVersionResolver
                .GetVersionCandidates((Version)null, revitVersion)
                .Select(v => $"{v.Major}.{v.Minor}")
                .ToArray();

            Assert.Equal(expected, candidates);
        }

        [Fact]
        public void GetVersionCandidates_puts_the_detected_selector_version_first_and_dedupes()
        {
            var candidates = DynamoVersionResolver
                .GetVersionCandidates(new Version(3, 5, 1, 0), "2026")
                .Select(v => $"{v.Major}.{v.Minor}")
                .ToArray();

            Assert.Equal(new[] { "3.5", "4.0", "3.6", "3.4" }, candidates);
        }

        [Fact]
        public void GetVersionCandidates_returns_nothing_for_an_unsupported_revit_version()
        {
            Assert.Empty(DynamoVersionResolver.GetVersionCandidates((Version)null, "2024"));
        }

        // Audit finding V1: Revit 2025.4 ships Dynamo 3.4, which the original map omitted.
        [Fact]
        public void GetVersionCandidates_includes_dynamo_34_for_revit_2025()
        {
            var candidates = DynamoVersionResolver
                .GetVersionCandidates((Version)null, "2025")
                .Select(v => $"{v.Major}.{v.Minor}")
                .ToArray();

            Assert.Contains("3.4", candidates);
        }

        [Fact]
        public void GetPackagePathCandidates_prefers_dynamo_revit_below_version_4()
        {
            var paths = DynamoVersionResolver
                .GetPackagePathCandidates(new Version(3, 3), Roaming, "2025")
                .ToArray();

            Assert.Equal(Path.Combine(Roaming, "Dynamo", "Dynamo Revit", "3.3", "packages"), paths[0]);
            Assert.Equal(Path.Combine(Roaming, "Dynamo", "Dynamo Core", "3.3", "packages"), paths[1]);
        }

        [Fact]
        public void GetPackagePathCandidates_prefers_dynamo_core_from_version_4()
        {
            var paths = DynamoVersionResolver
                .GetPackagePathCandidates(new Version(4, 0), Roaming, "2026")
                .ToArray();

            Assert.Equal(Path.Combine(Roaming, "Dynamo", "Dynamo Core", "4.0", "packages"), paths[0]);
            Assert.Equal(Path.Combine(Roaming, "Dynamo", "Dynamo Revit", "4.0", "packages"), paths[1]);
        }

        [Theory]
        [InlineData(2025, "2025")]
        [InlineData(25, "2025")]
        public void IsRevitSelectorVersion_recognizes_year_style_selector_versions(int major, string revitVersion)
        {
            Assert.True(DynamoVersionResolver.IsRevitSelectorVersion(new Version(major, 0), revitVersion));
        }

        [Fact]
        public void GetPackagePathCandidates_yields_only_the_revit_folder_for_a_selector_style_version()
        {
            var paths = DynamoVersionResolver
                .GetPackagePathCandidates(new Version(25, 0), Roaming, "2025")
                .ToArray();

            Assert.Single(paths);
            Assert.Equal(Path.Combine(Roaming, "Dynamo", "Dynamo Revit", "25.0", "packages"), paths[0]);
        }

        // Audit finding R10: Revit 2026 and 2027 both list Dynamo 4.0 and resolve to the same
        // physical folder. Dynamo's layout makes this unavoidable, so it is detected and warned
        // about rather than fixed; see IsVersionSharedAcrossRevitVersions.
        [Fact]
        public void Revit_2026_and_2027_share_a_dynamo_core_40_packages_folder()
        {
            string path2026 = DynamoVersionResolver.GetPackagePathCandidates(new Version(4, 0), Roaming, "2026").First();
            string path2027 = DynamoVersionResolver.GetPackagePathCandidates(new Version(4, 0), Roaming, "2027").First();

            Assert.Equal(path2026, path2027);
        }
    }
}
