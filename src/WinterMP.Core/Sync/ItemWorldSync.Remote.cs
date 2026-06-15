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
            if (TryGetContainingVehicle(item, out SyncedItem? cargoVehicle)
                && cargoVehicle != null)
            {
                // Driver simulates cargo with the vehicle — never apply guest item streams.
                if (IsLocalVehicleOperator(cargoVehicle))
                    return;

                if (ItemTransformPolicy.ShouldIgnoreRemoteItemTransformForVehicleCargo(
                        item.IsVehicle,
                        IsVehicleInMotion(cargoVehicle, now)))
                    return;
            }

            // Stale unreliable packets from the same owner are dropped (wrap-aware).
            if (message.OwnerPlayerId == item.RemoteOwner)
            {
                if (ItemTransformPolicy.IsStaleSequence(item.LastRemoteSequence, message.Sequence))
                {
                    if (!message.IsFinal && !message.IsDriver)
                        ConnectionQuality.Instance.NoteUnreliableDropped();
                    return;
                }
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
                    // Snap on first packet so a car that drove away doesn't stay parked
                    // locally until someone walks up and triggers a huge correction.
                    body.transform.position = position;
                    body.transform.rotation = rotation;
                    InvalidateCargoFollowOffsets(item.Id);
                }

                item.TargetPosition = position;
                item.TargetRotation = rotation;
                item.LastRemoteAt = Time.unscaledTime;

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
            if ((transform.position - item.TargetPosition).sqrMagnitude > RemoteSnapDistance * RemoteSnapDistance)
            {
                transform.position = item.TargetPosition;
                transform.rotation = item.TargetRotation;
            }
            else
            {
                float t = 1f - Mathf.Exp(-RemoteLerpSpeed * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, item.TargetPosition, t);
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
