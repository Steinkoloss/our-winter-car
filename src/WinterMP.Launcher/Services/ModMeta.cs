using System.Text.Json;

namespace WinterMP.Launcher.Services
{
    /// <summary>Reads mod/protocol versions from the bundled payload manifest (no assembly loads — keeps DLLs unlocked for in-place updates).</summary>
    public static class ModMeta
    {
        public static string ModVersion
        {
            get
            {
                CompatManifest? manifest = TryLoadManifest();
                if (manifest != null && !string.IsNullOrWhiteSpace(manifest.ModVersion))
                    return manifest.ModVersion;

                return ReadCoreDllFileVersion() ?? ModPayload.LauncherVersion;
            }
        }

        public static ushort ProtocolVersion
        {
            get
            {
                CompatManifest? manifest = TryLoadManifest();
                if (manifest != null && manifest.ProtocolVersion > 0)
                    return manifest.ProtocolVersion;

                return 0;
            }
        }

        private static CompatManifest? TryLoadManifest()
        {
            string path = Path.Combine(ModPayload.PayloadDir, "wintermp-compat.json");
            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<CompatManifest>(json, JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        private static string? ReadCoreDllFileVersion()
        {
            string dll = Path.Combine(ModPayload.PayloadDir, "WinterMP.Core.dll");
            if (!File.Exists(dll)) return null;

            try
            {
                return System.Diagnostics.FileVersionInfo.GetVersionInfo(dll).FileVersion;
            }
            catch
            {
                return null;
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };
    }
}
