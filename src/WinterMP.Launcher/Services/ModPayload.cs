using System.Reflection;

namespace WinterMP.Launcher.Services
{
    /// <summary>Copies bundled WinterMP plugin DLLs into the game folder.</summary>
    public static class ModPayload
    {
        public static readonly string[] RequiredFiles =
        {
            "WinterMP.Core.dll",
            "WinterMP.Net.dll",
            "WinterMP.FastBoot.dll",
            "sync-catalog.json",
            "wintermp-compat.json",
        };

        public static string PayloadDir
        {
            get
            {
                string nextToExe = Path.Combine(AppContext.BaseDirectory, "payload");
                if (Directory.Exists(nextToExe)) return SelectPayloadDirectory(nextToExe, CachedPayloadDir);

                string dev = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory, "..", "..", "..", "..",
                    "WinterMP.Core", "bin", "Release", "net35"));
                return Directory.Exists(dev) ? dev : nextToExe;
            }
        }

        public static string LauncherVersion =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

        public static bool PayloadPresent()
        {
            foreach (string file in RequiredFiles)
            {
                if (!File.Exists(Path.Combine(PayloadDir, file)))
                    return false;
            }

            return true;
        }

        internal static string CachedPayloadDir => Path.Combine(PlatformEnv.AppDataDir(), "payload");

        internal static string SelectPayloadDirectory(string bundled, string cached)
        {
            var bundle = CompatManifest.LoadFrom(bundled);
            var update = CompatManifest.LoadFrom(cached);
            if (bundle != null && update != null && update.ValidatePayload(cached) == null
                && ModVersionHelper.TryParse(bundle.ModVersion, out var baseline)
                && ModVersionHelper.IsNewerThan(update.ModVersion, baseline)) return cached;
            return bundled;
        }

        public static string Deploy(string gameDir)
        {
            GameLauncher.RequireGameClosed();
            string modDir = Path.Combine(gameDir, "BepInEx", "plugins", "WinterMP");
            ReplaceDirectory(PayloadDir, modDir, Path.Combine(gameDir, "BepInEx"));
            return $"Deployed {Branding.ProductName} ({ModMeta.ModVersion}) — {RequiredFiles.Length} files to {modDir}";
        }

        internal static void ReplaceDirectory(string source, string destination, string transactionRoot)
        {
            var manifest = CompatManifest.LoadFrom(source);
            string? error = manifest?.ValidatePayload(source);
            if (manifest == null || error != null)
                throw new InvalidOperationException(error ?? "Mod compatibility manifest is missing or invalid.");

            // Keep staging and rollback DLLs outside plugins: BepInEx recursively loads
            // DLLs from there, including leftovers after an interrupted install.
            string transaction = Path.Combine(transactionRoot, ".wintermp-install-" + Guid.NewGuid().ToString("N"));
            string staged = Path.Combine(transaction, "new");
            string previous = Path.Combine(transaction, "previous");
            Directory.CreateDirectory(staged);
            bool moved = false;
            bool committed = false;
            try
            {
                if (Directory.Exists(destination)) CopyDirectory(destination, staged);
                foreach (string file in RequiredFiles)
                    File.Copy(Path.Combine(source, file), Path.Combine(staged, file), overwrite: true);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (Directory.Exists(destination))
                {
                    Directory.Move(destination, previous);
                    moved = true;
                }
                try { Directory.Move(staged, destination); }
                catch
                {
                    if (moved) Directory.Move(previous, destination);
                    moved = false;
                    throw;
                }
                committed = true;
            }
            finally
            {
                // If rollback itself failed, retain the previous installation for recovery.
                if (!moved || committed)
                    try { Directory.Delete(transaction, recursive: true); } catch { }
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }
}
