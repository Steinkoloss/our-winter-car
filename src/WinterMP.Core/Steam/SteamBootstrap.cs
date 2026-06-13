#if STEAMWORKS
using System;
using System.Text;
using Steamworks;
using UnityEngine;

namespace WinterMP.Core.Steam
{
    /// <summary>
    /// Binds to the game's own Steam session. My Winter Car embeds Steamworks.NET
    /// (classic API + CSteamworks.dll) inside Assembly-CSharp-firstpass.dll — we
    /// reference that assembly, so our callbacks share the game's dispatcher and a
    /// second SteamAPI.Init() simply attaches to the existing session (AppID 4164420).
    ///
    /// Callback pumping: the game most likely calls SteamAPI.RunCallbacks() itself
    /// (it has Steam stats/achievements). We therefore do NOT pump by default; if no
    /// callbacks arrive within a few seconds of a lobby operation, the session manager
    /// flips <see cref="SelfPump"/> on as a fallback. The verification run will tell.
    /// </summary>
    internal static class SteamBootstrap
    {
        private const float InitRetryIntervalSeconds = 0.2f;
        private const float ModuleWaitRetryIntervalSeconds = 0.35f;

        private static bool _initialized;
        private static float _lastWaitLogAt = -1f;
        private static float _nextInitAttemptAt = -1f;

        /// <summary>Set true when the game does not appear to pump Steam callbacks itself.</summary>
        public static bool SelfPump;

        public static bool EnsureInitialized()
        {
            if (_initialized) return true;

            float now = Time.unscaledTime;
            if (_nextInitAttemptAt >= 0f && now < _nextInitAttemptAt)
                return false;

            try
            {
                if (!AreNativeSteamModulesLoaded())
                {
                    ScheduleRetry(now, ModuleWaitRetryIntervalSeconds);
                    return false;
                }

                if (!SteamAPI.IsSteamRunning())
                {
                    LogWaiting("Steam client not running yet");
                    ScheduleRetry(now, InitRetryIntervalSeconds);
                    return false;
                }

                if (!SteamAPI.Init())
                {
                    // FastBoot can reach MainMenu before the game's own Steam init finishes.
                    // Init() is safe to retry — a later call attaches to the live session.
                    LogWaiting("SteamAPI.Init() not ready yet (waiting for the game to connect)");
                    ScheduleRetry(now, InitRetryIntervalSeconds);
                    return false;
                }

                _initialized = true;
                _nextInitAttemptAt = -1f;
                WinterMPPlugin.Log.LogInfo(
                    $"Steam attached: {SteamFriends.GetPersonaName()} ({SteamUser.GetSteamID().m_SteamID})");
                return true;
            }
            catch (Exception e)
            {
                LogWaiting($"Steam init exception (will retry): {e.Message}");
                ScheduleRetry(now, InitRetryIntervalSeconds);
                return false;
            }
        }

        /// <summary>Call when MainMenu loads so the first attach attempt happens immediately.</summary>
        public static void NoteMainMenuReady()
        {
            if (_initialized) return;
            _nextInitAttemptAt = -1f;
        }

        private static void ScheduleRetry(float now, float delaySeconds)
        {
            _nextInitAttemptAt = now + delaySeconds;
        }

        private static bool AreNativeSteamModulesLoaded()
        {
            return GetModuleHandle("steam_api64.dll") != IntPtr.Zero
                || GetModuleHandle("CSteamworks.dll") != IntPtr.Zero
                || GetModuleHandle("steam_api.dll") != IntPtr.Zero;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private static void LogWaiting(string detail)
        {
            float now = Time.unscaledTime;
            if (_lastWaitLogAt >= 0f && now - _lastWaitLogAt < 5f) return;

            _lastWaitLogAt = now;
            WinterMPPlugin.Log.LogWarning($"Steam not ready: {detail}");
        }

        /// <summary>Called once per frame; only pumps when the self-pump fallback is active.</summary>
        public static void Pump()
        {
            if (!_initialized || !SelfPump) return;

            try
            {
                SteamAPI.RunCallbacks();
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogError($"SteamAPI.RunCallbacks failed: {e.Message}");
                SelfPump = false;
            }
        }

        public static string? TryGetPersonaName()
        {
            try
            {
                return EnsureInitialized() ? SteamFriends.GetPersonaName() : null;
            }
            catch
            {
                return null;
            }
        }

        public static ulong LocalSteamId
        {
            get
            {
                try
                {
                    return _initialized ? SteamUser.GetSteamID().m_SteamID : 0UL;
                }
                catch
                {
                    return 0UL;
                }
            }
        }

        public static void AppendDiagnostics(StringBuilder sb)
        {
            sb.AppendLine("-- steam (via game's embedded Steamworks.NET):");
            try
            {
                sb.AppendLine($"   IsSteamRunning: {SteamAPI.IsSteamRunning()}");
            }
            catch (Exception e)
            {
                sb.AppendLine($"   IsSteamRunning: <error: {e.Message}>");
            }

            try
            {
                if (EnsureInitialized())
                {
                    sb.AppendLine($"   persona: {SteamFriends.GetPersonaName()}");
                    sb.AppendLine($"   steamId: {SteamUser.GetSteamID().m_SteamID}");
                    try
                    {
                        sb.AppendLine($"   appId: {SteamUtils.GetAppID().m_AppId}");
                    }
                    catch (Exception e)
                    {
                        sb.AppendLine($"   appId: <error: {e.Message}>");
                    }
                }
                else
                {
                    sb.AppendLine("   init: failed");
                }
            }
            catch (Exception e)
            {
                sb.AppendLine($"   steam: <error: {e.Message}>");
            }
        }
    }
}
#endif
