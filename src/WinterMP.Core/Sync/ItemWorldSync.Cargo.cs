using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        /// <summary>Items inside a driven vehicle ride the vehicle transform on every
        /// machine — never independent world-space item packets or local physics while
        /// the car is moving (which flings trunk cargo on ownership hand-off).</summary>
        private const float VehicleInteriorRadius = 7f;
        private const float CargoFollowResyncDistanceSqr = 1.5f * 1.5f;

        private static void ClearCargoFollow(SyncedItem item, Rigidbody? body = null)
        {
            if (!item.CargoFollowActive && item.CargoFollowVehicleId == 0) return;

            item.CargoFollowActive = false;
            item.CargoFollowVehicleId = 0;
            item.CargoFollowDriverId = WorldSyncIds.NoOwner;

            if (body != null && item.KinematicSaved)
                body.isKinematic = item.OriginalKinematic;
        }

        /// <summary>Drop item ownership / remote hold without waking physics.</summary>
        private static void PrepareCargoForVehicleFollow(SyncedItem item, Rigidbody body)
        {
            item.RemoteOwner = WorldSyncIds.NoOwner;
            item.RemoteIsDriver = false;
            item.RemoteVehicleStream = false;
            item.LastRemoteAt = -999f;
            item.LocallyOwned = false;

            if (!item.KinematicSaved)
            {
                item.OriginalKinematic = body.isKinematic;
                item.KinematicSaved = true;
            }
        }

        private void ApplyVehicleCargoFollow(SyncedItem item, SyncedItem vehicle, Rigidbody body)
        {
            if (vehicle.Body == null) return;

            var vehicleTransform = vehicle.Body.transform;
            byte driverId = GetVehicleControllingPlayerId(vehicle);
            bool recapture = !item.CargoFollowActive
                || item.CargoFollowVehicleId != vehicle.Id
                || item.CargoFollowDriverId != driverId;

            if (!recapture)
            {
                Vector3 expected = vehicleTransform.TransformPoint(item.CargoFollowLocalPos);
                if ((body.transform.position - expected).sqrMagnitude > CargoFollowResyncDistanceSqr)
                    recapture = true;
            }

            if (recapture)
            {
                item.CargoFollowVehicleId = vehicle.Id;
                item.CargoFollowDriverId = driverId;
                item.CargoFollowLocalPos = vehicleTransform.InverseTransformPoint(body.transform.position);
                item.CargoFollowLocalRot = Quaternion.Inverse(vehicleTransform.rotation) * body.transform.rotation;
                item.CargoFollowActive = true;
            }

            if (!item.KinematicSaved)
            {
                item.OriginalKinematic = body.isKinematic;
                item.KinematicSaved = true;
            }

            body.isKinematic = true;
            body.transform.position = vehicleTransform.TransformPoint(item.CargoFollowLocalPos);
            body.transform.rotation = vehicleTransform.rotation * item.CargoFollowLocalRot;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            item.LastPosition = body.transform.position;
            item.LastMovedAt = Time.unscaledTime;
        }

        private void InvalidateCargoFollowOffsets(uint vehicleId)
        {
            foreach (var item in _items.Values)
            {
                if (item.CargoFollowActive && item.CargoFollowVehicleId == vehicleId)
                    item.CargoFollowActive = false;
            }
        }

        private bool ShouldCargoRideVehicle(SyncedItem item, SyncedItem vehicle, float now)
        {
            if (item.IsVehicle || IsPlayerHeldItem(item) || vehicle.Body == null) return false;
            if (!IsVehicleInMotion(vehicle, now)) return false;

            if (IsLocalVehicleOperator(vehicle)) return true;

            if (vehicle.RemoteOwner == WorldSyncIds.NoOwner) return false;
            return ItemTransformPolicy.IsRemoteStreamLive(
                vehicle.LastRemoteAt, now, vehicle.RemoteIsDriver, vehicle.RemoteVehicleStream);
        }

        private byte GetVehicleControllingPlayerId(SyncedItem vehicle)
        {
            if (IsLocalVehicleOperator(vehicle))
            {
                var session = SessionManager.Instance;
                return session != null ? session.LocalPlayerId : WorldSyncIds.NoOwner;
            }

            if (vehicle.RemoteOwner != WorldSyncIds.NoOwner && vehicle.RemoteIsDriver)
                return vehicle.RemoteOwner;

            return WorldSyncIds.NoOwner;
        }

        private bool IsVehicleInMotion(SyncedItem vehicle, float now)
        {
            if (vehicle.Body == null) return false;
            if (vehicle.LocalDriveActive) return true;
            return now - vehicle.LastMovedAt < vehicle.StillSeconds;
        }

        private bool TryGetContainingVehicle(SyncedItem item, out SyncedItem? vehicle)
        {
            vehicle = null;
            if (item.IsVehicle || item.Body == null) return false;

            float radiusSqr = VehicleInteriorRadius * VehicleInteriorRadius;
            Vector3 position = item.Body.transform.position;
            float bestSqr = float.MaxValue;

            foreach (var candidate in _items.Values)
            {
                if (!candidate.IsVehicle || candidate.Body == null) continue;

                Vector3 anchor = GetVehicleSceneRoot(candidate.Body.transform).position;
                float sqr = (position - anchor).sqrMagnitude;
                if (sqr > radiusSqr || sqr >= bestSqr) continue;

                bestSqr = sqr;
                vehicle = candidate;
            }

            return vehicle != null;
        }

        private bool IsPlayerHeldItem(SyncedItem item)
        {
            if (item.Body == null) return false;

            _bridge.FindLocalPlayer();
            return _bridge.LocalPlayer != null && item.Body.transform.IsChildOf(_bridge.LocalPlayer);
        }
    }
}
