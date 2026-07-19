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
        private void OnPacketReceived(PeerId peer, byte[] payload, Channel channel)
        {
            if (payload == null)
            {
                WinterMPPlugin.Log.LogWarning($"Dropped null packet from {peer}.");
                return;
            }

            if (State != SessionState.Hosting && State != SessionState.Connecting && State != SessionState.Connected)
            {
                WinterMPPlugin.Log.LogDebug($"Dropped packet from {peer} while {State}.");
                return;
            }

            if (!SessionMessagePolicy.IsKnownChannel(channel))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"Dropped packet from {peer} on invalid channel {(byte)channel}.");
                return;
            }

            NetTrafficMeter.Instance.RecordReceived(payload.Length);

            IMessage message;
            try
            {
                message = PacketCodec.Decode(payload);
            }
            catch (ProtocolException e)
            {
                WinterMPPlugin.Log.LogWarning($"Dropped malformed packet from {peer}: {e.Message}");
                return;
            }

            bool senderIsAuthenticated = _playersByPeer.ContainsKey(peer);
            bool senderIsSelectedHost = _hostPeer.HasValue && _hostPeer.Value == peer;
            if (!SessionMessagePolicy.IsSenderAllowed(
                    message.Id, IsHost, senderIsAuthenticated, senderIsSelectedHost,
                    receiverHandshakeComplete: State == SessionState.Connected))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"Dropped {message.Id} from unauthorised peer {peer}.");
                return;
            }

            if (!SessionMessagePolicy.IsChannelAllowed(message.Id, channel))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"Dropped {message.Id} from {peer} on unexpected channel {(byte)channel}.");
                return;
            }

            try
            {
                HandleMessage(peer, message);
            }
            catch (Exception e)
            {
                // Crash containment: a bug in one handler must not take down the game loop.
                WinterMPPlugin.Log.LogError($"Error handling {message.Id} from {peer}: {e}");
            }
        }

        // ---------------------------------------------------------------- message handling

        private void HandleMessage(PeerId peer, IMessage message)
        {
            switch (message)
            {
                case HandshakeRequest request when IsHost:
                    HandleHandshakeRequest(peer, request);
                    break;

                case HandshakeResponse response when !IsHost && State == SessionState.Connecting:
                    HandleHandshakeResponse(peer, response);
                    break;

                case HandshakeResponse when !IsHost:
                    WinterMPPlugin.Log.LogWarning($"Dropped unexpected handshake response from {peer} while {State}.");
                    break;

                case ChatMessage chat when IsHost:
                    if (TryGetPlayerId(peer, out byte chatPlayerId))
                    {
                        chat.SenderPlayerId = chatPlayerId;
                        HandleChat(peer, chat);
                    }
                    break;

                case ChatMessage chat:
                    HandleChat(peer, chat);
                    break;

                case PingMessage ping:
                    SendTo(peer, new PongMessage { Nonce = ping.Nonce, SenderTimeMs = ping.SenderTimeMs }, Channel.ReliableOrdered);
                    break;

                case PongMessage pong:
                    if (_pendingPings.TryGetValue(pong.Nonce, out float sentAt))
                    {
                        _pendingPings.Remove(pong.Nonce);
                        if (_playersByPeer.TryGetValue(peer, out var pingedPlayer))
                            pingedPlayer.PingMs = (int)((Time.unscaledTime - sentAt) * 1000f);
                    }
                    break;

                case PlayerSpawn spawn when !IsHost:
                    if (spawn.PlayerId != LocalPlayerId)
                    {
                        var remote = new RemotePlayer
                        {
                            PlayerId = spawn.PlayerId,
                            Peer = peer, // clients only talk to the host; host relays
                            SteamId = spawn.SteamId,
                            Name = spawn.Name,
                        };
                        _playersByPeer[new PeerId(spawn.SteamId != 0 ? spawn.SteamId : spawn.PlayerId)] = remote;
                        AddChatLine($"* {remote.Name} joined");
                        PlayerJoined?.Invoke(remote);
                    }
                    break;

                case PlayerDespawn despawn when !IsHost:
                    RemovePlayerById(despawn.PlayerId, despawn.Reason);
                    break;

                case PlayerTransform transform:
                    if (!HasValidPlayerTransform(transform))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped malformed PlayerTransform from {peer} for player {transform.PlayerId}.");
                        break;
                    }
                    if (IsHost && !IsPeerPlayer(peer, transform.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped PlayerTransform claiming player {transform.PlayerId} from {peer}.");
                        break;
                    }
                    HandlePlayerTransform(peer, transform);
                    break;

                case PassengerState passengerState:
                    if (IsHost && !IsPeerPlayer(peer, passengerState.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped PassengerState claiming player {passengerState.PlayerId} from {peer}.");
                        break;
                    }
                    if (IsHost && !TryAcceptGuestPassengerState(peer, passengerState))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped invalid or stale PassengerState from player {passengerState.PlayerId}.");
                        SendTo(peer, new PassengerState
                        {
                            PlayerId = passengerState.PlayerId,
                            VehicleId = 0,
                            SeatIndex = PassengerState.SeatNone,
                            Sequence = passengerState.Sequence,
                        }, Channel.ReliableOrdered);
                        break;
                    }
                    RecordPassengerState(passengerState);
                    Sync.PassengerController.Instance?.OnRemotePassengerState(passengerState);
                    if (IsHost)
                        Broadcast(passengerState, Channel.ReliableOrdered, except: peer);
                    break;

                case GuestSpawn guestSpawn when !IsHost:
                    Sync.PlayerSyncManager.Instance?.OnGuestSpawn(guestSpawn);
                    break;

                case PlayerNeedsReport needsReport when IsHost:
                    if (!IsPeerPlayer(peer, needsReport.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped PlayerNeedsReport claiming player {needsReport.PlayerId} from {peer}.");
                        break;
                    }
                    if (!HandlePlayerNeedsReport(needsReport))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped invalid or stale PlayerNeedsReport from player {needsReport.PlayerId}.");
                    }
                    break;

                case SleepConsentRequest sleepRequest when !IsHost:
                    Sync.SleepConsentManager.Instance?.OnGuestRequest(sleepRequest);
                    break;

                case SleepConsentResponse sleepResponse when IsHost:
                    if (!IsPeerPlayer(peer, sleepResponse.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped SleepConsentResponse claiming player {sleepResponse.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.SleepConsentManager.Instance?.OnRemoteResponse(sleepResponse);
                    break;

                case SleepConsentResult sleepResult when !IsHost:
                    Sync.SleepConsentManager.Instance?.OnGuestResult(sleepResult);
                    break;

                case PlayerDeathReport deathReport when IsHost:
                    // Host-authority: a guest may only report its OWN death. Bind the report to the
                    // authenticated sender so a spoofed PlayerId (e.g. 0/host, or another player)
                    // cannot drive a permadeath wipe of someone who never died. Drop if unknown.
                    if (TryGetPlayerId(peer, out byte deathReporterId))
                    {
                        deathReport.PlayerId = deathReporterId;
                        Sync.DeathSyncManager.Instance?.OnRemoteDeathReport(deathReport);
                    }
                    break;

                case PlayerDeathEvent deathEvent when !IsHost:
                    Sync.DeathSyncManager.Instance?.OnRemoteDeathEvent(deathEvent);
                    break;

                case PlayerRespawn respawn when IsHost:
                    // Bind respawn to its sender: a guest may only respawn itself.
                    if (TryGetPlayerId(peer, out byte respawnerId))
                    {
                        respawn.PlayerId = respawnerId;
                        Sync.DeathSyncManager.Instance?.OnRemoteRespawn(respawn);
                        Broadcast(respawn, Channel.ReliableOrdered, except: peer);
                    }
                    break;

                case PlayerRespawn respawn when !IsHost:
                    Sync.DeathSyncManager.Instance?.OnRemoteRespawn(respawn);
                    break;

                case PlayerClothingState clothingState:
                    if (IsHost && !IsPeerPlayer(peer, clothingState.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped PlayerClothingState claiming player {clothingState.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.WorldSyncManager.Instance?.OnRemoteClothingState(clothingState);
                    if (IsHost)
                        Broadcast(clothingState, Channel.ReliableOrdered, except: peer);
                    break;

                case FsmStateEnter stateEnter when IsHost:
                {
                    RadiatorThermostatState? thermostatState = null;
                    var world = Sync.WorldSyncManager.Instance;
                    if (TryGetPlayerId(peer, out byte fsmPlayerId)
                        && world != null
                        && world.OnHostGuestStateEnter(stateEnter, fsmPlayerId, out thermostatState))
                    {
                        if (thermostatState != null)
                            Broadcast(thermostatState, Channel.ReliableOrdered);
                        else
                            Broadcast(stateEnter, Channel.ReliableOrdered, except: peer);
                    }
                    break;
                }

                case FsmStateEnter stateEnter:
                    Sync.WorldSyncManager.Instance?.OnRemoteStateEnter(stateEnter);
                    break;

                case FsmRawEvent rawEvent when IsHost:
                    if (TryGetPlayerId(peer, out byte rawEventPlayerId)
                        && Sync.WorldSyncManager.Instance?.OnHostGuestRawEvent(rawEvent, rawEventPlayerId) == true)
                        Broadcast(rawEvent, Channel.ReliableOrdered, except: peer);
                    break;

                case FsmRawEvent rawEvent:
                    Sync.WorldSyncManager.Instance?.OnRemoteRawEvent(rawEvent);
                    break;

                case RadiatorThermostatState thermostatState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteRadiatorThermostatState(thermostatState);
                    break;

                case BoltState boltState when IsHost:
                    if (TryGetPlayerId(peer, out byte boltPlayerId)
                        && Sync.WorldSyncManager.Instance?.OnHostGuestBoltState(boltState, boltPlayerId) == true)
                        Broadcast(boltState, Channel.ReliableOrdered, except: peer);
                    break;

                case BoltState boltState:
                    Sync.WorldSyncManager.Instance?.OnRemoteBoltState(boltState);
                    break;

                case PartState partState when IsHost:
                    if (TryGetPlayerId(peer, out byte partPlayerId)
                        && Sync.WorldSyncManager.Instance?.OnHostGuestPartState(partState, partPlayerId) == true)
                        Broadcast(partState, Channel.ReliableOrdered, except: peer);
                    break;

                case PartState partState:
                    Sync.WorldSyncManager.Instance?.OnRemotePartState(partState);
                    break;

                case ItemDespawn itemDespawn when IsHost:
                    if (TryGetPlayerId(peer, out byte despawnPlayerId)
                        && Sync.WorldSyncManager.Instance?.OnHostGuestItemDespawn(itemDespawn, despawnPlayerId) == true)
                        Broadcast(itemDespawn, Channel.ReliableOrdered, except: peer);
                    break;

                case ItemDespawn itemDespawn:
                    Sync.WorldSyncManager.Instance?.OnRemoteItemDespawn(itemDespawn);
                    break;

                case ItemSpawn itemSpawn when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteItemSpawn(itemSpawn);
                    break;

                case SpawnIntent spawnIntent when IsHost:
                    if (!IsPeerPlayer(peer, spawnIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped SpawnIntent claiming player {spawnIntent.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.WorldSyncManager.Instance?.OnHostGuestSpawnIntent(spawnIntent, spawnIntent.PlayerId);
                    break;

                case ItemTransform itemTransform when IsHost:
                    if (!IsPeerPlayer(peer, itemTransform.OwnerPlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped ItemTransform claiming player {itemTransform.OwnerPlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostGuestItemTransform(itemTransform, itemTransform.OwnerPlayerId) == true)
                        Broadcast(itemTransform,
                            ItemTransformPolicy.SelectSendChannel(itemTransform.IsFinal, itemTransform.IsVehicle),
                            except: peer);
                    else
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped unauthorized ItemTransform {itemTransform.ItemId:X8} from player {itemTransform.OwnerPlayerId}.");
                    break;

                case ItemTransform itemTransform:
                    Sync.WorldSyncManager.Instance?.OnRemoteItemTransform(itemTransform);
                    break;

                case NpcTransform npcTransform when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteNpcTransform(npcTransform);
                    break;

                case VehicleState vehicleState when IsHost:
                    if (!IsPeerPlayer(peer, vehicleState.OwnerPlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped VehicleState claiming player {vehicleState.OwnerPlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostGuestVehicleState(vehicleState, vehicleState.OwnerPlayerId) == true)
                        Broadcast(vehicleState, Channel.UnreliableSequenced, except: peer);
                    else
                        WinterMPPlugin.Log.LogWarning($"Dropped unauthorized VehicleState {vehicleState.VehicleId:X8}.");
                    break;

                case VehicleState vehicleState:
                    Sync.WorldSyncManager.Instance?.OnRemoteVehicleState(vehicleState);
                    break;

                case VehicleClimate vehicleClimate when IsHost:
                    if (!IsPeerPlayer(peer, vehicleClimate.OwnerPlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped VehicleClimate claiming player {vehicleClimate.OwnerPlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostGuestVehicleClimate(vehicleClimate, vehicleClimate.OwnerPlayerId) == true)
                        Broadcast(vehicleClimate, Channel.UnreliableSequenced, except: peer);
                    else
                        WinterMPPlugin.Log.LogWarning($"Dropped unauthorized VehicleClimate {vehicleClimate.VehicleId:X8}.");
                    break;

                case VehicleClimate vehicleClimate:
                    Sync.WorldSyncManager.Instance?.OnRemoteVehicleClimate(vehicleClimate);
                    break;

                case VehicleCargo vehicleCargo when IsHost:
                    if (!IsPeerPlayer(peer, vehicleCargo.OwnerPlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped VehicleCargo claiming player {vehicleCargo.OwnerPlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostGuestVehicleCargo(vehicleCargo, vehicleCargo.OwnerPlayerId) == true)
                    {
                        // Empty-set transitions ride the reliable channel end to end —
                        // losing one on relay would strand pinned cargo on other guests.
                        Broadcast(vehicleCargo,
                            vehicleCargo.Entries.Length == 0
                                ? Channel.ReliableOrdered
                                : Channel.UnreliableSequenced,
                            except: peer);
                    }
                    break;

                case VehicleCargo vehicleCargo:
                    Sync.WorldSyncManager.Instance?.OnRemoteVehicleCargo(vehicleCargo);
                    break;

                case VehicleFuelIntent fuelIntent when IsHost:
                    if (!IsPeerPlayer(peer, fuelIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped VehicleFuelIntent claiming player {fuelIntent.PlayerId} from {peer}.");
                        break;
                    }

                    if (Sync.WorldSyncManager.Instance?.OnHostVehicleFuelIntent(fuelIntent, out var fuelState) == true)
                        Broadcast(fuelState, Channel.ReliableOrdered);
                    break;

                case TimeSync timeSync when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteTimeSync(timeSync);
                    break;

                case WalletState walletState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteWalletState(walletState);
                    break;

                case PurchaseIntent purchaseIntent when IsHost:
                    if (!IsPeerPlayer(peer, purchaseIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped PurchaseIntent claiming player {purchaseIntent.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.WorldSyncManager.Instance?.OnHostGuestPurchaseIntent(purchaseIntent, purchaseIntent.PlayerId);
                    break;

                case PoliceIntent policeIntent when IsHost:
                    if (!IsPeerPlayer(peer, policeIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped PoliceIntent claiming player {policeIntent.PlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostPoliceIntent(policeIntent, out var policeState) == true)
                        Broadcast(policeState, Channel.ReliableOrdered);
                    break;

                case HeatSourceState heatState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteHeatSourceState(heatState);
                    break;

                case HeatSourceIntent heatIntent when IsHost:
                    if (!IsPeerPlayer(peer, heatIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped HeatSourceIntent claiming player {heatIntent.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.WorldSyncManager.Instance?.OnHostHeatSourceIntent(heatIntent);
                    break;

                case FluidContainerState fluidState when IsHost:
                    if (!IsPeerPlayer(peer, fluidState.OwnerPlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped FluidContainerState claiming player {fluidState.OwnerPlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostGuestFluidContainerState(
                            fluidState, fluidState.OwnerPlayerId) == true)
                    {
                        Broadcast(fluidState, Channel.ReliableOrdered, except: peer);
                    }
                    else
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped unauthorized FluidContainerState {fluidState.ItemId:X8} from player {fluidState.OwnerPlayerId}.");
                    }
                    break;

                case FluidContainerState fluidState:
                    Sync.WorldSyncManager.Instance?.OnRemoteFluidContainerState(fluidState);
                    break;

                case WorldProgressState progressState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteWorldProgressState(progressState);
                    break;

                case JobSiteState jobSiteState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteJobSiteState(jobSiteState);
                    break;

                case MailOrderState mailOrderState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteMailOrderState(mailOrderState);
                    break;

                case MailOrderIntent mailOrderIntent when IsHost:
                    if (!IsPeerPlayer(peer, mailOrderIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped MailOrderIntent claiming player {mailOrderIntent.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.WorldSyncManager.Instance?.OnHostMailOrderIntent(mailOrderIntent);
                    break;

                case InspectionState inspectionState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteInspectionState(inspectionState);
                    break;

                case PoliceState remotePoliceState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemotePoliceState(remotePoliceState);
                    break;

                case HomeStereoIntent homeStereoIntent when IsHost:
                    if (!IsPeerPlayer(peer, homeStereoIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped HomeStereoIntent claiming player {homeStereoIntent.PlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostHomeStereoIntent(homeStereoIntent, out var homeStereoState) == true)
                        Broadcast(homeStereoState, Channel.ReliableOrdered);
                    break;

                case HomeStereoState remoteHomeStereoState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteHomeStereoState(remoteHomeStereoState);
                    break;

                case RallyIntent rallyIntent when IsHost:
                    if (!IsPeerPlayer(peer, rallyIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped RallyIntent claiming player {rallyIntent.PlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostRallyIntent(rallyIntent, out var rallyState) == true)
                        Broadcast(rallyState, Channel.ReliableOrdered);
                    break;

                case RallyState remoteRallyState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteRallyState(remoteRallyState);
                    break;

                case IceRaceIntent iceRaceIntent when IsHost:
                    if (!IsPeerPlayer(peer, iceRaceIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped IceRaceIntent claiming player {iceRaceIntent.PlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostIceRaceIntent(iceRaceIntent, out var iceRaceState) == true)
                        Broadcast(iceRaceState, Channel.ReliableOrdered);
                    break;

                case IceRaceState remoteIceRaceState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteIceRaceState(remoteIceRaceState);
                    break;

                case IceRaceEventState iceRaceEventState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteIceRaceEventState(iceRaceEventState);
                    break;

                case IceRaceResultsState iceRaceResultsState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteIceRaceResultsState(iceRaceResultsState);
                    break;

                case WorldSnapshotRequest snapshotRequest when IsHost:
                    HandleSnapshotRequest(peer, snapshotRequest);
                    break;

                case WorldStateChecksum checksum when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteStateChecksum(checksum);
                    break;

                case WorldResyncRequest resync when IsHost:
                    HandleResyncRequest(peer, resync);
                    break;

                case WorldObjectStateRequest objectRequest when IsHost:
                    HandleObjectStateRequest(peer, objectRequest);
                    break;

                case WorldDoorSnapshot doorSnapshot when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteDoorSnapshot(doorSnapshot);
                    break;

                case WorldItemSnapshot itemSnapshot when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteItemSnapshot(itemSnapshot);
                    break;

                case WorldBoltSnapshot boltSnapshot when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteBoltSnapshot(boltSnapshot);
                    break;

                case WorldPartSnapshot partSnapshot when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemotePartSnapshot(partSnapshot);
                    break;

                case WorldItemDespawnSnapshot despawnSnapshot when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteItemDespawnSnapshot(despawnSnapshot);
                    break;

                case DisconnectMessage disconnect:
                    OnPeerDisconnected(peer, disconnect.Reason);
                    break;

                default:
                    WinterMPPlugin.Log.LogDebug($"Unhandled message {message.Id} from {peer}.");
                    break;
            }
        }
    }
}
