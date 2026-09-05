using System.Text.Json;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace WinterMP.Launcher.Services
{
    public sealed class CompatManifest
    {
        public string ModVersion { get; set; } = string.Empty;
        public ushort ProtocolVersion { get; set; }
        public string[] TestedGameBuildIds { get; set; } = Array.Empty<string>();
        public string[] TargetGameBuildIds { get; set; } = Array.Empty<string>();
        public string ReleaseChannel { get; set; } = "stable";
        public string GameVersion { get; set; } = string.Empty;
        public Dictionary<string, string> PayloadSha256 { get; set; } = new();

        public static CompatManifest? Load()
        {
            return LoadFrom(ModPayload.PayloadDir);
        }

        internal static CompatManifest? LoadFrom(string payloadDir)
        {
            string path = Path.Combine(payloadDir, "wintermp-compat.json");
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
            var builds = TargetGameBuildIds.Length > 0 ? TargetGameBuildIds : TestedGameBuildIds;
            if (builds.Length == 0) return null;
            if (string.IsNullOrEmpty(buildId))
                return "Could not identify the game build. Check Steam for updates before testing.";

            foreach (string tested in builds)
            {
                if (string.Equals(tested, buildId, StringComparison.Ordinal))
                    return null;
            }

            return $"This package targets game build {string.Join(", ", builds)}; your game is {buildId}. " +
                   "Use the matching tester package and Steam game version.";
        }

        public string? ValidatePayload()
            => ValidatePayload(ModPayload.PayloadDir);

        internal string? ValidatePayload(string payloadDir)
        {
            foreach (string file in ModPayload.RequiredFiles)
                if (!File.Exists(Path.Combine(payloadDir, file))) return $"Mod package is missing {file}.";
            string? dllVersion = ReadCoreDllFileVersion(payloadDir);
            if (dllVersion == null || string.IsNullOrWhiteSpace(ModVersion) || ProtocolVersion == 0)
                return "Mod package version could not be verified. Reinstall the tester package.";
            if (ReadProtocolVersion(Path.Combine(payloadDir, "WinterMP.Net.dll")) != ProtocolVersion)
                return "Mod networking DLL does not match the package protocol. Reinstall the tester package.";
            if (dllVersion != null
                && !string.IsNullOrWhiteSpace(ModVersion)
                && !ModVersionHelper.VersionsMatch(dllVersion, ModVersion))
            {
                return $"Payload Core.dll is v{dllVersion} but manifest says v{ModVersion}. Reinstall {Branding.ProductName}.";
            }

            if (PayloadSha256.Count > 0)
            {
                foreach (string file in ModPayload.RequiredFiles.Where(f => f != "wintermp-compat.json"))
                {
                    try
                    {
                        string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(payloadDir, file))));
                        if (!PayloadSha256.TryGetValue(file, out string? expected)
                            || !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                            return $"Mod file {file} differs from this release. Install / Repair the tester package.";
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    { return $"Could not verify {file}: {e.Message}"; }
                }
            }
            return null;
        }

        private static ushort? ReadProtocolVersion(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var pe = new PEReader(stream);
                var metadata = pe.GetMetadataReader();
                foreach (var handle in metadata.TypeDefinitions)
                {
                    var type = metadata.GetTypeDefinition(handle);
                    if (metadata.GetString(type.Namespace) != "WinterMP.Net" || metadata.GetString(type.Name) != "ProtocolInfo") continue;
                    foreach (var fieldHandle in type.GetFields())
                    {
                        var field = metadata.GetFieldDefinition(fieldHandle);
                        if (metadata.GetString(field.Name) != "Version" || field.GetDefaultValue().IsNil) continue;
                        var constant = metadata.GetConstant(field.GetDefaultValue());
                        if (constant.TypeCode == ConstantTypeCode.UInt16)
                            return metadata.GetBlobReader(constant.Value).ReadUInt16();
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is BadImageFormatException || e is InvalidOperationException || e is UnauthorizedAccessException) { }
            return null;
        }

        private static string? ReadCoreDllFileVersion(string payloadDir)
        {
            string dll = Path.Combine(payloadDir, "WinterMP.Core.dll");
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
