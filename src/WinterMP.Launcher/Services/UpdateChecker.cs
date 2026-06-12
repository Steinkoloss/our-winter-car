using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
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
        private const string ReleasesApi =
            "https://api.github.com/repos/Steinkoloss/our-winter-car/releases/latest";

        private static readonly string[] PayloadAssetNames = { "OurWinterCar-payload.zip", "WinterMP-payload.zip" };
        private static readonly string[] SetupAssetNames = { "OurWinterCar-Setup.exe", "WinterMP-Setup.exe" };

        public static async Task<UpdateCheckResult> CheckAsync(string? gameDir = null, string? githubToken = null)
        {
            try
            {
                var (doc, status) = await FetchReleaseJsonAsync(githubToken).ConfigureAwait(false);
                if (doc == null)
                {
                    return new UpdateCheckResult
                    {
                        ErrorMessage = DescribeApiFailure(status ?? HttpStatusCode.NotFound, githubToken),
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
            string downloadUrl, string gameDir, string? githubToken = null)
        {
            string zipPath = await DownloadAssetAsync(downloadUrl, FileNameFromUrl(downloadUrl, "payload.zip"), githubToken)
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

        public static async Task<string> DownloadLauncherSetupAsync(string downloadUrl, string? githubToken = null)
        {
            return await DownloadAssetAsync(downloadUrl, FileNameFromUrl(downloadUrl, "Setup.exe"), githubToken)
                .ConfigureAwait(false);
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

        private static async Task<string> DownloadAssetAsync(
            string downloadUrl, string fileName, string? githubToken)
        {
            string downloadDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinterMP", "downloads");
            Directory.CreateDirectory(downloadDir);

            string destPath = Path.Combine(downloadDir, fileName);
            using var client = CreateClient(githubToken);
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

        private static async Task<(JsonDocument? Doc, HttpStatusCode? ErrorStatus)> FetchReleaseJsonAsync(
            string? githubToken)
        {
            using var client = CreateClient(githubToken);
            using var response = await client.GetAsync(ReleasesApi).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return (null, response.StatusCode);

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (JsonDocument.Parse(json), null);
        }

        private static string DescribeApiFailure(HttpStatusCode status, string? githubToken)
        {
            if (status == HttpStatusCode.NotFound && string.IsNullOrWhiteSpace(githubToken))
            {
                return "GitHub returned 404. The repo is private — open Settings and add a " +
                       "GitHub token (read-only) or make the repo public.";
            }

            if (status == HttpStatusCode.NotFound)
                return "GitHub returned 404. Check your token or whether a release exists.";

            if (status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden)
                return "GitHub rejected the token. Check Settings → GitHub token.";

            return $"GitHub API error ({(int)status}).";
        }

        private static HttpClient CreateClient(string? githubToken)
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WinterMP-Launcher/0.1.1");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            if (!string.IsNullOrWhiteSpace(githubToken))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", githubToken.Trim());
            }

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
