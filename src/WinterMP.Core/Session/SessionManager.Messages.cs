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
                HandleMessage(peer, message, channel);
            }
            catch (Exception e)
            {
                // Crash containment: a bug in one handler must not take down the game loop.
                WinterMPPlugin.Log.LogError($"Error handling {message.Id} from {peer}: {e}");
            }
        }

        // ---------------------------------------------------------------- message handling

        private void HandleMessage(PeerId peer, IMessage message, Channel channel)
        {
            switch (message)
            {
                case SessionSettings settings when !IsHost:
                    SetPermanentDeathEnabled((settings.Flags & SessionFlags.PermadeathEnabled) != 0);
                    break;
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
                        // A (re)joining player restarts its per-subsystem counters; this
                        // client's stale latches for that slot must not drop its streams.
                        Sync.WorldSyncManager.Instance?.OnPlayerAdmitted(spawn.PlayerId);
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
                    if (IsHost)
                        HandleGuestPassengerState(peer, passengerState);
                    else
                        Sync.PassengerController.Instance?.OnRemotePassengerState(passengerState);
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
                        if (Sync.DeathSyncManager.Instance?.OnRemoteRespawn(respawn) == true)
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
                    if (TryGetPlayerId(peer, out byte rawEventPlayerId))
                        Sync.WorldSyncManager.Instance?.OnHostGuestRawEvent(rawEvent, rawEventPlayerId);
                    break;

                case FsmRawEvent rawEvent:
                    // v114 distributes settled bolt state, never relative turns from the host.
                    break;

                case RadiatorThermostatState thermostatState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteRadiatorThermostatState(thermostatState);
                    break;

                case BoltState boltState when IsHost:
                    if (TryGetPlayerId(peer, out byte boltPlayerId))
                        Sync.WorldSyncManager.Instance?.OnHostGuestBoltState(boltState, boltPlayerId);
                    break;

                case BoltState boltState:
                    Sync.WorldSyncManager.Instance?.OnRemoteBoltState(boltState);
                    break;

                case FirewoodLoadState woodLoad when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnFirewoodLoad(woodLoad);
                    break;
                case FirewoodUnloadIntent unload when IsHost:
                    if (IsPeerPlayer(peer, unload.PlayerId)) Sync.WorldSyncManager.Instance?.OnFirewoodUnload(unload);
                    break;
                case FirewoodBuyerState buyerState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnFirewoodBuyer(buyerState);
                    break;
                case MooseCorpseState corpse when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnMooseCorpse(corpse);
                    break;
                case MooseChopIntent chop when IsHost:
                    if (IsPeerPlayer(peer, chop.PlayerId)) Sync.WorldSyncManager.Instance?.OnMooseChop(chop);
                    break;
                case MooseMeatState meatState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnMeatState(meatState);
                    break;
                case AtfBottleState atfBottle when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnAtfBottleState(atfBottle);
                    break;
                case AtfFillerState atfFiller when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnAtfFillerState(atfFiller);
                    break;
                case AtfRefillIntent atfIntent when IsHost:
                    if (IsPeerPlayer(peer, atfIntent.PlayerId)) Sync.WorldSyncManager.Instance?.OnAtfIntent(atfIntent);
                    break;
                case MilkConditionState milkState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnMilkCondition(milkState);
                    break;
                case CylinderHeadState headState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnCylinderHeadState(headState);
                    break;
                case ValveAdjustmentState valveState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteValveState(valveState);
                    break;

                case PartState partState when IsHost:
                    if (TryGetPlayerId(peer, out byte partPlayerId))
                        Sync.WorldSyncManager.Instance?.OnHostGuestPartState(partState, partPlayerId);
                    break;

                case PartState partState:
                    Sync.WorldSyncManager.Instance?.OnRemotePartState(partState);
                    break;

                case ItemDespawn itemDespawn when IsHost:
                    if (TryGetPlayerId(peer, out byte despawnPlayerId)
                        && Sync.WorldSyncManager.Instance?.OnHostGuestItemDespawn(itemDespawn, despawnPlayerId) == true)
                        Broadcast(itemDespawn, Channel.ReliableOrdered);
                    break;

                case ItemDespawn itemDespawn:
                    Sync.WorldSyncManager.Instance?.OnRemoteItemDespawn(itemDespawn);
                    break;

                case ItemSpawn itemSpawn when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteItemSpawn(itemSpawn);
                    break;

                case ItemTransform itemTransform when IsHost:
                {
                    if (!IsPeerPlayer(peer, itemTransform.OwnerPlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped ItemTransform claiming player {itemTransform.OwnerPlayerId} from {peer}.");
                        break;
                    }
                    var itemWorld = Sync.WorldSyncManager.Instance;
                    if (itemWorld != null && itemWorld.OnHostGuestItemTransform(itemTransform, itemTransform.OwnerPlayerId, out var releaseConfirmation))
                    {
                        Broadcast(itemTransform,
                            ItemTransformPolicy.SelectSendChannel(itemTransform.IsFinal, itemTransform.IsVehicle),
                            except: peer);
                        if (releaseConfirmation != null) SendTo(peer, releaseConfirmation, Channel.ReliableOrdered);
                    }
                    else
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped unauthorized ItemTransform {itemTransform.ItemId:X8} from player {itemTransform.OwnerPlayerId}.");
                    break;
                }

                case ItemTransform itemTransform:
                    Sync.WorldSyncManager.Instance?.OnRemoteItemTransform(itemTransform);
                    break;

                case NpcTransform npcTransform when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteNpcTransform(npcTransform);
                    break;

                case NpcDeathReport npcDeathReport when IsHost:
                    if (!IsPeerPlayer(peer, npcDeathReport.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped NpcDeathReport claiming player {npcDeathReport.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.WorldSyncManager.Instance?.OnHostNpcDeathReport(this, npcDeathReport);
                    break;

                case VehicleState vehicleState when IsHost:
                    if (!IsPeerPlayer(peer, vehicleState.OwnerPlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped VehicleState claiming player {vehicleState.OwnerPlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostGuestVehicleState(vehicleState, vehicleState.OwnerPlayerId) == true)
                        Broadcast(vehicleState, channel, except: peer);
                    else
                        WinterMPPlugin.Log.LogWarning($"Dropped unauthorized VehicleState {vehicleState.VehicleId:X8}.");
                    break;

                case VehicleState vehicleState:
                    Sync.WorldSyncManager.Instance?.OnRemoteVehicleState(vehicleState);
                    break;

                case VehicleDamage vehicleDamage when IsHost:
                    WinterMPPlugin.Log.LogWarning($"Dropped guest VehicleDamage {vehicleDamage.VehicleId:X8}; engine damage belongs to the host.");
                    break;

                case VehicleDamage vehicleDamage:
                    Sync.WorldSyncManager.Instance?.OnRemoteVehicleDamage(vehicleDamage);
                    break;

                case VehicleCondition vehicleCondition when IsHost:
                    if (!IsPeerPlayer(peer, vehicleCondition.OwnerPlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped VehicleCondition claiming player {vehicleCondition.OwnerPlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostGuestVehicleCondition(vehicleCondition, vehicleCondition.OwnerPlayerId) == true)
                        Broadcast(vehicleCondition, Channel.ReliableOrdered, except: peer);
                    else
                        WinterMPPlugin.Log.LogWarning($"Dropped unauthorized VehicleCondition {vehicleCondition.VehicleId:X8}.");
                    break;

                case VehicleCondition vehicleCondition:
                    Sync.WorldSyncManager.Instance?.OnRemoteVehicleCondition(vehicleCondition);
                    break;

                case VehicleConditionReleaseAck conditionAck when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnVehicleConditionReleaseAck(conditionAck);
                    break;

                case VehicleClimate vehicleClimate when IsHost:
                    if (!IsPeerPlayer(peer, vehicleClimate.OwnerPlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped VehicleClimate claiming player {vehicleClimate.OwnerPlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostGuestVehicleClimate(vehicleClimate, vehicleClimate.OwnerPlayerId) == true)
                        Broadcast(vehicleClimate, channel, except: peer);
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

                case BankTransferIntent bankTransfer when IsHost:
                    if (IsPeerPlayer(peer, bankTransfer.PlayerId))
                        Sync.WorldSyncManager.Instance?.OnBankTransfer(bankTransfer);
                    break;

                case BankTransferResult bankResult when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnBankResult(bankResult);
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

                case VenttiTableState tableState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteVenttiTableState(tableState);
                    break;

                case VenttiPropertyState propertyState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteVenttiPropertyState(propertyState);
                    break;

                case DisconnectMessage disconnect:
                    OnPeerDisconnected(peer, disconnect.Reason);
                    break;

                default:
                    if (!HandleWorldMessage(peer, message))
                        WinterMPPlugin.Log.LogDebug($"Unhandled message {message.Id} from {peer}.");
                    break;
            }
        }
    }
}
