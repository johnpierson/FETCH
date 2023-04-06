using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Fetch
{
    internal class Globals
    {
        internal static string RevitVersion { get; set; }
        internal static string ExecutingPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        public static string UserRoaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        public static string TempPath = Path.GetTempPath();
        public static Version DynamoVersion { get; set; }
        public static bool IsDynamoInMemory { get; set; }
        public static string DefaultDynamoPackagePath { get; set; }
        public static string PackageURL { get; set; }

        public static FileSystemWatcher PackageWatcher { get; set; }
        
    }
}
