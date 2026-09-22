using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static class MilkConditionChecks
    {
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        private static readonly Type Binding = Core.GetType("WinterMP.Core.Sync.MilkConditionBinding", true);
        private static object Call(object target, string method, params object[] args) => Binding.GetMethod(method, Members).Invoke(target, args);
        private static void Require(bool value) { if (!value) throw new InvalidOperationException("Milk condition assertion failed."); }
        internal static void Run(Action<string, Action> check)
        {
            var catalogType = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
            catalogType.GetMethod("EnsureLoaded", Members).Invoke(null, null);
            var catalog = catalogType.GetProperty("MilkCondition", Members).GetValue(null, null);
            var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var reader = Activator.CreateInstance(readerType, Members, null, new object[] {
                File.ReadAllText(Path.Combine(Application.dataPath, "../native-milk.json")) }, null);
            var json = (Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null);
            var row = NativeBagPartChecks.Find((List<object>)json["fsms"], "milk", "Use");
            foreach (bool guest in new[] { false, true }) RunRole(row, catalog, guest, check);
        }
        private static void RunRole(Dictionary<string, object> row, object catalog, bool guest, Action<string, Action> check)
        {
            string label = "milk " + (guest ? "guest: " : "host: ");
            var root = new GameObject("milk(itemx)"); root.SetActive(false);
            var fsm = NativeBagPartChecks.MakeFsm(root, row); var body = root.AddComponent<Rigidbody>();
            object? binding = null;
            try
            {
                root.SetActive(true);
                NativeBagPartChecks.LoadActions(fsm, row, "Spoil 2", "In Fridge", "Bad", "Check drink");
                foreach (string name in new[] { "Wait player 2", "Wait button 2", "Eat 2" })
                    NativeBagPartChecks.State(fsm, name).Transitions = new FsmTransition[0];
                var spoil = NativeBagPartChecks.State(fsm, "Spoil 2"); var spoilActions = spoil.Actions;
                var fridge = NativeBagPartChecks.State(fsm, "In Fridge"); var fridgeActions = fridge.Actions;
                var drink = NativeBagPartChecks.State(fsm, "Check drink"); var drinkActions = drink.Actions;
                var condition = fsm.FsmVariables.FindFsmFloat("Condition");
                // The full scene test covers the fridge lookup. Isolate the native
                // subtraction here without inventing the absent scene's Areas list.
                for (int i = 0; i < 4; i++) spoilActions[i].Enabled = false;
                NativeBagPartChecks.Start(fsm); NativeBagPartChecks.Fire(fsm, "Wait player 2"); condition.Value = 80;
                binding = Activator.CreateInstance(Binding, Members, null, new object[] { fsm, body, catalog, guest }, null);
                Action<string, Action> test = (name, action) => check(label + name, action);
                Func<float, byte, uint, MilkConditionState> state = (value, bad, revision) => new MilkConditionState {
                    NetId = 42, Condition = value, Spoiled = bad, Revision = revision };
                if (!guest)
                {
                    test("binding preserves native action arrays", () => Require(ReferenceEquals(spoil.Actions, spoilActions)
                        && ReferenceEquals(fridge.Actions, fridgeActions) && ReferenceEquals(drink.Actions, drinkActions)));
                    var first = (MilkConditionState)Call(binding, "Capture", 42u);
                    test("warm native decay advances published revision", () =>
                    {
                        NativeBagPartChecks.Fire(fsm, "Spoil 2");
                        var next = (MilkConditionState)Call(binding, "Capture", 42u);
                        Require(Math.Abs(condition.Value - (80 - .006f)) < .0001f && next.Revision == first.Revision + 1);
                    });
                    test("cold native decay is six times slower", () =>
                    {
                        float before = condition.Value; NativeBagPartChecks.Fire(fsm, "In Fridge");
                        Require(Math.Abs(condition.Value - (before - .001f)) < .0001f);
                    });
                    test("repeat snapshot preserves revision and returns independent copy", () =>
                    {
                        var a = (MilkConditionState)Call(binding, "Capture", 42u); var b = (MilkConditionState)Call(binding, "Capture", 42u);
                        Require(a.Revision == b.Revision && !ReferenceEquals(a, b)); a.Condition = 0;
                        Require(((MilkConditionState)Call(binding, "Capture", 42u)).Condition == b.Condition);
                    });
                    test("native bad phase publishes spoiled presentation", () =>
                    {
                        condition.Value = .5f; NativeBagPartChecks.Fire(fsm, "Bad");
                        Require(((MilkConditionState)Call(binding, "Capture", 42u)).Spoiled == 1 && root.name == "spoiled milk(itemx)");
                    });
                    test("consumed sentinel is never published as milk condition", () =>
                    { condition.Value = 888; Require(Call(binding, "Capture", 42u) == null); });
                    return;
                }
                test("unseeded native drink is blocked", () =>
                { NativeBagPartChecks.Fire(fsm, "Check drink"); Require(fsm.ActiveStateName == "Wait player 2"); });
                test("condition snapshot waits for native load to finish", () =>
                {
                    NativeBagPartChecks.State(fsm, "Load").Transitions = new FsmTransition[0]; NativeBagPartChecks.Fire(fsm, "Load");
                    Require(!(bool)Call(binding, "Apply", state(60, 0, 1)) && condition.Value == 80);
                    NativeBagPartChecks.Fire(fsm, "Wait player 2");
                });
                test("first host condition is copied after load", () =>
                {
                    var input = state(60, 0, 1); Require((bool)Call(binding, "Apply", input) && condition.Value == 60);
                    input.Condition = 0; Require(condition.Value == 60);
                });
                test("guest native warm and fridge ticks do not decay", () =>
                {
                    for (int i = 0; i < 100; i++) { NativeBagPartChecks.Fire(fsm, "Spoil 2"); NativeBagPartChecks.Fire(fsm, "In Fridge"); }
                    Require(condition.Value == 60 && fsm.ActiveStateName == "Wait player 2");
                });
                test("fresh host seed allows native drink prerequisites", () =>
                { NativeBagPartChecks.Fire(fsm, "Check drink"); Require(fsm.ActiveStateName == "Eat 2"); NativeBagPartChecks.Fire(fsm, "Wait player 2"); });
                test("equal authoritative revision repairs local drift", () =>
                { condition.Value = 10; Call(binding, "Apply", state(60, 0, 1)); Require(condition.Value == 60); });
                test("old and contradictory same revision cannot rewind condition", () =>
                { Call(binding, "Apply", state(90, 0, 0)); Call(binding, "Apply", state(90, 0, 1)); Require(condition.Value == 60); });
                test("host bad phase uses native rename", () =>
                { Call(binding, "Apply", state(.5f, 1, 2)); Require(root.name == "spoiled milk(itemx)" && fsm.ActiveStateName == "Bad"); });
                test("spoiled milk cannot enter native drinking", () =>
                { NativeBagPartChecks.Fire(fsm, "Check drink"); Require(fsm.ActiveStateName == "Bad" && condition.Value == .5f); });
                test("fresh correction leaves the terminal bad phase", () =>
                { Call(binding, "Apply", state(50, 0, 3)); Require(root.name == "milk(itemx)" && fsm.ActiveStateName == "Wait player 2"); });
                test("cleanup restores native state and actions", () =>
                {
                    Call(binding, "Restore"); Require(condition.Value == 80 && root.name == "milk(itemx)"
                        && ReferenceEquals(spoil.Actions, spoilActions) && ReferenceEquals(fridge.Actions, fridgeActions)
                        && ReferenceEquals(drink.Actions, drinkActions));
                    foreach (var action in drinkActions) Require(action.Enabled);
                });
                test("native decay resumes after cleanup", () =>
                { NativeBagPartChecks.Fire(fsm, "In Fridge"); Require(Math.Abs(condition.Value - 79.999f) < .0001f); });
                test("changed native decay target is refused before cutting actions", () =>
                {
                    var target = fridgeActions[0].GetType().GetField("floatVariable"); var old = target.GetValue(fridgeActions[0]);
                    target.SetValue(fridgeActions[0], new FsmFloat { Value = 80 });
                    try
                    {
                        try { Activator.CreateInstance(Binding, Members, null, new object[] { fsm, body, catalog, true }, null);
                            throw new InvalidOperationException("Changed decay target accepted."); }
                        catch (TargetInvocationException e) { Require(e.InnerException is InvalidOperationException); }
                        Require(ReferenceEquals(spoil.Actions, spoilActions));
                    }
                    finally { target.SetValue(fridgeActions[0], old); }
                });
            }
            finally { if (binding != null) Call(binding, "Restore"); UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
