using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net.Messages;
using Xunit;

// Registration/dispatch doubles: execute the source-linked observer, NOT Harmony's
// native detour runtime. Marker files live only in this test assembly's bin tree.
namespace HarmonyLib
{
    public sealed class HarmonyMethod
    {
        public readonly MethodInfo Method;
        public HarmonyMethod(MethodInfo? method) { Assert.NotNull(method); Method = method; }
    }
    public sealed class Harmony
    {
        public static readonly List<(MethodInfo Original, HarmonyMethod? Prefix, HarmonyMethod? Postfix, HarmonyMethod? Finalizer)> Patches = new();
        public Harmony(string id) { }
        public void Patch(MethodInfo? original, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null, HarmonyMethod? finalizer = null)
        { Assert.NotNull(original); Patches.Add((original, prefix, postfix, finalizer)); }
    }
}
namespace BepInEx
{
    public static class Paths { public static string GameRootPath = ""; }
}
namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public readonly List<string> Lines = new();
        public bool Fail;
        public void LogInfo(object message)
        {
            if (Fail) throw new InvalidOperationException("Portable diagnostic log failure");
            Lines.Add(message.ToString()!);
        }
    }
    public static class Logger
    {
        public static readonly ManualLogSource ProbeLog = new();
        public static ManualLogSource CreateLogSource(string name) => ProbeLog;
    }
}
namespace WinterMP.GuestSaveProbe
{
    public sealed class Plugin { private void OnDestroy() { } }
    internal sealed partial class LiveBagProbe
    {
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static object PaneBridge => PaneScrapeSync.Instance!;
        internal void Tick() { }
        private static void Execute(string[] args, List<string> rows) { }
        private static void Snapshot(List<string> rows) { }
    }
}
namespace PaneScrapeBridge.Tests
{
    internal sealed class ProbeBoundary : IDisposable
    {
        private static readonly Type Probe = typeof(WinterMP.GuestSaveProbe.LiveBagProbe);
        private const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
        private readonly SessionManager? _oldSession;
        private readonly string _oldRoot;
        private readonly Dictionary<string, string?> _environment = new();
        public readonly string Root;
        public bool Armed => (bool?)Probe.GetField("_paneWrongPane", Flags)?.GetValue(null) ?? false;
        public ProbeBoundary(SessionManager session)
        {
            _oldSession = SessionManager.Instance; SessionManager.Instance = session;
            _oldRoot = BepInEx.Paths.GameRootPath;
            Root = Path.Combine(AppContext.BaseDirectory, "probe-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root); BepInEx.Paths.GameRootPath = Root;
            foreach (string name in new[] { "WINTERMP_LOCAL2P_BAG_TEST", "WINTERMP_LOCAL2P_V11_PANE_TEST" })
            {
                _environment[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, "1");
            }
            foreach (string name in new[] { "wintermp-live-bag-sandbox.txt", "wintermp-v11-pane-sandbox.txt" })
                File.WriteAllText(Path.Combine(Root, name), "Portable marker fixture only; NOT a game directory.\n");
            HarmonyLib.Harmony.Patches.Clear();
            Probe.GetField("_paneTracing", Flags)!.SetValue(null, false);
            BepInEx.Logging.Logger.ProbeLog.Lines.Clear(); BepInEx.Logging.Logger.ProbeLog.Fail = false;
            Probe.GetMethod("ClearPaneMutation", Flags)?.Invoke(null, null);
            try { Invoke("StartPaneTrace"); }
            catch { Dispose(); throw; }
        }
        public static object? Invoke(string name, params object[] arguments)
        {
            var method = Probe.GetMethod(name, Flags);
            Assert.True(method != null, "Source-linked diagnostic missing: " + name);
            try { return method!.Invoke(null, arguments); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        public void Arm() => Invoke("ArmPaneWrongPane");
        public static object? Read(string name) => Probe.GetField(name, Flags)!.GetValue(null);
        public IMessage Observe(IMessage message)
        {
            var patch = Assert.Single(HarmonyLib.Harmony.Patches, p => p.Original.Name == "SendWorldMessage");
            Assert.NotNull(patch.Prefix);
            Assert.Equal("message", Assert.Single(patch.Prefix!.Method.GetParameters()).Name);
            object[] args = { message }; Invoke(patch.Prefix.Method.Name, args); return (IMessage)args[0];
        }
        public void Lifecycle(string name)
        {
            var patch = Assert.Single(HarmonyLib.Harmony.Patches, p => p.Original.Name == name);
            Assert.NotNull(patch.Prefix);
            patch.Prefix!.Method.Invoke(null, null);
        }
        public void Failure(string name, Exception? error)
        {
            var patch = Assert.Single(HarmonyLib.Harmony.Patches, p => p.Original.Name == name);
            Assert.NotNull(patch.Finalizer);
            patch.Finalizer!.Method.Invoke(null, new object?[] { error });
        }
        public string[] Snapshot()
        {
            var rows = new List<string>(); Invoke("PaneTraceSnapshot", rows); return rows.ToArray();
        }
        public void Dispose()
        {
            Probe.GetMethod("ClearPaneMutation", Flags)?.Invoke(null, null);
            Probe.GetField("_paneLastStroke", Flags)!.SetValue(null, null);
            Probe.GetField("_paneTracing", Flags)!.SetValue(null, false);
            HarmonyLib.Harmony.Patches.Clear();
            foreach (var pair in _environment) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            BepInEx.Paths.GameRootPath = _oldRoot; SessionManager.Instance = _oldSession;
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
            BepInEx.Logging.Logger.ProbeLog.Fail = false;
        }
    }
}
