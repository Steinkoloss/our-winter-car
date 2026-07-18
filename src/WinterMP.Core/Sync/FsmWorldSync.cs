using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FsmWorldSync
    {
        private const float PendingTtlSeconds = 120f;
        private const float SnapshotPoseTtlSeconds = 300f;
        private const int DoorSnapshotChunk = 60;
        private const int BoltSnapshotChunk = 80;
        private const int PartSnapshotChunk = 80;
        private const int ItemSnapshotChunk = 40;
        private static readonly string[] AllowedRawEvents = { "TIGHTEN", "UNTIGHTEN" };

        private readonly WorldSyncBridge _bridge;
        private readonly VehicleWorldSync _vehicles;

        private readonly Dictionary<uint, SyncedDoor> _doors = new Dictionary<uint, SyncedDoor>();
        // Deliberately absent from CollectIds/checksum: bags are per-peer runtime
        // clones with salted ids, so cross-peer id agreement is impossible by design.
        private readonly Dictionary<uint, SyncedSpawnContainer> _spawnContainers = new Dictionary<uint, SyncedSpawnContainer>();
        private readonly Dictionary<uint, SyncedPart> _parts = new Dictionary<uint, SyncedPart>();
        private readonly Dictionary<uint, SyncedBuy> _buys = new Dictionary<uint, SyncedBuy>();
        private readonly Dictionary<uint, SyncedBolt> _bolts = new Dictionary<uint, SyncedBolt>();
        private readonly Dictionary<uint, SyncedIgnition> _ignitions = new Dictionary<uint, SyncedIgnition>();
        private readonly Dictionary<uint, SyncedControl> _controls = new Dictionary<uint, SyncedControl>();
        private readonly Dictionary<uint, SyncedStarter> _starters = new Dictionary<uint, SyncedStarter>();
        private readonly List<PendingFsmApply> _pending = new List<PendingFsmApply>();
        private readonly Dictionary<uint, PendingBoltState> _pendingBoltStates = new Dictionary<uint, PendingBoltState>();
        private readonly Dictionary<uint, PendingPartState> _pendingPartStates = new Dictionary<uint, PendingPartState>();
        private readonly List<PendingPurchaseIntent> _pendingPurchaseIntents = new List<PendingPurchaseIntent>();
        private ushort _outPurchaseSequence;


        public int DoorCount => _doors.Count;
        public int SpawnContainerCount => _spawnContainers.Count;
        public int PartCount => _parts.Count;
        public int BuyCount => _buys.Count;
        public int BoltCount => _bolts.Count;

        public int IgnitionCount => _ignitions.Count;
        public int ControlCount => _controls.Count;
        public int StarterCount => _starters.Count;

        internal void CollectIds(List<uint> ids)
        {
            foreach (uint id in _doors.Keys) ids.Add(id);
            foreach (uint id in _parts.Keys) ids.Add(id);
            foreach (uint id in _buys.Keys) ids.Add(id);
            foreach (uint id in _bolts.Keys) ids.Add(id);
            foreach (uint id in _ignitions.Keys) ids.Add(id);
            foreach (uint id in _controls.Keys) ids.Add(id);
            foreach (uint id in _starters.Keys) ids.Add(id);
        }

        internal int FindNearestId(Vector3 origin, float maxSq, ref uint bestId, ref float bestSq)
        {
            foreach (var pair in _doors)
                ConsiderNearestFsm(origin, ref bestId, ref bestSq, pair.Key, pair.Value.Fsm);
            foreach (var pair in _controls)
                ConsiderNearestFsm(origin, ref bestId, ref bestSq, pair.Key, pair.Value.Fsm);
            foreach (var pair in _parts)
                ConsiderNearestFsm(origin, ref bestId, ref bestSq, pair.Key, pair.Value.Fsm);
            foreach (var pair in _bolts)
                ConsiderNearestFsm(origin, ref bestId, ref bestSq, pair.Key, pair.Value.Fsm);

            return bestId != 0 ? 1 : 0;
        }

        private static void ConsiderNearestFsm(Vector3 origin, ref uint bestId, ref float bestSq, uint id, PlayMakerFSM? fsm)
        {
            if (fsm == null) return;
            float sq = (origin - fsm.transform.position).sqrMagnitude;
            if (sq < bestSq)
            {
                bestSq = sq;
                bestId = id;
            }
        }

        internal bool TryGetPartSnapshot(uint netId, out byte flags, out byte tightness, out byte wear)
        {
            if (_parts.TryGetValue(netId, out var part) && ShouldIncludePartSnapshot(part, out flags, out tightness, out wear))
                return true;
            flags = tightness = wear = 0;
            return false;
        }

        internal bool TryGetBoltSnapshot(uint netId, out ushort boltTightness, out ushort screwInt)
        {
            if (_bolts.TryGetValue(netId, out var bolt))
            {
                ReadBoltVars(bolt, out boltTightness, out screwInt);
                if (boltTightness != 0 || screwInt != 0)
                    return true;
            }
            boltTightness = screwInt = 0;
            return false;
        }

        public void Clear()
        {
            _doors.Clear();
            _spawnContainers.Clear();
            _parts.Clear();
            _buys.Clear();
            _bolts.Clear();
            _ignitions.Clear();
            _controls.Clear();
            _starters.Clear();
            _pending.Clear();
            _pendingBoltStates.Clear();
            _pendingPartStates.Clear();
            _pendingPurchaseIntents.Clear();
        }

        public FsmWorldSync(WorldSyncBridge bridge, VehicleWorldSync vehicles)
        {
            _bridge = bridge;
            _vehicles = vehicles;
        }


    }
}
