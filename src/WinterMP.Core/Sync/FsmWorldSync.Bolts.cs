using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FsmWorldSync
    {
        private sealed class HostBoltReport
        {
            public float ExpiresAt;
            public BoltState? Request;
        }
        private readonly Dictionary<uint, HostBoltReport> _hostBoltReports = new Dictionary<uint, HostBoltReport>();
        private readonly PartTightnessReceipts _partTightnessReceipts = new PartTightnessReceipts();
        private ulong _partReceiptOrder;

        private static bool IsDerivedPartState(string? state) => state == "Bolted" || state == "Unbolted" || state == "Stop";

        private float ResolvePartTightness(PlayMakerFSM data, ulong order, float value) =>
            NativePartIdentity.IsData(data) && _bridge.PartIdentities.TryRootId(data, out uint partId)
                ? _partTightnessReceipts.Resolve(partId, order, value) : value;

        private static T? BoltField<T>(FsmStateAction action, string name) where T : class =>
            action.GetType().GetField(name)?.GetValue(action) as T;

        private static FsmStateAction[] BoltActions(PlayMakerFSM fsm, string name, params string[][] layouts)
        {
            var actions = FsmHook.FindState(fsm, name)?.Actions;
            if (actions != null)
                foreach (var layout in layouts)
                {
                    if (actions.Length != layout.Length) continue;
                    bool match = true;
                    for (int i = 0; i < layout.Length; i++)
                        if (!actions[i].Enabled || actions[i].GetType().Name != layout[i]) { match = false; break; }
                    if (match) return actions;
                }
            throw new InvalidOperationException("Native bolt actions changed: " + name);
        }

        private static bool BoltTransition(PlayMakerFSM fsm, string state, string ev, string to)
        {
            foreach (var transition in FsmHook.FindState(fsm, state)!.Transitions)
                if (transition.EventName == ev && transition.ToState == to) return true;
            return false;
        }

        private static void BindNativeBolt(SyncedBolt bolt)
        {
            var fsm = bolt.Fsm; var vars = fsm.FsmVariables;
            var screw = BoltActions(fsm, "Screw", new[] { "IntAdd", "ArrayListSet", "AddFsmFloat", "ConvertIntToFloat", "SendEventByName" },
                new[] { "IntAdd", "ArrayListSet", "ConvertIntToFloat" });
            bolt.UpdatesPart = screw.Length == 5;
            bolt.BoltTightnessVar = vars.FindFsmInt("BoltTightness") ?? throw new InvalidOperationException("Missing bolt tightness.");
            bolt.TightnessFVar = vars.FindFsmFloat("TightnessF") ?? throw new InvalidOperationException("Missing bolt pose.");
            bolt.IndexVar = vars.FindFsmInt("Index") ?? throw new InvalidOperationException("Missing native bolt index.");
            var part = vars.FindFsmGameObject("ThisPart")?.Value ?? throw new InvalidOperationException("Missing bolt part.");
            bolt.PartData = NativePartIdentity.FindData(part.transform) ?? throw new InvalidOperationException("Missing native bolt Data.");
            if (bolt.PartData.gameObject != part || ScenePath.RelativeTo(fsm.transform, part.transform) == null)
                throw new InvalidOperationException("Bolt belongs to another part.");
            bolt.PartTightnessVar = bolt.PartData.FsmVariables.FindFsmFloat("Tightness")
                ?? throw new InvalidOperationException("Missing part tightness.");
            if (BoltField<FsmInt>(screw[0], "intVariable")?.Name != "BoltTightness"
                || BoltField<FsmInt>(screw[0], "add")?.Name != "ScrewInt"
                || BoltField<FsmOwnerDefault>(screw[1], "gameObject")?.GameObject.Name != "ThisPart"
                || BoltField<FsmInt>(screw[1], "atIndex")?.Name != "Index")
                throw new InvalidOperationException("Native bolt array target changed.");
            // ArrayMaker wraps the written FsmInt in FsmVar rather than exposing it directly.
            var value = screw[1].GetType().GetField("variable")?.GetValue(screw[1]);
            if (value == null || value.GetType().GetField("variableName")?.GetValue(value) as string != "BoltTightness")
                throw new InvalidOperationException("Native bolt array value changed.");
            var reference = BoltField<FsmString>(screw[1], "reference");
            if (reference == null || reference.UseVariable || string.IsNullOrEmpty(reference.Value))
                throw new InvalidOperationException("Missing native bolt array reference.");
            foreach (var component in part.GetComponents<MonoBehaviour>())
            {
                if (component == null || component.GetType().Name != "PlayMakerArrayListProxy"
                    || component.GetType().GetField("referenceName")?.GetValue(component) as string != reference.Value) continue;
                if (bolt.ArrayProxy != null) throw new InvalidOperationException("Duplicate native bolt array.");
                bolt.ArrayProxy = component;
                bolt.ArrayProperty = component.GetType().GetProperty("arrayList")
                    ?? throw new InvalidOperationException("Missing native bolt array storage.");
            }
            if (bolt.ArrayProxy == null) throw new InvalidOperationException("Missing native bolt array.");
            bolt.AdjustsTimingAtLimit = ValidateBoltTurn(fsm, "Tight?", 1, BoltStatePolicy.MaximumTightness, bolt.UpdatesPart);
            ValidateBoltTurn(fsm, "Loose?", -1, 0, bolt.UpdatesPart);
            int conversion = bolt.UpdatesPart ? 3 : 2;
            if (BoltField<FsmInt>(screw[conversion], "intVariable")?.Name != "BoltTightness"
                || BoltField<FsmFloat>(screw[conversion], "floatVariable")?.Name != "TightnessF"
                || !BoltTransition(fsm, "Screw", "FINISHED", "Calc pos")
                || !BoltTransition(fsm, "Calc pos", "FINISHED", "Set pos"))
                throw new InvalidOperationException("Native bolt pose conversion changed.");
            if (bolt.UpdatesPart)
            {
                var target = BoltField<FsmEventTarget>(screw[4], "eventTarget");
                if (BoltField<FsmOwnerDefault>(screw[2], "gameObject")?.GameObject.Name != "ThisPart"
                    || BoltField<FsmString>(screw[2], "fsmName")?.Value != "Data"
                    || BoltField<FsmString>(screw[2], "variableName")?.Value != "Tightness"
                    || BoltField<FsmFloat>(screw[2], "addValue")?.Name != "ScrewFloat"
                    || target == null || target.gameObject.GameObject.Name != "ThisPart" || target.fsmName.Value != "Data"
                    || BoltField<FsmString>(screw[4], "sendEvent")?.Value != "BOLTING")
                    throw new InvalidOperationException("Native bolt part update changed.");
            }
            var calc = BoltActions(fsm, "Calc pos", new[] { "SetRandomRotation", "FloatDivide" }, new[] { "SetRandomRotation" });
            if (calc.Length == 2)
            {
                var divisor = BoltField<FsmFloat>(calc[1], "divideBy");
                if (BoltField<FsmFloat>(calc[1], "floatVariable")?.Name != "TightnessF"
                    || divisor == null || divisor.UseVariable || !IsFinite(divisor.Value) || divisor.Value == 0)
                    throw new InvalidOperationException("Native bolt pose scale changed.");
                bolt.PositionDivisor = divisor.Value;
            }
            var pose = BoltActions(fsm, "Set pos", new[] { "SetPosition" }, new[] { "SetPosition", "IntCompare" }, new string[0]);
            if (pose.Length > 0 && (BoltField<FsmOwnerDefault>(pose[0], "gameObject")?.GameObject.Name != "ThisBolt"
                || BoltField<FsmFloat>(pose[0], "z")?.Name != "TightnessF"))
                throw new InvalidOperationException("Native bolt visual target changed.");
            if (!FsmHook.EnsureRemoteEntry(fsm, "Set pos")) throw new InvalidOperationException("Missing bolt pose state.");
        }

        private static bool ValidateBoltTurn(PlayMakerFSM fsm, string state, int direction, int limit, bool changesPart)
        {
            var a = BoltActions(fsm, state, changesPart ? new[] { "SetIntValue", "SetFloatValue", "IntCompare" }
                : new[] { "SetIntValue", "IntCompare" });
            var step = BoltField<FsmInt>(a[0], "intValue"); var compare = a[a.Length - 1];
            var bound = BoltField<FsmInt>(compare, "integer2");
            if (BoltField<FsmInt>(a[0], "intVariable")?.Name != "ScrewInt" || step == null || step.UseVariable || step.Value != direction
                || BoltField<FsmInt>(compare, "integer1")?.Name != "BoltTightness" || bound == null || bound.UseVariable || bound.Value != limit
                || BoltField<FsmEvent>(compare, "equal")?.Name != "BACK"
                || BoltField<FsmEvent>(compare, direction > 0 ? "greaterThan" : "lessThan")?.Name != "BACK"
                || !BoltTransition(fsm, state, "FINISHED", "Screw"))
                throw new InvalidOperationException("Native bolt turn/bounds changed.");
            if (changesPart)
            {
                var amount = BoltField<FsmFloat>(a[1], "floatValue");
                if (BoltField<FsmFloat>(a[1], "floatVariable")?.Name != "ScrewFloat"
                    || amount == null || amount.UseVariable || amount.Value != direction)
                    throw new InvalidOperationException("Native bolt aggregate step changed.");
            }
            if (BoltTransition(fsm, state, "BACK", "Set pos")) return false;
            if (direction != 1 || !BoltTransition(fsm, state, "BACK", "State 1"))
                throw new InvalidOperationException("Native bolt limit transition changed.");

            // Crank pulley/camshaft sprocket use an extra turn to adjust the mount.
            // Ordinary bolt state cannot represent that separate engine timing value.
            var adjust = BoltActions(fsm, "State 1", new[] { "GetFsmGameObject", "SendEventByName" });
            var target = BoltField<FsmEventTarget>(adjust[1], "eventTarget");
            var delay = BoltField<FsmFloat>(adjust[1], "delay");
            if (BoltField<FsmOwnerDefault>(adjust[0], "gameObject")?.GameObject.Name != "ThisPart"
                || BoltField<FsmString>(adjust[0], "fsmName")?.Value != "Data"
                || BoltField<FsmString>(adjust[0], "variableName")?.Value != "InstallPoint"
                || BoltField<FsmGameObject>(adjust[0], "storeValue")?.Name != "VINP"
                || target == null || target.gameObject.GameObject.Name != "VINP" || target.fsmName.Value != "Data"
                || BoltField<FsmString>(adjust[1], "sendEvent")?.Value != "ADJUST"
                || delay == null || delay.UseVariable || delay.Value != 0
                || !BoltTransition(fsm, "State 1", "FINISHED", "Set pos"))
                throw new InvalidOperationException("Native bolt timing adjustment changed.");
            return true;
        }

        private static bool IsBoltTimingAdjustment(SyncedBolt bolt, string eventName) =>
            bolt.AdjustsTimingAtLimit && eventName == "TIGHTEN" && bolt.BoltTightnessVar!.Value >= BoltStatePolicy.MaximumTightness;

        private static IList? BoltArray(SyncedBolt bolt) => bolt.ArrayProxy == null ? null : bolt.ArrayProperty.GetValue(bolt.ArrayProxy, null) as IList;

        private static bool BoltReady(SyncedBolt bolt) => !bolt.Failed && bolt.Fsm != null && bolt.PartData != null
            && (bolt.ReplicaGate == null || bolt.ReplicaGate.Attached)
            && bolt.Fsm.Fsm.Initialized && bolt.Fsm.Fsm.Started && bolt.Fsm.enabled && bolt.Fsm.gameObject.activeInHierarchy
            && bolt.PartData.Fsm.Started && bolt.PartData.enabled && bolt.PartData.gameObject.activeInHierarchy
            && NativePartIdentity.Phase(bolt.PartData) == NativePartPhase.Fitted
            && bolt.Fsm.ActiveStateName != "Init" && bolt.Fsm.ActiveStateName != "Calc pos"
            && bolt.Fsm.ActiveStateName != "Screw" && bolt.Fsm.ActiveStateName != "Tight?" && bolt.Fsm.ActiveStateName != "Loose?";

        private static bool IsGuestNearBolt(SessionManager? session, byte playerId, Vector3 target)
        {
            if (session == null || !session.IsHost) return false;
            float now = Time.unscaledTime;
            foreach (var player in session.Players)
                if (player.PlayerId == playerId && !player.IsDead && player.LastTransformTime > 0
                    && now >= player.LastTransformTime && now - player.LastTransformTime <= GuestInteractionPoseMaxAgeSeconds)
                    return (player.Position - target).sqrMagnitude <= 9f;
            return false;
        }

        internal BoltState? BuildBoltState(uint id)
        {
            if (!_bolts.TryGetValue(id, out var bolt) || !BoltReady(bolt) || bolt.ReplicaGate?.Seeded == false) return null;
            try
            {
                var values = BoltArray(bolt); int index = bolt.IndexVar.Value;
                if (values == null || index < 0 || index >= values.Count || !(values[index] is int tightness)
                    || bolt.BoltTightnessVar == null || tightness != bolt.BoltTightnessVar.Value) return null;
                return BoltStatePolicy.Capture(id, tightness, bolt.PartTightnessVar.Value);
            }
            catch (Exception e) { FailBolt(bolt, e); return null; }
        }

        private static void FailBolt(SyncedBolt bolt, Exception error)
        {
            if (bolt.Failed) return;
            bolt.Failed = true;
            if (bolt.ReplicaGate != null)
            {
                if (bolt.ReplicaCollider != null) bolt.ReplicaCollider.enabled = false;
                if (bolt.Fsm != null) bolt.Fsm.enabled = false;
            }
            WinterMPPlugin.Log.LogWarning("WorldSync: bolt disabled at " + bolt.Path + ": " + error.Message);
            SyncEventLog.Record("bolt-disabled", bolt.Path + " " + error.Message);
        }

        private void QueueHostBoltReport(uint id, BoltState? request = null) => _hostBoltReports[id] = new HostBoltReport {
            ExpiresAt = Time.unscaledTime + 2f,
            Request = request == null ? null : BoltStatePolicy.Capture(request.NetId, request.BoltTightness, request.PartTightness) };

        private void ProcessHostBoltReports(SessionManager session)
        {
            foreach (uint id in new List<uint>(_hostBoltReports.Keys))
            {
                var state = BuildBoltState(id);
                var pending = _hostBoltReports[id];
                if (state != null)
                {
                    var reply = pending.Request == null ? state : BoltStatePolicy.HostReply(pending.Request, state);
                    if (reply != null) session.SendWorldMessage(reply, Channel.ReliableOrdered);
                    _hostBoltReports.Remove(id);
                }
                else if (Time.unscaledTime >= pending.ExpiresAt) _hostBoltReports.Remove(id);
            }
        }

        private bool ApplyBoltState(uint id, ushort tightness, ushort screwInt, float partTightness, ulong receiptOrder)
        {
            var state = new BoltState { NetId = id, BoltTightness = tightness, ScrewInt = screwInt, PartTightness = partTightness };
            if (!BoltStatePolicy.Valid(state)) return true;
            if (!_bolts.TryGetValue(id, out var bolt) || !BoltReady(bolt)) return false;
            if (bolt.ReplicaGate != null && !bolt.ReplicaGate.CanReceive(receiptOrder)) return true;
            bool applying = _bridge.ApplyingRemote;
            try
            {
                var values = BoltArray(bolt); int index = bolt.IndexVar.Value;
                if (values == null || index < 0 || index >= values.Count || !(values[index] is int previous)) return false;
                partTightness = ResolvePartTightness(bolt.PartData, receiptOrder, partTightness);
                bool changed = previous != tightness || bolt.BoltTightnessVar!.Value != tightness
                    || bolt.PartTightnessVar.Value != partTightness || bolt.TightnessFVar!.Value != tightness / bolt.PositionDivisor;
                if (!changed && bolt.ReplicaGate == null) return true;
                if (!BoltStatePolicy.ApplyArray(state, values, index, bolt.PositionDivisor, out float position)) return false;
                if (_bridge.PartIdentities.TryRootId(bolt.PartData, out uint partId)
                    && PartIdentity.TryFsmId(partId, string.Empty, bolt.PartData.FsmName, out uint dataId))
                {
                    // This later host result supersedes older part data parked while
                    // the same graph was inactive; replaying it would undo reconciliation.
                    _pending.RemoveAll(p => p.NetId == dataId && !p.IsRawEvent
                        && (p.Name == "Bolted" || p.Name == "Unbolted" || p.Name == "Stop"));
                }
                _bridge.ApplyingRemote = true;
                bolt.BoltTightnessVar!.Value = tightness;
                bolt.TightnessFVar!.Value = position;
                bolt.PartTightnessVar.Value = partTightness;
                bolt.ReplicaGate?.Received(receiptOrder);
                FsmHook.FireRemoteEntry(bolt.Fsm, "Set pos");
                if (bolt.ReplicaGate == null && bolt.UpdatesPart && NativePartIdentity.Phase(bolt.PartData) == NativePartPhase.Fitted)
                    bolt.PartData.SendEvent("BOLTING");
                SyncEventLog.Record("bolt-state", id.ToString("X8") + " " + tightness + " total=" + partTightness);
                return true;
            }
            catch (Exception e) { FailBolt(bolt, e); return true; }
            finally { _bridge.ApplyingRemote = applying; }
        }
    }
}
