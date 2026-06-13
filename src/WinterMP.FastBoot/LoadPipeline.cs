using System;
using System.IO;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace WinterMP.FastBoot
{
    /// <summary>
    /// Overlaps GAME scene streaming with main-menu ES2 hydrate by starting
    /// <see cref="Application.LoadLevelAsync"/> as early as save validation allows.
    /// </summary>
    internal static class LoadPipeline
    {
        public const int GameLevelIndex = 3;

        public static bool PreloadEnabled { get; set; } = true;
        public static bool LogTimings { get; set; } = true;

        public static float PreloadStartedAt { get; private set; } = -1f;
        public static float SyncLoadLevelAt { get; private set; } = -1f;
        public static float Es2OpenStartedAt { get; private set; } = -1f;
        public static float Es2OpenEndedAt { get; private set; } = -1f;
        public static int Es2OpenCount { get; private set; }
        public static int LoadLevelCallCount { get; private set; }
        public static float PreloadProgressAtSync { get; private set; } = -1f;

        private static AsyncOperation? _asyncOp;
        private static bool _preloadFailed;

        public static bool HasActivePreload => _asyncOp != null;

        public static float PreloadProgress =>
            _asyncOp != null ? _asyncOp.progress : 0f;

        public static void ResetBootMetrics(bool keepPreload)
        {
            Es2OpenStartedAt = -1f;
            Es2OpenEndedAt = -1f;
            Es2OpenCount = 0;
            LoadLevelCallCount = 0;
            PreloadProgressAtSync = -1f;

            if (keepPreload) return;

            PreloadStartedAt = -1f;
            SyncLoadLevelAt = -1f;
            _asyncOp = null;
            _preloadFailed = false;
        }

        public static bool DevDirectLoadActive { get; set; }

        public static void AbandonPreload()
        {
            if (_asyncOp == null) return;

            if (LogTimings)
            {
                FastBootLog.Host.LogInfo(
                    "FastBoot: abandoning async GAME preload (conflicting sync scene load).");
            }

            try
            {
                _asyncOp.allowSceneActivation = false;
            }
            catch
            {
                // best effort
            }

            _asyncOp = null;
            PreloadStartedAt = -1f;
        }

        public static void TryBeginPreload(ManualLogSource log)
        {
            if (!PreloadEnabled || _preloadFailed || DevDirectLoadActive) return;
            if (_asyncOp != null || PreloadStartedAt >= 0f) return;

            try
            {
                string level = Application.loadedLevelName ?? string.Empty;
                // MainMenu only — LoadLevelAsync(GAME) during SplashScreen races with
                // sync LoadLevel(MainMenu) and hard-crashes Unity 5.
                if (level != BootPhase.Menu) return;

                PreloadStartedAt = Time.realtimeSinceStartup;
                _asyncOp = Application.LoadLevelAsync(GameLevelIndex);
                if (_asyncOp == null)
                {
                    _preloadFailed = true;
                    log.LogWarning("FastBoot: LoadLevelAsync(GAME) returned null — preload disabled.");
                    return;
                }

                // Hold activation until the game calls LoadLevel(GAME); assets still stream.
                _asyncOp.allowSceneActivation = false;

                if (LogTimings)
                {
                    log.LogInfo(
                        "FastBoot: async GAME preload started on MainMenu (level "
                        + GameLevelIndex
                        + ") — streaming during ES2 hydrate.");
                }
            }
            catch (Exception e)
            {
                _preloadFailed = true;
                log.LogWarning("FastBoot: async GAME preload failed: " + e.Message);
            }
        }

        /// <summary>
        /// Harmony prefix: when async preload is far enough along, activate it and skip
        /// the redundant synchronous LoadLevel (game FSM continues on the next frame).
        /// </summary>
        public static bool TrySkipSyncLoadLevel(int level)
        {
            if (level != GameLevelIndex) return false;

            LoadLevelCallCount++;
            SyncLoadLevelAt = Time.realtimeSinceStartup;

            if (_asyncOp == null) return false;

            float progress = _asyncOp.progress;
            PreloadProgressAtSync = progress;

            AsyncOperation op = _asyncOp;
            _asyncOp = null;
            op.allowSceneActivation = true;

            if (LogTimings)
            {
                FastBootLog.Host.LogInfo(
                    "FastBoot: sync LoadLevel(GAME) — finishing async preload at "
                    + (progress * 100f).ToString("0")
                    + "% (lead "
                    + (SyncLoadLevelAt - PreloadStartedAt).ToString("0.0")
                    + "s).");
            }

            FastBootPlugin? host = FastBootPlugin.Instance;
            if (host != null)
                host.StartCoroutine(WaitForAsyncActivation(op));

            // Never call sync LoadLevel while an async load is active — Unity 5 crashes.
            return true;
        }

        private static System.Collections.IEnumerator WaitForAsyncActivation(AsyncOperation op)
        {
            while (op != null && !op.isDone)
                yield return null;
        }

        public static void NoteEs2OpenBegin()
        {
            Es2OpenCount++;
            if (Es2OpenStartedAt < 0f)
                Es2OpenStartedAt = Time.realtimeSinceStartup;
        }

        public static void NoteEs2OpenEnd()
        {
            Es2OpenEndedAt = Time.realtimeSinceStartup;
        }

        public static void WriteReport(ManualLogSource log, float continueAt, float gameAt)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== FastBoot load pipeline ===");
            sb.AppendLine("  ES2 hydrate passes: " + Es2OpenCount);
            if (Es2OpenStartedAt >= 0f && Es2OpenEndedAt >= 0f)
            {
                sb.AppendLine(
                    "  ES2 open window: "
                    + Es2OpenStartedAt.ToString("0.0")
                    + "s -> "
                    + Es2OpenEndedAt.ToString("0.0")
                    + "s ("
                    + (Es2OpenEndedAt - Es2OpenStartedAt).ToString("0.0")
                    + "s)");
            }

            if (PreloadStartedAt >= 0f)
            {
                sb.AppendLine("  Async preload started: " + PreloadStartedAt.ToString("0.0") + "s");
            }

            if (SyncLoadLevelAt >= 0f)
            {
                sb.AppendLine("  LoadLevel(GAME) call: " + SyncLoadLevelAt.ToString("0.0") + "s");
                if (PreloadStartedAt >= 0f)
                {
                    sb.AppendLine(
                        "  Preload lead time: "
                        + (SyncLoadLevelAt - PreloadStartedAt).ToString("0.0")
                        + "s");
                    sb.AppendLine(
                        "  Preload progress at sync: "
                        + (PreloadProgressAtSync * 100f).ToString("0")
                        + "%");
                }
            }

            sb.AppendLine("  LoadLevel calls (total): " + LoadLevelCallCount);
            if (Es2HydratePolicy.SkippedTagChecks > 0 || Es2HydratePolicy.SkippedTagReads > 0)
            {
                sb.AppendLine(
                    "  ES2 tags skipped (exists/read): "
                    + Es2HydratePolicy.SkippedTagChecks
                    + "/"
                    + Es2HydratePolicy.SkippedTagReads);
            }

            if (continueAt >= 0f && gameAt >= 0f)
                sb.AppendLine("  Continue -> GAME: " + (gameAt - continueAt).ToString("0.0") + "s");

            string report = sb.ToString();
            log.LogInfo(report);

            try
            {
                string dir = Path.Combine(BepInEx.Paths.GameRootPath, "WinterMP");
                Directory.CreateDirectory(dir);
                File.WriteAllText(
                    Path.Combine(dir, "load-pipeline.log"),
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]\n" + report);
            }
            catch
            {
                // best effort
            }
        }
    }
}
