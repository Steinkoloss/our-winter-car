using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core;
using WinterMP.Core.Catalog;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using WinterMP.Net.Transport;

namespace WinterMP.Core.Session
{
    public sealed partial class SessionManager
    {
        private void HandleHandshakeRequest(PeerId peer, HandshakeRequest request)
        {
            if (_playersByPeer.TryGetValue(peer, out var existing))
            {
                SendAcceptedHandshake(peer, existing.PlayerId);
                WinterMPPlugin.Log.LogDebug(
                    $"Re-acknowledged duplicate handshake from {existing.Name} ({peer}) as player {existing.PlayerId}.");
                return;
            }

            SyncCatalog.EnsureLoaded();

            string? refusal = null;
            string hostGameVersion = Util.SafeApp.GameVersion;
            if (request.ProtocolVersion != ProtocolInfo.Version)
                refusal = $"Protocol mismatch (host v{ProtocolInfo.Version}, you v{request.ProtocolVersion}). Update {MyPluginInfo.PLUGIN_NAME}.";
            else if (request.ModVersion != MyPluginInfo.PLUGIN_VERSION)
                refusal = $"Mod version mismatch (host {MyPluginInfo.PLUGIN_VERSION}, you {request.ModVersion}).";
            else if (request.GameVersion != hostGameVersion)
                refusal = $"Game version mismatch (host {hostGameVersion}, you {request.GameVersion}).";
            else if (SyncCatalog.Loaded && request.CatalogHash != SyncCatalog.Hash)
                refusal = $"Sync catalog mismatch (host {SyncCatalog.Hash:X8}, you {request.CatalogHash:X8}). Reinstall {MyPluginInfo.PLUGIN_NAME}.";

            if (refusal != null)
            {
                WinterMPPlugin.Log.LogWarning($"Refused {request.PlayerName} ({peer}): {refusal}");
                SendTo(peer, new HandshakeResponse { Accepted = false, Reason = refusal }, Channel.ReliableOrdered);
                return;
            }

            var player = new RemotePlayer
            {
                PlayerId = AssignPlayerId(peer.Value, out bool reconnecting),
                Peer = peer,
                SteamId = peer.Value,
                Name = request.PlayerName,
                ReturningGuest = GuestProfileStore.TryGet(peer.Value, out _, out _),
            };

            // Every (re)admission restarts the remote's per-subsystem intent counters (its
            // sync state clears on scene/menu transitions), while our per-player dedup
            // latches would survive — dropping every report from the returning player as
            // "stale" until it out-counted its previous life. Reset them for this slot.
            _passengerSeats.ForgetPlayer(player.PlayerId);
            Sync.WorldSyncManager.Instance?.OnPlayerAdmitted(player.PlayerId);

            SendAcceptedHandshake(peer, player.PlayerId);

            // Introduce existing players to the newcomer...
            foreach (var otherPlayer in _playersByPeer.Values)
            {
                SendTo(peer, new PlayerSpawn
                {
                    PlayerId = otherPlayer.PlayerId,
                    SteamId = otherPlayer.SteamId,
                    Name = otherPlayer.Name,
                }, Channel.ReliableOrdered);
            }

            // ...then the newcomer to everyone (including itself is harmless; clients filter their own id).
            _playersByPeer[peer] = player;
            Broadcast(new PlayerSpawn
            {
                PlayerId = player.PlayerId,
                SteamId = player.SteamId,
                Name = player.Name,
            }, Channel.ReliableOrdered);

            AddChatLine(reconnecting
                ? $"* {player.Name} reconnected"
                : $"* {player.Name} joined");
            PlayerJoined?.Invoke(player);

            if (IsHost && PlayerCount == 1)
                StatusText = $"{player.Name} joined — click Continue";

            // World snapshot is request-driven: the guest asks once its own world
            // scan completes (see WorldSnapshotRequest), not at handshake time —
            // at this point it is typically still in the main menu.
        }

        private void SendAcceptedHandshake(PeerId peer, byte playerId)
        {
            SendTo(peer, new HandshakeResponse
            {
                Accepted = true,
                PlayerId = playerId,
                HostPlayerName = LocalPlayerName,
                SessionFlags = BuildSessionFlags(),
            }, Channel.ReliableOrdered);
        }

        private void HandleSnapshotRequest(PeerId peer, WorldSnapshotRequest request)
        {
            var world = Sync.WorldSyncManager.Instance;
            if (world == null) return;
            if (!TryBeginHostRequest(_nextSnapshotRequestAt, peer, SnapshotRequestCooldownSeconds, "snapshot")) return;

            if (request.IdHash != world.IdHash)
                WinterMPPlugin.Log.LogWarning(
                    $"Snapshot request from {peer}: id hash {request.IdHash:X8} != ours {world.IdHash:X8} " +
                    "(usually transient — scans grow as the world streams in).");

            int messages = 0;
            foreach (var chunk in world.BuildWorldSnapshot())
            {
                SendTo(peer, chunk, Channel.ReliableOrdered);
                messages++;
            }

            var time = world.BuildTimeSync();
            if (time != null)
            {
                SendTo(peer, time, Channel.ReliableOrdered);
                messages++;
            }

            var wallet = world.BuildWalletState();
            if (wallet != null)
            {
                SendTo(peer, wallet, Channel.ReliableOrdered);
                messages++;
            }

            foreach (var occupancy in _passengerSeats.Occupants)
            {
                SendTo(peer, occupancy, Channel.ReliableOrdered);
                messages++;
            }

            if (_playersByPeer.TryGetValue(peer, out var joining))
            {
                SendTo(peer, BuildGuestSpawn(joining), Channel.ReliableOrdered);
                messages++;

                // Clothing is change-only and not in the chunked snapshot; send the joiner
                // every other player's current outfit so already-dressed players don't render
                // in default clothing (wrong warmth tier + visual) until each next changes.
                foreach (var clothing in world.BuildClothingSnapshot(LocalPlayerId, joining.PlayerId))
                {
                    SendTo(peer, clothing, Channel.ReliableOrdered);
                    messages++;
                }
            }

            // Heat sources ride the periodic HeatSourceState stream, not snapshot chunks;
            // force a full re-broadcast so the joiner isn't cold until the 20 s keepalive.
            world.ForceHeatSourceBroadcast();
            // Gambling devices (slot machines) likewise ride their own periodic stream.
            world.ForceGamblingBroadcast();
            // Utility bill ledger + blackout ride their own periodic stream too.
            world.ForceUtilityBillBroadcast();
            // Lottery draw rides its own periodic stream too.
            world.ForceLotteryBroadcast();

            WinterMPPlugin.Log.LogInfo($"Sent world snapshot to {peer} ({messages} messages).");
        }

        private byte AssignPlayerId(ulong steamId, out bool reconnecting)
        {
            reconnecting = false;
            if (steamId == 0)
                return AllocateFreshPlayerId();

            if (_guestSlotsBySteam.TryGetValue(steamId, out GuestSlot slot))
            {
                reconnecting = slot.Disconnected;
                slot.Disconnected = false;
                return slot.PlayerId;
            }

            byte id = AllocateFreshPlayerId();
            _guestSlotsBySteam[steamId] = new GuestSlot { PlayerId = id, Disconnected = false };
            return id;
        }

        private byte AllocateFreshPlayerId()
        {
            byte id = _nextPlayerId++;
            if (id == 0) id = _nextPlayerId++;
            return id;
        }

        private static GuestSpawn BuildGuestSpawn(RemotePlayer guest)
        {
            var offer = new GuestSpawn();

            if (TryReadHostFeet(out Vector3 hostFeet, out Quaternion hostRot))
            {
                offer.HostPosition = hostFeet.ToNet();
                offer.HostRotation = hostRot.ToNet();
            }

            if (guest.ReturningGuest
                && GuestProfileStore.TryGet(guest.SteamId, out NetVector3 lastPos, out NetQuaternion lastRot))
            {
                offer.LastPosition = lastPos;
                offer.LastRotation = lastRot;
                offer.Flags |= GuestSpawn.FlagHasLastPosition;
            }

            if (guest.ReturningGuest
                && GuestProfileStore.TryGetNeeds(guest.SteamId, out GuestProfileStore.NeedsSnapshot needs))
            {
                offer.Hunger = needs.Hunger;
                offer.Fatigue = needs.Fatigue;
                offer.Thirst = needs.Thirst;
                offer.Urine = needs.Urine;
                offer.BodyTemp = needs.BodyTemp;
                offer.Stress = needs.Stress;
                offer.Drunk = needs.Drunk;
                offer.Dirtiness = needs.Dirtiness;
                offer.PlayerAlco = needs.PlayerAlco;
                offer.Flags |= GuestSpawn.FlagHasSavedNeeds;
                if (needs.HasDirtiness)
                    offer.Flags |= GuestSpawn.FlagHasSavedDirtiness;
                if (needs.HasAlco)
                    offer.Flags |= GuestSpawn.FlagHasSavedAlco;
            }

            return offer;
        }

        private bool HandlePlayerNeedsReport(PlayerNeedsReport report)
        {
            if (!HasFiniteNeeds(report)) return false;

            foreach (var player in _playersByPeer.Values)
            {
                if (player.PlayerId != report.PlayerId || player.SteamId == 0) continue;

                if (player.HasNeedsReport)
                {
                    ushort difference = (ushort)(report.Sequence - player.LastNeedsSequence);
                    if (difference == 0 || difference > short.MaxValue) return false;
                }
                player.LastNeedsSequence = report.Sequence;
                player.HasNeedsReport = true;

                GuestProfileStore.RememberNeeds(player.SteamId, new GuestProfileStore.NeedsSnapshot
                {
                    Hunger = report.Hunger,
                    Fatigue = report.Fatigue,
                    Thirst = report.Thirst,
                    Urine = report.Urine,
                    BodyTemp = report.BodyTemp,
                    Stress = report.Stress,
                    Drunk = report.Drunk,
                    Dirtiness = report.Dirtiness,
                    HasDirtiness = report.HasDirtiness,
                    PlayerAlco = report.PlayerAlco,
                    HasAlco = report.HasAlco,
                    Valid = true,
                });
                return true;
            }
            return false;
        }

        private static bool HasFiniteNeeds(PlayerNeedsReport report)
        {
            return IsFinite(report.Hunger) && IsFinite(report.Fatigue) && IsFinite(report.Thirst)
                && IsFinite(report.Urine) && IsFinite(report.BodyTemp) && IsFinite(report.Stress)
                && IsFinite(report.Drunk) && IsFinite(report.Dirtiness);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool TryReadHostFeet(out Vector3 feet, out Quaternion lookRotation)
        {
            feet = Vector3.zero;
            lookRotation = Quaternion.identity;

            var playerObject = GameObject.Find("PLAYER");
            if (playerObject == null) return false;

            var player = playerObject.transform;
            var controller = playerObject.GetComponent<CharacterController>();
            feet = Sync.PlayerPoseReader.ReadFeetPosition(player, controller);
            lookRotation = Sync.PlayerPoseReader.ReadLookRotation(player);
            return true;
        }

        private void HandleResyncRequest(PeerId peer, WorldResyncRequest request)
        {
            var world = Sync.WorldSyncManager.Instance;
            if (world == null) return;
            if (!SessionMessagePolicy.IsValidResyncFlags(request.Flags))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"Dropped invalid soft-resync flags 0x{request.Flags:X2} from {peer}.");
                return;
            }
            if (!TryBeginHostRequest(_nextResyncRequestAt, peer, ResyncRequestCooldownSeconds, "soft resync")) return;

            int messages = 0;
            foreach (var chunk in world.BuildResyncMessages(request.Flags))
            {
                SendTo(peer, chunk, Channel.ReliableOrdered);
                messages++;
            }

            WinterMPPlugin.Log.LogInfo(
                $"Soft resync to {peer} for checksum seq {request.ChecksumSequence} flags 0x{request.Flags:X2} ({messages} messages).");
        }

        private static bool TryBeginHostRequest(Dictionary<PeerId, float> nextAllowedAt, PeerId peer,
            float cooldownSeconds, string requestName)
        {
            float now = Time.unscaledTime;
            if (nextAllowedAt.TryGetValue(peer, out float nextAt) && now < nextAt)
            {
                WinterMPPlugin.Log.LogDebug(
                    $"Dropped rate-limited {requestName} request from {peer} ({nextAt - now:0.0}s remaining).");
                return false;
            }

            nextAllowedAt[peer] = now + cooldownSeconds;
            return true;
        }

        private void HandleObjectStateRequest(PeerId peer, WorldObjectStateRequest request)
        {
            var world = Sync.WorldSyncManager.Instance;
            if (world == null) return;
            if (!TryBeginHostRequest(_nextObjectStateRequestAt, peer, ObjectStateRequestCooldownSeconds, "object state")) return;

            int messages = 0;
            foreach (var message in world.BuildObjectStateMessages(request.NetId))
            {
                Channel channel = message switch
                {
                    ItemTransform t => ItemTransformPolicy.SelectSendChannel(t.IsFinal, t.IsVehicle),
                    VehicleState => Channel.ReliableOrdered,
                    VehicleClimate => Channel.ReliableOrdered,
                    _ => Channel.ReliableOrdered,
                };
                SendTo(peer, message, channel);
                messages++;
            }

            if (messages > 0)
                WinterMPPlugin.Log.LogInfo($"Object state for {request.NetId:X8} -> {peer} ({messages} messages).");
        }

        private void HandleHandshakeResponse(PeerId peer, HandshakeResponse response)
        {
            if (!response.Accepted)
            {
                string reason = response.Reason ?? "unknown reason";
                AddChatLine($"* Join refused: {reason}");
                FailSession($"Join refused: {reason}");
                WinterMPPlugin.Log.LogWarning($"Join refused by host: {reason}");
                return;
            }

            LocalPlayerId = response.PlayerId;
            SetPermanentDeathEnabled((response.SessionFlags & SessionFlags.PermadeathEnabled) != 0);
            _playersByPeer[peer] = new RemotePlayer
            {
                PlayerId = 0,
                Peer = peer,
                SteamId = peer.Value,
                Name = response.HostPlayerName,
            };
            SetState(SessionState.Connected, $"Connected to {response.HostPlayerName}");
            AddChatLine($"* Connected to {response.HostPlayerName}'s game");
            if (PermanentDeathEnabled)
                AddChatLine("* Host session: PERMADEATH (one death ends it for everyone)");
        }

        private void HandleChat(PeerId peer, ChatMessage chat)
        {
            string sender = ResolveName(chat.SenderPlayerId);
            AddChatLine($"{sender}: {chat.Text}");

            // Host relays guest chat to all other guests.
            if (IsHost)
                Broadcast(chat, Channel.ReliableOrdered, except: peer);
        }

        private void HandlePlayerTransform(PeerId peer, PlayerTransform transform)
        {
            foreach (var player in _playersByPeer.Values)
            {
                if (player.PlayerId != transform.PlayerId) continue;

                // Drop stale unreliable packets (sequence wrap-aware). Always accept the
                // first pose so avatars appear even after a sender sequence reset.
                if (player.LastTransformTime > 0f)
                {
                    ushort diff = (ushort)(transform.Sequence - player.LastTransformSequence);
                    if (diff == 0 || diff > short.MaxValue) return;
                }

                player.LastTransformSequence = transform.Sequence;
                player.Position = transform.Position.ToUnity();
                player.Rotation = transform.Rotation.ToUnity();
                player.MoveState = transform.MoveState;
                player.LastTransformTime = Time.unscaledTime;

                if (IsHost && player.SteamId != 0)
                    GuestProfileStore.Remember(player.SteamId, transform.Position, transform.Rotation);
                break;
            }

            // Host relays transforms so all guests see all players.
            if (IsHost)
                Broadcast(transform, Channel.UnreliableSequenced, except: peer);
        }

        /// <summary>
        /// Player poses are presentation data, but the host also uses a fresh pose
        /// as the physical-presence proof for validated intents. Reject malformed
        /// values before they can poison remote transforms, sidecar saves, or an
        /// intent's proximity comparison. The normal local sender always produces a
        /// unit quaternion and only defines the seven documented animation bits.
        /// </summary>
        private static bool HasValidPlayerTransform(PlayerTransform transform)
        {
            const byte KnownMoveStateFlags = Sync.PlayerMoveState.Walking
                | Sync.PlayerMoveState.Running
                | Sync.PlayerMoveState.Crouch
                | Sync.PlayerMoveState.Carry
                | Sync.PlayerMoveState.Driving
                | Sync.PlayerMoveState.Passenger
                | Sync.PlayerMoveState.Swimming;
            const float MaxCoordinate = 100000f;

            if (!IsFinite(transform.Position.X) || !IsFinite(transform.Position.Y) || !IsFinite(transform.Position.Z)
                || !IsFinite(transform.Rotation.X) || !IsFinite(transform.Rotation.Y)
                || !IsFinite(transform.Rotation.Z) || !IsFinite(transform.Rotation.W)
                || Mathf.Abs(transform.Position.X) > MaxCoordinate || Mathf.Abs(transform.Position.Y) > MaxCoordinate
                || Mathf.Abs(transform.Position.Z) > MaxCoordinate || (transform.MoveState & ~KnownMoveStateFlags) != 0)
            {
                return false;
            }

            float rotationLengthSquared = transform.Rotation.X * transform.Rotation.X
                + transform.Rotation.Y * transform.Rotation.Y
                + transform.Rotation.Z * transform.Rotation.Z
                + transform.Rotation.W * transform.Rotation.W;
            return rotationLengthSquared >= 0.25f && rotationLengthSquared <= 2.25f;
        }

        private void HandleGuestPassengerState(PeerId peer, PassengerState request)
        {
            var decision = _passengerSeats.Apply(request, ValidatePassengerSeat);
            if (decision == null) return;

            if (decision.Evicted != null)
            {
                foreach (var pair in _playersByPeer)
                {
                    if (pair.Value.PlayerId != decision.Evicted.PlayerId) continue;
                    SendTo(pair.Key, decision.Evicted, Channel.ReliableOrdered);
                    break;
                }
            }

            if (!decision.Accepted)
            {
                WinterMPPlugin.Log.LogWarning(
                    $"Passenger: rejected claim from player {request.PlayerId}, vehicle {request.VehicleId:X8}, " +
                    $"seat {request.SeatIndex}, seq {request.Sequence}; cleared occupancy for all peers.");
                Diagnostics.SyncEventLog.Record("passenger", $"reject player {request.PlayerId} seq {request.Sequence}");
            }

            Sync.PassengerController.Instance?.OnRemotePassengerState(decision.State);
            // A rejection changes canonical occupancy as well as the claimant's local
            // state. Observers and join snapshots must not retain a ghost passenger.
            if (decision.Accepted)
                Broadcast(decision.State, Channel.ReliableOrdered, except: peer);
            else
                Broadcast(decision.State, Channel.ReliableOrdered);
        }

        private bool ValidatePassengerSeat(PassengerState state, bool continuing)
        {
            var passengers = Sync.PassengerController.Instance;
            if (passengers == null) return false;
            foreach (var player in _playersByPeer.Values)
            {
                if (player.PlayerId == state.PlayerId)
                    return passengers.TryValidateGuestPassengerState(state, player, continuing);
            }
            return false;
        }

        /// <summary>Guest-originated owner fields must match the authenticated peer.</summary>
        private bool IsPeerPlayer(PeerId peer, byte playerId)
        {
            return _playersByPeer.TryGetValue(peer, out var player) && player.PlayerId == playerId;
        }
    }
}
