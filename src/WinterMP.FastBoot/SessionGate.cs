using System;
using System.Reflection;

namespace WinterMP.FastBoot
{
    /// <summary>
    /// Optional integration with Our Winter Car's session manager (when Core is loaded).
    /// Hosts wait for at least one remote player before auto-loading — same rule as the
    /// old <c>-wintermp-autoload</c> path in WorldSyncManager.
    /// </summary>
    internal static class SessionGate
    {
        private static bool _resolved;
        private static Type? _sessionManagerType;
        private static PropertyInfo? _instanceProperty;
        private static PropertyInfo? _isHostProperty;
        private static PropertyInfo? _playerCountProperty;
        private static PropertyInfo? _stateProperty;

        public static bool CanAutoLoadContinue()
        {
            if (!TryGetSession(out object? session) || session == null)
                return true;

            try
            {
                string state = _stateProperty!.GetValue(session, null)?.ToString() ?? string.Empty;
                if (state != "Hosting" && state != "Connected")
                    return false;

                bool isHost = (bool)_isHostProperty!.GetValue(session, null)!;
                int playerCount = (int)_playerCountProperty!.GetValue(session, null)!;
                if (isHost && playerCount == 0)
                    return false;

                return true;
            }
            catch
            {
                return true;
            }
        }

        private static bool TryGetSession(out object? session)
        {
            session = null;
            EnsureResolved();
            if (_sessionManagerType == null || _instanceProperty == null)
                return false;

            try
            {
                session = _instanceProperty.GetValue(null, null);
                return session != null;
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureResolved()
        {
            if (_resolved) return;
            _resolved = true;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "WinterMP.Core") continue;

                _sessionManagerType = assembly.GetType("WinterMP.Core.Session.SessionManager");
                if (_sessionManagerType == null) return;

                _instanceProperty = _sessionManagerType.GetProperty(
                    "Instance", BindingFlags.Public | BindingFlags.Static);
                _isHostProperty = _sessionManagerType.GetProperty(
                    "IsHost", BindingFlags.Public | BindingFlags.Instance);
                _playerCountProperty = _sessionManagerType.GetProperty(
                    "PlayerCount", BindingFlags.Public | BindingFlags.Instance);
                _stateProperty = _sessionManagerType.GetProperty(
                    "State", BindingFlags.Public | BindingFlags.Instance);
                return;
            }
        }
    }
}
