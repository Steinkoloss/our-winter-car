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

            return $"Game build {buildId} has not been tested with WinterMP {ModVersion}. " +
                   "It may still work — report issues on GitHub.";
        }

        public string? ValidatePayload()
        {
            if (ModMeta.ProtocolVersion > 0 && ModMeta.ProtocolVersion != ProtocolVersion)
            {
                return $"Launcher payload protocol v{ModMeta.ProtocolVersion} does not match " +
                       $"manifest v{ProtocolVersion}. Reinstall WinterMP.";
            }

            return null;
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };
    }
}
