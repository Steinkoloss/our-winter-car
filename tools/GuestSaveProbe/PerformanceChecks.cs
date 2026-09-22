using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    // Synthetic discovery workload in the actual Unity/Mono runtime. No save or
    // scene loading; this does not measure rendering, physics or Steam latency.
    internal static partial class PerformanceChecks
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        private static int _observed;

        internal static void Run(string output, Action<string, Action> check)
        {
            if (!File.Exists(Path.Combine(BepInEx.Paths.GameRootPath, "wintermp-performance-sandbox.txt")))
                throw new InvalidOperationException("Performance probe requires an explicitly marked isolated game copy.");
            var rows = new List<string> { "objects,workload,samples,median_ms,p95_ms,max_ms,gen0_collections,heap_delta_bytes" };
            var paths = Core.GetType("WinterMP.Core.Sync.ScenePath", true);
            var scan = (Func<IEnumerable<UnityEngine.Object>>)Delegate.CreateDelegate(typeof(Func<IEnumerable<UnityEngine.Object>>), paths.GetMethod("ScanFsms", Static));
            var path = (Func<Transform, string>)Delegate.CreateDelegate(typeof(Func<Transform, string>), paths.GetMethod("Of", Static));
            var findData = (Func<Transform, PlayMakerFSM>)Delegate.CreateDelegate(typeof(Func<Transform, PlayMakerFSM>),
                Core.GetType("WinterMP.Core.Sync.NativePartIdentity", true).GetMethod("FindData", Static));
            var worldType = Core.GetType("WinterMP.Core.Sync.WorldSyncManager", true);
            var instance = worldType.GetProperty("Instance", Static);
            var previous = instance.GetValue(null, null);
            var session = SessionManager.Instance;
            if (session == null) throw new InvalidOperationException("Missing solo session.");
            var host = typeof(SessionManager).GetProperty("IsHost", Members);
            bool wasHost = session.IsHost;
            var controller = new GameObject("performance controller"); controller.SetActive(false);
            var world = controller.AddComponent(worldType); ((Behaviour)world).enabled = false;
            check("performance readiness: part prompt is safe before world initialization", () =>
                worldType.GetMethod("DrawPartFitPrompt", Members).Invoke(world, null));
            worldType.GetMethod("EnsureSyncReady", Members).Invoke(world, null);
            CheckCatalogIndex(check);
            CheckUninitializedActions(check);
            GuestEngineInputChecks.MeasureEngineBlockReads((count, name, action) => Measure(rows, count, name, action), check);
            MeasureRelativePaths(rows, check);
            MeasureFullPaths(rows, check);
            MeasureProtectionRules(rows, check);
            MeasureEngineMetadata(rows, check);
            MeasureUnrelatedEngineInputs(rows, worldType.GetField("_items", Members).GetValue(world), check);
            MeasureProtectionPasses(rows, check);
            CheckVehicleDiscovery(check);
            CheckVehicleSourceLookup(check);
            var scanWorld = (Action)Delegate.CreateDelegate(typeof(Action), world, worldType.GetMethod("ScanWorld", Members));
            try
            {
                CheckWorldDiscovery(world, scanWorld, check);
                CheckWorldDiscoveryScheduling(world, scanWorld, check);
                host.SetValue(session, true, null);
                foreach (int count in new[] { 1000, 5000 })
                {
                    var root = new GameObject("Performance fixture"); root.SetActive(false);
                    try
                    {
                        var nodes = new List<PlayMakerFSM>();
                        for (int group = 0; nodes.Count < count; group++)
                        {
                            var parent = Child(root, "Group" + group);
                            for (int depth = 0; depth < 4; depth++) parent = Child(parent, "Nested" + depth);
                            for (int child = 0; child < 25 && nodes.Count < count; child++)
                            {
                                var obj = Child(parent, "Object" + child);
                                var fsm = obj.AddComponent<PlayMakerFSM>();
                                fsm.enabled = false;
                                typeof(PlayMakerFSM).GetField("fsm", Members).SetValue(fsm, new Fsm());
                                fsm.Fsm.Name = child % 5 == 0 ? "Use" : "PerformanceIdle";
                                fsm.Fsm.StartState = "Idle";
                                fsm.Fsm.States = new[] { new FsmState(fsm.Fsm) { Name = "Idle", Actions = new FsmStateAction[0] } };
                                nodes.Add(fsm);
                            }
                        }
                        root.SetActive(true);
                        foreach (var fsm in nodes) { fsm.enabled = true; if (!fsm.Fsm.Started) fsm.Fsm.Start(); }
                        MeasureSceneSearch(rows, count, root, check);
                        Measure(rows, count, "enumerate-fsms", () => { int n = 0; foreach (var obj in scan()) n++; _observed = n; });
                        check("performance: discovery includes " + count + " native fixture FSMs", () => Require(_observed >= count));
                        Measure(rows, count, "scene-paths", () => { foreach (var obj in scan()) if (obj is PlayMakerFSM fsm) _observed = path(fsm.transform).Length; });
                        Measure(rows, count, "part-parent-lookup", () => { foreach (var fsm in nodes) if (findData(fsm.transform) != null) _observed++; });
                        Measure(rows, count, "host-world-scan-active", scanWorld);
                        MeasureGuestWorldScan(rows, count, scanWorld);
                        foreach (var fsm in nodes) fsm.enabled = false;
                        Measure(rows, count, "host-world-scan-disabled", scanWorld);
                        MeasurePeriodicDiscovery(rows, count, world);
                        MeasureVehicleDiscovery(rows, count, root);
                        MeasureVehicleSourceLookup(rows, count, root);
                        var items = worldType.GetField("_items", Members).GetValue(world);
                        foreach (string method in new[] { "RefreshBagFactories", "RefreshTrophyFactories", "RefreshPackageFactories", "RefreshReplacementFactories", "ScanNativeParts" })
                        {
                            var target = items.GetType().GetMethod(method, Members);
                            if (target != null) Measure(rows, count, method, (Action)Delegate.CreateDelegate(typeof(Action), items, target));
                        }
                        check("performance: unrelated fixture FSMs remain undiscovered as synced objects " + count, () =>
                            Require((int)worldType.GetProperty("DoorCount", Members).GetValue(world, null) == 0));
                    }
                    finally { UnityEngine.Object.DestroyImmediate(root); }
                }
                CheckPeriodicDiscovery(worldType.GetField("_items", Members).GetValue(world), check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-bound-input-performance") >= 0
                    || Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-bound-input-breakdown") >= 0)
                    GuestEngineInputChecks.MeasureBoundInputs((count, name, action) => Measure(rows, count, name, action), check);
            }
            finally
            {
                host.SetValue(session, wasHost, null);
                UnityEngine.Object.DestroyImmediate(controller);
                instance.SetValue(null, previous, null);
                File.WriteAllLines(Path.Combine(output, "performance.csv"), rows.ToArray());
            }
        }

        internal static void Measure(List<string> rows, int count, string name, Action action)
        {
            for (int i = 0; i < 3; i++) action();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var times = new double[21];
            int collections = GC.CollectionCount(0); long memory = GC.GetTotalMemory(false);
            for (int i = 0; i < times.Length; i++)
            {
                long start = Stopwatch.GetTimestamp(); action();
                times[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            }
            long delta = GC.GetTotalMemory(false) - memory; collections = GC.CollectionCount(0) - collections;
            Array.Sort(times);
            rows.Add(string.Join(",", new[] { count.ToString(), name, times.Length.ToString(),
                times[10].ToString("F3", CultureInfo.InvariantCulture), times[19].ToString("F3", CultureInfo.InvariantCulture),
                times[20].ToString("F3", CultureInfo.InvariantCulture), collections.ToString(), delta.ToString() }));
        }

        private static GameObject Child(GameObject parent, string name)
        { var child = new GameObject(name); child.transform.SetParent(parent.transform, false); return child; }
        private static void Require(bool value) { if (!value) throw new InvalidOperationException("Performance fixture invariant failed."); }
    }
}
