using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private sealed class PartHandScrew
        {
            public PlayMakerFSM Screw = null!;
            public BoxCollider Pick = null!;
            public FsmFloat Scalar = null!;
            public int ScalarIndex;
            public float NextRequestAt;
        }

        private static PartHandScrew? GetPartHandScrew(ReplacementBinding part)
        {
            var rule = part.Factory.Rule.HandScrew;
            if (rule == null || part.HandScrewFailed || part.Data == null) return null;
            if (part.HandScrew != null) return part.HandScrew;
            try
            {
                var screw = FindPartAdjustmentFsm(part.Data.transform, rule.Fsm);
                var pick = part.Data.GetComponent<BoxCollider>();
                var scalar = part.Data.FsmVariables.FindFsmFloat(rule.Scalar);
                RequireFit(pick != null && scalar != null && part.Data.gameObject.layer == 19
                    && pick.size.x > 0 && pick.size.y > 0 && pick.size.z > 0);
                ValidatePartHandScrewGraph(screw, part.Data, rule);
                var control = new PartHandScrew { Screw = screw, Pick = pick!, Scalar = scalar!,
                    ScalarIndex = Array.IndexOf(part.Factory.Rule.Scalars, rule.Scalar) };
                RequireFit(control.ScalarIndex == part.Factory.Rule.Identity.TightnessIndex);
                if (!part.Replica)
                    RequireFit(FsmHook.EnsureRemoteEntry(screw, rule.TightenState)
                        && FsmHook.EnsureRemoteEntry(screw, rule.LoosenState));
                part.HandScrew = control;
                return control;
            }
            catch (Exception e) { DisablePartHandScrew(part, e.Message); return null; }
        }

        private static void DisablePartHandScrew(ReplacementBinding part, string reason)
        {
            if (part.HandScrewFailed) return;
            part.HandScrewFailed = true;
            WinterMPPlugin.Log.LogWarning("WorldSync: hand tightening disabled for " + part.NativeId + ": " + reason);
            SyncEventLog.Record("part-hand-screw-disabled", part.NativeId + " " + reason);
        }

        private void OnHostPartHandScrew(PartFitRequest request, byte actor, SessionManager session)
        {
            ReplacementBinding? part = null;
            bool applying = _bridge.ApplyingRemote;
            try
            {
                _replacementParts.TryGetValue(request.ItemId, out part);
                var control = part == null || part.Replica || part.Factory.Failed ? null : GetPartHandScrew(part);
                var state = BuildReplacementPartState(request.ItemId);
                var mount = part == null ? null : FindRemovalMount(part);
                var rule = part?.Factory.Rule.HandScrew;
                bool available = control != null && part != null && FitFsmReady(part.Data);
                bool near = available && GuestNearPackage(session, actor, part!.Data.transform.position);
                bool ready = available && control!.Pick != null && control.Pick.enabled && control.Pick.isTrigger
                    && NativeHandScrewMountReady(part!, control, mount);
                bool busy = control == null || rule == null || !FitFsmReady(control.Screw)
                    || Time.unscaledTime < control.NextRequestAt
                    || Array.IndexOf(rule.ReadyStates, control.Screw.ActiveStateName) < 0;
                var status = PartHandScrewPolicy.Check(request, state, control?.ScalarIndex ?? -1,
                    available, near, ready, busy);
                var receipt = _partFitLedger.Begin(request, actor, status);
                if (receipt == null) return;
                if (status == PartFitStatus.Pending)
                {
                    control!.NextRequestAt = Time.unscaledTime + rule!.Cooldown;
                    _bridge.ApplyingRemote = true;
                    ExecutePartHandScrew(control.Screw, part!.Data, rule, request.Operation);
                    RequireFit(NativeHandScrewMountReady(part, control, mount));
                    receipt = _partFitLedger.Complete(request, true)!;
                }
                SendPartFitReceipt(session, receipt);
                SyncEventLog.Record("part-hand-screw", actor + ":" + request.Sequence + " " + request.ItemId.ToString("X8")
                    + " " + request.Operation + " " + receipt.Status);
            }
            catch (Exception e)
            {
                if (part != null) DisablePartHandScrew(part, e.Message);
                var receipt = _partFitLedger.Complete(request, false) ?? _partFitLedger.Begin(request, actor, PartFitStatus.Failed);
                if (receipt != null) SendPartFitReceipt(session, receipt);
            }
            finally { _bridge.ApplyingRemote = applying; }
        }

        private static float ExecutePartHandScrew(PlayMakerFSM screw, PlayMakerFSM data,
            PartHandScrewData rule, PartFitOperation operation)
        {
            float current = data.FsmVariables.FindFsmFloat(rule.Scalar).Value;
            if (!PartHandScrewPolicy.TryTurn(current, operation, out float expected))
                throw new InvalidOperationException("Native hand tightening value or limit is unavailable.");
            string entry = operation == PartFitOperation.HandTighten ? rule.TightenState : rule.LoosenState;
            RequireFit(FsmHook.EnsureRemoteEntry(screw, entry));
            // Native Set turns this scratch value into a negative position offset.
            // The normal input state refreshes it before the bound check; remote
            // entry must do the same or it can add a turn using stale pose data.
            screw.FsmVariables.FindFsmFloat(rule.ScratchVariable).Value = current;
            FsmHook.FireRemoteEntry(screw, entry);
            RequireFit(data.FsmVariables.FindFsmFloat(rule.Scalar).Value == expected
                && screw.ActiveStateName == rule.WaitState && HandScrewPoseMatches(data.transform, expected));
            return expected;
        }

        private static bool NativeHandScrewMountReady(ReplacementBinding part, PartHandScrew control, PlayMakerFSM? mount)
        {
            var c = SyncCatalog.ReplacementParts!;
            float tightness = control.Scalar.Value;
            return mount != null && FitFsmReady(mount) && part.Body == null
                && part.Data.transform.parent == mount.transform && control.Screw.gameObject == part.Data.gameObject
                && NativePartIdentity.Phase(part.Data) == NativePartPhase.Fitted && PartHandScrewPolicy.ValidTightness(tightness)
                && mount.FsmVariables.FindFsmGameObject(c["mountPartVariable"])?.Value == part.Data.gameObject
                && mount.FsmVariables.FindFsmGameObject(c["mountPointVariable"])?.Value == mount.gameObject
                && mount.FsmVariables.FindFsmBool(c["mountInstalledVariable"])?.Value == true
                && mount.FsmVariables.FindFsmFloat(part.Factory.Rule.HandScrew!.Scalar)?.Value == tightness
                && mount.FsmVariables.FindFsmBool("Bolted")?.Value == (tightness >= 1)
                && HandScrewPoseMatches(part.Data.transform, tightness);
        }

        private static bool HandScrewPoseMatches(Transform part, float tightness) =>
            Vector3.Distance(part.localPosition, new Vector3(0, 0, -tightness / 400f)) < .00001f
            && Quaternion.Angle(part.localRotation, Quaternion.Euler(0, 0, tightness * 20f)) < .01f;

        private static void ApplyPartHandScrewPose(ReplacementBinding part)
        {
            if (!part.Replica || !part.FittedPresentation || part.HandScrewFailed) return;
            var control = GetPartHandScrew(part);
            if (control == null) return;
            // Use the resolved parent receipt, which can be newer than a deferred
            // replacement snapshot. Guest native Screw and BOLTING stay disabled.
            float tightness = control.Scalar.Value;
            if (!PartHandScrewPolicy.ValidTightness(tightness))
            { DisablePartHandScrew(part, "Invalid authoritative tightness."); return; }
            part.Data.transform.localRotation = Quaternion.Euler(0, 0, tightness * 20f);
            part.Data.transform.localPosition = new Vector3(0, 0, -tightness / 400f);
        }
    }
}
