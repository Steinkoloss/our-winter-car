using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class PerformanceChecks
    {
        private static void MeasureProtectionRules(List<string> rows, Action<string, Action> check)
        {
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
            var property = catalog.GetProperty("GuestEngineProtection", Static);
            var previous = property.GetValue(null, null);
            var guard = Core.GetType("WinterMP.Core.Sync.GuestEngineProtection", true);
            var match = (Func<PlayMakerFSM, object>)Delegate.CreateDelegate(typeof(Func<PlayMakerFSM, object>), guard.GetMethod("FindRule", Static));
            var paths = Core.GetType("WinterMP.Core.Sync.ScenePath", true);
            var path = (Func<Transform, string>)Delegate.CreateDelegate(typeof(Func<Transform, string>), paths.GetMethod("Of", Static));
            var scan = (Func<IEnumerable<UnityEngine.Object>>)Delegate.CreateDelegate(typeof(Func<IEnumerable<UnityEngine.Object>>), paths.GetMethod("ScanFsms", Static));
            var root = new GameObject("Engine rule fixture"); root.SetActive(false);
            try
            {
                var parent = root;
                for (int depth = 0; depth < 6; depth++)
                {
                    for (int i = 0; i < 16; i++) Child(parent, "Unrelated" + i);
                    parent = Child(parent, "Nested" + depth);
                }
                var leaf = Child(parent, "Unrelated");
                var fsm = leaf.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
                typeof(PlayMakerFSM).GetField("fsm", Members).SetValue(fsm, new Fsm()); fsm.Fsm.Name = "Data";
                foreach (string name in new[] { "Unrelated", "VINP_Battery" })
                {
                    leaf.name = name;
                    Measure(rows, 10000, "engine-rule-wrong-path-" + name, () =>
                    { for (int i = 0; i < 10000; i++) if (match(fsm) != null) throw new InvalidOperationException("Unrelated path matched engine protection."); });
                    check("engine rule: " + name + " still requires its full current path", () => Require(match(fsm) == null));
                }

                var profile = Activator.CreateInstance(previous.GetType(), true);
                var writers = (IList)ReadField(profile, "Writers");
                var pauses = (IList)ReadField(profile, "PausedFsms");
                var writer = Activator.CreateInstance(Core.GetType("WinterMP.Core.Catalog.GuestEngineWriterData", true), true);
                var paused = Activator.CreateInstance(Core.GetType("WinterMP.Core.Catalog.GuestEnginePausedFsmData", true), true);
                Action<object, string, string> set = (rule, key, value) => rule.GetType().GetField(key, Members).SetValue(rule, value);
                set(writer, "Fsm", "Data"); set(paused, "Fsm", "Data");
                writers.Add(writer); pauses.Add(paused); property.GetSetMethod(true).Invoke(null, new[] { profile });
                set(writer, "Path", path(leaf.transform)); set(paused, "Path", path(leaf.transform));
                check("engine rule: writer priority precedes an equally matching paused rule", () => Require(ReferenceEquals(match(fsm), writer)));
                Measure(rows, 10000, "engine-rule-matching-leaf", () =>
                { for (int i = 0; i < 10000; i++) if (!ReferenceEquals(match(fsm), writer)) throw new InvalidOperationException("Matching rule was lost."); });
                check("engine rule: changed earlier rule falls through to the paused match", () =>
                { set(writer, "Path", "Other/Impossible"); Require(ReferenceEquals(match(fsm), paused)); set(writer, "Path", path(leaf.transform)); });
                check("engine rule: rename and repair are observed between calls", () =>
                { leaf.name = "Renamed"; Require(match(fsm) == null); leaf.name = "VINP_Battery"; Require(ReferenceEquals(match(fsm), writer)); });
                check("engine rule: reparent and repair are observed between calls", () =>
                { leaf.transform.SetParent(root.transform, false); Require(match(fsm) == null); leaf.transform.SetParent(parent.transform, false); Require(ReferenceEquals(match(fsm), writer)); });
                check("engine rule: duplicate sibling indices retain the complete literal name", () =>
                {
                    leaf.name = "Mount[0]"; var duplicate = Child(parent, leaf.name);
                    try
                    {
                        set(writer, "Path", path(leaf.transform)); Require(ReferenceEquals(match(fsm), writer));
                        leaf.transform.SetAsLastSibling(); Require(match(fsm) == null);
                        set(writer, "Path", path(leaf.transform)); Require(ReferenceEquals(match(fsm), writer));
                    }
                    finally { UnityEngine.Object.DestroyImmediate(duplicate); }
                });
                check("engine rule: literal separators still match the existing rendered path", () =>
                { leaf.name = "Literal/Leaf"; set(writer, "Path", path(leaf.transform)); Require(ReferenceEquals(match(fsm), writer)); });
                check("engine rule: active discovery keeps its existing path snapshot", () =>
                {
                    using (var objects = scan().GetEnumerator())
                    {
                        Require(objects.MoveNext()); path(leaf.transform); leaf.name = "Changed during scan";
                        Require(ReferenceEquals(match(fsm), writer));
                    }
                    Require(match(fsm) == null);
                });
                check("engine rule: moving native mount fallback survives an unrelated static leaf", () =>
                {
                    parent.name = "VIN1010"; leaf.name = "CurrentMount";
                    set(paused, "Path", "Original/DifferentMount"); set(paused, "RootPrefix", "VIN101"); set(paused, "RelativePath", leaf.name);
                    Require(ReferenceEquals(match(fsm), paused));
                    parent.name = "UnrelatedRoot"; Require(match(fsm) == null);
                    parent.name = "VIN1010"; Require(ReferenceEquals(match(fsm), paused));
                });
                check("engine rule: moving mount rename and FSM rename stay live", () =>
                {
                    leaf.name = "WrongMount"; Require(match(fsm) == null);
                    leaf.name = "CurrentMount"; Require(ReferenceEquals(match(fsm), paused));
                    fsm.Fsm.Name = "Other"; Require(match(fsm) == null);
                    fsm.Fsm.Name = "Data"; Require(ReferenceEquals(match(fsm), paused));
                });
                check("engine rule: moving mount catalog edits take effect immediately", () =>
                {
                    set(paused, "RelativePath", "AnotherMount"); Require(match(fsm) == null);
                    set(paused, "RelativePath", "CurrentMount"); Require(ReferenceEquals(match(fsm), paused));
                });
                check("engine rule: renamed moving part uses its current native ID", () =>
                {
                    var data = parent.AddComponent<PlayMakerFSM>(); data.enabled = false;
                    typeof(PlayMakerFSM).GetField("fsm", Members).SetValue(data, new Fsm()); data.Fsm.Name = "Data";
                    var id = new FsmString { Name = "ID", UseVariable = true, Value = "VIN101123" };
                    data.FsmVariables.StringVariables = new[] { id }; parent.name = "Renamed block";
                    try
                    {
                        Require(ReferenceEquals(match(fsm), paused));
                        id.Value = "Unrelated123"; Require(match(fsm) == null);
                        id.Value = "VIN101123"; Require(ReferenceEquals(match(fsm), paused));
                    }
                    finally { parent.name = "VIN1010"; UnityEngine.Object.DestroyImmediate(data); }
                });
                check("engine rule: moving fallback keeps live names inside a discovery snapshot", () =>
                {
                    using (var objects = scan().GetEnumerator())
                    {
                        Require(objects.MoveNext()); path(leaf.transform); leaf.name = "WrongMount";
                        Require(match(fsm) == null);
                        leaf.name = "CurrentMount"; Require(ReferenceEquals(match(fsm), paused));
                    }
                });
                check("engine rule: catalog replacement takes effect immediately", () =>
                {
                    property.GetSetMethod(true).Invoke(null, new[] { Activator.CreateInstance(previous.GetType(), true) }); Require(match(fsm) == null);
                    property.GetSetMethod(true).Invoke(null, new[] { profile }); Require(ReferenceEquals(match(fsm), paused));
                });
            }
            finally
            {
                property.GetSetMethod(true).Invoke(null, new[] { previous });
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void CheckCatalogIndex(Action<string, Action> check)
        {
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
            var setType = catalog.GetNestedType("CatalogRuleSet", System.Reflection.BindingFlags.NonPublic);
            // Permit the identical workload to profile older released assemblies.
            if (setType == null) return;
            var ruleType = catalog.GetNestedType("CatalogRule", System.Reflection.BindingFlags.NonPublic);
            var rules = Activator.CreateInstance(setType, true);
            var add = setType.GetMethod("Add", Members);
            Action<string, string> rule = (name, state) => add.Invoke(rules, new[] { Activator.CreateInstance(ruleType, Members, null,
                new object?[] { "Performance catalog", null, null, null, name, new[] { state }, null, null, new string[0], new string[0] }, null) });
            Action clear = () => setType.GetMethod("Clear", Members).Invoke(rules, null);
            var root = new GameObject("Performance catalog"); root.SetActive(false);
            try
            {
                var fsm = root.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
                typeof(PlayMakerFSM).GetField("fsm", Members).SetValue(fsm, new Fsm());
                fsm.Fsm.Name = "Use";
                fsm.Fsm.States = new[] { new FsmState(fsm.Fsm) { Name = "On", Actions = new FsmStateAction[0] },
                    new FsmState(fsm.Fsm) { Name = "Off", Actions = new FsmStateAction[0] } };
                Func<string[]?> match = () => (string[]?)catalog.GetMethod("TryMatch", Static).Invoke(null, new[] { rules, fsm });
                check("performance catalog: unrelated FSM name cannot match", () => { rule("Other", "On"); Require(match() == null); });
                check("performance catalog: missing required state falls through within the name group", () =>
                { rule("Use", "Missing"); rule("Use", "On"); Require(match()?[0] == "On"); });
                check("performance catalog: first valid rule retains catalog priority", () =>
                { clear(); rule("Use", "Off"); rule("Use", "On"); Require(match()?[0] == "Off"); });
                check("performance catalog: current path still rejects renamed objects", () =>
                { root.name = "Renamed performance catalog"; Require(match() == null); root.name = "Performance catalog"; });
                check("performance catalog: clearing for reload removes old name groups", () =>
                { clear(); Require(match() == null && (int)setType.GetProperty("Count", Members).GetValue(rules, null) == 0); rule("Use", "On"); Require(match()?[0] == "On"); });
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void WithDiscoveryRole(bool guest, Action run)
        {
            var session = SessionManager.Instance ?? throw new InvalidOperationException("Missing discovery probe session.");
            var host = typeof(SessionManager).GetProperty("IsHost", Members); bool savedHost = session.IsHost;
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protection = policy.GetType().GetProperty("ProtectWorld", Members); bool savedProtection = (bool)protection.GetValue(policy, null);
            try
            {
                host.SetValue(session, !guest, null); protection.GetSetMethod(true).Invoke(policy, new object[] { guest });
                run();
            }
            finally
            {
                host.SetValue(session, savedHost, null); protection.GetSetMethod(true).Invoke(policy, new object[] { savedProtection });
            }
        }

        private static void MeasureGuestWorldScan(List<string> rows, int count, Action scan)
            => WithDiscoveryRole(true, () => Measure(rows, count, "guest-world-scan-active", scan));

        private static void CheckWorldDiscovery(object world, Action scan, Action<string, Action> check)
        {
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
            var ruleType = catalog.GetNestedType("CatalogRule", System.Reflection.BindingFlags.NonPublic);
            var fsmSync = ReadField(world, "_fsm");
            foreach (bool guest in new[] { false, true })
                foreach (string kind in new[] { "_controls", "_starters" })
                    WithDiscoveryRole(guest, () =>
                    {
                        var rules = catalog.GetField(kind, Static).GetValue(null);
                        var add = rules.GetType().GetMethod("Add", Members); var clear = rules.GetType().GetMethod("Clear", Members);
                        var previous = new List<object>();
                        foreach (IEnumerable group in ((IDictionary)ReadField(rules, "_byName")).Values)
                            foreach (var rule in group) previous.Add(rule);
                        string expectedName = "Performance custom " + kind;
                        var root = new GameObject("Performance discovery"); root.SetActive(false);
                        var other = new GameObject("Unrelated discovery"); other.SetActive(false);
                        var node = Child(other, "Accepted");
                        var fsm = node.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
                        typeof(PlayMakerFSM).GetField("fsm", Members).SetValue(fsm, new Fsm());
                        fsm.Fsm.Name = "Unlisted performance logic"; fsm.Fsm.StartState = "Idle";
                        var state = new FsmState(fsm.Fsm) { Name = "Idle", Actions = new FsmStateAction[0] };
                        fsm.Fsm.States = new[] { state };
                        var registry = (IDictionary)ReadField(fsmSync, kind);
                        var hooks = (IDictionary)ReadField(world, "_hookedFsms");
                        Action<string> ruleFor = name => add.Invoke(rules, new[] { Activator.CreateInstance(ruleType, Members, null,
                            new object?[] { "Performance discovery/Accepted", null, null, null, name, new[] { "Idle" }, null, null, new string[0], new string[0] }, null) });
                        string label = "world discovery " + (guest ? "guest " : "host ") + kind + ": ";
                        try
                        {
                            root.SetActive(true); other.SetActive(true); NativeBagPartChecks.Start(fsm);
                            ruleFor(expectedName);
                            check(label + "unlisted live FSM stays unregistered", () => { scan(); Require(registry.Count == 0 && !hooks.Contains(fsm)); });
                            check(label + "catalogued name still requires its current path", () =>
                            { fsm.Fsm.Name = expectedName; scan(); Require(registry.Count == 0 && !hooks.Contains(fsm)); });
                            check(label + "later reparent discovers a custom catalog name", () =>
                            { node.transform.SetParent(root.transform, false); scan(); Require(registry.Count == 1 && hooks.Contains(fsm)); });
                            check(label + "repeat scan retains ids and avoids duplicate hooks", () =>
                            {
                                object hash = world.GetType().GetProperty("IdHash", Members).GetValue(world, null);
                                var actions = state.Actions; scan();
                                Require(registry.Count == 1 && ReferenceEquals(actions, state.Actions)
                                    && hash.Equals(world.GetType().GetProperty("IdHash", Members).GetValue(world, null)));
                            });
                            check(label + "catalog reload admits a newly named consumer", () =>
                            {
                                fsmSync.GetType().GetMethod("Clear", Members).Invoke(fsmSync, null);
                                clear.Invoke(rules, null); fsm.Fsm.Name = expectedName + " reloaded";
                                scan(); Require(registry.Count == 0 && !hooks.Contains(fsm));
                                ruleFor(fsm.FsmName); scan(); Require(registry.Count == 1 && hooks.Contains(fsm));
                            });
                        }
                        finally
                        {
                            fsmSync.GetType().GetMethod("Clear", Members).Invoke(fsmSync, null);
                            clear.Invoke(rules, null); foreach (var rule in previous) add.Invoke(rules, new[] { rule });
                            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(other);
                        }
                    });
        }
    }
}
