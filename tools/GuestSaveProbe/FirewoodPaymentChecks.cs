using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    internal static class FirewoodPaymentChecks
    {
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly Type Guard = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Sync.FirewoodPaymentGuard", true);
        private sealed class Entry : FsmStateAction
        {
            internal Action Callback = null!;
            public override void OnEnter() { Callback(); Finish(); }
        }
        internal static void Run(Action<string, Action> check)
        {
            var readerType = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var reader = Activator.CreateInstance(readerType, Members, null, new object[] {
                File.ReadAllText(Path.Combine(Application.dataPath, "../native-payments.json")) }, null);
            var json = (Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null);
            foreach (Dictionary<string, object> row in (List<object>)json["fsms"]) RunCase(row, check);
        }
        private static object Make(PlayMakerFSM fsm) => Activator.CreateInstance(Guard, Members, null, new object[] { fsm }, null);
        private static object Call(object guard, string method, params object[] args) => Guard.GetMethod(method, Members).Invoke(guard, args);
        private static void Require(bool value) { if (!value) throw new InvalidOperationException("Firewood payment assertion failed."); }
        private static void RunCase(Dictionary<string, object> row, Action<string, Action> check)
        {
            string label = ((string)row["path"]).Split('/')[1] + ": ";
            var root = new GameObject("native firewood payout fixture"); root.SetActive(false);
            var fsm = NativeBagPartChecks.MakeFsm(root, row);
            try
            {
                root.SetActive(true);
                NativeBagPartChecks.LoadActions(fsm, row, "State 1");
                var state = NativeBagPartChecks.State(fsm, "State 1"); var native = state.Actions;
                // Observe the payment while its animation would still be running.
                // Audio and presentation need scene objects absent from this fixture.
                state.Transitions = new FsmTransition[0];
                NativeBagPartChecks.State(fsm, "Wait player").Transitions = new FsmTransition[0];
                for (int i = 0; i < native.Length; i++) if (i != 4 && i != 5) native[i].Enabled = false;
                var cash = (FsmFloat)native[4].GetType().GetField("floatVariable").GetValue(native[4]);
                var income = (FsmFloat)native[5].GetType().GetField("floatVariable").GetValue(native[5]);
                var money = fsm.FsmVariables.FindFsmFloat("Money");
                NativeBagPartChecks.Start(fsm); money.Value = 500; cash.Value = income.Value = 0;
                check(label + "unguarded native entry reproduces duplicate cash and income", () =>
                {
                    NativeBagPartChecks.Fire(fsm, "State 1"); NativeBagPartChecks.Fire(fsm, "State 1");
                    Require(cash.Value == 1000 && income.Value == 1000);
                });
                object guard = Make(fsm); bool guest = false;
                var entry = new Entry { Callback = () => Call(guard, "Enter", guest) };
                var actions = new List<FsmStateAction>(native); actions.Insert(0, entry); state.Actions = actions.ToArray(); entry.Init(state);
                // Guard validation uses the untouched native list, as registration does.
                Action ready = () => { NativeBagPartChecks.Fire(fsm, "Wait player"); Call(guard, "Ready"); };
                cash.Value = income.Value = 0;
                check(label + "two requests reserve only one offer before native execution", () =>
                {
                    ready(); Require((bool)Call(guard, "Reserve")); Require(!(bool)Call(guard, "Reserve"));
                });
                check(label + "reserved native payment credits exactly once", () =>
                {
                    NativeBagPartChecks.Fire(fsm, "State 1"); NativeBagPartChecks.Fire(fsm, "State 1");
                    Require(cash.Value == 500 && income.Value == 500);
                });
                check(label + "payment animation refuses a further request", () => Require(!(bool)Call(guard, "Reserve")));
                check(label + "a new native offer can be collected", () =>
                {
                    ready(); NativeBagPartChecks.Fire(fsm, "State 1"); Require(cash.Value == 1000 && income.Value == 1000);
                });
                check(label + "guest native clicks cannot predict cash or taxable income", () =>
                {
                    guest = true; ready(); NativeBagPartChecks.Fire(fsm, "State 1");
                    Require(cash.Value == 1000 && income.Value == 1000 && !native[4].Enabled && !native[5].Enabled);
                });
                check(label + "empty and nonfinite offers are refused", () =>
                {
                    foreach (float value in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
                    { ready(); money.Value = value; Require(!(bool)Call(guard, "Reserve")); }
                    money.Value = 500;
                });
                check(label + "disabled native payment is refused", () =>
                {
                    ready(); fsm.enabled = false; Require(!(bool)Call(guard, "Reserve")); fsm.enabled = true;
                });
                check(label + "cleanup restores original credit action flags", () =>
                {
                    Call(guard, "Restore"); Require(native[4].Enabled && native[5].Enabled);
                });
                state.Actions = native;
                check(label + "changed native credit destination fails binding", () =>
                {
                    string name = cash.Name; cash.Name = "UnrelatedBalance";
                    try { Make(fsm); throw new InvalidOperationException("Changed destination accepted."); }
                    catch (TargetInvocationException e) { Require(e.InnerException is InvalidOperationException); }
                    finally { cash.Name = name; }
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
