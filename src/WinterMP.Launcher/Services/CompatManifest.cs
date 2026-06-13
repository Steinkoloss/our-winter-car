using System.IO;
using System.Text.Json;

namespace WinterMP.Launcher.Services
{
    public sealed class CompatManifest
    {
        public string ModVersion { get; set; } = string.Empty;
        public ushort ProtocolVersion { get; set; }
        public string[] TestedGameBuildIds { get; set; } = Array.Empty<string>();

        public static CompatManifest? Load()
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

        public string? ValidateGameBuild(string? buildId)
        {
            if (string.IsNullOrEmpty(buildId)) return null;
            if (TestedGameBuildIds.Length == 0) return null;

            foreach (string tested in TestedGameBuildIds)
            {
                if (string.Equals(tested, buildId, StringComparison.Ordinal))
                    return null;
            }

            return $"Game build {buildId} has not been tested with {Branding.ProductName} {ModVersion}. " +
                   "It may still work — report issues on GitHub.";
        }

        public string? ValidatePayload()
        {
            string? dllVersion = ReadCoreDllFileVersion();
            if (dllVersion != null
                && !string.IsNullOrWhiteSpace(ModVersion)
                && !string.Equals(dllVersion, ModVersion, StringComparison.OrdinalIgnoreCase))
            {
                return $"Payload Core.dll is v{dllVersion} but manifest says v{ModVersion}. Reinstall {Branding.ProductName}.";
            }

            return null;
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
