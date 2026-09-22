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
                var train = _train.Snapshot(); if (train != null) yield return train;
                var lotto = _lottery.BuildSnapshot();
                if (lotto != null) yield return lotto;
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
                if (_lottoTickets != null)
                    foreach (var ticket in _lottoTickets.BuildSnapshots()) yield return ticket;
                foreach (var chunk in _items.BuildItemSnapshotChunks())
                    yield return chunk;
                foreach (var chunk in BuildItemDespawnSnapshots()) yield return chunk;
                foreach (var meat in _items.BuildMeatStates()) yield return meat;
                foreach (var coffee in _items.BuildCoffeeStates()) yield return coffee;
                foreach (var sausage in _items.BuildSausageStates()) yield return sausage;
                foreach (var milk in _items.BuildMilkStates()) yield return milk;
                foreach (var atfBottle in _items.BuildAtfStates()) yield return atfBottle;
                foreach (var bag in _items.BuildBagStates()) yield return bag;
                foreach (var package in _items.BuildPackageStates()) yield return package;
            foreach (var supply in _items.BuildSupplyStates()) yield return supply;
            foreach (var bulb in _items.BuildBulbStates()) yield return bulb;
            foreach (var motorOil in _items.BuildMotorOilStates()) yield return motorOil;
            var oilFiller = _items.BuildMotorOilFillerState(); if(oilFiller!=null)yield return oilFiller;
            var advertJob = _items.BuildAdvertJobState(); if (advertJob != null) yield return advertJob;
            foreach (var sheet in _items.BuildAdvertSheetStates()) yield return sheet;
                foreach (var replacement in _items.BuildReplacementPartStates()) yield return replacement;
                var head = _items.BuildCylinderHeadState(); if (head != null) yield return head;
                foreach (var spawn in _items.BuildSpawnReplayManifests()) yield return spawn;
            }

            if ((flags & (WorldResyncRequest.FlagVehicles | WorldResyncRequest.FlagFsmStates)) != 0)
            {
                var atfFiller = _atf.BuildFillerState(); if (atfFiller != null) yield return atfFiller;
            }
            if ((flags & WorldResyncRequest.FlagVehicles) != 0)
            {
                foreach (var wire in _items.BuildWiringStates()) yield return wire;
                var battery = _items.BuildBatteryState(); if (battery != null) yield return battery;
                var block = _items.BuildEngineBlockState(); if (block != null) yield return block;
                var heater = _items.BuildHeaterState(); if (heater != null) yield return heater;
                var gearbox = _items.BuildGearboxState(); if (gearbox != null) yield return gearbox;
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

            if (netId == _train.NetId) { var train = _train.Snapshot(); if (train != null) yield return train; }
            var meat = _items.BuildMeatState(netId); if (meat != null) yield return meat;
            var coffee = _items.BuildCoffeeState(netId); if (coffee != null) yield return coffee;
            var sausage = _items.BuildSausageState(netId); if (sausage != null) yield return sausage;
            var milk = _items.BuildMilkState(netId); if (milk != null) yield return milk;
            var atfBottle = _items.BuildAtfState(netId); if (atfBottle != null) yield return atfBottle;
            var atfFiller = _atf.BuildFillerState(); if (atfFiller != null && atfFiller.VehicleId == netId) yield return atfFiller;
            var head = _items.BuildCylinderHeadState(); if (head != null && head.NetId == netId) yield return head;
            var supply = _items.BuildSupplyState(netId); if (supply != null) yield return supply;
            var bulb = _items.BuildBulbState(netId); if (bulb != null) yield return bulb;
            var motorOil = _items.BuildMotorOilState(netId); if (motorOil != null) yield return motorOil;
            var oilFiller = _items.BuildMotorOilFillerState(); if(oilFiller!=null)yield return oilFiller;
            var advertJob = _items.BuildAdvertJobState(); if (advertJob != null) yield return advertJob;
            var advertSheet = _items.BuildAdvertSheetState(netId); if (advertSheet != null) yield return advertSheet;
            var replacement = _items.BuildReplacementPartState(netId);
            if (replacement != null) yield return replacement;

            if (_items.Items.TryGetValue(netId, out var item) && item.Body != null && _items.CanSyncItemMotion(item))
            {
                var bag = _items.BuildBagState(netId);
                if (bag != null) yield return bag;
                var package = _items.BuildPackageState(netId);
                if (package != null) yield return package;
                if (item.IsVehicle)
                {
                    // A repair snapshot is not a new ownership claim or a driver's
                    // final packet. The normal snapshot path preserves live ownership.
                    var pose = new WorldItemSnapshot();
                    pose.Entries.Add(new WorldItemSnapshot.Entry { ItemId = netId,
                        Position = item.Body.transform.position.ToNet(), Rotation = item.Body.transform.rotation.ToNet() });
                    yield return pose;
                }
                else
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
            var partState = _fsm.BuildPartState(netId);
            if (partState != null)
            {
                // A part's visual FSM state does not carry its native wear/tightness.
                if (fsmState != null) yield return new FsmStateEnter { NetId = netId, StateName = fsmState };
                yield return partState;
                yield break;
            }
            if (fsmState != null)
            {
                yield return new FsmStateEnter { NetId = netId, StateName = fsmState };
                yield break;
            }

            var boltState = _fsm.BuildBoltState(netId);
            if (boltState != null) yield return boltState;
            var valveState = _fsm.BuildValveState(netId);
            if (valveState != null) yield return valveState;
        }

        public IEnumerable<IMessage> BuildWorldSnapshot()
        {
            if (!_syncReady) yield break;

            foreach (var chunk in _fsm.BuildDoorSnapshotChunks())
                yield return chunk;

            foreach (var thermostat in _fsm.BuildRadiatorThermostatStates())
                yield return thermostat;

            if (_lottoTickets != null)
                foreach (var ticket in _lottoTickets.BuildSnapshots()) yield return ticket;
            foreach (var chunk in _items.BuildItemSnapshotChunks())
                yield return chunk;

            foreach (var chunk in _fsm.BuildBoltSnapshotChunks())
                yield return chunk;
            foreach (var valve in _fsm.BuildValveStates()) yield return valve;

            foreach (var chunk in _fsm.BuildPartSnapshotChunks())
                yield return chunk;

            foreach (var chunk in BuildItemDespawnSnapshots()) yield return chunk;

            foreach (var meat in _items.BuildMeatStates()) yield return meat;
            foreach (var coffee in _items.BuildCoffeeStates()) yield return coffee;
            foreach (var sausage in _items.BuildSausageStates()) yield return sausage;
            foreach (var milk in _items.BuildMilkStates()) yield return milk;
            foreach (var atfBottle in _items.BuildAtfStates()) yield return atfBottle;
            var atfFiller = _atf.BuildFillerState(); if (atfFiller != null) yield return atfFiller;
            foreach (var bag in _items.BuildBagStates()) yield return bag;
            foreach (var package in _items.BuildPackageStates()) yield return package;
            foreach (var supply in _items.BuildSupplyStates()) yield return supply;
            foreach (var bulb in _items.BuildBulbStates()) yield return bulb;
            foreach (var motorOil in _items.BuildMotorOilStates()) yield return motorOil;
            var oilFiller = _items.BuildMotorOilFillerState(); if(oilFiller!=null)yield return oilFiller;
            var advertJob = _items.BuildAdvertJobState(); if (advertJob != null) yield return advertJob;
            foreach (var sheet in _items.BuildAdvertSheetStates()) yield return sheet;
            foreach (var replacement in _items.BuildReplacementPartStates()) yield return replacement;
                var head = _items.BuildCylinderHeadState(); if (head != null) yield return head;

            foreach (var wire in _items.BuildWiringStates()) yield return wire;
            var batteryState = _items.BuildBatteryState(); if (batteryState != null) yield return batteryState;
            var blockState = _items.BuildEngineBlockState(); if (blockState != null) yield return blockState;
            var heaterState = _items.BuildHeaterState(); if (heaterState != null) yield return heaterState;
            var gearboxState = _items.BuildGearboxState(); if (gearboxState != null) yield return gearboxState;

            foreach (var vehicle in _vehicles.BuildJoinVehicleSnapshots())
                yield return vehicle;

            foreach (var deadMover in _npcTraffic.BuildDeadMoverSnapshots())
                yield return deadMover;

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

            foreach (var buyer in _fsm.BuildFirewoodBuyerSnapshots())
                yield return buyer;

            foreach (var jobSite in _jobSites.BuildSnapshots())
                yield return jobSite;

            foreach (var mailOrder in _mailOrders.BuildSnapshots())
                yield return mailOrder;

            var fleetariOrder = _repairShop.BuildSnapshot();
            if (fleetariOrder != null)
                yield return fleetariOrder;

            var fleaListings = _fleaSale.BuildListingSnapshot();
            if (fleaListings != null) yield return fleaListings;
            var fleaSale = _fleaSale.BuildSnapshot();
            if (fleaSale != null)
                yield return fleaSale;

            var trainState = _train.Snapshot(); if (trainState != null) yield return trainState;
            var trailer = _trailer.Snapshot();
            if (trailer != null) yield return trailer;
            var householdFuses = _items.BuildHouseholdFuseSnapshot();
            if (householdFuses != null) yield return householdFuses;
            var taxiFare = _taxiJob.BuildFareSnapshot();
            if (taxiFare != null) yield return taxiFare;
            var taxiMeter = _taxiJob.BuildMeterSnapshot();
            if (taxiMeter != null) yield return taxiMeter;
            var taxiService = _taxiJob.BuildServiceSnapshot();
            if (taxiService != null) yield return taxiService;
            var taxiJob = _taxiJob.BuildSnapshot();
            if (taxiJob != null)
                yield return taxiJob;

            var worldScalars = _worldScalars.BuildSnapshot();
            if (worldScalars != null)
                yield return worldScalars;

            var venttiProperty = _ventti.BuildPropertySnapshot();
            if (venttiProperty != null)
                yield return venttiProperty;

            var venttiGame = _ventti.BuildGameSnapshot();
            if (venttiGame != null) yield return venttiGame;
            var venttiScene = _ventti.BuildSceneSnapshot();
            if (venttiScene != null) yield return venttiScene;

            var venttiTable = _ventti.BuildTableSnapshot();
            if (venttiTable != null)
                yield return venttiTable;

            var lotto = _lottery.BuildSnapshot();
            if (lotto != null) yield return lotto;

            var woodLoad = _woodDelivery.BuildSnapshot();
            if (woodLoad != null) yield return woodLoad;

            var hockey = _hockey.BuildSnapshot();
            if (hockey != null)
                yield return hockey;

            var welfare = _welfare.BuildSnapshot();
            if (welfare != null)
                yield return welfare;
            var debtLetter = _welfare.BuildDebtSnapshot();
            if (debtLetter != null)
                yield return debtLetter;

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

        private IEnumerable<WorldItemDespawnSnapshot> BuildItemDespawnSnapshots()
        {
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
        }
    }
}
