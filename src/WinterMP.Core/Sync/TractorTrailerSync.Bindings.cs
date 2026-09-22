using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class TractorTrailerSync
    {
        private void Locate(SessionManager session, ItemWorldSync items)
        {
            SyncCatalog.EnsureLoaded();
            var c = SyncCatalog.TractorTrailer ?? throw new InvalidOperationException("Missing trailer catalog.");
            var found = new Dictionary<string, PlayMakerFSM>();
            foreach (var obj in ScenePath.ScanFsms())
            {
                var f = obj as PlayMakerFSM; if (f == null) continue;
                string p = ScenePath.Of(f.transform), key = "";
                if (p == c["hook"] && f.FsmName == c["hookFsm"]) key = "hook";
                if (p == c["trailer"] && f.FsmName == c["detachFsm"]) key = "detach";
                if (p == c["remove"] && f.FsmName == c["useFsm"]) key = "remove";
                if (p == c["hydraulics"] && f.FsmName == c["useFsm"]) key = "hydraulics";
                if (key.Length > 0) { if (found.ContainsKey(key)) throw new InvalidOperationException("Ambiguous trailer FSM " + key); found.Add(key, f); }
            }
            if (found.Count != 4) return;
            var hook = found["hook"]; var detach = found["detach"]; var remove = found["remove"]; var hydraulics = found["hydraulics"];
            if (!hook.Fsm.Started || !detach.Fsm.Started || (hook.ActiveStateName != c["attachedState"]
                && hook.ActiveStateName != c["waitingState"] && hook.ActiveStateName != c["feelState"])) return;
            SyncedItem? tractor = null;
            foreach (var item in items.Items.Values) if (item.IsVehicle && item.Path == c["tractor"]) tractor = item;
            if (tractor == null) return;
            foreach (var fsm in new[] { hook, detach, remove, hydraulics })
            {
                if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
                foreach (var state in fsm.Fsm.States) if (!state.IsInitialized) return;
            }
            var root = detach.GetComponent<Rigidbody>();
            var bed = root.transform.Find(c["bed"]).GetComponent<Rigidbody>();
            var support = root.transform.Find(c["support"]).GetComponent<Rigidbody>();
            var joint = root.GetComponent<CharacterJoint>();
            if (root == null || bed == null || support == null || joint == null || bed.GetComponent<HingeJoint>()?.connectedBody != root
                || support.GetComponent<FixedJoint>()?.connectedBody != root || root.GetComponentsInChildren<Rigidbody>(true).Length != 3)
                throw new InvalidOperationException("Changed native trailer rigidbody graph.");
            var attached = hook.FsmVariables.FindFsmBool(c["attached"]) ?? throw new InvalidOperationException("Missing native trailer flag.");
            if (joint.connectedBody != (attached.Value ? tractor.Body : support)) throw new InvalidOperationException("Unexpected native trailer connection.");
            Signature(detach, c["attachState"], "ActivateGameObject", "GetPosition", "SetPosition", "SetJointConnectedBody");
            Signature(detach, c["detachState"], "SetHingeJointProperties", "SetProperty", "SetIsKinematic", "SetPosition", "SetJointConnectedBody", "ActivateGameObject", "SetIsKinematic");
            Signature(remove, c["releaseState"], "SetBoolValue", "SetStringValue", "MasterAudioPlaySound", "EnableFSM", "EnableFSM", "SendEvent", "SendEvent", "SendEvent", "ActivateGameObject");
            foreach (string state in new[] { c["attachState"], c["detachState"] })
                if (!FsmHook.EnsureRemoteEntry(detach, state)) throw new InvalidOperationException("Missing trailer transition.");
            foreach (string state in new[] { c["releaseState"], c["idleState"] })
                if (!FsmHook.EnsureRemoteEntry(remove, state)) throw new InvalidOperationException("Missing trailer release transition.");
            _c = c; _hook = hook; _detach = detach; _remove = remove; _hydraulics = hydraulics; _attached = attached;
            _bodies = new[] { root, bed, support }; _tractor = tractor; _target = root.transform.Find(c["target"]); _joint = joint; _guest = !session.IsHost;
            var originalBodies = _bodies; var originalPoses = Bodies(); var kinematic = new bool[3];
            for (int i = 0; i < 3; i++) kinematic[i] = _bodies[i].isKinematic;
            _restore.Add(() => {
                for (int i = 0; i < 3; i++)
                {
                    var b = originalBodies[i]; var pose = originalPoses[i]; if (b == null) continue;
                    if (_guest) { b.position = pose.Position.ToUnity(); b.rotation = pose.Rotation.ToUnity(); }
                    b.isKinematic = kinematic[i];
                    if (_guest && !kinematic[i]) { b.velocity = pose.Velocity.ToUnity(); b.angularVelocity = pose.AngularVelocity.ToUnity(); }
                }
            });
            if (_guest)
            {
                bool oldAttached = attached.Value, oldRemove = remove.gameObject.activeSelf, oldHydraulics = hydraulics.enabled;
                var oldBody = joint.connectedBody; var oldAnchor = joint.connectedAnchor; bool auto = joint.autoConfigureConnectedAnchor;
                var log = root.transform.Find(c["log"]).gameObject; bool oldLog = log.activeSelf;
                var hinge = bed.GetComponent<HingeJoint>(); bool oldMotor = hinge.useMotor, oldSpring = hinge.useSpring; var motor = hinge.motor; var spring = hinge.spring;
                _restore.Add(() => {
                    if (joint != null) { joint.connectedBody = oldBody; joint.autoConfigureConnectedAnchor = false; joint.connectedAnchor = oldAnchor; joint.autoConfigureConnectedAnchor = auto; }
                    attached.Value = oldAttached;
                    if (remove != null) remove.gameObject.SetActive(oldRemove);
                    if (hydraulics != null) hydraulics.enabled = oldHydraulics;
                    if (log != null) log.SetActive(oldLog);
                    if (hinge != null) { hinge.motor = motor; hinge.spring = spring; hinge.useMotor = oldMotor; hinge.useSpring = oldSpring; }
                });
                if (!_hookPause.Suppress(hook) || !_detachPause.Suppress(detach)) throw new InvalidOperationException("Cannot pause local trailer connection.");
                var release = FsmHook.FindState(remove, c["releaseState"])!;
                if (!FsmHook.OnStateEnter(remove, c["releaseState"], () => {
                    if (_applying) return;
                    if (_remote != null && _remote.Attached)
                        session.SendWorldMessage(new TractorTrailerIntent { PlayerId = session.LocalPlayerId, Revision = _remote.Revision, Sequence = Next(ref _intentSequence) }, Channel.ReliableOrdered);
                    FsmHook.FireRemoteEntry(remove, c["idleState"]);
                }, out var action) || action == null) throw new InvalidOperationException("Cannot intercept guest trailer release.");
                _restore.Add(() => { var actions = new List<FsmStateAction>(release.Actions); actions.Remove(action); release.Actions = actions.ToArray(); });
                SetPhysics(false, Bodies());
            }
            else _localPhysics = true;
            WinterMPPlugin.Log.LogInfo("Tractor trailer sync: native connection and three-body assembly bound.");
        }

        private void PresentConnection(TractorTrailerState state)
        {
            _applying = true;
            try
            {
                // Place all bodies before native SetJointConnectedBody computes its frame.
                SetPhysics(false, state.Bodies); ApplyBodies(state.Bodies, 1);
                _detachPause.Restore();
                FsmHook.FireRemoteEntry(_detach!, _c![state.Attached ? "attachState" : "detachState"]);
                _detachPause.Suppress(_detach);
                _attached!.Value = state.Attached;
                _joint!.autoConfigureConnectedAnchor = false; _joint.connectedAnchor = state.ConnectedAnchor.ToUnity();
                _hydraulics!.enabled = state.Attached;
                _remove!.gameObject.SetActive(state.Attached);
                if (state.Attached) FsmHook.FireRemoteEntry(_remove, _c["idleState"]);
                ApplyBodies(state.Bodies, 1);
            }
            finally { _applying = false; }
        }

        private static void Signature(PlayMakerFSM fsm, string name, params string[] types)
        {
            var state = FsmHook.FindState(fsm, name) ?? throw new InvalidOperationException("Missing trailer state " + name);
            for (int i = 0; i < types.Length; i++)
                if (FsmHook.NativeAction(state, i)?.GetType().Name != types[i]) throw new InvalidOperationException("Changed trailer action " + name + "/" + i);
            if (FsmHook.NativeAction(state, types.Length) != null) throw new InvalidOperationException("Extra trailer action " + name);
        }
    }
}
