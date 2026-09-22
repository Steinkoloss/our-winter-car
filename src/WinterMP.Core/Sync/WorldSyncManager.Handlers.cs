using System;
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
        public void OnFirewoodLoad(FirewoodLoadState message) { _woodDelivery.Apply(message); }
        public void OnFirewoodUnload(FirewoodUnloadIntent message) { _woodDelivery.Request(message); }
        public void OnFirewoodBuyer(FirewoodBuyerState message) { EnsureSyncReady(); _fsm.OnFirewoodBuyer(message); }
        public void OnMooseCorpse(MooseCorpseState message) { EnsureSyncReady(); _items.OnMooseCorpse(message); }
        public void OnMooseChop(MooseChopIntent message) { EnsureSyncReady(); _items.OnMooseChop(message); }
        public void OnTrainState(TrainState message)
        {
            if (!IsGameLevel() || _worldSyncDisabled || Time.unscaledTime < _syncErrorBackoffUntil
                || !_trainReceiveFailure.CanRun(Time.unscaledTime)) return;
            // Admit only the current state; never retain or replay a failed packet.
            try { EnsureSyncReady(); _train.Receive(message); }
            catch (Exception e) { HandleSyncError("Receive.TrainSync", e, _trainReceiveFailure); }
        }
        public void OnCoffeeState(CoffeeState message) { if (IsGameLevel()) { EnsureSyncReady(); _items.OnCoffeeState(message); } }
        public void OnVendorCoffeeState(VendorCoffeeState message) { if (IsGameLevel()) { EnsureSyncReady(); _items.OnVendorCoffeeState(message); } }
        public void OnBeerCaseExtract(BeerCaseExtractIntent message, byte actor) { if (IsGameLevel()) { EnsureSyncReady(); _items.OnBeerCaseExtract(message, actor); } }
        public void OnBeerCaseUpdate(BeerCaseUpdate message) { if (IsGameLevel()) { EnsureSyncReady(); _items.OnBeerCaseUpdate(message); } }
        public void OnVendorCoffeeIntent(VendorCoffeeIntent message, byte actor) { if (IsGameLevel()) { EnsureSyncReady(); _items.OnVendorCoffeeIntent(message, actor); } }
        public void OnVendorCoffeeResult(VendorCoffeeResult message) { if (IsGameLevel()) { EnsureSyncReady(); _items.OnVendorCoffeeResult(message); } }
        public void OnCoffeeIntent(CoffeeIntent message, byte actor) { if (IsGameLevel()) { EnsureSyncReady(); _items.OnCoffeeIntent(message, actor); } }
        public void OnCoffeeDrink(CoffeeDrinkResult message) { if (IsGameLevel()) { EnsureSyncReady(); _items.OnCoffeeDrink(message); } }
        public void OnSausageState(SausageState message) { EnsureSyncReady(); _items.OnSausageState(message); }
        public void OnSausageOpen(SausageOpenIntent message, byte actor) { EnsureSyncReady(); _items.OnSausageOpen(message, actor); }
        public void OnMeatState(MooseMeatState message) { EnsureSyncReady(); _items.OnMeatState(message); }
        public void OnAtfBottleState(AtfBottleState message) { EnsureSyncReady(); _items.OnAtfState(message); }
        public void OnAtfFillerState(AtfFillerState message) { EnsureSyncReady(); _atf.OnFillerState(message); }
        public void OnMotorOilFillerState(MotorOilFillerState message) { if(!IsGameLevel())return; EnsureSyncReady(); _items.OnMotorOilFillerState(message); }
        public void OnMotorOilRefillIntent(MotorOilRefillIntent message) { if(!IsGameLevel())return; EnsureSyncReady(); _items.OnMotorOilRefillIntent(message); }
        public void OnAtfIntent(AtfRefillIntent message) { EnsureSyncReady(); _atf.OnIntent(message); }
        public void OnMilkCondition(MilkConditionState message) { EnsureSyncReady(); _items.OnMilkCondition(message); }
        public void OnCylinderHeadState(CylinderHeadState message) { EnsureSyncReady(); _items.OnCylinderHeadState(message); }
        public void OnRemoteValveState(ValveAdjustmentState message) { EnsureSyncReady(); _fsm.OnRemoteValveState(message); }
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
        public void OnRemoteItemTransform(ItemTransform message) { EnsureSyncReady(); _items.OnRemoteItemTransform(message); }
        public bool OnHostGuestItemTransform(ItemTransform message, byte playerId, out VehicleConditionReleaseAck? conditionAck)
        {
            conditionAck = null;
            EnsureSyncReady();
            if (!_items.TryAcceptGuestItemTransform(message, playerId)) return false;
            if (!_items.Items.TryGetValue(message.ItemId, out var item)) return false;
            byte previousOwner = item.RemoteOwner;
            if (!_items.OnRemoteItemTransform(message)) return false;
            if (message.IsFinal) conditionAck = _vehicles.BuildConditionReleaseAck(item, previousOwner, message);
            return true;
        }
        public void OnRemoteNpcTransform(NpcTransform message) { EnsureSyncReady(); _npcTraffic.OnRemoteNpcTransform(message); }
        public void OnHostNpcDeathReport(Session.SessionManager session, NpcDeathReport message) { EnsureSyncReady(); if (!_items.OnMooseDeathReport(session, message)) _npcTraffic.OnHostDeathReport(session, message); }

        /// <summary>
        /// A player was (re)admitted into a slot — drop every per-player dedup latch for it.
        /// Runs on the HOST at handshake accept, and on GUESTS when the relayed PlayerSpawn
        /// arrives: other clients hold the same stale per-item sequence latches for the
        /// returning player, and without the reset they stale-drop its fresh streams.
        /// </summary>
        public void OnPlayerAdmitted(byte playerId)
        {
            _items.ForgetHouseholdFuseIntents(playerId);
            _items.ForgetCoffeePlayer(playerId);
            _items.ForgetVendorCoffeePlayer(playerId);
            _items.ForgetBeerCasePlayer(playerId);
            _items.ForgetAdvertPlayer(playerId);
            _phone.Adverts.Forget(playerId);
            _items.ForgetSausageIntents(playerId);
            _taxiJob.ForgetMeterIntents(playerId);
            _taxiJob.ForgetFareIntents(playerId);
            _woodDelivery.OnPlayerAdmitted(playerId);
            _trailer.PlayerAdmitted(playerId);
            _lottoTickets?.ForgetPlayer(playerId);
            EnsureSyncReady();
            _wanted.ForgetPlayer(playerId);
            _welfare.ForgetDebtPlayer(playerId);
            _utilityBills.ForgetPlayer(playerId);
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
            _atf.Forget(playerId); _items.ForgetOilRefill(playerId);
            _items.ForgetPackageOpeningPlayer(playerId);
            _items.ForgetWirePlayer(playerId);
            _items.ForgetPartFittingPlayer(playerId);
            _items.ForgetBagPlayer(playerId);
            _items.ForgetPlayerItemSequences(playerId);
            _vehicles.ForgetVehicleStatePlayer(playerId);
            PassengerController.Instance?.ForgetPlayer(playerId);
        }

        public void OnPlayerDeparted(byte playerId)
        {
            _phone.Adverts.Forget(playerId);
            _taxiJob.ForgetCaller(playerId);
            _atf.Forget(playerId); _items.ForgetOilRefill(playerId);
            _items.ForgetPartFittingPlayer(playerId);
            _items.ForgetBagPlayer(playerId);
            _welfare.ForgetDebtPlayer(playerId);
            _utilityBills.ForgetPlayer(playerId);
            _gambling.ForgetPlayer(playerId);
            _poker.ForgetPlayer(playerId);
            _ventti.ForgetPlayer(playerId);
        }

        public void OnHostApplianceFireReport(ApplianceFireReport message) { EnsureSyncReady(); _appliances.OnHostFireReport(message); }
        public void OnHostStoveKnob(StoveKnobIntent message) { EnsureSyncReady(); _appliances.OnStoveKnob(message); }
        public void OnRemoteItemSnapshot(WorldItemSnapshot message) { EnsureSyncReady(); _items.OnRemoteItemSnapshot(message); }
        public void OnRemoteItemDespawnSnapshot(WorldItemDespawnSnapshot message) { EnsureSyncReady(); _items.OnRemoteItemDespawnSnapshot(message); }

        public void OnRemoteClothingState(PlayerClothingState message) { EnsureSyncReady(); _clothing.OnRemoteClothingState(message); }
        public void OnRemoteHeatSourceState(HeatSourceState message) { EnsureSyncReady(); _heat.OnRemoteState(message); }
        internal void OnCabinFuelIntent(WoodstoveFeedIntent message, byte actor)
        { if (!IsGameLevel()) return; EnsureSyncReady(); _heat.OnCabinIntent(message, actor); }
        internal void OnSaunaTimerIntent(SaunaTimerIntent message, byte actor)
        { if (!IsGameLevel()) return; EnsureSyncReady(); _heat.OnSaunaIntent(message, actor); }
        internal void OnSaunaTimerState(SaunaTimerState message)
        { if (!IsGameLevel()) return; EnsureSyncReady(); _heat.OnSaunaState(message); }
        internal void OnCabinFuelUpdate(WoodstoveFuelUpdate message)
        { if (!IsGameLevel()) return; EnsureSyncReady(); _heat.OnCabinUpdate(message); }
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
        public void OnContainerFuelIntent(ContainerFuelIntent message, byte actor)
        { if (!IsGameLevel()) return; EnsureSyncReady(); _fluids.OnFuelIntent(message, actor); }
        public void OnContainerFuelResult(ContainerFuelResult message)
        { if (!IsGameLevel()) return; EnsureSyncReady(); _fluids.OnFuelResult(message); }
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
        public void OnFleaListingState(FleaListingState message) { EnsureSyncReady(); _fleaSale.ApplyListings(message); }
        public void OnFleaListingResult(FleaListingResult message) { EnsureSyncReady(); _fleaSale.OnListingResult(message); }
        public bool OnFleaListingIntent(FleaListingIntent message) { EnsureSyncReady(); return _fleaSale.TryAcceptListing(message); }
        public void OnFleaSaleResult(FleaSaleResult message) { EnsureSyncReady(); _fleaSale.OnResult(message); }
        public void OnRemoteFleaSaleState(FleaSaleState message) { EnsureSyncReady(); _fleaSale.Apply(message); }
        public bool OnHostFleaSaleIntent(FleaSaleIntent message) { EnsureSyncReady(); return _fleaSale.TryAcceptIntent(message); }
        public void OnTractorTrailerState(TractorTrailerState message) { if (IsGameLevel()) _trailer.Receive(message); }
        public void OnTractorTrailerIntent(TractorTrailerIntent message, byte actor) { if (IsGameLevel()) _trailer.Request(message, actor); }
        public void OnTractorTrailerMotion(TractorTrailerMotion message, byte actor) { if (IsGameLevel()) _trailer.ReceiveMotion(message, actor); }
        public void OnHouseholdFuseState(HouseholdFuseState message) { if (!IsGameLevel()) return; EnsureSyncReady(); _items.ReceiveHouseholdFuses(message); }
        public void OnHouseholdFuseIntent(HouseholdFuseIntent message, byte actor) { if (!IsGameLevel()) return; EnsureSyncReady(); _items.ReceiveHouseholdFuseIntent(message, actor); }
        public void OnHouseholdFuseResult(HouseholdFuseResult message) { if (!IsGameLevel()) return; EnsureSyncReady(); _items.ReceiveHouseholdFuseResult(message); }

        private bool PrepareTaxiMessage()
        {
            // Session polling can deliver a packet after Unity destroys GAME,
            // before our next Update clears its cached native taxi bindings.
            if (!IsGameLevel()) return false;
            EnsureSyncReady(); return true;
        }
        public void OnTaxiFareState(TaxiFareState message) { if (PrepareTaxiMessage()) _taxiJob.ReceiveFare(message); }
        public void OnTaxiFareIntent(TaxiFareIntent message, byte actor) { if (PrepareTaxiMessage()) _taxiJob.ReceiveFareIntent(message, actor); }
        public void OnTaxiMeterState(TaxiMeterState message) { if (PrepareTaxiMessage()) _taxiJob.ReceiveMeter(message); }
        public void OnTaxiMeterIntent(TaxiMeterIntent message, byte actor) { if (PrepareTaxiMessage()) _taxiJob.ReceiveMeterIntent(message, actor); }
        public void OnTaxiServiceState(TaxiServiceState message) { if (PrepareTaxiMessage()) _taxiJob.ReceiveService(message); }
        public void OnTaxiPaydayReadIntent(TaxiPaydayReadIntent message, byte actor) { if (PrepareTaxiMessage()) _taxiJob.ReceivePaydayRead(message, actor); }
        public void OnTaxiCallIntent(TaxiCallIntent message, byte actor) { if (PrepareTaxiMessage()) _taxiJob.ReceiveCall(message, actor); }
        public void OnRemoteTaxiJobState(TaxiJobState message) { if (PrepareTaxiMessage()) _taxiJob.Apply(message); }
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
        public void OnPissAreaIntent(PissAreaIntent message, byte actor) { EnsureSyncReady(); _pissAreas.OnIntent(message, actor); }
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
        public void OnRemoteVenttiTableState(VenttiTableState message)
        {
            EnsureSyncReady();
            _ventti.OnRemoteState(message);
        }
        public void OnRemoteVenttiPropertyState(VenttiPropertyState message)
        {
            EnsureSyncReady();
            _ventti.OnPropertyState(message);
        }
        public void OnVenttiRequest(VenttiRequest message) { EnsureSyncReady(); _ventti.OnRequest(message); }
        public void OnVenttiGameState(VenttiLedgerState message) { EnsureSyncReady(); _ventti.OnGameState(message); }
        public void OnVenttiReceipt(VenttiReceipt message) { EnsureSyncReady(); _ventti.OnReceipt(message); }
        public void OnVenttiSceneState(VenttiSceneState message) { EnsureSyncReady(); _ventti.OnSceneState(message); }
        public void OnVenttiSoundCue(VenttiSoundCue message) { EnsureSyncReady(); _ventti.OnSoundCue(message); }
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
        public void OnUtilityPayment(UtilityPaymentIntent message) { EnsureSyncReady(); _utilityBills.OnPayment(message); }
        public void OnUtilityPaymentResult(UtilityPaymentResult message) { EnsureSyncReady(); _utilityBills.OnPaymentResult(message); }
        public void ForceUtilityBillBroadcast() { EnsureSyncReady(); _utilityBills.ForceBroadcast(); }
        public void OnLottoTicketRequest(LottoTicketRequest message) { EnsureSyncReady(); _lottoTickets?.OnRequest(message); }
        public void OnLottoTicketReceipt(LottoTicketReceipt message) { EnsureSyncReady(); _lottoTickets?.OnReceipt(message); }
        public void OnLottoTicketState(LottoTicketState message) { EnsureSyncReady(); _lottoTickets?.OnState(message); }
        public void OnBagState(BagState message) { EnsureSyncReady(); _items.OnBagState(message); }
        public void OnHostBagOpen(BagOpenRequest message, byte actor) { EnsureSyncReady(); _items.OnHostBagOpen(message, actor); }
        public void OnBagOpenReceipt(BagOpenReceipt message) { EnsureSyncReady(); _items.OnBagOpenReceipt(message); }
        public void OnMotorOilState(MotorOilBottleState message) { if (IsGameLevel()) { EnsureSyncReady(); _items.OnMotorOilState(message); } }
        public void OnAdvertJob(AdvertJobState message) { if (IsGameLevel()) { EnsureSyncReady(); _items.OnAdvertJobState(message); } }
        public void OnAdvertSheet(AdvertSheetState message) { if (IsGameLevel()) { EnsureSyncReady(); _items.OnAdvertSheetState(message); } }
        public void OnAdvertPhoneIntent(AdvertPhoneIntent message, byte actor) { if (IsGameLevel()) { EnsureSyncReady(); _phone.Adverts.Intent(message, actor); } }
        public void OnAdvertPhoneResult(AdvertPhoneResult message) { if (IsGameLevel()) { EnsureSyncReady(); _phone.Adverts.Result(message); } }
        public void OnAdvertIntent(AdvertIntent message, byte actor) { if (IsGameLevel()) { EnsureSyncReady(); _items.OnAdvertIntent(message, actor); } }
        public void OnBulbState(BulbState message) { EnsureSyncReady(); _items.OnBulbState(message); }
        public void OnSupplyState(SupplyItemState message) { EnsureSyncReady(); _items.OnSupplyState(message); }
        public void OnPackageState(PackageState message) { EnsureSyncReady(); _items.OnPackageState(message); }
        public void OnHostPartFit(PartFitRequest message, byte playerId) { EnsureSyncReady(); _items.OnHostPartFit(message, playerId); }
        public void OnPartFitReceipt(PartFitReceipt message) { EnsureSyncReady(); _items.OnPartFitReceipt(message); }
        internal void DrawPartFitPrompt() { if (_syncReady) _items.DrawPartFitPrompt(); }

        public void OnHostPackageOpen(PackageOpenRequest message, byte playerId) { EnsureSyncReady(); _items.OnHostPackageOpen(message, playerId); }
        public void OnPackageOpenReceipt(PackageOpenReceipt message) { EnsureSyncReady(); _items.OnPackageOpenReceipt(message); }
        public void OnReplacementPartState(ReplacementPartState message) { EnsureSyncReady(); _items.OnReplacementPartState(message); }
        public void OnWiringState(WiringState message) { EnsureSyncReady(); _items.OnWiringState(message); }
        public void OnHostWireInstall(WiringInstallRequest message, byte actor) { EnsureSyncReady(); _items.OnHostWireInstall(message, actor); }
        public void OnWireInstallReceipt(WiringInstallReceipt message) { EnsureSyncReady(); _items.OnWireInstallReceipt(message); }
        public void OnVehicleCoolantState(VehicleCoolantState message) { EnsureSyncReady(); _vehicles.OnVehicleCoolantState(message); }
        internal bool ReadHeaterHoseInstalled(byte index) => _items?.ReadHeaterHoseInstalled(index) ?? false;

        internal WiringState? ReadWiringInputs(uint id) => _items?.ReadWiringInputs(id);
        internal HeaterState? ReadHeaterInputs() => _items?.ReadHeaterInputs();
        public void OnHeaterState(HeaterState message) { EnsureSyncReady(); _items.OnHeaterState(message); }
        public void OnGearboxState(GearboxState message) { EnsureSyncReady(); _items.OnGearboxState(message); }
        public void OnEngineBlockState(EngineBlockState message) { EnsureSyncReady(); _items.OnEngineBlockState(message); }
        public void OnBatteryState(BatteryState message) { EnsureSyncReady(); _items.OnBatteryState(message); }
        internal BatteryState? ReadBatteryInputs() => _items?.ReadBatteryInputs();
        internal bool CanObserveStarterDraw(PlayMakerFSM fsm) => _vehicles?.CanObserveStarterDraw(fsm) == true;
        internal bool CanObserveGearboxWear(PlayMakerFSM fsm) => _syncReady && _vehicles.CanObserveGearboxWear(fsm);
        internal bool ShouldDelegateGearboxWear(PlayMakerFSM fsm) => _syncReady && _vehicles.ShouldDelegateGearboxWear(fsm);
        internal void RecordGearboxWear(PlayMakerFSM fsm) => _vehicles?.RecordGearboxWear(fsm);
        public bool OnHostGearboxWear(GearboxWearRequest request, byte playerId)
        { EnsureSyncReady(); return _vehicles.OnHostGearboxWear(request, playerId); }
        internal bool CanObserveGearboxOil(PlayMakerFSM fsm) => _syncReady && _vehicles.CanObserveGearboxOil(fsm);
        internal bool ShouldDelegateGearboxOil(PlayMakerFSM fsm) => _syncReady && _vehicles.ShouldDelegateGearboxOil(fsm);
        internal void RecordGearboxOilUse(PlayMakerFSM fsm, byte phase) => _vehicles?.RecordGearboxOilUse(fsm, phase);
        public bool OnHostGearboxOilUse(GearboxOilUseRequest request, byte playerId)
        { EnsureSyncReady(); return _vehicles.OnHostGearboxOilUse(request, playerId); }
        internal void RecordStarterWear(PlayMakerFSM fsm, float seconds) => _vehicles?.RecordStarterWear(fsm, seconds);
        public bool OnHostStarterWear(StarterWearRequest message, byte playerId)
        { EnsureSyncReady(); return _vehicles.OnHostStarterWear(message, playerId); }
        internal void RecordStarterDraw(PlayMakerFSM fsm, byte kind) => _vehicles?.RecordStarterDraw(fsm, kind);
        public bool OnHostStarterDraw(StarterDrawRequest message, byte playerId)
        { EnsureSyncReady(); return _vehicles.OnHostStarterDraw(message, playerId); }
        public void OnRemoteLottoDrawState(LottoDrawState message) { EnsureSyncReady(); _lottery.Apply(message); }
        public void ForceLotteryBroadcast() { EnsureSyncReady(); _lottery.ForceBroadcast(); }

        public void OnRemoteVehicleState(VehicleState message) { EnsureSyncReady(); _vehicles.OnRemoteVehicleState(message); }
        public bool OnHostGuestVehicleState(VehicleState message, byte playerId)
        {
            EnsureSyncReady();
            if (!_vehicles.TryAcceptGuestVehicleState(message, playerId)) return false;
            return _vehicles.OnRemoteVehicleState(message);
        }
        public void OnRemoteVehicleDamage(VehicleDamage message) { EnsureSyncReady(); _vehicles.ApplyVehicleDamage(message); }
        public void OnVehicleDrivetrainWearState(VehicleDrivetrainWearState message) { EnsureSyncReady(); _vehicles.OnVehicleDrivetrainWearState(message); }
        internal void RecordWheelPuncture(PlayMakerFSM fsm, int wheel) => _vehicles?.RecordWheelPuncture(fsm, wheel);
        internal bool OnHostWheelPuncture(WheelPunctureRequest message, byte player)
        { EnsureSyncReady(); return _vehicles.OnHostWheelPuncture(message, player); }
        public void OnVehicleWheelHealthState(VehicleWheelHealthState message) { EnsureSyncReady(); _vehicles.OnVehicleWheelHealthState(message); }
        public bool TryReadHostWheelHealth(PlayMakerFSM fsm, int wheel, out float health, out bool ready) => _vehicles.TryReadHostWheelHealth(fsm, wheel, out health, out ready);
        public void OnRemoteVehicleCondition(VehicleCondition message) { EnsureSyncReady(); _vehicles.ApplyVehicleCondition(message); }
        public void OnVehicleConditionReleaseAck(VehicleConditionReleaseAck message) { EnsureSyncReady(); _vehicles.ReceiveConditionReleaseAck(message); }
        public bool OnHostGuestVehicleCondition(VehicleCondition message, byte playerId)
        {
            EnsureSyncReady();
            if (!_vehicles.TryAcceptGuestVehicleCondition(message, playerId)) return false;
            return _vehicles.ApplyVehicleCondition(message);
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
            return _vehicles.OnRemoteVehicleClimate(message);
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
