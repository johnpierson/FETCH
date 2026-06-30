using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Analysis;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace Fetch.Utilities
{
    public static class Utilities
    {
        private static readonly HttpClient InternetCheckClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        public static bool CheckForInternetConnection()
        {
            try
            {
                using (HttpResponseMessage response = InternetCheckClient.GetAsync("http://google.com/generate_204").GetAwaiter().GetResult())
                {
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }
    }
    public static class StringUtilities
    {
        public static string SimplifyString(this string str)
        {
            return str.ToLower().Replace(" ", "");
        }
    }
    
}
