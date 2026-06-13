using UnityEngine;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        /// <summary>Items inside a driven vehicle are never claimed locally —
        /// cargo rides with the driver's physics / vehicle stream, not independent
        /// world-space item packets (which desync and fight the car).</summary>
        private const float VehicleInteriorRadius = 7f;

        private static void ClearCargoFollow(SyncedItem item)
        {
            item.CargoFollowActive = false;
            item.CargoFollowVehicleId = 0;
        }

        private static void ReleaseLocalCargoOwnership(SyncedItem item, Rigidbody body)
        {
            if (item.RemoteOwner != WorldSyncIds.NoOwner)
            {
                body.isKinematic = item.KinematicSaved ? item.OriginalKinematic : body.isKinematic;
                item.RemoteOwner = WorldSyncIds.NoOwner;
                item.RemoteIsDriver = false;
                item.RemoteVehicleStream = false;
                item.LastRemoteAt = -999f;
            }

            if (item.KinematicSaved)
                body.isKinematic = item.OriginalKinematic;

            item.LocallyOwned = false;
        }

        private void ApplyVehicleCargoFollow(SyncedItem item, SyncedItem vehicle, Rigidbody body)
        {
            if (vehicle.Body == null) return;

            var vehicleTransform = vehicle.Body.transform;
            if (!item.CargoFollowActive || item.CargoFollowVehicleId != vehicle.Id)
            {
                item.CargoFollowVehicleId = vehicle.Id;
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
        }

        private bool ShouldFollowVehicleCargo(SyncedItem item, SyncedItem vehicle, float now)
        {
            if (item.IsVehicle || IsPlayerHeldItem(item) || vehicle.Body == null) return false;
            if (vehicle.RemoteOwner == WorldSyncIds.NoOwner) return false;
            if (!ItemTransformPolicy.IsRemoteStreamLive(
                    vehicle.LastRemoteAt, now, vehicle.RemoteIsDriver, vehicle.RemoteVehicleStream))
                return false;

            return IsVehicleInMotion(vehicle, now);
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

                float sqr = (position - candidate.Body.transform.position).sqrMagnitude;
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
