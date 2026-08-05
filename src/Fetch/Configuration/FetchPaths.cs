using System;
using System.IO;
using System.Reflection;

namespace Fetch.Configuration
{
    /// <summary>
    /// Well-known locations FETCH reads from and writes to. These are environment facts rather than
    /// state, so unlike the settings and the resolved Dynamo installation they are not carried in
    /// <see cref="FetchContext"/>.
    /// </summary>
    internal static class FetchPaths
    {
        private static readonly string executingDirectory =
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        /// <summary>The folder the add-in was loaded from; also where its settings and log live.</summary>
        internal static string ExecutingDirectory => executingDirectory;

        internal static string UserRoaming =>
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        internal static string TempPath => Path.GetTempPath();

        internal static string DefaultDynamoGraphRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "FETCH Dynamo Graphs");
    }
}
