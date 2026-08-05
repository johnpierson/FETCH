using System;
using Fetch.Classes;

namespace Fetch.Configuration
{
    /// <summary>Reads FetchSettings.ini into a <see cref="FetchSettings"/>.</summary>
    internal static class SettingsReader
    {
        internal const string SectionName = "Settings";

        internal static FetchSettings Read(string executingDirectory, string revitVersion)
        {
            FetchIniFile iniFile = new FetchIniFile(executingDirectory, revitVersion);

            string graphRootPath = iniFile.Read("GraphPath", SectionName);

            return new FetchSettings(
                iniFile.FilePath,
                iniFile.Read("Path", SectionName).Trim(),
                string.IsNullOrWhiteSpace(graphRootPath)
                    ? FetchPaths.DefaultDynamoGraphRoot
                    : graphRootPath.Trim(),
                iniFile.Read("Sha256", SectionName).Trim(),
                string.Equals(iniFile.Read("AllowInsecureHttp", SectionName).Trim(), "true", StringComparison.OrdinalIgnoreCase));
        }
    }
}
