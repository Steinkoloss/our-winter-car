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

            // PlayerInVar and the 1.5 m MassDriver fallback can also promote the local
            // player to "driver", but neither is a clean local-only signal:
            //  - PlayerInVar is the game's PlayerIn FsmBool, which the REMOTE climate
            //    stream overwrites on the observing machine (see
            //    VehicleWorldSync.Climate.ApplyRemoteFogLevel), so on an observer it
            //    reflects whether the *remote* occupant is in the car, not the local one;
            //  - the 1.5 m fallback fires for ANY nearby player, not only a seated one.
            // Trust them only when no remote owner is asserting this car. Otherwise an
            // observer (or a bystander standing by the door) of a remotely owned/driven
            // car would be mis-detected as its driver and could wrongly claim it with
            // FlagDriver, fighting the real owner. Entering your own free car still works
            // because an unowned car has RemoteOwner == NoOwner.
            if (item != null && item.RemoteOwner == WorldSyncIds.NoOwner)
            {
                // Additional guard for PlayerInVar specifically: the transform stream can
                // go stale (RemoteOwner scrubbed to NoOwner) while the CLIMATE stream is
                // still live — e.g. a remote player sitting in a parked car — and during
                // that window PlayerInVar is still the remote occupant's contaminated
                // value. Only trust it when no remote climate is live either.
                if (item.PlayerInVar != null && item.PlayerInVar.Value
                    && Time.unscaledTime >= item.RemoteClimateUntil)
                    return true;

                // Enter-seat race: hierarchy may lag one frame; MassDriver is the
                // in-cabin physics anchor the game uses while driving.
                EnsureSeat(item);
                if (item.DriverAnchorTransform != null)
                {
                    float distSq = (_bridge.LocalPlayer.position - item.DriverAnchorTransform.position).sqrMagnitude;
                    if (distSq <= 2.25f)
                        return true;
                }
            }

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
        /// Vehicles carry a "DriveTrigger"/"DriveTriggerX" child whose collider is
        /// the get-in interaction. Found lazily, once per vehicle.
        /// </summary>
        private static void EnsureSeat(SyncedItem item)
        {
            if (item.SeatSearched || item.Body == null) return;
            item.SeatSearched = true;

            foreach (var transform in item.Body.GetComponentsInChildren<Transform>(true))
            {
                string name = transform.name;
                if (item.SeatTransform == null && name.StartsWith("DriveTrigger", StringComparison.Ordinal))
                {
                    item.SeatTransform = transform;
                    item.SeatCollider = transform.GetComponent<Collider>();
                }

                if (item.DriverAnchorTransform == null && name == "MassDriver")
                    item.DriverAnchorTransform = transform;
            }
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
