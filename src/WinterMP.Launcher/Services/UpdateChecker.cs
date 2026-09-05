using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
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
        /// <summary>Background poll interval. GitHub allows 60 unauthenticated API calls/hour (~40 at 90s).</summary>
        public static readonly TimeSpan BackgroundCheckInterval = TimeSpan.FromSeconds(90);

        private const string ReleasesApi =
            "https://api.github.com/repos/Steinkoloss/our-winter-car/releases/latest";

        private static readonly string[] PayloadAssetNames = { "OurWinterCar-payload.zip", "WinterMP-payload.zip" };
        private static readonly string[] SetupAssetNames = OperatingSystem.IsLinux()
            ? new[] { "OurWinterCar-Launcher-linux-x64.AppImage" }
            : new[] { "OurWinterCar-Setup.exe", "WinterMP-Setup.exe" };

        public static async Task<UpdateCheckResult> CheckAsync(string? gameDir = null)
        {
            try
            {
                var (doc, status, errorBody) = await FetchReleaseJsonAsync().ConfigureAwait(false);
                if (doc == null)
                {
                    return new UpdateCheckResult
                    {
                        ErrorMessage = DescribeApiFailure(status ?? HttpStatusCode.NotFound, errorBody),
                    };
                }

                using (doc)
                {

                var root = doc.RootElement;
                string tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
                string url = root.GetProperty("html_url").GetString() ?? string.Empty;
                string? notes = root.TryGetProperty("body", out var body) ? body.GetString() : null;
                Version remote = ParseTag(tag);

                var launcherVersion = ModVersionHelper.Normalize(ParseVersion(ModPayload.LauncherVersion));
                var bundledMod = ModVersionHelper.Normalize(ParseVersion(ModMeta.ModVersion));
                Version? installedMod = null;
                if (!string.IsNullOrEmpty(gameDir))
                {
                    string? installed = BepInExInstaller.GetInstalledModVersion(gameDir);
                    if (ModVersionHelper.TryParse(installed, out Version parsed))
                        installedMod = ModVersionHelper.Normalize(parsed);
                }

                string? payloadUrl = FindAssetUrl(root, PayloadAssetNames);
                string? setupUrl = FindAssetUrl(root, SetupAssetNames);

                bool launcherUpdate = ModVersionHelper.IsNewerThan(remote, launcherVersion);
                bool modUpdate = false;
                if (!string.IsNullOrEmpty(gameDir))
                {
                    if (installedMod != null)
                        modUpdate = ModVersionHelper.IsNewerThan(remote, installedMod);
                    else if (ModVersionHelper.IsNewerThan(remote, bundledMod))
                        modUpdate = true;
                }
                else if (ModVersionHelper.IsNewerThan(remote, bundledMod))
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

        public static async Task<string> DownloadAndApplyPayloadAsync(string downloadUrl, string gameDir)
        {
            string zipPath = await DownloadAssetAsync(downloadUrl, FileNameFromUrl(downloadUrl, "payload.zip"))
                .ConfigureAwait(false);

            string downloadDir = Path.GetDirectoryName(zipPath)!;
            string extractDir = Path.Combine(downloadDir, "payload-extract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(extractDir);
            try
            {
                ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

                GameLauncher.RequireGameClosed();
                ModPayload.ReplaceDirectory(extractDir, ModPayload.CachedPayloadDir, PlatformEnv.AppDataDir());

                return BepInExInstaller.InstallOrRepair(gameDir);
            }
            finally
            {
                try { Directory.Delete(extractDir, recursive: true); } catch { /* best effort */ }
            }
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

            if (OperatingSystem.IsLinux())
            {
                // setupPath is the downloaded .AppImage — replace ourselves then relaunch.
                string launcherExe = ResolveLauncherExePath();
                int pid = Process.GetCurrentProcess().Id;
                string helper = WriteLinuxLauncherUpdateScript(setupPath, launcherExe, pid);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    ArgumentList = { helper },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                return;
            }

            string helperScript = WritePostUpdateRestartScript(setupPath, ResolveLauncherExePath());
            Process.Start(new ProcessStartInfo
            {
                FileName = helperScript,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }

        private static string WriteLinuxLauncherUpdateScript(string newAppImage, string launcherExe, int launcherPid)
        {
            string dir = Path.Combine(PlatformEnv.AppDataDir(), "updates");
            Directory.CreateDirectory(dir);
            string scriptPath = Path.Combine(dir, $"launcher-update-{Guid.NewGuid():N}.sh");

            string script =
                "#!/usr/bin/env bash\n" +
                $"while kill -0 {launcherPid} 2>/dev/null; do sleep 1; done\n" +
                $"chmod +x '{newAppImage}'\n" +
                $"mv '{newAppImage}' '{launcherExe}'\n" +
                $"nohup '{launcherExe}' >/dev/null 2>&1 &\n" +
                "rm -- \"$0\"\n";

            File.WriteAllText(scriptPath, script);
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(scriptPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            return scriptPath;
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
            // When running as an AppImage the process path points inside the squashfs mount;
            // $APPIMAGE is the real file on disk and is the thing we actually want to replace/relaunch.
            if (OperatingSystem.IsLinux())
            {
                string? appImage = Environment.GetEnvironmentVariable("APPIMAGE");
                if (!string.IsNullOrEmpty(appImage) && File.Exists(appImage))
                    return appImage;
            }
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

        private static async Task<(JsonDocument? Doc, HttpStatusCode? ErrorStatus, string? ErrorBody)> FetchReleaseJsonAsync()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync(ReleasesApi).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return (null, response.StatusCode, errorBody);
            }

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (JsonDocument.Parse(json), null, null);
        }

        private static string DescribeApiFailure(HttpStatusCode status, string? errorBody)
        {
            if (status == HttpStatusCode.Forbidden
                && errorBody != null
                && errorBody.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "GitHub API rate limit reached (too many checks). Try again in about an hour.";
            }

            if (status == HttpStatusCode.NotFound)
                return "No release found on GitHub.";

            if (status == HttpStatusCode.Forbidden || status == HttpStatusCode.Unauthorized)
                return "Could not access GitHub releases.";

            return $"GitHub API error ({(int)status}).";
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("OurWinterCar-Launcher/" + ModPayload.LauncherVersion);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
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
            if (ModVersionHelper.TryParse(trimmed, out Version v))
                return ModVersionHelper.Normalize(v);
            return new Version(0, 0, 0);
        }

        private static Version ParseVersion(string text)
        {
            if (ModVersionHelper.TryParse(text, out Version v))
                return ModVersionHelper.Normalize(v);
            return new Version(0, 0, 0);
        }
    }
}
