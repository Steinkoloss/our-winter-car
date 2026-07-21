using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    public sealed partial class WorldSyncManager
    {
        public WorldStateChecksum? BuildStateChecksum()
        {
            if (!_syncReady || _fsm.DoorCount == 0) return null;

            return new WorldStateChecksum
            {
                WalletCrc = ComputeWalletCrc(),
                WorldCrc = _fsm.ComputeWorldCrc(),
                ItemCrc = _items.ComputeItemCrc(),
                VehicleCrc = _items.ComputeVehicleCrc(),
                Sequence = ++_outChecksumSequence,
            };
        }

        public void OnRemoteStateChecksum(WorldStateChecksum message)
        {
            if (!_syncReady) return;

            var session = SessionManager.Instance;
            if (session == null || session.IsHost || !_snapshotRequested || _fsm.DoorCount == 0) return;
            if (Time.unscaledTime < _nextResyncRequestAt) return;

            uint localWallet = ComputeWalletCrc();
            uint localWorld = _fsm.ComputeWorldCrc();
            uint localItems = _items.ComputeItemCrc();
            uint localVehicles = _items.ComputeVehicleCrc();
            byte flags = 0;
            if (localWallet != message.WalletCrc) flags |= WorldResyncRequest.FlagWallet;
            if (localWorld != message.WorldCrc)
            {
                flags |= WorldResyncRequest.FlagFsmStates;
                flags |= WorldResyncRequest.FlagParts;
                flags |= WorldResyncRequest.FlagBolts;
            }

            if (localItems != message.ItemCrc) flags |= WorldResyncRequest.FlagItems;
            if (localVehicles != message.VehicleCrc) flags |= WorldResyncRequest.FlagVehicles;

            if (flags == 0) return;

            WinterMPPlugin.Log.LogWarning(
                $"WorldSync: checksum mismatch seq {message.Sequence} (wallet={(flags & WorldResyncRequest.FlagWallet) != 0}, " +
                $"world={(flags & WorldResyncRequest.FlagFsmStates) != 0}, items={(flags & WorldResyncRequest.FlagItems) != 0}, " +
                $"vehicles={(flags & WorldResyncRequest.FlagVehicles) != 0}) — requesting soft resync.");
            SyncEventLog.Record("checksum", $"seq {message.Sequence} flags 0x{flags:X2}");
            _nextResyncRequestAt = Time.unscaledTime + ResyncCooldownSeconds;
            session.SendWorldMessage(new WorldResyncRequest
            {
                Flags = flags,
                ChecksumSequence = message.Sequence,
            }, Channel.ReliableOrdered);
        }

        public IEnumerable<IMessage> BuildResyncMessages(byte flags)
        {
            if (!_syncReady) yield break;

            if ((flags & WorldResyncRequest.FlagWallet) != 0)
            {
                var wallet = BuildWalletState();
                if (wallet != null) yield return wallet;
            }

            if ((flags & WorldResyncRequest.FlagFsmStates) != 0)
            {
                foreach (var chunk in _fsm.BuildDoorSnapshotChunks())
                    yield return chunk;
                foreach (var thermostat in _fsm.BuildRadiatorThermostatStates())
                    yield return thermostat;
            }

            if ((flags & WorldResyncRequest.FlagParts) != 0)
            {
                foreach (var chunk in _fsm.BuildPartSnapshotChunks())
                    yield return chunk;
            }

            if ((flags & WorldResyncRequest.FlagBolts) != 0)
            {
                foreach (var chunk in _fsm.BuildBoltSnapshotChunks())
                    yield return chunk;
            }

            if ((flags & WorldResyncRequest.FlagItems) != 0)
            {
                foreach (var chunk in _items.BuildItemSnapshotChunks())
                    yield return chunk;
            }

            if ((flags & WorldResyncRequest.FlagVehicles) != 0)
            {
                foreach (var message in _vehicles.BuildVehicleResyncMessages())
                    yield return message;
            }
        }

        public void RequestObjectState(uint netId)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;

            float now = Time.unscaledTime;
            if (_nextObjectRequestAt.TryGetValue(netId, out float nextAt) && now < nextAt) return;

            _nextObjectRequestAt[netId] = now + ObjectRequestCooldownSeconds;
            WinterMPPlugin.Log.LogInfo($"WorldSync: requesting object state for {netId:X8}.");
            SyncEventLog.Record("obj-req", netId.ToString("X8"));
            session.SendWorldMessage(new WorldObjectStateRequest { NetId = netId }, Channel.ReliableOrdered);
        }

        public IEnumerable<IMessage> BuildObjectStateMessages(uint netId)
        {
            if (!_syncReady) yield break;

            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) yield break;

            if (_items.Items.TryGetValue(netId, out var item) && item.Body != null)
            {
                yield return new ItemTransform
                {
                    ItemId = netId,
                    OwnerPlayerId = session.LocalPlayerId,
                    Flags = ItemTransform.FlagFinal,
                    Position = item.Body.transform.position.ToNet(),
                    Rotation = item.Body.transform.rotation.ToNet(),
                };

                if (item.IsVehicle)
                {
                    foreach (var message in _vehicles.BuildVehicleStateMessages(item, session.LocalPlayerId))
                        yield return message;
                }

                yield break;
            }

            if (_fsm.TryBuildRadiatorThermostatState(netId, out var thermostat))
            {
                yield return thermostat;
                yield break;
            }

            string? fsmState = _fsm.TryGetFsmSnapshotState(netId);
            if (fsmState != null)
            {
                yield return new FsmStateEnter { NetId = netId, StateName = fsmState };
                yield break;
            }

            if (_fsm.TryGetPartSnapshot(netId, out byte flags, out byte tightness, out byte wear))
            {
                yield return new PartState { NetId = netId, Flags = flags, Tightness = tightness, Wear = wear };
                yield break;
            }

            if (_fsm.TryGetBoltSnapshot(netId, out ushort boltTightness, out ushort screwInt))
            {
                yield return new BoltState { NetId = netId, BoltTightness = boltTightness, ScrewInt = screwInt };
            }
        }

        public IEnumerable<IMessage> BuildWorldSnapshot()
        {
            if (!_syncReady) yield break;

            foreach (var chunk in _fsm.BuildDoorSnapshotChunks())
                yield return chunk;

            foreach (var thermostat in _fsm.BuildRadiatorThermostatStates())
                yield return thermostat;

            foreach (var chunk in _items.BuildItemSnapshotChunks())
                yield return chunk;

            foreach (var chunk in _fsm.BuildBoltSnapshotChunks())
                yield return chunk;

            foreach (var chunk in _fsm.BuildPartSnapshotChunks())
                yield return chunk;

            var despawns = new WorldItemDespawnSnapshot();
            foreach (uint itemId in _items.SessionDespawnedIds)
            {
                despawns.ItemIds.Add(itemId);
                if (despawns.ItemIds.Count >= DespawnSnapshotChunk)
                {
                    yield return despawns;
                    despawns = new WorldItemDespawnSnapshot();
                }
            }

            if (despawns.ItemIds.Count > 0)
                yield return despawns;

            foreach (var climate in _vehicles.BuildJoinClimateSnapshots())
                yield return climate;

            // After the item snapshot (adoption relies on the joiner having seen the
            // host-known id set first): re-send this session's spill manifests so a
            // late joiner materializes container-spawned items it can't ever scan.
            foreach (var spawn in _items.BuildSpawnReplayManifests())
                yield return spawn;

            foreach (var progress in _progress.BuildSnapshots())
                yield return progress;

            var session = SessionManager.Instance;
            byte ownerPlayerId = session != null ? session.LocalPlayerId : WorldSyncIds.NoOwner;
            foreach (var brew in _kilju.BuildSnapshots(ownerPlayerId))
                yield return brew;

            foreach (var fluid in _fluids.BuildSnapshots(ownerPlayerId))
                yield return fluid;

            foreach (var jobSite in _jobSites.BuildSnapshots())
                yield return jobSite;

            foreach (var mailOrder in _mailOrders.BuildSnapshots())
                yield return mailOrder;

            var fleetariOrder = _repairShop.BuildSnapshot();
            if (fleetariOrder != null)
                yield return fleetariOrder;

            var fleaSale = _fleaSale.BuildSnapshot();
            if (fleaSale != null)
                yield return fleaSale;

            var taxiJob = _taxiJob.BuildSnapshot();
            if (taxiJob != null)
                yield return taxiJob;

            var welfare = _welfare.BuildSnapshot();
            if (welfare != null)
                yield return welfare;

            var hitchhiker = _hitchhiker.BuildSnapshot();
            if (hitchhiker != null)
                yield return hitchhiker;

            var wanted = _wanted.BuildSnapshot();
            if (wanted != null)
                yield return wanted;

            var jail = _jail.BuildSnapshot();
            if (jail != null)
                yield return jail;

            var pursuit = _pursuit.BuildSnapshot();
            if (pursuit != null)
                yield return pursuit;

            var rallyResults = _rallyResults.BuildSnapshot();
            if (rallyResults != null)
                yield return rallyResults;

            var jokkis = _jokkis.BuildSnapshot();
            if (jokkis != null)
                yield return jokkis;

            foreach (var appliance in _appliances.BuildSnapshots())
                yield return appliance;

            var pissAreas = _pissAreas.BuildSnapshot();
            if (pissAreas != null)
                yield return pissAreas;

            foreach (var carRadio in _carRadio.BuildSnapshots())
                yield return carRadio;

            var inspection = _inspection.BuildSnapshot();
            if (inspection != null)
                yield return inspection;

            var police = _police.BuildSnapshot();
            if (police != null)
                yield return police;

            var homeStereo = _homeStereo.BuildSnapshot();
            if (homeStereo != null)
                yield return homeStereo;

            foreach (var rally in _rally.BuildSnapshots())
                yield return rally;

            foreach (var iceRace in _iceRace.BuildSnapshots())
                yield return iceRace;

            var iceRaceEvent = _iceRaceEvent.BuildSnapshot();
            if (iceRaceEvent != null)
                yield return iceRaceEvent;

            var iceRaceResults = _iceRaceResults.BuildSnapshot();
            if (iceRaceResults != null)
                yield return iceRaceResults;
        }
    }
}
