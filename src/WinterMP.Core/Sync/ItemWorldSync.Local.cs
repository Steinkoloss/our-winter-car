using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private const float DriverKeepaliveSeconds = 0.4f;

        /// <summary>
        /// After a remote car's stream goes quiet (past its hold), keep it frozen at its
        /// last pose for this much longer before releasing it to local physics. Absorbs
        /// brief lag spikes / GC stalls on the owner's machine so the car doesn't drop to
        /// gravity at a stale pose and then hard-snap when packets resume (#6).
        /// </summary>
        private const float RemoteReleaseGraceSeconds = 2f;

        /// <summary>
        /// A locally-owned vehicle counts as "at rest" (safe to hand off with a final
        /// packet) below this squared speed, in addition to Rigidbody.IsSleeping(). The
        /// position-threshold "moving" check alone treats a slow roll as still, so without
        /// this a car creeping downhill would stop being streamed and freeze on the guest
        /// while it keeps drifting on the owner (#10).
        /// </summary>
        private const float RestVelocitySqr = 0.0025f;

        internal void UpdateItems(SessionManager session)
        {
            if (session.PlayerCount == 0) return;

            _bridge.FindLocalPlayer();
            float now = Time.unscaledTime;
            CollectActiveCargoVehicles(now);

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

                if (!item.IsVehicle)
                {
                    // Pinned by a remote cargo stream: composed after this loop, once
                    // every vehicle has been smoothed for the frame.
                    if (item.RemoteCargoVehicleId != 0 && HandleRemoteCargoState(item, body, now))
                        continue;

                    // Riding a vehicle we stream: the game's own physics simulates it
                    // here; SendLocalCargo broadcasts its vehicle-local pose.
                    var cargoVehicle = FindLocalCargoVehicle(item, now, out float cargoDistanceSqr);
                    if (cargoVehicle != null)
                    {
                        TrackMotion(item, body, now);
                        EnterLocalCargo(item, cargoVehicle, body, cargoDistanceSqr);
                        continue;
                    }

                    if (item.LocalCargoVehicleId != 0)
                        HandleLocalCargoExit(session, item, body, now, demoted: false);
                }

                if (remoteDriven && !item.LocallyOwned)
                {
                    ApplyRemoteSmoothing(item, body);
                    continue;
                }

                if (item.RemoteOwner != WorldSyncIds.NoOwner && !remoteDriven)
                {
                    // Stream went quiet past its hold. Hold the car frozen at its last pose
                    // for a short grace window before releasing it to local physics: a brief
                    // lag spike or GC stall on the owner's machine must not drop the car to
                    // gravity at a stale pose and then hard-snap when packets resume. The
                    // body is already kinematic from the last remote packet, so simply
                    // holding (skipping the release) keeps it frozen, and keeping RemoteOwner
                    // set means a resuming packet is not treated as a first packet (no snap).
                    if (!item.LocallyOwned
                        && now - item.LastRemoteAt < GetRemoteHoldSeconds(item) + RemoteReleaseGraceSeconds)
                        continue;

                    body.isKinematic = item.OriginalKinematic;
                    item.RemoteOwner = WorldSyncIds.NoOwner;
                    item.RemoteIsDriver = false;
                    item.RemoteVehicleStream = false;
                    item.HasRemoteVelocity = false;
                    SetSeatBlocked(item, false);
                }

                TrackMotion(item, body, now);
                bool moving = now - item.LastMovedAt < item.StillSeconds;

                if (item.LocallyOwned)
                {
                    if (item.IsVehicle)
                    {
                        // Driver status tracks seat occupancy (IsLocalPlayerDriving is
                        // anchored on the game's own PlayerInVar + seat hierarchy, so it
                        // is stable during a real drive). Release it the moment the player
                        // leaves the seat — NOT only once the car is parked. The old
                        // "clear only when !moving" tied seat-release to motion, so a car
                        // coasting after the driver got out kept streaming FlagDriver and
                        // the remote seat stayed blocked (and under packet loss, far
                        // longer). Once !operating the car still streams as a FlagVehicle
                        // push while it rolls (the `|| moving` branch below), which leaves
                        // the remote seat free so the other player can get in.
                        if (operating)
                            item.LocalDriveActive = true;
                        else
                            item.LocalDriveActive = false;

                        // Don't hand the car off until its physics have actually settled.
                        // The position-threshold "moving" check treats a slow roll (a car
                        // creeping below ~3 m/s) as still, so releasing on !moving alone
                        // could stop streaming a car that keeps drifting on the owner's
                        // machine — the guest would freeze at the last pose (the final
                        // packet sleeps its body) while the owner's car rolls away, with no
                        // resync until someone walks back within range.
                        bool atRest = body.IsSleeping()
                            || body.velocity.sqrMagnitude < RestVelocitySqr;

                        if (item.LocalDriveActive || moving || !atRest)
                        {
                            if (now >= item.NextSendAt)
                            {
                                SendItem(session, item, body, false);
                                item.NextSendAt = now + ((moving || !atRest)
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
                else if (moving && CanClaim(item, body.transform.position, now)
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

            SendLocalCargo(session, now);
            ComposeRemoteCargo(now);
        }

        private static void TrackMotion(SyncedItem item, Rigidbody body, float now)
        {
            var position = body.transform.position;
            if ((position - item.LastPosition).sqrMagnitude > item.MoveThresholdSqr)
            {
                item.LastMovedAt = now;
                item.LastPosition = position;
            }
        }

        private bool CanClaim(SyncedItem item, Vector3 position, float now)
        {
            _bridge.FindLocalPlayer();
            if (_bridge.LocalPlayer == null) return false;

            // Genuine local driver/operator fast-path: not proximity-gated, because at
            // speed the game leaves PLAYER lagging the rigidbody (often >ClaimRadius).
            if (item.IsVehicle && IsLocalVehicleOperator(item)) return true;

            // A live remote owner never reaches here (UpdateItems handles it via the
            // remote-driven branch / stale-clear), so there is no send-side proximity
            // policy to consult — competing claims converge on the receive side via
            // RemoteClaimWinsOverLocal. Proximity claims just need to be in range.
            if ((position - _bridge.LocalPlayer.position).sqrMagnitude >= item.ClaimRadius * item.ClaimRadius)
                return false;

            if (ItemTransformPolicy.ShouldBlockClaimForVehicleCargo(
                    item.IsVehicle,
                    ItemTransformPolicy.IsCargoStreamFresh(item.RemoteCargoAt, now),
                    IsPlayerHeldItem(item)))
                return false;

            return true;
        }

        private void ClaimItem(SessionManager session, SyncedItem item, Rigidbody body, float now)
        {
            ReleaseRemoteCargo(item, body, now, seedVelocity: false);

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

            // Claiming a vehicle also ends the previous owner's cargo authority over the
            // items riding it — our physics simulates them from here on.
            if (item.IsVehicle)
                ReleaseRemoteCargoForVehicle(item.Id, now, seedVelocity: true);

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

            // Moving vehicles carry velocity so receivers dead-reckon between packets
            // and can seed physics when a stream dies mid-drive.
            if (isVehicle && !final && !body.isKinematic)
            {
                message.Flags |= ItemTransform.FlagHasVelocity;
                message.Velocity = body.velocity.ToNet();
            }

            session.SendWorldMessage(message, ItemTransformPolicy.SelectSendChannel(final, isVehicle));

            if (final && isVehicle)
                item.LocalDriveActive = false;
        }
    }
}
