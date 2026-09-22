using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private void MaterializeSausage(SausageState state)
        {
            uint id = state.ItemId; var c = SyncCatalog.Sausages!;
            if (_sausagePrefab == null) return;
            if (_items.TryGetValue(id, out var old))
            { if (old.Body != null) throw new InvalidOperationException("Sausage replica identity collision."); RemoveTrackedItem(id, old.Body); }
            var templates = _sausagePrefab.GetComponents<PlayMakerFSM>();
            var enabled = new bool[templates.Length]; GameObject clone;
            try
            {
                for (int n = 0; n < templates.Length; n++) { enabled[n] = templates[n].enabled; templates[n].enabled = false; }
                clone = (GameObject)UnityEngine.Object.Instantiate(_sausagePrefab, state.Position.ToUnity(), state.Rotation.ToUnity());
            }
            finally { for (int n = 0; n < templates.Length; n++) templates[n].enabled = enabled[n]; }
            try
            {
                foreach (var fsm in clone.GetComponents<PlayMakerFSM>()) fsm.enabled = false;
                var use = SausageUse(clone, c["use"]) ?? throw new InvalidOperationException("Replica sausage Use missing.");
                if (!use.Fsm.Initialized) use.Fsm.Init(use);
                use.FsmVariables.FindFsmGameObject(c["owner"]).Value = clone;
                var wait = FsmHook.FindState(use, c["wait"])!;
                wait.Actions = new[] { wait.Actions[0], wait.Actions[1] };
                var transitions = new List<FsmTransition>();
                foreach (var t in wait.Transitions) if (t.ToState == c["button"]) transitions.Add(t);
                wait.Transitions = transitions.ToArray();
                // Only native eating remains local. Host food state supplies all
                // cooking/spoilage; neither guest fire contacts nor time can fork it.
                use.Fsm.States = new[] { wait, FsmHook.FindState(use, c["button"])!, FsmHook.FindState(use, c["eat"])!,
                    FsmHook.FindState(use, c["freshEat"])!, FsmHook.FindState(use, c["grilledEat"])!, FsmHook.FindState(use, c["destroy"])! };
                var globals = new List<FsmTransition>();
                foreach (var t in use.Fsm.GlobalTransitions) if (t.ToState == c["destroy"]) globals.Add(t);
                use.Fsm.GlobalTransitions = globals.ToArray(); use.Fsm.StartState = c["wait"]; use.Fsm.RestartOnEnable = true;
                var body = clone.GetComponent<Rigidbody>(); body.isKinematic = false;
                var b = new SausageBinding { Body = body, Use = use, Replica = true };
                _sausages[id] = b; _trackedBodies[body] = true;
                BindSpawnedBody(body, new ItemSpawn.Entry { NetId = id, TemplateName = _sausageNames[state.Kind], Position = state.Position, Rotation = state.Rotation }, 0, Time.unscaledTime);
                HookSausageRetirement(id, b);
                _pendingItemPoses.Remove(id); ApplySausageFood(b, state); clone.SetActive(true);
            }
            catch
            {
                foreach (var fsm in clone.GetComponents<PlayMakerFSM>()) fsm.enabled = false;
                RemoveTrackedItem(id, clone.GetComponent<Rigidbody>()); _sausages.Remove(id); UnityEngine.Object.Destroy(clone); throw;
            }
        }
        private void ApplySausageFood(SausageBinding b, SausageState state)
        {
            if (!b.Replica) throw new InvalidOperationException("Sausage state overlaps local food.");
            var c = SyncCatalog.Sausages!;
            b.Use.FsmVariables.FindFsmFloat(c["condition"]).Value = state.Condition;
            b.Use.FsmVariables.FindFsmBool(c["grilled"]).Value = state.Grilled;
            if (b.Received == null || b.Received.Kind != state.Kind)
            {
                b.Use.enabled = false; b.Body.name = _sausageNames[state.Kind];
                var fresh = b.Body.transform.Find(c["freshMesh"]); var grilled = b.Body.transform.Find(c["grilledMesh"]);
                // A spoiled grilled sausage retains its cooked mesh in vanilla.
                bool cookedMesh = state.Kind == 1 || state.Kind == 3 && state.Grilled;
                fresh.gameObject.SetActive(!cookedMesh); grilled.gameObject.SetActive(cookedMesh);
                fresh.GetComponent<Renderer>().sharedMaterial = state.Kind == 2 ? _sausageBurntMaterial : _sausageFreshMaterial;
                if (state.Kind < 2) { b.Use.enabled = true; if (!b.Use.Fsm.Started) b.Use.Fsm.Start(); }
            }
            b.Received = state;
        }
    }
}
