using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using HarmonyLib;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LivePerformanceProbe
    {
        private struct HeapSample
        {
            internal long Stamp, Bytes;
            internal int Collections, OutsideScan;
            internal bool Forced, Electricity;
            internal MethodBase? Parent;
            internal CallerCost? Caller;
            internal int EngineOrigin;
            internal Cost? EngineConsumer;
        }
        private sealed class CallerCost
        {
            internal readonly Cost All = new Cost(), OutsideScan = new Cost();
        }
        [ThreadStatic] private static MethodBase? _heapParent;
        private static readonly Dictionary<MethodBase, CallerCost> PathCallers = new Dictionary<MethodBase, CallerCost>();
        private static readonly Dictionary<MethodBase, CallerCost> RuleCallers = new Dictionary<MethodBase, CallerCost>();
        private static CallerCost _rootPathCalls = new CallerCost(), _rootRuleCalls = new CallerCost();
        private bool _heapProfile;
        private HeapSample _frameHeap;
        private readonly Cost _frameHeapCost = new Cost();
        private static FieldInfo? _scanPathsField;
        private static MethodInfo? _ruleLookupMethod, _pathMethod;
        private static Cost _outsideScanRules = new Cost(), _outsideScanPaths = new Cost();
        private static Cost _forcedPreparation = new Cost(), _electricityPreparation = new Cost();
        private static int _electricityDepth, _electricityFrame = -1, _electricityFrames, _electricityFrameCalls, _electricityFrameMaximum;

        private void InstallHeapDiagnostics()
        {
            if (_harmony == null) throw new InvalidOperationException("Heap diagnostics were not initialized.");
            CheckHeapCounter();
            var paths = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Sync.ScenePath", true);
            _scanPathsField = paths.GetField("_scanPaths", Members) ?? throw new InvalidOperationException("Missing path scan scope.");
            _pathMethod = paths.GetMethod("Of", Members);
            _ruleLookupMethod = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Sync.GuestEngineProtection", true).GetMethod("FindRule", Members);
            _outsideScanRules = new Cost(); _outsideScanPaths = new Cost();
            _forcedPreparation = new Cost(); _electricityPreparation = new Cost();
            _electricityDepth = _electricityFrames = _electricityFrameCalls = _electricityFrameMaximum = 0; _electricityFrame = -1;
            _heapParent = null; PathCallers.Clear(); RuleCallers.Clear();
            _rootPathCalls = new CallerCost(); _rootRuleCalls = new CallerCost();
            InitializeEngineInputDiagnostics();
            var targets = new Dictionary<string, string[]>
            {
                { "WorldSyncManager", new[] { "UpdateWorldSync", "ScanWorld", "UpdateWorldDiscovery", "ScanWorldCore", "ScanWorldObjects" } },
                { "GuestEngineProtection", new[] { "PrepareCore", "Reassert", "GuardStateEntry", "PrepareBoundInputs", "FindRule", "Disable", "FinishActive", "RetireExternalFloatTargets",
                    "BeforeExternalFloatWrite", "IsProtectedFsm", "Bind", "ValidateDrivetrainProtection", "ProjectDrivetrainWearRead", "ProjectWheelHealthRead",
                    "IsBatteryReadSource", "IsHeaterReadSource", "FindWireReadSource", "FindHeaterHoseReadSource", "ProjectRearWindowRead" } },
                { "ItemWorldSync", new[] { "ScanItems", "PrepareGuestEngineInputFsm", "PrepareGuestEngineInputRead", "PrepareGuestEngineInputSource", "ValidateGuestEngineInputs",
                    "ValidateGuestEngineInputMount", "GuestEngineInputActionsCurrent", "UpdateGuestEngineInputValues", "GuestEngineSlotMount", "UpdateItems", "ProcessPendingSpawns",
                    "ProcessPackageRemovals", "ProcessPackages", "ProcessReplacementParts", "ProcessPackageOpening", "ProcessPartFitting", "ProcessTrophySpawns",
                    "ProcessWiring", "ProcessBattery", "ProcessHeater", "ProcessEngineBlock", "ProcessGearbox", "ProcessReplacementOutputs", "ProcessPartToolScrews",
                    "IsolatePartBeltSources", "UpdatePartBeltViews", "ProcessReplacementGarbage", "IsolateGuestParts", "ProcessNativeParts",
                    "DiscoverTrophyFactories", "RefreshPackageFactories", "ScanNativeParts", "RefreshReplacementFactories", "RefreshBagFactories", "PrepareBagSpillCapture", "BindPartBeltScroll" } },
                { "VehicleWorldSync", new[] { "UpdateRemoteEngineAudio", "ApplyRemoteElectricity", "EnsureVehicleSystemsProbe", "EnsureClimateProbe", "RefreshNativeRpmBinding", "FindTemperatureFsm", "PrepareGuestDamageIsolation" } },
                { "WalletSync", new[] { "Locate", "LocateBanking" } },
                { "TimeWeatherSync", new[] { "Locate" } },
                { "IceRaceEventSync", new[] { "Scan" } },
                { "IceRaceResultsSync", new[] { "Scan" } },
                { "InspectionSync", new[] { "Scan" } },
                { "JobSiteSync", new[] { "Scan" } },
                { "LotterySync", new[] { "Locate" } },
                { "MailOrderSync", new[] { "Scan" } },
                { "HomeStereoSync", new[] { "Scan" } },
                { "PoliceSync", new[] { "Scan" } },
                { "RallySync", new[] { "Scan" } },
                { "VenttiSync", new[] { "LocateTableBindings", "LocateProperties" } },
                { "TaxiJobSync", new[] { "Locate" } },
                { "IceRaceSync", new[] { "Scan" } },
                { "JailSync", new[] { "Locate" } },
                { "PhoneSync", new[] { "Locate" } },
                { "GamblingSync", new[] { "Locate" } },
                { "PokerSync", new[] { "Setup" } },
                { "WelfareSync", new[] { "LocateDebtLetter" } },
                { "LottoTicketSync", new[] { "Locate", "Discover" } },
                { "HockeyBettingSync", new[] { "Locate" } },
                { "NpcTrafficSync", new[] { "Scan", "UpdateGuest" } },
                { "NativePartIdentity", new[] { "FindData" } },
                { "ScenePath", new[] { "Of", "RelativeTo", "FindRelative" } }
            };
            var begin = new HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("BeginHeap", Members));
            var end = new HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("EndHeap", Members));
            foreach (var target in targets)
            {
                var type = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Sync." + target.Key, true);
                foreach (string name in target.Value)
                {
                    var method = type.GetMethod(name, Members);
                    if (method == null && target.Key == "WorldSyncManager"
                        && (name == "UpdateWorldDiscovery" || name == "ScanWorldCore" || name == "ScanWorldObjects")) continue;
                    if (method == null && target.Key == "ItemWorldSync" && name == "PrepareGuestEngineInputRead") continue;
                    if (method == null) throw new InvalidOperationException("Missing heap target " + target.Key + "." + name);
                    Costs.Add(method, new Cost());
                    PathCallers.Add(method, new CallerCost()); RuleCallers.Add(method, new CallerCost());
                    // A finalizer observes expected pending-input exceptions too.
                    // Returning void leaves every native exception unchanged.
                    bool electricity = target.Key == "VehicleWorldSync" && name == "ApplyRemoteElectricity";
                    var prefix = electricity ? new HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("BeginElectricityHeap", Members))
                        : target.Key == "GuestEngineProtection" && name == "PrepareCore"
                            ? new HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("BeginPreparationHeap", Members))
                            : target.Key == "ItemWorldSync" && name == "PrepareGuestEngineInputFsm"
                                ? new HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("BeginEngineInputHeap", Members)) : begin;
                    var finalizer = electricity ? new HarmonyMethod(typeof(LivePerformanceProbe).GetMethod("EndElectricityHeap", Members)) : end;
                    _harmony.Patch(method, prefix: prefix, finalizer: finalizer);
                }
            }
            _observations.Add("Heap growth is process-wide and inclusive. Calls containing a collection or decreasing heap are excluded from growth and eligible timing totals.");
            _observations.Add("OutsideScan rows are subsets of their matching method totals, recorded only without an active discovery path index.");
            _observations.Add("PrepareCoreForced is a subset of PrepareCore; PrepareCoreFromElectricity is its forced subset inside ApplyRemoteElectricity, including throwing calls.");
            CheckHeapCallerScopes();
            CheckEngineInputScopes();
            _observations.Add("Lookup callers identify the nearest enclosing instrumented method, not necessarily the immediate C# caller; root means no instrumented scope. Caller rows partition lookup totals and include throwing calls.");
        }

        private static HeapSample ReadHeap() => new HeapSample {
            Collections = GC.CollectionCount(0), Bytes = GC.GetTotalMemory(false), Stamp = Stopwatch.GetTimestamp() };

        private static void BeginHeap(MethodBase __originalMethod, out HeapSample __state)
        {
            int outside = 0;
            if (Sampling && (__originalMethod.Equals(_ruleLookupMethod) || __originalMethod.Equals(_pathMethod))
                && _scanPathsField!.GetValue(null) == null)
                outside = __originalMethod.Equals(_ruleLookupMethod) ? 1 : 2;
            __state = Sampling ? ReadHeap() : default(HeapSample);
            __state.OutsideScan = outside;
            __state.Parent = _heapParent;
            __state.EngineOrigin = _engineOrigin;
            if (__originalMethod.Equals(_engineReassertMethod)) _engineOrigin = 1;
            else if (__originalMethod.Equals(_engineStateEntryMethod)) _engineOrigin = 2;
            if (__state.Stamp != 0 && (__originalMethod.Equals(_pathMethod) || __originalMethod.Equals(_ruleLookupMethod)))
            {
                bool path = __originalMethod.Equals(_pathMethod);
                __state.Caller = _heapParent == null ? (path ? _rootPathCalls : _rootRuleCalls)
                    : (path ? PathCallers : RuleCallers)[_heapParent];
            }
            _heapParent = __originalMethod;
        }

        private static void EndHeap(MethodBase __originalMethod, HeapSample __state)
        {
            try
            {
                if (__state.Stamp == 0) return;
                var after = ReadHeap();
                AddHeap(Costs[__originalMethod], __state, after);
                if (__state.EngineConsumer != null) AddHeap(__state.EngineConsumer, __state, after);
                if (__state.OutsideScan != 0) AddHeap(__state.OutsideScan == 1 ? _outsideScanRules : _outsideScanPaths, __state, after);
                if (__state.Forced) AddHeap(_forcedPreparation, __state, after);
                if (__state.Electricity) AddHeap(_electricityPreparation, __state, after);
                if (__state.Caller != null)
                {
                    AddHeap(__state.Caller.All, __state, after);
                    if (__state.OutsideScan != 0) AddHeap(__state.Caller.OutsideScan, __state, after);
                }
            }
            finally { _heapParent = __state.Parent; _engineOrigin = __state.EngineOrigin; }
        }

        private void CheckHeapCallerScopes()
        {
            if (Sampling || _heapParent != null || _pathMethod == null || _ruleLookupMethod == null)
                throw new InvalidOperationException("Heap caller calibration must precede sampling.");
            BeginHeap(_ruleLookupMethod, out var outer);
            try
            {
                BeginHeap(_pathMethod, out var nested);
                try
                {
                    if (!ReferenceEquals(nested.Parent, _ruleLookupMethod) || !ReferenceEquals(_heapParent, _pathMethod))
                        throw new InvalidOperationException("Heap caller nesting failed.");
                    BeginHeap(_pathMethod, out var recursive);
                    try { if (!ReferenceEquals(recursive.Parent, _pathMethod)) throw new InvalidOperationException("Heap caller recursion failed."); }
                    finally { EndHeap(_pathMethod, recursive); }
                }
                finally { EndHeap(_pathMethod, nested); }
                if (!ReferenceEquals(_heapParent, _ruleLookupMethod)) throw new InvalidOperationException("Heap caller restoration failed.");
            }
            finally { EndHeap(_ruleLookupMethod, outer); }
            if (_heapParent != null) throw new InvalidOperationException("Heap caller root restoration failed.");
            _observations.Add("Heap caller scope calibration passed: nested, recursive and unsampled scopes restore their parent.");
        }

        private static void BeginPreparationHeap(MethodBase __originalMethod, bool force, out HeapSample __state)
        {
            BeginHeap(__originalMethod, out __state);
            if (!Sampling) return;
            __state.Forced = force; __state.Electricity = force && _electricityDepth > 0;
            if (!__state.Electricity) return;
            int frame = UnityEngine.Time.frameCount;
            if (_electricityFrame != frame) { _electricityFrame = frame; _electricityFrames++; _electricityFrameCalls = 0; }
            _electricityFrameCalls++;
            if (_electricityFrameCalls > _electricityFrameMaximum) _electricityFrameMaximum = _electricityFrameCalls;
        }

        private static void BeginElectricityHeap(MethodBase __originalMethod, out HeapSample __state)
        {
            _electricityDepth++;
            BeginHeap(__originalMethod, out __state);
        }

        private static void EndElectricityHeap(MethodBase __originalMethod, HeapSample __state)
        {
            try { EndHeap(__originalMethod, __state); }
            finally { _electricityDepth--; }
        }

        private static void AddHeap(Cost cost, HeapSample before, HeapSample after)
        {
            long ticks = after.Stamp - before.Stamp; cost.Calls++; cost.Ticks += ticks;
            if (ticks > cost.Maximum) cost.Maximum = ticks;
            if (before.Collections != after.Collections) { cost.CollectedCalls++; return; }
            long growth = after.Bytes - before.Bytes;
            if (growth < 0) { cost.DecreasedCalls++; return; }
            cost.HeapCalls++; cost.HeapGrowth += growth; cost.HeapTicks += ticks;
            if (growth > cost.HeapMaximum) cost.HeapMaximum = growth;
        }

        private void RecordFrameHeap()
        {
            var after = ReadHeap(); AddHeap(_frameHeapCost, _frameHeap, after); _frameHeap = after;
        }

        private void CheckHeapCounter()
        {
            var held = new byte[8][];
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var before = ReadHeap();
            for (int i = 0; i < held.Length; i++) held[i] = new byte[32768];
            var after = ReadHeap(); GC.KeepAlive(held);
            var cost = new Cost(); AddHeap(cost, before, after);
            if (cost.HeapCalls != 1 || cost.HeapGrowth < 262144 || cost.HeapTicks != after.Stamp - before.Stamp)
                throw new InvalidOperationException("Native heap counter cannot observe the retained 256 KiB calibration allocation.");
            long eligibleTicks = cost.HeapTicks;
            before = ReadHeap(); GC.Collect(); after = ReadHeap(); AddHeap(cost, before, after);
            if (cost.CollectedCalls != 1 || cost.HeapCalls != 1 || cost.HeapTicks != eligibleTicks)
                throw new InvalidOperationException("Heap counter failed to exclude the forced collection calibration.");
            _observations.Add("Heap calibration passed: retained array growth=" + cost.HeapGrowth + " bytes; forced collection excluded from growth and eligible timing.");
        }

        private void WriteHeapCosts()
        {
            var rows = new List<string> { "method,calls,eligible_calls,heap_growth_bytes,mean_growth_bytes,max_growth_bytes,calls_with_collection,calls_with_decrease,eligible_total_ms,eligible_mean_ms" };
            AppendHeap(rows, "frame", _frameHeapCost);
            foreach (var pair in Costs) AppendHeap(rows, pair.Key.DeclaringType.Name + "." + pair.Key.Name, pair.Value);
            AppendHeap(rows, "GuestEngineProtection.FindRuleOutsideScan", _outsideScanRules);
            AppendHeap(rows, "ScenePath.OfOutsideScan", _outsideScanPaths);
            AppendHeap(rows, "GuestEngineProtection.PrepareCoreForced", _forcedPreparation);
            AppendHeap(rows, "GuestEngineProtection.PrepareCoreFromElectricity", _electricityPreparation);
            _observations.Add("Remote electricity forced checks: calls=" + _electricityPreparation.Calls + "; frames=" + _electricityFrames
                + "; maximum per frame=" + _electricityFrameMaximum + "; final scope depth=" + _electricityDepth);
            File.WriteAllLines(Path.Combine(_output, _role + "-heap.csv"), rows.ToArray());
            var callers = new List<string> { "method,parent,calls,eligible_calls,heap_growth_bytes,mean_growth_bytes,max_growth_bytes,calls_with_collection,calls_with_decrease,eligible_total_ms,eligible_mean_ms" };
            AppendCallers(callers, "ScenePath.Of", "root", _rootPathCalls);
            AppendCallers(callers, "GuestEngineProtection.FindRule", "root", _rootRuleCalls);
            foreach (var pair in PathCallers) AppendCallers(callers, "ScenePath.Of", pair.Key.DeclaringType.Name + "." + pair.Key.Name, pair.Value);
            foreach (var pair in RuleCallers) AppendCallers(callers, "GuestEngineProtection.FindRule", pair.Key.DeclaringType.Name + "." + pair.Key.Name, pair.Value);
            _observations.Add("Heap caller final scope=" + (_heapParent == null ? "root" : _heapParent.Name));
            File.WriteAllLines(Path.Combine(_output, _role + "-heap-callers.csv"), callers.ToArray());
            WriteEngineInputCosts();
        }

        private static void AppendCallers(List<string> rows, string method, string parent, CallerCost cost)
        {
            if (cost.All.Calls == 0) return;
            AppendHeap(rows, method + "," + parent, cost.All);
            AppendHeap(rows, method + "OutsideScan," + parent, cost.OutsideScan);
        }

        private static void AppendHeap(List<string> rows, string name, Cost cost)
        {
            rows.Add(name + "," + cost.Calls + "," + cost.HeapCalls + "," + cost.HeapGrowth + ","
                + Number(cost.HeapCalls == 0 ? 0 : (double)cost.HeapGrowth / cost.HeapCalls) + "," + cost.HeapMaximum
                + "," + cost.CollectedCalls + "," + cost.DecreasedCalls
                + "," + Number(cost.HeapTicks * 1000.0 / Stopwatch.Frequency)
                + "," + Number(cost.HeapCalls == 0 ? 0 : cost.HeapTicks * 1000.0 / Stopwatch.Frequency / cost.HeapCalls));
        }
    }
}
