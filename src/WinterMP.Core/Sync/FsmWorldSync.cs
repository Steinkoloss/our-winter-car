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
        private const int DoorSnapshotChunk = WorldDoorSnapshot.MaxEntries;
        private const int BoltSnapshotChunk = WorldBoltSnapshot.MaxEntries;
        private const int PartSnapshotChunk = WorldPartSnapshot.MaxEntries;
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
        private readonly Dictionary<uint, WinterMP.Net.Messages.PartState> _hostPartReports = new Dictionary<uint, WinterMP.Net.Messages.PartState>();
        private readonly Dictionary<uint, PendingRadiatorThermostatState> _pendingRadiatorThermostatStates = new Dictionary<uint, PendingRadiatorThermostatState>();
        private readonly List<PendingPurchaseIntent> _pendingPurchaseIntents = new List<PendingPurchaseIntent>();
        private readonly Dictionary<byte, ushort> _lastGuestPurchaseSequences = new Dictionary<byte, ushort>();
        private ushort _outPurchaseSequence;

        /// <summary>Host: a player (re)joined — its purchase counter restarted; drop the stale latch.</summary>
        public void ForgetPlayer(byte playerId) => _lastGuestPurchaseSequences.Remove(playerId);


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

        internal WinterMP.Net.Messages.PartState? BuildPartState(uint netId)
        {
            return _parts.TryGetValue(netId, out var part) ? ReadPartState(netId, part) : null;
        }

        public void Clear()
        {
            ClearFsmHooks();
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
            _hostBoltReports.Clear();
            _partTightnessReceipts.Clear(); _partReceiptOrder = 0;
            _pendingPartStates.Clear();
            _hostPartReports.Clear();
            _pendingRadiatorThermostatStates.Clear();
            _pendingPurchaseIntents.Clear();
            _lastGuestPurchaseSequences.Clear();
        }

        public FsmWorldSync(WorldSyncBridge bridge, VehicleWorldSync vehicles)
        {
            _bridge = bridge;
            _vehicles = vehicles;
        }


    }
}
