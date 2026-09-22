using System;
using System.Collections.Generic;
using WinterMP.Core.Session;

namespace UnityEngine
{
    internal static class Time { internal static float unscaledTime; }
}

namespace WinterMP.Core.Session
{
    public enum SessionState { Idle, Hosting, Connecting, Connected, Failed }
    internal sealed class SessionManager
    {
        internal static SessionManager? Instance;
        internal SessionState State;
        internal bool IsHost => State == SessionState.Hosting;
        internal int PlayerCount = 1;
        internal readonly List<string> Sent = new List<string>();
        internal void SendWorldMessage(object message, WinterMP.Net.Channel channel) => Sent.Add(message.GetType().Name);
    }
}

namespace WinterMP.Core.Diagnostics
{
    internal static class SyncEventLog
    {
        internal static readonly List<string> Entries = new List<string>();
        internal static int Dumps;
        internal static void Record(string category, string detail) => Entries.Add(category + ": " + detail);
        internal static void DumpToFile() { Dumps++; }
        internal static void Clear() => Entries.Clear();
    }
}

namespace WinterMP.Core
{
    internal static class WinterMPPlugin
    {
        internal static readonly LogDouble Log = new LogDouble();
        internal static readonly FlagDouble DevKeysEnabled = new FlagDouble();
    }
    internal sealed class FlagDouble { internal bool Value; }
    internal sealed class LogDouble
    {
        internal readonly List<string> Warnings = new List<string>(), Errors = new List<string>();
        internal void LogWarning(string text) => Warnings.Add(text);
        internal void LogError(string text) => Errors.Add(text);
        internal void LogInfo(string text) { }
    }
}

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleDouble
    {
        internal Action<float> OnLate = _ => { };
        internal void LateUpdateRemoteVehicles(float now) => OnLate(now);
    }
    internal sealed class TrailerDouble : UpdateDouble
    {
        internal Action OnLate = () => { };
        internal void LateUpdate() => OnLate();
    }
    internal sealed class VenttiDouble : UpdateDouble
    {
        internal Action<SessionManager> OnLate = _ => { };
        internal void LateUpdate(SessionManager session) => OnLate(session);
    }

    public sealed partial class WorldSyncManager
    {
        private bool _syncReady = true, _wasSessionActive = true;
        private readonly VehicleDouble _vehicles = new VehicleDouble();
        private readonly TrailerDouble _trailer = new TrailerDouble();
        private readonly VenttiDouble _ventti = new VenttiDouble();
        internal bool GameLevel = true;
        internal bool Disabled => _worldSyncDisabled;
        internal int ErrorCount => _syncErrorCount;
        internal float GlobalBackoff => _syncErrorBackoffUntil;
        internal int Releases, WorldUpdates;
        internal Action OnWorldUpdate = () => { };
        internal Action OnRelease = () => { };
        internal void Bind(Action<float> vehicles, Action trailer, Action<SessionManager> ventti)
        { _vehicles.OnLate = vehicles; _trailer.OnLate = trailer; _ventti.OnLate = ventti; }
        internal void SetAdmission(bool ready, bool active) { _syncReady = ready; _wasSessionActive = active; }
        internal void TickLate(float now) { UnityEngine.Time.unscaledTime = now; LateUpdate(); }
        internal void TickUpdate(float now) { UnityEngine.Time.unscaledTime = now; Update(); }
        internal void ResetErrors() => ResetSyncErrors();
        internal void ResetWorldErrors() => ResetLateUpdateErrors();
        private bool IsGameLevel() => GameLevel;
        // Native discovery/cleanup are not linked or executed. Static wiring tests
        // separately check that production teardown reaches ResetSyncErrors.
        private void ReleaseEverything() { Releases++; OnRelease(); ResetSyncErrors(); }
        private void WatchLevelChanges()
        {
            var session = SessionManager.Instance;
            bool active = session != null && (session.State == SessionState.Hosting || session.State == SessionState.Connected);
            if (!active) return;
            WorldUpdates++;
            OnWorldUpdate();
        }
    }
}
