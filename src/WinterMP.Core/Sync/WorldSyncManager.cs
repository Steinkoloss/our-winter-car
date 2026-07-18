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
    public sealed class WorldSyncManager : MonoBehaviour
    {
        private const float ScanIntervalSeconds = 5f;
        private const float FirstScanDelaySeconds = 2f;
        private const float PendingRetrySeconds = 0.5f;
        private const float TimeSyncIntervalSeconds = 30f;
        private const float ChecksumIntervalSeconds = 20f;
        private const float ResyncCooldownSeconds = 15f;
        private const float ObjectRequestCooldownSeconds = 5f;
        private const int DespawnSnapshotChunk = 80;
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
        private readonly WorldProgressSync _progress = new WorldProgressSync();
        private readonly JobSiteSync _jobSites = new JobSiteSync();

        private bool _syncReady;

        private WorldSyncBridge _bridge = null!;
        private FsmWorldSync _fsm = null!;
        private ItemWorldSync _items = null!;
        private VehicleWorldSync _vehicles = null!;
        private NpcTrafficSync _npcTraffic = null!;

        private string _lastLevel = string.Empty;
        private float _nextScanAt;
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
        private readonly HashSet<string> _spawnDiag = new HashSet<string>();

        internal bool ApplyingRemote { get; set; }
        internal bool SelfTest => _selfTest;
        internal float FirstDoorRegisteredAt { get; set; } = -1f;
        internal float DoorTestDelay => _doorTestDelay;
        internal int DoorTestStep { get; set; }
        internal Transform? LocalPlayer { get; set; }
        internal float NextPlayerSearchAt { get; set; }

        public static WorldSyncManager? Instance { get; private set; }

        public int DoorCount => _syncReady ? _fsm.DoorCount : 0;
        public int SpawnContainerCount => _syncReady ? _fsm.SpawnContainerCount : 0;
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
        }

        private void EnsureSyncReady()
        {
            if (_syncReady) return;

            SyncCatalog.EnsureLoaded();
            Util.BootTrace.Crumb("WorldSyncManager: lazy init");

            _bridge = new WorldSyncBridge(this, _hookedFsms);
            _items = new ItemWorldSync(_bridge);
            _vehicles = new VehicleWorldSync(_bridge, _items);
            _fluids = new FluidContainerSync(_items);
            _npcTraffic = new NpcTrafficSync(_bridge);
            _items.BindVehicles(_vehicles);
            _bridge.BindItems(_items);
            _bridge.BindWallet(_wallet);
            _fsm = new FsmWorldSync(_bridge, _vehicles);
            _syncReady = true;
        }

        private void OnDestroy()
        {
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

        private void LateUpdate()
        {
            if (!_syncReady || _worldSyncDisabled || !_wasSessionActive || Time.unscaledTime < _syncErrorBackoffUntil)
                return;

            if (!IsGameLevel()) return;

            try
            {
                _vehicles.LateUpdateRemoteVehicles(Time.unscaledTime);
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

            if (Time.unscaledTime >= _nextScanAt)
            {
                _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;
                ScanWorld();
            }

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
            _items.UpdateItems(session);
            _npcTraffic.Update(session!);
            _vehicles.UpdateVehicleStates(session!);
            _vehicles.UpdateVehicleClimate(session!);
            _clothing.Update(session!);
            _heat.Update(session!);
            _fluids.Update(session!);
            _progress.Update(session!);
            _jobSites.Update(session!);

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
            _items.Clear();
            _npcTraffic.Clear();
            _hookedFsms.Clear();
            _nextObjectRequestAt.Clear();
            _timeWeather.Reset();
            _wallet.Reset();
            _clothing.Clear();
            _heat.Clear();
            _progress.Reset();
            _jobSites.Clear();
            _snapshotRequested = false;
            _outChecksumSequence = 0;
            _nextChecksumAt = 0f;
            _nextResyncRequestAt = 0f;
            IdHash = 0;
            LocalPlayer = null;
            NextPlayerSearchAt = 0f;
            _nextScanAt = Time.unscaledTime + FirstScanDelaySeconds;
            FirstDoorRegisteredAt = -1f;
        }

        private void ScanWorld()
        {
            int newDoors = 0, newParts = 0, newBuys = 0, newBolts = 0, newIgnitions = 0, newControls = 0, newStarters = 0, newSpawnContainers = 0;

            try
            {
                var fsms = Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM));
                foreach (var obj in fsms)
                {
                    var fsm = obj as PlayMakerFSM;
                    if (fsm == null || _hookedFsms.ContainsKey(fsm)) continue;

                    // TEMP spawn diagnostic: report each grocery-bag-like Use FSM once
                    // (match result + active state) — placed before the active check so
                    // an inactive/disabled bag still shows up with active=false.
                    try
                    {
                        if (fsm.FsmName == "Use")
                        {
                            string nm = fsm.gameObject.name;
                            if (nm.IndexOf("shop", StringComparison.OrdinalIgnoreCase) >= 0 && _spawnDiag.Add(nm))
                            {
                                bool act = fsm.gameObject.activeInHierarchy;
                                bool en = fsm.enabled;
                                var sc = SyncCatalog.TryMatchSpawnContainer(fsm);
                                string diag = $"bag '{nm}' active={act} enabled={en} match={(sc != null)}";
                                WinterMPPlugin.Log.LogInfo("WorldSync: " + diag);
                                Util.BootTrace.Crumb("SPAWN-DIAG " + diag);
                            }
                        }
                    }
                    catch { }

                    try
                    {
                        if (!fsm.gameObject.activeInHierarchy || !fsm.enabled) continue;

                        string fsmName = fsm.FsmName;
                        if (fsmName == "Use")
                        {
                            string[]? states = SyncCatalog.TryMatchDoor(fsm);
                            if (states != null && _fsm.RegisterDoor(fsm, states)) newDoors++;
                            else if ((states = SyncCatalog.TryMatchSpawnContainer(fsm)) != null && _fsm.RegisterSpawnContainer(fsm, states)) newSpawnContainers++;
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
                                        states = SyncCatalog.TryMatchControl(fsm);
                                        if (states != null && _fsm.RegisterControl(fsm, states)) newControls++;
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
                                string[]? states = SyncCatalog.TryMatchControl(fsm);
                                if (states != null && _fsm.RegisterControl(fsm, states)) newControls++;
                            }
                        }
                        else
                        {
                            string[]? states = SyncCatalog.TryMatchStarter(fsm);
                            if (states != null && _fsm.RegisterStarter(fsm, states)) newStarters++;
                            else
                            {
                                states = SyncCatalog.TryMatchControl(fsm);
                                if (states != null && _fsm.RegisterControl(fsm, states)) newControls++;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        WinterMPPlugin.Log.LogDebug($"WorldSync: skipped FSM during scan: {e.Message}");
                    }
                }

                int newItems = _items.ScanItems();
                int newNpcs = _npcTraffic.Scan();
                if (newDoors > 0 || newParts > 0 || newBuys > 0 || newBolts > 0 || newIgnitions > 0 || newControls > 0 || newStarters > 0 || newSpawnContainers > 0 || newItems > 0 || newNpcs > 0)
                {
                    RecomputeIdHash();
                    WinterMPPlugin.Log.LogInfo(
                        $"WorldSync: +{newDoors} doors, +{newSpawnContainers} spawn-containers, +{newParts} parts, +{newBuys} buys, +{newBolts} bolts, +{newIgnitions} ignitions, +{newControls} controls, +{newStarters} starters, +{newItems} items, +{newNpcs} npcs — " +
                        $"now {_fsm.DoorCount}/{_fsm.SpawnContainerCount}/{_fsm.PartCount}/{_fsm.BuyCount}/{_fsm.BoltCount}/{_fsm.IgnitionCount}/{_fsm.ControlCount}/{_fsm.StarterCount}/{_items.ItemCount}/{_npcTraffic.NpcCount} (id hash {IdHash:X8}).");
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
            foreach (var fluid in _fluids.BuildSnapshots(ownerPlayerId))
                yield return fluid;

            foreach (var jobSite in _jobSites.BuildSnapshots())
                yield return jobSite;
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

        public void OnRemoteStateEnter(FsmStateEnter message) { EnsureSyncReady(); _fsm.OnRemoteStateEnter(message); }
        public void OnRemoteRawEvent(FsmRawEvent message) { EnsureSyncReady(); _fsm.OnRemoteRawEvent(message); }
        public void OnRemoteBoltState(BoltState message) { EnsureSyncReady(); _fsm.OnRemoteBoltState(message); }
        public void OnRemotePartState(PartState message) { EnsureSyncReady(); _fsm.OnRemotePartState(message); }
        public void OnRemoteDoorSnapshot(WorldDoorSnapshot message) { EnsureSyncReady(); _fsm.OnRemoteDoorSnapshot(message); }
        public void OnRemoteBoltSnapshot(WorldBoltSnapshot message) { EnsureSyncReady(); _fsm.OnRemoteBoltSnapshot(message); }
        public void OnRemotePartSnapshot(WorldPartSnapshot message) { EnsureSyncReady(); _fsm.OnRemotePartSnapshot(message); }
        public void OnHostPurchaseIntent(PurchaseIntent intent) { EnsureSyncReady(); _fsm.OnHostPurchaseIntent(intent); }

        public void OnRemoteItemDespawn(ItemDespawn message) { EnsureSyncReady(); _items.OnRemoteItemDespawn(message); }
        public void OnRemoteItemSpawn(ItemSpawn message) { EnsureSyncReady(); _items.StartGuestSpawnBind(message); }
        public void OnHostSpawnIntent(SpawnIntent intent) { EnsureSyncReady(); _items.OnHostSpawnIntent(intent); }

        public void OnRemoteItemTransform(ItemTransform message) { EnsureSyncReady(); _items.OnRemoteItemTransform(message); }
        public void OnRemoteNpcTransform(NpcTransform message) { EnsureSyncReady(); _npcTraffic.OnRemoteNpcTransform(message); }
        public void OnRemoteItemSnapshot(WorldItemSnapshot message) { EnsureSyncReady(); _items.OnRemoteItemSnapshot(message); }
        public void OnRemoteItemDespawnSnapshot(WorldItemDespawnSnapshot message) { EnsureSyncReady(); _items.OnRemoteItemDespawnSnapshot(message); }

        public void OnRemoteClothingState(PlayerClothingState message) { EnsureSyncReady(); _clothing.OnRemoteClothingState(message); }
        public void OnRemoteHeatSourceState(HeatSourceState message) { EnsureSyncReady(); _heat.OnRemoteState(message); }
        public void OnHostHeatSourceIntent(HeatSourceIntent message) { EnsureSyncReady(); _heat.OnHostIntent(message); }
        public void OnRemoteFluidContainerState(FluidContainerState message) { EnsureSyncReady(); _fluids.OnRemoteState(message); }
        public void OnRemoteWorldProgressState(WorldProgressState message) { EnsureSyncReady(); _progress.Apply(message); }
        public void OnRemoteJobSiteState(JobSiteState message) { EnsureSyncReady(); _jobSites.Apply(message); }
        public void ForceHeatSourceBroadcast() { EnsureSyncReady(); _heat.ForceBroadcast(); }

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

        public void OnRemoteVehicleState(VehicleState message) { EnsureSyncReady(); _vehicles.OnRemoteVehicleState(message); }
        public void OnRemoteVehicleClimate(VehicleClimate message) { EnsureSyncReady(); _vehicles.OnRemoteVehicleClimate(message); }
        public void OnRemoteVehicleCargo(VehicleCargo message) { EnsureSyncReady(); _items.OnRemoteVehicleCargo(message); }

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
            if (!_syncReady) return;

            _items.ReleaseSession();
            _npcTraffic.ReleaseSession();
            _fsm.Clear();
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
            if (!_wallet.TryGetMoney(out float money)) return 0;
            int mk = Mathf.RoundToInt(money);
            return StableHash.Combine(StableHash.OffsetBasis, (uint)mk);
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
