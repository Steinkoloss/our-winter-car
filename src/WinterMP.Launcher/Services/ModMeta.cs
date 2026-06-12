using System.IO;
using System.Reflection;

namespace WinterMP.Launcher.Services
{
    /// <summary>Reads mod/protocol versions from the bundled plugin DLLs.</summary>
    public static class ModMeta
    {
        public static string ModVersion
        {
            get
            {
                string dll = Path.Combine(ModPayload.PayloadDir, "WinterMP.Core.dll");
                if (!File.Exists(dll)) return ModPayload.LauncherVersion;
                try
                {
                    return System.Diagnostics.FileVersionInfo.GetVersionInfo(dll).FileVersion
                        ?? ModPayload.LauncherVersion;
                }
                catch
                {
                    return ModPayload.LauncherVersion;
                }
            }
        }

        public static ushort ProtocolVersion
        {
            get
            {
                try
                {
                    string netDll = Path.Combine(ModPayload.PayloadDir, "WinterMP.Net.dll");
                    if (!File.Exists(netDll)) return 0;
                    var asm = Assembly.LoadFrom(netDll);
                    var type = asm.GetType("WinterMP.Net.ProtocolInfo");
                    if (type == null) return 0;
                    var versionField = type.GetField("Version", BindingFlags.Public | BindingFlags.Static);
                    if (versionField == null) return 0;
                    return Convert.ToUInt16(versionField.GetValue(null));
                }
                catch
                {
                    return 0;
                }
            }
        }
    }
}
