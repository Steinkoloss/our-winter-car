using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace WinterMP.Launcher.Services
{
    /// <summary>Read-only package validation before any installer, settings or UI path.</summary>
    internal static class CliPayloadVerifier
    {
        internal static bool TryRun(string[] args, out int exitCode)
        {
            exitCode = 0;
            if (!args.Contains("--verify-payload")) return false;
            exitCode = 2;
            if (args.Length != 2 || args[0] != "--verify-payload" || args[1].StartsWith("--"))
            {
                Console.Error.WriteLine("Usage: --verify-payload <directory> (no installation options)");
                return true;
            }
            try
            {
                string directory = Path.GetFullPath(args[1]);
                var manifest = CompatManifest.LoadFrom(directory);
                if (manifest == null) throw new InvalidDataException("Compatibility manifest missing or invalid.");
                if (manifest.PayloadSha256 == null || manifest.PayloadSha256.Count != ModPayload.RequiredFiles.Length - 1)
                    throw new InvalidDataException("Package verification requires every payload content hash.");
                string? error = manifest.ValidatePayload(directory);
                if (error != null) throw new InvalidDataException(error);
                var assemblies = new Dictionary<string, object>();
                foreach (string file in ModPayload.RequiredFiles.Where(f => f.EndsWith(".dll")))
                {
                    string path = Path.Combine(directory, file);
                    assemblies[file] = new {
                        name = AssemblyName.GetAssemblyName(path).Name,
                        version = FileVersionInfo.GetVersionInfo(path).FileVersion,
                    };
                }
                Console.WriteLine(JsonSerializer.Serialize(new {
                    status = "PASS", manifest.ModVersion, manifest.ProtocolVersion, assemblies,
                    payloadSha256 = manifest.PayloadSha256,
                    evidenceLevel = "read-only package metadata and hashes; no install or game execution",
                }));
                exitCode = 0;
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException
                || e is ArgumentException || e is BadImageFormatException || e is JsonException)
            {
                Console.Error.WriteLine("Payload verification failed: " + e.Message);
            }
            return true;
        }
    }
}
