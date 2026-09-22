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
        private sealed class PartDistributorTiming
        {
            internal PlayMakerFSM Hand = null!;
            internal SphereCollider Pick = null!;
            internal Transform Mesh = null!;
            internal FsmFloat Scalar = null!, Tightness = null!;
            internal int ScalarIndex;
            internal float NextRequestAt;
        }

        private static PartDistributorTiming? GetPartDistributorTiming(ReplacementBinding part)
        {
            var rule = part.Factory.Rule.DistributorTiming;
            if (rule == null || part.DistributorTimingFailed || part.Data == null) return null;
            if (part.DistributorTiming != null) return part.DistributorTiming;
            try
            {
                var hand = FindPartAdjustmentFsm(part.Data.transform, rule.Fsm);
                var mesh = ScenePath.FindRelative(part.Data.transform, rule.MeshPath);
                var pick = part.Data.GetComponent<SphereCollider>();
                var scalar = part.Data.FsmVariables.FindFsmFloat(rule.Scalar);
                var tightness = part.Data.FsmVariables.FindFsmFloat(rule.TightnessVariable);
                RequireFit(mesh != null && mesh != part.Data.transform && pick != null && pick.radius > 0
                    && pick.isTrigger && part.Data.gameObject.layer == 19 && scalar != null && tightness != null
                    && hand.FsmVariables.FindFsmGameObject(rule.MeshVariable)?.Value == mesh!.gameObject);
                ValidatePartDistributorTimingGraph(hand, part.Data, rule);
                var control = new PartDistributorTiming { Hand = hand, Mesh = mesh!, Pick = pick!,
                    Scalar = scalar!, Tightness = tightness!, ScalarIndex = Array.IndexOf(part.Factory.Rule.Scalars, rule.Scalar) };
                RequireFit(control.ScalarIndex >= 0);
                if (!part.Replica)
                    RequireFit(FsmHook.EnsureRemoteEntry(hand, rule.ClockwiseState)
                        && FsmHook.EnsureRemoteEntry(hand, rule.CounterwiseState));
                part.DistributorTiming = control;
                return control;
            }
            catch (Exception e) { DisablePartDistributorTiming(part, e.Message); return null; }
        }

        private static void DisablePartDistributorTiming(ReplacementBinding part, string reason)
        {
            if (part.DistributorTimingFailed) return;
            part.DistributorTimingFailed = true;
            WinterMPPlugin.Log.LogWarning("WorldSync: distributor timing disabled for " + part.NativeId + ": " + reason);
            SyncEventLog.Record("part-timing-disabled", part.NativeId + " " + reason);
        }

        private void OnHostPartDistributorTiming(PartFitRequest request, byte actor, SessionManager session)
        {
            ReplacementBinding? part = null;
            bool applying = _bridge.ApplyingRemote;
            try
            {
                _replacementParts.TryGetValue(request.ItemId, out part);
                var control = part == null || part.Replica || part.Factory.Failed ? null : GetPartDistributorTiming(part);
                var state = BuildReplacementPartState(request.ItemId);
                var mount = part == null ? null : FindRemovalMount(part);
                var rule = part?.Factory.Rule.DistributorTiming;
                bool available = control != null && part != null && FitFsmReady(part.Data);
                bool near = available && GuestNearPackage(session, actor, part!.Data.transform.position);
                bool ready = available && NativeDistributorMountReady(part!, control!, mount);
                bool loose = control != null && PartAdjustmentPolicy.IsDistributorLoose(control.Tightness.Value);
                bool busy = control == null || rule == null || !FitFsmReady(control.Hand)
                    || !control.Pick.enabled || !control.Pick.isTrigger || Time.unscaledTime < control.NextRequestAt
                    || Array.IndexOf(rule.ReadyStates, control.Hand.ActiveStateName) < 0;
                var status = PartAdjustmentPolicy.Check(PartAdjustmentProfile.Distributor, request, state,
                    control?.ScalarIndex ?? -1, available, near, ready, loose, busy);
                var receipt = _partFitLedger.Begin(request, actor, status);
                if (receipt == null) return;
                if (status == PartFitStatus.Pending)
                {
                    control!.NextRequestAt = Time.unscaledTime + rule!.Cooldown;
                    _bridge.ApplyingRemote = true;
                    ExecutePartDistributorTiming(control, part!.Data, mount!, rule, request.Operation);
                    RequireFit(NativeDistributorMountReady(part, control, mount));
                    receipt = _partFitLedger.Complete(request, true)!;
                }
                SendPartFitReceipt(session, receipt);
                SyncEventLog.Record("part-timing", actor + ":" + request.Sequence + " " + request.ItemId.ToString("X8")
                    + " " + request.Operation + " " + receipt.Status);
            }
            catch (Exception e)
            {
                if (part != null) DisablePartDistributorTiming(part, e.Message);
                var receipt = _partFitLedger.Complete(request, false) ?? _partFitLedger.Begin(request, actor, PartFitStatus.Failed);
                if (receipt != null) SendPartFitReceipt(session, receipt);
            }
            finally { _bridge.ApplyingRemote = applying; }
        }

        private static float ExecutePartDistributorTiming(PartDistributorTiming control, PlayMakerFSM data,
            PlayMakerFSM mount, PartDistributorTimingData rule, PartFitOperation operation)
        {
            var mesh = control.Mesh;
            if (mesh == null) throw new InvalidOperationException("Timing mesh is unavailable.");
            RequireFit(control.Hand.gameObject == data.gameObject
                && ScenePath.RelativeTo(mesh, data.transform) == rule.MeshPath
                && control.Hand.FsmVariables.FindFsmGameObject(rule.MeshVariable)?.Value == mesh.gameObject
                && control.Hand.FsmVariables.FindFsmGameObject(rule.MountVariable)?.Value == mount.gameObject
                && ReferenceEquals(control.Scalar, data.FsmVariables.FindFsmFloat(rule.Scalar))
                && ReferenceEquals(control.Tightness, data.FsmVariables.FindFsmFloat(rule.TightnessVariable)));
            float current = control.Scalar.Value;
            RequireFit(PartAdjustmentPolicy.TryRotation(PartAdjustmentProfile.Distributor, current, operation, out float expected)
                && PartAdjustmentPolicy.IsDistributorLoose(control.Tightness.Value));
            float tightness = control.Tightness.Value;
            string entry = operation == PartFitOperation.RotateIncrease ? rule.ClockwiseState : rule.CounterwiseState;
            RequireFit(FsmHook.EnsureRemoteEntry(control.Hand, entry));
            // Native GetRotation uses the mesh as its scratch input. Seed from
            // saved SparkAngle so an old pose cannot silently change ignition timing.
            mesh.localRotation = Quaternion.Euler(0, 0, current);
            FsmHook.FireRemoteEntry(control.Hand, entry);
            RequireFit(control.Hand.ActiveStateName == rule.WaitState
                && Math.Abs(control.Scalar.Value - expected) < .001f
                && Math.Abs(mount.FsmVariables.FindFsmFloat(rule.Scalar).Value - expected) < .001f
                && Quaternion.Angle(mesh.localRotation, Quaternion.Euler(0, 0, expected)) < .01f
                && control.Tightness.Value == tightness);
            return control.Scalar.Value;
        }

        private static bool NativeDistributorMountReady(ReplacementBinding part, PartDistributorTiming control, PlayMakerFSM? mount)
        {
            var c = SyncCatalog.ReplacementParts!;
            var rule = part.Factory.Rule.DistributorTiming!;
            if (mount == null || !FitFsmReady(mount) || part.Body != null
                || part.Data.transform.parent != mount.transform || control.Hand.gameObject != part.Data.gameObject
                || control.Mesh == null || ScenePath.RelativeTo(control.Mesh, part.Data.transform) != rule.MeshPath
                || control.Hand.FsmVariables.FindFsmGameObject(rule.MeshVariable)?.Value != control.Mesh.gameObject
                || NativePartIdentity.Phase(part.Data) != NativePartPhase.Fitted
                || control.Hand.FsmVariables.FindFsmGameObject(rule.MountVariable)?.Value != mount.gameObject
                || mount.FsmVariables.FindFsmGameObject(c["mountPartVariable"])?.Value != part.Data.gameObject
                || mount.FsmVariables.FindFsmGameObject(c["mountPointVariable"])?.Value != mount.gameObject
                || mount.FsmVariables.FindFsmBool(c["mountInstalledVariable"])?.Value != true
                || !PartAdjustmentPolicy.ValidRotation(PartAdjustmentProfile.Distributor, control.Scalar.Value)) return false;
            var scalar = mount.FsmVariables.FindFsmFloat(rule.Scalar);
            var tightness = mount.FsmVariables.FindFsmFloat(rule.TightnessVariable);
            return scalar != null && Math.Abs(scalar.Value - control.Scalar.Value) < .001f
                && tightness != null && tightness.Value == control.Tightness.Value;
        }

        private static void ApplyPartDistributorTimingPose(ReplacementBinding part)
        {
            if (!part.Replica || !part.FittedPresentation || part.DistributorTimingFailed) return;
            var control = GetPartDistributorTiming(part);
            if (control == null) return;
            if (control.Mesh == null || ScenePath.RelativeTo(control.Mesh, part.Data.transform) != part.Factory.Rule.DistributorTiming!.MeshPath)
            { DisablePartDistributorTiming(part, "Timing mesh left its owned part."); return; }
            if (!PartAdjustmentPolicy.ValidRotation(PartAdjustmentProfile.Distributor, control.Scalar.Value))
            { DisablePartDistributorTiming(part, "Invalid authoritative SparkAngle."); return; }
            // The authored Pivot has its own -10 degree offset. Only its child
            // mesh turns; the attachment pose and saved guest mounts stay intact.
            control.Mesh.localRotation = Quaternion.Euler(0, 0, control.Scalar.Value);
        }

        private static bool TryDistributorAdjustmentPick(PartDistributorTiming control, BoxCollider? removal,
            Ray ray, float range, out float distance, out bool controlHit)
        {
            distance = 0; controlHit = false;
            var sphere = control.Pick;
            if (sphere == null || !sphere.gameObject.activeInHierarchy) return false;
            var scale = sphere.transform.lossyScale;
            float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            controlHit = PartAdjustmentPolicy.RaySphere(ray.origin.ToNet(), ray.direction.ToNet(),
                sphere.transform.TransformPoint(sphere.center).ToNet(), radius, range, out float sphereHit);
            bool blocked = false; float boxHit = 0;
            if (removal != null)
            {
                var origin = removal.transform.InverseTransformPoint(ray.origin);
                var direction = removal.transform.InverseTransformPoint(ray.origin + ray.direction) - origin;
                blocked = PartRemovalPolicy.RayBox(origin.ToNet(), direction.ToNet(), removal.center.ToNet(),
                    removal.size.ToNet(), range, out boxHit);
            }
            if (!controlHit && !blocked) return false;
            // Its box can surround its own timing pick, but must still hide other
            // controls behind this part. Use that visible surface for ordering.
            distance = controlHit ? (blocked ? Mathf.Min(sphereHit, boxHit) : sphereHit) : boxHit;
            return true;
        }
    }
}
