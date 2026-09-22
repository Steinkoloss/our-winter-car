using System;
using System.Collections;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Core.UI;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static class SleepConsentChecks
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private sealed class Hold : FsmStateAction { public override void OnEnter() { } }
        private sealed class Count : FsmStateAction
        { internal int Entries; public override void OnEnter() { Entries++; Finish(); } }
        private static object Get(object owner, string field) => owner.GetType().GetField(field, Members).GetValue(owner);
        private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Members).SetValue(owner, value);
        private static object Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Members).Invoke(owner, args);
        private static void Require(bool value, string why) { if (!value) throw new InvalidOperationException(why); }

        internal static void Run(Action<string, Action> check)
        {
            Require(File.Exists(Path.Combine(BepInEx.Paths.GameRootPath, "wintermp-sleep-sandbox.txt")),
                "Sleep checks require a marked isolated game copy.");
            var session = SessionManager.Instance!;
            var manager = SleepConsentManager.Instance!;
            var prompt = SleepConsentPrompt.Instance!;
            Require(session.State == SessionState.Idle && session.PlayerCount == 0, "Sleep probe requires idle isolated session.");
            var root = new GameObject("Sleep audit"); root.SetActive(false);
            var sleep = new GameObject("Sleep"); sleep.transform.parent = root.transform;
            var trigger = new GameObject("SleepTrigger"); trigger.transform.parent = sleep.transform;
            var fsm = trigger.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
            Set(fsm, "fsm", new Fsm()); fsm.Fsm.Name = "Activate"; fsm.Fsm.StartState = "State 1";
            fsm.Fsm.States = new[] {
                State(fsm, "State 1"), State(fsm, "Conditions?", "STOP", "State 3", "FINISHED", "Wait click"),
                State(fsm, "Wait click", "ACTIVATE", "Confirm"),
                State(fsm, "Confirm", "FINISHED", "State 3", "ACTIVATE", "Get positions"),
                State(fsm, "Get positions", "FINISHED", "AnimateSleep"), State(fsm, "AnimateSleep"),
                State(fsm, "Calc rates"), State(fsm, "State 3") };
            // Actual build 23268598: STOP is local to Conditions?; ABORT globally
            // enters Calc rates (which applies wake-up needs), not cancellation.
            fsm.Fsm.Events = new[] { FsmEvent.GetFsmEvent("STOP"), FsmEvent.GetFsmEvent("ABORT"), FsmEvent.GetFsmEvent("ACTIVATE") };
            fsm.Fsm.GlobalTransitions = new[] { Edge("ABORT", "Calc rates") };
            var getPositions = new Count { Enabled = true };
            var wake = new Count { Enabled = true };
            var players = (IDictionary)Get(session, "_playersByPeer");
            Action<byte> add = id => players.Add(new PeerId(id), new RemotePlayer { PlayerId = id, Peer = new PeerId(id), Name = "Guest " + id });
            Action<bool> role = host => {
                typeof(SessionManager).GetProperty("IsHost", Members).SetValue(session, host, null);
                typeof(SessionManager).GetProperty("State", Members).SetValue(session, SessionState.Connected, null);
            };
            Action reset = () => {
                Call(manager, "CancelWaiting"); players.Clear(); role(true); add(1); Enter(fsm, "State 1");
                getPositions.Entries = 0; wake.Entries = 0;
            };
            Func<byte> request = () => (byte)Get(manager, "_activeRequestId");
            Action<byte, bool> answer = (id, accepted) => manager.OnRemoteResponse(new SleepConsentResponse {
                RequestId = request(), PlayerId = id, Accepted = accepted });
            Action begin = () => { Enter(fsm, "Confirm"); fsm.SendEvent("ACTIVATE"); };
            try
            {
                root.SetActive(true); fsm.enabled = true; if (!fsm.Fsm.Started) fsm.Fsm.Start();
                foreach (var state in fsm.Fsm.States) state.Actions = new FsmStateAction[] { new Hold { Enabled = true } };
                Find(fsm, "Get positions").Actions = new FsmStateAction[] { getPositions, new Hold { Enabled = true } };
                Find(fsm, "Calc rates").Actions = new FsmStateAction[] { wake, new Hold { Enabled = true } };
                var hookType = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Sync.PlayerSleepHook", true);
                var hook = Activator.CreateInstance(hookType, true);
                Call(hook, "TryHookSleepActivate", fsm);

                check("sleep: pending consent prevents native movement setup", () => {
                    reset(); begin();
                    Require((bool)Get(manager, "_waitingForGuests") && getPositions.Entries == 0,
                        "Native Get positions ran before consent: " + getPositions.Entries);
                });
                check("sleep: decline exits without native sleep or wake side effects", () => {
                    reset(); begin(); answer(1, false);
                    Require(!(bool)Get(manager, "_waitingForGuests") && fsm.ActiveStateName == "State 3"
                        && getPositions.Entries == 0 && wake.Entries == 0, "Decline left state=" + fsm.ActiveStateName);
                });
                check("sleep: timeout exits without a second round", () => {
                    reset(); begin(); Set(manager, "_consentDeadlineAt", 0f); Call(manager, "Update");
                    Require(!(bool)Get(manager, "_waitingForGuests") && fsm.ActiveStateName == "State 3" && wake.Entries == 0,
                        "Timeout left state=" + fsm.ActiveStateName);
                });
                check("sleep: host cancellation retires pending answers", () => {
                    reset(); begin(); Enter(fsm, "State 3"); Call(manager, "Update"); answer(1, true);
                    Require(!(bool)Get(manager, "_waitingForGuests") && !(bool)Get(manager, "_consentGranted")
                        && getPositions.Entries == 0, "Late acceptance revived cancelled sleep.");
                });
                check("sleep: the final guest leaving releases the waiting host", () => {
                    reset(); begin(); players.Clear(); Call(manager, "Update");
                    Require(!(bool)Get(manager, "_waitingForGuests") && fsm.ActiveStateName == "State 3",
                        "Last guest departure left state=" + fsm.ActiveStateName);
                });
                check("sleep: remaining approvals settle when another guest leaves", () => {
                    reset(); add(2); begin(); answer(1, true); players.Remove(new PeerId(2)); Call(manager, "Update");
                    Require(!(bool)Get(manager, "_waitingForGuests") && getPositions.Entries == 1,
                        "Accepted remaining guest still waiting, entries=" + getPositions.Entries);
                });
                check("sleep: any decline settles without waiting for silent guests", () => {
                    reset(); add(2); begin(); answer(1, false);
                    Require(!(bool)Get(manager, "_waitingForGuests") && fsm.ActiveStateName == "State 3",
                        "Decline waited for unrelated responses.");
                });
                check("sleep: late joiners cannot veto a request they never received", () => {
                    reset(); begin(); add(2); answer(2, false); answer(1, true);
                    Require(!(bool)Get(manager, "_waitingForGuests") && getPositions.Entries == 1,
                        "Unrequested answer affected the sleep round.");
                });
                check("sleep: unanimous consent enters native setup exactly once", () => {
                    reset(); begin(); answer(1, true); answer(1, true);
                    Require(!(bool)Get(manager, "_waitingForGuests") && getPositions.Entries == 1,
                        "Accepted sleep failed or repeated, entries=" + getPositions.Entries);
                });
                check("sleep: first answer is final within its request", () => {
                    reset(); add(2); begin(); answer(1, true); answer(1, false); answer(2, true);
                    Require(getPositions.Entries == 1 && !(bool)Get(manager, "_waitingForGuests"),
                        "Duplicate response rewrote an accepted answer.");
                });
                check("sleep: declined attempt can be retried without resetting the manager", () => {
                    reset(); begin(); byte old = request(); answer(1, false); begin();
                    manager.OnRemoteResponse(new SleepConsentResponse { RequestId = old, PlayerId = 1, Accepted = true });
                    Require(request() != old && (bool)Get(manager, "_waitingForGuests"), "Old round answer affected retry.");
                    answer(1, true); Enter(fsm, "Calc rates");
                    Require(getPositions.Entries == 1 && !(bool)Get(manager, "_awaitingPostSleepSync")
                        && !(bool)Get(manager, "_consentGranted"), "Retry or post-sleep completion stayed latched.");
                });
                check("sleep: solo host keeps the vanilla sleep path", () => {
                    reset(); players.Clear(); begin();
                    Require(getPositions.Entries == 1 && !(bool)Get(manager, "_waitingForGuests"), "Solo sleep was gated.");
                });
                check("sleep: guest attempts cannot reach native setup", () => {
                    reset(); role(false); begin();
                    Require(getPositions.Entries == 0 && fsm.ActiveStateName == "State 3", "Guest entered sleep.");
                });
                check("sleep: leaving a session dismisses its unanswered prompt", () => {
                    reset(); role(false); prompt.ShowRequest(new SleepConsentRequest { RequestId = 71 });
                    Require(prompt.IsBlockingInput, "Guest prompt did not open.");
                    typeof(SessionManager).GetProperty("State", Members).SetValue(session, SessionState.Idle, null);
                    Call(manager, "Update");
                    Require(!prompt.IsBlockingInput, "Disconnected guest retained a sleep prompt.");
                });
            }
            finally
            {
                Call(manager, "CancelWaiting"); players.Clear(); prompt.DismissRequest(71);
                typeof(SessionManager).GetProperty("IsHost", Members).SetValue(session, false, null);
                typeof(SessionManager).GetProperty("State", Members).SetValue(session, SessionState.Idle, null);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static FsmTransition Edge(string evt, string state) => new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent(evt), ToState = state };
        private static FsmState State(PlayMakerFSM fsm, string name, params string[] edges)
        {
            var transitions = new FsmTransition[edges.Length / 2];
            for (int i = 0; i < edges.Length; i += 2) transitions[i / 2] = Edge(edges[i], edges[i + 1]);
            return new FsmState(fsm.Fsm) { Name = name, Actions = new FsmStateAction[0], Transitions = transitions };
        }
        private static FsmState Find(PlayMakerFSM fsm, string name)
        { foreach (var state in fsm.Fsm.States) if (state.Name == name) return state; throw new InvalidOperationException(name); }
        private static void Enter(PlayMakerFSM fsm, string state)
        {
            var hook = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Sync.FsmHook", true);
            hook.GetMethod("EnsureRemoteEntry").Invoke(null, new object[] { fsm, state });
            hook.GetMethod("FireRemoteEntry").Invoke(null, new object[] { fsm, state });
        }
    }
}
