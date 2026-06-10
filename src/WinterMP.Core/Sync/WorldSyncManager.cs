using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

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
    /// Items  — "(itemx)" rigidbodies are claimed by whoever moves them nearby and
    ///          streamed at 10 Hz; receivers freeze their copy kinematic while the
    ///          stream lasts and restore physics at the final resting pose.
    /// Vehicles — heavy root rigidbodies (SORBET, KEKMET, GIFU...) use the same
    ///          ownership stream at 15 Hz with two extras: the machine whose player
    ///          sits *in* the vehicle is the driver and out-claims everyone, and
    ///          remote copies ease toward targets instead of teleporting. While a
    ///          remote driver holds a vehicle its seat trigger is blocked locally
    ///          and the driver's avatar rides along in the cabin. Engine state is
    ///          NOT yet synced (M4): the remote copy just follows poses.
    /// Snapshot — a guest requests the world state once its first scan completes;
    ///          the host answers with every door it has seen change plus the
    ///          current pose of every item/vehicle, then keeps the clock and
    ///          weather forecast aligned via periodic <see cref="TimeSync"/>.
    ///
    /// Known v1 limits: bolt stages are event-replayed only (no variable
    /// reconcile on join — peers should start from the same save), item
    /// consumption/destruction is not yet replicated, and day-of-week is not
    /// synced.
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
        private const float RemoteHoldSeconds = 0.75f;
        /// <summary>A seated driver holds the vehicle with keepalives even when parked,
        /// so ownership can't flap when two players sit in (their copies of) one car.</summary>
        private const float DriverKeepaliveSeconds = 0.4f;
        /// <summary>Owner releases an item (final packet) after this long without motion.</summary>
        private const float ItemStillSeconds = 1.5f;
        private const float VehicleStillSeconds = 3f;
        private const float MoveEpsilonSqr = 1e-6f; // 1 mm — carried items move slowly
        private const float VehicleMoveEpsilonSqr = 2.5e-3f; // 5 cm — ignore idle-engine jitter
        /// <summary>Root rigidbodies at least this heavy are treated as vehicles.</summary>
        private const float VehicleMinMass = 150f;
        /// <summary>Remote pose smoothing (same feel as RemoteAvatar).</summary>
        private const float RemoteLerpSpeed = 12f;
        private const float RemoteSnapDistance = 15f;
        /// <summary>Items inside a remote-driven vehicle are never claimed locally —
        /// the vehicle's owner streams them; claiming them here would create the
        /// kinematic-battering-ram feedback loop that dragged cars around in v1.</summary>
        private const float VehicleInteriorRadius = 3.5f;
        private const float PlayerSearchIntervalSeconds = 2f;
        private const string PlayerObjectName = "PLAYER";

        /// <summary>Host broadcasts the clock/weather this often (drift is slow).</summary>
        private const float TimeSyncIntervalSeconds = 30f;
        /// <summary>Snapshot poses for not-yet-scanned items stay parked this long.</summary>
        private const float SnapshotPoseTtlSeconds = 300f;
        private const int DoorSnapshotChunk = 60;
        private const int ItemSnapshotChunk = 40;

        public static WorldSyncManager? Instance { get; private set; }

        public int DoorCount => _doors.Count;
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

        private sealed class SyncedBolt
        {
            public PlayMakerFSM Fsm = null!;
            public string Path = string.Empty;
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
            public float LastRemoteAt = -999f;
            public ushort LastRemoteSequence;
            public Vector3 TargetPosition;
            public Quaternion TargetRotation = Quaternion.identity;

            public bool LocallyOwned;
            public ushort OutSequence;
            public float NextSendAt;

            public Vector3 LastPosition;
            public float LastMovedAt = -999f;

            // Vehicles only: the game's drive trigger (seat). Blocked while a
            // remote driver holds the vehicle; also anchors the driver's avatar.
            public bool SeatSearched;
            public Transform? SeatTransform;
            public Collider? SeatCollider;
            public bool SeatBlocked;

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
        private readonly Dictionary<uint, SyncedBolt> _bolts = new Dictionary<uint, SyncedBolt>();
        private readonly Dictionary<uint, SyncedItem> _items = new Dictionary<uint, SyncedItem>();
        private readonly Dictionary<PlayMakerFSM, bool> _hookedFsms = new Dictionary<PlayMakerFSM, bool>();
        private readonly Dictionary<Rigidbody, bool> _trackedBodies = new Dictionary<Rigidbody, bool>();
        private readonly List<PendingFsmApply> _pending = new List<PendingFsmApply>();
        private readonly List<uint> _deadItemIds = new List<uint>();
        /// <summary>Snapshot poses waiting for their item to be scanned in.</summary>
        private readonly Dictionary<uint, PendingPose> _pendingItemPoses = new Dictionary<uint, PendingPose>();
        private readonly TimeWeatherSync _timeWeather = new TimeWeatherSync();

        /// <summary>True while a remote event is being replayed, so hooks don't echo it back.</summary>
        private bool _applyingRemote;

        private string _lastLevel = string.Empty;
        private float _nextScanAt;
        private float _nextPendingAt;
        private float _nextTimeSyncAt;
        private bool _snapshotRequested;
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

        public void Configure(LaunchOptions launch)
        {
            _autoLoad = launch.AutoLoadSave;
            _doorTestDelay = launch.DoorTestDelaySeconds;
            _selfTest = launch.DoorTestDelaySeconds > 0f;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
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

            UpdateItems(session);

            if (_selfTest)
                RunDoorTest();
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
            _bolts.Clear();
            _items.Clear();
            _hookedFsms.Clear();
            _trackedBodies.Clear();
            _pending.Clear();
            _pendingItemPoses.Clear();
            _timeWeather.Reset();
            _snapshotRequested = false;
            IdHash = 0;
            _localPlayer = null;
            _nextPlayerSearchAt = 0f;
            _nextScanAt = Time.unscaledTime + FirstScanDelaySeconds;
            _firstDoorRegisteredAt = -1f;
        }

        // ------------------------------------------------------------------ registry

        private void ScanWorld()
        {
            int newDoors = 0, newBolts = 0, newItems = 0;

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
                            string[]? states = ClassifyDoor(fsm);
                            if (states != null && RegisterDoor(fsm, states)) newDoors++;
                        }
                        else if (fsmName == "Screw")
                        {
                            if (FsmHook.HasState(fsm, "Tight?") && FsmHook.HasState(fsm, "Loose?")
                                && RegisterBolt(fsm))
                            {
                                newBolts++;
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

            if (newDoors > 0 || newBolts > 0 || newItems > 0)
            {
                RecomputeIdHash();
                WinterMPPlugin.Log.LogInfo(
                    $"WorldSync: +{newDoors} doors, +{newBolts} bolts, +{newItems} items — " +
                    $"now {_doors.Count}/{_bolts.Count}/{_items.Count} (id hash {IdHash:X8}).");
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

        /// <summary>House/vehicle door handles vs. the two garage doors (different state sets).</summary>
        private static string[]? ClassifyDoor(PlayMakerFSM fsm)
        {
            if (FsmHook.HasState(fsm, "Open door") && FsmHook.HasState(fsm, "Close door"))
                return new[] { "Open door", "Close door" };
            if (FsmHook.HasState(fsm, "Open") && FsmHook.HasState(fsm, "Close") && FsmHook.HasState(fsm, "Set rotation"))
                return new[] { "Open", "Close" };
            return null;
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

        private bool RegisterBolt(PlayMakerFSM fsm)
        {
            string path = ScenePath.Of(fsm.transform);
            uint id = StableHash.Fnv1a32(path + "::" + fsm.FsmName);
            if (_bolts.ContainsKey(id) || _doors.ContainsKey(id))
            {
                WinterMPPlugin.Log.LogWarning($"WorldSync: bolt id collision, not syncing '{path}'.");
                _hookedFsms[fsm] = true;
                return false;
            }

            // "Tight?"/"Loose?" are entered exactly when the wrench turns the bolt;
            // their own BACK check rejects over/under-tightening on each machine.
            if (!FsmHook.OnStateEnter(fsm, "Tight?", () => OnBoltTurned(id, "TIGHTEN"))) return false;
            if (!FsmHook.OnStateEnter(fsm, "Loose?", () => OnBoltTurned(id, "UNTIGHTEN"))) return false;

            _bolts[id] = new SyncedBolt { Fsm = fsm, Path = path };
            _hookedFsms[fsm] = true;
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

                    bool isItem = body.name.IndexOf("(itemx)") >= 0;
                    bool isVehicle = !isItem && IsVehicleRoot(body);
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

        /// <summary>
        /// Vehicles = heavy root-level rigidbodies (SORBET 955 kg, KEKMET, BACHGLOTZ,
        /// FLATBED trailer...) plus the light two-wheelers/trucks caught by name.
        /// </summary>
        private static bool IsVehicleRoot(Rigidbody body)
        {
            if (body.transform.parent != null) return false;
            if (body.mass >= VehicleMinMass) return true;

            string name = body.name;
            return name.StartsWith("JONNEZ", StringComparison.Ordinal)
                || name.StartsWith("GIFU", StringComparison.Ordinal)
                || name.StartsWith("JOKKIS", StringComparison.Ordinal)
                || name.StartsWith("CORRIS", StringComparison.Ordinal); // project car shell is only 107 kg
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
            var ids = new List<uint>(_doors.Count + _bolts.Count + _items.Count);
            foreach (uint id in _doors.Keys) ids.Add(id);
            foreach (uint id in _bolts.Keys) ids.Add(id);
            foreach (uint id in _items.Keys) ids.Add(id);
            ids.Sort();

            uint hash = StableHash.OffsetBasis;
            foreach (uint id in ids)
                hash = StableHash.Combine(hash, id);
            IdHash = hash;
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

        private void OnBoltTurned(uint netId, string eventName)
        {
            if (_applyingRemote) return;
            var session = SessionManager.Instance;
            if (session == null || session.PlayerCount == 0) return;

            WinterMPPlugin.Log.LogInfo($"WorldSync: bolt {netId:X8} {eventName} (local).");
            session.SendWorldMessage(new FsmRawEvent { NetId = netId, EventName = eventName }, Channel.ReliableOrdered);
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
            if (!_doors.TryGetValue(netId, out var door) || door.Fsm == null) return false;
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
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var entry = _pending[i];
                bool applied = entry.IsRawEvent
                    ? TryApplyRawEvent(entry.NetId, entry.Name)
                    : TryApplyStateEnter(entry.NetId, entry.Name);

                if (applied || Time.unscaledTime >= entry.ExpiresAt)
                {
                    if (!applied)
                        WinterMPPlugin.Log.LogWarning($"WorldSync: dropping expired event {entry.Name} for {entry.NetId:X8}.");
                    _pending.RemoveAt(i);
                }
            }
        }

        // ------------------------------------------------------------------ join snapshot & time

        /// <summary>
        /// Host side: world state for a fresh joiner — every door we saw change
        /// plus the current pose of every item/vehicle, in send-ready chunks.
        /// </summary>
        public IEnumerable<IMessage> BuildWorldSnapshot()
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

            if (doors.Entries.Count > 0)
                yield return doors;

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

        /// <summary>Host side: current clock/weather, or null while still in the menu.</summary>
        public TimeSync? BuildTimeSync()
        {
            return _timeWeather.BuildMessage();
        }

        public void OnRemoteTimeSync(TimeSync message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            _timeWeather.Apply(message);
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
                    if (item.LocallyOwned || Time.unscaledTime - item.LastRemoteAt < RemoteHoldSeconds)
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
                ushort diff = (ushort)(message.Sequence - item.LastRemoteSequence);
                if (diff == 0 || diff > short.MaxValue) return;
            }

            var session = SessionManager.Instance;
            if (item.LocallyOwned && session != null)
            {
                // Conflicting claims: a driver beats a non-driver; among equal
                // priorities the lowest player id wins (host is 0, always wins).
                bool localIsDriver = item.IsVehicle && IsLocalPlayerDriving(item.Body);
                bool remoteWins = message.IsDriver != localIsDriver
                    ? message.IsDriver
                    : message.OwnerPlayerId < session.LocalPlayerId;
                if (!remoteWins) return;
                item.LocallyOwned = false;
            }

            bool firstPacket = item.RemoteOwner != message.OwnerPlayerId;
            item.RemoteOwner = message.OwnerPlayerId;
            item.RemoteIsDriver = message.IsDriver;
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
                    WinterMPPlugin.Log.LogInfo($"WorldSync: '{item.Path}' now {(message.IsDriver ? "driven" : "moved")} by player {message.OwnerPlayerId}.");
                item.TargetPosition = position;
                item.TargetRotation = rotation;
                item.LastRemoteAt = Time.unscaledTime;

                // An occupied driver's seat must not be enterable locally.
                if (item.IsVehicle)
                    SetSeatBlocked(item, message.IsDriver);
            }
        }

        private readonly List<Vector3> _remoteVehiclePositions = new List<Vector3>();

        private void UpdateItems(SessionManager session)
        {
            if (session.PlayerCount == 0) return;

            FindLocalPlayer();
            float now = Time.unscaledTime;
            _deadItemIds.Clear();

            // Remote-driven vehicles first: items inside them must not be claimed
            // locally (their owner streams them), so collect cabin positions.
            _remoteVehiclePositions.Clear();
            foreach (var item in _items.Values)
            {
                if (item.IsVehicle && item.Body != null && now - item.LastRemoteAt < RemoteHoldSeconds)
                    _remoteVehiclePositions.Add(item.Body.transform.position);
            }

            foreach (var pair in _items)
            {
                var item = pair.Value;
                var body = item.Body;
                if (body == null)
                {
                    _deadItemIds.Add(pair.Key);
                    continue;
                }

                bool seatedDriver = item.IsVehicle && IsLocalPlayerDriving(body);
                bool remoteDriven = now - item.LastRemoteAt < RemoteHoldSeconds;

                // Entering the driver's seat out-claims proximity owners, pushers,
                // and the previous driver (protocol: driver beats non-driver; among
                // drivers the lowest player id wins).
                if (seatedDriver && !item.LocallyOwned
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

                if (item.RemoteOwner != NoOwner)
                {
                    // Stream died without a final packet — give physics back.
                    body.isKinematic = item.OriginalKinematic;
                    item.RemoteOwner = NoOwner;
                    item.RemoteIsDriver = false;
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
                    if (seatedDriver)
                    {
                        // Drivers hold their vehicle until they leave the seat. A
                        // stillness release here would let the other machine's copy
                        // wake up, claim, and freeze us — the mutual-driver deadlock.
                        if (now >= item.NextSendAt)
                        {
                            SendItem(session, item, body, false);
                            item.NextSendAt = now + (moving ? 1f / item.SendRateHz : DriverKeepaliveSeconds);
                        }
                    }
                    else if (!moving)
                    {
                        SendItem(session, item, body, true);
                        item.LocallyOwned = false;
                    }
                    else if (now >= item.NextSendAt)
                    {
                        SendItem(session, item, body, false);
                        item.NextSendAt = now + 1f / item.SendRateHz;
                    }
                }
                else if (moving && CanClaim(item, position))
                {
                    ClaimItem(session, item, body, now);
                }
            }

            foreach (uint id in _deadItemIds)
                _items.Remove(id);
        }

        private bool CanClaim(SyncedItem item, Vector3 position)
        {
            if (_localPlayer == null) return false;

            if (item.IsVehicle && IsLocalPlayerDriving(item.Body)) return true;
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
                item.LastRemoteAt = -999f;
                SetSeatBlocked(item, false);
            }

            item.LocallyOwned = true;
            item.LastMovedAt = now;
            SendItem(session, item, body, false);
            item.NextSendAt = now + 1f / item.SendRateHz;

            if (item.IsVehicle)
                WinterMPPlugin.Log.LogInfo($"WorldSync: claimed vehicle '{item.Path}' " +
                    $"({(IsLocalPlayerDriving(body) ? "driving" : "pushing")}).");
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

        private bool IsLocalPlayerDriving(Rigidbody vehicleBody)
        {
            // A seated passenger is parented under the car exactly like a driver,
            // but passengers never own the vehicle.
            if (PassengerController.Instance != null && PassengerController.Instance.IsLocalSeated)
                return false;

            // Entering a vehicle parents PLAYER under it; that machine is the driver.
            return _localPlayer != null && vehicleBody != null
                && _localPlayer.IsChildOf(vehicleBody.transform);
        }

        /// <summary>
        /// Whether a locally seated driver should take this vehicle away from a
        /// remote stream. Drivers beat pushers/proximity owners; among two drivers
        /// the lowest player id wins (see <see cref="OnRemoteItemTransform"/>).
        /// </summary>
        private static bool LocalDriverOutClaimsRemote(SessionManager session, SyncedItem item, bool remoteDriven)
        {
            if (!remoteDriven) return true;
            if (!item.RemoteIsDriver) return true;
            return session.LocalPlayerId < item.RemoteOwner;
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
                if (!transform.name.StartsWith("DriveTrigger", StringComparison.Ordinal)) continue;
                item.SeatTransform = transform;
                item.SeatCollider = transform.GetComponent<Collider>();
                return;
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
                if (!item.IsVehicle || !item.RemoteIsDriver || item.RemoteOwner != playerId) continue;
                if (item.Body == null || now - item.LastRemoteAt >= RemoteHoldSeconds) continue;

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
            byte flags = 0;
            if (final) flags |= ItemTransform.FlagFinal;
            if (item.IsVehicle && IsLocalPlayerDriving(body)) flags |= ItemTransform.FlagDriver;

            var message = new ItemTransform
            {
                ItemId = item.Id,
                OwnerPlayerId = session.LocalPlayerId,
                Sequence = ++item.OutSequence,
                Flags = flags,
                Position = body.transform.position.ToNet(),
                Rotation = body.transform.rotation.ToNet(),
            };

            // The resting pose must arrive even if every streamed packet was lost.
            session.SendWorldMessage(message, final ? Channel.ReliableOrdered : Channel.UnreliableSequenced);
        }

        private void FindLocalPlayer()
        {
            if (_localPlayer != null || Time.unscaledTime < _nextPlayerSearchAt) return;
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
                item.LocallyOwned = false;
                item.LastRemoteAt = -999f;
                SetSeatBlocked(item, false);
            }

            _pending.Clear();
            _pendingItemPoses.Clear();
            _snapshotRequested = false;
        }

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
