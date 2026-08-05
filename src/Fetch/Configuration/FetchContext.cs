using System.IO;
using Fetch.Dynamo;

namespace Fetch.Configuration
{
    /// <summary>
    /// Everything a sync needs, resolved once at startup and then read-only.
    ///
    /// This replaces the mutable statics the add-in previously shared, which required callers to
    /// know an undocumented ordering: the Revit version had to be assigned before settings were
    /// read, because settings resolution depended on it. Constructing this object makes that
    /// ordering explicit and impossible to get wrong.
    /// </summary>
    internal sealed class FetchContext
    {
        internal FetchContext(string revitVersion, DynamoInstallation dynamo, FetchSettings settings)
        {
            RevitVersion = revitVersion;
            Dynamo = dynamo;
            Settings = settings;
        }

        internal string RevitVersion { get; }

        internal DynamoInstallation Dynamo { get; }

        internal FetchSettings Settings { get; }

        /// <summary>Where deployed graphs for the running Revit version are installed.</summary>
        internal string GraphDestination => Path.Combine(Settings.GraphRootPath, RevitVersion);
    }
}
