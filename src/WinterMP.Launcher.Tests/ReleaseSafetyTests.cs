using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using WinterMP.Launcher.Services;
using Xunit;

namespace WinterMP.Launcher.Tests
{
    public sealed class ReleaseSafetyTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "wintermp-release-" + Guid.NewGuid().ToString("N"));
        public ReleaseSafetyTests() { Directory.CreateDirectory(_root); }
        public void Dispose() { Directory.Delete(_root, true); }
        private string Dir(string name) { string path = Path.Combine(_root, name); Directory.CreateDirectory(path); return path; }

        private string Payload(string name)
        {
            string path = Dir(name);
            foreach (string file in ModPayload.RequiredFiles) File.WriteAllText(Path.Combine(path, file), file);
            File.Copy(typeof(CompatManifest).Assembly.Location, Path.Combine(path, "WinterMP.Core.dll"), true);
            File.Copy(typeof(WinterMP.Net.ProtocolInfo).Assembly.Location, Path.Combine(path, "WinterMP.Net.dll"), true);
            string version = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(CompatManifest).Assembly.Location).FileVersion!;
            File.WriteAllText(Path.Combine(path, "wintermp-compat.json"), JsonSerializer.Serialize(new CompatManifest
            {
                ModVersion = version, ProtocolVersion = 94, TargetGameBuildIds = new[] { "23268598" }, ReleaseChannel = "test",
            }));
            return path;
        }

        [Fact]
        public void MixedProtocolOrCorruptedReleaseFilesAreRejected()
        {
            string payload = Payload("payload");
            var manifest = CompatManifest.LoadFrom(payload)!;
            Assert.Null(manifest.ValidatePayload(payload));
            manifest.ProtocolVersion++;
            Assert.Contains("networking DLL", manifest.ValidatePayload(payload));
            manifest.ProtocolVersion--;
            foreach (string file in ModPayload.RequiredFiles.Where(f => f != "wintermp-compat.json"))
                manifest.PayloadSha256[file] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(payload, file))));
            Assert.Null(manifest.ValidatePayload(payload));
            File.AppendAllText(Path.Combine(payload, "sync-catalog.json"), "changed");
            Assert.Contains("differs from this release", manifest.ValidatePayload(payload));
            File.WriteAllText(Path.Combine(payload, "WinterMP.Net.dll"), "broken");
            Assert.Contains("networking DLL", manifest.ValidatePayload(payload));
        }

        [Fact]
        public void InstallationReplacesAllPayloadFilesAndPreservesLocalExtras()
        {
            string source = Payload("payload"), dest = Dir("game/BepInEx/plugins/WinterMP");
            File.WriteAllText(Path.Combine(dest, "notes.txt"), "keep me");
            File.WriteAllText(Path.Combine(dest, "WinterMP.Net.dll"), "old");
            ModPayload.ReplaceDirectory(source, dest, Dir("game/BepInEx"));
            foreach (string file in ModPayload.RequiredFiles)
                Assert.Equal(File.ReadAllBytes(Path.Combine(source, file)), File.ReadAllBytes(Path.Combine(dest, file)));
            Assert.Equal("keep me", File.ReadAllText(Path.Combine(dest, "notes.txt")));
            Assert.Empty(Directory.GetDirectories(Dir("game/BepInEx"), ".wintermp-install-*"));
        }

        [Fact]
        public void MissingOrMislabeledPayloadNeverTouchesTheInstalledVersion()
        {
            string source = Payload("payload"), dest = Dir("installed");
            string core = Path.Combine(dest, "WinterMP.Core.dll"); File.WriteAllText(core, "old version");
            File.Delete(Path.Combine(source, "WinterMP.Net.dll"));
            Assert.Throws<InvalidOperationException>(() => ModPayload.ReplaceDirectory(source, dest, _root));
            Assert.Equal("old version", File.ReadAllText(core));
            File.WriteAllText(Path.Combine(source, "WinterMP.Net.dll"), "net");
            File.WriteAllText(Path.Combine(source, "wintermp-compat.json"), "{\"modVersion\":\"99.0.0\",\"protocolVersion\":94}");
            Assert.Throws<InvalidOperationException>(() => ModPayload.ReplaceDirectory(source, dest, _root));
            Assert.Equal("old version", File.ReadAllText(core));
        }

        [Fact]
        public void StagingFailureLeavesTheEntirePreviousInstallInPlace()
        {
            string source = Payload("payload"), dest = Dir("installed");
            File.WriteAllText(Path.Combine(dest, "WinterMP.Core.dll"), "original core");
            Directory.CreateDirectory(Path.Combine(dest, "WinterMP.Net.dll"));
            var failure = Record.Exception(() => ModPayload.ReplaceDirectory(source, dest, _root));
            Assert.True(failure is IOException || failure is UnauthorizedAccessException);
            Assert.Equal("original core", File.ReadAllText(Path.Combine(dest, "WinterMP.Core.dll")));
            Assert.True(Directory.Exists(Path.Combine(dest, "WinterMP.Net.dll")));
            Assert.Empty(Directory.GetDirectories(_root, ".wintermp-install-*"));
        }

        [Fact]
        public void AppImageUpdatesUseOnlyCompleteNewerCachedPayloads()
        {
            string bundle = Payload("bundle"), cache = Payload("cache");
            string meta = Path.Combine(bundle, "wintermp-compat.json");
            File.WriteAllText(meta, "{\"modVersion\":\"0.0.1\",\"protocolVersion\":1}");
            Assert.Equal(cache, ModPayload.SelectPayloadDirectory(bundle, cache));
            File.WriteAllText(meta, "{\"modVersion\":\"99.0.0\",\"protocolVersion\":94}");
            Assert.Equal(bundle, ModPayload.SelectPayloadDirectory(bundle, cache));
            File.WriteAllText(meta, "{\"modVersion\":\"0.0.1\",\"protocolVersion\":1}");
            File.Delete(Path.Combine(cache, "WinterMP.FastBoot.dll"));
            Assert.Equal(bundle, ModPayload.SelectPayloadDirectory(bundle, cache));
        }

        [Fact]
        public void MissingCompanionDllDoesNotCountAsAnInstalledMod()
        {
            string game = Dir("game"), dest = Payload("game/BepInEx/plugins/WinterMP");
            Dir("game/BepInEx/core"); Dir("game/BepInEx/config");
            File.WriteAllText(Path.Combine(game, "winhttp.dll"), "loader");
            File.WriteAllText(Path.Combine(game, "BepInEx/config/BepInEx.cfg"), "[Preloader.Entrypoint]\nType = MonoBehaviour\n");
            Assert.True(BepInExInstaller.IsFullyInstalled(game));
            File.Delete(Path.Combine(dest, "WinterMP.Net.dll"));
            Assert.False(BepInExInstaller.IsFullyInstalled(game));
            Assert.True(BepInExInstaller.NeedsBundledRepair(game));
        }

        [Fact]
        public void ExplicitGameFolderKeepsItsSteamBuildAndInvalidSelectionCannotFallBack()
        {
            string game = Dir("steamapps/common/My Winter Car"); Dir("steamapps/common/My Winter Car/mywintercar_Data");
            File.WriteAllText(Path.Combine(game, "mywintercar.exe"), "exe");
            File.WriteAllText(Path.Combine(_root, "steamapps/appmanifest_4164420.acf"), "\"installdir\" \"My Winter Car\"\n\"buildid\" \"23268598\"");
            Assert.Equal("23268598", GameLocator.FindInstall(game)!.BuildId);
            Assert.Null(GameLocator.FindInstall(Path.Combine(_root, "does-not-exist")));
        }

        [Fact]
        public void TargetBuildIsDistinctFromCompletedMultiplayerTesting()
        {
            var manifest = new CompatManifest { TargetGameBuildIds = new[] { "23268598" }, ReleaseChannel = "test" };
            Assert.Empty(manifest.TestedGameBuildIds);
            Assert.Null(manifest.ValidateGameBuild("23268598"));
            Assert.Contains("targets game build", manifest.ValidateGameBuild("99999999"));
            Assert.Contains("Could not identify", manifest.ValidateGameBuild(null));
        }

        private string Save()
        {
            string path = Dir("save"); File.WriteAllText(Path.Combine(path, "defaultES2File.txt"), "current save"); return path;
        }

        [Fact]
        public void CorruptOrEmptyBackupCannotEraseTheCurrentSave()
        {
            string save = Save(), backups = Dir("backups"), zip = Path.Combine(_root, "bad.zip");
            File.WriteAllText(zip, "not a zip");
            Assert.Throws<InvalidDataException>(() => SaveBackupService.RestoreBackup(zip, save, backups));
            Assert.Equal("current save", File.ReadAllText(Path.Combine(save, "defaultES2File.txt")));
            File.Delete(zip);
            using (ZipFile.Open(zip, ZipArchiveMode.Create)) { }
            Assert.Throws<InvalidDataException>(() => SaveBackupService.RestoreBackup(zip, save, backups));
            Assert.Equal("current save", File.ReadAllText(Path.Combine(save, "defaultES2File.txt")));
            Assert.Empty(Directory.GetDirectories(_root, ".wintermp-restore-*"));
        }

        [Fact]
        public void EscapingBackupEntryIsRejectedBeforeTheLiveSaveChanges()
        {
            string save = Save(), backups = Dir("backups"), zip = Path.Combine(_root, "escape.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("../../outside.txt").Open())) writer.Write("bad");
            Assert.ThrowsAny<IOException>(() => SaveBackupService.RestoreBackup(zip, save, backups));
            Assert.Equal("current save", File.ReadAllText(Path.Combine(save, "defaultES2File.txt")));
            Assert.False(File.Exists(Path.Combine(_root, "outside.txt")));
        }

        [Fact]
        public void RestoreStagesTheSelectedOldestBackupBeforeSafetyBackupPruning()
        {
            string save = Save(), backups = Dir("backups");
            string original = SaveBackupService.CreateBackup(save, backups);
            for (int i = 0; i < 19; i++) SaveBackupService.CreateBackup(save, backups);
            File.WriteAllText(Path.Combine(save, "defaultES2File.txt"), "latest save");
            File.WriteAllText(Path.Combine(save, "stale.txt"), "not in backup");
            SaveBackupService.RestoreBackup(original, save, backups);
            Assert.Equal("current save", File.ReadAllText(Path.Combine(save, "defaultES2File.txt")));
            Assert.False(File.Exists(Path.Combine(save, "stale.txt")));
            Assert.Equal(20, Directory.GetFiles(backups, "*.zip").Length);
            string safety = Directory.GetFiles(backups, "*.zip").OrderBy(x => x, StringComparer.Ordinal).Last();
            using var archive = ZipFile.OpenRead(safety);
            using var reader = new StreamReader(archive.GetEntry("defaultES2File.txt")!.Open());
            Assert.Equal("latest save", reader.ReadToEnd());
            Assert.Empty(Directory.GetFiles(backups, "*.partial"));
        }

        private string MainData()
        {
            string game = Dir("game"); Dir("game/mywintercar_Data");
            byte[] data = new byte[5000]; Encoding.ASCII.GetBytes("Amistech My Winter Car").CopyTo(data, 32); data[4224] = 1;
            File.WriteAllBytes(Path.Combine(game, "mywintercar_Data/mainData"), data);
            return game;
        }

        [Fact]
        public void ResolutionPatchOnlyTouchesTheVerifiedBuildAndRestoresExactOriginal()
        {
            string game = MainData(), path = Path.Combine(game, "mywintercar_Data/mainData");
            byte[] original = File.ReadAllBytes(path);
            Assert.False(MainDataBootPatch.TryDisableResolutionDialog(game, "99999", out _));
            Assert.False(MainDataBootPatch.TryDisableResolutionDialog(game, out _));
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.True(MainDataBootPatch.TryDisableResolutionDialog(game, "23268598", out _));
            Assert.Equal(0, File.ReadAllBytes(path)[4224]);
            Assert.True(MainDataBootPatch.TryRestoreResolutionDialog(game, out _));
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.False(File.Exists(path + MainDataBootPatch.BackupSuffix));
        }

        [Fact]
        public void RemovingModCannotRestoreObsoleteGameDataOverASteamUpdate()
        {
            string game = MainData(), path = Path.Combine(game, "mywintercar_Data/mainData");
            Assert.True(MainDataBootPatch.TryDisableResolutionDialog(game, "23268598", out _));
            byte[] updated = File.ReadAllBytes(path); updated[4999] = 42; File.WriteAllBytes(path, updated);
            Assert.False(MainDataBootPatch.TryRestoreResolutionDialog(game, out _));
            Assert.Equal(updated, File.ReadAllBytes(path));
        }
    }
}
