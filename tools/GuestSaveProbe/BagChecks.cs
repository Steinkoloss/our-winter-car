using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    internal static class BagChecks
    {
        private const BindingFlags HiddenInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private sealed class CountEntry : FsmStateAction
        {
            internal int Count;
            public override void OnEnter() { Count++; Finish(); }
        }

        internal static void Run(Action<string, Action> check)
        {
            SyncCatalog.EnsureLoaded();
            Require(typeof(SyncCatalog).GetProperty("ShoppingBags", BindingFlags.Static | BindingFlags.NonPublic)
                .GetValue(null, null) != null, "Current shopping bag catalog is missing.");
            var session = SessionManager.Instance!;
            SessionState savedState = session.State; bool savedHost = session.IsHost;
            var core = typeof(SessionManager).Assembly;
            var itemsType = core.GetType("WinterMP.Core.Sync.ItemWorldSync", true);
            var items = Activator.CreateInstance(itemsType, new object[] { null! });
            var hook = core.GetType("WinterMP.Core.Sync.FsmHook", true);
            var guard = itemsType.GetMethod("GuardUnboundBag", HiddenInstance);
            var clear = itemsType.GetMethod("ClearBags", HiddenInstance);
            var bagObject = new GameObject("probe shopping bag"); bagObject.SetActive(false);
            var contentsObject = new GameObject("probe bag contents"); contentsObject.SetActive(false);
            var bag = MakeFsm(bagObject, "Use", new[] { "Wait player", "Confirm", "Spawn one", "Spawn all", "Is garbage" });
            var contents = MakeFsm(contentsObject, "Logic", new[] { "Idle", "Spawned" });
            var received = new CountEntry();
            FsmStateAction[]? originalOne = null, originalAll = null;
            try
            {
                contentsObject.SetActive(true); bagObject.SetActive(true);
                contents.Fsm.States[1].Actions = new FsmStateAction[] { received };
                received.Init(contents.Fsm.States[1]);
                contents.Fsm.GlobalTransitions = new[] {
                    new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("PROBE_SPAWN"), ToState = "Spawned" } };
                foreach (var state in bag.Fsm.States)
                {
                    state.Actions = state.Name == "Wait player" ? new FsmStateAction[0]
                        : new[] { NativeSend(contentsObject) };
                    foreach (var action in state.Actions) action.Init(state);
                    Require((bool)hook.GetMethod("EnsureRemoteEntry").Invoke(null, new object[] { bag, state.Name }), "Cannot enter bag fixture state.");
                }
                originalOne = bag.Fsm.States[2].Actions; originalAll = bag.Fsm.States[3].Actions;
                contents.enabled = true; bag.enabled = true;
                if (!contents.Fsm.Started) contents.Fsm.Start();
                if (!bag.Fsm.Started) bag.Fsm.Start();
                Action<string> fire = state => hook.GetMethod("FireRemoteEntry").Invoke(null, new object[] { bag, state });
                Action idle = () => fire("Wait player");
                Action<string> blocked = state =>
                {
                    int before = received.Count;
                    idle(); fire(state);
                    Require(bag.ActiveStateName == "Wait player" && received.Count == before,
                        "Native " + state + " continued after the bag guard.");
                };
                check("shopping bags: native send fixture reaches the contents FSM", () =>
                {
                    int before = received.Count;
                    fire("Spawn one"); idle(); fire("Spawn all"); idle();
                    Require(bag.Fsm.Started && contents.Fsm.Started && received.Count == before + 2,
                        "Native SendEventByName baseline did not run twice: count=" + received.Count + " before=" + before
                        + " bag=" + bag.ActiveStateName + " contents=" + contents.ActiveStateName + " name=" + contents.FsmName);
                });
                guard.Invoke(items, new object[] { bag });
                SetSession(session, SessionState.Connected, false);
                check("shopping bags: guest guard stops native one and all spills", () =>
                { blocked("Spawn one"); blocked("Spawn all"); });
                check("shopping bags: guest guard also prevents unbound selection and garbage", () =>
                { blocked("Confirm"); blocked("Is garbage"); });
                SetSession(session, SessionState.Hosting, true);
                check("shopping bags: host cannot open a bag before identity binding", () =>
                { blocked("Spawn one"); blocked("Spawn all"); });
                SetSession(session, SessionState.Idle, false);
                check("shopping bags: teardown restores native action delivery", () =>
                {
                    clear.Invoke(items, null);
                    Require(bag.Fsm.States[2].Actions.Length == originalOne.Length
                        && bag.Fsm.States[3].Actions.Length == originalAll.Length
                        && ReferenceEquals(bag.Fsm.States[2].Actions[0], originalOne[0])
                        && ReferenceEquals(bag.Fsm.States[3].Actions[0], originalAll[0]), "Native spill actions changed during teardown.");
                    int before = received.Count;
                    fire("Spawn one"); idle(); fire("Spawn all"); idle();
                    Require(received.Count == before + 2, "Native spills were not restored.");
                });
            }
            finally
            {
                SetSession(session, savedState, savedHost);
                clear.Invoke(items, null);
                UnityEngine.Object.DestroyImmediate(bagObject); UnityEngine.Object.DestroyImmediate(contentsObject);
            }
        }

        private static PlayMakerFSM MakeFsm(GameObject owner, string name, string[] names)
        {
            var fsm = owner.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
            typeof(PlayMakerFSM).GetField("fsm", HiddenInstance).SetValue(fsm, new Fsm());
            fsm.Fsm.Name = name;
            var states = new List<FsmState>();
            foreach (string state in names) states.Add(new FsmState(fsm.Fsm) { Name = state, Actions = new FsmStateAction[0] });
            fsm.Fsm.States = states.ToArray(); fsm.Fsm.StartState = names[0];
            return fsm;
        }

        private static FsmStateAction NativeSend(GameObject target)
        {
            Type? type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if ((type = assembly.GetType("HutongGames.PlayMaker.Actions.SendEventByName")) != null) break;
            var action = (FsmStateAction)Activator.CreateInstance(type ?? throw new InvalidOperationException("Native send action missing."));
            action.Reset(); action.Enabled = true;
            type.GetField("eventTarget").SetValue(action, new FsmEventTarget { target = FsmEventTarget.EventTarget.GameObjectFSM,
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject(target) },
                fsmName = new FsmString { Value = "Logic" } });
            type.GetField("sendEvent").SetValue(action, new FsmString { Value = "PROBE_SPAWN" });
            type.GetField("delay").SetValue(action, new FsmFloat(0));
            type.GetField("everyFrame").SetValue(action, false);
            return action;
        }

        private static void SetSession(SessionManager session, SessionState state, bool host)
        {
            typeof(SessionManager).GetProperty("State").GetSetMethod(true).Invoke(session, new object[] { state });
            typeof(SessionManager).GetProperty("IsHost").GetSetMethod(true).Invoke(session, new object[] { host });
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
