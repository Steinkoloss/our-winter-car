using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private float _nextParkingJointError;

        private bool MoveRemoteBody(SyncedItem item, Vector3 position, Quaternion rotation, bool resumePhysics = false)
        {
            var body = item.Body;
            FixedJoint? parking = null;
            var profile = SyncCatalog.ParkingJoint;
            // A kinematic observer does not solve this constraint. Rebase once
            // when physics resumes instead of rebuilding a joint every frame.
            if (item.IsVehicle && (resumePhysics || !body.isKinematic) && (profile == null || item.Path == profile.RootPath))
                foreach (var joint in body.GetComponents<FixedJoint>())
                    if (joint.connectedBody == null) { parking = joint; break; }
            if (parking == null)
            {
                body.transform.position = position; body.transform.rotation = rotation;
                return true;
            }
            try
            {
                if (profile == null || body.GetComponents<FixedJoint>().Length != 1 || ScenePath.Of(body.transform) != profile.RootPath)
                    throw new InvalidOperationException("Parking joint metadata or unique world anchor unavailable.");
                PlayMakerFSM? control = null;
                foreach (var fsm in body.GetComponents<PlayMakerFSM>())
                    if (fsm.FsmName == profile.Fsm)
                    { if (control != null) throw new InvalidOperationException("Ambiguous parking joint control."); control = fsm; }
                if (control == null || !control.Fsm.Initialized || !control.Fsm.Started)
                    throw new InvalidOperationException("Parking joint control is not ready.");
                var state = FsmHook.FindState(control, profile.ReleaseState);
                if (state == null || profile.ReleaseIndex >= state.Actions.Length) throw new InvalidOperationException("Parking joint release is missing.");
                var action = state.Actions[profile.ReleaseIndex];
                var target = PackageField<FsmOwnerDefault>(action, "gameObject");
                var component = PackageField<FsmString>(action, "component");
                if (!action.Enabled || action.GetType().FullName != "HutongGames.PlayMaker.Actions.DestroyComponent"
                    || target == null || target.OwnerOption != OwnerDefaultOption.UseOwner || component == null
                    || component.UseVariable || component.Value != "FixedJoint" || control.Fsm.GetOwnerDefaultTarget(target) != body.gameObject)
                    throw new InvalidOperationException("Native parking joint release changed.");

                var oldPosition = body.transform.position; var oldRotation = body.transform.rotation;
                var anchor = parking.anchor; var axis = parking.axis; var connected = parking.connectedAnchor;
                bool automatic = parking.autoConfigureConnectedAnchor, collision = parking.enableCollision, preprocessing = parking.enablePreprocessing;
                float force = parking.breakForce, torque = parking.breakTorque;
                var variables = new System.Collections.Generic.List<FsmObject>();
                foreach (var fsm in body.GetComponents<PlayMakerFSM>())
                    foreach (var variable in fsm.FsmVariables.ObjectVariables)
                        if (ReferenceEquals(variable.Value, parking)) variables.Add(variable);

                // Unity 5 caches a FixedJoint's world frame at creation. Moving its
                // body leaves that frame behind, so the solver pulls the car back.
                // Rebuild only the audited parking lock before the next physics step.
                UnityEngine.Object.DestroyImmediate(parking);
                body.transform.position = position; body.transform.rotation = rotation;
                var replacement = body.gameObject.AddComponent<FixedJoint>();
                replacement.axis = axis; replacement.anchor = anchor;
                replacement.autoConfigureConnectedAnchor = automatic;
                if (!automatic) replacement.connectedAnchor = position + rotation * Quaternion.Inverse(oldRotation) * (connected - oldPosition);
                replacement.breakForce = force; replacement.breakTorque = torque;
                replacement.enableCollision = collision; replacement.enablePreprocessing = preprocessing;
                foreach (var variable in variables) variable.Value = replacement;
                SyncEventLog.Record("vehicle-parking-rebased", "id=" + item.Id);
                return true;
            }
            catch (Exception error)
            {
                if (Time.unscaledTime >= _nextParkingJointError)
                {
                    _nextParkingJointError = Time.unscaledTime + 10;
                    WinterMPPlugin.Log.LogWarning("Vehicle parking pose deferred for " + item.Path + ": " + error.Message);
                }
                return false;
            }
        }
    }
}
