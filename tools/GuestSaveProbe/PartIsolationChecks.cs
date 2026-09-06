using System;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    internal static class PartIsolationChecks
    {
        private sealed class CountEntry : FsmStateAction
        {
            internal int Count;
            public override void OnEnter() { Count++; Finish(); }
        }

        internal static void Run(Action<string, Action> check)
        {
            Type type = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Sync.GuestPartIsolation", true);
            var parent = new GameObject("probe original mount");
            var storage = new GameObject("probe inactive storage"); storage.SetActive(false);
            var part = new GameObject("probe saved part"); part.SetActive(false);
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = new Vector3(1, 2, 3);
            part.transform.localRotation = Quaternion.Euler(0, 30, 0);
            part.transform.localScale = new Vector3(2, 3, 4);
            var body = part.AddComponent<Rigidbody>(); body.useGravity = false;
            part.AddComponent<BoxCollider>();
            var fsm = part.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
            typeof(PlayMakerFSM).GetField("fsm", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(fsm, new Fsm());
            var enter = new CountEntry(); var settled = new CountEntry();
            fsm.Fsm.States = new[] {
                new FsmState(fsm.Fsm) { Name = "Start", Actions = new FsmStateAction[] { enter },
                    Transitions = new[] { new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("PROBE_SETTLE"), ToState = "Settled" } } },
                new FsmState(fsm.Fsm) { Name = "Settled", Actions = new FsmStateAction[] { settled } } };
            fsm.Fsm.StartState = "Start"; fsm.Fsm.RestartOnEnable = true;
            // Awake/Init hydrates serialized action data and replaces runtime
            // arrays. Inject counters after that step, as production hooks do.
            part.SetActive(true);
            fsm.Fsm.States[0].Actions = new FsmStateAction[] { enter };
            fsm.Fsm.States[1].Actions = new FsmStateAction[] { settled };
            fsm.enabled = true;
            if (!fsm.Fsm.Started) fsm.Fsm.Start();
            fsm.SendEvent("PROBE_SETTLE");
            int entered = enter.Count, settledCount = settled.Count;
            body.velocity = new Vector3(2, 0, 1); body.angularVelocity = new Vector3(0, .5f, 0);
            Vector3 velocity = body.velocity, angular = body.angularVelocity;
            Quaternion rotation = part.transform.localRotation;
            object? saved = null;
            try
            {
                check("part isolation: live PlayMaker fixture", () =>
                {
                    if (!fsm.Fsm.Started || fsm.ActiveStateName != "Settled" || entered < 1 || settledCount < 1)
                        throw new InvalidOperationException("Fixture state=" + fsm.ActiveStateName + " started=" + fsm.Fsm.Started
                            + " start entries=" + entered + " settled entries=" + settledCount);
                });
                check("part isolation: physics and FSMs are parked", () =>
                {
                    saved = Activator.CreateInstance(type, BindingFlags.NonPublic | BindingFlags.Instance, null,
                        new object[] { part, storage.transform }, null);
                    Require(saved != null && part.transform.parent == storage.transform && !part.activeInHierarchy
                        && !fsm.enabled && body.isKinematic && !body.detectCollisions);
                });
                check("part isolation: external activation cannot unpark a saved part", () =>
                {
                    part.SetActive(true);
                    Require(!part.activeInHierarchy && !fsm.enabled && enter.Count == entered);
                });
                check("part isolation: restore follows the original mount without replaying FSM entry", () =>
                {
                    parent.transform.position = new Vector3(8, 9, 10);
                    Restore(type, saved);
                    Require(part.activeSelf && part.activeInHierarchy && part.transform.parent == parent.transform
                        && part.transform.localPosition == new Vector3(1, 2, 3)
                        && part.transform.localScale == new Vector3(2, 3, 4)
                        && Quaternion.Angle(rotation, part.transform.localRotation) < .01f
                        && fsm.enabled && fsm.Fsm.RestartOnEnable && fsm.ActiveStateName == "Settled"
                        && enter.Count == entered && settled.Count == settledCount);
                    Require(!body.isKinematic && body.detectCollisions && body.velocity == velocity && body.angularVelocity == angular);
                });
                check("part isolation: repeated restore is inert", () =>
                {
                    Restore(type, saved);
                    Require(enter.Count == entered && settled.Count == settledCount && fsm.ActiveStateName == "Settled");
                });
                check("part isolation: a second session preserves the same original", () =>
                {
                    saved = Activator.CreateInstance(type, BindingFlags.NonPublic | BindingFlags.Instance, null,
                        new object[] { part, storage.transform }, null);
                    Restore(type, saved);
                    Require(part.transform.parent == parent.transform && part.activeInHierarchy && enter.Count == entered);
                });
                check("part isolation: vanished mount does not resurrect an orphan", () =>
                {
                    saved = Activator.CreateInstance(type, BindingFlags.NonPublic | BindingFlags.Instance, null,
                        new object[] { part, storage.transform }, null);
                    UnityEngine.Object.DestroyImmediate(parent);
                    Restore(type, saved);
                    Require(!part.activeInHierarchy && part.transform.parent == storage.transform);
                });
            }
            finally
            {
                if (part != null) UnityEngine.Object.DestroyImmediate(part);
                if (parent != null) UnityEngine.Object.DestroyImmediate(parent);
                UnityEngine.Object.DestroyImmediate(storage);
            }
        }

        private static void Restore(Type type, object? saved)
        {
            Require(saved != null);
            type.GetMethod("Restore", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(saved, null);
        }

        private static void Require(bool success)
        {
            if (!success) throw new InvalidOperationException("Native part isolation assertion failed.");
        }
    }
}
