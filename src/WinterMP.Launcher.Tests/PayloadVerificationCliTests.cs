using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using WinterMP.Launcher.Services;
using Xunit;

namespace WinterMP.Launcher.Tests
{
    public sealed class PayloadVerificationCliTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "wintermp-verify-" + Guid.NewGuid().ToString("N"));
        public PayloadVerificationCliTests() { Directory.CreateDirectory(_root); }
        public void Dispose() { Directory.Delete(_root, true); }

        [Fact]
        public void MissingOrConflictingVerificationArgumentsCannotFallThroughToInstallationOrUi()
        {
            Assert.False(CliPayloadVerifier.TryRun(Array.Empty<string>(), out _));
            foreach (var args in new[] {
                new[] { "--verify-payload" },
                new[] { "--verify-payload", _root, "--install-mod" },
                new[] { "--install-mod", "--verify-payload", _root },
                new[] { "--verify-payload", _root } })
            {
                Assert.True(CliPayloadVerifier.TryRun(args, out int code));
                Assert.NotEqual(0, code);
            }
            Assert.Empty(Directory.GetFileSystemEntries(_root));
        }

        [Fact]
        public void VerificationUsesProductionProtocolAndHashesWithoutWritingPayload()
        {
            // Portable PE fixtures: these are not claimed as actual game plugins.
            foreach (string name in new[] { "WinterMP.Core.dll", "WinterMP.FastBoot.dll" })
                File.Copy(typeof(CompatManifest).Assembly.Location, Path.Combine(_root, name));
            File.Copy(typeof(WinterMP.Net.ProtocolInfo).Assembly.Location, Path.Combine(_root, "WinterMP.Net.dll"));
            File.WriteAllText(Path.Combine(_root, "sync-catalog.json"), "{}");
            var manifest = new CompatManifest {
                ModVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(CompatManifest).Assembly.Location).FileVersion!,
                ProtocolVersion = WinterMP.Net.ProtocolInfo.Version, ReleaseChannel = "test",
            };
            foreach (string name in ModPayload.RequiredFiles.Where(n => n != "wintermp-compat.json"))
                manifest.PayloadSha256[name] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(_root, name))));
            string file = Path.Combine(_root, "wintermp-compat.json");
            File.WriteAllText(file, JsonSerializer.Serialize(manifest));
            byte[] before = File.ReadAllBytes(file);
            Assert.True(CliPayloadVerifier.TryRun(new[] { "--verify-payload", _root }, out int code));
            Assert.Equal(0, code);
            Assert.Equal(before, File.ReadAllBytes(file));
            manifest.ProtocolVersion++;
            File.WriteAllText(file, JsonSerializer.Serialize(manifest));
            Assert.True(CliPayloadVerifier.TryRun(new[] { "--verify-payload", _root }, out code));
            Assert.NotEqual(0, code);
            manifest.ProtocolVersion--;
            manifest.PayloadSha256.Clear();
            File.WriteAllText(file, JsonSerializer.Serialize(manifest));
            Assert.True(CliPayloadVerifier.TryRun(new[] { "--verify-payload", _root }, out code));
            Assert.NotEqual(0, code);
        }
    }
}
