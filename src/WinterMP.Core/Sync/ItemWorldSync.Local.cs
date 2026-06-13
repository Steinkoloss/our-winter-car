using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private const float DriverKeepaliveSeconds = 0.4f;

        internal void UpdateItems(SessionManager session)
        {
            if (session.PlayerCount == 0) return;

            _bridge.FindLocalPlayer();
            float now = Time.unscaledTime;

            foreach (var pair in _items)
            {
                var item = pair.Value;
                var body = item.Body;
                if (body == null)
                {
                    if (!item.DespawnSent && !_bridge.ApplyingRemote)
                        AnnounceItemDespawn(pair.Key, "removed");
                    // Removing here would invalidate the enumerator (this killed
                    // world sync mid-session); collect and remove after the loop.
                    _deadItemIds.Add(pair.Key);
                    continue;
                }

                bool operating = item.IsVehicle && IsLocalPlayerDriving(item);
                bool remoteDriven = item.RemoteOwner != WorldSyncIds.NoOwner
                    && ItemTransformPolicy.IsRemoteStreamLive(
                        item.LastRemoteAt, now, item.RemoteIsDriver, item.RemoteVehicleStream);

                if (item.IsVehicle)
                    VehicleWorldSync.UpdateRemoteEngineAudio(item, now);

                if (operating && !item.LocallyOwned
                    && LocalDriverOutClaimsRemote(session, item, remoteDriven))
                {
                    ClaimItem(session, item, body, now);
                    remoteDriven = false;
                }

                if (remoteDriven && !item.LocallyOwned)
                {
                    if (TryGetContainingVehicle(item, out SyncedItem? cargoVehicle)
                        && cargoVehicle != null
                        && ShouldFollowVehicleCargo(item, cargoVehicle, now))
                    {
                        item.RemoteOwner = WorldSyncIds.NoOwner;
                        item.RemoteIsDriver = false;
                        item.RemoteVehicleStream = false;
                        item.LastRemoteAt = -999f;
                        ApplyVehicleCargoFollow(item, cargoVehicle, body);
                        continue;
                    }

                    ApplyRemoteSmoothing(item, body);
                    continue;
                }

                if (TryGetContainingVehicle(item, out SyncedItem? localCargoVehicle)
                    && localCargoVehicle != null
                    && IsLocalVehicleOperator(localCargoVehicle)
                    && IsVehicleInMotion(localCargoVehicle, now))
                {
                    ClearCargoFollow(item);
                    ReleaseLocalCargoOwnership(item, body);
                    item.LastPosition = body.transform.position;
                    item.LastMovedAt = now;
                    continue;
                }

                ClearCargoFollow(item);

                if (item.RemoteOwner != WorldSyncIds.NoOwner && !remoteDriven)
                {
                    body.isKinematic = item.OriginalKinematic;
                    item.RemoteOwner = WorldSyncIds.NoOwner;
                    item.RemoteIsDriver = false;
                    item.RemoteVehicleStream = false;
                    SetSeatBlocked(item, false);
                }

                var position = body.transform.position;
                if ((position - item.LastPosition).sqrMagnitude > item.MoveThresholdSqr)
                {
                    item.LastMovedAt = now;
                    item.LastPosition = position;
                }

                bool moving = now - item.LastMovedAt < item.StillSeconds;

                if (item.LocallyOwned)
                {
                    if (item.IsVehicle)
                    {
                        // Latch driver status while the car is in motion (seat
                        // detection can flicker mid-drive), but release it once
                        // the car is parked and the seat is empty — a permanent
                        // latch kept streaming "driver" keepalives forever and
                        // the other player could never use the car again.
                        if (operating)
                            item.LocalDriveActive = true;
                        else if (!moving)
                            item.LocalDriveActive = false;

                        if (item.LocalDriveActive || moving)
                        {
                            if (now >= item.NextSendAt)
                            {
                                SendItem(session, item, body, false);
                                item.NextSendAt = now + (moving
                                    ? 1f / item.SendRateHz
                                    : DriverKeepaliveSeconds);
                            }
                        }
                        else if (!ConnectionQuality.Instance.ShouldPauseOwnershipTransfers)
                        {
                            SendItem(session, item, body, true);
                            item.LocallyOwned = false;
                            item.LocalDriveActive = false;
                        }
                        else if (now >= item.NextSendAt)
                        {
                            SendItem(session, item, body, false);
                            item.NextSendAt = now + DriverKeepaliveSeconds;
                        }
                    }
                    else if (!moving)
                    {
                        if (!ConnectionQuality.Instance.ShouldPauseOwnershipTransfers)
                        {
                            SendItem(session, item, body, true);
                            item.LocallyOwned = false;
                        }
                        else if (now >= item.NextSendAt)
                        {
                            SendItem(session, item, body, false);
                            item.NextSendAt = now + DriverKeepaliveSeconds;
                        }
                    }
                    else if (now >= item.NextSendAt)
                    {
                        SendItem(session, item, body, false);
                        item.NextSendAt = now + 1f / item.SendRateHz;
                    }
                }
                else if (moving && CanClaim(item, position, now)
                         && !ConnectionQuality.Instance.ShouldPauseOwnershipTransfers)
                {
                    ClaimItem(session, item, body, now);
                }
            }

            if (_deadItemIds.Count > 0)
            {
                foreach (uint id in _deadItemIds)
                    RemoveTrackedItem(id, null);
                _deadItemIds.Clear();
            }
        }

        private bool CanClaim(SyncedItem item, Vector3 position, float now)
        {
            _bridge.FindLocalPlayer();
            if (_bridge.LocalPlayer == null) return false;

            if (item.IsVehicle && IsLocalVehicleOperator(item)) return true;

            if (item.IsVehicle && !IsLocalVehicleOperator(item)
                && !ItemTransformPolicy.AllowsVehicleProximityClaim(
                    item.RemoteIsDriver,
                    item.RemoteVehicleStream && ItemTransformPolicy.IsRemoteStreamLive(
                        item.LastRemoteAt, now, item.RemoteIsDriver, isVehicle: true),
                    item.RemoteOwner,
                    item.LastRemoteAt,
                    now))
                return false;

            if ((position - _bridge.LocalPlayer.position).sqrMagnitude >= item.ClaimRadius * item.ClaimRadius)
                return false;

            if (TryGetContainingVehicle(item, out SyncedItem? cargoVehicle)
                && cargoVehicle != null
                && ItemTransformPolicy.ShouldBlockClaimForVehicleCargo(
                    item.IsVehicle,
                    IsVehicleInMotion(cargoVehicle, now)))
                return false;

            return true;
        }

        private void ClaimItem(SessionManager session, SyncedItem item, Rigidbody body, float now)
        {
            // Taking over from a remote stream: restore physics before simulating.
            if (item.RemoteOwner != WorldSyncIds.NoOwner || (body.isKinematic && item.KinematicSaved))
            {
                body.isKinematic = item.OriginalKinematic;
                item.RemoteOwner = WorldSyncIds.NoOwner;
                item.RemoteIsDriver = false;
                item.RemoteVehicleStream = false;
                item.LastRemoteAt = -999f;
                SetSeatBlocked(item, false);
            }

            item.RemoteEngineUntil = -999f;

            item.LocallyOwned = true;
            item.LastMovedAt = now;
            // Driver status only when actually seated — a proximity claim (pushing
            // the car, standing nearby) must stream as FlagVehicle, not FlagDriver,
            // or it blocks the real driver's seat on the other machine.
            if (item.IsVehicle)
                item.LocalDriveActive = IsLocalPlayerDriving(item);
            SendItem(session, item, body, false);
            item.NextSendAt = now + 1f / item.SendRateHz;

            if (item.IsVehicle)
                WinterMPPlugin.Log.LogInfo($"WorldSync: claimed vehicle '{item.Path}' " +
                    $"({(IsLocalVehicleOperator(item) ? "driving" : "pushing")}).");
            else
                WinterMPPlugin.Log.LogDebug($"WorldSync: claimed item '{item.Path}'.");
        }

        private void SendItem(SessionManager session, SyncedItem item, Rigidbody body, bool final)
        {
            bool isVehicle = item.IsVehicle;
            bool isDriver = isVehicle && IsLocalVehicleOperator(item);
            byte flags = 0;
            if (final) flags |= ItemTransform.FlagFinal;
            if (isDriver) flags |= ItemTransform.FlagDriver;
            if (isVehicle && !final) flags |= ItemTransform.FlagVehicle;

            var message = new ItemTransform
            {
                ItemId = item.Id,
                OwnerPlayerId = session.LocalPlayerId,
                Sequence = ++item.OutSequence,
                Flags = flags,
                Position = body.transform.position.ToNet(),
                Rotation = body.transform.rotation.ToNet(),
            };

            session.SendWorldMessage(message, ItemTransformPolicy.SelectSendChannel(final, isVehicle));

            if (final && isVehicle)
                item.LocalDriveActive = false;
        }
    }
}
