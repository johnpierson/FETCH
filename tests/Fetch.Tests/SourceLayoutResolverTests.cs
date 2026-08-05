using System.IO;
using System.Linq;
using Fetch.Sources;
using Xunit;

namespace Fetch.Tests
{
    /// <summary>
    /// Characterization tests: these lock in the layout resolution behavior that ships today,
    /// including the fallbacks identified as defects in the audit. Tests marked BASELINE document
    /// behavior that is expected to change; update them deliberately when the fix lands.
    /// </summary>
    public class SourceLayoutResolverTests
    {
        [Theory]
        [InlineData("2025", "2025", true)]
        [InlineData("Revit 2025", "2025", true)]
        [InlineData("revit2025", "2025", true)]
        [InlineData("R25", "2025", true)]
        [InlineData("r-25", "2025", true)]
        [InlineData("R_25", "2025", true)]
        [InlineData("2026", "2025", false)]
        [InlineData("R26", "2025", false)]
        [InlineData("Packages", "2025", false)]
        public void IsVersionDirectoryName_matches_supported_aliases(string directoryName, string revitVersion, bool expected)
        {
            Assert.Equal(expected, SourceLayoutResolver.IsVersionDirectoryName(directoryName, revitVersion));
        }

        [Theory]
        [InlineData("2025", true)]
        [InlineData("Revit 2027", true)]
        [InlineData("R25", true)]
        [InlineData("99", true)]
        [InlineData("2100", false)]
        [InlineData("Packages", false)]
        [InlineData("Dynamo Graphs", false)]
        public void IsAnyRevitVersionDirectoryName_recognizes_year_and_short_forms(string directoryName, bool expected)
        {
            Assert.Equal(expected, SourceLayoutResolver.IsAnyRevitVersionDirectoryName(directoryName));
        }

        [Fact]
        public void Resolve_finds_packages_and_graphs_under_matching_version_folder()
        {
            using (var temp = new TempDirectory())
            {
                string packages = temp.CreateDirectory("2025", "Packages");
                string graphs = temp.CreateDirectory("2025", "Dynamo Graphs");
                temp.CreateDirectory("2026", "Packages");

                SourceLayout layout = SourceLayoutResolver.Resolve(temp.Path, "2025");

                Assert.Equal(packages, layout.PackageSourceDirectory);
                Assert.Equal(graphs, layout.GraphSourceDirectory);
            }
        }

        [Theory]
        [InlineData("Revit 2025")]
        [InlineData("R25")]
        public void Resolve_accepts_version_folder_aliases(string versionFolderName)
        {
            using (var temp = new TempDirectory())
            {
                string packages = temp.CreateDirectory(versionFolderName, "Packages");

                SourceLayout layout = SourceLayoutResolver.Resolve(temp.Path, "2025");

                Assert.Equal(packages, layout.PackageSourceDirectory);
            }
        }

        [Theory]
        [InlineData("Package")]
        [InlineData("Packages")]
        public void Resolve_accepts_package_folder_aliases(string packageFolderName)
        {
            using (var temp = new TempDirectory())
            {
                string packages = temp.CreateDirectory("2025", packageFolderName);

                SourceLayout layout = SourceLayoutResolver.Resolve(temp.Path, "2025");

                Assert.Equal(packages, layout.PackageSourceDirectory);
            }
        }

        [Theory]
        [InlineData("Dynamo Graphs")]
        [InlineData("DynamoGraphs")]
        [InlineData("Graphs")]
        public void Resolve_accepts_graph_folder_aliases(string graphFolderName)
        {
            using (var temp = new TempDirectory())
            {
                temp.CreateDirectory("2025", "Packages");
                string graphs = temp.CreateDirectory("2025", graphFolderName);

                SourceLayout layout = SourceLayoutResolver.Resolve(temp.Path, "2025");

                Assert.Equal(graphs, layout.GraphSourceDirectory);
            }
        }

        [Fact]
        public void Resolve_descends_a_single_wrapper_folder_from_an_extracted_zip()
        {
            using (var temp = new TempDirectory())
            {
                string packages = temp.CreateDirectory("Deployment", "2025", "Packages");

                SourceLayout layout = SourceLayoutResolver.Resolve(temp.Path, "2025");

                Assert.Equal(packages, layout.PackageSourceDirectory);
            }
        }

        [Fact]
        public void Resolve_throws_when_deployment_has_other_version_folders_but_not_ours()
        {
            using (var temp = new TempDirectory())
            {
                temp.CreateDirectory("2026", "Packages");
                temp.CreateDirectory("2027", "Packages");

                var exception = Assert.Throws<DirectoryNotFoundException>(
                    () => SourceLayoutResolver.Resolve(temp.Path, "2025"));

                Assert.Contains("Revit 2025", exception.Message);
            }
        }

        [Fact]
        public void Resolve_uses_a_bare_package_folder_directly()
        {
            using (var temp = new TempDirectory())
            {
                string packages = temp.CreateDirectory("Packages");

                SourceLayout layout = SourceLayoutResolver.Resolve(temp.Path, "2025");

                Assert.Equal(packages, layout.PackageSourceDirectory);
                Assert.Null(layout.GraphSourceDirectory);
            }
        }

        // Audit finding F3: an unrecognized layout still resolves to the root, but it is now marked
        // as a guess. PackageSync refuses to act on a guess that is not package-shaped.
        [Fact]
        public void Resolve_marks_the_unrecognized_layout_fallback_as_a_guess()
        {
            using (var temp = new TempDirectory())
            {
                temp.CreateDirectory("SomeFolder");
                temp.CreateFile("readme.txt");

                SourceLayout layout = SourceLayoutResolver.Resolve(temp.Path, "2025");

                Assert.Equal(temp.Path, layout.PackageSourceDirectory);
                Assert.False(layout.PackageSourceIsExplicit);
                Assert.Null(layout.GraphSourceDirectory);
            }
        }

        [Fact]
        public void Resolve_marks_a_named_package_folder_as_explicit()
        {
            using (var temp = new TempDirectory())
            {
                temp.CreateDirectory("2025", "Packages");

                SourceLayout layout = SourceLayoutResolver.Resolve(temp.Path, "2025");

                Assert.True(layout.PackageSourceIsExplicit);
            }
        }

        // Audit finding F3: re-zipped deployments nest the payload under several wrapper folders.
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        public void Resolve_descends_multiple_single_child_wrapper_folders(int wrapperDepth)
        {
            using (var temp = new TempDirectory())
            {
                string relative = string.Join("\\", Enumerable.Range(0,wrapperDepth).Select(i => "Wrapper" + i));
                string packages = temp.CreateDirectory(relative, "2025", "Packages");

                SourceLayout layout = SourceLayoutResolver.Resolve(temp.Path, "2025");

                Assert.Equal(packages, layout.PackageSourceDirectory);
                Assert.True(layout.PackageSourceIsExplicit);
            }
        }

        [Fact]
        public void Resolve_stops_descending_past_the_wrapper_depth_limit()
        {
            using (var temp = new TempDirectory())
            {
                string relative = string.Join("\\", Enumerable.Range(0,SourceLayoutResolver.MaxWrapperDepth + 3).Select(i => "Wrapper" + i));
                temp.CreateDirectory(relative, "2025", "Packages");

                SourceLayout layout = SourceLayoutResolver.Resolve(temp.Path, "2025");

                Assert.Equal(temp.Path, layout.PackageSourceDirectory);
                Assert.False(layout.PackageSourceIsExplicit);
            }
        }

        // Audit finding F7: a version folder with no Packages child yields a null path. PackageSync
        // now turns that into a descriptive error instead of an empty-path message.
        [Fact]
        public void Resolve_returns_null_package_path_when_version_folder_lacks_packages()
        {
            using (var temp = new TempDirectory())
            {
                temp.CreateDirectory("2025", "Dynamo Graphs");

                SourceLayout layout = SourceLayoutResolver.Resolve(temp.Path, "2025");

                Assert.Null(layout.PackageSourceDirectory);
                Assert.NotNull(layout.GraphSourceDirectory);
            }
        }

    }
}
