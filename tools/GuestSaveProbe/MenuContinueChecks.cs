using System;
using System.Reflection;
using BepInEx.Logging;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.FastBoot;

namespace WinterMP.GuestSaveProbe
{
    internal static class MenuContinueChecks
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
        private sealed class Bounce : FsmStateAction
        { public override void OnEnter() { Fsm.Event("OFF"); Finish(); } }
        private sealed class CountEntry : FsmStateAction
        { internal int Entries; public override void OnEnter() { Entries++; Finish(); } }

        internal static void Run(Action<string, Action> check)
        {
            var root = new GameObject("Menu Continue probe"); root.SetActive(false);
            var log = new ManualLogSource("Menu Continue probe");
            try
            {
                var fsm = root.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
                typeof(PlayMakerFSM).GetField("fsm", Members).SetValue(fsm, new Fsm());
                fsm.Fsm.Name = "SetSize"; fsm.Fsm.StartState = "Mouse";
                var input = (FsmStateAction)Activator.CreateInstance(typeof(PlayMakerFSM).Assembly
                    .GetType("HutongGames.PlayMaker.Actions.MousePickEvent")
                    ?? Assembly.Load("Assembly-CSharp").GetType("HutongGames.PlayMaker.Actions.MousePickEvent", true));
                input.Reset(); input.Enabled = true;
                var bounce = new Bounce { Enabled = true }; var accepted = new CountEntry { Enabled = true };
                var mouse = State(fsm, "Mouse", new FsmStateAction[0], "OVER", "Action");
                var hover = State(fsm, "Action", new[] { input, bounce }, "OFF", "Mouse", "DOWN", "Accepted");
                var done = State(fsm, "Accepted", new FsmStateAction[] { accepted });
                fsm.Fsm.States = new[] { mouse, hover, done };
                var menu = new MenuContinue();
                typeof(MenuContinue).GetField("_button", Members).SetValue(menu, root);
                typeof(MenuContinue).GetField("_fsm", Members).SetValue(menu, fsm);
                check("menu Continue: waits for the native button to be active and initialized", () =>
                    Require(!menu.TryAdvance(log, false) && menu.ClickedAt < 0f && !menu.IsFinished));
                root.SetActive(true); fsm.enabled = true; if (!fsm.Fsm.Started) fsm.Fsm.Start();
                // Native Init reloads serialized ActionData, replacing the arrays
                // assigned before Awake. Install the fixture actions after it.
                hover.Actions = new[] { input, bounce }; done.Actions = new FsmStateAction[] { accepted };
                Func<string> status = () => "state=" + fsm.ActiveStateName + ", init=" + fsm.Fsm.Initialized
                    + ", started=" + fsm.Fsm.Started + ", clicks=" + accepted.Entries + ", finished=" + menu.IsFinished;
                check("menu Continue: cancelled gesture restores input and remains retryable", () =>
                {
                    Require(!menu.TryAdvance(log, false) && !menu.IsFinished && menu.ClickedAt < 0f, status());
                    Require(input.Enabled && bounce.Enabled && accepted.Entries == 0 && fsm.ActiveStateName == "Mouse", status());
                });
                bounce.Enabled = false;
                check("menu Continue: synthetic click reaches the native DOWN destination", () =>
                    Require(menu.TryAdvance(log, false) && menu.IsFinished && menu.ClickedAt >= 0f && accepted.Entries == 1, status()));
                check("menu Continue: input enabled flags are restored without enabling other actions", () =>
                    Require(input.Enabled && !bounce.Enabled));
                check("menu Continue: successful click is never repeated", () =>
                    Require(!menu.TryAdvance(log, false) && accepted.Entries == 1));
                menu.Reset();
                typeof(MenuContinue).GetField("_button", Members).SetValue(menu, root);
                typeof(MenuContinue).GetField("_fsm", Members).SetValue(menu, fsm);
                check("menu Continue: unrelated current states cannot be driven into the loading sequence", () =>
                    Require(!menu.TryAdvance(log, false) && !menu.IsFinished && menu.ClickedAt < 0f && accepted.Entries == 1));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); log.Dispose(); }
        }

        private static FsmState State(PlayMakerFSM owner, string name, FsmStateAction[] actions, params string[] edges)
        {
            var transitions = new FsmTransition[edges.Length / 2];
            for (int i = 0; i < edges.Length; i += 2)
                transitions[i / 2] = new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent(edges[i]), ToState = edges[i + 1] };
            return new FsmState(owner.Fsm) { Name = name, Actions = actions, Transitions = transitions };
        }
        private static void Require(bool value, string detail = "")
        { if (!value) throw new InvalidOperationException("Native Continue invariant failed. " + detail); }
    }
}
