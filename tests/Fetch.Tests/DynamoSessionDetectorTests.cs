using Fetch.Sync;
using Xunit;

namespace Fetch.Tests
{
    /// <summary>
    /// Audit findings R3 and R4: the guard against syncing while another session has Dynamo loaded
    /// was computed but never consumed, and the process inspection behind it could throw out of the
    /// add-in's startup path.
    /// </summary>
    [Collection("LogState")]
    public class DynamoSessionDetectorTests
    {
        [Theory]
        [InlineData(@"C:\Program Files\Autodesk\Revit 2025\Revit.exe", "2025", true)]
        [InlineData(@"C:\Program Files\Autodesk\Revit 2026\Revit.exe", "2025", false)]
        [InlineData(@"C:\Program Files\Autodesk\REVIT 2025\Revit.exe", "2025", true)]
        [InlineData(null, "2025", false)]
        [InlineData("", "2025", false)]
        [InlineData(@"C:\Program Files\Autodesk\Revit 2025\Revit.exe", null, false)]
        [InlineData(@"C:\Program Files\Autodesk\Revit 2025\Revit.exe", "", false)]
        public void IsMatchingRevitProcess_matches_only_the_running_version(string path, string revitVersion, bool expected)
        {
            Assert.Equal(expected, DynamoSessionDetector.IsMatchingRevitProcess(path, revitVersion));
        }

        [Theory]
        [InlineData(@"C:\Program Files\Dynamo\DynamoCore.dll", true)]
        [InlineData(@"C:\...\DynamoRevitDS.dll", true)]
        [InlineData(@"C:\...\dynamo.units.dll", true)]
        [InlineData(@"C:\Windows\System32\kernel32.dll", false)]
        [InlineData(null, false)]
        [InlineData("", false)]
        public void IsDynamoModule_detects_dynamo_assemblies(string moduleFileName, bool expected)
        {
            Assert.Equal(expected, DynamoSessionDetector.IsDynamoModule(moduleFileName));
        }

        /// <summary>
        /// The real enumeration runs against whatever is on this machine. The guarantee under test
        /// is that it answers rather than throwing, since it gates add-in startup.
        /// </summary>
        [Fact]
        public void IsDynamoLoadedInAnotherSession_never_throws()
        {
            Assert.Null(Record.Exception(() => DynamoSessionDetector.IsDynamoLoadedInAnotherSession("2025")));
        }

        [Fact]
        public void IsDynamoLoadedInAnotherSession_tolerates_a_missing_version()
        {
            Assert.False(DynamoSessionDetector.IsDynamoLoadedInAnotherSession(null));
            Assert.False(DynamoSessionDetector.IsDynamoLoadedInAnotherSession(string.Empty));
        }
    }
}
