using System;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    public sealed partial class WorldSyncManager
    {
        private readonly CallbackFailure _vehicleStatesFailure = new CallbackFailure();
        private readonly CallbackFailure _starterDrawsFailure = new CallbackFailure();
        private readonly CallbackFailure _starterWearFailure = new CallbackFailure();
        private readonly CallbackFailure _vehicleCoolantFailure = new CallbackFailure();
        private readonly CallbackFailure _drivetrainWearFailure = new CallbackFailure();
        private readonly CallbackFailure _wheelHealthFailure = new CallbackFailure();
        private readonly CallbackFailure _vehicleDamageFailure = new CallbackFailure();
        private readonly CallbackFailure _vehicleConditionFailure = new CallbackFailure();
        private readonly CallbackFailure _fuelTransfersFailure = new CallbackFailure();
        private readonly CallbackFailure _vehicleClimateFailure = new CallbackFailure();

        private void ResetVehicleUpdateErrors()
        {
            _vehicleStatesFailure.Clear();
            _starterDrawsFailure.Clear();
            _starterWearFailure.Clear();
            _vehicleCoolantFailure.Clear();
            _drivetrainWearFailure.Clear();
            _wheelHealthFailure.Clear();
            _vehicleDamageFailure.Clear();
            _vehicleConditionFailure.Clear();
            _fuelTransfersFailure.Clear();
            _vehicleClimateFailure.Clear();
        }

        private void UpdateWorldSync()
        {
            WatchLevelChanges();

            if (GuestSaveGuard.ProtectWorld && IsGameLevel())
            {
                EnsureSyncReady();
                _vehicles.PrepareGuestDamageIsolation();
                _vehicles.PrepareGuestEngineProtection();
            }

            var session = SessionManager.Instance;
            bool sessionActive = session != null
                && (session.State == SessionState.Hosting || session.State == SessionState.Connected);

            if (!sessionActive)
            {
                if (_wasSessionActive) ReleaseEverything();
                _wasSessionActive = false;
                return;
            }

            if (!IsGameLevel())
            {
                _wasSessionActive = true;
                return;
            }

            EnsureSyncReady();
            _wasSessionActive = true;

            _items.ProcessBags(session!);

            UpdateWorldDiscovery();

            if (Time.unscaledTime >= _nextPendingAt)
            {
                _nextPendingAt = Time.unscaledTime + PendingRetrySeconds;
                _fsm.ProcessPending();
            }

            if (!session!.IsHost && !_snapshotRequested && _fsm.DoorCount > 0)
            {
                _snapshotRequested = true;
                WinterMPPlugin.Log.LogInfo($"WorldSync: requesting join snapshot (id hash {IdHash:X8}).");
                session.SendWorldMessage(new WorldSnapshotRequest { IdHash = IdHash }, Channel.ReliableOrdered);
            }

            if (session.IsHost && session.PlayerCount > 0 && Time.unscaledTime >= _nextTimeSyncAt)
            {
                _nextTimeSyncAt = Time.unscaledTime + TimeSyncIntervalSeconds;
                var time = _timeWeather.BuildMessage();
                if (time != null)
                    session.SendWorldMessage(time, Channel.ReliableOrdered);
            }

            _wallet.UpdateBanking(session);

            if (session.IsHost && session.PlayerCount > 0)
            {
                float now = Time.unscaledTime;
                var wallet = _wallet.BuildMessage();
                if (wallet != null && _wallet.ShouldBroadcast(wallet, now))
                    session.SendWorldMessage(wallet, Channel.ReliableOrdered);

                if (Time.unscaledTime >= _nextChecksumAt)
                {
                    _nextChecksumAt = Time.unscaledTime + ChecksumIntervalSeconds;
                    var checksum = BuildStateChecksum();
                    if (checksum != null)
                        session.SendWorldMessage(checksum, Channel.ReliableOrdered);
                }
            }

            _items.ProcessPendingSpawns(session!);
            _items.ProcessMilk(session!);
            _items.UpdateItems(session);
            _items.UpdateAtf(session!);
            _atf.Update(session!);
            _npcTraffic.Update(session!);
            _train.Update(session!);
            // Keep current-session work in its original order. Retry the next
            // live callback, never a captured intent or a partially applied frame.
            float vehicleNow = Time.unscaledTime;
            if (_vehicleStatesFailure.CanRun(vehicleNow))
            {
                try { _vehicles.UpdateVehicleStates(session!); }
                catch (Exception e) { HandleSyncError("Update.VehicleWorldSync.UpdateVehicleStates", e, _vehicleStatesFailure); }
            }
            if (_starterDrawsFailure.CanRun(vehicleNow))
            {
                try { _vehicles.UpdateStarterDraws(session!); }
                catch (Exception e) { HandleSyncError("Update.VehicleWorldSync.UpdateStarterDraws", e, _starterDrawsFailure); }
            }
            if (_starterWearFailure.CanRun(vehicleNow))
            {
                try { _vehicles.UpdateStarterWear(session!); }
                catch (Exception e) { HandleSyncError("Update.VehicleWorldSync.UpdateStarterWear", e, _starterWearFailure); }
            }
            if (_vehicleCoolantFailure.CanRun(vehicleNow))
            {
                try { _vehicles.UpdateVehicleCoolant(session!); }
                catch (Exception e) { HandleSyncError("Update.VehicleWorldSync.UpdateVehicleCoolant", e, _vehicleCoolantFailure); }
            }
            if (_drivetrainWearFailure.CanRun(vehicleNow))
            {
                try { _vehicles.UpdateDrivetrainWearStates(session!); }
                catch (Exception e) { HandleSyncError("Update.VehicleWorldSync.UpdateDrivetrainWearStates", e, _drivetrainWearFailure); }
            }
            if (_wheelHealthFailure.CanRun(vehicleNow))
            {
                try { _vehicles.UpdateWheelHealthStates(session!); }
                catch (Exception e) { HandleSyncError("Update.VehicleWorldSync.UpdateWheelHealthStates", e, _wheelHealthFailure); }
            }
            if (_vehicleDamageFailure.CanRun(vehicleNow))
            {
                try { _vehicles.UpdateVehicleDamage(session!); }
                catch (Exception e) { HandleSyncError("Update.VehicleWorldSync.UpdateVehicleDamage", e, _vehicleDamageFailure); }
            }
            if (_vehicleConditionFailure.CanRun(vehicleNow))
            {
                try { _vehicles.UpdateVehicleCondition(session!); }
                catch (Exception e) { HandleSyncError("Update.VehicleWorldSync.UpdateVehicleCondition", e, _vehicleConditionFailure); }
            }
            if (_fuelTransfersFailure.CanRun(vehicleNow))
            {
                try { _vehicles.UpdateFuelTransfers(session!); }
                catch (Exception e) { HandleSyncError("Update.VehicleWorldSync.UpdateFuelTransfers", e, _fuelTransfersFailure); }
            }
            if (_vehicleClimateFailure.CanRun(vehicleNow))
            {
                try { _vehicles.UpdateVehicleClimate(session!); }
                catch (Exception e) { HandleSyncError("Update.VehicleWorldSync.UpdateVehicleClimate", e, _vehicleClimateFailure); }
            }
            _clothing.Update(session!);
            _heat.Update(session!);
            _fluids.Update(session!);
            _kilju.Update(session!);
            _progress.Update(session!);
            _jobSites.Update(session!);
            _fsm.UpdateFirewoodBuyers(session!);
            _mailOrders.Update(session!);
            _inspection.Update(session!);
            _police.Update(session!);
            _homeStereo.Update(session!);
            _rally.Update(session!);
            _iceRace.Update(session!);
            _iceRaceEvent.Update(session!);
            _iceRaceResults.Update(session!);
            _gambling.Update(session!);
            _poker.Update(session!);
            _ventti.Update(session!);
            _utilityBills.Update(session!);
            _lottery.Update(session!);
            _lottoTickets?.Update(session!);
            _repairShop.Update(session!);
            _fleaSale.Update(session!);
            _taxiJob.Update(session!);
            _worldScalars.Update(session!);
            _hockey.Update(session!);
            _trailer.Update(session!, _items);
            _woodDelivery.Update(session!);
            _welfare.Update(session!);
            _hitchhiker.Update(session!);
            _wanted.Update(session!);
            _jail.Update(session!);
            _pursuit.Update(session!);
            _rallyResults.Update(session!);
            _jokkis.Update(session!);
            _appliances.Update(session!);
            _phone.Update(session!);
            _pissAreas.Update(session!);
            _carRadio.Update(session!);

            if (_selfTest)
                _fsm.RunDoorTest();

            if (_cargoTestDelay > 0f)
                _items.RunCargoTest(session!, _cargoTestDelay);

            if (WinterMPPlugin.DevKeysEnabled.Value)
                HandleDevKeys(session);
        }
    }
}
