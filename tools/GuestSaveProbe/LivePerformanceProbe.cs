using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    // Opt-in observations only. Inclusive timings overlap and include Harmony
    // overhead; compare identical instrumentation and exclude startup/warmup.
    internal sealed partial class LivePerformanceProbe : IDisposable
    {
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private const string PatchId = "wintermp.probe.live-performance";
        private sealed class Cost
        {
            internal long Calls, Ticks, Maximum, HeapCalls, HeapGrowth, HeapMaximum, HeapTicks, CollectedCalls, DecreasedCalls;
        }
        private struct FrameEvent
        {
            internal double Seconds, Milliseconds;
            internal int Collections;
        }
        private static readonly Dictionary<MethodBase, Cost> Costs = new Dictionary<MethodBase, Cost>();
        private static readonly HashSet<FsmStateAction> ReportedLooking = new HashSet<FsmStateAction>();
        private static LivePerformanceProbe? _instance;
        private readonly List<double> _frames = new List<double>(16384);
        private readonly List<FrameEvent> _frameEvents = new List<FrameEvent>(256);
        private readonly List<string> _observations = new List<string>();
        private Harmony? _harmony;
        private string _output = string.Empty, _role = string.Empty;
        private float _gameAt = -1f, _sampleAt = -1f;
        private int _collections, _lastCollections;
        private bool _finished, _cameraCaptured;
        private long _lastFrame;
        private int _cameraWrites;
        private readonly HashSet<string> _uninitializedReads = new HashSet<string>();
        private readonly Dictionary<PlayMakerFSM, string> _engineFailureReasons = new Dictionary<PlayMakerFSM, string>();
        private static bool Sampling => _instance != null && _instance._sampleAt >= 0f && !_instance._finished;

        internal void Start()
        {
            if (!File.Exists(Path.Combine(BepInEx.Paths.GameRootPath, "wintermp-live-performance-sandbox.txt")))
                throw new InvalidOperationException("Live profiling requires an explicitly marked isolated game copy.");
            _output = Path.Combine(BepInEx.Paths.GameRootPath, "live-performance"); Directory.CreateDirectory(_output);
            _role = Environment.GetEnvironmentVariable("WINTERMP_LOG_ROLE") == "guest" ? "guest" : "host";
            _instance = this; Costs.Clear(); ReportedLooking.Clear();
            _harmony = new Harmony(PatchId);
            bool framesOnly = Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-live-performance-frames-only") >= 0;
            _heapProfile = Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-live-performance-heap") >= 0;
            if (_heapProfile && framesOnly) throw new InvalidOperationException("Choose either heap diagnostics or frame-only control.");
            if (_heapProfile) InstallHeapDiagnostics();
            else if (!framesOnly) InstallDiagnostics();
            if (_heapProfile || !framesOnly) InstallDiscoveryDiagnostics();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-live-performance-solo") >= 0)
            {
                var gate = Assembly.Load("WinterMP.FastBoot").GetType("WinterMP.FastBoot.SessionGate", true);
                _harmony.Patch(gate.GetMethod("CanAutoLoadContinue", Members), new HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("AllowSoloContinue", Members)));
                _observations.Add("Solo control: allowed native Continue automation while the multiplayer session stays idle.");
            }
            _observations.Add(_heapProfile ? "Heap diagnostics: selected method timings and process heap growth; no interaction/camera hooks."
                : framesOnly ? "Frame-only control: no method, interaction or camera diagnostic hooks."
                : "Detailed diagnostics: method, interaction and camera hooks enabled.");
            _observations.Add("Instrumented " + Costs.Count + " methods; timings do not suppress native exceptions.");
            CaptureDisplay();
            SaveObservations();
        }

        private void InstallDiagnostics()
        {
            if (_harmony == null) throw new InvalidOperationException("Live diagnostics were not initialized.");
            _harmony.Patch(typeof(FsmState).GetProperty("Actions").GetGetMethod(),
                new HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("ObserveUninitializedActions", Members)));
            var core = typeof(SessionManager).Assembly;
            var world = core.GetType("WinterMP.Core.Sync.WorldSyncManager", true);
            var types = new HashSet<Type> { world };
            foreach (var field in world.GetFields(Members))
                if (field.FieldType.Namespace == "WinterMP.Core.Sync") types.Add(field.FieldType);
            types.Add(core.GetType("WinterMP.Core.Sync.GuestEngineProtection", true));
            var prefix = new HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("Begin", Members));
            var end = new HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("End", Members));
            foreach (var type in types)
                foreach (var method in type.GetMethods(Members | BindingFlags.DeclaredOnly))
                {
                    if (method.IsGenericMethod || method.IsAbstract || method.GetMethodBody() == null) continue;
                    if (!(method.Name.StartsWith("Update", StringComparison.Ordinal) || method.Name.StartsWith("Scan", StringComparison.Ordinal)
                        || method.Name.StartsWith("Refresh", StringComparison.Ordinal) || method.Name.StartsWith("Prepare", StringComparison.Ordinal)
                        || method.Name.StartsWith("Process", StringComparison.Ordinal) || method.Name.StartsWith("Isolate", StringComparison.Ordinal)
                        || method.Name == "EnsureSyncReady" || method.Name == "WatchLevelChanges"
                        || method.Name == "Reassert" || method.Name == "BuildStateChecksum")) continue;
                    Costs.Add(method, new Cost());
                    _harmony.Patch(method, prefix: prefix, finalizer: end);
                }
            var looking = Assembly.Load("Assembly-CSharp").GetType("HutongGames.PlayMaker.Actions.WherePlayerIsLooking", true);
            _harmony.Patch(looking.GetMethod("OnUpdate"), new HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("ObserveLooking", Members)));
            var setProperty = Assembly.Load("Assembly-CSharp").GetType("HutongGames.PlayMaker.Actions.SetProperty", true);
            _harmony.Patch(setProperty.GetMethod("OnEnter"), new HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("ObserveCameraWrite", Members)));
            _harmony.Patch(setProperty.GetMethod("OnUpdate"), new HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("ObserveCameraWrite", Members)));
            var enable = Assembly.Load("Assembly-CSharp").GetType("HutongGames.PlayMaker.Actions.EnableBehaviour", true);
            foreach (string method in new[] { "OnEnter", "OnExit" })
                _harmony.Patch(enable.GetMethod(method), postfix: new HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("ObserveCameraEnable", Members)));
            var reflection = typeof(Fsm).Assembly.GetType("HutongGames.PlayMaker.ReflectionUtils", true);
            _harmony.Patch(reflection.GetMethod("SetMemberValue", new[] { typeof(MemberInfo), typeof(object), typeof(object) }),
                new HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("ObserveReflectedCameraWrite", Members)));
            var cut = Assembly.Load("Assembly-CSharp").GetType("HutongGames.PlayMaker.Actions.CutToCamera", true);
            foreach (string method in new[] { "OnEnter", "OnExit" })
                _harmony.Patch(cut.GetMethod(method), new HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("ObserveCameraCut", Members)));
            var guard = core.GetType("WinterMP.Core.Sync.GuestEngineProtection", true);
            _harmony.Patch(guard.GetMethod("Fail", Members),
                new HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("ObserveEngineFailure", Members)));
            // Older comparison binaries report waits through Fail; current ones
            // can pause without throwing an exception during routine preparation.
            var wait = guard.GetMethod("WaitForInputs", Members);
            if (wait != null) _harmony.Patch(wait,
                new HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("ObserveEngineInputWait", Members)));
        }

        private static void ObserveEngineFailure(PlayMakerFSM fsm, Exception error)
        {
            if (error != null) ObserveEngineInputWait(fsm, error.Message);
        }

        private static void ObserveEngineInputWait(PlayMakerFSM fsm, string reason)
        {
            var probe = _instance;
            if (probe == null || probe._finished || fsm == null || reason == null || probe._engineFailureReasons.Count >= 64) return;
            probe._engineFailureReasons[fsm] = reason;
        }

        private static void Begin(out CallSample __state) { __state = Sampling
            ? new CallSample { Stamp = Stopwatch.GetTimestamp(), Collections = GC.CollectionCount(0) } : default(CallSample); }
        private static void ObserveUninitializedActions(FsmState __instance)
        {
            var probe = _instance;
            if (probe == null || probe._finished || __instance.IsInitialized || probe._uninitializedReads.Count >= 16) return;
            string trace = __instance.Name + Environment.NewLine + new StackTrace();
            if (!probe._uninitializedReads.Add(trace)) return;
            probe._observations.Add("Uninitialized actions read: " + trace);
            probe.SaveObservations();
        }
        private static void End(MethodBase __originalMethod, CallSample __state)
        {
            if (__state.Stamp == 0) return;
            long ended = Stopwatch.GetTimestamp(), ticks = ended - __state.Stamp;
            var cost = Costs[__originalMethod]; cost.Calls++; cost.Ticks += ticks;
            if (ticks > cost.Maximum) cost.Maximum = ticks;
            _instance?.RecordSlowCall(__originalMethod, __state, ended);
        }

        private static void ObserveLooking(FsmStateAction __instance)
        {
            var probe = _instance;
            if (probe == null || probe._finished || ReportedLooking.Contains(__instance) || ReportedLooking.Count >= 32) return;
            var direction = __instance.GetType().GetField("direction").GetValue(__instance) as FsmVector3;
            var camera = Camera.main;
            if (camera != null && direction != null) return;
            ReportedLooking.Add(__instance);
            var owner = __instance.Fsm?.Owner as PlayMakerFSM;
            probe._observations.Add("looking failure at " + Time.realtimeSinceStartup.ToString("F1", CultureInfo.InvariantCulture)
                + "s: " + (owner != null ? PathOf(owner.transform) + "::" + owner.FsmName : "no owner")
                + "; state=" + __instance.State?.Name + "; active=" + owner?.ActiveStateName
                + "; Camera.main=" + (camera != null ? PathOf(camera.transform) : "NULL")
                + "; direction=" + (direction != null ? direction.Name + " (bound)" : "NULL"));
            probe.CaptureCameras(); probe.SaveObservations();
        }

        private static void ObserveCameraWrite(FsmStateAction __instance)
        {
            var probe = _instance;
            if (probe == null || probe._finished || probe._cameraWrites >= 64) return;
            var property = __instance.GetType().GetField("targetProperty").GetValue(__instance) as FsmProperty;
            var camera = property?.TargetObject?.Value as Camera;
            if (camera == null || property!.PropertyName != "enabled") return;
            probe._cameraWrites++;
            var owner = __instance.Fsm?.Owner as PlayMakerFSM;
            probe._observations.Add("camera write at " + Time.realtimeSinceStartup.ToString("F1", CultureInfo.InvariantCulture)
                + "s: " + (owner != null ? PathOf(owner.transform) + "::" + owner.FsmName : "no owner")
                + "; state=" + __instance.State?.Name + "; target=" + PathOf(camera.transform)
                + "; enabled=" + property.BoolParameter.Value);
            probe.SaveObservations();
        }

        private static bool AllowSoloContinue(ref bool __result) { __result = true; return false; }
        private static void ObserveReflectedCameraWrite(MemberInfo member, object target, object value)
        {
            var probe = _instance;
            if (probe == null || probe._finished || probe._cameraWrites >= 64 || member.Name != "enabled" || !(target is Camera camera)) return;
            if (!(value is bool enabled) || enabled) return;
            probe._cameraWrites++;
            probe._observations.Add("reflected camera disable at " + Time.realtimeSinceStartup.ToString("F1", CultureInfo.InvariantCulture)
                + "s: " + PathOf(camera.transform) + Environment.NewLine + new StackTrace());
            probe.SaveObservations();
        }
        private static void ObserveCameraEnable(FsmStateAction __instance, MethodBase __originalMethod)
        {
            var probe = _instance; if (probe == null || probe._finished || probe._cameraWrites >= 64) return;
            var camera = __instance.GetType().GetField("componentTarget", Members).GetValue(__instance) as Camera;
            if (camera == null) return;
            probe._cameraWrites++;
            var owner = __instance.Fsm?.Owner as PlayMakerFSM;
            probe._observations.Add("camera enable " + __originalMethod.Name + " at " + Time.realtimeSinceStartup.ToString("F1", CultureInfo.InvariantCulture)
                + "s: " + (owner != null ? PathOf(owner.transform) + "::" + owner.FsmName : "no owner")
                + "; state=" + __instance.State?.Name + "; target=" + PathOf(camera.transform) + "; enabled=" + camera.enabled);
            probe.SaveObservations();
        }
        private static void ObserveCameraCut(FsmStateAction __instance, MethodBase __originalMethod)
        {
            var probe = _instance; if (probe == null || probe._finished || probe._cameraWrites >= 64) return;
            probe._cameraWrites++;
            var owner = __instance.Fsm?.Owner as PlayMakerFSM;
            var target = __instance.GetType().GetField("camera").GetValue(__instance) as Camera;
            probe._observations.Add("camera cut " + __originalMethod.Name + " at " + Time.realtimeSinceStartup.ToString("F1", CultureInfo.InvariantCulture)
                + "s: " + (owner != null ? PathOf(owner.transform) + "::" + owner.FsmName : "no owner")
                + "; state=" + __instance.State?.Name + "; target=" + (target != null ? PathOf(target.transform) : "NULL"));
            probe.SaveObservations();
        }

        internal void Tick()
        {
            if (_finished || Application.loadedLevelName != "GAME") return;
            float now = Time.realtimeSinceStartup;
            if (_gameAt < 0f) _gameAt = now;
            if (!_cameraCaptured && now - _gameAt >= 5f) { _cameraCaptured = true; CaptureCameras(); SaveObservations(); }
            if (now - _gameAt < 15f) return;
            long stamp = Stopwatch.GetTimestamp();
            if (_sampleAt < 0f)
            {
                _sampleAt = now; _collections = _lastCollections = GC.CollectionCount(0); _lastFrame = stamp;
                _sampleStamp = stamp;
                if (_heapProfile) _frameHeap = ReadHeap();
                return;
            }
            double interval = (stamp - _lastFrame) * 1000.0 / Stopwatch.Frequency;
            int currentCollections = GC.CollectionCount(0), frameCollections = currentCollections - _lastCollections;
            _frames.Add(interval); _lastFrame = stamp; _lastCollections = currentCollections;
            if (_heapProfile) RecordFrameHeap();
            if (interval >= 50 || frameCollections > 0)
                _frameEvents.Add(new FrameEvent { Seconds = now - _sampleAt, Milliseconds = interval, Collections = frameCollections });
            if (now - _sampleAt < 60f) return;
            int collections = currentCollections - _collections;
            _finished = true;
            CaptureDisplay(); CaptureCameras();
            _frames.Sort();
            var rows = new List<string> { "method,calls,total_ms,mean_ms,max_ms" };
            foreach (var pair in Costs)
            {
                var c = pair.Value; if (c.Calls == 0) continue;
                double ms = c.Ticks * 1000.0 / Stopwatch.Frequency;
                rows.Add(pair.Key.DeclaringType.Name + "." + pair.Key.Name + "," + c.Calls + "," + Number(ms)
                    + "," + Number(ms / c.Calls) + "," + Number(c.Maximum * 1000.0 / Stopwatch.Frequency));
            }
            File.WriteAllLines(Path.Combine(_output, _role + "-methods.csv"), rows.ToArray());
            WriteSlowCalls();
            if (_heapProfile) WriteHeapCosts();
            var frames = new List<string> { "frame_count,seconds,median_ms,p95_ms,max_ms,gen0_collections",
                _frames.Count + "," + Number(now - _sampleAt) + "," + Number(_frames[_frames.Count / 2]) + ","
                + Number(_frames[(int)Math.Ceiling(_frames.Count * .95) - 1]) + "," + Number(_frames[_frames.Count - 1])
                + "," + collections };
            File.WriteAllLines(Path.Combine(_output, _role + "-frames.csv"), frames.ToArray());
            var events = new List<string> { "seconds,frame_ms,gen0_collections" };
            foreach (var entry in _frameEvents)
                events.Add(Number(entry.Seconds) + "," + Number(entry.Milliseconds) + "," + entry.Collections);
            File.WriteAllLines(Path.Combine(_output, _role + "-frame-events.csv"), events.ToArray());
            CaptureFactoryReadiness(); WriteDiscoveryFrames();
            _observations.Add("Completed sixty-second sample after fifteen-second GAME warmup."); SaveObservations(); Dispose();
        }

        private void CaptureFactoryReadiness()
        {
            try
            {
                var core = typeof(SessionManager).Assembly;
                var world = core.GetType("WinterMP.Core.Sync.WorldSyncManager", true).GetProperty("Instance", Members).GetValue(null, null);
                if (world == null) { _observations.Add("Factory readiness: world unavailable."); return; }
                var items = world.GetType().GetField("_items", Members).GetValue(world);
                var factories = (IDictionary)items.GetType().GetField("_replacementFactories", Members).GetValue(items);
                int failed = 0, decoded = 0;
                foreach (DictionaryEntry entry in factories)
                {
                    var factory = entry.Value; var type = factory.GetType();
                    bool invalid = (bool)type.GetField("Failed", Members).GetValue(factory);
                    var data = type.GetField("TemplateData", Members).GetValue(factory) as PlayMakerFSM;
                    if (data != null && data.Fsm.Initialized) decoded++;
                    if (!invalid) continue;
                    failed++;
                    var rule = type.GetField("Rule", Members).GetValue(factory);
                    _observations.Add("Failed replacement factory: " + rule.GetType().GetField("Prefix", Members).GetValue(rule));
                }
                _observations.Add("Replacement factories: bound=" + factories.Count + "; decoded=" + decoded + "; failed=" + failed);
                foreach (string name in new[] { "_guestEngineInputs", "_replacementParts", "_nativeParts", "_isolatedGuestParts" })
                    _observations.Add(name + "=" + ((ICollection)items.GetType().GetField(name, Members).GetValue(items)).Count);
                var nativePhase = core.GetType("WinterMP.Core.Sync.NativePartIdentity", true).GetMethod("Phase", Members);
                var supportedPart = items.GetType().GetMethod("IsReplacementPart", Members);
                int listed = 0;
                foreach (DictionaryEntry entry in (IDictionary)items.GetType().GetField("_nativeParts", Members).GetValue(items))
                {
                    if (listed++ >= 64) break;
                    var data = entry.Value as PlayMakerFSM; if (data == null) continue;
                    _observations.Add("Native part readiness: " + data.FsmVariables.FindFsmString("ID")?.Value
                        + "; path=" + PathOf(data.transform) + "; phase=" + nativePhase.Invoke(null, new object[] { data })
                        + "; replacement=" + supportedPart.Invoke(items, new object[] { data })
                        + "; state=" + data.ActiveStateName + "; active=" + data.gameObject.activeInHierarchy);
                }
                var guard = core.GetType("WinterMP.Core.Sync.GuestEngineProtection", true);
                _observations.Add("Dormant guest engines awaiting native initialization="
                    + ((IDictionary)guard.GetField("PendingInitializations", Members).GetValue(null)).Count);
                var failures = (IDictionary)guard.GetField("Failures", Members).GetValue(null);
                _observations.Add("Guest engine protection failures=" + failures.Count);
                foreach (DictionaryEntry entry in failures)
                    if (entry.Key is PlayMakerFSM fsm && fsm != null)
                    {
                        _observations.Add("Paused guest engine: " + PathOf(fsm.transform) + "::" + fsm.FsmName
                            + "; initialized=" + fsm.Fsm.Initialized + "; started=" + fsm.Fsm.Started
                            + "; objectActive=" + fsm.gameObject.activeInHierarchy
                            + "; reason=" + (_engineFailureReasons.TryGetValue(fsm, out var reason) ? reason : "not observed"));
                    }
                foreach (DictionaryEntry entry in (IDictionary)guard.GetField("Bindings", Members).GetValue(null))
                    if (entry.Key is PlayMakerFSM fsm && fsm != null && fsm.FsmName == "Cooling")
                        CaptureFactoryMounts(fsm, factories);
                CaptureVehicleIgnition(items);
            }
            catch (Exception error) { _observations.Add("Factory readiness observation failed: " + error.Message); }
        }

        private void CaptureVehicleIgnition(object items)
        {
            int listed = 0;
            foreach (DictionaryEntry entry in (IDictionary)ProbeField(items, "_items"))
            {
                var item = entry.Value;
                if (!(bool)ProbeField(item, "IsVehicle")) continue;
                if (listed++ >= 64) break;
                var power = ProbeField(item, "ElectricityPowerFsm") as PlayMakerFSM;
                _observations.Add("Vehicle ignition: " + ProbeField(item, "Path")
                    + "; local=" + ProbeField(item, "LocallyOwned")
                    + "; accepted=" + (ProbeField(item, "AcceptedVehicleState") != null)
                    + "; requestedOn=" + ((bool)ProbeField(item, "RemoteEngineOn") || (bool)ProbeField(item, "RemoteAccOn"))
                    + "; applied=" + ProbeField(item, "HasRemoteElectricsState")
                    + "; appliedOn=" + ProbeField(item, "RemoteElectricsApplied")
                    + "; nativeInitialized=" + (power != null && power.Fsm.Initialized)
                    + "; nativeStarted=" + (power != null && power.Fsm.Started)
                    + "; nativeActive=" + (power != null && power.gameObject.activeInHierarchy)
                    + "; nativeState=" + (power == null ? "missing" : power.ActiveStateName));
            }
        }

        private void CaptureFactoryMounts(PlayMakerFSM consumer, IDictionary factories)
        {
            var catalog = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
            var profile = catalog.GetProperty("GuestEngineInputs", Members).GetValue(null, null);
            if (profile == null) return;
            string path = PathOf(consumer.transform);
            foreach (var rule in (IEnumerable)ProbeField(profile, "Entries"))
            {
                if ((string)ProbeField(rule, "Fsm") != consumer.FsmName || (string)ProbeField(rule, "ReaderPath") != path) continue;
                string target = (string)ProbeField(rule, "TargetVariable"), mountVariable = (string)ProbeField(rule, "MountVariable");
                var source = consumer.FsmVariables.FindFsmGameObject(target)?.Value;
                foreach (var family in (IEnumerable)ProbeField(rule, "Families"))
                    foreach (DictionaryEntry pair in factories)
                    {
                        if (!ReferenceEquals(ProbeField(pair.Value, "Rule"), family)) continue;
                        var factory = (PlayMakerFSM)ProbeField(pair.Value, "Fsm");
                        var mount = factory.FsmVariables.FindFsmGameObject(mountVariable)?.Value;
                        _observations.Add("Engine factory mount: " + consumer.FsmName + "." + target + "; family=" + ProbeField(family, "Prefix")
                            + "; consumer=" + (source != null ? PathOf(source.transform) : "NULL")
                            + "; factory=" + (mount != null ? PathOf(mount.transform) : "NULL") + "; equal=" + (source == mount));
                    }
            }
        }

        private static object ProbeField(object value, string name) => value.GetType().GetField(name, Members).GetValue(value);

        private void CaptureCameras()
        {
            _observations.Add("Cameras at " + Time.realtimeSinceStartup.ToString("F1", CultureInfo.InvariantCulture) + "s:");
            foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(Camera)))
            {
                var c = (Camera)obj;
                _observations.Add(PathOf(c.transform) + "; tag=" + c.tag + "; enabled=" + c.enabled
                    + "; active=" + c.gameObject.activeInHierarchy);
            }
        }
        private void CaptureDisplay()
        {
            var camera = Camera.main;
            _observations.Add("Display at " + Time.realtimeSinceStartup.ToString("F1", CultureInfo.InvariantCulture)
                + "s: count=" + Display.displays.Length + "; screen=" + Screen.width + "x" + Screen.height
                + "; mainCamera=" + (camera != null ? PathOf(camera.transform) : "NULL"));
        }
        private static string PathOf(Transform node)
        {
            string path = node.name;
            while (node.parent != null) { node = node.parent; path = node.name + "/" + path; }
            return path;
        }
        private static string Number(double value) => value.ToString("F3", CultureInfo.InvariantCulture);
        private void SaveObservations() => File.WriteAllLines(Path.Combine(_output, _role + "-observations.txt"), _observations.ToArray());
        public void Dispose()
        {
            _harmony?.UnpatchSelf(); _harmony = null;
            if (_heapProfile) EngineConsumers.Clear();
            if (ReferenceEquals(_instance, this)) _instance = null;
        }
    }
}
