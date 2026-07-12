using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private const float RemoteLerpSpeed = 12f;
        private const float RemoteSnapDistance = 15f;

        public void OnRemoteItemTransform(ItemTransform message)
        {
            if (!_items.TryGetValue(message.ItemId, out var item) || item.Body == null) return;

            // Validate the wire pose BEFORE mutating any ownership/sequence state. Wire
            // floats reach the transform verbatim (NetReader reinterprets raw bytes,
            // ToUnity copies components). A single NaN/Infinity would propagate forever
            // through the smoothing lerp (NaN compares false against the snap threshold)
            // and Unity silently drops a NaN transform — the body vanishes for the rest
            // of the session. Re-normalize the quaternion too (round-trip precision
            // leaves it slightly non-unit, which makes Slerp wobble). Rejecting here
            // (rather than after the ownership logic) means a garbage packet cannot make
            // us yield local ownership or advance the sequence baseline.
            var position = message.Position.ToUnity();
            var rotation = message.Rotation.ToUnity();
            if (!IsFinite(position) || !TryNormalize(rotation, out rotation))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"WorldSync: dropping non-finite transform for item {message.ItemId} from player {message.OwnerPlayerId}.");
                return;
            }

            float now = Time.unscaledTime;

            // Stale, out-of-order packets from the owner whose sequence baseline we hold
            // are dropped (wrap-aware). LastRemoteSequenceOwner survives a final packet's
            // RemoteOwner reset, so a late straggler that arrives AFTER the reliable final
            // (a non-vehicle moving packet on the unreliable channel can overtake it) is
            // still recognised as stale and dropped instead of reviving the just-rested
            // item at a mid-flight pose (#7). Checked BEFORE the cargo-pin hand-off below:
            // a stale straggler must not be able to kill a live pin and then get dropped.
            if (message.OwnerPlayerId == item.RemoteOwner
                || message.OwnerPlayerId == item.LastRemoteSequenceOwner)
            {
                if (ItemTransformPolicy.IsStaleSequence(item.LastRemoteSequence, message.Sequence))
                {
                    if (!message.IsFinal && !message.IsDriver)
                        ConnectionQuality.Instance.NoteUnreliableDropped();
                    return;
                }
            }

            if (item.RemoteCargoVehicleId != 0)
            {
                // A live cargo pin yields only to its own authority: that stream handing
                // the item off to world space (flung out, grabbed, settled). Third-party
                // streams wait until the pin goes stale.
                if (!ItemTransformPolicy.ShouldAcceptItemTransformOverCargoPin(
                        ItemTransformPolicy.IsCargoStreamFresh(item.RemoteCargoAt, now),
                        item.RemoteCargoOwner,
                        message.OwnerPlayerId))
                    return;

                ReleaseRemoteCargo(item, item.Body, now, seedVelocity: false);
            }

            if (!message.IsFinal && !message.IsDriver)
                ConnectionQuality.Instance.NoteUnreliableReceived();

            var session = SessionManager.Instance;
            if (item.LocallyOwned && session != null)
            {
                bool localIsDriver = item.IsVehicle && IsLocalVehicleOperator(item);
                if (!ItemTransformPolicy.RemoteClaimWinsOverLocal(
                        localIsDriver,
                        message.IsDriver,
                        session.LocalPlayerId,
                        message.OwnerPlayerId))
                    return;
                item.LocallyOwned = false;
                item.LocalDriveActive = false;
            }

            bool firstPacket = item.RemoteOwner != message.OwnerPlayerId;
            item.RemoteOwner = message.OwnerPlayerId;
            item.RemoteIsDriver = message.IsDriver;
            item.RemoteVehicleStream = message.IsVehicle && !message.IsFinal;
            item.LastRemoteSequence = message.Sequence;
            // Remember whose sequence space the baseline belongs to; unlike RemoteOwner
            // this is not cleared by a final, so post-final stragglers stay recognisable.
            item.LastRemoteSequenceOwner = message.OwnerPlayerId;

            var body = item.Body;
            if (!item.KinematicSaved)
            {
                item.OriginalKinematic = body.isKinematic;
                item.KinematicSaved = true;
            }

            if (message.IsFinal)
            {
                body.isKinematic = item.OriginalKinematic;
                body.transform.position = position;
                body.transform.rotation = rotation;
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.Sleep();
                }

                item.RemoteOwner = WorldSyncIds.NoOwner;
                item.RemoteIsDriver = false;
                item.RemoteVehicleStream = false;
                item.HasRemoteVelocity = false;
                item.LastRemoteAt = -999f;
                item.LastMovedAt = -999f; // the landing must not look like local motion
                item.LastPosition = position;
                SetSeatBlocked(item, false);
            }
            else
            {
                // Frozen while remote-driven; UpdateItems eases toward the target so
                // vehicles glide instead of teleporting at packet rate.
                body.isKinematic = true;
                if (firstPacket && item.IsVehicle)
                {
                    WinterMPPlugin.Log.LogInfo(
                        $"WorldSync: '{item.Path}' now {(message.IsDriver ? "driven" : "moved")} by player {message.OwnerPlayerId}.");
                    // Snap only when the new pose is far from the car's current local pose:
                    // a car that genuinely drove away while parked here needs the jump, but
                    // an ownership handoff between two remote contributors (e.g. driver to
                    // proximity-pusher) lands near the current pose and should keep
                    // smoothing instead of teleport-flickering (#15).
                    if ((body.transform.position - position).sqrMagnitude
                        > RemoteSnapDistance * RemoteSnapDistance)
                    {
                        body.transform.position = position;
                        body.transform.rotation = rotation;
                    }
                }

                item.TargetPosition = position;
                item.TargetRotation = rotation;
                item.LastRemoteAt = Time.unscaledTime;

                if (message.HasVelocity)
                {
                    // Garbage velocity must not poison the extrapolation; the validated
                    // pose is still worth keeping.
                    var velocity = message.Velocity.ToUnity();
                    item.HasRemoteVelocity = IsFinite(velocity);
                    if (item.HasRemoteVelocity)
                        item.RemoteVelocity = velocity;
                }
                else
                {
                    item.HasRemoteVelocity = false;
                }

                // A remotely-driven vehicle must count as "in motion" on the observing
                // machine so cargo-follow and per-item-stream suppression engage
                // (IsVehicleInMotion reads LastMovedAt; nothing else sets it for a car
                // driven by the remote player, so it would otherwise stay at -999f and
                // loose cargo would never ride the car / would jitter and fling out).
                // The final packet resets LastMovedAt to -999f so the rest still looks parked.
                if (item.IsVehicle)
                    item.LastMovedAt = Time.unscaledTime;

                // An occupied driver's seat must not be enterable locally. Push
                // streams (FlagVehicle without FlagDriver) leave the seat free —
                // policy lets a seated local player out-claim those.
                if (item.IsVehicle)
                    SetSeatBlocked(item, message.IsDriver);
            }
        }

        private static void ApplyRemoteSmoothing(SyncedItem item, Rigidbody body)
        {
            var transform = body.transform;
            Vector3 target = item.TargetPosition;
            if (item.HasRemoteVelocity)
            {
                // Dead-reckon ahead of the last packet so a fast car doesn't trail its
                // true pose by the smoothing constant (#14); capped so a stalling stream
                // stops extrapolating instead of driving through the scenery.
                target += item.RemoteVelocity
                    * ItemTransformPolicy.GetExtrapolationSeconds(item.LastRemoteAt, Time.unscaledTime);
            }

            if ((transform.position - target).sqrMagnitude > RemoteSnapDistance * RemoteSnapDistance)
            {
                transform.position = target;
                transform.rotation = item.TargetRotation;
            }
            else
            {
                float t = 1f - Mathf.Exp(-RemoteLerpSpeed * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, target, t);
                transform.rotation = Quaternion.Slerp(transform.rotation, item.TargetRotation, t);
            }

            item.LastPosition = transform.position;
        }

        private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        private static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);

        /// <summary>
        /// Validates the quaternion is finite and non-degenerate, returning a unit-length
        /// copy. Returns false (reject the packet) for NaN/Infinity components or a
        /// near-zero quaternion that cannot be normalized.
        /// </summary>
        private static bool TryNormalize(Quaternion q, out Quaternion result)
        {
            result = q;
            if (!IsFinite(q.x) || !IsFinite(q.y) || !IsFinite(q.z) || !IsFinite(q.w))
                return false;

            float sumSq = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (sumSq < 1e-8f)
                return false;

            float inv = 1f / Mathf.Sqrt(sumSq);
            result = new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
            return true;
        }
    }
}
