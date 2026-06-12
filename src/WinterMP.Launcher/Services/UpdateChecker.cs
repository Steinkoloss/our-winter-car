using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace WinterMP.Launcher.Services
{
    public sealed class UpdateCheckResult
    {
        public string Tag = string.Empty;
        public string ReleaseUrl = string.Empty;
        public string? ReleaseNotes;
        public Version RemoteVersion = new(0, 0, 0);

        public Version LauncherVersion = new(0, 0, 0);
        public Version BundledModVersion = new(0, 0, 0);
        public Version? InstalledModVersion;

        public bool LauncherUpdateAvailable;
        public bool ModUpdateAvailable;

        public string? PayloadDownloadUrl;
        public string? SetupDownloadUrl;

        public bool AnyUpdateAvailable => LauncherUpdateAvailable || ModUpdateAvailable;

        public string StatusSummary
        {
            get
            {
                if (!AnyUpdateAvailable)
                    return "Up to date";

                var parts = new List<string>();
                if (LauncherUpdateAvailable)
                    parts.Add($"launcher {LauncherVersion} → {RemoteVersion}");
                if (ModUpdateAvailable)
                {
                    Version from = InstalledModVersion ?? BundledModVersion;
                    parts.Add($"mod {from} → {RemoteVersion}");
                }

                return $"{Tag}: {string.Join(", ", parts)}";
            }
        }
    }

    /// <summary>Checks GitHub releases and downloads mod payload or launcher setup updates.</summary>
    public static class UpdateChecker
    {
        private const string ReleasesApi =
            "https://api.github.com/repos/Steinkoloss/our-winter-car/releases/latest";

        private const string PayloadAssetSuffix = "WinterMP-payload.zip";
        private const string SetupAssetSuffix = "WinterMP-Setup.exe";

        public static async Task<UpdateCheckResult?> CheckAsync(string? gameDir = null)
        {
            try
            {
                using var doc = await FetchReleaseJsonAsync().ConfigureAwait(false);
                if (doc == null) return null;

                var root = doc.RootElement;
                string tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
                string url = root.GetProperty("html_url").GetString() ?? string.Empty;
                string? notes = root.TryGetProperty("body", out var body) ? body.GetString() : null;
                Version remote = ParseTag(tag);

                var launcherVersion = ParseVersion(ModPayload.LauncherVersion);
                var bundledMod = ParseVersion(ModMeta.ModVersion);
                Version? installedMod = null;
                if (!string.IsNullOrEmpty(gameDir))
                {
                    string? installed = BepInExInstaller.GetInstalledModVersion(gameDir);
                    if (installed != null)
                        installedMod = ParseVersion(installed);
                }

                string? payloadUrl = FindAssetUrl(root, PayloadAssetSuffix);
                string? setupUrl = FindAssetUrl(root, SetupAssetSuffix);

                bool launcherUpdate = remote > launcherVersion;
                bool modUpdate = false;
                if (!string.IsNullOrEmpty(gameDir))
                {
                    if (installedMod != null)
                        modUpdate = remote > installedMod;
                    else if (remote > bundledMod)
                        modUpdate = true;
                }
                else if (remote > bundledMod)
                {
                    modUpdate = true;
                }

                if (modUpdate && payloadUrl == null)
                    modUpdate = false;
                if (launcherUpdate && setupUrl == null)
                    launcherUpdate = false;

                return new UpdateCheckResult
                {
                    Tag = tag,
                    ReleaseUrl = url,
                    ReleaseNotes = notes,
                    RemoteVersion = remote,
                    LauncherVersion = launcherVersion,
                    BundledModVersion = bundledMod,
                    InstalledModVersion = installedMod,
                    LauncherUpdateAvailable = launcherUpdate,
                    ModUpdateAvailable = modUpdate,
                    PayloadDownloadUrl = payloadUrl,
                    SetupDownloadUrl = setupUrl,
                };
            }
            catch
            {
                return null;
            }
        }

        public static async Task<string> DownloadAndApplyPayloadAsync(string downloadUrl, string gameDir)
        {
            string zipPath = await DownloadAssetAsync(downloadUrl, PayloadAssetSuffix).ConfigureAwait(false);

            string downloadDir = Path.GetDirectoryName(zipPath)!;
            string extractDir = Path.Combine(downloadDir, "payload-extract");
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, recursive: true);
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

            string payloadDir = ModPayload.PayloadDir;
            Directory.CreateDirectory(payloadDir);
            CopyPayloadFiles(extractDir, payloadDir);

            return BepInExInstaller.InstallOrRepair(gameDir);
        }

        public static async Task<string> DownloadLauncherSetupAsync(string downloadUrl)
        {
            return await DownloadAssetAsync(downloadUrl, SetupAssetSuffix).ConfigureAwait(false);
        }

        public static void RunLauncherSetup(string setupPath)
        {
            if (!File.Exists(setupPath))
                throw new FileNotFoundException("Installer not found.", setupPath);

            Process.Start(new ProcessStartInfo
            {
                FileName = setupPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true,
            });
        }

        private static async Task<string> DownloadAssetAsync(string downloadUrl, string fileName)
        {
            string downloadDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinterMP", "downloads");
            Directory.CreateDirectory(downloadDir);

            string destPath = Path.Combine(downloadDir, fileName);
            using var client = CreateClient();
            await using var stream = await client.GetStreamAsync(downloadUrl).ConfigureAwait(false);
            await using var file = File.Create(destPath);
            await stream.CopyToAsync(file).ConfigureAwait(false);
            return destPath;
        }

        private static void CopyPayloadFiles(string sourceDir, string destDir)
        {
            foreach (string file in ModPayload.RequiredFiles)
            {
                string? found = FindFileRecursive(sourceDir, file);
                if (found == null)
                    throw new InvalidOperationException($"Update package is missing {file}.");

                File.Copy(found, Path.Combine(destDir, file), overwrite: true);
            }
        }

        private static string? FindFileRecursive(string dir, string fileName)
        {
            foreach (string file in Directory.GetFiles(dir, fileName, SearchOption.AllDirectories))
                return file;
            return null;
        }

        private static async Task<JsonDocument?> FetchReleaseJsonAsync()
        {
            using var client = CreateClient();
            string json = await client.GetStringAsync(ReleasesApi).ConfigureAwait(false);
            return JsonDocument.Parse(json);
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WinterMP-Launcher");
            return client;
        }

        private static string? FindAssetUrl(JsonElement root, string suffix)
        {
            if (!root.TryGetProperty("assets", out var assets)) return null;
            foreach (var asset in assets.EnumerateArray())
            {
                string? name = asset.GetProperty("name").GetString();
                if (name != null && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return asset.GetProperty("browser_download_url").GetString();
            }

            return null;
        }

        private static Version ParseTag(string tag)
        {
            string trimmed = tag.TrimStart('v', 'V');
            return Version.TryParse(trimmed, out var v) ? v : new Version(0, 0, 0);
        }

        private static Version ParseVersion(string text)
        {
            if (Version.TryParse(text, out var v)) return v;
            int dash = text.IndexOf('-');
            if (dash > 0 && Version.TryParse(text[..dash], out v)) return v;
            return new Version(0, 0, 0);
        }
    }
}
