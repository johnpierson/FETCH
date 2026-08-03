using System;
using System.IO;
using System.Linq;
using Fetch.Classes;
using Xunit;

namespace Fetch.Tests
{
    /// <summary>
    /// Documents where FETCH looks for its settings file. The order matters: a machine-wide file
    /// takes precedence over the per-user one when no file sits beside the add-in, which the audit
    /// flagged as both undocumented (U4) and a potential local escalation route (S2).
    /// </summary>
    public class SettingsPathResolutionTests
    {
        [Fact]
        public void The_folder_beside_the_addin_is_checked_first()
        {
            string[] candidates = FetchIniFile.GetSettingsPathCandidates(@"C:\addin", "2025").ToArray();

            Assert.Equal(Path.Combine(@"C:\addin", "FetchSettings.ini"), candidates[0]);
        }

        /// <summary>
        /// Audit finding S2: the default ACL on ProgramData lets any standard user create files
        /// there, so a machine-wide settings file must never win over the user's own.
        /// </summary>
        [Fact]
        public void Per_user_settings_are_checked_before_machine_wide_settings()
        {
            string[] candidates = FetchIniFile.GetSettingsPathCandidates(@"C:\addin", "2025").ToArray();

            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            int firstMachineWide = Array.FindIndex(candidates, c => c.StartsWith(programData, StringComparison.OrdinalIgnoreCase));
            int firstPerUser = Array.FindIndex(candidates, c => c.StartsWith(appData, StringComparison.OrdinalIgnoreCase));

            Assert.True(firstMachineWide >= 0, "Expected a ProgramData candidate.");
            Assert.True(firstPerUser >= 0, "Expected an AppData candidate.");
            Assert.True(firstPerUser < firstMachineWide, "AppData is searched before ProgramData.");
        }

        [Fact]
        public void Both_casings_of_the_install_folder_are_considered()
        {
            string[] candidates = FetchIniFile.GetSettingsPathCandidates(@"C:\addin", "2025").ToArray();

            Assert.Contains(candidates, c => c.Contains(Path.Combine("2025", "FETCH")));
            Assert.Contains(candidates, c => c.Contains(Path.Combine("2025", "Fetch")));
        }

        [Fact]
        public void Without_a_revit_version_only_the_addin_folder_is_considered()
        {
            Assert.Single(FetchIniFile.GetSettingsPathCandidates(@"C:\addin", null));
            Assert.Single(FetchIniFile.GetSettingsPathCandidates(@"C:\addin", "   "));
        }

        [Fact]
        public void An_absent_settings_file_resolves_to_the_addin_folder()
        {
            // A Revit version that cannot be installed, so no machine-wide or per-user candidate
            // can exist on the machine running the tests.
            string resolved = FetchIniFile.ResolveSettingsPath(Path.GetTempPath(), "9999");

            Assert.Equal(
                new FileInfo(Path.Combine(Path.GetTempPath(), "FetchSettings.ini")).FullName,
                resolved);
        }

        /// <summary>
        /// Confirms the precedence that made the machine-wide location a finding: when no file sits
        /// beside the add-in, a ProgramData file wins over the per-user one the installer writes.
        /// </summary>
        [Fact]
        public void A_machine_wide_file_is_preferred_over_the_addin_folder_only_when_that_is_absent()
        {
            using (var temp = new TempDirectory())
            {
                string beside = Path.Combine(temp.Path, "FetchSettings.ini");
                File.WriteAllText(beside, "[Settings]\r\nPath=local\r\n");

                Assert.Equal(new FileInfo(beside).FullName, FetchIniFile.ResolveSettingsPath(temp.Path, "2025"));
            }
        }
    }
}
