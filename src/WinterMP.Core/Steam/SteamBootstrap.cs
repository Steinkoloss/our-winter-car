#if STEAMWORKS
using System;
using System.Text;
using Steamworks;

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
        private static bool _initialized;
        private static bool _initFailed;

        /// <summary>Set true when the game does not appear to pump Steam callbacks itself.</summary>
        public static bool SelfPump;

        public static bool EnsureInitialized()
        {
            if (_initialized) return true;
            if (_initFailed) return false;

            try
            {
                _initialized = SteamAPI.Init();
                if (!_initialized)
                {
                    _initFailed = true;
                    WinterMPPlugin.Log.LogError("SteamAPI.Init() failed — is Steam running and logged in?");
                    return false;
                }

                WinterMPPlugin.Log.LogInfo(
                    $"Steam attached: {SteamFriends.GetPersonaName()} ({SteamUser.GetSteamID().m_SteamID})");
                return true;
            }
            catch (Exception e)
            {
                _initFailed = true;
                WinterMPPlugin.Log.LogError($"Steam init exception (Steamworks API mismatch?): {e}");
                return false;
            }
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
