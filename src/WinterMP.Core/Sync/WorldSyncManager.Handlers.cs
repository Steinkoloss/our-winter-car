using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Message handlers: every OnRemote*/OnHost* entry point SessionManager dispatch
    /// routes into the sync subsystems, plus the per-player admission reset. Split from
    /// WorldSyncManager.cs by the ~1000-line rule; update/clear/snapshot stay there.
    /// </summary>
    public sealed partial class WorldSyncManager
    {
        public void OnRemoteTimeSync(TimeSync message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            _timeWeather.Apply(message);
        }

        public void OnRemoteWalletState(WalletState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            _wallet.Apply(message);
        }

        public void OnBankTransfer(BankTransferIntent message)
        {
            var session = SessionManager.Instance;
            if (session != null && session.IsHost) _wallet.OnBankTransfer(message, session);
        }

        public void OnBankResult(BankTransferResult message)
        {
            var session = SessionManager.Instance;
            if (session != null && !session.IsHost) _wallet.OnBankResult(message, session);
        }

        public void OnRemoteStateEnter(FsmStateEnter message) { EnsureSyncReady(); _fsm.OnRemoteStateEnter(message); }
        public void OnRemoteRawEvent(FsmRawEvent message) { EnsureSyncReady(); _fsm.OnRemoteRawEvent(message); }
        public bool OnHostGuestStateEnter(
            FsmStateEnter message,
            byte playerId,
            out RadiatorThermostatState? thermostatState)
        {
            EnsureSyncReady();
            return _fsm.TryAcceptGuestStateEnter(message, playerId, out thermostatState);
        }
        public bool OnHostGuestRawEvent(FsmRawEvent message, byte playerId)
        {
            EnsureSyncReady();
            return _fsm.TryAcceptGuestRawEvent(message, playerId);
        }
        public bool OnHostGuestBoltState(BoltState message, byte playerId)
        {
            EnsureSyncReady();
            return _fsm.TryAcceptGuestBoltState(message, playerId);
        }
        public bool OnHostGuestPartState(PartState message, byte playerId)
        {
            EnsureSyncReady();
            return _fsm.TryAcceptGuestPartState(message, playerId);
        }
        public void OnRemoteBoltState(BoltState message) { EnsureSyncReady(); _fsm.OnRemoteBoltState(message); }
        public void OnRemotePartState(PartState message) { EnsureSyncReady(); _fsm.OnRemotePartState(message); }
        public void OnRemoteRadiatorThermostatState(RadiatorThermostatState message)
        {
            EnsureSyncReady();
            _fsm.OnRemoteRadiatorThermostatState(message);
        }
        public void OnRemoteDoorSnapshot(WorldDoorSnapshot message) { EnsureSyncReady(); _fsm.OnRemoteDoorSnapshot(message); }
        public void OnRemoteBoltSnapshot(WorldBoltSnapshot message) { EnsureSyncReady(); _fsm.OnRemoteBoltSnapshot(message); }
        public void OnRemotePartSnapshot(WorldPartSnapshot message) { EnsureSyncReady(); _fsm.OnRemotePartSnapshot(message); }
        public bool OnHostGuestPurchaseIntent(PurchaseIntent intent, byte playerId)
        {
            EnsureSyncReady();
            // Resolve + activate the guest's selected mail-order FSM (if any) so it is
            // scannable, but do NOT yet write its values. Mail-order data may start
            // inactive on a host that did not make the guest's phone call.
            if (!_mailOrders.PrepareIntentForPurchase(intent)) return false;
            if (!_repairShop.PrepareIntentForPurchase(intent)) return false;
            ScanWorld();
            // Validate proximity/sequence BEFORE mutating authoritative host state, so a
            // rejected payment never leaves the host order carrying the guest's descriptor.
            if (!_fsm.TryAcceptGuestPurchaseIntent(intent, playerId)) return false;
            _mailOrders.CommitIntentForPurchase(intent);
            _repairShop.CommitIntentForPurchase(intent);
            _fsm.OnHostPurchaseIntent(intent);
            return true;
        }

        public void OnRemoteItemDespawn(ItemDespawn message) { EnsureSyncReady(); _items.OnRemoteItemDespawn(message); }
        public bool OnHostGuestItemDespawn(ItemDespawn message, byte playerId)
        {
            EnsureSyncReady();
            return _items.TryAcceptGuestDespawn(message, playerId);
        }
        public void OnRemoteItemSpawn(ItemSpawn message) { EnsureSyncReady(); _items.StartGuestSpawnBind(message); }
        public bool OnHostGuestSpawnIntent(SpawnIntent intent, byte playerId)
        {
            EnsureSyncReady();
            if (!_items.TryAcceptGuestSpawnIntent(intent, playerId)) return false;
            _items.OnHostSpawnIntent(intent);
            return true;
        }

        public void OnRemoteItemTransform(ItemTransform message) { EnsureSyncReady(); _items.OnRemoteItemTransform(message); }
        public bool OnHostGuestItemTransform(ItemTransform message, byte playerId)
        {
            EnsureSyncReady();
            if (!_items.TryAcceptGuestItemTransform(message, playerId)) return false;
            _items.OnRemoteItemTransform(message);
            return true;
        }
        public void OnRemoteNpcTransform(NpcTransform message) { EnsureSyncReady(); _npcTraffic.OnRemoteNpcTransform(message); }
        public void OnHostNpcDeathReport(Session.SessionManager session, NpcDeathReport message) { EnsureSyncReady(); _npcTraffic.OnHostDeathReport(session, message); }

        /// <summary>
        /// A player was (re)admitted into a slot — drop every per-player dedup latch for it.
        /// Runs on the HOST at handshake accept, and on GUESTS when the relayed PlayerSpawn
        /// arrives: other clients hold the same stale per-item sequence latches for the
        /// returning player, and without the reset they stale-drop its fresh streams.
        /// </summary>
        public void OnPlayerAdmitted(byte playerId)
        {
            EnsureSyncReady();
            _wanted.ForgetPlayer(playerId);
            _welfare.ForgetDebtPlayer(playerId);
            _wallet.ForgetBankPlayer(playerId);
            _gambling.ForgetPlayer(playerId);
            _poker.ForgetPlayer(playerId);
            _ventti.ForgetPlayer(playerId);
            _fleaSale.ForgetPlayer(playerId);
            _repairShop.ForgetPlayer(playerId);
            _mailOrders.ForgetPlayer(playerId);
            _heat.ForgetPlayer(playerId);
            _police.ForgetPlayer(playerId);
            _rally.ForgetPlayer(playerId);
            _iceRace.ForgetPlayer(playerId);
            _homeStereo.ForgetPlayer(playerId);
            _jail.ForgetPlayer(playerId);
            _fsm.ForgetPlayer(playerId);
            _appliances.ForgetPlayer(playerId);
            _items.ForgetPlayerSpawnSequence(playerId);
            _items.ForgetPlayerItemSequences(playerId);
            PassengerController.Instance?.ForgetPlayer(playerId);
        }

        public void OnPlayerDeparted(byte playerId)
        {
            _welfare.ForgetDebtPlayer(playerId);
            _gambling.ForgetPlayer(playerId);
            _poker.ForgetPlayer(playerId);
        }

        public void OnHostApplianceFireReport(ApplianceFireReport message) { EnsureSyncReady(); _appliances.OnHostFireReport(message); }
        public void OnRemoteItemSnapshot(WorldItemSnapshot message) { EnsureSyncReady(); _items.OnRemoteItemSnapshot(message); }
        public void OnRemoteItemDespawnSnapshot(WorldItemDespawnSnapshot message) { EnsureSyncReady(); _items.OnRemoteItemDespawnSnapshot(message); }

        public void OnRemoteClothingState(PlayerClothingState message) { EnsureSyncReady(); _clothing.OnRemoteClothingState(message); }
        public void OnRemoteHeatSourceState(HeatSourceState message) { EnsureSyncReady(); _heat.OnRemoteState(message); }
        public bool OnHostHeatSourceIntent(HeatSourceIntent message)
        {
            EnsureSyncReady();
            return _heat.TryAcceptIntent(message);
        }
        public bool OnHostGuestFluidContainerState(FluidContainerState message, byte playerId)
        {
            EnsureSyncReady();
            return _fluids.TryAcceptGuestState(message, playerId);
        }
        public void OnRemoteFluidContainerState(FluidContainerState message) { EnsureSyncReady(); _fluids.OnRemoteState(message); }
        public bool OnHostGuestBrewState(BrewState message, byte playerId)
        {
            EnsureSyncReady();
            return _kilju.TryAcceptGuestState(message, playerId);
        }
        public void OnRemoteBrewState(BrewState message) { EnsureSyncReady(); _kilju.OnRemoteState(message); }
        public void OnRemoteWorldProgressState(WorldProgressState message) { EnsureSyncReady(); _progress.Apply(message); }
        public void OnRemoteJobSiteState(JobSiteState message) { EnsureSyncReady(); _jobSites.Apply(message); }
        public void OnRemoteMailOrderState(MailOrderState message) { EnsureSyncReady(); _mailOrders.Apply(message); }
        public void OnHostMailOrderIntent(MailOrderIntent message) { EnsureSyncReady(); _mailOrders.RememberIntent(message); }
        public void OnRemoteFleetariOrderState(FleetariOrderState message) { EnsureSyncReady(); _repairShop.Apply(message); }
        public void OnHostFleetariOrderIntent(FleetariOrderIntent message) { EnsureSyncReady(); _repairShop.RememberIntent(message); }
        internal bool TryBuildFleetariIntent(string path, PlayMakerFSM fsm, out FleetariOrderIntent intent)
        {
            EnsureSyncReady();
            return _repairShop.TryBuildIntent(path, fsm, out intent);
        }
        public void OnRemoteFleaSaleState(FleaSaleState message) { EnsureSyncReady(); _fleaSale.Apply(message); }
        public bool OnHostFleaSaleIntent(FleaSaleIntent message) { EnsureSyncReady(); return _fleaSale.TryAcceptIntent(message); }
        public void OnRemoteTaxiJobState(TaxiJobState message) { EnsureSyncReady(); _taxiJob.Apply(message); }
        public void OnRemoteWorldScalarsState(WorldScalarsState message) { EnsureSyncReady(); _worldScalars.Apply(message); }
        public void OnRemoteHockeyBettingState(HockeyBettingState message) { EnsureSyncReady(); _hockey.Apply(message); }
        public void OnRemoteWelfareState(WelfareState message) { EnsureSyncReady(); _welfare.Apply(message); }
        public void OnDebtLetterState(DebtLetterState message) { EnsureSyncReady(); _welfare.OnDebtState(message); }
        public void OnDebtPayment(DebtPaymentIntent message) { EnsureSyncReady(); _welfare.OnDebtPayment(message); }
        public void OnDebtPaymentResult(DebtPaymentResult message) { EnsureSyncReady(); _welfare.OnDebtResult(message); }
        public void OnRemoteHitchhikerState(HitchhikerState message) { EnsureSyncReady(); _hitchhiker.Apply(message); }
        public void OnRemoteWantedState(WantedState message) { EnsureSyncReady(); _wanted.Apply(message); }
        public bool OnHostCrimeReport(CrimeReport message) { EnsureSyncReady(); return _wanted.TryAcceptCrimeReport(message); }
        public void OnRemoteJailState(JailState message) { EnsureSyncReady(); _jail.Apply(message); }
        public void OnHostJailReport(JailState message) { EnsureSyncReady(); _jail.OnHostJailReport(message); }
        public void OnRemotePursuitState(PursuitState message) { EnsureSyncReady(); _pursuit.Apply(message); }
        public void OnRemoteRallyResultsState(RallyResultsState message) { EnsureSyncReady(); _rallyResults.Apply(message); }
        public void OnRemoteJokkisRaceState(JokkisRaceState message) { EnsureSyncReady(); _jokkis.Apply(message); }
        public void OnRemoteApplianceState(ApplianceState message) { EnsureSyncReady(); _appliances.Apply(message); }
        public void OnRemotePhoneCallEvent(PhoneCallEvent message) { EnsureSyncReady(); _phone.Apply(message); }
        public void OnRemotePissAreaState(PissAreaState message) { EnsureSyncReady(); _pissAreas.Apply(message); }
        public void OnRemoteCarRadioState(CarRadioState message) { EnsureSyncReady(); _carRadio.Apply(message); }
        public void OnRemoteInspectionState(InspectionState message) { EnsureSyncReady(); _inspection.Apply(message); }
        public void OnRemotePoliceState(PoliceState message) { EnsureSyncReady(); _police.Apply(message); }
        public bool OnHostPoliceIntent(PoliceIntent message, out PoliceState state)
        {
            EnsureSyncReady();
            return _police.TryAcceptIntent(message, out state);
        }
        public void OnRemoteHomeStereoState(HomeStereoState message) { EnsureSyncReady(); _homeStereo.Apply(message); }
        public bool OnHostHomeStereoIntent(HomeStereoIntent message, out HomeStereoState state)
        {
            EnsureSyncReady();
            return _homeStereo.TryAcceptIntent(message, out state);
        }
        public void OnRemoteRallyState(RallyState message) { EnsureSyncReady(); _rally.Apply(message); }
        public bool OnHostRallyIntent(RallyIntent message, out RallyState state)
        {
            EnsureSyncReady();
            return _rally.TryAcceptIntent(message, out state);
        }
        public void OnRemoteIceRaceState(IceRaceState message) { EnsureSyncReady(); _iceRace.Apply(message); }
        public bool OnHostIceRaceIntent(IceRaceIntent message, out IceRaceState state)
        {
            EnsureSyncReady();
            return _iceRace.TryAcceptIntent(message, out state);
        }
        public void OnRemoteIceRaceEventState(IceRaceEventState message) { EnsureSyncReady(); _iceRaceEvent.Apply(message); }
        public void OnRemoteIceRaceResultsState(IceRaceResultsState message) { EnsureSyncReady(); _iceRaceResults.Apply(message); }
        internal bool TryBuildMailOrderIntent(string path, PlayMakerFSM fsm, out MailOrderIntent intent)
        {
            EnsureSyncReady();
            return _mailOrders.TryBuildIntent(path, fsm, out intent);
        }
        public void ForceHeatSourceBroadcast() { EnsureSyncReady(); _heat.ForceBroadcast(); }
        public void OnRemoteGamblingState(GamblingState message)
        {
            EnsureSyncReady();
            _ventti.OnRemoteState(message);
        }
        public bool OnHostGamblingIntent(GamblingIntent message)
        {
            EnsureSyncReady();
            return _ventti.TryAcceptIntent(message);
        }
        public void OnSlotIntent(SlotMachineIntent message) { EnsureSyncReady(); _gambling.OnIntent(message); }
        public void OnSlotState(SlotMachineState message) { EnsureSyncReady(); _gambling.OnState(message); }
        public void OnSlotResult(SlotMachineResult message) { EnsureSyncReady(); _gambling.OnResult(message); }
        public void OnPokerIntent(PokerIntent message) { EnsureSyncReady(); _poker.OnIntent(message); }
        public void OnPokerState(PokerState message) { EnsureSyncReady(); _poker.OnState(message); }
        public void OnPokerResult(PokerResult message) { EnsureSyncReady(); _poker.OnResult(message); }
        public void ForceGamblingBroadcast()
        {
            EnsureSyncReady();
            _gambling.ForceBroadcast();
            _poker.ForceBroadcast();
            _ventti.ForceBroadcast();
        }
        public void OnRemoteUtilityBillState(UtilityBillState message) { EnsureSyncReady(); _utilityBills.Apply(message); }
        public void ForceUtilityBillBroadcast() { EnsureSyncReady(); _utilityBills.ForceBroadcast(); }
        public void OnRemoteLotteryDrawState(LotteryDrawState message) { EnsureSyncReady(); _lottery.Apply(message); }
        public void ForceLotteryBroadcast() { EnsureSyncReady(); _lottery.ForceBroadcast(); }

        public void OnRemoteVehicleState(VehicleState message) { EnsureSyncReady(); _vehicles.OnRemoteVehicleState(message); }
        public bool OnHostGuestVehicleState(VehicleState message, byte playerId)
        {
            EnsureSyncReady();
            if (!_vehicles.TryAcceptGuestVehicleState(message, playerId)) return false;
            _vehicles.OnRemoteVehicleState(message);
            return true;
        }
        public void OnRemoteVehicleDamage(VehicleDamage message) { EnsureSyncReady(); _vehicles.ApplyVehicleDamage(message); }
        public bool OnHostGuestVehicleDamage(VehicleDamage message, byte playerId)
        {
            EnsureSyncReady();
            if (!_vehicles.TryAcceptGuestVehicleDamage(message, playerId)) return false;
            _vehicles.ApplyVehicleDamage(message); // host applies if it isn't the owner
            return true;
        }
        public void OnRemoteVehicleCondition(VehicleCondition message) { EnsureSyncReady(); _vehicles.ApplyVehicleCondition(message); }
        public bool OnHostGuestVehicleCondition(VehicleCondition message, byte playerId)
        {
            EnsureSyncReady();
            if (!_vehicles.TryAcceptGuestVehicleCondition(message, playerId)) return false;
            _vehicles.ApplyVehicleCondition(message); // host applies if it isn't the owner
            return true;
        }
        public bool OnHostVehicleFuelIntent(VehicleFuelIntent message, out VehicleState state)
        {
            EnsureSyncReady();
            return _vehicles.TryAcceptFuelTransfer(message, out state);
        }
        public void OnRemoteVehicleClimate(VehicleClimate message) { EnsureSyncReady(); _vehicles.OnRemoteVehicleClimate(message); }
        public bool OnHostGuestVehicleClimate(VehicleClimate message, byte playerId)
        {
            EnsureSyncReady();
            if (!_vehicles.TryAcceptGuestVehicleClimate(message, playerId)) return false;
            _vehicles.OnRemoteVehicleClimate(message);
            return true;
        }
        public void OnRemoteVehicleCargo(VehicleCargo message) { EnsureSyncReady(); _items.OnRemoteVehicleCargo(message); }
        public bool OnHostGuestVehicleCargo(VehicleCargo message, byte playerId)
        {
            EnsureSyncReady();
            if (!_items.TryAcceptGuestVehicleCargo(message, playerId)) return false;
            _items.OnRemoteVehicleCargo(message);
            return true;
        }

    }
}
