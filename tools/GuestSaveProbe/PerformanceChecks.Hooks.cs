using System;
using System.Collections;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class PerformanceChecks
    {
        private static void CheckUninitializedActions(Action<string, Action> check)
        {
            // These checks target the development fix, not the older release
            // payloads that the same benchmark can measure.
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
            if (catalog.GetNestedType("CatalogRuleSet", BindingFlags.NonPublic) == null) return;
            var root = new GameObject("Performance uninitialized state"); root.SetActive(false);
            try
            {
                var fsm = root.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
                typeof(PlayMakerFSM).GetField("fsm", Members).SetValue(fsm, new Fsm());
                var state = new FsmState((Fsm)null!) { Name = "Pending" };
                fsm.Fsm.States = new[] { state };
                var hooks = Core.GetType("WinterMP.Core.Sync.FsmHook", true);
                var onEnter = hooks.GetMethod("OnStateEnter", Static, null,
                    new[] { typeof(PlayMakerFSM), typeof(string), typeof(Action) }, null);
                int entered = 0;
                Action callback = () => entered++;
                Func<bool> hook = () => (bool)onEnter.Invoke(null, new object[] { fsm, state.Name, callback });
                var walletType = Core.GetType("WinterMP.Core.Sync.WalletSync", true);
                var wallet = Activator.CreateInstance(walletType, true);
                var bank = walletType.GetMethod("HookBankMutation", Members);
                Action bankHook = () => bank.Invoke(wallet, new object?[] { fsm, state.Name, "FloatAdd", "Money", null });
                Func<int> bankCount = () => ((ICollection)walletType.GetField("_bankHooks", Members).GetValue(wallet)).Count;
                check("performance readiness: state-entry retry does not deserialize an uninitialized state", () =>
                { for (int i = 0; i < 20; i++) Require(!hook()); Require(!state.ActionsLoaded && entered == 0); });
                check("performance readiness: banking retry does not deserialize an uninitialized state", () =>
                { for (int i = 0; i < 20; i++) bankHook(); Require(!state.ActionsLoaded && bankCount() == 0); });
                check("performance readiness: vehicle binding defers an uninitialized state without loading actions", () =>
                {
                    RequireDeferred(() => Core.GetType("WinterMP.Core.Sync.VehicleWorldSync", true)
                        .GetMethod("HeatState", Static).Invoke(null, new object[] { fsm, state.Name }));
                    Require(!state.ActionsLoaded);
                });
                check("performance readiness: guest engine validation rejects an uninitialized writer without loading actions", () =>
                {
                    RequireDeferred(() => Core.GetType("WinterMP.Core.Sync.GuestEngineProtection", true)
                        .GetMethod("FindAction", Static).Invoke(null, new object[] { fsm, state.Name, 0, "FloatAdd" }));
                    Require(!state.ActionsLoaded);
                });
                check("performance readiness: package validation leaves pending template actions unloaded", () =>
                {
                    for (int i = 0; i < 20; i++)
                        RequireDeferred(() => Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true)
                            .GetMethod("PackageStateActions", Static).Invoke(null, new object[] { fsm, state.Name, new string[0] }));
                    Require(!state.ActionsLoaded);
                });
                check("performance readiness: RPM validation leaves pending producer actions unloaded", () =>
                {
                    for (int i = 0; i < 20; i++)
                        RequireDeferred(() => Core.GetType("WinterMP.Core.Sync.VehicleWorldSync", true)
                            .GetMethod("NativeRpmAction", Static).Invoke(null, new object[] { state, 0 }));
                    Require(!state.ActionsLoaded);
                });
                state.Fsm = fsm.Fsm;
                var action = (FsmStateAction)Activator.CreateInstance(Assembly.Load("Assembly-CSharp")
                    .GetType("HutongGames.PlayMaker.Actions.FloatAdd", true));
                action.GetType().GetField("floatVariable").SetValue(action, new FsmFloat { Name = "Money" });
                action.Enabled = true;
                state.Actions = new[] { action };
                check("performance readiness: state-entry hook succeeds after initialization and preserves native action", () =>
                {
                    Require(hook() && state.Actions.Length == 2 && ReferenceEquals(state.Actions[1], action));
                    state.Actions[0].Init(state); state.Actions[0].OnEnter(); Require(entered == 1);
                });
                check("performance readiness: banking hook succeeds after initialization without duplicate mutation", () =>
                { bankHook(); bankHook(); Require(bankCount() == 1 && state.Actions.Length == 3 && ReferenceEquals(state.Actions[2], action)); });
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void RequireDeferred(Action action)
        {
            try { action(); }
            catch (TargetInvocationException error)
            {
                Require(error.InnerException is InvalidOperationException);
                return;
            }
            throw new InvalidOperationException("Uninitialized action validation unexpectedly succeeded.");
        }
    }
}
