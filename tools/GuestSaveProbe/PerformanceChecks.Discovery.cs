using System;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class PerformanceChecks
    {
        private static object? _discoveryWorld, _discoveryItems, _discoveryNpcs;
        private static WinterMP.Net.Sync.PeriodicDiscoveryBudget? _discoveryBudget;
        private static readonly List<string> DiscoveryCalls = new List<string>();
        private static int _discoveryFrame;
        private static float _discoveryNow;
        private static bool _discoveryThrowItems, _discoveryAddItem;

        private static bool DiscoveryClock(ref float nextAt, float interval, bool force, ref bool __result)
        {
            __result = _discoveryBudget!.TryBegin(_discoveryFrame, _discoveryNow, ref nextAt, interval, force);
            return false;
        }

        private static void DiscoveryCore(object __instance, bool includeObjects)
        {
            if (ReferenceEquals(__instance, _discoveryWorld)) DiscoveryCalls.Add(includeObjects ? "full" : "controls");
        }

        private static bool DiscoveryItems(object __instance, ref int __result)
        {
            if (!ReferenceEquals(__instance, _discoveryItems)) return true;
            DiscoveryCalls.Add("items");
            if (_discoveryThrowItems) throw new InvalidOperationException("Injected item discovery failure.");
            __result = 0;
            if (_discoveryAddItem)
            {
                var registry = (IDictionary)ReadField(__instance, "_items");
                registry[0xCAFE1234u] = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                __result = 1;
            }
            return false;
        }

        private static bool DiscoveryNpcs(object __instance, ref int __result)
        {
            if (!ReferenceEquals(__instance, _discoveryNpcs)) return true;
            DiscoveryCalls.Add("npcs"); __result = 0;
            return false;
        }

        private static void CheckWorldDiscoveryScheduling(object world, Action full, Action<string, Action> check)
        {
            var type = world.GetType(); var routine = type.GetMethod("UpdateWorldDiscovery", Members);
            if (routine == null) return; // The comparison binary only has combined discovery.
            WithDiscoveryRole(false, () =>
            {
                var next = type.GetField("_nextScanAt", Members);
                var itemsAt = type.GetField("_nextItemScanAt", Members); var npcsAt = type.GetField("_nextNpcScanAt", Members);
                float savedNext = (float)next.GetValue(world), savedItems = (float)itemsAt.GetValue(world), savedNpcs = (float)npcsAt.GetValue(world);
                var paths = Core.GetType("WinterMP.Core.Sync.ScenePath", true);
                var harmony = new Harmony("WinterMP.WorldDiscoverySchedulingProbe");
                _discoveryWorld = world; _discoveryItems = ReadField(world, "_items"); _discoveryNpcs = ReadField(world, "_npcTraffic");
                _discoveryBudget = new WinterMP.Net.Sync.PeriodicDiscoveryBudget();
                _discoveryFrame = 1; _discoveryNow = Time.unscaledTime + 1f;
                var registry = (IDictionary)ReadField(_discoveryItems, "_items");
                Require(!registry.Contains(0xCAFE1234u));
                Action tick = () => { DiscoveryCalls.Clear(); routine.Invoke(world, null); };
                Action<float> due = value => { next.SetValue(world, value); itemsAt.SetValue(world, value); npcsAt.SetValue(world, value); };
                Action<string> trace = expected => Require(string.Join(",", DiscoveryCalls.ToArray()) == expected);
                try
                {
                    harmony.Patch(paths.GetMethod("TryBeginDiscovery", Static), new HarmonyMethod(typeof(PerformanceChecks), nameof(DiscoveryClock)));
                    harmony.Patch(type.GetMethod("ScanWorldCore", Members), new HarmonyMethod(typeof(PerformanceChecks), nameof(DiscoveryCore)));
                    harmony.Patch(_discoveryItems.GetType().GetMethod("ScanItems", Members), new HarmonyMethod(typeof(PerformanceChecks), nameof(DiscoveryItems)));
                    harmony.Patch(_discoveryNpcs.GetType().GetMethod("Scan", Members), new HarmonyMethod(typeof(PerformanceChecks), nameof(DiscoveryNpcs)));
                    const string prefix = "staged discovery: ";
                    check(prefix + "initial delay does not start item or NPC scans early", () =>
                    { due(0); next.SetValue(world, _discoveryNow + 5); tick(); trace(""); });
                    check(prefix + "first discovery completes every phase together", () =>
                    {
                        next.SetValue(world, 0f); tick(); trace("full,items,npcs");
                        Require((float)itemsAt.GetValue(world) > _discoveryNow && (float)npcsAt.GetValue(world) > _discoveryNow);
                    });
                    check(prefix + "due controls defer both object scans without moving their deadlines", () =>
                    {
                        _discoveryFrame++; due(_discoveryNow - .5f); tick(); trace("controls");
                        Require((float)itemsAt.GetValue(world) == _discoveryNow - .5f && (float)npcsAt.GetValue(world) == _discoveryNow - .5f);
                    });
                    check(prefix + "next frame discovers items and refreshes their identity hash", () =>
                    {
                        var hash = type.GetProperty("IdHash", Members); object before = hash.GetValue(world, null);
                        _discoveryFrame++; _discoveryAddItem = true;
                        try { tick(); trace("items"); Require(registry.Contains(0xCAFE1234u) && !before.Equals(hash.GetValue(world, null))); }
                        finally { _discoveryAddItem = false; registry.Remove(0xCAFE1234u); type.GetMethod("RecomputeIdHash", Members).Invoke(world, null); }
                        Require((float)npcsAt.GetValue(world) == _discoveryNow - .5f);
                    });
                    check(prefix + "third frame discovers NPCs and settled deadlines stay quiet", () =>
                    { _discoveryFrame++; tick(); trace("npcs"); _discoveryFrame++; tick(); trace(""); });
                    check(prefix + "explicit refresh completes all phases despite an occupied frame and future deadlines", () =>
                    {
                        float occupied = 1; _discoveryBudget.TryBegin(_discoveryFrame, _discoveryNow, ref occupied, 5, true);
                        due(_discoveryNow + 50); DiscoveryCalls.Clear(); full(); trace("full,items,npcs");
                        Require((float)itemsAt.GetValue(world) < _discoveryNow + 50 && (float)npcsAt.GetValue(world) < _discoveryNow + 50);
                    });
                    check(prefix + "failed item scan stays contained and does not strand NPC discovery", () =>
                    {
                        _discoveryFrame++; itemsAt.SetValue(world, _discoveryNow - .5f); npcsAt.SetValue(world, _discoveryNow - .5f);
                        _discoveryThrowItems = true;
                        try { tick(); trace("items"); } finally { _discoveryThrowItems = false; }
                        _discoveryFrame++; tick(); trace("npcs");
                        _discoveryFrame++; itemsAt.SetValue(world, _discoveryNow - .5f); tick(); trace("items");
                    });
                    check(prefix + "session release restores complete initial discovery", () =>
                    {
                        type.GetMethod("ReleaseEverything", Members).Invoke(world, null);
                        Require((float)itemsAt.GetValue(world) == 0f && (float)npcsAt.GetValue(world) == 0f);
                        _discoveryFrame++; next.SetValue(world, 0f); tick(); trace("full,items,npcs");
                    });
                }
                finally
                {
                    harmony.UnpatchSelf(); registry.Remove(0xCAFE1234u);
                    next.SetValue(world, savedNext); itemsAt.SetValue(world, savedItems); npcsAt.SetValue(world, savedNpcs);
                    _discoveryWorld = _discoveryItems = _discoveryNpcs = null; _discoveryBudget = null;
                    _discoveryThrowItems = _discoveryAddItem = false; DiscoveryCalls.Clear();
                }
            });
        }

        private static readonly string[] PeriodicSystems =
            { "_police", "_homeStereo", "_rally", "_iceRace", "_jobSites", "_wallet", "_gambling" };

        private static object VehicleDiscoveryItem(GameObject car, bool climateReady)
        {
            var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
            SetDiscoveryField(item, "Body", car.GetComponent<Rigidbody>() ?? car.AddComponent<Rigidbody>());
            SetDiscoveryField(item, "Path", "vehicle discovery fixture");
            SetDiscoveryField(item, "ClimateReady", climateReady);
            return item;
        }

        private static void SetDiscoveryField(object target, string name, object value)
            => target.GetType().GetField(name, Members).SetValue(target, value);

        private static Action VehicleDiscoveryScan(object item, bool climate)
        {
            var method = Core.GetType("WinterMP.Core.Sync.VehicleWorldSync", true)
                .GetMethod(climate ? "EnsureClimateProbe" : "EnsureVehicleSystemsProbe", Static);
            var arguments = new[] { item };
            return () =>
            {
                SetDiscoveryField(item, climate ? "NextClimateProbeAt" : "NextSystemsProbeAt", 0f);
                method.Invoke(null, arguments);
            };
        }

        private static void MeasureVehicleDiscovery(List<string> rows, int count, GameObject root)
        {
            var item = VehicleDiscoveryItem(root, true);
            SetDiscoveryField(item, "AudioEngine", root);
            Measure(rows, count, "vehicle-systems-unrelated-discovery", VehicleDiscoveryScan(item, false));
            SetDiscoveryField(item, "ClimateReady", false);
            Measure(rows, count, "vehicle-climate-unrelated-discovery", VehicleDiscoveryScan(item, true));
        }

        private static PlayMakerFSM VehicleDiscoveryFsm(GameObject obj, string name, params string[] floats)
        {
            var fsm = obj.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
            typeof(PlayMakerFSM).GetField("fsm", Members).SetValue(fsm, new Fsm());
            fsm.Fsm.Name = name; fsm.Fsm.States = new FsmState[0];
            var values = new FsmFloat[floats.Length];
            for (int i = 0; i < values.Length; i++) values[i] = new FsmFloat { Name = floats[i] };
            fsm.FsmVariables.FloatVariables = values;
            return fsm;
        }

        private static Func<PlayMakerFSM[], string, string, PlayMakerFSM> VehicleSourceLookup()
            => (Func<PlayMakerFSM[], string, string, PlayMakerFSM>)Delegate.CreateDelegate(
                typeof(Func<PlayMakerFSM[], string, string, PlayMakerFSM>),
                Core.GetType("WinterMP.Core.Sync.VehicleWorldSync", true).GetMethod("FindTemperatureFsm", Static));

        private static void RequireSourceLookupFailure(Action lookup, bool ambiguous = false)
        {
            try { lookup(); }
            catch (InvalidOperationException error)
            {
                Require(error.Message == (ambiguous ? "Ambiguous temperature FSM." : "Temperature FSM not loaded."));
                return;
            }
            throw new InvalidOperationException("Invalid vehicle source lookup succeeded.");
        }

        private static void MeasureVehicleSourceLookup(List<string> rows, int count, GameObject root)
        {
            var target = Child(root, "Temperature source"); target.SetActive(false);
            try
            {
                var expected = VehicleDiscoveryFsm(target, "Use");
                var fsms = root.GetComponentsInChildren<PlayMakerFSM>(true);
                string path = root.name + "/Temperature source";
                var lookup = VehicleSourceLookup();
                Measure(rows, count, "vehicle-source-same-fsm-name", () => Require(lookup(fsms, path, "Use") == expected));
            }
            finally { UnityEngine.Object.DestroyImmediate(target); }
        }

        private static void CheckVehicleSourceLookup(Action<string, Action> check)
        {
            var root = new GameObject("vehicle source lookup fixture"); root.SetActive(false);
            try
            {
                var lookup = VehicleSourceLookup(); var parent = Child(root, "Controls");
                var target = VehicleDiscoveryFsm(Child(parent, "Target"), "Use");
                var other = VehicleDiscoveryFsm(Child(parent, "Other"), "Use");
                VehicleDiscoveryFsm(target.gameObject, "OtherFSM");
                var fsms = new[] { null!, other, target }; string path = root.name + "/Controls/Target";
                check("vehicle source lookup: exact selection ignores nulls and unrelated object and FSM names", () =>
                    Require(lookup(root.GetComponentsInChildren<PlayMakerFSM>(true), path, "Use") == target
                        && lookup(fsms, path, "Use") == target));
                check("vehicle source lookup: current FSM rename rejects and restores selection", () =>
                {
                    target.Fsm.Name = "Changed";
                    try { RequireSourceLookupFailure(() => lookup(fsms, path, "Use")); }
                    finally { target.Fsm.Name = "Use"; }
                    Require(lookup(fsms, path, "Use") == target);
                });
                check("vehicle source lookup: current object rename rejects old paths and accepts the new path", () =>
                {
                    target.gameObject.name = "Changed";
                    try { RequireSourceLookupFailure(() => lookup(fsms, path, "Use")); Require(lookup(fsms, root.name + "/Controls/Changed", "Use") == target); }
                    finally { target.gameObject.name = "Target"; }
                });
                var elsewhere = Child(root, "Elsewhere");
                check("vehicle source lookup: matching leaf still requires its current parent path", () =>
                {
                    target.transform.SetParent(elsewhere.transform, false);
                    try { RequireSourceLookupFailure(() => lookup(fsms, path, "Use")); Require(lookup(fsms, root.name + "/Elsewhere/Target", "Use") == target); }
                    finally { target.transform.SetParent(parent.transform, false); }
                });
                var duplicate = VehicleDiscoveryFsm(Child(parent, "Target"), "Use");
                check("vehicle source lookup: duplicate siblings require current ordinal suffixes", () =>
                {
                    var candidates = new[] { target, duplicate };
                    RequireSourceLookupFailure(() => lookup(candidates, path, "Use"));
                    Require(lookup(candidates, path + "[0]", "Use") == target && lookup(candidates, path + "[1]", "Use") == duplicate);
                    duplicate.transform.SetAsFirstSibling();
                    Require(lookup(candidates, path + "[0]", "Use") == duplicate && lookup(candidates, path + "[1]", "Use") == target);
                });
                var literal = VehicleDiscoveryFsm(Child(parent, "Target[0]"), "Use");
                check("vehicle source lookup: literal suffix collisions remain ambiguous", () =>
                    RequireSourceLookupFailure(() => lookup(new[] { target, duplicate, literal }, path + "[0]", "Use"), true));
                UnityEngine.Object.DestroyImmediate(literal.gameObject); UnityEngine.Object.DestroyImmediate(duplicate.gameObject);
                var extra = VehicleDiscoveryFsm(target.gameObject, "Use");
                check("vehicle source lookup: duplicate components stay ambiguous until removed", () =>
                {
                    RequireSourceLookupFailure(() => lookup(new[] { target, extra }, path, "Use"), true);
                    UnityEngine.Object.DestroyImmediate(extra);
                    Require(lookup(new[] { target, extra }, path, "Use") == target);
                });
                var odd = VehicleDiscoveryFsm(Child(root, "ä/Thing"), "Use");
                var empty = VehicleDiscoveryFsm(Child(odd.gameObject, ""), "Use");
                check("vehicle source lookup: literal separators unicode and empty names retain exact paths", () =>
                    Require(lookup(new[] { odd, empty }, root.name + "/ä/Thing", "Use") == odd
                        && lookup(new[] { odd, empty }, root.name + "/ä/Thing/", "Use") == empty));
                check("vehicle source lookup: active scan identity ends when its enumerator is disposed", () =>
                {
                    var paths = Core.GetType("WinterMP.Core.Sync.ScenePath", true);
                    var scan = (Func<IEnumerable<UnityEngine.Object>>)Delegate.CreateDelegate(typeof(Func<IEnumerable<UnityEngine.Object>>), paths.GetMethod("ScanFsms", Static));
                    using (var objects = scan().GetEnumerator())
                    {
                        Require(objects.MoveNext()); Require(lookup(fsms, path, "Use") == target);
                        target.gameObject.name = "Changed during scan";
                        Require(lookup(fsms, path, "Use") == target);
                    }
                    try { RequireSourceLookupFailure(() => lookup(fsms, path, "Use")); }
                    finally { target.gameObject.name = "Target"; }
                    Require(lookup(fsms, path, "Use") == target);
                });
                UnityEngine.Object.DestroyImmediate(target.gameObject);
                check("vehicle source lookup: destroyed native components cannot satisfy old paths", () =>
                    RequireSourceLookupFailure(() => lookup(fsms, path, "Use")));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void CheckVehicleDiscovery(Action<string, Action> check)
        {
            var car = new GameObject("vehicle discovery fixture"); car.SetActive(false);
            try
            {
                var item = VehicleDiscoveryItem(car, true); var scan = VehicleDiscoveryScan(item, false);
                var wrong = Child(car, "Unrelated"); var systems = Child(Child(car, "PowerON"), "Systems");
                var signal = VehicleDiscoveryFsm(wrong, "TurnSignals");
                signal.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "BlinkerLeft" }, new FsmBool { Name = "BlinkerRight" }, new FsmBool { Name = "BlinkerHazards" } };
                check("vehicle discovery: turn signals reject the wrong subtree", () =>
                { scan(); Require(ReadField(item, "TurnSignalsFsm") == null); });
                signal.transform.SetParent(systems.transform, false); signal.Fsm.Name = "Other";
                check("vehicle discovery: turn signals reject the wrong FSM name", () =>
                { scan(); Require(ReadField(item, "TurnSignalsFsm") == null); });
                signal.Fsm.Name = "TurnSignals";
                check("vehicle discovery: renamed and moved signals bind with all outputs", () =>
                {
                    scan(); Require(ReferenceEquals(ReadField(item, "TurnSignalsFsm"), signal));
                    Require(ReferenceEquals(ReadField(item, "BlinkerLeftVar"), signal.FsmVariables.FindFsmBool("BlinkerLeft"))
                        && ReferenceEquals(ReadField(item, "BlinkerRightVar"), signal.FsmVariables.FindFsmBool("BlinkerRight"))
                        && ReferenceEquals(ReadField(item, "BlinkerHazardsVar"), signal.FsmVariables.FindFsmBool("BlinkerHazards")));
                });
                var speed = VehicleDiscoveryFsm(Child(car, "SpeedGauge"), "Speedo", "Speed", "Angle", "RPM");
                var fuel = VehicleDiscoveryFsm(Child(car, "FuelGauge"), "Fuel", "Level", "Angle");
                var tank = VehicleDiscoveryFsm(Child(car, "Tank"), "Data", "FuelLevel", "MaxCapacity");
                check("vehicle discovery: path-independent gauges and tank still bind", () =>
                {
                    scan(); Require(ReferenceEquals(ReadField(item, "GaugeSpeedVar"), speed.FsmVariables.FindFsmFloat("Speed"))
                        && ReferenceEquals(ReadField(item, "GaugeRpmVar"), speed.FsmVariables.FindFsmFloat("RPM"))
                        && ReferenceEquals(ReadField(item, "GaugeFuelLevelVar"), fuel.FsmVariables.FindFsmFloat("Level"))
                        && ReferenceEquals(ReadField(item, "FuelTankLevelVar"), tank.FsmVariables.FindFsmFloat("FuelLevel")));
                });
                var audio = Child(car, "AudioEngine");
                check("vehicle discovery: late engine audio binds and keeps its identity", () =>
                {
                    scan(); Require(ReferenceEquals(ReadField(item, "AudioEngine"), audio));
                    var duplicate = Child(car, "AudioEngine"); duplicate.transform.SetAsFirstSibling();
                    try { scan(); Require(ReferenceEquals(ReadField(item, "AudioEngine"), audio)); }
                    finally { UnityEngine.Object.DestroyImmediate(duplicate); }
                });
                UnityEngine.Object.DestroyImmediate(audio); audio = Child(car, "AudioEngine");
                check("vehicle discovery: destroyed engine audio is rediscovered", () =>
                { scan(); Require(ReferenceEquals(ReadField(item, "AudioEngine"), audio)); });

                var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
                var config = catalog.GetField("_vehicleClimate", Static).GetValue(null);
                car.name = ((string[])ReadField(config, "PathPrefixes"))[0].TrimEnd('/');
                var cabin = car;
                foreach (string part in ((string[])ReadField(config, "CarTempPathContains"))[0].Trim('/').Split('/')) cabin = Child(cabin, part);
                var entries = new Dictionary<PlayMakerFSM, string>();
                var frost = VehicleDiscoveryFsm(cabin, "GlassFrosting", "Frost"); entries.Add(frost, "GlassFrostingFsm");
                entries.Add(VehicleDiscoveryFsm(cabin, "Freezing", "CutoffWindshield"), "FreezingFsm");
                entries.Add(VehicleDiscoveryFsm(cabin, "Data", "InteriorTemp"), "CarTempDataFsm");
                var heater = car;
                foreach (string part in ((string[])ReadField(config, "HeaterPathContains"))[0].Trim('/').Split('/')) heater = Child(heater, part);
                entries.Add(VehicleDiscoveryFsm(heater, "Function", "SettingTemp", "SettingBlower", "SettingDirection"), "HeaterUnitFsm");
                string[] buttons = { "ButtonHeaterTemp", "ButtonHeaterBlower", "ButtonHeaterDirection", "ButtonWindowHeater" };
                string[] fields = { "KnobTempFsm", "KnobBlowerFsm", "KnobDirectionFsm", "WindowHeaterButtonFsm" };
                var controls = new List<PlayMakerFSM>();
                for (int i = 0; i < buttons.Length; i++)
                {
                    var control = VehicleDiscoveryFsm(Child(car, buttons[i]), "Use", "Setting", "Angle");
                    controls.Add(control); entries.Add(control, fields[i]);
                }
                var names = new Dictionary<PlayMakerFSM, string>();
                foreach (var entry in entries) { names.Add(entry.Key, entry.Key.FsmName); entry.Key.Fsm.Name = "Unrelated"; }
                var climate = VehicleDiscoveryItem(car, false); var climateScan = VehicleDiscoveryScan(climate, true);
                Action<object, bool> verify = (target, bound) =>
                { foreach (var entry in entries) Require(bound ? ReferenceEquals(ReadField(target, entry.Value), entry.Key) : ReadField(target, entry.Value) == null); };
                check("vehicle climate discovery: wrong FSM names remain unbound", () =>
                { climateScan(); verify(climate, false); });
                foreach (var name in names) name.Key.Fsm.Name = name.Value;
                string rootName = car.name; car.name = "wrong climate vehicle";
                check("vehicle climate discovery: correct names still require a catalog vehicle path", () =>
                { climateScan(); verify(climate, false); });
                car.name = rootName;
                check("vehicle climate discovery: later scan recovers every renamed binding", () =>
                { climateScan(); verify(climate, true); Require((bool)ReadField(climate, "ClimateReady")); });
                check("vehicle climate discovery: settled bindings and Frost identity are retained", () =>
                { climateScan(); verify(climate, true); Require(ReferenceEquals(ReadField(climate, "FrostVar"), frost.FsmVariables.FindFsmFloat("Frost"))); });
                foreach (var control in controls) control.gameObject.name = "OtherButton";
                check("vehicle climate discovery: other Use buttons cannot bind climate controls", () =>
                {
                    var other = VehicleDiscoveryItem(car, false); VehicleDiscoveryScan(other, true)();
                    foreach (string field in fields) Require(ReadField(other, field) == null);
                });
                cabin.name = "wrong cabin"; heater.name = "wrong heater";
                check("vehicle climate discovery: cabin and heater paths remain independently required", () =>
                {
                    var other = VehicleDiscoveryItem(car, false); VehicleDiscoveryScan(other, true)();
                    foreach (string field in new[] { "GlassFrostingFsm", "FreezingFsm", "CarTempDataFsm", "HeaterUnitFsm" })
                        Require(ReadField(other, field) == null);
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(car); }
        }

        private static Action DiscoveryScan(object target)
        {
            var type = target.GetType();
            if (type.Name == "GamblingSync") type.GetMethod("EnsureBuilt", Members).Invoke(target, null);
            var scan = type.GetMethod(type.Name == "WalletSync" ? "LocateBanking" : type.Name == "GamblingSync" ? "Locate" : "Scan", Members);
            var arguments = scan.GetParameters().Length == 0 ? null : new object[] { true };
            var next = type.GetField("_nextScanAt", Members);
            return () => { if (next != null) next.SetValue(target, 0f); scan.Invoke(target, arguments); };
        }

        private static void MeasurePeriodicDiscovery(List<string> rows, int count, object world)
        {
            foreach (string field in PeriodicSystems)
            {
                var target = ReadField(world, field);
                Measure(rows, count, target.GetType().Name + "-discovery", DiscoveryScan(target));
            }
        }

        private static object ReadField(object target, string name)
        { return target.GetType().GetField(name, Members).GetValue(target); }

        private static object? DictionaryField(object target, string name, object key)
        { return ((IDictionary)ReadField(target, name))[key]; }

        private static void CheckPeriodicDiscovery(object items, Action<string, Action> check)
        {
            var method = Core.GetType("WinterMP.Core.Sync.ScenePath", true).GetMethod("TryBeginDiscovery", Static);
            var begin = method == null ? null : (BeginDiscovery)Delegate.CreateDelegate(typeof(BeginDiscovery), method);
            if (begin != null) CheckDiscoveryBudget(begin, check);
            foreach (string name in new[] { "PoliceSync", "HomeStereoSync", "RallySync", "IceRaceSync", "JobSiteSync", "WalletSync", "GamblingSync" })
            {
                var type = Core.GetType("WinterMP.Core.Sync." + name, true);
                Func<object> create = () => name == "PoliceSync" || name == "RallySync" || name == "IceRaceSync"
                    ? Activator.CreateInstance(type, Members, null, new[] { items }, null)
                    : Activator.CreateInstance(type, true);
                using (var fixture = new DiscoveryFixture())
                {
                    fixture.Populate(name);
                    var target = create();
                    var scan = DiscoveryScan(target);
                    if (begin != null && name != "WalletSync" && name != "GamblingSync")
                        check("periodic discovery " + name + ": deferred scan retains deadline and leaves bindings untouched", () =>
                        {
                            float reserve = 0; Require(begin(ref reserve, 5f, true));
                            float due = Time.unscaledTime;
                            var next = type.GetField("_nextScanAt", Members); next.SetValue(target, due);
                            var routine = type.GetMethod("Scan", Members);
                            routine.Invoke(target, routine.GetParameters().Length == 0 ? null : new object[] { false });
                            Require((float)next.GetValue(target) == due);
                            // Rally allocates its stage records during Scan, so a
                            // deferred first pass must leave that registry empty too.
                            if (name == "RallySync") Require(((IDictionary)ReadField(target, "_stages")).Count == 0);
                            else fixture.Verify(target, false);
                        });
                    check("periodic discovery " + name + ": wrong FSM names cannot bind at matching paths", () =>
                    {
                        fixture.SetNames(false); scan(); fixture.Verify(target, false);
                    });
                    check("periodic discovery " + name + ": matching names cannot bind under another parent", () =>
                    {
                        fixture.SetNames(true); fixture.Reparent(true); scan(); fixture.Verify(target, false);
                    });
                    check("periodic discovery " + name + ": later scan finds every binding after rename and reparent", () =>
                    {
                        fixture.Reparent(false); scan(); fixture.Verify(target, true);
                    });
                }
            }
        }

        private delegate bool BeginDiscovery(ref float next, float interval, bool force);

        private static void CheckDiscoveryBudget(BeginDiscovery begin, Action<string, Action> check)
        {
            float next = 0;
            check("discovery budget: forced native refresh remains immediate", () => Require(begin(ref next, 5f, true)));
            check("discovery budget: routine work waits without moving its deadline", () =>
            { float due = Time.unscaledTime, saved = due; Require(!begin(ref due, 5f, false) && due == saved); });
            check("discovery budget: first discovery remains immediate in an occupied frame", () =>
            { float first = 0; Require(begin(ref first, 5f, false) && first > Time.unscaledTime); });
            check("discovery budget: another first discovery also remains immediate", () =>
            { float first = 0; Require(begin(ref first, 5f, false)); });
            check("discovery budget: explicit refresh bypasses future deadline", () =>
            { float future = Time.unscaledTime + 100; Require(begin(ref future, 5f, true) && future < Time.unscaledTime + 100); });
        }

        private sealed class DiscoveryFixture : IDisposable
        {
            private readonly Dictionary<string, GameObject> _objects = new Dictionary<string, GameObject>();
            private readonly Dictionary<PlayMakerFSM, string> _names = new Dictionary<PlayMakerFSM, string>();
            private readonly List<GameObject> _roots = new List<GameObject>();
            private readonly List<Func<object, bool, bool>> _expectations = new List<Func<object, bool, bool>>();
            private readonly GameObject _other = new GameObject("Unrelated discovery parent");

            public void SetNames(bool valid)
            { foreach (var pair in _names) pair.Key.Fsm.Name = valid ? pair.Value : "Unrelated discovery FSM"; }
            public void Reparent(bool invalid)
            { foreach (var root in _roots) root.transform.SetParent(invalid ? _other.transform : null, false); }
            public void Verify(object target, bool bound)
            {
                Require(_expectations.Count > 0);
                foreach (var expected in _expectations) Require(expected(target, bound));
            }
            public void Dispose()
            {
                foreach (var root in _roots) UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(_other);
            }

            private PlayMakerFSM FsmAt(string path, string name)
            {
                GameObject? parent = null;
                string prefix = "";
                foreach (string part in path.Split('/'))
                {
                    prefix = prefix.Length == 0 ? part : prefix + "/" + part;
                    if (!_objects.TryGetValue(prefix, out var obj))
                    {
                        obj = new GameObject(part); obj.SetActive(false);
                        if (parent == null) _roots.Add(obj); else obj.transform.SetParent(parent.transform, false);
                        _objects.Add(prefix, obj);
                    }
                    parent = obj;
                }
                var fsm = parent!.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
                typeof(PlayMakerFSM).GetField("fsm", Members).SetValue(fsm, new Fsm());
                fsm.Fsm.Name = name;
                fsm.Fsm.States = new FsmState[0];
                fsm.FsmVariables.FloatVariables = new[] { new FsmFloat { Name = "Volume" }, new FsmFloat { Name = "Bass" },
                    new FsmFloat { Name = "Time" }, new FsmFloat { Name = "ShitLevel" }, new FsmFloat { Name = "Surplus" } };
                fsm.FsmVariables.IntVariables = new[] { new FsmInt { Name = "JobStage" }, new FsmInt { Name = "Laps" } };
                fsm.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Done" }, new FsmBool { Name = "Called" },
                    new FsmBool { Name = "Order" }, new FsmBool { Name = "PumpRunning" }, new FsmBool { Name = "RadioOn" },
                    new FsmBool { Name = "Channel" }, new FsmBool { Name = "Checkpoint1" }, new FsmBool { Name = "Checkpoint2" } };
                fsm.FsmVariables.StringVariables = new[] { new FsmString { Name = "Price" } };
                _names.Add(fsm, name);
                return fsm;
            }

            private void Expect(Func<object, object?> lookup, object expected)
            { _expectations.Add((target, bound) => bound ? ReferenceEquals(lookup(target), expected) : lookup(target) == null); }
            private void ExpectField(string field, object expected)
            { Expect(target => ReadField(target, field), expected); }
            private static object Config(string name)
            { return Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true).GetProperty(name, Static).GetValue(null, null); }
            private static string Text(object config, string field) { return (string)ReadField(config, field); }

            public void Populate(string name)
            {
                if (name == "PoliceSync")
                {
                    var fine = FsmAt("HOMENEW/Functions/FunctionsDisable/Fines", "Activate");
                    ExpectField("_finePrice", fine.FsmVariables.FindFsmString("Price"));
                    var cop = FsmAt("TRAFFIC/Police/Checkpoints/Cops/PerformanceCop", "Animations");
                    Expect(target =>
                    {
                        foreach (var value in ((IDictionary)ReadField(target, "_checkpoints")).Values) return ReadField(value, "Transform");
                        return null;
                    }, cop.transform);
                }
                else if (name == "HomeStereoSync")
                {
                    const string path = "HOMENEW/Functions/FunctionsDisable/Stereos/Player/ButtonsCD/";
                    ExpectField("_volumeFsm", FsmAt(path + "Volume", "Knob"));
                    ExpectField("_bassFsm", FsmAt(path + "Bass", "Knob"));
                    ExpectField("_radioFsm", FsmAt(path + "RadioCDSwitch", "Use"));
                    ExpectField("_channelFsm", FsmAt(path + "TrackChannelSwitch", "ChangeChannel"));
                }
                else if (name == "IceRaceSync")
                {
                    ExpectField("_corrisRace", FsmAt("CORRIS/DB/RaceTrigger", "Data"));
                    string[] markers = { "StartFinishTime", "StartFinishLaps", "Checkpoint1", "Checkpoint2" };
                    string[] fields = { "_timeStart", "_lapStart", "_checkpoint1Marker", "_checkpoint2Marker" };
                    for (int i = 0; i < markers.Length; i++)
                        ExpectField(fields[i], FsmAt("RACES/ICERACE/TrackFunctions/" + markers[i], "Checkpoint").transform);
                }
                else if (name == "JobSiteSync")
                {
                    string[] paths = { "JOBS/Farm/Farmer/Walker", "JOBS/HouseShit1/Level", "JOBS/HouseWood1/Logic", "GIFU(750/450psi)/ShitTank" };
                    string[] names = { "Speak", "Level", "Logic", "Pump" };
                    string[] variables = { "Done", "Called", "Order", "PumpRunning" };
                    for (int i = 0; i < paths.Length; i++)
                    {
                        string path = paths[i]; var fsm = FsmAt(path, names[i]);
                        Expect(target =>
                        {
                            foreach (var site in ((IDictionary)ReadField(target, "_sites")).Values)
                                if ((string)ReadField(site, "Path") == path) return ReadField(site, "Active");
                            return null;
                        }, fsm.FsmVariables.FindFsmBool(variables[i]));
                    }
                }
                else if (name == "WalletSync")
                {
                    var config = Config("Banking");
                    ExpectField("_bankData", FsmAt(Text(config, "BankPath"), Text(config, "BankFsm")));
                    ExpectField("_atm", FsmAt(Text(config, "AtmPath"), Text(config, "AtmFsm")));
                    ExpectField("_cashTrigger", FsmAt(Text(config, "CashPath"), Text(config, "CashFsm")));
                }
                else if (name == "GamblingSync")
                {
                    var config = Config("SlotMachines");
                    var stats = FsmAt(Text(config, "StatsPath"), Text(config, "StatsFsm"));
                    stats.FsmVariables.FloatVariables = new[] { new FsmFloat { Name = Text(config, "StatsMoneyIn") }, new FsmFloat { Name = Text(config, "StatsMoneyOut") } };
                    ExpectField("_statsIn", stats.FsmVariables.FloatVariables[0]);
                    ExpectField("_statsOut", stats.FsmVariables.FloatVariables[1]);
                    var buttons = (string[])ReadField(config, "ButtonPaths");
                    foreach (string path in (IEnumerable<string>)ReadField(config, "Paths"))
                    {
                        uint id = WinterMP.Net.StableHash.Fnv1a32(path);
                        for (int i = 0; i < buttons.Length; i++)
                        {
                            int index = i; var button = FsmAt(path + "/" + buttons[index], Text(config, "FsmName"));
                            Expect(target => ((Array)ReadField(DictionaryField(target, "_machines", id)!, "Buttons")).GetValue(index), button);
                        }
                        Expect(target => ReadField(DictionaryField(target, "_machines", id)!, "Root"), _objects[path].transform);
                    }
                }
                else if (name == "RallySync") PopulateRally();
            }

            private void PopulateRally()
            {
                var config = Config("RallyProgress"); byte number = 0;
                foreach (var stage in (IEnumerable)ReadField(config, "Stages"))
                {
                    byte key = ++number;
                    Func<object, object> boundStage = target => DictionaryField(target, "_stages", key)!;
                    string timingPath = Text(stage, "TimingPath");
                    var timing = FsmAt(timingPath, Text(config, "TimingFsm"));
                    timing.FsmVariables.BoolVariables = new[] { new FsmBool { Name = Text(config, "StartedVariable") } };
                    Expect(target => ReadField(boundStage(target), "Timing"), timing);
                    Expect(target => ReadField(boundStage(target), "Started"), timing.FsmVariables.BoolVariables[0]);
                    var start = FsmAt(Text(stage, "StartPath"), Text(config, "MarkerFsm"));
                    Expect(target => ReadField(boundStage(target), "StartLine"), start.transform);
                    byte checkpoint = 0;
                    foreach (string markerName in (string[])ReadField(stage, "Checkpoints"))
                    {
                        byte markerKey = ++checkpoint;
                        var marker = FsmAt(timingPath + "/" + markerName, Text(config, "MarkerFsm"));
                        var complete = new FsmState(marker.Fsm) { Name = Text(config, "CompletedState"), Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] };
                        var commit = new FsmState(marker.Fsm) { Name = Text(config, "CommitState"), Actions = new FsmStateAction[0],
                            Transitions = new[] { new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("FINISHED"), ToState = complete.Name } } };
                        marker.Fsm.States = new[] { commit, complete };
                        Expect(target =>
                        {
                            var point = DictionaryField(boundStage(target), "Checkpoints", markerKey);
                            return point == null ? null : ReadField(point, "Marker");
                        }, marker);
                    }
                }
            }
        }
    }
}
