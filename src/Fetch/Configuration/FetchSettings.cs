using System;

namespace Fetch.Configuration
{
    /// <summary>
    /// The contents of the [Settings] section of FetchSettings.ini, read once at startup.
    /// </summary>
    internal sealed class FetchSettings
    {
        internal FetchSettings(
            string settingsPath,
            string packageSource,
            string graphRootPath,
            string packageSha256,
            bool allowInsecureHttp)
        {
            SettingsPath = settingsPath;
            PackageSource = packageSource;
            GraphRootPath = graphRootPath;
            PackageSha256 = packageSha256;
            AllowInsecureHttp = allowInsecureHttp;
        }

        /// <summary>The settings file these values came from, quoted back in error messages.</summary>
        internal string SettingsPath { get; }

        /// <summary>A local/UNC folder or a package zip URL.</summary>
        internal string PackageSource { get; }

        /// <summary>Root beneath which per-Revit-version graph folders are deployed.</summary>
        internal string GraphRootPath { get; }

        /// <summary>Optional expected SHA-256 of the downloaded package zip.</summary>
        internal string PackageSha256 { get; }

        /// <summary>Opt-in to plain-HTTP package sources, which can be tampered with in transit.</summary>
        internal bool AllowInsecureHttp { get; }
    }
}
