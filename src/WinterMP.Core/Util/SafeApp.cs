using System.Reflection;
using UnityEngine;

namespace WinterMP.Core.Util
{
    /// <summary>
    /// Reflection-guarded access to Application properties that may not exist on
    /// Unity 5.0 (e.g. Application.version was added later). Never throws.
    /// </summary>
    internal static class SafeApp
    {
        public static string GameVersion => GetStaticString("version");
        public static string ProductName => GetStaticString("productName");
        public static string CompanyName => GetStaticString("companyName");

        private static string GetStaticString(string property)
        {
            try
            {
                var prop = typeof(Application).GetProperty(property, BindingFlags.Public | BindingFlags.Static);
                return prop?.GetValue(null, null) as string ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
