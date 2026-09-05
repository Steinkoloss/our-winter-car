using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WinterMP.Launcher.Services
{
    /// <summary>
    /// My Winter Car ships with Unity 5.0 <c>displayResolutionDialog = Enabled</c> baked into
    /// <c>mywintercar_Data/mainData</c>. Registry/cmdline prefs do not skip that native
    /// ScreenSelector — only setting the field to <c>Disabled (0)</c> does.
    /// </summary>
    public static class MainDataBootPatch
    {
        // PlayerSettings.displayResolutionDialog (int32) — verified on MWC Unity 5.0.0f4 mainData.
        private const int DisplayResolutionDialogOffset = 4224;

        public const string BackupSuffix = ".wintermp-original";

        /// <summary>Steam appmanifest buildids where offset 4224 was verified.</summary>
        private static readonly HashSet<string> VerifiedSteamBuildIds = new(StringComparer.Ordinal)
        {
            "23268598",
        };

        private const int Unity50Disabled = 0;
        private const int Unity50Enabled = 1;
        private const int Unity50HiddenByDefault = 2;

        public static bool TryDisableResolutionDialog(string gameDir, out string? message)
            => TryDisableResolutionDialog(gameDir, steamBuildId: null, out message);

        public static bool TryDisableResolutionDialog(string gameDir, string? steamBuildId, out string? message)
        {
            message = null;
            string path = Path.Combine(gameDir, "mywintercar_Data", "mainData");
            if (!File.Exists(path))
                return false;

            if (steamBuildId == null || !VerifiedSteamBuildIds.Contains(steamBuildId))
            {
                message = "Resolution-dialog patch skipped: this game build has not been verified.";
                return false;
            }

            byte[] data = File.ReadAllBytes(path);
            if (data.Length < DisplayResolutionDialogOffset + 4)
            {
                message = "mainData too small — game update may have changed layout; patch skipped.";
                return false;
            }

            if (!LooksLikeMyWinterCarMainData(data))
            {
                message = "mainData does not look like My Winter Car — patch skipped.";
                return false;
            }

            int current = BitConverter.ToInt32(data, DisplayResolutionDialogOffset);
            if (current == Unity50Disabled)
            {
                message = "Resolution dialog already disabled in mainData.";
                AppendBuildWarning(ref message, steamBuildId);
                return true;
            }

            if (current != Unity50Enabled && current != Unity50HiddenByDefault)
            {
                message =
                    $"Unexpected displayResolutionDialog value {current} at offset {DisplayResolutionDialogOffset} — patch skipped.";
                AppendBuildWarning(ref message, steamBuildId);
                return false;
            }

            string backup = path + BackupSuffix;
            if (!File.Exists(backup))
                File.Copy(path, backup, false);

            data[DisplayResolutionDialogOffset] = Unity50Disabled;
            data[DisplayResolutionDialogOffset + 1] = 0;
            data[DisplayResolutionDialogOffset + 2] = 0;
            data[DisplayResolutionDialogOffset + 3] = 0;
            File.WriteAllBytes(path, data);

            message = "Disabled Unity ScreenSelector in mywintercar_Data/mainData.";
            AppendBuildWarning(ref message, steamBuildId);
            return true;
        }

        /// <summary>Restores mainData from the first-patch backup written by <see cref="TryDisableResolutionDialog"/>.</summary>
        public static bool TryRestoreResolutionDialog(string gameDir, out string? message)
        {
            message = null;
            string path = Path.Combine(gameDir, "mywintercar_Data", "mainData");
            string backup = path + BackupSuffix;

            if (!File.Exists(backup))
            {
                message = "No mainData backup to restore (ScreenSelector patch was never applied).";
                return false;
            }

            if (!File.Exists(path))
            {
                message = "mainData missing — cannot restore.";
                return false;
            }

            byte[] current = File.ReadAllBytes(path);
            byte[] original = File.ReadAllBytes(backup);
            if (current.Length != original.Length || original.Length < DisplayResolutionDialogOffset + 4)
            {
                message = "Game files changed since the resolution-dialog backup; leaving the current game files in place.";
                return false;
            }
            for (int i = 0; i < current.Length; i++)
            {
                if (i >= DisplayResolutionDialogOffset && i < DisplayResolutionDialogOffset + 4) continue;
                if (current[i] != original[i])
                {
                    message = "Game files changed since the resolution-dialog backup; leaving the current game files in place.";
                    return false;
                }
            }
            File.Copy(backup, path, true);
            File.Delete(backup);
            message = "Restored mywintercar_Data/mainData from .wintermp-original backup.";
            return true;
        }

        private static void AppendBuildWarning(ref string? message, string? steamBuildId)
        {
            if (string.IsNullOrWhiteSpace(steamBuildId)) return;
            if (VerifiedSteamBuildIds.Contains(steamBuildId)) return;

            string warning =
                $"Warning: Steam build {steamBuildId} is not in the verified list for the mainData ScreenSelector patch "
                + $"(offset {DisplayResolutionDialogOffset}). If boot breaks after a game update, report the new buildid.";
            message = string.IsNullOrWhiteSpace(message) ? warning : message + " " + warning;
        }

        private static bool LooksLikeMyWinterCarMainData(byte[] data)
        {
            string text = Encoding.ASCII.GetString(data);
            return text.Contains("Amistech") && text.Contains("My Winter Car");
        }
    }
}
