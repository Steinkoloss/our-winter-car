using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
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

        /// <summary>Set when the GitHub API call failed.</summary>
        public string? ErrorMessage;

        public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
        public bool AnyUpdateAvailable => LauncherUpdateAvailable || ModUpdateAvailable;

        public string StatusSummary
        {
            get
            {
                if (!IsSuccess) return ErrorMessage ?? "Update check failed";
                if (!AnyUpdateAvailable)
                    return string.IsNullOrEmpty(Tag) ? "Up to date" : $"Up to date ({Tag})";

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

        private static readonly string[] PayloadAssetNames = { "OurWinterCar-payload.zip", "WinterMP-payload.zip" };
        private static readonly string[] SetupAssetNames = { "OurWinterCar-Setup.exe", "WinterMP-Setup.exe" };

        public static async Task<UpdateCheckResult> CheckAsync(string? gameDir = null)
        {
            try
            {
                var (doc, status) = await FetchReleaseJsonAsync().ConfigureAwait(false);
                if (doc == null)
                {
                    return new UpdateCheckResult
                    {
                        ErrorMessage = DescribeApiFailure(status ?? HttpStatusCode.NotFound),
                    };
                }

                using (doc)
                {

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

                string? payloadUrl = FindAssetUrl(root, PayloadAssetNames);
                string? setupUrl = FindAssetUrl(root, SetupAssetNames);

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
            }
            catch (HttpRequestException ex)
            {
                return new UpdateCheckResult
                {
                    ErrorMessage = $"Network error: {ex.Message}",
                };
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult
                {
                    ErrorMessage = ex.Message,
                };
            }
        }

        public static async Task<string> DownloadAndApplyPayloadAsync(
            string downloadUrl, string gameDir)
        {
            string zipPath = await DownloadAssetAsync(downloadUrl, FileNameFromUrl(downloadUrl, "payload.zip"))
                .ConfigureAwait(false);

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
            return await DownloadAssetAsync(downloadUrl, FileNameFromUrl(downloadUrl, "Setup.exe"))
                .ConfigureAwait(false);
        }

        public static void RunLauncherSetup(string setupPath)
        {
            if (!File.Exists(setupPath))
                throw new FileNotFoundException("Installer not found.", setupPath);

            string launcherExe = ResolveLauncherExePath();
            string helper = WritePostUpdateRestartScript(setupPath, launcherExe);
            Process.Start(new ProcessStartInfo
            {
                FileName = helper,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }

        /// <summary>Starts a fresh launcher instance and returns — caller should shut down.</summary>
        public static void RestartApplication()
        {
            string launcherExe = ResolveLauncherExePath();
            Process.Start(new ProcessStartInfo
            {
                FileName = launcherExe,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(launcherExe) ?? AppContext.BaseDirectory,
            });
        }

        private static string ResolveLauncherExePath()
        {
            return Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Could not resolve launcher executable path.");
        }

        private static string WritePostUpdateRestartScript(string setupPath, string launcherExe)
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinterMP", "updates");
            Directory.CreateDirectory(dir);

            string scriptPath = Path.Combine(dir, $"restart-{Guid.NewGuid():N}.cmd");
            const string setupArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS";

            string content =
                "@echo off\r\n" +
                "timeout /t 2 /nobreak >nul\r\n" +
                $"call \"{setupPath}\" {setupArgs}\r\n" +
                $"if exist \"{launcherExe}\" start \"\" \"{launcherExe}\"\r\n" +
                "del \"%~f0\"\r\n";

            File.WriteAllText(scriptPath, content);
            return scriptPath;
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

        private static async Task<(JsonDocument? Doc, HttpStatusCode? ErrorStatus)> FetchReleaseJsonAsync()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync(ReleasesApi).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return (null, response.StatusCode);

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (JsonDocument.Parse(json), null);
        }

        private static string DescribeApiFailure(HttpStatusCode status)
        {
            if (status == HttpStatusCode.NotFound)
                return "No release found on GitHub.";

            if (status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden)
                return "Could not access GitHub releases.";

            return $"GitHub API error ({(int)status}).";
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WinterMP-Launcher/0.1.1");
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            return client;
        }

        private static string? FindAssetUrl(JsonElement root, IEnumerable<string> fileNames)
        {
            if (!root.TryGetProperty("assets", out var assets)) return null;
            var wanted = new HashSet<string>(fileNames, StringComparer.OrdinalIgnoreCase);
            foreach (var asset in assets.EnumerateArray())
            {
                string? name = asset.GetProperty("name").GetString();
                if (name != null && wanted.Contains(name))
                    return asset.GetProperty("browser_download_url").GetString();
            }

            return null;
        }

        private static string FileNameFromUrl(string url, string fallback)
        {
            try
            {
                string name = Path.GetFileName(new Uri(url).LocalPath);
                return string.IsNullOrEmpty(name) ? fallback : name;
            }
            catch
            {
                return fallback;
            }
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
