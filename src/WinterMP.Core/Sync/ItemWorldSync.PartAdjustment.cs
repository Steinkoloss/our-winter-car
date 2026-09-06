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
        private void OnHostPartAdjustment(PartFitRequest request, byte actor, SessionManager session)
        {
            ReplacementBinding? part = null;
            bool applying = _bridge.ApplyingRemote;
            try
            {
                _replacementParts.TryGetValue(request.ItemId, out part);
                var rotation = part == null || part.Replica || part.Factory.Failed ? null : GetPartHandRotation(part);
                var state = BuildReplacementPartState(request.ItemId);
                var mount = part == null ? null : FindRemovalMount(part);
                bool available = rotation != null && part != null && FitFsmReady(part.Data);
                bool near = available && GuestNearPackage(session, actor, rotation!.Hand.transform.position);
                int bolt = rotation?.Bolt.FsmVariables.FindFsmInt("BoltTightness")?.Value ?? -1;
                bool loose = rotation != null && FitFsmReady(rotation.Bolt) && bolt >= 0 && bolt < BoltStatePolicy.MaximumTightness
                    && rotation.Hand.enabled && rotation.Pick != null && rotation.Pick.enabled;
                string active = rotation == null ? string.Empty : rotation.Hand.ActiveStateName;
                bool busy = rotation == null || !FitFsmReady(rotation.Hand) || Time.unscaledTime < rotation.NextRequestAt
                    || (active != "Wait Player" && active != "Input" && active != "Wait 2");
                var status = PartAdjustmentPolicy.Check(request, state, rotation?.ScalarIndex ?? -1, available, near,
                    available && NativeRotationMountReady(part!, rotation!, mount), loose, busy);
                var receipt = _partFitLedger.Begin(request, actor, status);
                if (receipt == null) return;
                if (status == PartFitStatus.Pending)
                {
                    PartAdjustmentPolicy.TryRotation(rotation!.Scalar.Value, request.Operation, out float expected);
                    rotation.NextRequestAt = Time.unscaledTime + .1f;
                    _bridge.ApplyingRemote = true;
                    // The native turn writes both the saved part and the engine
                    // mount before its timed Wait. A retry only replays the receipt.
                    FsmHook.FireRemoteEntry(rotation.Hand, request.Operation == PartFitOperation.RotateIncrease ? "Clockwise" : "Counterwise");
                    string scalar = part!.Factory.Rule.HandRotation!.Scalar;
                    RequireFit(Math.Abs(rotation.Scalar.Value - expected) < .001f
                        && Math.Abs(mount!.FsmVariables.FindFsmFloat(scalar).Value - expected) < .001f
                        && Quaternion.Angle(rotation.Hand.transform.localRotation, Quaternion.Euler(0, expected, 0)) < .01f
                        && NativeRotationMountReady(part, rotation, mount));
                    receipt = _partFitLedger.Complete(request, true)!;
                }
                SendPartFitReceipt(session, receipt);
                SyncEventLog.Record("part-adjust", actor + ":" + request.Sequence + " " + request.ItemId.ToString("X8")
                    + " " + request.Operation + " " + receipt.Status);
            }
            catch (Exception e)
            {
                if (part != null) part.HandRotationFailed = true;
                WinterMPPlugin.Log.LogWarning("WorldSync: hand adjustment disabled: " + e.Message);
                SyncEventLog.Record("part-adjust-disabled", request.ItemId.ToString("X8") + " " + e.Message);
                var receipt = _partFitLedger.Complete(request, false) ?? _partFitLedger.Begin(request, actor, PartFitStatus.Failed);
                if (receipt != null) SendPartFitReceipt(session, receipt);
            }
            finally { _bridge.ApplyingRemote = applying; }
        }

        private static bool NativeRotationMountReady(ReplacementBinding part, PartHandRotation rotation, PlayMakerFSM? mount)
        {
            var c = SyncCatalog.ReplacementParts!;
            if (mount == null || !FitFsmReady(mount) || part.Body != null || part.Data.transform.parent != mount.transform
                || NativePartIdentity.Phase(part.Data) != NativePartPhase.Fitted
                || rotation.Hand.FsmVariables.FindFsmGameObject("ThisPart")?.Value != part.Data.gameObject
                || rotation.Bolt.FsmVariables.FindFsmGameObject("ThisPart")?.Value != part.Data.gameObject
                || rotation.Hand.FsmVariables.FindFsmGameObject("VINP")?.Value != mount.gameObject
                || mount.FsmVariables.FindFsmGameObject(c["mountPartVariable"])?.Value != part.Data.gameObject
                || mount.FsmVariables.FindFsmGameObject(c["mountPointVariable"])?.Value != mount.gameObject
                || mount.FsmVariables.FindFsmBool(c["mountInstalledVariable"])?.Value != true) return false;
            var scalar = mount.FsmVariables.FindFsmFloat(part.Factory.Rule.HandRotation!.Scalar);
            return scalar != null && Math.Abs(scalar.Value - rotation.Scalar.Value) < .001f
                && Quaternion.Angle(rotation.Hand.transform.localRotation, Quaternion.Euler(0, rotation.Scalar.Value, 0)) < .01f;
        }

        private bool DrawPartAdjustmentPrompt(SessionManager session)
        {
            var camera = Camera.main;
            if (camera == null || !PartInteractionHandFree()
                || FsmVariables.GlobalVariables.FindFsmFloat("ToolWrenchSize")?.Value != 0) return false;
            var ray = camera.ScreenPointToRay(Input.mousePosition);
            float nearest = 1f;
            if (Physics.Raycast(ray, out var hit, nearest, 1 << 19)) nearest = hit.distance;
            ReplacementBinding? selected = null;
            ReplacementPartState? selectedState = null;
            uint selectedId = 0;
            foreach (var pair in _replacementParts)
            {
                var part = pair.Value;
                if (!part.Replica || !part.FittedPresentation || part.Data == null || !part.Data.gameObject.activeInHierarchy
                    || part.Factory.Failed) continue;
                var state = _replacementReplica?.Get(pair.Key);
                if (state == null || !PartAttachmentPolicy.HasAttachment(state)) continue;
                var box = part.RemovalCollider;
                if (state.RemovalAllowed && box != null)
                {
                    var origin = box.transform.InverseTransformPoint(ray.origin);
                    var direction = box.transform.InverseTransformPoint(ray.origin + ray.direction) - origin;
                    if (PartRemovalPolicy.RayBox(origin.ToNet(), direction.ToNet(), box.center.ToNet(), box.size.ToNet(), nearest, out float obstruction))
                    { nearest = obstruction; selected = null; selectedState = null; }
                }
                var rotation = part.HandRotation;
                if (part.HandRotationFailed || rotation == null || rotation.Pick == null
                    || !rotation.Pick.gameObject.activeInHierarchy) continue;
                var sphere = rotation.Pick; var scale = sphere.transform.lossyScale;
                float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                if (!PartAdjustmentPolicy.RaySphere(ray.origin.ToNet(), ray.direction.ToNet(), sphere.transform.TransformPoint(sphere.center).ToNet(),
                    radius, nearest, out float distance)) continue;
                nearest = distance; selected = part; selectedState = state; selectedId = pair.Key;
            }
            if (selected == null || selectedState == null || !_bridge.ReplacementBoltLoose(selected.HandRotation!.Bolt)) return false;
            GUI.Box(new Rect(Screen.width / 2f - 150f, Screen.height / 2f + 55f, 300f, 28f), "Scroll to adjust the alternator");
            var input = Event.current;
            if (input.type == EventType.ScrollWheel && input.delta.y != 0 && _partFitInputFrame != Time.frameCount)
            {
                _partFitInputFrame = Time.frameCount;
                var rotation = selected.HandRotation!;
                var operation = input.delta.y < 0 ? PartFitOperation.RotateIncrease : PartFitOperation.RotateDecrease;
                if (Time.unscaledTime >= rotation.NextRequestAt
                    && PartAdjustmentPolicy.TryRotation(selectedState.Scalars[rotation.ScalarIndex], operation, out _))
                {
                    rotation.NextRequestAt = Time.unscaledTime + .1f;
                    EnsurePartFitClient();
                    if (_partFitClient!.TryBegin(session.LocalPlayerId, selectedId, selectedState.Revision, operation)) ProcessPartFitting(session);
                }
                input.Use();
            }
            // Keep the existing right-click removal interaction when both picks overlap.
            return input.type != EventType.MouseDown || input.button != 1;
        }
    }
}
