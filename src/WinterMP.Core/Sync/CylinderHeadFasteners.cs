using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    /// <summary>Native array-slot binding and reversible guest tool display for the saved head.</summary>
    internal sealed class CylinderHeadFasteners
    {
        private sealed class Saved
        {
            internal FsmState[] States = null!;
            internal FsmTransition[] Transitions = null!;
            internal string Start = "";
            internal int Index, Tightness;
            internal float Size, Position;
            internal GameObject? Visual;
            internal Vector3 Pose;
            internal Quaternion Rotation;
            internal bool Pick;
        }
        internal readonly SyncedBolt[] Bolts = new SyncedBolt[CylinderHeadPolicy.BoltCount];
        private readonly Saved[] _saved = new Saved[CylinderHeadPolicy.BoltCount];
        private readonly GameObject _group;
        private readonly bool _groupActive;
        private bool _guest, _attached;
        private Func<byte, PartFitOperation, bool>? _turn;
        internal float NextTurnAt;

        internal static bool Matches(PlayMakerFSM fsm)
        {
            var c = SyncCatalog.CylinderHead;
            if (c == null || fsm == null || fsm.FsmName != "Screw") return false;
            var data = NativePartIdentity.FindData(fsm.transform);
            int slash = c.BoltPath.LastIndexOf('/');
            return data != null && data.FsmVariables.FindFsmString("ID")?.Value == c.NativeId
                && slash > 0 && fsm.gameObject.name == c.BoltPath.Substring(slash + 1)
                && ScenePath.RelativeTo(fsm.transform.parent, data.transform) == c.BoltPath.Substring(0, slash);
        }

        internal CylinderHeadFasteners(PlayMakerFSM data)
        {
            _group = data.FsmVariables.FindFsmGameObject("Bolts")?.Value ?? throw new InvalidOperationException("Missing head bolt group.");
            if (ScenePath.RelativeTo(_group.transform, data.transform) != "Bolts") throw new InvalidOperationException("Head bolt group moved.");
            _groupActive = _group.activeSelf;
            var fsms = _group.GetComponentsInChildren<PlayMakerFSM>(true);
            if (fsms.Length != Bolts.Length) throw new InvalidOperationException("Head needs ten native fasteners.");
            foreach (var fsm in fsms)
            {
                if (!Matches(fsm)) throw new InvalidOperationException("Unexpected head fastener.");
                if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
                var saved = new Saved { States = fsm.Fsm.States, Transitions = fsm.Fsm.GlobalTransitions, Start = fsm.Fsm.StartState,
                    Index = fsm.FsmVariables.FindFsmInt("Index").Value, Tightness = fsm.FsmVariables.FindFsmInt("BoltTightness").Value,
                    Size = fsm.FsmVariables.FindFsmFloat("Boltsize").Value, Position = fsm.FsmVariables.FindFsmFloat("TightnessF").Value,
                    Visual = fsm.FsmVariables.FindFsmGameObject("ThisBolt").Value };
                var bolt = new SyncedBolt { Fsm = fsm, Path = ScenePath.Of(fsm.transform) };
                FsmWorldSync.BindNativeBolt(bolt);
                var pick = fsm.FsmVariables.FindFsmObject("Collider")?.Value as SphereCollider;
                saved.Pick = pick != null && pick.enabled;
                try { FsmWorldSync.BindReplicaBoltVisual(bolt); }
                finally { if (pick != null) pick.enabled = saved.Pick; }
                int index = bolt.IndexVar.Value;
                var values = Array(bolt);
                if (bolt.PartData != data || !bolt.UpdatesPart || bolt.AdjustsTimingAtLimit || values == null
                    || values.Count != Bolts.Length || index < 0 || index >= Bolts.Length || Bolts[index] != null)
                    throw new InvalidOperationException("Head fastener slot/array changed.");
                saved.Pose = bolt.ReplicaVisual!.localPosition; saved.Rotation = bolt.ReplicaVisual.localRotation;
                Bolts[index] = bolt; _saved[index] = saved;
            }
        }
        private static IList? Array(SyncedBolt bolt) => bolt.ArrayProperty.GetValue(bolt.ArrayProxy, null) as IList;

        internal bool Capture(CylinderHeadState state)
        {
            if (_guest) return false;
            var values = Array(Bolts[0]);
            if (values == null || values.Count != Bolts.Length) return false;
            for (int i = 0; i < Bolts.Length; i++)
            {
                var b = Bolts[i];
                if (b.Fsm == null || b.PartData == null || b.IndexVar.Value != i
                    || b.Fsm.FsmVariables.FindFsmGameObject("ThisPart")?.Value != b.PartData.gameObject
                    || !(values[i] is int value) || value < 0 || value > BoltStatePolicy.MaximumTightness) return false;
                state.Fasteners[i] = (byte)value;
            }
            state.Tightness = Bolts[0].PartTightnessVar.Value; state.FastenersAvailable = true;
            return CylinderHeadPolicy.Valid(state);
        }

        internal bool Ready(byte slot)
        {
            if (_guest || slot < 1 || slot > Bolts.Length) return false;
            var b = Bolts[slot - 1];
            return b.Fsm != null && b.Fsm.enabled && b.Fsm.gameObject.activeInHierarchy && b.Fsm.Fsm.Started
                && (b.Fsm.ActiveStateName == "Set pos" || b.Fsm.ActiveStateName == "On" || b.Fsm.ActiveStateName == "Off");
        }
        internal void Turn(byte slot, PartFitOperation operation, CylinderHeadState before)
        {
            if (!Ready(slot)) throw new InvalidOperationException("Head fastener is not ready.");
            var b = Bolts[slot - 1]; int direction = operation == PartFitOperation.ToolTighten ? 1 : -1;
            b.BoltTightnessVar!.Value = before.Fasteners[slot - 1];
            b.Fsm.SendEvent(direction > 0 ? "TIGHTEN" : "UNTIGHTEN");
            var after = CylinderHeadPolicy.Copy(before);
            if (!Capture(after) || after.Fasteners[slot - 1] != before.Fasteners[slot - 1] + direction
                || after.Tightness != before.Tightness + direction)
                throw new InvalidOperationException("Native head tightening did not settle.");
        }

        internal void PrepareGuest(Func<byte, PartFitOperation, bool> turn)
        {
            _guest = true; _turn = turn;
            for (int i = 0; i < Bolts.Length; i++)
            {
                byte slot = (byte)(i + 1); var fsm = Bolts[i].Fsm;
                fsm.Fsm.States = new[] { State(fsm, "Set pos", Tick),
                    State(fsm, "Tight?", () => GuestTurn(slot, PartFitOperation.ToolTighten)),
                    State(fsm, "Loose?", () => GuestTurn(slot, PartFitOperation.ToolLoosen)),
                    State(fsm, "On", Tick), State(fsm, "Off", Tick) };
                var transitions = new List<FsmTransition>();
                foreach (var pair in new[] { new[] { "TIGHTEN", "Tight?" }, new[] { "UNTIGHTEN", "Loose?" },
                    new[] { "REPAIRMODE_ON", "On" }, new[] { "REPAIRMODE_OFF", "Off" } })
                    transitions.Add(new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent(pair[0]), ToState = pair[1] });
                fsm.Fsm.GlobalTransitions = transitions.ToArray(); fsm.Fsm.StartState = "Set pos";
                if (!FsmHook.EnsureRemoteEntry(fsm, "Set pos")) throw new InvalidOperationException("Cannot prepare head bolt view.");
            }
        }
        private static FsmState State(PlayMakerFSM fsm, string name, Action callback) => new FsmState(fsm.Fsm) {
            Name = name, Actions = new FsmStateAction[] { new FsmHookAction(callback) }, Transitions = new FsmTransition[0] };
        private void GuestTurn(byte slot, PartFitOperation operation)
        {
            try { Tick(); if (_attached && Bolts[slot - 1].ReplicaCollider!.enabled) _turn?.Invoke(slot, operation); }
            finally { FsmHook.FireRemoteEntry(Bolts[slot - 1].Fsm, "Set pos"); }
        }
        internal void Apply(CylinderHeadState state)
        {
            _attached = state.ParentId != 0 && state.FastenersAvailable;
            foreach (var b in Bolts) b.Fsm.enabled = _attached;
            _group.SetActive(_attached);
            if (_attached)
                for (int i = 0; i < Bolts.Length; i++)
                {
                    var b = Bolts[i];
                    if (!b.Fsm.Fsm.Started) b.Fsm.Fsm.Start();
                    b.BoltTightnessVar!.Value = state.Fasteners[i]; b.TightnessFVar!.Value = state.Fasteners[i] / b.PositionDivisor;
                    var pose = b.ReplicaVisual!.localPosition; pose.z = b.TightnessFVar.Value; b.ReplicaVisual.localPosition = pose;
                    FsmHook.FireRemoteEntry(b.Fsm, "Set pos");
                }
            Tick();
        }
        internal void Tick()
        {
            if (!_guest) return;
            bool repair = FsmVariables.GlobalVariables.FindFsmBool(SyncCatalog.ReplacementParts!["replicaRepairVariable"])?.Value == true;
            foreach (var b in Bolts) if (b.ReplicaCollider != null)
                b.ReplicaCollider.enabled = _attached && repair && b.Fsm != null && b.Fsm.enabled && b.Fsm.gameObject.activeInHierarchy;
        }
        internal void Hold() { _attached = false; Tick(); }
        internal void Restore()
        {
            if (!_guest) return;
            Hold(); _turn = null;
            for (int i = 0; i < Bolts.Length; i++)
            {
                var b = Bolts[i]; var s = _saved[i]; if (b.Fsm == null) continue;
                b.Fsm.enabled = false; b.Fsm.Fsm.States = s.States; b.Fsm.Fsm.GlobalTransitions = s.Transitions; b.Fsm.Fsm.StartState = s.Start;
                b.IndexVar.Value = s.Index; b.BoltTightnessVar!.Value = s.Tightness; b.TightnessFVar!.Value = s.Position;
                b.Fsm.FsmVariables.FindFsmFloat("Boltsize").Value = s.Size; b.Fsm.FsmVariables.FindFsmGameObject("ThisBolt").Value = s.Visual;
                if (b.ReplicaVisual != null) { b.ReplicaVisual.localPosition = s.Pose; b.ReplicaVisual.localRotation = s.Rotation; }
            }
            _group.SetActive(_groupActive); _guest = false;
        }
    }
}
