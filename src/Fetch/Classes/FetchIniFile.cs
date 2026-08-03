using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Fetch.Classes
{
    internal class FetchIniFile
    {
        private const int MaxIniValueLength = 4096;
        private readonly string path;
        private readonly string exe = Assembly.GetExecutingAssembly().GetName().Name;

        public string FilePath => path;

        public FetchIniFile(string executingDirectory, string revitVersion)
        {
            path = ResolveSettingsPath(executingDirectory, revitVersion);
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern long WritePrivateProfileString(string Section, string Key, string Value, string FilePath);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern int GetPrivateProfileString(string Section, string Key, string Default, StringBuilder RetVal, int Size, string FilePath);

        public string Read(string Key, string Section = null)
        {
            var RetVal = new StringBuilder(MaxIniValueLength);
            GetPrivateProfileString(Section ?? exe, Key, "", RetVal, MaxIniValueLength, path);
            return RetVal.ToString();
        }

        public void Write(string Key, string Value, string Section = null)
        {
            WritePrivateProfileString(Section ?? exe, Key, Value, path);
        }

        public void DeleteKey(string Key, string Section = null)
        {
            Write(Key, null, Section ?? exe);
        }

        public void DeleteSection(string Section = null)
        {
            Write(null, null, Section ?? exe);
        }

        public bool KeyExists(string Key, string Section = null)
        {
            return Read(Key, Section).Length > 0;
        }

        internal static string ResolveSettingsPath(string executingDirectory, string revitVersion)
        {
            foreach (string candidatePath in GetSettingsPathCandidates(executingDirectory, revitVersion))
            {
                if (File.Exists(candidatePath))
                {
                    return new FileInfo(candidatePath).FullName;
                }
            }

            return new FileInfo(System.IO.Path.Combine(executingDirectory, "FetchSettings.ini")).FullName;
        }

        internal static IEnumerable<string> GetSettingsPathCandidates(string executingDirectory, string revitVersion)
        {
            yield return System.IO.Path.Combine(executingDirectory, "FetchSettings.ini");

            if (string.IsNullOrWhiteSpace(revitVersion))
            {
                yield break;
            }

            foreach (string addinRoot in GetRevitAddinRoots())
            {
                yield return System.IO.Path.Combine(addinRoot, revitVersion, "FETCH", "FetchSettings.ini");
                yield return System.IO.Path.Combine(addinRoot, revitVersion, "Fetch", "FetchSettings.ini");
            }
        }

        /// <summary>
        /// Per-user settings are searched before machine-wide ones.
        ///
        /// The ProgramData location is only still consulted so that installations left by earlier,
        /// machine-wide releases keep working. It is deliberately last: the default ACL on
        /// ProgramData lets any standard user create files there, so a machine-wide settings file
        /// must never override the one belonging to the user running Revit.
        /// </summary>
        private static IEnumerable<string> GetRevitAddinRoots()
        {
            yield return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk",
                "Revit",
                "Addins");

            yield return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Autodesk",
                "Revit",
                "Addins");
        }
    }
}
