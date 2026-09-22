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
    /// <summary>
    /// Thin coordinator for M3+ world sync. PlayMaker FSM state lives in
    /// <see cref="FsmWorldSync"/>; rigidbody items in <see cref="ItemWorldSync"/>;
    /// vehicle engine/climate in <see cref="VehicleWorldSync"/>.
    /// </summary>
    public sealed partial class WorldSyncManager : MonoBehaviour
    {
        private const float ScanIntervalSeconds = 5f;
        private const float FirstScanDelaySeconds = 2f;
        private const float PendingRetrySeconds = 0.5f;
        private const float TimeSyncIntervalSeconds = 30f;
        private const float ChecksumIntervalSeconds = 20f;
        private const float ResyncCooldownSeconds = 15f;
        private const float ObjectRequestCooldownSeconds = 5f;
        private const int DespawnSnapshotChunk = WorldItemDespawnSnapshot.MaxItems;
        private const int MaxSyncErrors = 8;
        private const float SyncErrorBackoffSeconds = 1f;

        private const byte NoOwner = 255;

        private readonly Dictionary<PlayMakerFSM, bool> _hookedFsms = new Dictionary<PlayMakerFSM, bool>();
        private readonly Dictionary<uint, float> _nextObjectRequestAt = new Dictionary<uint, float>();
        private readonly TimeWeatherSync _timeWeather = new TimeWeatherSync();
        private readonly WalletSync _wallet = new WalletSync();
        private readonly ClothingSync _clothing = new ClothingSync();
        private readonly HeatSourceSync _heat = new HeatSourceSync();
        private FluidContainerSync _fluids = null!;
        private AtfRefillSync _atf = null!;
        private KiljuSync _kilju = null!;
        private readonly WorldProgressSync _progress = new WorldProgressSync();
        private readonly JobSiteSync _jobSites = new JobSiteSync();
        private readonly MailOrderSync _mailOrders = new MailOrderSync();
        private readonly InspectionSync _inspection = new InspectionSync();
        private PoliceSync _police = null!;
        private readonly HomeStereoSync _homeStereo = new HomeStereoSync();
        private RallySync _rally = null!;
        private IceRaceSync _iceRace = null!;
        private readonly IceRaceEventSync _iceRaceEvent = new IceRaceEventSync();
        private readonly IceRaceResultsSync _iceRaceResults = new IceRaceResultsSync();
        private readonly GamblingSync _gambling = new GamblingSync();
        private readonly PokerSync _poker = new PokerSync();
        private readonly VenttiSync _ventti = new VenttiSync();
        private readonly UtilityBillSync _utilityBills = new UtilityBillSync();
        private readonly LotterySync _lottery = new LotterySync();
        private LottoTicketSync? _lottoTickets;
        private readonly RepairShopSync _repairShop = new RepairShopSync();
        private readonly FleaSaleSync _fleaSale = new FleaSaleSync();
        private readonly TaxiJobSync _taxiJob = new TaxiJobSync();
        private readonly WorldScalarsSync _worldScalars = new WorldScalarsSync();
        private readonly HockeyBettingSync _hockey = new HockeyBettingSync();
        private readonly TrainSync _train = new TrainSync();
        private readonly TractorTrailerSync _trailer = new TractorTrailerSync();
        private readonly FirewoodDeliverySync _woodDelivery = new FirewoodDeliverySync();
        private readonly WelfareSync _welfare = new WelfareSync();
        private readonly HitchhikerSync _hitchhiker = new HitchhikerSync();
        private readonly WantedSync _wanted = new WantedSync();
        private readonly JailSync _jail = new JailSync();
        private readonly PursuitSync _pursuit = new PursuitSync();
        private readonly RallyResultsSync _rallyResults = new RallyResultsSync();
        private readonly JokkisRaceSync _jokkis = new JokkisRaceSync();
        private readonly ApplianceSync _appliances = new ApplianceSync();
        private readonly PhoneSync _phone = new PhoneSync();
        private readonly PissAreaSync _pissAreas = new PissAreaSync();
        private readonly CarRadioSync _carRadio = new CarRadioSync();

        private bool _syncReady;

        private WorldSyncBridge _bridge = null!;
        private FsmWorldSync _fsm = null!;
        private ItemWorldSync _items = null!;
        internal ItemWorldSync ItemSync => _items;
        private VehicleWorldSync _vehicles = null!;
        private NpcTrafficSync _npcTraffic = null!;

        private string _lastLevel = string.Empty;
        private float _nextScanAt;
        private float _nextItemScanAt, _nextNpcScanAt;
        private float _nextPendingAt;
        private float _nextTimeSyncAt;
        private float _nextChecksumAt;
        private float _nextResyncRequestAt;
        private ushort _outChecksumSequence;
        private bool _snapshotRequested;
        private bool _wasSessionActive;
        private float _doorTestDelay;
        private float _cargoTestDelay;
        private bool _selfTest;
        private bool _readyAnnounced;
        private bool _worldSyncDisabled;
        private int _syncErrorCount;
        private float _syncErrorBackoffUntil;
        // TEMP spawn debugging: crumb each grocery-bag-like Use FSM once per name.

        internal bool ApplyingRemote { get; set; }
        internal bool SelfTest => _selfTest;
        internal float FirstDoorRegisteredAt { get; set; } = -1f;
        internal float DoorTestDelay => _doorTestDelay;
        internal int DoorTestStep { get; set; }
        internal Transform? LocalPlayer { get; set; }
        internal float NextPlayerSearchAt { get; set; }

        public static WorldSyncManager? Instance { get; private set; }

        public int DoorCount => _syncReady ? _fsm.DoorCount : 0;
        public int SpawnContainerCount => _syncReady ? _items.BagCount : 0;
        public int PartCount => _syncReady ? _fsm.PartCount : 0;
        public int BuyCount => _syncReady ? _fsm.BuyCount : 0;
        public int BoltCount => _syncReady ? _fsm.BoltCount : 0;
        public int ItemCount => _syncReady ? _items.ItemCount : 0;
        public int NpcCount => _syncReady ? _npcTraffic.NpcCount : 0;
        public uint IdHash { get; private set; }

        public void Configure(LaunchOptions launch)
        {
            _doorTestDelay = launch.DoorTestDelaySeconds;
            _selfTest = launch.DoorTestDelaySeconds > 0f;
            _cargoTestDelay = launch.CargoTestDelaySeconds;
        }

        private void Awake()
        {
            Instance = this;
            ItemWorldSync.InitializeMotorOilStartup();
        }

        internal bool PrepareGuestEngineInputsForAdmission()
        {
            EnsureSyncReady();
            return _items.PrepareGuestEngineInputs(force: true, admission: true);
        }

        internal bool TryReadWheelHealthInput(PlayMakerFSM fsm, int wheel, out byte health)
        {
            health = 0;
            return _syncReady && _vehicles.TryReadWheelHealthInput(fsm, wheel, out health);
        }

        internal bool TryReadGearboxConditionInput(PlayMakerFSM fsm, out byte damage)
        {
            damage = 0;
            return _syncReady && _vehicles.TryReadGearboxConditionInput(fsm, out damage);
        }

        internal bool TryReadDrivetrainWearInput(PlayMakerFSM fsm, int part, out float wear, out bool ready)
        {
            wear = 0; ready = false;
            return _syncReady && _vehicles.TryReadDrivetrainWearInput(fsm, part, out wear, out ready);
        }

        internal bool TryReadGearboxOilInput(PlayMakerFSM fsm, out float oil, out bool ready)
        {
            oil = 0; ready = false;
            return _syncReady && _vehicles.TryReadGearboxOilInput(fsm, out oil, out ready);
        }

        private void EnsureSyncReady()
        {
            if (_syncReady) return;

            SyncCatalog.EnsureLoaded();
            Util.BootTrace.Crumb("WorldSyncManager: lazy init");

            _bridge = new WorldSyncBridge(this, _hookedFsms);
            _items = new ItemWorldSync(_bridge);
            _fleaSale.BindItems(_items);
            _lottoTickets = new LottoTicketSync(_items, _lottery);
            _vehicles = new VehicleWorldSync(_bridge, _items);
            _fluids = new FluidContainerSync(_items);
            _atf = new AtfRefillSync(_items);
            _kilju = new KiljuSync(_items);
            _police = new PoliceSync(_items);
            _rally = new RallySync(_items);
            _iceRace = new IceRaceSync(_items);
            _npcTraffic = new NpcTrafficSync(_bridge);
            _items.BindVehicles(_vehicles);
            _bridge.BindItems(_items);
            _bridge.BindWallet(_wallet);
            _fsm = new FsmWorldSync(_bridge, _vehicles);
            _bridge.BindFsms(_fsm);
            _jail.BindWanted(_wanted);
            _syncReady = true;
        }

        private void OnDestroy()
        {
            if (_syncReady) { _vehicles.ClearDamageState(); _vehicles.ClearVehicleStateStreams(); }
            _gambling.Clear();
            _poker.Clear();
            _ventti.Clear();
            _lottoTickets?.Clear();
            _lottery.Clear();
            _hockey.Clear();
            _trailer.Clear();
            _train.Clear();
            _woodDelivery.Clear();
            _wallet.Reset();
            _welfare.Clear();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_worldSyncDisabled || Time.unscaledTime < _syncErrorBackoffUntil) return;

            try
            {
                UpdateWorldSync();
            }
            catch (Exception e)
            {
                HandleSyncError("Update", e);
            }
        }

        private void FixedUpdate()
        {
            if (_syncReady && !_worldSyncDisabled && _wasSessionActive && IsGameLevel()) _train.FixedUpdate();
        }

        private void LateUpdate()
        {
            if (!_syncReady || _worldSyncDisabled || !_wasSessionActive || Time.unscaledTime < _syncErrorBackoffUntil)
                return;

            if (!IsGameLevel()) return;

            try
            {
                _vehicles.LateUpdateRemoteVehicles(Time.unscaledTime);
                _trailer.LateUpdate();
                var session = SessionManager.Instance;
                if (session != null) _ventti.LateUpdate(session);
            }
            catch (Exception e)
            {
                HandleSyncError("LateUpdate", e);
            }
        }

        /// <summary>
        /// One transient exception must not silently kill world sync for the rest
        /// of the session (a despawn race did exactly that and broke all vehicle
        /// sync). Back off briefly and only disable after repeated failures.
        /// </summary>
        private void HandleSyncError(string phase, Exception e)
        {
            _syncErrorCount++;
            SyncEventLog.Record("error", $"{phase} #{_syncErrorCount}: {e}");

            if (_syncErrorCount >= MaxSyncErrors)
            {
                _worldSyncDisabled = true;
                WinterMPPlugin.Log.LogError($"WorldSync disabled after {_syncErrorCount} errors; last ({phase}): {e}");
                SyncEventLog.DumpToFile();
                return;
            }

            _syncErrorBackoffUntil = Time.unscaledTime + SyncErrorBackoffSeconds;
            WinterMPPlugin.Log.LogWarning(
                $"WorldSync {phase} error {_syncErrorCount}/{MaxSyncErrors} — retrying in {SyncErrorBackoffSeconds:0.#}s: {e}");
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
            _vehicles.UpdateVehicleStates(session!);
            _vehicles.UpdateStarterDraws(session!);
            _vehicles.UpdateStarterWear(session!);
            _vehicles.UpdateVehicleCoolant(session!);
            _vehicles.UpdateDrivetrainWearStates(session!);
            _vehicles.UpdateWheelHealthStates(session!);
            _vehicles.UpdateVehicleDamage(session!);
            _vehicles.UpdateVehicleCondition(session!);
            _vehicles.UpdateFuelTransfers(session!);
            _vehicles.UpdateVehicleClimate(session!);
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

        private static bool IsGameLevel()
        {
            try
            {
                return Application.loadedLevelName == "GAME";
            }
            catch
            {
                return false;
            }
        }

        private void WatchLevelChanges()
        {
            string level;
            try
            {
                level = Application.loadedLevelName ?? string.Empty;
            }
            catch
            {
                return;
            }

            if (level == _lastLevel) return;
            _lastLevel = level;

            if (!_syncReady) return;

            _fsm.Clear();
            _bridge.PartIdentities.Clear();
            _atf.Clear();
            _taxiJob.Clear();
            _items.Clear();
            _npcTraffic.Clear();
            _fluids.Clear();
            _kilju.Clear();
            _hookedFsms.Clear();
            _nextObjectRequestAt.Clear();
            _timeWeather.Reset();
            _wallet.Reset();
            _clothing.Clear();
            _heat.Clear();
            _progress.Reset();
            _jobSites.Clear();
            _mailOrders.Clear();
            _inspection.Clear();
            _police.Clear();
            _homeStereo.Clear();
            _rally.Clear();
            _iceRace.Clear();
            _iceRaceEvent.Clear();
            _iceRaceResults.Clear();
            _gambling.Clear();
            _ventti.Clear();
            _poker.Clear();
            _utilityBills.Clear();
            _lottoTickets?.Clear();
            _lottery.Clear();
            _repairShop.Clear();
            _fleaSale.Clear();
            _worldScalars.Clear();
            _hockey.Clear();
            _trailer.Clear();
            _train.Clear();
            _woodDelivery.Clear();
            _welfare.Clear();
            _hitchhiker.Clear();
            _wanted.Clear();
            _jail.Clear();
            _pursuit.Clear();
            _rallyResults.Clear();
            _jokkis.Clear();
            _appliances.Clear();
            _phone.Clear();
            _pissAreas.Clear();
            _carRadio.Clear();
            _snapshotRequested = false;
            _outChecksumSequence = 0;
            _nextChecksumAt = 0f;
            _nextResyncRequestAt = 0f;
            IdHash = 0;
            LocalPlayer = null;
            NextPlayerSearchAt = 0f;
            _nextScanAt = Time.unscaledTime + FirstScanDelaySeconds;
            _nextItemScanAt = _nextNpcScanAt = 0f;
            FirstDoorRegisteredAt = -1f;
        }

        private void UpdateWorldDiscovery()
        {
            if (ScenePath.TryBeginDiscovery(ref _nextScanAt, ScanIntervalSeconds))
                ScanWorldCore(_nextItemScanAt <= 0f || _nextNpcScanAt <= 0f);

            // Initial discovery completes before join snapshots can be requested.
            // Later passes each own a fresh scan scope on an admitted frame.
            if (_nextItemScanAt > 0f && ScenePath.TryBeginDiscovery(ref _nextItemScanAt, ScanIntervalSeconds))
                ScanWorldObjects(true);
            if (_nextNpcScanAt > 0f && ScenePath.TryBeginDiscovery(ref _nextNpcScanAt, ScanIntervalSeconds))
                ScanWorldObjects(false);
        }

        private void ScanWorld() => ScanWorldCore(true);

        private void ScanWorldCore(bool includeObjects)
        {
            _vehicles.PrepareGuestDamageIsolationNow();
            _vehicles.PrepareGuestEngineProtectionNow();
            _items.RefreshBagFactories();
            _items.PrepareGuestPartIsolation();
            int newDoors = 0, newParts = 0, newBuys = 0, newBolts = 0, newIgnitions = 0, newControls = 0, newStarters = 0;

            try
            {
                var fsms = ScenePath.ScanFsms();
                foreach (var obj in fsms)
                {
                    var fsm = obj as PlayMakerFSM;
                    if (fsm == null || _hookedFsms.ContainsKey(fsm)) continue;

                    try
                    {
                        if (!fsm.gameObject.activeInHierarchy || !fsm.enabled) continue;
                        if (!fsm.Fsm.Initialized || !fsm.Fsm.Started) continue;
                        if (TrainSync.Owns(fsm.transform) || ItemWorldSync.IsCoffeeFsm(fsm) || ItemWorldSync.IsAdvertFsm(fsm) || ItemWorldSync.IsBagUse(fsm)) continue;
                        string fsmName = fsm.FsmName;
                        // The remaining branches accept only catalogued starter or
                        // control names. Avoid walking saved-part parents for graphs
                        // that none of this scan's registration paths can accept.
                        if (fsmName != "Use" && fsmName != "Screw" && fsmName != "Buy" && fsmName != "Data" && fsmName != "Button"
                            && !SyncCatalog.HasStarterOrControlRules(fsmName)) continue;
                        // Parent lookup is needed only for a protected guest's saved
                        // parts. Hosts cannot defer discovery for guest isolation.
                        var session = SessionManager.Instance;
                        if (GuestSaveGuard.ProtectWorld && session != null && !session.IsHost)
                        {
                            var nativePart = NativePartIdentity.FindData(fsm.transform);
                            if (nativePart != null && _items.IsPendingGuestPartIsolation(nativePart)) continue;
                        }

                        if (fsmName == "Use")
                        {
                            string[]? states = SyncCatalog.TryMatchDoor(fsm);
                            if (states != null && _fsm.RegisterDoor(fsm, states)) newDoors++;
                            else
                            {
                                states = SyncCatalog.TryMatchIgnition(fsm);
                                if (states != null && _fsm.RegisterIgnition(fsm, states)) newIgnitions++;
                                else
                                {
                                    states = SyncCatalog.TryMatchSwitch(fsm);
                                    if (states != null && _fsm.RegisterControl(fsm, states)) newControls++;
                                    else
                                    {
                                        var control = SyncCatalog.TryMatchControl(fsm);
                                        if (control != null && _fsm.RegisterControl(fsm, control)) newControls++;
                                        else if (FsmWorldSync.ClassifyBuy(fsm, out var useBuyProfile) && _fsm.RegisterBuy(fsm, useBuyProfile))
                                            newBuys++;
                                    }
                                }
                            }
                        }
                        else if (fsmName == "Screw")
                        {
                            if (SyncCatalog.TryMatchBolt(fsm) && _fsm.RegisterBolt(fsm))
                                newBolts++;
                        }
                        else if (fsmName == "Buy")
                        {
                            if (FsmWorldSync.ClassifyBuy(fsm, out var buyProfile) && _fsm.RegisterBuy(fsm, buyProfile))
                                newBuys++;
                        }
                        else if (fsmName == "Data")
                        {
                            string[]? partStates = SyncCatalog.TryMatchPart(fsm);
                            if (partStates != null && _fsm.RegisterPart(fsm, partStates))
                                newParts++;
                            else if (FsmWorldSync.ClassifyBuy(fsm, out var dataBuy) && _fsm.RegisterBuy(fsm, dataBuy))
                                newBuys++;
                        }
                        else if (fsmName == "Button")
                        {
                            if (FsmWorldSync.ClassifyBuy(fsm, out var buttonBuy) && _fsm.RegisterBuy(fsm, buttonBuy))
                                newBuys++;
                            else
                            {
                                var control = SyncCatalog.TryMatchControl(fsm);
                                if (control != null && _fsm.RegisterControl(fsm, control)) newControls++;
                            }
                        }
                        else
                        {
                            string[]? states = SyncCatalog.TryMatchStarter(fsm);
                            if (states != null && _fsm.RegisterStarter(fsm, states)) newStarters++;
                            else
                            {
                                var control = SyncCatalog.TryMatchControl(fsm);
                                if (control != null && _fsm.RegisterControl(fsm, control)) newControls++;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        WinterMPPlugin.Log.LogDebug($"WorldSync: skipped FSM during scan: {e.Message}");
                    }
                }

                int newItems = includeObjects ? _items.ScanItems() : 0;
                int newNpcs = includeObjects ? _npcTraffic.Scan() : 0;
                if (includeObjects) _nextItemScanAt = _nextNpcScanAt = Time.unscaledTime + ScanIntervalSeconds;
                if (newDoors > 0 || newParts > 0 || newBuys > 0 || newBolts > 0 || newIgnitions > 0 || newControls > 0 || newStarters > 0 || newItems > 0 || newNpcs > 0)
                {
                    RecomputeIdHash();
                    WinterMPPlugin.Log.LogInfo(
                        $"WorldSync: +{newDoors} doors, +{newParts} parts, +{newBuys} buys, +{newBolts} bolts, +{newIgnitions} ignitions, +{newControls} controls, +{newStarters} starters, +{newItems} items, +{newNpcs} npcs — " +
                        $"now {_fsm.DoorCount}/{_items.BagCount}/{_fsm.PartCount}/{_fsm.BuyCount}/{_fsm.BoltCount}/{_fsm.IgnitionCount}/{_fsm.ControlCount}/{_fsm.StarterCount}/{_items.ItemCount}/{_npcTraffic.NpcCount} (id hash {IdHash:X8}).");
                }

                if (_selfTest && !_readyAnnounced && _fsm.DoorCount > 0)
                {
                    _readyAnnounced = true;
                    SessionManager.Instance?.SendChat(
                        $"[ws] ready: {_fsm.DoorCount} doors, {_fsm.BoltCount} bolts, {_items.ItemCount} items, hash {IdHash:X8}");
                }
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogError($"WorldSync: world scan failed: {e}");
            }
        }

        private void ScanWorldObjects(bool items)
        {
            try
            {
                int added = items ? _items.ScanItems() : _npcTraffic.Scan();
                if (added <= 0) return;
                RecomputeIdHash();
                WinterMPPlugin.Log.LogInfo($"WorldSync: +{added} {(items ? "items" : "npcs")} — now {_items.ItemCount} items, {_npcTraffic.NpcCount} npcs (id hash {IdHash:X8}).");
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogError($"WorldSync: {(items ? "item" : "NPC")} scan failed: {e}");
            }
        }


        private void RecomputeIdHash()
        {
            var ids = new List<uint>(_fsm.DoorCount + _fsm.PartCount + _fsm.BuyCount + _fsm.BoltCount + _items.ItemCount + 64);
            _fsm.CollectIds(ids);
            foreach (uint id in _items.Items.Keys) ids.Add(id);
            ids.Sort();

            uint hash = StableHash.OffsetBasis;
            foreach (uint id in ids)
                hash = StableHash.Combine(hash, id);
            IdHash = hash;
        }

        public TimeSync? BuildTimeSync() => _timeWeather.BuildMessage();
        public WalletState? BuildWalletState() => _wallet.BuildMessage();

        /// <summary>Push an immediate clock snapshot (e.g. after host sleep consent completes).</summary>
        public void BroadcastTimeSyncNow()
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || session.PlayerCount == 0) return;

            var time = _timeWeather.BuildMessage();
            if (time == null) return;

            session.SendWorldMessage(time, Channel.ReliableOrdered);
            _nextTimeSyncAt = Time.unscaledTime + TimeSyncIntervalSeconds;
        }



        public bool TryGetRemoteClothing(byte playerId, out byte stage, out byte type)
        {
            if (!_syncReady) { stage = 0; type = 0; return false; }
            return _clothing.TryGetClothing(playerId, out stage, out type);
        }

        public void ForgetRemoteClothing(byte playerId) => _clothing.Forget(playerId);

        // Host -> joining guest: current clothing of every player but the joiner. Clothing
        // isn't part of the chunked snapshot, so a joiner needs this burst to see already-
        // dressed players correctly (see ClothingSync.BuildSnapshot).
        public IEnumerable<PlayerClothingState> BuildClothingSnapshot(byte localPlayerId, byte excludePlayerId)
        {
            if (!_syncReady) yield break;
            foreach (var clothing in _clothing.BuildSnapshot(localPlayerId, excludePlayerId))
                yield return clothing;
        }

        public struct VehicleInfo
        {
            public uint Id;
            public Rigidbody Body;
        }

        public void CollectVehicles(List<VehicleInfo> results)
        {
            EnsureSyncReady();
            _items.CollectVehicles(results);
        }

        public bool TryGetDriverAnchor(byte playerId, out Transform? seat, out Transform? vehicle)
        {
            EnsureSyncReady();
            return _items.TryGetDriverAnchor(playerId, out seat, out vehicle);
        }

        public bool IsLocalPlayerDrivingAny()
        {
            if (!_syncReady) return false;
            return _items.IsLocalPlayerDrivingAny();
        }

        public uint? FindNearestResyncTarget(float maxDistance = 15f)
        {
            EnsureSyncReady();
            FindLocalPlayer();
            if (LocalPlayer == null) return null;

            float maxSq = maxDistance * maxDistance;
            uint bestId = 0;
            float bestSq = maxSq;

            foreach (var pair in _items.Items)
            {
                var body = pair.Value.Body;
                if (body == null) continue;

                float sq = (LocalPlayer.position - body.transform.position).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    bestId = pair.Key;
                }
            }

            if (bestId != 0) return bestId;

            return _fsm.FindNearestId(LocalPlayer.position, maxSq, ref bestId, ref bestSq) != 0 ? bestId : (uint?)null;
        }

        internal void FindLocalPlayer()
        {
            if (LocalPlayer != null)
            {
                if ((UnityEngine.Object)LocalPlayer != null)
                    return;
                LocalPlayer = null;
            }

            if (Time.unscaledTime < NextPlayerSearchAt) return;
            NextPlayerSearchAt = Time.unscaledTime + WorldSyncBridge.PlayerSearchIntervalSeconds;

            var playerObject = GameObject.Find(WorldSyncBridge.PlayerObjectName);
            if (playerObject != null)
                LocalPlayer = playerObject.transform;
        }

        private void ReleaseEverything()
        {
            _nextItemScanAt = _nextNpcScanAt = 0f;
            _wallet.Reset();
            if (!_syncReady) return;

            _atf.Clear();
            _taxiJob.Clear();
            _items.ReleaseSession();
            _npcTraffic.ReleaseSession();
            _fsm.Clear();
            _bridge.PartIdentities.Clear();
            _police.Clear();
            _homeStereo.Clear();
            _rally.Clear();
            _iceRace.Clear();
            _iceRaceEvent.Clear();
            _iceRaceResults.Clear();
            _gambling.Clear();
            _ventti.Clear();
            _poker.Clear();
            _utilityBills.Clear();
            _lottoTickets?.Clear();
            _lottery.Clear();
            _repairShop.Clear();
            _fleaSale.Clear();
            _worldScalars.Clear();
            _hockey.Clear();
            _trailer.Clear();
            _train.Clear();
            _woodDelivery.Clear();
            _welfare.Clear();
            _hitchhiker.Clear();
            _wanted.Clear();
            _jail.Clear();
            _pursuit.Clear();
            _rallyResults.Clear();
            _jokkis.Clear();
            _appliances.Clear();
            _phone.Clear();
            _pissAreas.Clear();
            _carRadio.Clear();
            _nextObjectRequestAt.Clear();
            _snapshotRequested = false;
            _worldSyncDisabled = false;
            _syncErrorCount = 0;
            _syncErrorBackoffUntil = 0f;
            SyncEventLog.Clear();
        }

        internal bool IsLocalPlayerDriving(SyncedItem item)
        {
            if (!_syncReady) return false;
            return _items.IsLocalPlayerDriving(item);
        }

        public bool IsDisabled => _worldSyncDisabled;

        private uint ComputeWalletCrc()
        {
            return _wallet.ComputeCrc();
        }

        private void HandleDevKeys(SessionManager session)
        {
            if (Input.GetKeyDown(KeyCode.F6))
                TryDevResyncNearest(session);
            else if (Input.GetKeyDown(KeyCode.F7))
            {
                SyncEventLog.DumpToLog();
                SyncEventLog.DumpToFile();
            }
        }

        private void TryDevResyncNearest(SessionManager session)
        {
            uint? netId = FindNearestResyncTarget();
            if (!netId.HasValue)
            {
                WinterMPPlugin.Log.LogWarning("WorldSync: F6 resync — nothing registered nearby.");
                return;
            }

            SyncEventLog.Record("resync-req", $"{netId.Value:X8} (manual F6)");
            if (session.IsHost)
                session.SendChat($"[ws] nearest id {netId.Value:X8} (host has authority)");
            else
                RequestObjectState(netId.Value);
        }
    }
}
