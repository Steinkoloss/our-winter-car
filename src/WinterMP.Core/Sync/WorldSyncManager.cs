using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// M3 world sync, first slice: doors, world items (pickable rigidbodies) and
    /// the RIVETT's bolts (PLAN §4.2/§4.4).
    ///
    /// Doors  — every "Use" FSM with door states gets a state-enter hook; entering
    ///          "Open door"/"Close door" locally is broadcast and replayed remotely
    ///          through an injected MP_* global transition, so the real state
    ///          actions (animation, sound, save flags) run on every machine.
    /// Bolts  — "Screw" FSMs broadcast TIGHTEN/UNTIGHTEN when the local wrench
    ///          turns them; receivers deliver the same event to their copy, which
    ///          re-runs the game's own tightness logic.
    /// Items  — pickable rigidbodies ((itemx), food with a Use::Destroy FSM, …) are
    ///          claimed by whoever moves them nearby and streamed at 10 Hz;
    ///          receivers freeze their copy kinematic while the stream lasts and
    ///          restore physics at the final resting pose. Eating/destruction
    ///          broadcasts <see cref="ItemDespawn"/>.
    /// Vehicles — heavy root rigidbodies (SORBET, KEKMET, GIFU...) use the same
    ///          ownership stream at 15 Hz with two extras: the machine whose player
    ///          sits *in* the vehicle is the driver and out-claims everyone, and
    ///          remote copies ease toward targets instead of teleporting. While a
    ///          remote driver holds a vehicle its seat trigger is blocked locally
    ///          and the driver's avatar rides along in the cabin. Engine state is
    /// Snapshot — a guest requests the world state once its first scan completes;
    ///          the host answers with every door it has seen change plus the
    ///          current pose of every item/vehicle, then keeps the clock and
    ///          weather forecast aligned via periodic <see cref="TimeSync"/>.
    ///
    /// Bolt tightness is reconciled via <see cref="BoltState"/> after each turn
    /// and <see cref="WorldBoltSnapshot"/> on join. Car-part Installed/Tightness/Wear
    /// is reconciled via <see cref="PartState"/> and <see cref="WorldPartSnapshot"/>.
    /// </summary>
    public sealed class WorldSyncManager : MonoBehaviour
    {
        private const float ScanIntervalSeconds = 5f;
        private const float FirstScanDelaySeconds = 2f;
        private const float PendingRetrySeconds = 0.5f;
        private const float PendingTtlSeconds = 120f;

        private const float ItemSendRateHz = 10f;
        private const float VehicleSendRateHz = 15f;
        /// <summary>You can only push/carry items near you; claims need proximity.</summary>
        private const float ItemClaimRadius = 4f;
        /// <summary>Vehicle claims: covers the cabin (driver) and pushing from any side.</summary>
        private const float VehicleClaimRadius = 7f;
        /// <summary>Remote stream is considered live for this long after the last packet.</summary>
        private const float RemoteHoldSeconds = ItemTransformPolicy.ItemRemoteHoldSeconds;
        /// <summary>Remote drivers keep the vehicle frozen longer — packet loss must not
        /// wake local physics and let nearby players fight over the car.</summary>
        private const float RemoteDriverHoldSeconds = ItemTransformPolicy.DriverRemoteHoldSeconds;
        /// <summary>A seated driver holds the vehicle with keepalives even when parked,
        /// so ownership can't flap when two players sit in (their copies of) one car.</summary>
        private const float DriverKeepaliveSeconds = 0.4f;
        /// <summary>Owner releases an item (final packet) after this long without motion.</summary>
        private const float ItemStillSeconds = 1.5f;
        private const float VehicleStillSeconds = 3f;
        private const float MoveEpsilonSqr = 1e-6f; // 1 mm — carried items move slowly
        private const float VehicleMoveEpsilonSqr = 2.5e-3f; // 5 cm — ignore idle-engine jitter
        /// <summary>Root rigidbodies at least this heavy are treated as vehicles.</summary>
        /// <summary>Remote pose smoothing (same feel as RemoteAvatar).</summary>
        private const float RemoteLerpSpeed = 12f;
        private const float RemoteSnapDistance = 15f;
        /// <summary>Items inside a remote-driven vehicle are never claimed locally —
        /// the vehicle's owner streams them; claiming them here would create the
        /// kinematic-battering-ram feedback loop that dragged cars around in v1.</summary>
        private const float VehicleInteriorRadius = 3.5f;
        private const float PlayerSearchIntervalSeconds = 2f;
        private const string PlayerObjectName = "PLAYER";

        /// <summary>Engine state stream rate (audio only — coarse is fine).</summary>
        private const float VehicleStateRateHz = 4f;
        /// <summary>Frost + heater knobs — slow enough to save bandwidth, fast enough to feel live.</summary>
        private const float VehicleClimateRateHz = 2f;
        private const float HeaterTempMax = 30f;
        private const float HeaterBlowerMax = 4f;
        private const float HeaterDirectionMax = 4f;
        private const float CabinTempMaxC = 40f;
        private const float CoolantTempMaxC = 120f;
        private const float ClimateProbeIntervalSeconds = 3f;
        /// <summary>Keep pushing frost/defrost visuals after the last climate packet.</summary>
        private const float ClimateHoldSeconds = 3f;
        private const float DefrostPulseSeconds = 0.5f;
        /// <summary>Remote engine audio stops when no state arrived for this long.</summary>
        private const float EngineAudioHoldSeconds = 2f;
        private const float EnginePitchBase = 0.55f;
        private const float EnginePitchPerRpm = 1f / 6500f;
        private const float EnginePitchMin = 0.65f;
        private const float EnginePitchMax = 1.65f;
        private const float EngineAudioVolume = 0.8f;
        private const float SystemsProbeIntervalSeconds = 3f;
        /// <summary>FuelLine.Revs above this counts as engine running (idle ~800).</summary>
        private const float EngineRunningRevs = 250f;
        private const float TachMaxRpm = 7000f;
        private const float SpeedoMaxKmh = 140f;
        private const float FuelTankDefaultLiters = 30f;

        /// <summary>Host broadcasts the clock/weather this often (drift is slow).</summary>
        private const float TimeSyncIntervalSeconds = 30f;
        private const float ChecksumIntervalSeconds = 20f;
        private const float ResyncCooldownSeconds = 15f;
        private const float ObjectRequestCooldownSeconds = 5f;
        private const float WalletSyncIntervalSeconds = 2f;
        /// <summary>Snapshot poses for not-yet-scanned items stay parked this long.</summary>
        private const float SnapshotPoseTtlSeconds = 300f;
        private const int DoorSnapshotChunk = 60;
        private const int BoltSnapshotChunk = 80;
        private const int PartSnapshotChunk = 80;
        private const int ItemSnapshotChunk = 40;
        private const int DespawnSnapshotChunk = 80;

        public static WorldSyncManager? Instance { get; private set; }

        public int DoorCount => _doors.Count;
        public int PartCount => _parts.Count;
        public int BuyCount => _buys.Count;
        public int BoltCount => _bolts.Count;
        public int ItemCount => _items.Count;
        /// <summary>Order-independent hash over all registered ids — compare across machines.</summary>
        public uint IdHash { get; private set; }

        private sealed class SyncedDoor
        {
            public PlayMakerFSM Fsm = null!;
            public string Path = string.Empty;
            public string[] SyncedStates = null!;
            /// <summary>Last synced state seen locally or applied remotely — the
            /// join snapshot sends this so late joiners converge.</summary>
            public string? LastSyncedState;
        }

        private sealed class SyncedPart
        {
            public PlayMakerFSM Fsm = null!;
            public string Path = string.Empty;
            public string[] SyncedStates = null!;
            public string? LastSyncedState;
            public HutongGames.PlayMaker.FsmBool? InstalledVar;
            public HutongGames.PlayMaker.FsmFloat? TightnessVar;
            public HutongGames.PlayMaker.FsmFloat? WearVar;
        }

        private struct PendingPartState
        {
            public byte Flags;
            public byte Tightness;
            public byte Wear;
            public float ExpiresAt;
        }

        private struct BuyEntryGuard
        {
            public string StateName;
            public string TriggerEvent;
        }

        private struct BuyProfile
        {
            public BuyEntryGuard[] EntryGuards;
            public string[] ResultStates;
        }

        private sealed class SyncedBuy
        {
            public PlayMakerFSM Fsm = null!;
            public string Path = string.Empty;
            public BuyEntryGuard[] EntryGuards = null!;
            public string[] ResultStates = null!;
            public string? LastSyncedState;
        }

        private struct PendingPurchaseIntent
        {
            public uint NetId;
            public string EventName;
            public float ExpiresAt;
        }

        private sealed class SyncedBolt
        {
            public PlayMakerFSM Fsm = null!;
            public string Path = string.Empty;
            public HutongGames.PlayMaker.FsmInt? BoltTightnessVar;
            public HutongGames.PlayMaker.FsmInt? ScrewIntVar;
            public HutongGames.PlayMaker.FsmFloat? TightnessFVar;
            public HutongGames.PlayMaker.FsmFloat? ScrewFloatVar;
        }

        private struct PendingBoltState
        {
            public ushort BoltTightness;
            public ushort ScrewInt;
            public float ExpiresAt;
        }

        private sealed class SyncedIgnition
        {
            public PlayMakerFSM Fsm = null!;
            public string Path = string.Empty;
            public string[] SyncedStates = null!;
            public string? LastSyncedState;
        }

        private sealed class SyncedControl
        {
            public PlayMakerFSM Fsm = null!;
            public string Path = string.Empty;
            public string[] SyncedStates = null!;
            public string? LastSyncedState;
        }

        private sealed class SyncedStarter
        {
            public PlayMakerFSM Fsm = null!;
            public string Path = string.Empty;
            public string[] SyncedStates = null!;
            public string? LastSyncedState;
        }

        private sealed class SyncedItem
        {
            public Rigidbody Body = null!;
            public string Path = string.Empty;
            public uint Id;
            public bool IsVehicle;

            public bool KinematicSaved;
            public bool OriginalKinematic;

            public byte RemoteOwner = NoOwner;
            public bool RemoteIsDriver;
            public bool RemoteVehicleStream;
            public float LastRemoteAt = -999f;
            public ushort LastRemoteSequence;
            public Vector3 TargetPosition;
            public Quaternion TargetRotation = Quaternion.identity;

            public bool LocallyOwned;
            public bool DespawnSent;
            public ushort OutSequence;
            public float NextSendAt;

            public Vector3 LastPosition;
            public float LastMovedAt = -999f;

            // Vehicles only: the game's drive trigger (seat). Blocked while a
            // remote driver holds the vehicle; also anchors the driver's avatar.
            public bool SeatSearched;
            public Transform? SeatTransform;
            public Transform? DriverAnchorTransform;
            public Collider? SeatCollider;
            public bool SeatBlocked;

            // Vehicles only: stays true from claim/drive until rest final — MWC often
            // leaves PLAYER at the door while the rigidbody moves away.
            public bool LocalDriveActive;

            // Vehicles only: engine / ignition sync (M4).
            public bool SystemsReady;
            public float NextSystemsProbeAt;
            public GameObject? AudioEngine;
            public HutongGames.PlayMaker.FsmFloat? EngineRevsVar;
            public HutongGames.PlayMaker.FsmFloat? GaugeSpeedVar;
            public HutongGames.PlayMaker.FsmFloat? GaugeSpeedAngleVar;
            public HutongGames.PlayMaker.FsmFloat? GaugeRpmVar;
            public HutongGames.PlayMaker.FsmFloat? GaugeTachRevsVar;
            public HutongGames.PlayMaker.FsmFloat? GaugeTachRotationVar;
            public HutongGames.PlayMaker.FsmFloat? GaugeFuelLevelVar;
            public HutongGames.PlayMaker.FsmFloat? GaugeFuelAngleVar;
            public HutongGames.PlayMaker.FsmFloat? FuelTankLevelVar;
            public HutongGames.PlayMaker.FsmFloat? FuelTankCapacityVar;
            public PlayMakerFSM? TurnSignalStalkFsm;
            public PlayMakerFSM? TurnSignalsFsm;
            public PlayMakerFSM? HazardButtonFsm;
            public HutongGames.PlayMaker.FsmBool? BlinkerLeftVar;
            public HutongGames.PlayMaker.FsmBool? BlinkerRightVar;
            public HutongGames.PlayMaker.FsmBool? BlinkerHazardsVar;
            public HutongGames.PlayMaker.FsmFloat? GaugeCoolantVar;
            public HutongGames.PlayMaker.FsmFloat? GaugeCoolantAngleVar;
            public bool RemoteHazard;
            public byte RemoteCoolantTemp;
            public GameObject? GaugeTachNeedle;
            public PlayMakerFSM? ElectricityPowerFsm;
            public PlayMakerFSM? GaugeTachDataFsm;
            public ushort OutVehicleStateSequence;
            public float NextVehicleStateAt;
            public ushort LastVehicleStateSequence;
            public bool RemoteEngineOn;
            public bool RemoteAccOn;
            public bool RemoteElectricsApplied;
            public float RemoteRpm;
            public float RemoteSpeedKmh;
            public byte RemoteFuelLevel;
            public bool RemoteBlinkerLeft;
            public bool RemoteBlinkerRight;
            public float RemoteEngineUntil = -999f;
            public AudioSource? RemoteEngineAudio;
            public bool RemoteEngineAudioSearched;
            public bool LoggedEngineSend;

            // Vehicles only: frost + heater (M4).
            public bool ClimateReady;
            public PlayMakerFSM? GlassFrostingFsm;
            public PlayMakerFSM? FreezingFsm;
            public PlayMakerFSM? CarTempDataFsm;
            public HutongGames.PlayMaker.FsmFloat? FrostVar;
            public HutongGames.PlayMaker.FsmFloat? SweatRateVar;
            public HutongGames.PlayMaker.FsmFloat? GlassTempVar;
            public HutongGames.PlayMaker.FsmBool? PlayerInVar;
            public HutongGames.PlayMaker.FsmFloat? InteriorTempVar;
            public HutongGames.PlayMaker.FsmColor? FrostColorVar;
            public HutongGames.PlayMaker.FsmMaterial? FrostGlassMat;
            public HutongGames.PlayMaker.FsmFloat? CutoffWindshieldVar;
            public HutongGames.PlayMaker.FsmFloat? CutoffSideLeftVar;
            public HutongGames.PlayMaker.FsmFloat? CutoffSideRightVar;
            public HutongGames.PlayMaker.FsmFloat? CutoffDoorLeftVar;
            public HutongGames.PlayMaker.FsmFloat? CutoffDoorRightVar;
            public HutongGames.PlayMaker.FsmFloat? CutoffRearVar;
            public PlayMakerFSM? HeaterUnitFsm;
            public PlayMakerFSM? WindowHeaterButtonFsm;
            public HutongGames.PlayMaker.FsmFloat? HeaterSettingTemp;
            public HutongGames.PlayMaker.FsmFloat? HeaterSettingBlower;
            public HutongGames.PlayMaker.FsmFloat? HeaterSettingDirection;
            public HutongGames.PlayMaker.FsmBool? GlassDefrostingVar;
            public PlayMakerFSM? KnobTempFsm;
            public PlayMakerFSM? KnobBlowerFsm;
            public PlayMakerFSM? KnobDirectionFsm;
            public HutongGames.PlayMaker.FsmFloat? KnobTempSetting;
            public HutongGames.PlayMaker.FsmFloat? KnobBlowerSetting;
            public HutongGames.PlayMaker.FsmFloat? KnobDirectionSetting;
            public HutongGames.PlayMaker.FsmFloat? KnobTempAngle;
            public HutongGames.PlayMaker.FsmFloat? KnobBlowerAngle;
            public HutongGames.PlayMaker.FsmFloat? KnobDirectionAngle;
            public string KnobBlowerCommitState = "Set";
            public HutongGames.PlayMaker.FsmBool? WindowHeaterOnVar;
            public byte RemoteHeaterTemp;
            public byte RemoteHeaterBlower;
            public byte RemoteHeaterDirection;
            public byte PresentedHeaterTemp;
            public byte PresentedHeaterBlower;
            public byte PresentedHeaterDirection;
            public ushort OutClimateSequence;
            public float NextClimateAt;
            public ushort LastClimateSequence;
            public byte RemoteFrost;
            public byte RemoteFog;
            public byte RemoteCabinTemp;
            public bool RemotePlayerIn;
            public bool RemoteWindowHeater;
            public bool RemoteGlassDefrosting;
            public float RemoteClimateUntil = -999f;
            public float NextClimateProbeAt;
            public float NextDefrostPulseAt;
            public bool LoggedClimateSend;
            public bool LoggedClimateApply;

            public float ClaimRadius => IsVehicle ? VehicleClaimRadius : ItemClaimRadius;
            public float SendRateHz => IsVehicle ? VehicleSendRateHz : ItemSendRateHz;
            public float StillSeconds => IsVehicle ? VehicleStillSeconds : ItemStillSeconds;
            public float MoveThresholdSqr => IsVehicle ? VehicleMoveEpsilonSqr : MoveEpsilonSqr;
        }

        private struct PendingFsmApply
        {
            public uint NetId;
            public bool IsRawEvent;
            public string Name;
            public float ExpiresAt;
        }

        private struct PendingPose
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public float ExpiresAt;
        }

        private const byte NoOwner = 255;

        /// <summary>Raw events we are willing to replay into game FSMs (anti-foot-gun).</summary>
        private static readonly string[] AllowedRawEvents = { "TIGHTEN", "UNTIGHTEN" };

        private readonly Dictionary<uint, SyncedDoor> _doors = new Dictionary<uint, SyncedDoor>();
        private readonly Dictionary<uint, SyncedPart> _parts = new Dictionary<uint, SyncedPart>();
        private readonly Dictionary<uint, SyncedBuy> _buys = new Dictionary<uint, SyncedBuy>();
        private readonly Dictionary<uint, SyncedBolt> _bolts = new Dictionary<uint, SyncedBolt>();
        private readonly Dictionary<uint, SyncedIgnition> _ignitions = new Dictionary<uint, SyncedIgnition>();
        private readonly Dictionary<uint, SyncedControl> _controls = new Dictionary<uint, SyncedControl>();
        private readonly Dictionary<uint, SyncedStarter> _starters = new Dictionary<uint, SyncedStarter>();
        private readonly Dictionary<uint, SyncedItem> _items = new Dictionary<uint, SyncedItem>();
        private readonly Dictionary<PlayMakerFSM, bool> _hookedFsms = new Dictionary<PlayMakerFSM, bool>();
        private readonly Dictionary<Rigidbody, bool> _trackedBodies = new Dictionary<Rigidbody, bool>();
        private readonly List<PendingFsmApply> _pending = new List<PendingFsmApply>();
        /// <summary>Snapshot poses waiting for their item to be scanned in.</summary>
        private readonly Dictionary<uint, PendingPose> _pendingItemPoses = new Dictionary<uint, PendingPose>();
        private readonly Dictionary<uint, PendingBoltState> _pendingBoltStates = new Dictionary<uint, PendingBoltState>();
        private readonly Dictionary<uint, PendingPartState> _pendingPartStates = new Dictionary<uint, PendingPartState>();
        private readonly List<PendingPurchaseIntent> _pendingPurchaseIntents = new List<PendingPurchaseIntent>();
        private readonly Dictionary<uint, float> _nextObjectRequestAt = new Dictionary<uint, float>();
        private readonly HashSet<uint> _sessionDespawnedItems = new HashSet<uint>();
        private readonly HashSet<uint> _pendingDespawnedItems = new HashSet<uint>();
        private readonly TimeWeatherSync _timeWeather = new TimeWeatherSync();
        private readonly WalletSync _wallet = new WalletSync();

        /// <summary>True while a remote event is being replayed, so hooks don't echo it back.</summary>
        private bool _applyingRemote;

        private string _lastLevel = string.Empty;
        private float _nextScanAt;
        private float _nextPendingAt;
        private float _nextTimeSyncAt;
        private float _nextChecksumAt;
        private float _nextResyncRequestAt;
        private ushort _outChecksumSequence;
        private bool _snapshotRequested;
        private ushort _outPurchaseSequence;
        private Transform? _localPlayer;
        private float _nextPlayerSearchAt;
        private bool _wasSessionActive;

        // Test tooling (Local2PTest.bat / -wintermp-autoload / -wintermp-doortest).
        private bool _autoLoad;
        private float _doorTestDelay;
        /// <summary>Self-test mode: fire the door test and ack remote applies via chat.
        /// Both instances share one BepInEx log file (the second can't lock it), so chat —
        /// which the host logs for every player — is the only single-file evidence channel.</summary>
        private bool _selfTest;
        private int _autoLoadStep;
        private float _autoLoadNextAt;
        private float _firstDoorRegisteredAt = -1f;
        private int _doorTestStep;
        private bool _readyAnnounced;
        private bool _worldSyncDisabled;

        public void Configure(LaunchOptions launch)
        {
            _autoLoad = launch.AutoLoadSave;
            _doorTestDelay = launch.DoorTestDelaySeconds;
            _selfTest = launch.DoorTestDelaySeconds > 0f;
        }

        private void Awake()
        {
            Instance = this;
            SyncCatalog.Load();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_worldSyncDisabled) return;

            try
            {
                UpdateWorldSync();
            }
            catch (Exception e)
            {
                _worldSyncDisabled = true;
                WinterMPPlugin.Log.LogError($"WorldSync disabled after unhandled error: {e}");
                SyncEventLog.Record("fatal", e.ToString());
                SyncEventLog.DumpToFile();
            }
        }

        private void UpdateWorldSync()
        {
            WatchLevelChanges();

            var session = SessionManager.Instance;
            bool sessionActive = session != null
                && (session.State == SessionState.Hosting || session.State == SessionState.Connected);

            if (_autoLoad)
                RunAutoLoad(session, sessionActive);

            if (!sessionActive)
            {
                if (_wasSessionActive) ReleaseEverything();
                _wasSessionActive = false;
                return;
            }

            _wasSessionActive = true;

            if (Time.unscaledTime >= _nextScanAt)
            {
                _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;
                ScanWorld();
            }

            if (Time.unscaledTime >= _nextPendingAt)
            {
                _nextPendingAt = Time.unscaledTime + PendingRetrySeconds;
                ProcessPending();
            }

            // A guest with a scanned world asks the host for the join snapshot;
            // the host keeps every clock aligned.
            if (!session!.IsHost && !_snapshotRequested && _doors.Count > 0)
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

            UpdateItems(session);
            UpdateVehicleStates(session!);
            UpdateVehicleClimate(session!);

            if (_selfTest)
                RunDoorTest();

            if (WinterMPPlugin.DevKeysEnabled.Value)
                HandleDevKeys(session);
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

        /// <summary>Nearest registered net id for manual / dev resync (items, then FSMs).</summary>
        public uint? FindNearestResyncTarget(float maxDistance = 15f)
        {
            FindLocalPlayer();
            if (_localPlayer == null) return null;

            float maxSq = maxDistance * maxDistance;
            uint bestId = 0;
            float bestSq = maxSq;

            foreach (var pair in _items)
            {
                var body = pair.Value.Body;
                if (body == null) continue;

                float sq = (_localPlayer.position - body.transform.position).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    bestId = pair.Key;
                }
            }

            if (bestId != 0) return bestId;

            void ConsiderFsm(uint id, PlayMakerFSM? fsm)
            {
                if (fsm == null) return;
                float sq = (_localPlayer.position - fsm.transform.position).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    bestId = id;
                }
            }

            foreach (var pair in _doors) ConsiderFsm(pair.Key, pair.Value.Fsm);
            foreach (var pair in _controls) ConsiderFsm(pair.Key, pair.Value.Fsm);
            foreach (var pair in _parts) ConsiderFsm(pair.Key, pair.Value.Fsm);
            foreach (var pair in _bolts) ConsiderFsm(pair.Key, pair.Value.Fsm);

            return bestId != 0 ? bestId : (uint?)null;
        }

        /// <summary>
        /// Re-apply frost/defrost presentation after PlayMaker runs so the remote
        /// car's local frost sim cannot overwrite streamed values every frame.
        /// </summary>
        private void LateUpdate()
        {
            if (_worldSyncDisabled || !_wasSessionActive) return;

            try
            {
                LateUpdateWorldSync();
            }
            catch (Exception e)
            {
                _worldSyncDisabled = true;
                WinterMPPlugin.Log.LogError($"WorldSync disabled after LateUpdate error: {e}");
                SyncEventLog.Record("fatal", $"LateUpdate: {e}");
                SyncEventLog.DumpToFile();
            }
        }

        private void LateUpdateWorldSync()
        {
            float now = Time.unscaledTime;
            foreach (var item in _items.Values)
            {
                if (!item.IsVehicle || item.LocallyOwned || item.Body == null) continue;
                if (now >= item.RemoteClimateUntil) continue;
                UpdateRemoteClimatePresentation(item, now);
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

            // Scene swap destroyed every hooked FSM and tracked body.
            _doors.Clear();
            _parts.Clear();
            _buys.Clear();
            _bolts.Clear();
            _ignitions.Clear();
            _controls.Clear();
            _starters.Clear();
            _items.Clear();
            _hookedFsms.Clear();
            _trackedBodies.Clear();
            _pending.Clear();
            _pendingItemPoses.Clear();
            _pendingBoltStates.Clear();
            _pendingPartStates.Clear();
            _pendingPurchaseIntents.Clear();
            _sessionDespawnedItems.Clear();
            _pendingDespawnedItems.Clear();
            _outPurchaseSequence = 0;
            _timeWeather.Reset();
            _wallet.Reset();
            _snapshotRequested = false;
            _outChecksumSequence = 0;
            _nextChecksumAt = 0f;
            _nextResyncRequestAt = 0f;
            IdHash = 0;
            _localPlayer = null;
            _nextPlayerSearchAt = 0f;
            _nextScanAt = Time.unscaledTime + FirstScanDelaySeconds;
            _firstDoorRegisteredAt = -1f;
        }

        // ------------------------------------------------------------------ registry

        private void ScanWorld()
        {
            int newDoors = 0, newParts = 0, newBuys = 0, newBolts = 0, newIgnitions = 0, newControls = 0, newStarters = 0, newItems = 0;

            try
            {
                var fsms = Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM));
                foreach (var obj in fsms)
                {
                    var fsm = obj as PlayMakerFSM;
                    if (fsm == null || _hookedFsms.ContainsKey(fsm)) continue;

                    try
                    {
                        if (!fsm.gameObject.activeInHierarchy || !fsm.enabled) continue;

                        string fsmName = fsm.FsmName;
                        if (fsmName == "Use")
                        {
                            string[]? states = SyncCatalog.TryMatchDoor(fsm);
                            if (states != null && RegisterDoor(fsm, states)) newDoors++;
                            else
                            {
                                states = SyncCatalog.TryMatchIgnition(fsm);
                                if (states != null && RegisterIgnition(fsm, states)) newIgnitions++;
                                else
                                {
                                    states = SyncCatalog.TryMatchSwitch(fsm);
                                    if (states != null && RegisterControl(fsm, states)) newControls++;
                                    else
                                    {
                                        states = SyncCatalog.TryMatchControl(fsm);
                                        if (states != null && RegisterControl(fsm, states)) newControls++;
                                        else if (ClassifyBuy(fsm, out var useBuyProfile) && RegisterBuy(fsm, useBuyProfile))
                                            newBuys++;
                                    }
                                }
                            }
                        }
                        else if (fsmName == "Screw")
                        {
                            if (SyncCatalog.TryMatchBolt(fsm) && RegisterBolt(fsm))
                                newBolts++;
                        }
                        else if (fsmName == "Buy")
                        {
                            if (ClassifyBuy(fsm, out var buyProfile) && RegisterBuy(fsm, buyProfile))
                                newBuys++;
                        }
                        else if (fsmName == "Data")
                        {
                            string[]? partStates = SyncCatalog.TryMatchPart(fsm);
                            if (partStates != null && RegisterPart(fsm, partStates))
                                newParts++;
                            else if (ClassifyBuy(fsm, out var dataBuy) && RegisterBuy(fsm, dataBuy))
                                newBuys++;
                        }
                        else if (fsmName == "Button")
                        {
                            if (ClassifyBuy(fsm, out var buttonBuy) && RegisterBuy(fsm, buttonBuy))
                                newBuys++;
                            else
                            {
                                string[]? states = SyncCatalog.TryMatchControl(fsm);
                                if (states != null && RegisterControl(fsm, states)) newControls++;
                            }
                        }
                        else
                        {
                            string[]? states = SyncCatalog.TryMatchStarter(fsm);
                            if (states != null && RegisterStarter(fsm, states)) newStarters++;
                            else
                            {
                                states = SyncCatalog.TryMatchControl(fsm);
                                if (states != null && RegisterControl(fsm, states)) newControls++;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // One broken FSM must not abort the scan.
                        WinterMPPlugin.Log.LogDebug($"WorldSync: skipped FSM during scan: {e.Message}");
                    }
                }

                newItems = ScanItems();
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogError($"WorldSync: world scan failed: {e}");
            }

            if (newDoors > 0 || newParts > 0 || newBuys > 0 || newBolts > 0 || newIgnitions > 0 || newControls > 0 || newStarters > 0 || newItems > 0)
            {
                RecomputeIdHash();
                WinterMPPlugin.Log.LogInfo(
                    $"WorldSync: +{newDoors} doors, +{newParts} parts, +{newBuys} buys, +{newBolts} bolts, +{newIgnitions} ignitions, +{newControls} controls, +{newItems} items — " +
                    $"now {_doors.Count}/{_parts.Count}/{_buys.Count}/{_bolts.Count}/{_ignitions.Count}/{_controls.Count}/{_starters.Count}/{_items.Count} (id hash {IdHash:X8}).");
            }

            // Self-test: announce the catalog once via chat so both instances' id
            // hashes can be compared in the host's log.
            if (_selfTest && !_readyAnnounced && _doors.Count > 0)
            {
                _readyAnnounced = true;
                SessionManager.Instance?.SendChat(
                    $"[ws] ready: {_doors.Count} doors, {_bolts.Count} bolts, {_items.Count} items, hash {IdHash:X8}");
            }
        }

        private static bool ClassifyBuy(PlayMakerFSM fsm, out BuyProfile profile)
        {
            profile = default;
            if (!SyncCatalog.TryMatchBuy(fsm, out var catalog) || catalog == null)
                return false;

            profile.EntryGuards = ToBuyGuards(catalog.EntryGuards);
            profile.ResultStates = catalog.ResultStates;
            return true;
        }

        private static BuyEntryGuard[] ToBuyGuards(CatalogBuyGuard[] guards)
        {
            var result = new BuyEntryGuard[guards.Length];
            for (int i = 0; i < guards.Length; i++)
            {
                result[i] = new BuyEntryGuard
                {
                    StateName = guards[i].StateName,
                    TriggerEvent = guards[i].TriggerEvent,
                };
            }

            return result;
        }

        private static bool HasAllStates(PlayMakerFSM fsm, params string[] states)
        {
            foreach (string state in states)
            {
                if (!FsmHook.HasState(fsm, state)) return false;
            }

            return true;
        }

        private bool RegisterDoor(PlayMakerFSM fsm, string[] syncedStates)
        {
            string path = ScenePath.Of(fsm.transform);
            uint id = StableHash.Fnv1a32(path + "::" + fsm.FsmName);
            if (_doors.ContainsKey(id) || _bolts.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: door id collision, not syncing '{path}'.");
                _hookedFsms[fsm] = true;
                return false;
            }

            foreach (string state in syncedStates)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, state)) return false;
                string captured = state;
                if (!FsmHook.OnStateEnter(fsm, state, () => OnDoorStateEntered(id, captured))) return false;
            }

            _doors[id] = new SyncedDoor { Fsm = fsm, Path = path, SyncedStates = syncedStates };
            _hookedFsms[fsm] = true;
            if (_firstDoorRegisteredAt < 0f) _firstDoorRegisteredAt = Time.unscaledTime;
            return true;
        }

        private bool RegisterPart(PlayMakerFSM fsm, string[] syncedStates)
        {
            string path = ScenePath.Of(fsm.transform);
            uint id = StableHash.Fnv1a32(path + "::" + fsm.FsmName);
            if (_parts.ContainsKey(id) || _doors.ContainsKey(id) || _bolts.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: part id collision, not syncing '{path}'.");
                _hookedFsms[fsm] = true;
                return false;
            }

            foreach (string state in syncedStates)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, state)) return false;
                string captured = state;
                if (!FsmHook.OnStateEnter(fsm, state, () => OnPartStateEntered(id, captured))) return false;
                if (state == "Stop" || state == "Bolted" || state == "Unbolted")
                {
                    if (!FsmHook.OnStateEnter(fsm, state, () => OnPartSettled(id))) return false;
                }
            }

            _parts[id] = new SyncedPart
            {
                Fsm = fsm,
                Path = path,
                SyncedStates = syncedStates,
                InstalledVar = fsm.FsmVariables.FindFsmBool("Installed"),
                TightnessVar = fsm.FsmVariables.FindFsmFloat("Tightness"),
                WearVar = fsm.FsmVariables.FindFsmFloat("Wear"),
            };
            _hookedFsms[fsm] = true;

            if (_pendingPartStates.TryGetValue(id, out var pending) && Time.unscaledTime < pending.ExpiresAt)
            {
                _pendingPartStates.Remove(id);
                ApplyPartState(id, pending.Flags, pending.Tightness, pending.Wear);
            }

            return true;
        }

        private bool RegisterBuy(PlayMakerFSM fsm, BuyProfile profile)
        {
            string path = ScenePath.Of(fsm.transform);
            uint id = StableHash.Fnv1a32(path + "::" + fsm.FsmName);
            if (_buys.ContainsKey(id) || _doors.ContainsKey(id) || _parts.ContainsKey(id) || _bolts.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: buy id collision, not syncing '{path}'.");
                _hookedFsms[fsm] = true;
                return false;
            }

            foreach (string state in profile.ResultStates)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, state)) return false;
                string captured = state;
                if (!FsmHook.OnStateEnter(fsm, state, () => OnBuyResultStateEntered(id, captured))) return false;
            }

            foreach (var guard in profile.EntryGuards)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, guard.StateName)) return false;
                string capturedEvent = guard.TriggerEvent;
                if (!FsmHook.OnStateEnter(fsm, guard.StateName, () => OnBuyEntryGuard(id, capturedEvent))) return false;
            }

            _buys[id] = new SyncedBuy
            {
                Fsm = fsm,
                Path = path,
                EntryGuards = profile.EntryGuards,
                ResultStates = profile.ResultStates,
            };
            _hookedFsms[fsm] = true;

            for (int i = _pendingPurchaseIntents.Count - 1; i >= 0; i--)
            {
                var pending = _pendingPurchaseIntents[i];
                if (pending.NetId != id) continue;
                if (TryExecuteHostPurchase(id, pending.EventName))
                    _pendingPurchaseIntents.RemoveAt(i);
            }

            return true;
        }

        private bool RegisterBolt(PlayMakerFSM fsm)
        {
            string path = ScenePath.Of(fsm.transform);
            uint id = StableHash.Fnv1a32(path + "::" + fsm.FsmName);
            if (_bolts.ContainsKey(id) || _doors.ContainsKey(id) || _buys.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: bolt id collision, not syncing '{path}'.");
                _hookedFsms[fsm] = true;
                return false;
            }

            // "Tight?"/"Loose?" are entered exactly when the wrench turns the bolt;
            // their own BACK check rejects over/under-tightening on each machine.
            if (!FsmHook.OnStateEnter(fsm, "Tight?", () => OnBoltTurned(id, "TIGHTEN"))) return false;
            if (!FsmHook.OnStateEnter(fsm, "Loose?", () => OnBoltTurned(id, "UNTIGHTEN"))) return false;
            if (!FsmHook.OnStateEnter(fsm, "Set pos", () => OnBoltSettled(id))) return false;

            _bolts[id] = new SyncedBolt
            {
                Fsm = fsm,
                Path = path,
                BoltTightnessVar = fsm.FsmVariables.FindFsmInt("BoltTightness"),
                ScrewIntVar = fsm.FsmVariables.FindFsmInt("ScrewInt"),
                TightnessFVar = fsm.FsmVariables.FindFsmFloat("TightnessF"),
                ScrewFloatVar = fsm.FsmVariables.FindFsmFloat("ScrewFloat"),
            };
            _hookedFsms[fsm] = true;

            if (_pendingBoltStates.TryGetValue(id, out var pending) && Time.unscaledTime < pending.ExpiresAt)
            {
                _pendingBoltStates.Remove(id);
                ApplyBoltState(id, pending.BoltTightness, pending.ScrewInt);
            }

            return true;
        }

        private bool RegisterIgnition(PlayMakerFSM fsm, string[] syncedStates)
        {
            string path = ScenePath.Of(fsm.transform);
            uint id = StableHash.Fnv1a32(path + "::" + fsm.FsmName);
            if (_ignitions.ContainsKey(id) || _starters.ContainsKey(id) || _doors.ContainsKey(id) || _bolts.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: ignition id collision, not syncing '{path}'.");
                _hookedFsms[fsm] = true;
                return false;
            }

            foreach (string state in syncedStates)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, state)) return false;
                string captured = state;
                if (!FsmHook.OnStateEnter(fsm, state, () => OnIgnitionStateEntered(id, captured))) return false;
            }

            _ignitions[id] = new SyncedIgnition { Fsm = fsm, Path = path, SyncedStates = syncedStates };
            _hookedFsms[fsm] = true;
            WinterMPPlugin.Log.LogInfo($"WorldSync: ignition registered: '{path}'.");
            return true;
        }

        private bool RegisterControl(PlayMakerFSM fsm, string[] syncedStates)
        {
            string path = ScenePath.Of(fsm.transform);
            uint id = StableHash.Fnv1a32(path + "::" + fsm.FsmName);
            if (_controls.ContainsKey(id) || _starters.ContainsKey(id) || _ignitions.ContainsKey(id) || _doors.ContainsKey(id) || _bolts.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: control id collision, not syncing '{path}'.");
                _hookedFsms[fsm] = true;
                return false;
            }

            foreach (string state in syncedStates)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, state)) return false;
                string captured = state;
                if (!FsmHook.OnStateEnter(fsm, state, () => OnControlStateEntered(id, captured))) return false;
            }

            _controls[id] = new SyncedControl { Fsm = fsm, Path = path, SyncedStates = syncedStates };
            _hookedFsms[fsm] = true;
            WinterMPPlugin.Log.LogInfo($"WorldSync: control registered: '{path}'.");
            return true;
        }

        private bool RegisterStarter(PlayMakerFSM fsm, string[] syncedStates)
        {
            string path = ScenePath.Of(fsm.transform);
            uint id = StableHash.Fnv1a32(path + "::" + fsm.FsmName);
            if (_starters.ContainsKey(id) || _controls.ContainsKey(id) || _ignitions.ContainsKey(id) || _doors.ContainsKey(id) || _bolts.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: starter id collision, not syncing '{path}'.");
                _hookedFsms[fsm] = true;
                return false;
            }

            foreach (string state in syncedStates)
            {
                if (!FsmHook.EnsureRemoteEntry(fsm, state)) return false;
                string captured = state;
                if (!FsmHook.OnStateEnter(fsm, state, () => OnStarterStateEntered(id, captured))) return false;
            }

            _starters[id] = new SyncedStarter { Fsm = fsm, Path = path, SyncedStates = syncedStates };
            _hookedFsms[fsm] = true;
            WinterMPPlugin.Log.LogInfo($"WorldSync: starter registered: '{path}'.");
            return true;
        }

        private int ScanItems()
        {
            // Collect new candidates first so same-path clones (six sausages at the
            // scene root...) get deterministic ordinals from one consistent batch.
            var newcomers = new List<SyncedItem>();
            var bodies = Resources.FindObjectsOfTypeAll(typeof(Rigidbody));
            foreach (var obj in bodies)
            {
                var body = obj as Rigidbody;
                if (body == null || _trackedBodies.ContainsKey(body)) continue;

                try
                {
                    if (!body.gameObject.activeInHierarchy) continue;

                    bool isVehicle = SyncCatalog.IsVehicleRoot(body);
                    bool isItem = !isVehicle && SyncCatalog.IsPickableRigidbody(body);
                    if (!isItem && !isVehicle) continue;

                    newcomers.Add(new SyncedItem
                    {
                        Body = body,
                        Path = ScenePath.Of(body.transform),
                        LastPosition = body.transform.position,
                        IsVehicle = isVehicle,
                    });
                }
                catch (Exception e)
                {
                    WinterMPPlugin.Log.LogDebug($"WorldSync: skipped rigidbody during scan: {e.Message}");
                }
            }

            if (newcomers.Count == 0) return 0;

            // Same-path groups: ordinal by initial position (same save => same order
            // on every machine). Quantized to 1 mm so float noise can't flip ties.
            var byPath = new Dictionary<string, List<SyncedItem>>();
            foreach (var item in newcomers)
            {
                if (!byPath.TryGetValue(item.Path, out var group))
                    byPath[item.Path] = group = new List<SyncedItem>();
                group.Add(item);
            }

            int added = 0;
            foreach (var pair in byPath)
            {
                var group = pair.Value;
                if (group.Count > 1)
                    group.Sort(CompareByInitialPosition);

                for (int i = 0; i < group.Count; i++)
                {
                    var item = group[i];
                    string prefix = item.IsVehicle ? "vehicle:" : "item:";
                    string idSource = group.Count > 1 ? prefix + item.Path + "#" + i : prefix + item.Path;
                    item.Id = StableHash.Fnv1a32(idSource);
                    _trackedBodies[item.Body] = true;

                    if (_pendingDespawnedItems.Contains(item.Id))
                    {
                        _pendingDespawnedItems.Remove(item.Id);
                        Destroy(item.Body.gameObject);
                        WinterMPPlugin.Log.LogInfo(
                            $"WorldSync: item '{item.Path}' removed from snapshot despawn list.");
                        continue;
                    }

                    if (_items.ContainsKey(item.Id))
                    {
                        // Late-discovered clone of an existing group — its ordinal may
                        // disagree across machines, so it is safer not to sync it.
                        WinterMPPlugin.Log.LogWarning($"WorldSync: item id collision, not syncing '{item.Path}'.");
                        continue;
                    }

                    _items[item.Id] = item;
                    if (item.IsVehicle)
                        WinterMPPlugin.Log.LogInfo($"WorldSync: vehicle registered: '{item.Path}' ({item.Body.mass:0} kg).");
                    else
                        TryRegisterConsumableHooks(item);
                    added++;

                    // A join snapshot may have arrived before this item was scanned.
                    if (_pendingItemPoses.TryGetValue(item.Id, out var pose))
                    {
                        _pendingItemPoses.Remove(item.Id);
                        if (Time.unscaledTime < pose.ExpiresAt)
                            ApplySnapshotPose(item, pose.Position, pose.Rotation);
                    }
                }
            }

            return added;
        }

        private void TryRegisterConsumableHooks(SyncedItem item)
        {
            if (item.Body == null || item.IsVehicle) return;

            foreach (var fsm in item.Body.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName != "Use" || _hookedFsms.ContainsKey(fsm)) continue;

                var despawnStates = new List<string>();
                SyncCatalog.CollectConsumableDespawnStates(fsm, despawnStates);
                if (despawnStates.Count == 0) continue;

                bool hooked = false;
                uint itemId = item.Id;
                foreach (string state in despawnStates)
                {
                    string captured = state;
                    if (!FsmHook.OnStateEnter(fsm, state, () => OnItemConsumed(itemId, captured))) continue;
                    hooked = true;
                }

                if (hooked)
                    _hookedFsms[fsm] = true;
            }
        }

        private void OnItemConsumed(uint itemId, string stateName)
        {
            if (_applyingRemote) return;
            AnnounceItemDespawn(itemId, $"consumed ({stateName})");
        }

        private void AnnounceItemDespawn(uint itemId, string reason)
        {
            if (!_items.TryGetValue(itemId, out var item) || item.DespawnSent) return;

            var session = SessionManager.Instance;
            if (session == null || !SessionSyncActive(session)) return;

            item.DespawnSent = true;
            TrackSessionDespawn(itemId);
            WinterMPPlugin.Log.LogInfo($"WorldSync: item {itemId:X8} despawn — {reason} (local).");
            SyncEventLog.Record("despawn", $"{itemId:X8} {reason}");
            session.SendWorldMessage(new ItemDespawn { ItemId = itemId }, Channel.ReliableOrdered);
        }

        private void TrackSessionDespawn(uint itemId)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return;
            _sessionDespawnedItems.Add(itemId);
        }

        public void OnRemoteItemDespawn(ItemDespawn message)
        {
            TrackSessionDespawn(message.ItemId);

            if (!_items.TryGetValue(message.ItemId, out var item)) return;

            WinterMPPlugin.Log.LogInfo($"WorldSync: item {message.ItemId:X8} despawn (remote).");
            item.DespawnSent = true;

            _applyingRemote = true;
            try
            {
                if (item.Body != null)
                    Destroy(item.Body.gameObject);
            }
            finally
            {
                _applyingRemote = false;
            }

            RemoveTrackedItem(message.ItemId, item.Body);
        }

        private void RemoveTrackedItem(uint itemId, Rigidbody? body)
        {
            _items.Remove(itemId);
            if (body != null)
                _trackedBodies.Remove(body);
        }

        private static int CompareByInitialPosition(SyncedItem a, SyncedItem b)
        {
            long ax = Quantize(a.LastPosition.x), bx = Quantize(b.LastPosition.x);
            if (ax != bx) return ax.CompareTo(bx);
            long ay = Quantize(a.LastPosition.y), by = Quantize(b.LastPosition.y);
            if (ay != by) return ay.CompareTo(by);
            return Quantize(a.LastPosition.z).CompareTo(Quantize(b.LastPosition.z));
        }

        private static long Quantize(float value) => (long)Math.Round(value * 1000f);

        private void RecomputeIdHash()
        {
            var ids = new List<uint>(_doors.Count + _parts.Count + _buys.Count + _bolts.Count + _ignitions.Count + _controls.Count + _starters.Count + _items.Count);
            foreach (uint id in _doors.Keys) ids.Add(id);
            foreach (uint id in _parts.Keys) ids.Add(id);
            foreach (uint id in _buys.Keys) ids.Add(id);
            foreach (uint id in _bolts.Keys) ids.Add(id);
            foreach (uint id in _ignitions.Keys) ids.Add(id);
            foreach (uint id in _controls.Keys) ids.Add(id);
            foreach (uint id in _starters.Keys) ids.Add(id);
            foreach (uint id in _items.Keys) ids.Add(id);
            ids.Sort();

            uint hash = StableHash.OffsetBasis;
            foreach (uint id in ids)
                hash = StableHash.Combine(hash, id);
            IdHash = hash;
        }

        private uint ComputeWalletCrc()
        {
            if (!_wallet.TryGetMoney(out float money)) return 0;
            int mk = Mathf.RoundToInt(money);
            return StableHash.Combine(StableHash.OffsetBasis, (uint)mk);
        }

        private uint ComputeWorldCrc()
        {
            uint crc = StableHash.OffsetBasis;
            MixFsmStates(ref crc, _doors);
            MixFsmStates(ref crc, _ignitions);
            MixFsmStates(ref crc, _controls);
            MixFsmStates(ref crc, _starters);
            MixFsmStates(ref crc, _buys);

            var partIds = new List<uint>(_parts.Keys);
            partIds.Sort();
            foreach (uint id in partIds)
            {
                if (!_parts.TryGetValue(id, out var part)) continue;
                ReadPartVars(part, out byte flags, out byte tightness, out byte wear);
                crc = StableHash.Combine(crc, id);
                crc = StableHash.Combine(crc, flags);
                crc = StableHash.Combine(crc, tightness);
                crc = StableHash.Combine(crc, wear);
            }

            var boltIds = new List<uint>(_bolts.Keys);
            boltIds.Sort();
            foreach (uint id in boltIds)
            {
                if (!_bolts.TryGetValue(id, out var bolt)) continue;
                ReadBoltVars(bolt, out ushort tightness, out ushort screwInt);
                crc = StableHash.Combine(crc, id);
                crc = StableHash.Combine(crc, tightness);
                crc = StableHash.Combine(crc, screwInt);
            }

            return crc;
        }

        private uint ComputeItemCrc()
        {
            uint crc = StableHash.OffsetBasis;
            var ids = new List<uint>(_items.Keys);
            ids.Sort();
            foreach (uint id in ids)
            {
                if (!_items.TryGetValue(id, out var item) || item.Body == null || item.IsVehicle) continue;
                if (item.LocallyOwned || item.RemoteOwner != NoOwner) continue;

                var body = item.Body;
                if (!body.IsSleeping() && body.velocity.sqrMagnitude > 0.04f) continue;

                var pos = body.transform.position;
                var rot = body.transform.rotation;
                crc = StableHash.Combine(crc, id);
                crc = StableHash.Combine(crc, (uint)Quantize(pos.x));
                crc = StableHash.Combine(crc, (uint)Quantize(pos.y));
                crc = StableHash.Combine(crc, (uint)Quantize(pos.z));
                crc = StableHash.Combine(crc, (uint)Quantize(rot.x * 1000f));
                crc = StableHash.Combine(crc, (uint)Quantize(rot.y * 1000f));
                crc = StableHash.Combine(crc, (uint)Quantize(rot.z * 1000f));
                crc = StableHash.Combine(crc, (uint)Quantize(rot.w * 1000f));
            }

            return crc;
        }

        private uint ComputeVehicleCrc()
        {
            uint crc = StableHash.OffsetBasis;
            var ids = new List<uint>();
            foreach (var pair in _items)
            {
                if (pair.Value.IsVehicle) ids.Add(pair.Key);
            }

            ids.Sort();
            foreach (uint id in ids)
            {
                if (!_items.TryGetValue(id, out var item) || !TryReadVehicleChecksum(item, out byte flags,
                        out ushort rpm, out byte fuel, out byte coolant, out byte frost, out byte fog, out byte cabinTemp))
                {
                    continue;
                }

                crc = StableHash.Combine(crc, id);
                crc = StableHash.Combine(crc, flags);
                crc = StableHash.Combine(crc, rpm);
                crc = StableHash.Combine(crc, fuel);
                crc = StableHash.Combine(crc, coolant);
                crc = StableHash.Combine(crc, frost);
                crc = StableHash.Combine(crc, fog);
                crc = StableHash.Combine(crc, cabinTemp);
            }

            return crc;
        }

        private bool TryReadVehicleChecksum(SyncedItem item, out byte flags, out ushort rpm, out byte fuel,
            out byte coolant, out byte frost, out byte fog, out byte cabinTemp)
        {
            flags = 0;
            rpm = 0;
            fuel = 0;
            coolant = 0;
            frost = 0;
            fog = 0;
            cabinTemp = 0;
            if (!item.IsVehicle || item.Body == null) return false;

            if (item.LocallyOwned || item.RemoteOwner == NoOwner)
            {
                EnsureVehicleSystemsProbe(item);
                if (!item.SystemsReady) return false;

                float revs = ReadBestRpm(item);
                bool engineOn = revs > EngineRunningRevs;
                bool accOn = ReadAccOn(item) || engineOn;
                if (engineOn) flags |= VehicleState.FlagEngineOn;
                if (accOn) flags |= VehicleState.FlagAccOn;
                if (ReadBlinkerLeft(item)) flags |= VehicleState.FlagBlinkerLeft;
                if (ReadBlinkerRight(item)) flags |= VehicleState.FlagBlinkerRight;
                if (ReadHazardOn(item)) flags |= VehicleState.FlagHazard;
                rpm = (ushort)Mathf.Clamp(revs, 0f, ushort.MaxValue);
                fuel = ReadFuelLevelByte(item);
                coolant = ReadCoolantTempByte(item);

                EnsureClimateProbe(item);
                if (item.ClimateReady)
                {
                    frost = QuantizeFrost(ReadFrost(item));
                    fog = QuantizeFrost(ReadFog(item));
                    cabinTemp = QuantizeHeater(ReadCabinTemp(item), CabinTempMaxC);
                }

                return true;
            }

            flags = 0;
            if (item.RemoteEngineOn) flags |= VehicleState.FlagEngineOn;
            if (item.RemoteAccOn) flags |= VehicleState.FlagAccOn;
            if (item.RemoteBlinkerLeft) flags |= VehicleState.FlagBlinkerLeft;
            if (item.RemoteBlinkerRight) flags |= VehicleState.FlagBlinkerRight;
            if (item.RemoteHazard) flags |= VehicleState.FlagHazard;
            rpm = (ushort)Mathf.Clamp(item.RemoteRpm, 0f, ushort.MaxValue);
            fuel = item.RemoteFuelLevel;
            coolant = item.RemoteCoolantTemp;
            frost = item.RemoteFrost;
            fog = item.RemoteFog;
            cabinTemp = item.RemoteCabinTemp;
            return true;
        }

        private static void MixFsmStates<T>(ref uint crc, Dictionary<uint, T> entries) where T : class
        {
            var ids = new List<uint>(entries.Keys);
            ids.Sort();
            foreach (uint id in ids)
            {
                if (!entries.TryGetValue(id, out var entry)) continue;
                string? state = entry switch
                {
                    SyncedDoor d => d.LastSyncedState,
                    SyncedIgnition i => i.LastSyncedState,
                    SyncedControl c => c.LastSyncedState,
                    SyncedStarter s => s.LastSyncedState,
                    SyncedBuy b => b.LastSyncedState,
                    _ => null,
                };
                if (state == null) continue;
                crc = StableHash.Combine(crc, id);
                crc = StableHash.Combine(crc, StableHash.Fnv1a32(state));
            }
        }

        public WorldStateChecksum? BuildStateChecksum()
        {
            if (_doors.Count == 0) return null;

            return new WorldStateChecksum
            {
                WalletCrc = ComputeWalletCrc(),
                WorldCrc = ComputeWorldCrc(),
                ItemCrc = ComputeItemCrc(),
                VehicleCrc = ComputeVehicleCrc(),
                Sequence = ++_outChecksumSequence,
            };
        }

        public void OnRemoteStateChecksum(WorldStateChecksum message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || !_snapshotRequested || _doors.Count == 0) return;
            if (Time.unscaledTime < _nextResyncRequestAt) return;

            uint localWallet = ComputeWalletCrc();
            uint localWorld = ComputeWorldCrc();
            uint localItems = ComputeItemCrc();
            uint localVehicles = ComputeVehicleCrc();
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

        /// <summary>Host: targeted snapshot chunks after a guest checksum mismatch.</summary>
        public IEnumerable<IMessage> BuildResyncMessages(byte flags)
        {
            if ((flags & WorldResyncRequest.FlagWallet) != 0)
            {
                var wallet = BuildWalletState();
                if (wallet != null) yield return wallet;
            }

            if ((flags & WorldResyncRequest.FlagFsmStates) != 0)
            {
                foreach (var chunk in BuildDoorSnapshotChunks())
                    yield return chunk;
            }

            if ((flags & WorldResyncRequest.FlagParts) != 0)
            {
                foreach (var chunk in BuildPartSnapshotChunks())
                    yield return chunk;
            }

            if ((flags & WorldResyncRequest.FlagBolts) != 0)
            {
                foreach (var chunk in BuildBoltSnapshotChunks())
                    yield return chunk;
            }

            if ((flags & WorldResyncRequest.FlagItems) != 0)
            {
                foreach (var chunk in BuildItemSnapshotChunks())
                    yield return chunk;
            }

            if ((flags & WorldResyncRequest.FlagVehicles) != 0)
            {
                foreach (var message in BuildVehicleResyncMessages())
                    yield return message;
            }
        }

        private IEnumerable<IMessage> BuildVehicleResyncMessages()
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) yield break;

            foreach (var pair in _items)
            {
                if (!pair.Value.IsVehicle) continue;
                foreach (var message in BuildVehicleStateMessages(pair.Value, session.LocalPlayerId))
                    yield return message;
            }
        }

        /// <summary>Guest -> host: ask for one object's authoritative state (self-healing).</summary>
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

        /// <summary>Host: reply to a per-object state request with whatever we know.</summary>
        public IEnumerable<IMessage> BuildObjectStateMessages(uint netId)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) yield break;

            if (_items.TryGetValue(netId, out var item) && item.Body != null)
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
                    foreach (var message in BuildVehicleStateMessages(item, session.LocalPlayerId))
                        yield return message;
                }

                yield break;
            }

            string? fsmState = TryGetFsmSnapshotState(netId);
            if (fsmState != null)
            {
                yield return new FsmStateEnter { NetId = netId, StateName = fsmState };
                yield break;
            }

            if (_parts.TryGetValue(netId, out var part) && ShouldIncludePartSnapshot(part, out byte flags, out byte tightness, out byte wear))
            {
                yield return new PartState { NetId = netId, Flags = flags, Tightness = tightness, Wear = wear };
                yield break;
            }

            if (_bolts.TryGetValue(netId, out var bolt))
            {
                ReadBoltVars(bolt, out ushort boltTightness, out ushort screwInt);
                if (boltTightness != 0 || screwInt != 0)
                    yield return new BoltState { NetId = netId, BoltTightness = boltTightness, ScrewInt = screwInt };
            }
        }

        private IEnumerable<IMessage> BuildVehicleStateMessages(SyncedItem item, byte ownerPlayerId)
        {
            var state = TryBuildVehicleStateMessage(item, ownerPlayerId);
            if (state != null) yield return state;

            var climate = TryBuildVehicleClimate(item);
            if (climate != null)
            {
                climate.OwnerPlayerId = ownerPlayerId;
                yield return climate;
            }
        }

        private string? TryGetFsmSnapshotState(uint netId)
        {
            if (_doors.TryGetValue(netId, out var door))
                return door.LastSyncedState ?? TryReadActiveSyncedState(door.Fsm, door.SyncedStates);
            if (_ignitions.TryGetValue(netId, out var ignition))
                return ignition.LastSyncedState ?? TryReadActiveSyncedState(ignition.Fsm, ignition.SyncedStates);
            if (_controls.TryGetValue(netId, out var control))
                return control.LastSyncedState ?? TryReadActiveSyncedState(control.Fsm, control.SyncedStates);
            if (_starters.TryGetValue(netId, out var starter))
                return starter.LastSyncedState ?? TryReadActiveSyncedState(starter.Fsm, starter.SyncedStates);
            if (_buys.TryGetValue(netId, out var buy))
                return buy.LastSyncedState ?? TryReadActiveSyncedState(buy.Fsm, buy.ResultStates);
            if (_parts.TryGetValue(netId, out var part))
                return part.LastSyncedState ?? TryReadActiveSyncedState(part.Fsm, part.SyncedStates);
            return null;
        }

        private static string? TryReadActiveSyncedState(PlayMakerFSM? fsm, string[] syncedStates)
        {
            if (fsm?.Fsm == null) return null;

            try
            {
                string active = fsm.Fsm.ActiveStateName;
                if (Array.IndexOf(syncedStates, active) >= 0) return active;
            }
            catch
            {
                // FSM not ready.
            }

            return null;
        }

        private VehicleState? TryBuildVehicleStateMessage(SyncedItem item, byte ownerPlayerId)
        {
            if (!item.IsVehicle || item.Body == null) return null;

            EnsureVehicleSystemsProbe(item);
            if (!item.SystemsReady) return null;

            float revs = ReadBestRpm(item);
            bool engineOn = revs > EngineRunningRevs;
            bool accOn = ReadAccOn(item) || engineOn;
            float speedKmh = item.GaugeSpeedVar != null ? item.GaugeSpeedVar.Value : 0f;

            byte flags = 0;
            if (engineOn) flags |= VehicleState.FlagEngineOn;
            if (accOn) flags |= VehicleState.FlagAccOn;
            if (ReadBlinkerLeft(item)) flags |= VehicleState.FlagBlinkerLeft;
            if (ReadBlinkerRight(item)) flags |= VehicleState.FlagBlinkerRight;
            if (ReadHazardOn(item)) flags |= VehicleState.FlagHazard;

            return new VehicleState
            {
                VehicleId = item.Id,
                OwnerPlayerId = ownerPlayerId,
                Flags = flags,
                Rpm = (ushort)Mathf.Clamp(revs, 0f, ushort.MaxValue),
                SpeedTenthsKmh = (ushort)Mathf.Clamp(speedKmh * 10f, 0f, ushort.MaxValue),
                FuelLevel = ReadFuelLevelByte(item),
                CoolantTemp = ReadCoolantTempByte(item),
            };
        }

        // ------------------------------------------------------------------ local -> network

        private void OnDoorStateEntered(uint netId, string stateName)
        {
            if (_applyingRemote) return;

            // Track even without peers: the join snapshot replays everything the
            // host touched before the guest arrived.
            if (_doors.TryGetValue(netId, out var door))
                door.LastSyncedState = stateName;

            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            WinterMPPlugin.Log.LogInfo($"WorldSync: door {netId:X8} -> '{stateName}' (local).");
            session.SendWorldMessage(new FsmStateEnter { NetId = netId, StateName = stateName }, Channel.ReliableOrdered);
        }

        private void OnPartStateEntered(uint netId, string stateName)
        {
            if (_applyingRemote) return;

            if (_parts.TryGetValue(netId, out var part))
                part.LastSyncedState = stateName;

            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            WinterMPPlugin.Log.LogInfo($"WorldSync: part {netId:X8} -> '{stateName}' (local).");
            session.SendWorldMessage(new FsmStateEnter { NetId = netId, StateName = stateName }, Channel.ReliableOrdered);
        }

        private static bool SessionSyncActive(SessionManager session)
        {
            if (session.State != SessionState.Hosting && session.State != SessionState.Connected)
                return false;
            return session.IsHost ? session.PlayerCount > 0 : true;
        }

        private void OnBuyEntryGuard(uint netId, string triggerEvent)
        {
            if (_applyingRemote) return;

            var session = SessionManager.Instance;
            if (session == null || session.IsHost || !SessionSyncActive(session)) return;
            if (!_buys.TryGetValue(netId, out var buy))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: buy guard on unknown id {netId:X8}.");
                return;
            }

            _wallet.RestoreGuestMoney();

            WinterMPPlugin.Log.LogInfo($"WorldSync: buy {netId:X8} intent {triggerEvent} (guest).");
            session.SendWorldMessage(new PurchaseIntent
            {
                PlayerId = session.LocalPlayerId,
                NetId = netId,
                EventName = triggerEvent,
                Sequence = ++_outPurchaseSequence,
            }, Channel.ReliableOrdered);

            _applyingRemote = true;
            try
            {
                AbortGuestBuy(buy);
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        private void OnBuyResultStateEntered(uint netId, string stateName)
        {
            if (_applyingRemote) return;

            if (_buys.TryGetValue(netId, out var buy))
                buy.LastSyncedState = stateName;

            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0 || !session.IsHost) return;

            _wallet.NotifyMoneyChanged();

            WinterMPPlugin.Log.LogInfo($"WorldSync: buy {netId:X8} -> '{stateName}' (local).");
            SyncEventLog.Record("buy", $"{netId:X8} -> {stateName}");
            session.SendWorldMessage(new FsmStateEnter { NetId = netId, StateName = stateName }, Channel.ReliableOrdered);
        }

        private static void AbortGuestBuy(SyncedBuy buy)
        {
            try
            {
                if (HasFsmEvent(buy.Fsm, "STOP"))
                    buy.Fsm.SendEvent("STOP");
                else if (HasFsmEvent(buy.Fsm, "RESET"))
                    buy.Fsm.SendEvent("RESET");
                else
                    buy.Fsm.SendEvent("FINISHED");
            }
            catch
            {
                // FSM may be tearing down.
            }
        }

        private static bool HasFsmEvent(PlayMakerFSM fsm, string eventName)
        {
            var events = fsm.Fsm != null ? fsm.Fsm.Events : null;
            if (events == null) return false;
            foreach (var evt in events)
            {
                if (evt != null && evt.Name == eventName) return true;
            }

            return false;
        }

        public void OnHostPurchaseIntent(PurchaseIntent intent)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return;

            WinterMPPlugin.Log.LogInfo(
                $"WorldSync: buy intent from player {intent.PlayerId}: {intent.NetId:X8} {intent.EventName}.");

            if (!_buys.ContainsKey(intent.NetId))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"WorldSync: buy intent for unregistered id {intent.NetId:X8} (scan still catching up?).");
            }

            if (!TryExecuteHostPurchase(intent.NetId, intent.EventName))
            {
                _pendingPurchaseIntents.Add(new PendingPurchaseIntent
                {
                    NetId = intent.NetId,
                    EventName = intent.EventName,
                    ExpiresAt = Time.unscaledTime + PendingTtlSeconds,
                });
            }
        }

        private static string? ResolveForcedPurchaseState(SyncedBuy buy, string eventName)
        {
            switch (eventName)
            {
                case "USE":
                    if (FsmHook.HasState(buy.Fsm, "Check car")) return "Check car";
                    if (FsmHook.HasState(buy.Fsm, "1") && FsmHook.HasState(buy.Fsm, "Remove order")) return "1";
                    if (FsmHook.HasState(buy.Fsm, "Check money")) return "Check money";
                    if (Array.IndexOf(buy.ResultStates, "Purchase") >= 0) return "Purchase";
                    break;
                case "PAY":
                    if (FsmHook.HasState(buy.Fsm, "Check money")) return "Check money";
                    if (FsmHook.HasState(buy.Fsm, "Wait payment")) return "Wait payment";
                    break;
                case "PAYMENT":
                    if (FsmHook.HasState(buy.Fsm, "Spawn package")) return "Spawn package";
                    // Fleetari: PAYMENT confirms the bill after Wait payment, not the initial order.
                    if (buy.Fsm.ActiveStateName == "Wait payment" && FsmHook.HasState(buy.Fsm, "State 3"))
                        return "State 3";
                    if (FsmHook.HasState(buy.Fsm, "Pending cost")) return "Pending cost";
                    if (FsmHook.HasState(buy.Fsm, "State 3")) return "State 3";
                    break;
                case "BUY":
                    if (Array.IndexOf(buy.ResultStates, "Fleetari 2") >= 0) return "Fleetari 2";
                    break;
                case "CLICK":
                    if (FsmHook.HasState(buy.Fsm, "Pending cost")) return "Pending cost";
                    break;
                case "PURCHASE":
                    if (FsmHook.HasState(buy.Fsm, "Purchase event")) return "Purchase event";
                    if (FsmHook.HasState(buy.Fsm, "Check inventory")) return "Check inventory";
                    if (Array.IndexOf(buy.ResultStates, "Add") >= 0) return "Add";
                    if (Array.IndexOf(buy.ResultStates, "Cashier") >= 0) return "Cashier";
                    if (Array.IndexOf(buy.ResultStates, "Purchase") >= 0) return "Purchase";
                    break;
                case "DEPURCHASE":
                    if (FsmHook.HasState(buy.Fsm, "Check if 0")) return "Check if 0";
                    if (Array.IndexOf(buy.ResultStates, "Subtract") >= 0) return "Subtract";
                    if (Array.IndexOf(buy.ResultStates, "State 1") >= 0) return "State 1";
                    break;
            }

            return null;
        }

        private bool TryExecuteHostPurchase(uint netId, string eventName)
        {
            if (!_buys.TryGetValue(netId, out var buy) || buy.Fsm == null) return false;
            if (!buy.Fsm.gameObject.activeInHierarchy || !buy.Fsm.enabled) return false;

            // SendEvent(USE) only works when the FSM is already in Wait button — the
            // host is often elsewhere in the shop. Jump straight into the purchase
            // pipeline via the injected MP_* global transition instead.
            string? forcedState = ResolveForcedPurchaseState(buy, eventName);
            if (forcedState != null)
            {
                if (!FsmHook.EnsureRemoteEntry(buy.Fsm, forcedState)) return false;

                WinterMPPlugin.Log.LogInfo(
                    $"WorldSync: buy {netId:X8} remote entry '{forcedState}' for {eventName} (host).");
                FsmHook.FireRemoteEntry(buy.Fsm, forcedState);
                return true;
            }

            if (!HasFsmEvent(buy.Fsm, eventName)) return false;

            WinterMPPlugin.Log.LogInfo($"WorldSync: buy {netId:X8} event {eventName} (host).");
            buy.Fsm.SendEvent(eventName);
            return true;
        }

        private void OnPartSettled(uint netId)
        {
            if (_applyingRemote) return;
            if (!_parts.TryGetValue(netId, out var part)) return;

            ReadPartVars(part, out byte flags, out byte tightness, out byte wear);
            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            WinterMPPlugin.Log.LogInfo(
                $"WorldSync: part {netId:X8} settled installed={((flags & PartState.FlagInstalled) != 0)} tightness={tightness} wear={wear} (local).");
            session.SendWorldMessage(new PartState
            {
                NetId = netId,
                Flags = flags,
                Tightness = tightness,
                Wear = wear,
            }, Channel.ReliableOrdered);
        }

        private static void ReadPartVars(SyncedPart part, out byte flags, out byte tightness, out byte wear)
        {
            bool installed = part.InstalledVar != null && part.InstalledVar.Value;
            flags = installed ? PartState.FlagInstalled : (byte)0;
            tightness = EncodeUnitFloat(part.TightnessVar != null ? part.TightnessVar.Value : 0f);
            wear = EncodeUnitFloat(part.WearVar != null ? part.WearVar.Value : 0f);
        }

        private static byte EncodeUnitFloat(float value) =>
            (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(value) * 255f), 0, 255);

        private static float DecodeUnitFloat(byte value) => value / 255f;

        private static bool ShouldIncludePartSnapshot(SyncedPart part, out byte flags, out byte tightness, out byte wear)
        {
            ReadPartVars(part, out flags, out tightness, out wear);
            if ((flags & PartState.FlagInstalled) != 0) return true;
            if (tightness > 0) return true;
            if (wear > 0) return true;

            try
            {
                string active = part.Fsm.Fsm.ActiveStateName;
                return active == "Bolted" || active == "Unbolted";
            }
            catch
            {
                return false;
            }
        }

        private void OnBoltTurned(uint netId, string eventName)
        {
            if (_applyingRemote) return;
            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            WinterMPPlugin.Log.LogInfo($"WorldSync: bolt {netId:X8} {eventName} (local).");
            session.SendWorldMessage(new FsmRawEvent { NetId = netId, EventName = eventName }, Channel.ReliableOrdered);
        }

        private void OnBoltSettled(uint netId)
        {
            if (_applyingRemote) return;
            if (!_bolts.TryGetValue(netId, out var bolt)) return;

            ReadBoltVars(bolt, out ushort tightness, out ushort screwInt);
            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            WinterMPPlugin.Log.LogInfo($"WorldSync: bolt {netId:X8} settled tightness={tightness} screw={screwInt} (local).");
            session.SendWorldMessage(new BoltState
            {
                NetId = netId,
                BoltTightness = tightness,
                ScrewInt = screwInt,
            }, Channel.ReliableOrdered);
        }

        private static void ReadBoltVars(SyncedBolt bolt, out ushort tightness, out ushort screwInt)
        {
            int rawTightness = bolt.BoltTightnessVar != null ? bolt.BoltTightnessVar.Value : 0;
            int rawScrew = bolt.ScrewIntVar != null ? bolt.ScrewIntVar.Value : 0;
            tightness = (ushort)Mathf.Clamp(rawTightness, 0, ushort.MaxValue);
            screwInt = (ushort)Mathf.Clamp(rawScrew, 0, ushort.MaxValue);
        }

        private void OnIgnitionStateEntered(uint netId, string stateName)
        {
            if (_applyingRemote) return;

            if (_ignitions.TryGetValue(netId, out var ignition))
                ignition.LastSyncedState = stateName;

            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            WinterMPPlugin.Log.LogInfo($"WorldSync: ignition {netId:X8} -> '{stateName}' (local).");
            session.SendWorldMessage(new FsmStateEnter { NetId = netId, StateName = stateName }, Channel.ReliableOrdered);
        }

        private void OnControlStateEntered(uint netId, string stateName)
        {
            if (_applyingRemote) return;

            if (_controls.TryGetValue(netId, out var control))
                control.LastSyncedState = stateName;

            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            WinterMPPlugin.Log.LogInfo($"WorldSync: control {netId:X8} -> '{stateName}' (local).");
            session.SendWorldMessage(new FsmStateEnter { NetId = netId, StateName = stateName }, Channel.ReliableOrdered);
        }

        private void OnStarterStateEntered(uint netId, string stateName)
        {
            if (_applyingRemote) return;

            if (_starters.TryGetValue(netId, out var starter))
                starter.LastSyncedState = stateName;

            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            WinterMPPlugin.Log.LogInfo($"WorldSync: starter {netId:X8} -> '{stateName}' (local).");
            session.SendWorldMessage(new FsmStateEnter { NetId = netId, StateName = stateName }, Channel.ReliableOrdered);
        }

        // ------------------------------------------------------------------ network -> world

        public void OnRemoteStateEnter(FsmStateEnter message)
        {
            if (!TryApplyStateEnter(message.NetId, message.StateName))
                QueuePending(message.NetId, false, message.StateName);
        }

        public void OnRemoteRawEvent(FsmRawEvent message)
        {
            if (Array.IndexOf(AllowedRawEvents, message.EventName) < 0)
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: refusing non-whitelisted event '{message.EventName}'.");
                return;
            }

            if (!TryApplyRawEvent(message.NetId, message.EventName))
                QueuePending(message.NetId, true, message.EventName);
        }

        private bool TryApplyStateEnter(uint netId, string stateName)
        {
            if (_doors.TryGetValue(netId, out var door) && door.Fsm != null)
            {
                if (Array.IndexOf(door.SyncedStates, stateName) < 0)
                {
                    WinterMPPlugin.Log.LogWarning($"WorldSync: '{stateName}' is not a synced state of {door.Path}; dropped.");
                    return true; // don't queue — it would never become valid
                }

                if (!door.Fsm.gameObject.activeInHierarchy || !door.Fsm.enabled) return false;

                WinterMPPlugin.Log.LogInfo($"WorldSync: door {netId:X8} -> '{stateName}' (remote).");
                _applyingRemote = true;
                try
                {
                    FsmHook.FireRemoteEntry(door.Fsm, stateName);
                    door.LastSyncedState = stateName;
                }
                finally
                {
                    _applyingRemote = false;
                }

                if (_selfTest)
                    SessionManager.Instance?.SendChat($"[ws] applied '{stateName}' on {netId:X8}");

                return true;
            }

            if (_ignitions.TryGetValue(netId, out var ignition) && ignition.Fsm != null)
            {
                if (Array.IndexOf(ignition.SyncedStates, stateName) < 0)
                {
                    WinterMPPlugin.Log.LogWarning($"WorldSync: '{stateName}' is not a synced ignition state of {ignition.Path}; dropped.");
                    return true;
                }

                if (!ignition.Fsm.gameObject.activeInHierarchy || !ignition.Fsm.enabled) return false;

                WinterMPPlugin.Log.LogInfo($"WorldSync: ignition {netId:X8} -> '{stateName}' (remote).");
                _applyingRemote = true;
                try
                {
                    FsmHook.FireRemoteEntry(ignition.Fsm, stateName);
                    ignition.LastSyncedState = stateName;
                }
                finally
                {
                    _applyingRemote = false;
                }

                return true;
            }

            if (_controls.TryGetValue(netId, out var control) && control.Fsm != null)
            {
                if (Array.IndexOf(control.SyncedStates, stateName) < 0)
                {
                    WinterMPPlugin.Log.LogWarning($"WorldSync: '{stateName}' is not a synced control state of {control.Path}; dropped.");
                    return true;
                }

                if (!control.Fsm.gameObject.activeInHierarchy || !control.Fsm.enabled) return false;

                WinterMPPlugin.Log.LogInfo($"WorldSync: control {netId:X8} -> '{stateName}' (remote).");
                PrepareRemoteControl(control);
                _applyingRemote = true;
                try
                {
                    FsmHook.FireRemoteEntry(control.Fsm, stateName);
                    control.LastSyncedState = stateName;
                    FinishRemoteControl(control, stateName);
                }
                finally
                {
                    _applyingRemote = false;
                }

                return true;
            }

            if (_starters.TryGetValue(netId, out var starter) && starter.Fsm != null)
            {
                if (Array.IndexOf(starter.SyncedStates, stateName) < 0)
                {
                    WinterMPPlugin.Log.LogWarning($"WorldSync: '{stateName}' is not a synced starter state of {starter.Path}; dropped.");
                    return true;
                }

                if (!starter.Fsm.gameObject.activeInHierarchy || !starter.Fsm.enabled) return false;

                WinterMPPlugin.Log.LogInfo($"WorldSync: starter {netId:X8} -> '{stateName}' (remote).");
                _applyingRemote = true;
                try
                {
                    FsmHook.FireRemoteEntry(starter.Fsm, stateName);
                    starter.LastSyncedState = stateName;
                }
                finally
                {
                    _applyingRemote = false;
                }

                return true;
            }

            if (_parts.TryGetValue(netId, out var part) && part.Fsm != null)
            {
                if (Array.IndexOf(part.SyncedStates, stateName) < 0)
                {
                    WinterMPPlugin.Log.LogWarning($"WorldSync: '{stateName}' is not a synced part state of {part.Path}; dropped.");
                    return true;
                }

                if (!part.Fsm.gameObject.activeInHierarchy || !part.Fsm.enabled) return false;

                WinterMPPlugin.Log.LogInfo($"WorldSync: part {netId:X8} -> '{stateName}' (remote).");
                _applyingRemote = true;
                try
                {
                    FsmHook.FireRemoteEntry(part.Fsm, stateName);
                    part.LastSyncedState = stateName;
                }
                finally
                {
                    _applyingRemote = false;
                }

                return true;
            }

            if (_buys.TryGetValue(netId, out var buy) && buy.Fsm != null)
            {
                if (Array.IndexOf(buy.ResultStates, stateName) < 0)
                {
                    WinterMPPlugin.Log.LogWarning($"WorldSync: '{stateName}' is not a synced buy state of {buy.Path}; dropped.");
                    return true;
                }

                if (!buy.Fsm.gameObject.activeInHierarchy || !buy.Fsm.enabled) return false;

                WinterMPPlugin.Log.LogInfo($"WorldSync: buy {netId:X8} -> '{stateName}' (remote).");
                _applyingRemote = true;
                try
                {
                    FsmHook.FireRemoteEntry(buy.Fsm, stateName);
                    buy.LastSyncedState = stateName;
                }
                finally
                {
                    _applyingRemote = false;
                }

                return true;
            }

            return false;
        }

        private bool TryApplyRawEvent(uint netId, string eventName)
        {
            if (!_bolts.TryGetValue(netId, out var bolt) || bolt.Fsm == null) return false;
            if (!bolt.Fsm.gameObject.activeInHierarchy || !bolt.Fsm.enabled) return false;

            WinterMPPlugin.Log.LogInfo($"WorldSync: bolt {netId:X8} {eventName} (remote).");
            _applyingRemote = true;
            try
            {
                bolt.Fsm.SendEvent(eventName);
            }
            finally
            {
                _applyingRemote = false;
            }

            if (_selfTest)
                SessionManager.Instance?.SendChat($"[ws] applied {eventName} on {netId:X8}");

            return true;
        }

        private void QueuePending(uint netId, bool isRawEvent, string name)
        {
            // The target FSM is in a disabled LOD cell or not yet scanned — keep the
            // event until the world streams it in (eventual consistency for doors
            // opened far away from the other player).
            _pending.Add(new PendingFsmApply
            {
                NetId = netId,
                IsRawEvent = isRawEvent,
                Name = name,
                ExpiresAt = Time.unscaledTime + PendingTtlSeconds,
            });
        }

        private void ProcessPending()
        {
            var session = SessionManager.Instance;

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var entry = _pending[i];
                bool applied = entry.IsRawEvent
                    ? TryApplyRawEvent(entry.NetId, entry.Name)
                    : TryApplyStateEnter(entry.NetId, entry.Name);

                if (applied || Time.unscaledTime >= entry.ExpiresAt)
                {
                    if (!applied)
                    {
                        WinterMPPlugin.Log.LogWarning($"WorldSync: dropping expired event {entry.Name} for {entry.NetId:X8}.");
                        if (session != null && !session.IsHost)
                            RequestObjectState(entry.NetId);
                    }

                    _pending.RemoveAt(i);
                }
            }

            if (session != null && session.IsHost)
            {
                for (int i = _pendingPurchaseIntents.Count - 1; i >= 0; i--)
                {
                    var intent = _pendingPurchaseIntents[i];
                    if (TryExecuteHostPurchase(intent.NetId, intent.EventName)
                        || Time.unscaledTime >= intent.ExpiresAt)
                    {
                        if (Time.unscaledTime >= intent.ExpiresAt)
                            WinterMPPlugin.Log.LogWarning($"WorldSync: dropping expired buy intent {intent.EventName} for {intent.NetId:X8}.");
                        _pendingPurchaseIntents.RemoveAt(i);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ join snapshot & time

        private IEnumerable<WorldDoorSnapshot> BuildDoorSnapshotChunks()
        {
            var doors = new WorldDoorSnapshot();
            foreach (var pair in _doors)
            {
                var door = pair.Value;
                if (door.LastSyncedState == null) continue;

                doors.Entries.Add(new WorldDoorSnapshot.Entry { NetId = pair.Key, StateName = door.LastSyncedState });
                if (doors.Entries.Count >= DoorSnapshotChunk)
                {
                    yield return doors;
                    doors = new WorldDoorSnapshot();
                }
            }

            foreach (var pair in _ignitions)
            {
                var ignition = pair.Value;
                if (ignition.LastSyncedState == null) continue;

                doors.Entries.Add(new WorldDoorSnapshot.Entry { NetId = pair.Key, StateName = ignition.LastSyncedState });
                if (doors.Entries.Count >= DoorSnapshotChunk)
                {
                    yield return doors;
                    doors = new WorldDoorSnapshot();
                }
            }

            foreach (var pair in _controls)
            {
                var control = pair.Value;
                if (control.LastSyncedState == null) continue;

                doors.Entries.Add(new WorldDoorSnapshot.Entry { NetId = pair.Key, StateName = control.LastSyncedState });
                if (doors.Entries.Count >= DoorSnapshotChunk)
                {
                    yield return doors;
                    doors = new WorldDoorSnapshot();
                }
            }

            foreach (var pair in _starters)
            {
                var starter = pair.Value;
                if (starter.LastSyncedState == null) continue;

                doors.Entries.Add(new WorldDoorSnapshot.Entry { NetId = pair.Key, StateName = starter.LastSyncedState });
                if (doors.Entries.Count >= DoorSnapshotChunk)
                {
                    yield return doors;
                    doors = new WorldDoorSnapshot();
                }
            }

            foreach (var pair in _parts)
            {
                var part = pair.Value;
                if (part.LastSyncedState == null) continue;

                doors.Entries.Add(new WorldDoorSnapshot.Entry { NetId = pair.Key, StateName = part.LastSyncedState });
                if (doors.Entries.Count >= DoorSnapshotChunk)
                {
                    yield return doors;
                    doors = new WorldDoorSnapshot();
                }
            }

            foreach (var pair in _buys)
            {
                var buy = pair.Value;
                if (buy.LastSyncedState == null) continue;

                doors.Entries.Add(new WorldDoorSnapshot.Entry { NetId = pair.Key, StateName = buy.LastSyncedState });
                if (doors.Entries.Count >= DoorSnapshotChunk)
                {
                    yield return doors;
                    doors = new WorldDoorSnapshot();
                }
            }

            if (doors.Entries.Count > 0)
                yield return doors;
        }

        private IEnumerable<WorldBoltSnapshot> BuildBoltSnapshotChunks()
        {
            var bolts = new WorldBoltSnapshot();
            foreach (var pair in _bolts)
            {
                ReadBoltVars(pair.Value, out ushort tightness, out ushort screwInt);
                if (tightness == 0 && screwInt == 0) continue;

                bolts.Entries.Add(new WorldBoltSnapshot.Entry
                {
                    NetId = pair.Key,
                    BoltTightness = tightness,
                    ScrewInt = screwInt,
                });
                if (bolts.Entries.Count >= BoltSnapshotChunk)
                {
                    yield return bolts;
                    bolts = new WorldBoltSnapshot();
                }
            }

            if (bolts.Entries.Count > 0)
                yield return bolts;
        }

        private IEnumerable<WorldPartSnapshot> BuildPartSnapshotChunks()
        {
            var parts = new WorldPartSnapshot();
            foreach (var pair in _parts)
            {
                if (!ShouldIncludePartSnapshot(pair.Value, out byte flags, out byte tightness, out byte wear))
                    continue;

                parts.Entries.Add(new WorldPartSnapshot.Entry
                {
                    NetId = pair.Key,
                    Flags = flags,
                    Tightness = tightness,
                    Wear = wear,
                });
                if (parts.Entries.Count >= PartSnapshotChunk)
                {
                    yield return parts;
                    parts = new WorldPartSnapshot();
                }
            }

            if (parts.Entries.Count > 0)
                yield return parts;
        }

        private IEnumerable<WorldItemSnapshot> BuildItemSnapshotChunks()
        {
            var items = new WorldItemSnapshot();
            foreach (var pair in _items)
            {
                var item = pair.Value;
                if (item.Body == null) continue;

                items.Entries.Add(new WorldItemSnapshot.Entry
                {
                    ItemId = pair.Key,
                    Position = item.Body.transform.position.ToNet(),
                    Rotation = item.Body.transform.rotation.ToNet(),
                });
                if (items.Entries.Count >= ItemSnapshotChunk)
                {
                    yield return items;
                    items = new WorldItemSnapshot();
                }
            }

            if (items.Entries.Count > 0)
                yield return items;
        }

        /// <summary>
        /// Host side: world state for a fresh joiner — every door we saw change
        /// plus the current pose of every item/vehicle, in send-ready chunks.
        /// </summary>
        public IEnumerable<IMessage> BuildWorldSnapshot()
        {
            foreach (var chunk in BuildDoorSnapshotChunks())
                yield return chunk;

            foreach (var chunk in BuildItemSnapshotChunks())
                yield return chunk;

            foreach (var chunk in BuildBoltSnapshotChunks())
                yield return chunk;

            foreach (var chunk in BuildPartSnapshotChunks())
                yield return chunk;

            var despawns = new WorldItemDespawnSnapshot();
            foreach (uint itemId in _sessionDespawnedItems)
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

            foreach (var pair in _items)
            {
                if (!pair.Value.IsVehicle) continue;
                var climate = TryBuildVehicleClimate(pair.Value);
                if (climate != null)
                    yield return climate;
            }
        }

        /// <summary>Host side: current clock/weather, or null while still in the menu.</summary>
        public TimeSync? BuildTimeSync()
        {
            return _timeWeather.BuildMessage();
        }

        public WalletState? BuildWalletState()
        {
            return _wallet.BuildMessage();
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

        public void OnRemoteDoorSnapshot(WorldDoorSnapshot message)
        {
            int applied = 0;
            foreach (var entry in message.Entries)
            {
                // Already in that state (we touched it ourselves, or an earlier
                // chunk) — replaying would re-run animation and sound for nothing.
                if (_doors.TryGetValue(entry.NetId, out var door) && door.LastSyncedState == entry.StateName)
                    continue;
                if (_parts.TryGetValue(entry.NetId, out var part) && part.LastSyncedState == entry.StateName)
                    continue;
                if (_buys.TryGetValue(entry.NetId, out var buy) && buy.LastSyncedState == entry.StateName)
                    continue;

                applied++;
                if (!TryApplyStateEnter(entry.NetId, entry.StateName))
                    QueuePending(entry.NetId, false, entry.StateName);
            }

            WinterMPPlugin.Log.LogInfo($"WorldSync: door snapshot — {message.Entries.Count} entries, {applied} applied/queued.");
        }

        public void OnRemoteItemSnapshot(WorldItemSnapshot message)
        {
            int applied = 0, parked = 0;
            foreach (var entry in message.Entries)
            {
                var position = entry.Position.ToUnity();
                var rotation = entry.Rotation.ToUnity();

                if (_items.TryGetValue(entry.ItemId, out var item) && item.Body != null)
                {
                    // Live streams beat the snapshot (it was built moments ago).
                    if (item.LocallyOwned || Time.unscaledTime - item.LastRemoteAt < GetRemoteHoldSeconds(item))
                        continue;
                    ApplySnapshotPose(item, position, rotation);
                    applied++;
                }
                else
                {
                    _pendingItemPoses[entry.ItemId] = new PendingPose
                    {
                        Position = position,
                        Rotation = rotation,
                        ExpiresAt = Time.unscaledTime + SnapshotPoseTtlSeconds,
                    };
                    parked++;
                }
            }

            WinterMPPlugin.Log.LogInfo($"WorldSync: item snapshot — {message.Entries.Count} entries, {applied} applied, {parked} parked.");
        }

        public void OnRemoteItemDespawnSnapshot(WorldItemDespawnSnapshot message)
        {
            int removed = 0, parked = 0;
            foreach (uint itemId in message.ItemIds)
            {
                if (_items.TryGetValue(itemId, out var item) && item.Body != null)
                {
                    item.DespawnSent = true;
                    _applyingRemote = true;
                    try
                    {
                        Destroy(item.Body.gameObject);
                    }
                    finally
                    {
                        _applyingRemote = false;
                    }

                    RemoveTrackedItem(itemId, item.Body);
                    removed++;
                }
                else
                {
                    _pendingDespawnedItems.Add(itemId);
                    parked++;
                }
            }

            WinterMPPlugin.Log.LogInfo(
                $"WorldSync: despawn snapshot — {message.ItemIds.Count} ids, {removed} removed, {parked} parked.");
        }

        public void OnRemoteBoltSnapshot(WorldBoltSnapshot message)
        {
            int applied = 0, parked = 0;
            foreach (var entry in message.Entries)
            {
                if (ApplyBoltState(entry.NetId, entry.BoltTightness, entry.ScrewInt))
                    applied++;
                else
                {
                    _pendingBoltStates[entry.NetId] = new PendingBoltState
                    {
                        BoltTightness = entry.BoltTightness,
                        ScrewInt = entry.ScrewInt,
                        ExpiresAt = Time.unscaledTime + SnapshotPoseTtlSeconds,
                    };
                    parked++;
                }
            }

            WinterMPPlugin.Log.LogInfo($"WorldSync: bolt snapshot — {message.Entries.Count} entries, {applied} applied, {parked} parked.");
        }

        public void OnRemoteBoltState(BoltState message)
        {
            if (!ApplyBoltState(message.NetId, message.BoltTightness, message.ScrewInt))
                QueuePendingBolt(message.NetId, message.BoltTightness, message.ScrewInt);
        }

        public void OnRemotePartState(PartState message)
        {
            if (!ApplyPartState(message.NetId, message.Flags, message.Tightness, message.Wear))
                QueuePendingPart(message.NetId, message.Flags, message.Tightness, message.Wear);
        }

        public void OnRemotePartSnapshot(WorldPartSnapshot message)
        {
            int applied = 0, parked = 0;
            foreach (var entry in message.Entries)
            {
                if (ApplyPartState(entry.NetId, entry.Flags, entry.Tightness, entry.Wear))
                    applied++;
                else
                {
                    _pendingPartStates[entry.NetId] = new PendingPartState
                    {
                        Flags = entry.Flags,
                        Tightness = entry.Tightness,
                        Wear = entry.Wear,
                        ExpiresAt = Time.unscaledTime + SnapshotPoseTtlSeconds,
                    };
                    parked++;
                }
            }

            WinterMPPlugin.Log.LogInfo($"WorldSync: part snapshot — {message.Entries.Count} entries, {applied} applied, {parked} parked.");
        }

        private void QueuePendingPart(uint netId, byte flags, byte tightness, byte wear)
        {
            _pendingPartStates[netId] = new PendingPartState
            {
                Flags = flags,
                Tightness = tightness,
                Wear = wear,
                ExpiresAt = Time.unscaledTime + SnapshotPoseTtlSeconds,
            };
        }

        private bool ApplyPartState(uint netId, byte flags, byte tightness, byte wear)
        {
            if (!_parts.TryGetValue(netId, out var part) || part.Fsm == null) return false;
            if (!part.Fsm.gameObject.activeInHierarchy || !part.Fsm.enabled) return false;

            WinterMPPlugin.Log.LogInfo(
                $"WorldSync: part {netId:X8} -> installed={((flags & PartState.FlagInstalled) != 0)} tightness={tightness} wear={wear} (remote).");
            _applyingRemote = true;
            try
            {
                if (part.InstalledVar != null)
                    part.InstalledVar.Value = (flags & PartState.FlagInstalled) != 0;
                if (part.TightnessVar != null)
                    part.TightnessVar.Value = DecodeUnitFloat(tightness);
                if (part.WearVar != null)
                    part.WearVar.Value = DecodeUnitFloat(wear);
            }
            finally
            {
                _applyingRemote = false;
            }

            return true;
        }

        private void QueuePendingBolt(uint netId, ushort tightness, ushort screwInt)
        {
            _pendingBoltStates[netId] = new PendingBoltState
            {
                BoltTightness = tightness,
                ScrewInt = screwInt,
                ExpiresAt = Time.unscaledTime + SnapshotPoseTtlSeconds,
            };
        }

        private bool ApplyBoltState(uint netId, ushort tightness, ushort screwInt)
        {
            if (!_bolts.TryGetValue(netId, out var bolt) || bolt.Fsm == null) return false;
            if (!bolt.Fsm.gameObject.activeInHierarchy || !bolt.Fsm.enabled) return false;

            WinterMPPlugin.Log.LogInfo($"WorldSync: bolt {netId:X8} -> tightness={tightness} screw={screwInt} (remote).");
            _applyingRemote = true;
            try
            {
                if (bolt.BoltTightnessVar != null) bolt.BoltTightnessVar.Value = tightness;
                if (bolt.ScrewIntVar != null) bolt.ScrewIntVar.Value = screwInt;
                if (bolt.TightnessFVar != null) bolt.TightnessFVar.Value = tightness;
                if (bolt.ScrewFloatVar != null) bolt.ScrewFloatVar.Value = screwInt;

                if (FsmHook.EnsureRemoteEntry(bolt.Fsm, "Set pos"))
                    FsmHook.FireRemoteEntry(bolt.Fsm, "Set pos");
            }
            finally
            {
                _applyingRemote = false;
            }

            return true;
        }

        private static void ApplySnapshotPose(SyncedItem item, Vector3 position, Quaternion rotation)
        {
            var body = item.Body;
            body.transform.position = position;
            body.transform.rotation = rotation;
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.Sleep();
            }

            // The teleport must not register as local motion (no claim, no stream).
            item.LastPosition = position;
            item.LastMovedAt = -999f;
        }

        // ------------------------------------------------------------------ items

        public void OnRemoteItemTransform(ItemTransform message)
        {
            if (!_items.TryGetValue(message.ItemId, out var item) || item.Body == null) return;

            // Stale unreliable packets from the same owner are dropped (wrap-aware).
            if (message.OwnerPlayerId == item.RemoteOwner)
            {
                if (ItemTransformPolicy.IsStaleSequence(item.LastRemoteSequence, message.Sequence))
                {
                    if (!message.IsFinal && !message.IsDriver)
                        ConnectionQuality.Instance.NoteUnreliableDropped();
                    return;
                }
            }

            if (!message.IsFinal && !message.IsDriver)
                ConnectionQuality.Instance.NoteUnreliableReceived();

            var session = SessionManager.Instance;
            if (item.LocallyOwned && session != null)
            {
                bool localIsDriver = item.IsVehicle && IsLocalVehicleOperator(item);
                bool remoteWins = ItemTransformPolicy.RemoteClaimWinsOverLocal(
                    localIsDriver,
                    message.IsDriver,
                    session.LocalPlayerId,
                    message.OwnerPlayerId,
                    message.IsVehicle,
                    message.IsFinal);
                if (!remoteWins) return;
                item.LocallyOwned = false;
            }

            bool firstPacket = item.RemoteOwner != message.OwnerPlayerId;
            item.RemoteOwner = message.OwnerPlayerId;
            item.RemoteIsDriver = message.IsDriver;
            item.RemoteVehicleStream = message.IsVehicle && !message.IsFinal;
            item.LastRemoteSequence = message.Sequence;

            var body = item.Body;
            if (!item.KinematicSaved)
            {
                item.OriginalKinematic = body.isKinematic;
                item.KinematicSaved = true;
            }

            var position = message.Position.ToUnity();
            var rotation = message.Rotation.ToUnity();

            if (message.IsFinal)
            {
                body.isKinematic = item.OriginalKinematic;
                body.transform.position = position;
                body.transform.rotation = rotation;
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.Sleep();
                }

                item.RemoteOwner = NoOwner;
                item.RemoteIsDriver = false;
                item.RemoteVehicleStream = false;
                item.LastRemoteAt = -999f;
                item.LastMovedAt = -999f; // the landing must not look like local motion
                item.LastPosition = position;
                SetSeatBlocked(item, false);
            }
            else
            {
                // Frozen while remote-driven; UpdateItems eases toward the target so
                // vehicles glide instead of teleporting at packet rate.
                body.isKinematic = true;
                if (firstPacket && item.IsVehicle)
                {
                    WinterMPPlugin.Log.LogInfo(
                        $"WorldSync: '{item.Path}' now {(message.IsDriver ? "driven" : "moved")} by player {message.OwnerPlayerId}.");
                    // Snap on first packet so a car that drove away doesn't stay parked
                    // locally until someone walks up and triggers a huge correction.
                    body.transform.position = position;
                    body.transform.rotation = rotation;
                }

                item.TargetPosition = position;
                item.TargetRotation = rotation;
                item.LastRemoteAt = Time.unscaledTime;

                // An occupied driver's seat must not be enterable locally.
                if (item.IsVehicle)
                    SetSeatBlocked(item, message.IsDriver || (message.IsVehicle && !message.IsFinal));
            }
        }

        private readonly List<Vector3> _remoteVehiclePositions = new List<Vector3>();

        private void UpdateItems(SessionManager session)
        {
            if (session.PlayerCount == 0) return;

            FindLocalPlayer();
            float now = Time.unscaledTime;
            // Remote-driven vehicles first: items inside them must not be claimed
            // locally (their owner streams them), so collect cabin positions.
            _remoteVehiclePositions.Clear();
            foreach (var item in _items.Values)
            {
                if (item.IsVehicle && item.Body != null
                    && item.RemoteOwner != NoOwner
                    && ItemTransformPolicy.IsRemoteStreamLive(
                        item.LastRemoteAt, now, item.RemoteIsDriver, item.RemoteVehicleStream))
                    _remoteVehiclePositions.Add(item.Body.transform.position);
            }

            foreach (var pair in _items)
            {
                var item = pair.Value;
                var body = item.Body;
                if (body == null)
                {
                    if (!item.DespawnSent && !_applyingRemote)
                        AnnounceItemDespawn(pair.Key, "removed");
                    RemoveTrackedItem(pair.Key, null);
                    continue;
                }

                bool operating = item.IsVehicle && IsLocalVehicleOperator(item);
                bool remoteDriven = item.RemoteOwner != NoOwner
                    && ItemTransformPolicy.IsRemoteStreamLive(
                        item.LastRemoteAt, now, item.RemoteIsDriver, item.RemoteVehicleStream);

                if (item.IsVehicle)
                    UpdateRemoteEngineAudio(item, now);

                if (operating && !item.LocallyOwned
                    && LocalDriverOutClaimsRemote(session, item, remoteDriven))
                {
                    ClaimItem(session, item, body, now);
                    remoteDriven = false;
                }

                if (remoteDriven && !item.LocallyOwned)
                {
                    ApplyRemoteSmoothing(item, body);
                    continue;
                }

                if (item.RemoteOwner != NoOwner && !remoteDriven)
                {
                    body.isKinematic = item.OriginalKinematic;
                    item.RemoteOwner = NoOwner;
                    item.RemoteIsDriver = false;
                    item.RemoteVehicleStream = false;
                    SetSeatBlocked(item, false);
                }

                var position = body.transform.position;
                if ((position - item.LastPosition).sqrMagnitude > item.MoveThresholdSqr)
                {
                    item.LastMovedAt = now;
                    item.LastPosition = position;
                }

                bool moving = now - item.LastMovedAt < item.StillSeconds;

                if (item.LocallyOwned)
                {
                    if (item.IsVehicle)
                    {
                        if (operating)
                            item.LocalDriveActive = true;

                        if (item.LocalDriveActive || moving)
                        {
                            if (now >= item.NextSendAt)
                            {
                                SendItem(session, item, body, false);
                                item.NextSendAt = now + (moving || item.LocalDriveActive
                                    ? 1f / item.SendRateHz
                                    : DriverKeepaliveSeconds);
                            }
                        }
                        else if (!ConnectionQuality.Instance.ShouldPauseOwnershipTransfers)
                        {
                            SendItem(session, item, body, true);
                            item.LocallyOwned = false;
                            item.LocalDriveActive = false;
                        }
                        else if (now >= item.NextSendAt)
                        {
                            SendItem(session, item, body, false);
                            item.NextSendAt = now + DriverKeepaliveSeconds;
                        }
                    }
                    else if (!moving)
                    {
                        if (!ConnectionQuality.Instance.ShouldPauseOwnershipTransfers)
                        {
                            SendItem(session, item, body, true);
                            item.LocallyOwned = false;
                        }
                        else if (now >= item.NextSendAt)
                        {
                            SendItem(session, item, body, false);
                            item.NextSendAt = now + DriverKeepaliveSeconds;
                        }
                    }
                    else if (now >= item.NextSendAt)
                    {
                        SendItem(session, item, body, false);
                        item.NextSendAt = now + 1f / item.SendRateHz;
                    }
                }
                else if (moving && CanClaim(item, position, now)
                         && !ConnectionQuality.Instance.ShouldPauseOwnershipTransfers)
                {
                    ClaimItem(session, item, body, now);
                }
            }

        }

        private bool CanClaim(SyncedItem item, Vector3 position, float now)
        {
            FindLocalPlayer();
            if (_localPlayer == null) return false;

            if (item.IsVehicle && IsLocalVehicleOperator(item)) return true;

            if (item.IsVehicle && !IsLocalVehicleOperator(item)
                && !ItemTransformPolicy.AllowsVehicleProximityClaim(
                    item.RemoteIsDriver,
                    item.RemoteVehicleStream && ItemTransformPolicy.IsRemoteStreamLive(
                        item.LastRemoteAt, now, item.RemoteIsDriver, isVehicle: true),
                    item.RemoteOwner,
                    item.LastRemoteAt,
                    now))
                return false;

            if ((position - _localPlayer.position).sqrMagnitude >= item.ClaimRadius * item.ClaimRadius)
                return false;

            // Loose items riding inside someone else's moving vehicle belong to that
            // vehicle's owner — never fight over them from the passenger side.
            if (!item.IsVehicle)
            {
                foreach (var vehiclePosition in _remoteVehiclePositions)
                {
                    if ((position - vehiclePosition).sqrMagnitude < VehicleInteriorRadius * VehicleInteriorRadius)
                        return false;
                }
            }

            return true;
        }

        private void ClaimItem(SessionManager session, SyncedItem item, Rigidbody body, float now)
        {
            // Taking over from a remote stream: restore physics before simulating.
            if (item.RemoteOwner != NoOwner || (body.isKinematic && item.KinematicSaved))
            {
                body.isKinematic = item.OriginalKinematic;
                item.RemoteOwner = NoOwner;
                item.RemoteIsDriver = false;
                item.RemoteVehicleStream = false;
                item.LastRemoteAt = -999f;
                SetSeatBlocked(item, false);
            }

            item.RemoteEngineUntil = -999f;

            item.LocallyOwned = true;
            item.LastMovedAt = now;
            if (item.IsVehicle)
                item.LocalDriveActive = IsLocalPlayerDriving(item) || IsLocalPlayerNearVehicle(item);
            SendItem(session, item, body, false);
            item.NextSendAt = now + 1f / item.SendRateHz;

            if (item.IsVehicle)
                WinterMPPlugin.Log.LogInfo($"WorldSync: claimed vehicle '{item.Path}' " +
                    $"({(IsLocalVehicleOperator(item) ? "driving" : "pushing")}).");
            else
                WinterMPPlugin.Log.LogDebug($"WorldSync: claimed item '{item.Path}'.");
        }

        private static void ApplyRemoteSmoothing(SyncedItem item, Rigidbody body)
        {
            var transform = body.transform;
            if ((transform.position - item.TargetPosition).sqrMagnitude > RemoteSnapDistance * RemoteSnapDistance)
            {
                transform.position = item.TargetPosition;
                transform.rotation = item.TargetRotation;
            }
            else
            {
                float t = 1f - Mathf.Exp(-RemoteLerpSpeed * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, item.TargetPosition, t);
                transform.rotation = Quaternion.Slerp(transform.rotation, item.TargetRotation, t);
            }

            item.LastPosition = transform.position;
        }

        private static Transform GetVehicleSceneRoot(Transform bodyTransform)
        {
            Transform root = bodyTransform;
            while (root.parent != null)
                root = root.parent;
            return root;
        }

        private static float GetRemoteHoldSeconds(SyncedItem item) =>
            ItemTransformPolicy.GetRemoteHoldSeconds(item.RemoteIsDriver, item.RemoteVehicleStream);

        private bool IsLocalVehicleOperator(SyncedItem item)
        {
            if (!item.IsVehicle) return false;
            if (item.LocalDriveActive) return true;
            return IsLocalPlayerDriving(item);
        }

        private bool IsLocalPlayerNearVehicle(SyncedItem item)
        {
            FindLocalPlayer();
            if (_localPlayer == null || item.Body == null) return false;
            float distSq = (_localPlayer.position - item.Body.transform.position).sqrMagnitude;
            return distSq <= VehicleClaimRadius * VehicleClaimRadius;
        }

        private bool IsLocalPlayerDriving(SyncedItem item) => IsLocalPlayerDriving(item.Body, item);

        private bool IsLocalPlayerDriving(Rigidbody vehicleBody, SyncedItem? item = null)
        {
            if (PassengerController.Instance != null && PassengerController.Instance.IsLocalSeated)
                return false;

            FindLocalPlayer();
            if (_localPlayer == null || vehicleBody == null) return false;

            if (item?.PlayerInVar != null && item.PlayerInVar.Value)
                return true;

            Transform vehicleRoot = GetVehicleSceneRoot(vehicleBody.transform);
            if (_localPlayer.IsChildOf(vehicleRoot))
                return true;

            if (_localPlayer.IsChildOf(vehicleBody.transform))
                return true;

            // Enter-seat race: hierarchy may lag one frame; MassDriver is the
            // in-cabin physics anchor the game uses while driving.
            if (item != null)
            {
                EnsureSeat(item);
                if (item.DriverAnchorTransform != null)
                {
                    float distSq = (_localPlayer.position - item.DriverAnchorTransform.position).sqrMagnitude;
                    if (distSq <= 2.25f)
                        return true;
                }
            }

            return false;
        }

        private static bool LocalDriverOutClaimsRemote(SessionManager session, SyncedItem item, bool remoteDriven)
        {
            return ItemTransformPolicy.ShouldSeatDriverOutClaimRemote(
                remoteDriven,
                item.RemoteIsDriver,
                item.RemoteVehicleStream,
                item.LastRemoteAt,
                Time.unscaledTime,
                session.LocalPlayerId,
                item.RemoteOwner);
        }

        // ------------------------------------------------------------------ seats

        /// <summary>
        /// Vehicles carry a "DriveTrigger"/"DriveTriggerX" child whose collider is
        /// the get-in interaction. Found lazily, once per vehicle.
        /// </summary>
        private static void EnsureSeat(SyncedItem item)
        {
            if (item.SeatSearched || item.Body == null) return;
            item.SeatSearched = true;

            foreach (var transform in item.Body.GetComponentsInChildren<Transform>(true))
            {
                string name = transform.name;
                if (item.SeatTransform == null && name.StartsWith("DriveTrigger", StringComparison.Ordinal))
                {
                    item.SeatTransform = transform;
                    item.SeatCollider = transform.GetComponent<Collider>();
                }

                if (item.DriverAnchorTransform == null && name == "MassDriver")
                    item.DriverAnchorTransform = transform;
            }
        }

        /// <summary>
        /// Disable the get-in trigger while a remote driver occupies the vehicle —
        /// otherwise the seat looks free here (remote avatars are visual only) and
        /// a second player could "enter" an already-driven car.
        /// </summary>
        private static void SetSeatBlocked(SyncedItem item, bool blocked)
        {
            if (!item.IsVehicle || item.SeatBlocked == blocked) return;
            EnsureSeat(item);
            item.SeatBlocked = blocked;
            if (item.SeatCollider != null)
                item.SeatCollider.enabled = !blocked;
        }

        /// <summary>Registered vehicle handle for the passenger-seat layer.</summary>
        public struct VehicleInfo
        {
            public uint Id;
            public Rigidbody Body;
        }

        public void CollectVehicles(List<VehicleInfo> results)
        {
            results.Clear();
            foreach (var pair in _items)
            {
                if (pair.Value.IsVehicle && pair.Value.Body != null)
                    results.Add(new VehicleInfo { Id = pair.Key, Body = pair.Value.Body });
            }
        }

        /// <summary>
        /// The vehicle (and its seat) currently driven by the given remote player,
        /// if any — PlayerSync anchors that player's avatar to it so the body rides
        /// in the cabin instead of trailing the smoothed world stream.
        /// </summary>
        public bool TryGetDriverAnchor(byte playerId, out Transform? seat, out Transform? vehicle)
        {
            float now = Time.unscaledTime;
            foreach (var item in _items.Values)
            {
                if (!item.IsVehicle || item.RemoteOwner != playerId) continue;
                if (item.Body == null
                    || !ItemTransformPolicy.IsRemoteStreamLive(
                        item.LastRemoteAt, now, item.RemoteIsDriver, item.RemoteVehicleStream))
                    continue;
                if (!item.RemoteIsDriver && !item.RemoteVehicleStream) continue;

                EnsureSeat(item);
                vehicle = item.Body.transform;
                seat = item.SeatTransform != null ? item.SeatTransform : vehicle;
                return true;
            }

            seat = null;
            vehicle = null;
            return false;
        }

        private void SendItem(SessionManager session, SyncedItem item, Rigidbody body, bool final)
        {
            bool isVehicle = item.IsVehicle;
            bool isDriver = isVehicle && IsLocalVehicleOperator(item);
            byte flags = 0;
            if (final) flags |= ItemTransform.FlagFinal;
            if (isDriver) flags |= ItemTransform.FlagDriver;
            if (isVehicle && !final) flags |= ItemTransform.FlagVehicle;

            var message = new ItemTransform
            {
                ItemId = item.Id,
                OwnerPlayerId = session.LocalPlayerId,
                Sequence = ++item.OutSequence,
                Flags = flags,
                Position = body.transform.position.ToNet(),
                Rotation = body.transform.rotation.ToNet(),
            };

            session.SendWorldMessage(message, ItemTransformPolicy.SelectSendChannel(final, isVehicle));

            if (final && isVehicle)
                item.LocalDriveActive = false;
        }

        // ------------------------------------------------------------------ engine & ignition

        /// <summary>
        /// Stream ignition/engine state for vehicles we are driving *or* whose engine
        /// is running locally (parked idling — pose ownership may already be released).
        /// </summary>
        private void UpdateVehicleStates(SessionManager session)
        {
            if (session.PlayerCount == 0) return;

            float now = Time.unscaledTime;
            foreach (var item in _items.Values)
            {
                if (!item.IsVehicle || item.Body == null) continue;
                if (!item.LocallyOwned && !HasLocalIgnitionActivity(item)) continue;
                SendVehicleState(session, item, now);
            }
        }

        private static bool HasLocalIgnitionActivity(SyncedItem item)
        {
            EnsureVehicleSystemsProbe(item);
            if (!item.SystemsReady) return false;
            return ReadAccOn(item) || ReadEngineRevs(item) > EngineRunningRevs;
        }

        private void SendVehicleState(SessionManager session, SyncedItem item, float now)
        {
            if (now < item.NextVehicleStateAt) return;
            item.NextVehicleStateAt = now + 1f / VehicleStateRateHz;

            EnsureVehicleSystemsProbe(item);
            if (!item.SystemsReady) return;

            float revs = ReadBestRpm(item);
            bool engineOn = revs > EngineRunningRevs;
            bool accOn = ReadAccOn(item) || engineOn;
            float speedKmh = item.GaugeSpeedVar != null ? item.GaugeSpeedVar.Value : 0f;

            byte flags = 0;
            if (engineOn) flags |= VehicleState.FlagEngineOn;
            if (accOn) flags |= VehicleState.FlagAccOn;
            if (ReadBlinkerLeft(item)) flags |= VehicleState.FlagBlinkerLeft;
            if (ReadBlinkerRight(item)) flags |= VehicleState.FlagBlinkerRight;
            if (ReadHazardOn(item)) flags |= VehicleState.FlagHazard;

            if (!item.LoggedEngineSend && (engineOn || accOn))
            {
                item.LoggedEngineSend = true;
                WinterMPPlugin.Log.LogInfo(
                    $"WorldSync: streaming '{item.Path}' ignition — engine={engineOn} ACC={accOn} revs={revs:0} speed={speedKmh:0.0} km/h.");
            }

            session.SendWorldMessage(new VehicleState
            {
                VehicleId = item.Id,
                OwnerPlayerId = session.LocalPlayerId,
                Sequence = ++item.OutVehicleStateSequence,
                Flags = flags,
                Rpm = (ushort)Mathf.Clamp(revs, 0f, ushort.MaxValue),
                SpeedTenthsKmh = (ushort)Mathf.Clamp(speedKmh * 10f, 0f, ushort.MaxValue),
                FuelLevel = ReadFuelLevelByte(item),
                CoolantTemp = ReadCoolantTempByte(item),
            }, Channel.UnreliableSequenced);
        }

        private static float ReadEngineRevs(SyncedItem item)
        {
            return item.EngineRevsVar != null ? item.EngineRevsVar.Value : 0f;
        }

        private static float ReadBestRpm(SyncedItem item)
        {
            // The dashboard's RPM float is closest to what the driver sees; fall
            // back to engine sim variables when the gauge object is inactive.
            if (item.GaugeRpmVar != null && item.GaugeRpmVar.Value > 1f)
                return item.GaugeRpmVar.Value;
            return ReadEngineRevs(item);
        }

        private static bool ReadAccOn(SyncedItem item)
        {
            if (item.ElectricityPowerFsm == null) return false;

            var acc = item.ElectricityPowerFsm.FsmVariables.FindFsmBool("ACC");
            if (acc != null && acc.Value) return true;

            try
            {
                if (item.ElectricityPowerFsm.Fsm.ActiveStateName == "ON") return true;
            }
            catch
            {
                // FSM not initialized yet.
            }

            return false;
        }

        public void OnRemoteVehicleState(VehicleState message)
        {
            if (!_items.TryGetValue(message.VehicleId, out var item) || item.Body == null || !item.IsVehicle)
                return;

            ushort diff = (ushort)(message.Sequence - item.LastVehicleStateSequence);
            if (diff == 0 || diff > short.MaxValue)
            {
                ConnectionQuality.Instance.NoteUnreliableDropped();
                return;
            }

            ConnectionQuality.Instance.NoteUnreliableReceived();
            item.LastVehicleStateSequence = message.Sequence;

            bool electricsOn = message.AccOn || message.EngineOn;
            bool electricsChanged = electricsOn != item.RemoteElectricsApplied;
            item.RemoteEngineOn = message.EngineOn;
            item.RemoteAccOn = message.AccOn;
            item.RemoteRpm = message.Rpm;
            item.RemoteSpeedKmh = message.SpeedTenthsKmh * 0.1f;
            item.RemoteFuelLevel = message.FuelLevel;
            item.RemoteCoolantTemp = message.CoolantTemp;
            item.RemoteEngineUntil = Time.unscaledTime + EngineAudioHoldSeconds;

            bool blinkersChanged = message.BlinkerLeft != item.RemoteBlinkerLeft
                || message.BlinkerRight != item.RemoteBlinkerRight;
            bool hazardChanged = message.HazardOn != item.RemoteHazard;
            item.RemoteBlinkerLeft = message.BlinkerLeft;
            item.RemoteBlinkerRight = message.BlinkerRight;
            item.RemoteHazard = message.HazardOn;

            if (!item.LocallyOwned && electricsChanged)
                ApplyRemoteElectricity(item, electricsOn);

            if (!item.LocallyOwned && (blinkersChanged || hazardChanged))
                ApplyRemoteLights(item, message.BlinkerLeft, message.BlinkerRight, message.HazardOn);
        }

        /// <summary>
        /// Replay the Electricity :: Power FSM into ON/OFF on the remote copy so
        /// dash lights, gauge power and child simulators wake the same way as
        /// locally — SetActive on PowerON alone does not run the state's actions.
        /// </summary>
        private static void ApplyRemoteElectricity(SyncedItem item, bool on)
        {
            EnsureVehicleSystemsProbe(item);
            item.RemoteElectricsApplied = on;

            if (item.ElectricityPowerFsm == null)
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: no Electricity FSM on '{item.Path}' — remote ignition skipped.");
                return;
            }

            string state = on ? "ON" : "OFF";
            if (!FsmHook.EnsureRemoteEntry(item.ElectricityPowerFsm, state))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: Electricity '{state}' missing on '{item.Path}'.");
                return;
            }

            WinterMPPlugin.Log.LogInfo($"WorldSync: remote electricity '{item.Path}' -> {state}.");
            FsmHook.FireRemoteEntry(item.ElectricityPowerFsm, state);
        }

        private static void ApplyRemoteGauges(SyncedItem item)
        {
            if (item.GaugeSpeedVar != null)
                item.GaugeSpeedVar.Value = item.RemoteSpeedKmh;
            if (item.GaugeSpeedAngleVar != null)
                item.GaugeSpeedAngleVar.Value = SpeedAngleForKmh(item.RemoteSpeedKmh);

            if (item.GaugeRpmVar != null)
                item.GaugeRpmVar.Value = item.RemoteRpm;

            if (item.GaugeTachRevsVar != null)
                item.GaugeTachRevsVar.Value = item.RemoteRpm;
            if (item.GaugeTachRotationVar != null)
                item.GaugeTachRotationVar.Value = TachRotationForRpm(item.RemoteRpm);

            if (item.GaugeTachNeedle != null)
            {
                var euler = item.GaugeTachNeedle.transform.localEulerAngles;
                euler.z = TachRotationForRpm(item.RemoteRpm);
                item.GaugeTachNeedle.transform.localEulerAngles = euler;
            }

            float fuel01 = item.RemoteFuelLevel / 255f;
            if (item.GaugeFuelLevelVar != null)
                item.GaugeFuelLevelVar.Value = fuel01;

            if (item.GaugeFuelAngleVar != null)
                item.GaugeFuelAngleVar.Value = FuelAngleForLevel(fuel01);

            float coolantC = item.RemoteCoolantTemp / 255f * CoolantTempMaxC;
            if (item.GaugeCoolantVar != null)
                item.GaugeCoolantVar.Value = coolantC;
            if (item.GaugeCoolantAngleVar != null)
                item.GaugeCoolantAngleVar.Value = CoolantAngleForTemp(coolantC);
        }

        private static void ApplyRemoteLights(SyncedItem item, bool left, bool right, bool hazard)
        {
            EnsureVehicleSystemsProbe(item);

            if (hazard)
            {
                ApplyRemoteHazardButton(item, true);
                if (item.BlinkerHazardsVar != null)
                    item.BlinkerHazardsVar.Value = true;

                if (item.TurnSignalsFsm != null)
                {
                    try { item.TurnSignalsFsm.SendEvent("BLINKERS"); }
                    catch { /* FSM not ready yet */ }
                }

                return;
            }

            ApplyRemoteHazardButton(item, false);
            if (item.BlinkerHazardsVar != null)
                item.BlinkerHazardsVar.Value = false;

            if (item.TurnSignalStalkFsm != null)
            {
                string state = right ? "On" : left ? "On 2" : "Check player";
                if (FsmHook.EnsureRemoteEntry(item.TurnSignalStalkFsm, state))
                    FsmHook.FireRemoteEntry(item.TurnSignalStalkFsm, state);
                return;
            }

            if (item.TurnSignalsFsm == null) return;

            if (item.BlinkerLeftVar != null) item.BlinkerLeftVar.Value = left;
            if (item.BlinkerRightVar != null) item.BlinkerRightVar.Value = right;

            try
            {
                if (left && !right)
                    item.TurnSignalsFsm.SendEvent("LEFT");
                else if (right && !left)
                    item.TurnSignalsFsm.SendEvent("RIGHT");
                else
                    item.TurnSignalsFsm.SendEvent("OFF");
            }
            catch
            {
                // FSM not ready yet.
            }
        }

        private static void ApplyRemoteHazardButton(SyncedItem item, bool on)
        {
            if (item.HazardButtonFsm == null) return;

            string state = on ? "On" : "Off";
            if (!FsmHook.EnsureRemoteEntry(item.HazardButtonFsm, state)) return;
            FsmHook.FireRemoteEntry(item.HazardButtonFsm, state);
            SetHazardVisual(item.HazardButtonFsm, on);
        }

        private static void UpdateRemoteLightPresentation(SyncedItem item)
        {
            if (item.HazardButtonFsm != null)
                SetHazardVisual(item.HazardButtonFsm, item.RemoteHazard);

            if (item.BlinkerHazardsVar != null)
                item.BlinkerHazardsVar.Value = item.RemoteHazard;

            if (item.RemoteHazard) return;

            if (item.BlinkerLeftVar != null)
                item.BlinkerLeftVar.Value = item.RemoteBlinkerLeft;
            if (item.BlinkerRightVar != null)
                item.BlinkerRightVar.Value = item.RemoteBlinkerRight;
        }

        private static void SetHazardVisual(PlayMakerFSM fsm, bool on)
        {
            var hazardsOn = fsm.FsmVariables.FindFsmBool("HazardsOn");
            if (hazardsOn != null)
                hazardsOn.Value = on;

            var buttonOn = fsm.FsmVariables.FindFsmBool("ButtonOn");
            if (buttonOn != null)
                buttonOn.Value = on;

            var light = fsm.FsmVariables.GetFsmGameObject("Light");
            if (light != null && light.Value != null)
                light.Value.SetActive(on);
        }

        private static bool ReadBlinkerLeft(SyncedItem item)
        {
            if (item.BlinkerLeftVar != null) return item.BlinkerLeftVar.Value;
            if (item.TurnSignalStalkFsm == null) return false;

            try { return item.TurnSignalStalkFsm.Fsm.ActiveStateName == "On 2"; }
            catch { return false; }
        }

        private static bool ReadBlinkerRight(SyncedItem item)
        {
            if (item.BlinkerRightVar != null) return item.BlinkerRightVar.Value;
            if (item.TurnSignalStalkFsm == null) return false;

            try { return item.TurnSignalStalkFsm.Fsm.ActiveStateName == "On"; }
            catch { return false; }
        }

        private static byte ReadFuelLevelByte(SyncedItem item)
        {
            if (item.GaugeFuelLevelVar != null)
            {
                float level = item.GaugeFuelLevelVar.Value;
                if (level <= 1.01f)
                    return (byte)Mathf.Clamp(Mathf.RoundToInt(level * 255f), 0, 255);
            }

            if (item.FuelTankLevelVar != null)
            {
                float capacity = item.FuelTankCapacityVar?.Value ?? FuelTankDefaultLiters;
                if (capacity > 0f)
                {
                    return (byte)Mathf.Clamp(
                        Mathf.RoundToInt(item.FuelTankLevelVar.Value / capacity * 255f), 0, 255);
                }
            }

            return 0;
        }

        private static float TachRotationForRpm(float rpm)
        {
            // Approximate visible sweep for the SORBET gauge. This is deliberately
            // conservative: status lights/ignition are authoritative; the tach is
            // presentation, and over-rotation looks much worse than under-rotation.
            return Mathf.Lerp(0f, -235f, Mathf.Clamp01(rpm / TachMaxRpm));
        }

        private static float SpeedAngleForKmh(float kmh) =>
            Mathf.Lerp(0f, -220f, Mathf.Clamp01(kmh / SpeedoMaxKmh));

        private static float FuelAngleForLevel(float level01) =>
            Mathf.Lerp(0f, -90f, 1f - Mathf.Clamp01(level01));

        private static void UpdateRemoteEngineAudio(SyncedItem item, float now)
        {
            if (!item.LocallyOwned && now >= item.RemoteEngineUntil
                && (item.RemoteEngineOn || item.RemoteAccOn || item.RemoteElectricsApplied))
            {
                item.RemoteEngineOn = false;
                item.RemoteAccOn = false;
                if (item.RemoteElectricsApplied)
                    ApplyRemoteElectricity(item, false);
            }

            if (!item.LocallyOwned && item.RemoteElectricsApplied)
            {
                ApplyRemoteGauges(item);
                UpdateRemoteLightPresentation(item);
            }

            bool shouldPlay = !item.LocallyOwned && item.RemoteEngineOn && now < item.RemoteEngineUntil;
            if (!shouldPlay)
            {
                if (item.RemoteEngineAudio != null && item.RemoteEngineAudio.isPlaying)
                    item.RemoteEngineAudio.Stop();
                return;
            }

            var source = EnsureRemoteEngineAudio(item);
            if (source == null) return;

            float targetPitch = Mathf.Clamp(
                EnginePitchBase + item.RemoteRpm * EnginePitchPerRpm,
                EnginePitchMin,
                EnginePitchMax);
            source.pitch = Mathf.Lerp(source.pitch, targetPitch, 1f - Mathf.Exp(-6f * Time.deltaTime));
            if (!source.isPlaying)
                source.Play();
        }

        private static void EnsureVehicleSystemsProbe(SyncedItem item)
        {
            if (item.Body == null) return;

            if (item.SystemsReady)
            {
                EnsureClimateProbe(item);
                return;
            }

            if (Time.unscaledTime < item.NextSystemsProbeAt) return;
            item.NextSystemsProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;

            foreach (var fsm in item.Body.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (item.ElectricityPowerFsm == null
                    && fsm.FsmName == "Power"
                    && fsm.gameObject.name == "Electricity")
                {
                    item.ElectricityPowerFsm = fsm;
                }

                if (item.EngineRevsVar == null)
                {
                    var revs = fsm.FsmVariables.FindFsmFloat("Revs");
                    if (revs != null && (fsm.FsmName == "FuelLine" || fsm.FsmName == "RotateEngine"))
                        item.EngineRevsVar = revs;
                }

                string path = ScenePath.Of(fsm.transform);
                if (item.GaugeSpeedVar == null && fsm.FsmName == "Speedo")
                {
                    var speed = fsm.FsmVariables.FindFsmFloat("Speed");
                    if (speed != null)
                    {
                        item.GaugeSpeedVar = speed;
                        item.GaugeSpeedAngleVar = fsm.FsmVariables.FindFsmFloat("Angle");
                        item.GaugeRpmVar = fsm.FsmVariables.FindFsmFloat("RPM")
                            ?? fsm.FsmVariables.FindFsmFloat("Revs");
                    }
                }

                if (item.GaugeFuelLevelVar == null && fsm.FsmName == "Fuel")
                {
                    var level = fsm.FsmVariables.FindFsmFloat("Level");
                    if (level != null)
                    {
                        item.GaugeFuelLevelVar = level;
                        item.GaugeFuelAngleVar = fsm.FsmVariables.FindFsmFloat("Angle");
                    }
                }

                if (item.FuelTankLevelVar == null && fsm.FsmName == "Data")
                {
                    var tankLevel = fsm.FsmVariables.FindFsmFloat("FuelLevel");
                    var capacity = fsm.FsmVariables.FindFsmFloat("MaxCapacity");
                    if (tankLevel != null && capacity != null)
                    {
                        item.FuelTankLevelVar = tankLevel;
                        item.FuelTankCapacityVar = capacity;
                    }
                }

                if (item.TurnSignalStalkFsm == null && fsm.FsmName == "Usage"
                    && fsm.gameObject.name == "TurnSignalsX")
                {
                    item.TurnSignalStalkFsm = fsm;
                }

                if (item.TurnSignalsFsm == null && fsm.FsmName == "TurnSignals"
                    && path.IndexOf("/PowerON/Systems", StringComparison.Ordinal) >= 0)
                {
                    item.TurnSignalsFsm = fsm;
                    item.BlinkerLeftVar = fsm.FsmVariables.FindFsmBool("BlinkerLeft");
                    item.BlinkerRightVar = fsm.FsmVariables.FindFsmBool("BlinkerRight");
                    item.BlinkerHazardsVar = fsm.FsmVariables.FindFsmBool("BlinkerHazards");
                }

                if (item.HazardButtonFsm == null && fsm.FsmName == "Use"
                    && fsm.gameObject.name == "ButtonHazard"
                    && HasAllStates(fsm, "On", "Off"))
                {
                    item.HazardButtonFsm = fsm;
                }

                if (item.GaugeCoolantAngleVar == null && fsm.FsmName == "Temp"
                    && (path.IndexOf("GaugeData", StringComparison.Ordinal) >= 0
                        || path.IndexOf("StandardGaugesData", StringComparison.Ordinal) >= 0))
                {
                    var rotation = fsm.FsmVariables.FindFsmFloat("Rotation");
                    var angle = fsm.FsmVariables.FindFsmFloat("Angle");
                    if (rotation != null || angle != null)
                    {
                        item.GaugeCoolantAngleVar = rotation ?? angle;
                        item.GaugeCoolantVar = fsm.FsmVariables.FindFsmFloat("Temp");
                    }
                }

                if (item.GaugeTachDataFsm == null && fsm.FsmName == "Tach"
                    && (fsm.FsmVariables.FindFsmFloat("Revs") != null
                        || fsm.FsmVariables.FindFsmFloat("Rotation") != null
                        || fsm.FsmVariables.FindFsmFloat("Angle") != null))
                {
                    item.GaugeTachDataFsm = fsm;
                    item.GaugeTachRevsVar = fsm.FsmVariables.FindFsmFloat("Revs");
                    item.GaugeTachRotationVar = fsm.FsmVariables.FindFsmFloat("Rotation")
                        ?? fsm.FsmVariables.FindFsmFloat("Angle");
                    item.GaugeTachNeedle = fsm.FsmVariables.GetFsmGameObject("Needle")?.Value;
                }
            }

            foreach (var transform in item.Body.GetComponentsInChildren<Transform>(true))
            {
                if (item.AudioEngine != null) break;
                if (transform.name != "AudioEngine") continue;
                item.AudioEngine = transform.gameObject;
            }

            // Electricity + revs are the minimum; audio/gauges are nice-to-have.
            if (item.ElectricityPowerFsm != null && item.EngineRevsVar != null)
            {
                item.SystemsReady = true;
                WinterMPPlugin.Log.LogInfo($"WorldSync: vehicle systems ready on '{item.Path}'.");
            }

            EnsureClimateProbe(item);
        }

        private static void EnsureClimateProbe(SyncedItem item)
        {
            if (item.ClimateReady || item.Body == null) return;
            if (Time.unscaledTime < item.NextClimateProbeAt) return;
            item.NextClimateProbeAt = Time.unscaledTime + ClimateProbeIntervalSeconds;

            foreach (var fsm in item.Body.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                string path = ScenePath.Of(fsm.transform);
                if (!SyncCatalog.IsClimateVehicleFsmPath(path)) continue;

                bool carTempRoot = SyncCatalog.IsCarTempFsmPath(path);

                if (item.GlassFrostingFsm == null && fsm.FsmName == "GlassFrosting" && carTempRoot)
                {
                    item.GlassFrostingFsm = fsm;
                    item.FrostVar = fsm.FsmVariables.FindFsmFloat("Frost");
                    item.SweatRateVar = fsm.FsmVariables.FindFsmFloat("SweatRate");
                    item.GlassTempVar = fsm.FsmVariables.FindFsmFloat("Temp");
                    item.PlayerInVar = fsm.FsmVariables.FindFsmBool("PlayerIn");
                    item.FrostColorVar = fsm.FsmVariables.FindFsmColor("Color");
                    item.FrostGlassMat = fsm.FsmVariables.GetFsmMaterial("FrostGlass");
                }

                if (item.FreezingFsm == null && fsm.FsmName == "Freezing" && carTempRoot)
                {
                    item.FreezingFsm = fsm;
                    item.CutoffWindshieldVar = fsm.FsmVariables.FindFsmFloat("CutoffWindshield");
                    item.CutoffSideLeftVar = fsm.FsmVariables.FindFsmFloat("CutoffSideLeft");
                    item.CutoffSideRightVar = fsm.FsmVariables.FindFsmFloat("CutoffSideRight");
                    item.CutoffDoorLeftVar = fsm.FsmVariables.FindFsmFloat("CutoffDoorleft");
                    item.CutoffDoorRightVar = fsm.FsmVariables.FindFsmFloat("CutoffDoorright");
                    item.CutoffRearVar = fsm.FsmVariables.FindFsmFloat("CutoffRear");
                }

                if (item.CarTempDataFsm == null && fsm.FsmName == "Data" && carTempRoot
                    && fsm.FsmVariables.FindFsmFloat("InteriorTemp") != null)
                {
                    item.CarTempDataFsm = fsm;
                    item.InteriorTempVar = fsm.FsmVariables.FindFsmFloat("InteriorTemp");
                }

                if (item.HeaterUnitFsm == null && fsm.FsmName == "Function"
                    && SyncCatalog.IsHeaterFsmPath(path))
                {
                    item.HeaterUnitFsm = fsm;
                    item.HeaterSettingTemp = fsm.FsmVariables.FindFsmFloat("SettingTemp");
                    item.HeaterSettingBlower = fsm.FsmVariables.FindFsmFloat("SettingBlower");
                    item.HeaterSettingDirection = fsm.FsmVariables.FindFsmFloat("SettingDirection");
                    item.GlassDefrostingVar = fsm.FsmVariables.FindFsmBool("GlassDefrosting");
                }

                if (fsm.FsmName != "Use") continue;

                string name = fsm.gameObject.name;
                if (name == "ButtonHeaterTemp")
                {
                    item.KnobTempFsm = fsm;
                    item.KnobTempSetting = fsm.FsmVariables.FindFsmFloat("Setting");
                    item.KnobTempAngle = fsm.FsmVariables.FindFsmFloat("Angle");
                }
                else if (name == "ButtonHeaterBlower")
                {
                    item.KnobBlowerFsm = fsm;
                    item.KnobBlowerSetting = fsm.FsmVariables.FindFsmFloat("Setting");
                    item.KnobBlowerAngle = fsm.FsmVariables.FindFsmFloat("Angle");
                    item.KnobBlowerCommitState = FsmHook.HasState(fsm, "Set angle") ? "Set angle" : "Set";
                }
                else if (name == "ButtonHeaterDirection")
                {
                    item.KnobDirectionFsm = fsm;
                    item.KnobDirectionSetting = fsm.FsmVariables.FindFsmFloat("Setting");
                    item.KnobDirectionAngle = fsm.FsmVariables.FindFsmFloat("Angle");
                }
                else if (name == "ButtonWindowHeater")
                {
                    item.WindowHeaterButtonFsm = fsm;
                    item.WindowHeaterOnVar = fsm.FsmVariables.FindFsmBool("ButtonOn");
                }
            }

            if (item.GlassFrostingFsm != null && item.FrostVar != null)
            {
                item.ClimateReady = true;
                WinterMPPlugin.Log.LogInfo($"WorldSync: climate ready on '{item.Path}'.");
            }
        }

        private void UpdateVehicleClimate(SessionManager session)
        {
            if (session.PlayerCount == 0) return;

            float now = Time.unscaledTime;
            foreach (var item in _items.Values)
            {
                if (!item.IsVehicle || item.Body == null) continue;
                EnsureClimateProbe(item);
                if (!ShouldStreamVehicleClimate(item, now)) continue;
                SendVehicleClimate(session, item, now);
            }
        }

        private bool ShouldStreamVehicleClimate(SyncedItem item, float now)
        {
            if (item.LocallyOwned || HasLocalIgnitionActivity(item)) return true;

            var passenger = PassengerController.Instance;
            if (passenger != null && passenger.IsLocalSeatedInVehicle(item.Id))
                return true;

            // Parked frost still matters to anyone standing near the car.
            if (now - item.LastRemoteAt < GetRemoteHoldSeconds(item)) return false;
            FindLocalPlayer();
            if (_localPlayer == null || item.Body == null) return false;

            float distSq = (_localPlayer.position - item.Body.transform.position).sqrMagnitude;
            return distSq <= item.ClaimRadius * item.ClaimRadius;
        }

        private VehicleClimate? TryBuildVehicleClimate(SyncedItem item)
        {
            EnsureClimateProbe(item);
            if (!item.ClimateReady) return null;

            byte flags = 0;
            if (ReadWindowHeaterOn(item)) flags |= VehicleClimate.FlagWindowHeater;
            if (ReadGlassDefrosting(item)) flags |= VehicleClimate.FlagGlassDefrosting;
            if (ReadPlayerInCar(item)) flags |= VehicleClimate.FlagPlayerIn;

            return new VehicleClimate
            {
                VehicleId = item.Id,
                OwnerPlayerId = SessionManager.Instance?.LocalPlayerId ?? 0,
                Frost = QuantizeFrost(ReadFrost(item)),
                Flags = flags,
                HeaterTemp = QuantizeHeater(ReadHeaterTemp(item), HeaterTempMax),
                HeaterBlower = QuantizeHeater(ReadHeaterBlower(item), HeaterBlowerMax),
                HeaterDirection = QuantizeHeater(ReadHeaterDirection(item), HeaterDirectionMax),
                Fog = QuantizeFrost(ReadFog(item)),
                CabinTemp = QuantizeHeater(ReadCabinTemp(item), CabinTempMaxC),
            };
        }

        private void SendVehicleClimate(SessionManager session, SyncedItem item, float now)
        {
            if (now < item.NextClimateAt) return;
            item.NextClimateAt = now + 1f / VehicleClimateRateHz;

            var message = TryBuildVehicleClimate(item);
            if (message == null) return;

            message.Sequence = ++item.OutClimateSequence;
            message.OwnerPlayerId = session.LocalPlayerId;

            if (!item.LoggedClimateSend)
            {
                item.LoggedClimateSend = true;
                WinterMPPlugin.Log.LogInfo(
                    $"WorldSync: streaming '{item.Path}' climate — frost={message.Frost} fog={message.Fog} " +
                    $"cabin={message.CabinTemp} heater={message.HeaterTemp}/{message.HeaterBlower}/{message.HeaterDirection} " +
                    $"flags=0x{message.Flags:X2}.");
            }

            session.SendWorldMessage(message, Channel.UnreliableSequenced);
        }

        public void OnRemoteVehicleClimate(VehicleClimate message)
        {
            if (!_items.TryGetValue(message.VehicleId, out var item) || item.Body == null || !item.IsVehicle)
                return;
            if (item.LocallyOwned) return;

            ushort diff = (ushort)(message.Sequence - item.LastClimateSequence);
            if (diff == 0 || diff > short.MaxValue)
            {
                ConnectionQuality.Instance.NoteUnreliableDropped();
                return;
            }

            ConnectionQuality.Instance.NoteUnreliableReceived();
            item.LastClimateSequence = message.Sequence;
            item.RemoteClimateUntil = Time.unscaledTime + ClimateHoldSeconds;

            ApplyRemoteClimate(item, message);
        }

        private static void ApplyRemoteClimate(SyncedItem item, VehicleClimate message)
        {
            EnsureClimateProbe(item);
            if (!item.ClimateReady) return;

            bool windowHeater = message.WindowHeaterOn;
            bool glassDefrosting = message.GlassDefrosting;
            bool defrostActive = windowHeater || glassDefrosting;
            bool wasDefrost = item.RemoteWindowHeater || item.RemoteGlassDefrosting;
            bool windowHeaterChanged = windowHeater != item.RemoteWindowHeater;

            item.RemoteFrost = message.Frost;
            item.RemoteFog = message.Fog;
            item.RemoteCabinTemp = message.CabinTemp;
            item.RemotePlayerIn = message.PlayerIn;
            item.RemoteHeaterTemp = message.HeaterTemp;
            item.RemoteHeaterBlower = message.HeaterBlower;
            item.RemoteHeaterDirection = message.HeaterDirection;
            item.RemoteWindowHeater = windowHeater;
            item.RemoteGlassDefrosting = glassDefrosting;

            ApplyRemoteHeaterKnobs(item, replayStates: false);

            if (windowHeaterChanged || !item.LoggedClimateApply)
                ApplyRemoteWindowHeater(item, windowHeater);

            if (defrostActive && !wasDefrost)
                PulseRemoteDefrost(item, true);
            else if (!defrostActive && wasDefrost)
                PulseRemoteDefrost(item, false);

            ApplyRemoteClimatePresentation(item);

            if (!item.LoggedClimateApply)
            {
                item.LoggedClimateApply = true;
                WinterMPPlugin.Log.LogInfo(
                    $"WorldSync: remote climate on '{item.Path}' — frost={message.Frost} fog={message.Fog} " +
                    $"windowHeater={windowHeater} defrost={glassDefrosting}.");
            }
        }

        private static void UpdateRemoteClimatePresentation(SyncedItem item, float now)
        {
            EnsureClimateProbe(item);
            if (!item.ClimateReady) return;

            ApplyRemoteClimatePresentation(item);

            bool defrostActive = item.RemoteWindowHeater || item.RemoteGlassDefrosting;
            if (item.GlassDefrostingVar != null)
                item.GlassDefrostingVar.Value = defrostActive;

            if (item.RemoteHeaterTemp != item.PresentedHeaterTemp
                || item.RemoteHeaterBlower != item.PresentedHeaterBlower
                || item.RemoteHeaterDirection != item.PresentedHeaterDirection)
            {
                ApplyRemoteHeaterKnobs(item, replayStates: true);
                item.PresentedHeaterTemp = item.RemoteHeaterTemp;
                item.PresentedHeaterBlower = item.RemoteHeaterBlower;
                item.PresentedHeaterDirection = item.RemoteHeaterDirection;
            }

            if (!defrostActive || now < item.NextDefrostPulseAt) return;
            item.NextDefrostPulseAt = now + DefrostPulseSeconds;
            PulseRemoteDefrost(item, true);
        }

        private static void ApplyRemoteClimatePresentation(SyncedItem item)
        {
            float frost = DequantizeFrost(item.RemoteFrost);
            float fog = DequantizeFrost(item.RemoteFog);
            float cabinTemp = DequantizeHeater(item.RemoteCabinTemp, CabinTempMaxC);

            ApplyRemoteFrostLevel(item, frost);
            ApplyRemoteFogLevel(item, fog, cabinTemp, item.RemotePlayerIn);
        }

        private static void ApplyRemoteFrostLevel(SyncedItem item, float frost)
        {
            if (item.FrostVar != null)
                item.FrostVar.Value = frost;

            if (item.FrostColorVar != null)
            {
                var color = item.FrostColorVar.Value;
                color.a = frost;
                item.FrostColorVar.Value = color;
            }

            WriteCutoff(item.CutoffWindshieldVar, frost);
            WriteCutoff(item.CutoffSideLeftVar, frost);
            WriteCutoff(item.CutoffSideRightVar, frost);
            WriteCutoff(item.CutoffDoorLeftVar, frost);
            WriteCutoff(item.CutoffDoorRightVar, frost);
            WriteCutoff(item.CutoffRearVar, frost);

            ApplyFrostGlassMaterial(item, fog: -1f, frost: frost);
        }

        private static void ApplyRemoteFogLevel(SyncedItem item, float fog, float cabinTemp, bool playerIn)
        {
            if (item.SweatRateVar != null)
                item.SweatRateVar.Value = fog;

            if (item.GlassTempVar != null)
                item.GlassTempVar.Value = cabinTemp;

            if (item.InteriorTempVar != null)
                item.InteriorTempVar.Value = cabinTemp;

            if (item.PlayerInVar != null)
                item.PlayerInVar.Value = playerIn;

            if (item.FrostColorVar != null)
            {
                var color = item.FrostColorVar.Value;
                color.r = fog;
                color.g = fog;
                color.b = fog;
                item.FrostColorVar.Value = color;
            }

            ApplyFrostGlassMaterial(item, fog, frost: -1f);
        }

        private static void ApplyFrostGlassMaterial(SyncedItem item, float fog, float frost)
        {
            if (item.FrostGlassMat == null || item.FrostGlassMat.Value == null) return;

            var mat = item.FrostGlassMat.Value;
            if (fog >= 0f)
            {
                if (mat.HasProperty("_Color"))
                {
                    var c = mat.color;
                    c.r = fog;
                    c.g = fog;
                    c.b = fog;
                    mat.color = c;
                }
            }

            if (frost >= 0f && mat.HasProperty("_Cutoff"))
                mat.SetFloat("_Cutoff", frost);
        }

        private static void WriteCutoff(HutongGames.PlayMaker.FsmFloat? var, float frost)
        {
            if (var != null) var.Value = frost;
        }

        private SyncedItem? FindVehicleItemForFsm(PlayMakerFSM fsm)
        {
            var t = fsm.transform;
            while (t != null)
            {
                var rb = t.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    foreach (var item in _items.Values)
                    {
                        if (item.Body == rb && item.IsVehicle)
                            return item;
                    }
                }

                t = t.parent;
            }

            return null;
        }

        private void PrepareRemoteControl(SyncedControl control)
        {
            var item = FindVehicleItemForFsm(control.Fsm);
            if (item == null) return;

            string path = control.Path;
            if (path.IndexOf("ButtonWindowHeater", StringComparison.Ordinal) >= 0
                || path.IndexOf("ButtonHazard", StringComparison.Ordinal) >= 0)
            {
                EnsureVehicleSystemsProbe(item);
                if (item.RemoteAccOn && !item.RemoteElectricsApplied)
                    ApplyRemoteElectricity(item, true);
            }
            else if (path.IndexOf("ButtonHeater", StringComparison.Ordinal) >= 0)
            {
                ApplyRemoteHeaterKnobs(item, replayStates: false);
            }
        }

        private static void FinishRemoteControl(SyncedControl control, string stateName)
        {
            if (control.Path.IndexOf("ButtonWindowHeater", StringComparison.Ordinal) >= 0)
                SetWindowHeaterVisual(control.Fsm, stateName == "On");
            else if (control.Path.IndexOf("ButtonHazard", StringComparison.Ordinal) >= 0)
                SetHazardVisual(control.Fsm, stateName == "On");
        }

        private static void SetWindowHeaterVisual(PlayMakerFSM fsm, bool on)
        {
            var buttonOn = fsm.FsmVariables.FindFsmBool("ButtonOn");
            if (buttonOn != null)
                buttonOn.Value = on;

            var light = fsm.FsmVariables.GetFsmGameObject("Light");
            if (light != null && light.Value != null)
                light.Value.SetActive(on);
        }

        private static void ApplyRemoteHeaterKnobs(SyncedItem item, bool replayStates)
        {
            float temp = DequantizeHeater(item.RemoteHeaterTemp, HeaterTempMax);
            float blower = DequantizeHeater(item.RemoteHeaterBlower, HeaterBlowerMax);
            float direction = DequantizeHeater(item.RemoteHeaterDirection, HeaterDirectionMax);

            WriteHeaterKnob(item.HeaterSettingTemp, item.KnobTempSetting, item.KnobTempAngle, temp);
            WriteHeaterKnob(item.HeaterSettingBlower, item.KnobBlowerSetting, item.KnobBlowerAngle, blower);
            WriteHeaterKnob(item.HeaterSettingDirection, item.KnobDirectionSetting, item.KnobDirectionAngle, direction);

            if (!replayStates) return;

            FireKnobCommitState(item.KnobTempFsm, "Set");
            FireKnobCommitState(item.KnobBlowerFsm, item.KnobBlowerCommitState);
            FireKnobCommitState(item.KnobDirectionFsm, "Set");
        }

        private static void FireKnobCommitState(PlayMakerFSM? fsm, string stateName)
        {
            if (fsm == null || !FsmHook.EnsureRemoteEntry(fsm, stateName)) return;

            var inst = Instance;
            if (inst != null) inst._applyingRemote = true;
            try
            {
                FsmHook.FireRemoteEntry(fsm, stateName);
            }
            finally
            {
                if (inst != null) inst._applyingRemote = false;
            }
        }

        private static void ApplyRemoteWindowHeater(SyncedItem item, bool on)
        {
            if (item.WindowHeaterButtonFsm == null) return;

            string state = on ? "On" : "Off";
            if (!FsmHook.EnsureRemoteEntry(item.WindowHeaterButtonFsm, state))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"WorldSync: window heater '{state}' missing on '{item.Path}'.");
                return;
            }

            FsmHook.FireRemoteEntry(item.WindowHeaterButtonFsm, state);
            SetWindowHeaterVisual(item.WindowHeaterButtonFsm, on);
        }

        private static void PulseRemoteDefrost(SyncedItem item, bool on)
        {
            if (item.GlassDefrostingVar != null)
                item.GlassDefrostingVar.Value = on;

            if (!on)
            {
                if (item.GlassFrostingFsm != null)
                {
                    try { item.GlassFrostingFsm.SendEvent("FINISHED"); }
                    catch { /* FSM not ready yet */ }
                }

                return;
            }

            if (item.GlassFrostingFsm != null)
            {
                try { item.GlassFrostingFsm.SendEvent("DEFROST"); }
                catch { /* FSM not ready yet */ }
            }

            if (item.CarTempDataFsm != null
                && FsmHook.EnsureRemoteEntry(item.CarTempDataFsm, "Defrost"))
            {
                FsmHook.FireRemoteEntry(item.CarTempDataFsm, "Defrost");
            }
        }

        private static float ReadFrost(SyncedItem item)
        {
            float frost = item.FrostVar != null ? item.FrostVar.Value : 0f;
            if (item.CutoffWindshieldVar != null)
                frost = Mathf.Max(frost, item.CutoffWindshieldVar.Value);
            return frost;
        }

        private static float ReadFog(SyncedItem item)
        {
            float fog = 0f;
            if (item.SweatRateVar != null)
                fog = Mathf.Max(fog, item.SweatRateVar.Value);

            if (item.FrostColorVar != null)
            {
                var color = item.FrostColorVar.Value;
                fog = Mathf.Max(fog, Mathf.Max(color.r, Mathf.Max(color.g, color.b)));
            }

            return fog;
        }

        private static float ReadCabinTemp(SyncedItem item)
        {
            if (item.InteriorTempVar != null) return item.InteriorTempVar.Value;
            if (item.GlassTempVar != null) return item.GlassTempVar.Value;
            return 0f;
        }

        private static bool ReadPlayerInCar(SyncedItem item)
        {
            if (item.PlayerInVar != null) return item.PlayerInVar.Value;

            var passenger = PassengerController.Instance;
            if (passenger != null && passenger.IsLocalSeatedInVehicle(item.Id))
                return true;

            return item.Body != null && Instance != null && Instance.IsLocalPlayerDriving(item);
        }

        private static float ReadHeaterTemp(SyncedItem item) =>
            item.HeaterSettingTemp?.Value ?? item.KnobTempSetting?.Value ?? 0f;

        private static float ReadHeaterBlower(SyncedItem item) =>
            item.HeaterSettingBlower?.Value ?? item.KnobBlowerSetting?.Value ?? 0f;

        private static float ReadHeaterDirection(SyncedItem item) =>
            item.HeaterSettingDirection?.Value ?? item.KnobDirectionSetting?.Value ?? 0f;

        private static bool ReadWindowHeaterOn(SyncedItem item) =>
            item.WindowHeaterOnVar != null && item.WindowHeaterOnVar.Value;

        private static bool ReadGlassDefrosting(SyncedItem item)
        {
            if (ReadWindowHeaterOn(item)) return true;
            return item.GlassDefrostingVar != null && item.GlassDefrostingVar.Value;
        }

        private static bool ReadHazardOn(SyncedItem item)
        {
            if (item.BlinkerHazardsVar != null && item.BlinkerHazardsVar.Value)
                return true;

            if (item.HazardButtonFsm != null)
            {
                var hazardsOn = item.HazardButtonFsm.FsmVariables.FindFsmBool("HazardsOn");
                if (hazardsOn != null && hazardsOn.Value) return true;
                var buttonOn = item.HazardButtonFsm.FsmVariables.FindFsmBool("ButtonOn");
                if (buttonOn != null && buttonOn.Value) return true;
            }

            return false;
        }

        private static byte ReadCoolantTempByte(SyncedItem item)
        {
            float temp = ReadCoolantTempC(item);
            return (byte)Mathf.Clamp(Mathf.RoundToInt(temp / CoolantTempMaxC * 255f), 0, 255);
        }

        private static float ReadCoolantTempC(SyncedItem item)
        {
            if (item.GaugeCoolantVar != null)
                return item.GaugeCoolantVar.Value;

            if (item.HeaterUnitFsm != null)
            {
                var coolant = item.HeaterUnitFsm.FsmVariables.FindFsmFloat("CoolantTemp");
                if (coolant != null) return coolant.Value;
            }

            if (item.GaugeCoolantAngleVar != null)
                return TempFromCoolantAngle(item.GaugeCoolantAngleVar.Value);

            return 0f;
        }

        private static float CoolantAngleForTemp(float tempC) =>
            Mathf.Lerp(0f, -220f, Mathf.Clamp01(tempC / CoolantTempMaxC));

        private static float TempFromCoolantAngle(float angle) =>
            Mathf.Clamp01(-angle / -220f) * CoolantTempMaxC;

        private static byte QuantizeFrost(float frost) =>
            (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(frost) * 255f), 0, 255);

        private static float DequantizeFrost(byte wire) => wire / 255f;

        private static byte QuantizeHeater(float value, float max)
        {
            if (max <= 0f) return 0;
            return (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp(value, 0f, max) / max * 255f), 0, 255);
        }

        private static float DequantizeHeater(byte wire, float max) => wire / 255f * max;

        private static void WriteHeaterValue(
            HutongGames.PlayMaker.FsmFloat? primary,
            HutongGames.PlayMaker.FsmFloat? knob,
            float value)
        {
            WriteHeaterKnob(primary, knob, null, value);
        }

        private static void WriteHeaterKnob(
            HutongGames.PlayMaker.FsmFloat? primary,
            HutongGames.PlayMaker.FsmFloat? setting,
            HutongGames.PlayMaker.FsmFloat? angle,
            float value)
        {
            if (primary != null) primary.Value = value;
            if (setting != null) setting.Value = value;
            if (angle != null)
                angle.Value = setting != null ? setting.Value : value;
        }

        private static AudioSource? EnsureRemoteEngineAudio(SyncedItem item)
        {
            if (item.RemoteEngineAudio != null) return item.RemoteEngineAudio;
            if (item.RemoteEngineAudioSearched || item.Body == null) return null;
            item.RemoteEngineAudioSearched = true;

            EnsureVehicleSystemsProbe(item);
            if (item.AudioEngine == null) return null;

            // Template: prefer the "High" loop, else any clip under AudioEngine.
            AudioSource? template = null;
            foreach (var source in item.AudioEngine.GetComponentsInChildren<AudioSource>(true))
            {
                if (source.clip == null) continue;
                if (template == null) template = source;
                if (source.gameObject.name.IndexOf("High", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    template = source;
                    break;
                }
            }

            if (template == null) return null;

            // A fresh GameObject, not Instantiate: cloning the game's audio object
            // would clone its FSMs — i.e. run a second engine simulation.
            var holder = new GameObject("WinterMP_EngineAudio");
            holder.transform.parent = item.AudioEngine.transform.parent;
            holder.transform.position = item.AudioEngine.transform.position;
            var audio = holder.AddComponent<AudioSource>();
            audio.clip = template.clip;
            audio.loop = true;
            audio.playOnAwake = false;
            audio.spatialBlend = template.spatialBlend;
            audio.minDistance = template.minDistance;
            audio.maxDistance = template.maxDistance;
            audio.rolloffMode = template.rolloffMode;
            audio.dopplerLevel = template.dopplerLevel;
            audio.volume = EngineAudioVolume;
            item.RemoteEngineAudio = audio;
            WinterMPPlugin.Log.LogInfo($"WorldSync: remote engine audio for '{item.Path}' (clip '{template.clip.name}').");
            return audio;
        }

        private void FindLocalPlayer()
        {
            if (_localPlayer != null)
            {
                // Unity destroys scene objects without clearing our reference.
                if ((UnityEngine.Object)_localPlayer != null)
                    return;
                _localPlayer = null;
            }

            if (Time.unscaledTime < _nextPlayerSearchAt) return;
            _nextPlayerSearchAt = Time.unscaledTime + PlayerSearchIntervalSeconds;

            var playerObject = GameObject.Find(PlayerObjectName);
            if (playerObject != null)
                _localPlayer = playerObject.transform;
        }

        private void ReleaseEverything()
        {
            // Session over: stop driving remote items, give their physics back.
            foreach (var item in _items.Values)
            {
                if (item.Body != null && item.RemoteOwner != NoOwner)
                    item.Body.isKinematic = item.OriginalKinematic;
                item.RemoteOwner = NoOwner;
                item.RemoteIsDriver = false;
                item.RemoteVehicleStream = false;
                item.LocalDriveActive = false;
                item.LocallyOwned = false;
                item.LastRemoteAt = -999f;
                item.RemoteEngineUntil = -999f;
                item.RemoteClimateUntil = -999f;
                item.RemoteEngineOn = false;
                item.RemoteAccOn = false;
                item.RemoteElectricsApplied = false;
                if (item.RemoteEngineAudio != null && item.RemoteEngineAudio.isPlaying)
                    item.RemoteEngineAudio.Stop();
                SetSeatBlocked(item, false);
            }

            _pending.Clear();
            _pendingItemPoses.Clear();
            _pendingBoltStates.Clear();
            _pendingPartStates.Clear();
            _pendingPurchaseIntents.Clear();
            _pendingDespawnedItems.Clear();
            _nextObjectRequestAt.Clear();
            _snapshotRequested = false;
            _worldSyncDisabled = false;
            SyncEventLog.Clear();
        }

        /// <summary>True after an unhandled sync error; cleared when the session ends.</summary>
        public bool IsDisabled => _worldSyncDisabled;

        // ------------------------------------------------------------------ test tooling

        /// <summary>
        /// -wintermp-autoload: clicks the main menu's Continue button once the
        /// session is up, so the local 2-instance test needs zero manual input.
        /// </summary>
        private void RunAutoLoad(SessionManager? session, bool sessionActive)
        {
            if (_autoLoadStep >= 3 || session == null || !sessionActive) return;
            if (_lastLevel != "MainMenu") return;
            // The host waits for the guest so both load into the world together.
            if (session.IsHost && session.PlayerCount == 0) return;
            if (Time.unscaledTime < _autoLoadNextAt) return;

            var button = GameObject.Find("Interface/Buttons/ButtonContinue");
            if (button == null) return;

            PlayMakerFSM? fsm = null;
            foreach (var component in button.GetComponents<PlayMakerFSM>())
            {
                if (component.FsmName == "SetSize") { fsm = component; break; }
            }
            if (fsm == null) return;

            switch (_autoLoadStep)
            {
                case 0:
                    // Real flow is hover -> click ('Mouse' --OVER--> 'Action' --DOWN--> load).
                    WinterMPPlugin.Log.LogInfo("WorldSync: auto-loading save (Continue).");
                    fsm.SendEvent("OVER");
                    _autoLoadStep = 1;
                    _autoLoadNextAt = Time.unscaledTime + 0.4f;
                    break;
                case 1:
                    fsm.SendEvent("DOWN");
                    _autoLoadStep = 2;
                    _autoLoadNextAt = Time.unscaledTime + 5f;
                    break;
                case 2:
                    // Still in the menu? The OVER/DOWN pair may have raced a UI reset; retry.
                    WinterMPPlugin.Log.LogInfo("WorldSync: auto-load retry.");
                    _autoLoadStep = 0;
                    break;
            }
        }

        /// <summary>
        /// -wintermp-doortest N: opens (then closes) the home WC door N seconds
        /// after door registration — an end-to-end world-sync self-test that needs
        /// no in-game input. Run it on both instances with different delays to
        /// exercise both directions; all evidence lands in the host's log.
        /// </summary>
        private void RunDoorTest()
        {
            if (_doorTestStep >= 2 || _firstDoorRegisteredAt < 0f) return;

            float dueAt = _firstDoorRegisteredAt + _doorTestDelay + (_doorTestStep == 0 ? 0f : 4f);
            if (Time.unscaledTime < dueAt) return;

            // Prefer the home WC door; otherwise the lowest-id door so both
            // machines deterministically pick the same one.
            SyncedDoor? target = null;
            uint targetId = 0;
            foreach (var pair in _doors)
            {
                var door = pair.Value;
                if (door.Fsm == null || Array.IndexOf(door.SyncedStates, "Open door") < 0) continue;
                if (door.Path.IndexOf("HOMENEW") >= 0 && door.Path.IndexOf("DoorWC") >= 0)
                {
                    target = door;
                    break;
                }
                if (target == null || pair.Key < targetId)
                {
                    target = door;
                    targetId = pair.Key;
                }
            }

            if (target == null) return;

            string state = _doorTestStep == 0 ? "Open door" : "Close door";
            WinterMPPlugin.Log.LogInfo($"WorldSync: [door test] firing '{state}' on {target.Path}.");
            // Local entry through the MP transition — the state-enter hook then
            // broadcasts it like any player-triggered door.
            FsmHook.FireRemoteEntry(target.Fsm, state);
            _doorTestStep++;
        }
    }
}
