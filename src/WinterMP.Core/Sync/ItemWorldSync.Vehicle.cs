using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private static Transform GetVehicleSceneRoot(Transform bodyTransform)
        {
            Transform root = bodyTransform;
            while (root.parent != null)
                root = root.parent;
            return root;
        }

        private static float GetRemoteHoldSeconds(SyncedItem item) =>
            ItemTransformPolicy.GetRemoteHoldSeconds(item.RemoteIsDriver, item.RemoteVehicleStream);

        private bool IsLocalVehicleOperator(SyncedItem item)
        {
            if (!item.IsVehicle) return false;
            if (item.LocalDriveActive) return true;
            return IsLocalPlayerDriving(item);
        }

        internal bool IsLocalPlayerDriving(SyncedItem item) => IsLocalPlayerDriving(item.Body, item);

        internal bool IsLocalPlayerDrivingAny()
        {
            _bridge.FindLocalPlayer();
            if (_bridge.LocalPlayer == null) return false;

            foreach (var item in _items.Values)
            {
                if (item.IsVehicle && IsLocalPlayerDriving(item))
                    return true;
            }

            return false;
        }

        internal bool IsLocalPlayerDriving(Rigidbody vehicleBody, SyncedItem? item = null)
        {
            if (PassengerController.Instance != null && PassengerController.Instance.IsLocalSeated)
                return false;

            _bridge.FindLocalPlayer();
            if (_bridge.LocalPlayer == null || vehicleBody == null) return false;

            // IsChildOf is the reliable, local-only signal: the local avatar is parented
            // under the car only on the machine that is actually driving it.
            Transform vehicleRoot = GetVehicleSceneRoot(vehicleBody.transform);
            if (_bridge.LocalPlayer.IsChildOf(vehicleRoot))
                return true;

            if (_bridge.LocalPlayer.IsChildOf(vehicleBody.transform))
                return true;

            // GlassFrosting.PlayerIn describes cabin proximity, including a player
            // standing beside the car after exiting. It cannot establish a driver
            // claim: doing so keeps the other peer's driver trigger blocked.
            return false;
        }

        private static bool LocalDriverOutClaimsRemote(SessionManager session, SyncedItem item, bool remoteDriven)
        {
            return ItemTransformPolicy.ShouldSeatDriverOutClaimRemote(
                remoteDriven,
                item.RemoteIsDriver,
                item.RemoteVehicleStream,
                item.LastRemoteAt,
                Time.unscaledTime,
                session.LocalPlayerId,
                item.RemoteOwner);
        }

        /// <summary>
        /// Resolve a vehicle's seat handles, lazily and once. Two distinct things: the
        /// "DriveTrigger"/"DriveTriggerX" child's collider is the get-in interaction (toggled
        /// to block a taken seat), while MassDriver is the actual seated point we anchor the
        /// remote driver's body to. They are NOT the same object — the trigger sits up at the
        /// door/window, so anchoring the body there floated it near the roof.
        /// </summary>
        private static void EnsureSeat(SyncedItem item)
        {
            if (item.SeatSearched || item.Body == null) return;
            item.SeatSearched = true;

            Transform? driveTrigger = null;
            Transform? massDriver = null;
            foreach (var transform in item.Body.GetComponentsInChildren<Transform>(true))
            {
                string name = transform.name;
                if (driveTrigger == null && name.StartsWith("DriveTrigger", StringComparison.Ordinal))
                    driveTrigger = transform;
                else if (massDriver == null && name == "MassDriver")
                    massDriver = transform;
                if (driveTrigger != null && massDriver != null) break;
            }

            if (driveTrigger != null)
                item.SeatCollider = driveTrigger.GetComponent<Collider>();

            // Anchor the seated body at MassDriver; fall back to the get-in trigger only for
            // odd vehicles that lack it (a slightly-high body beats one dropped at the origin).
            item.SeatTransform = massDriver != null ? massDriver : driveTrigger;
        }

        /// <summary>
        /// Disable the get-in trigger while a remote driver occupies the vehicle —
        /// otherwise the seat looks free here (remote avatars are visual only) and
        /// a second player could "enter" an already-driven car.
        /// </summary>
        private static void SetSeatBlocked(SyncedItem item, bool blocked)
        {
            if (!item.IsVehicle || item.SeatBlocked == blocked) return;
            EnsureSeat(item);
            item.SeatBlocked = blocked;
            if (item.SeatCollider != null)
                item.SeatCollider.enabled = !blocked;
        }

        /// <summary>Registered vehicle handle for the passenger-seat layer.</summary>
        public void CollectVehicles(List<WorldSyncManager.VehicleInfo> results)
        {
            results.Clear();
            foreach (var pair in _items)
            {
                if (pair.Value.IsVehicle && pair.Value.Body != null)
                    results.Add(new WorldSyncManager.VehicleInfo { Id = pair.Key, Body = pair.Value.Body });
            }
        }

        /// <summary>
        /// The vehicle (and its seat) currently driven by the given remote player,
        /// if any — PlayerSync anchors that player's avatar to it so the body rides
        /// in the cabin instead of trailing the smoothed world stream.
        /// </summary>
        public bool TryGetDriverAnchor(byte playerId, out Transform? seat, out Transform? vehicle)
        {
            float now = Time.unscaledTime;
            foreach (var item in _items.Values)
            {
                if (!item.IsVehicle || item.RemoteOwner != playerId) continue;
                if (item.Body == null
                    || !ItemTransformPolicy.IsRemoteStreamLive(
                        item.LastRemoteAt, now, item.RemoteIsDriver, item.RemoteVehicleStream))
                    continue;
                // Only actual drivers ride the cabin — players pushing the car
                // (FlagVehicle stream) stay on their own world-space pose.
                if (!item.RemoteIsDriver) continue;

                EnsureSeat(item);
                vehicle = item.Body.transform;
                seat = item.SeatTransform != null ? item.SeatTransform : vehicle;
                return true;
            }

            seat = null;
            vehicle = null;
            return false;
        }
    }
}
