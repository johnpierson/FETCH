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

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern long WritePrivateProfileString(string Section, string Key, string Value, string FilePath);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern int GetPrivateProfileString(string Section, string Key, string Default, StringBuilder RetVal, int Size, string FilePath);

        public FetchIniFile()
        {
            path = ResolveSettingsPath();
        }

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

        private static string ResolveSettingsPath()
        {
            foreach (string candidatePath in GetSettingsPathCandidates())
            {
                if (File.Exists(candidatePath))
                {
                    return new FileInfo(candidatePath).FullName;
                }
            }

            return new FileInfo(System.IO.Path.Combine(Globals.ExecutingPath, "FetchSettings.ini")).FullName;
        }

        private static IEnumerable<string> GetSettingsPathCandidates()
        {
            yield return System.IO.Path.Combine(Globals.ExecutingPath, "FetchSettings.ini");

            if (string.IsNullOrWhiteSpace(Globals.RevitVersion))
            {
                yield break;
            }

            foreach (string addinRoot in GetRevitAddinRoots())
            {
                yield return System.IO.Path.Combine(addinRoot, Globals.RevitVersion, "FETCH", "FetchSettings.ini");
                yield return System.IO.Path.Combine(addinRoot, Globals.RevitVersion, "Fetch", "FetchSettings.ini");
            }
        }

        private static IEnumerable<string> GetRevitAddinRoots()
        {
            yield return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Autodesk",
                "Revit",
                "Addins");

            yield return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk",
                "Revit",
                "Addins");
        }
    }
}
