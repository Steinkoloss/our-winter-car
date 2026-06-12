"""Repair subsystem extraction: types file, fields, and missing APIs."""
from pathlib import Path

SYNC = Path(__file__).resolve().parent.parent / "src" / "WinterMP.Core" / "Sync"

FSM_FIELDS = """
        private readonly Dictionary<uint, SyncedDoor> _doors = new Dictionary<uint, SyncedDoor>();
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
"""

FSM_API = """
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
            void ConsiderFsm(uint id, PlayMakerFSM? fsm)
            {
                if (fsm == null) return;
                float sq = (origin - fsm.transform.position).sqrMagnitude;
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

            return bestId != 0 ? 1 : 0;
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
"""

ITEM_FIELDS = """
        private readonly Dictionary<uint, SyncedItem> _items = new Dictionary<uint, SyncedItem>();
        private readonly Dictionary<Rigidbody, bool> _trackedBodies = new Dictionary<Rigidbody, bool>();
        private readonly Dictionary<uint, PendingPose> _pendingItemPoses = new Dictionary<uint, PendingPose>();
        private readonly HashSet<uint> _sessionDespawnedItems = new HashSet<uint>();
        private readonly HashSet<uint> _pendingDespawnedItems = new HashSet<uint>();

        internal IEnumerable<uint> SessionDespawnedIds => _sessionDespawnedItems;
"""

VEHICLE_API = """
        internal void LateUpdateClimate(float now)
        {
            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || item.LocallyOwned || item.Body == null) continue;
                if (now >= item.RemoteClimateUntil) continue;
                UpdateRemoteClimatePresentation(item, now);
            }
        }

        internal IEnumerable<IMessage> BuildVehicleResyncMessages()
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) yield break;

            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle) continue;
                foreach (var message in BuildVehicleStateMessages(item, session.LocalPlayerId))
                    yield return message;
            }
        }

        internal IEnumerable<IMessage> BuildVehicleStateMessages(SyncedItem item, byte ownerPlayerId)
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

        internal IEnumerable<VehicleClimate> BuildJoinClimateSnapshots()
        {
            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle) continue;
                var climate = TryBuildVehicleClimate(item);
                if (climate != null)
                    yield return climate;
            }
        }

        internal bool TryReadVehicleChecksum(SyncedItem item, out byte flags, out ushort rpm, out byte fuel,
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

            if (item.LocallyOwned || item.RemoteOwner == WorldSyncIds.NoOwner)
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
"""


def fix_world_sync_types():
    text = (SYNC / "WorldSyncTypes.cs").read_text(encoding="utf-8")
    text = text.replace("public byte RemoteOwner = NoOwner;", "public byte RemoteOwner = WorldSyncIds.NoOwner;")
    # Remove premature namespace close before FSM types
    text = text.replace(
        "    public struct VehicleInfo\n    {\n        public uint Id;\n        public Rigidbody Body;\n    }\n}\n\n",
        "    public struct VehicleInfo\n    {\n        public uint Id;\n        public Rigidbody Body;\n    }\n\n",
    )
    if text.count("namespace WinterMP.Core.Sync") > 1:
        text = text.replace("\n}\n\n    internal sealed class SyncedDoor", "\n    internal sealed class SyncedDoor")
    if not text.rstrip().endswith("}"):
        text = text.rstrip() + "\n}\n"
    # Drop duplicate closing brace at end
    while text.rstrip().endswith("}\n}\n"):
        text = text.rstrip()[:-2] + "\n}\n"
    (SYNC / "WorldSyncTypes.cs").write_text(text, encoding="utf-8")


def insert_after( content, marker, block):
    if block.strip() in content:
        return content
    idx = content.index(marker)
    end = idx + len(marker)
    return content[:end] + block + content[end:]


def remove_method_block(content, signature_fragment):
    idx = content.find(signature_fragment)
    if idx < 0:
        return content
    # walk back to method start
    line_start = content.rfind("\n", 0, idx) + 1
    brace = content.index("{", idx)
    depth = 0
    i = brace
    while i < len(content):
        if content[i] == "{":
            depth += 1
        elif content[i] == "}":
            depth -= 1
            if depth == 0:
                end = i + 1
                while end < len(content) and content[end] in "\r\n":
                    end += 1
                return content[:line_start] + content[end:]
        i += 1
    return content


def main():
    fix_world_sync_types()

    fsm = (SYNC / "FsmWorldSync.cs").read_text(encoding="utf-8")
    fsm = insert_after(fsm, "        private readonly VehicleWorldSync _vehicles;\n", FSM_FIELDS)
    fsm = fsm.replace(
        "        public int BoltCount => _bolts.Count;\n",
        "        public int BoltCount => _bolts.Count;\n" + FSM_API,
    )
    (SYNC / "FsmWorldSync.cs").write_text(fsm, encoding="utf-8")

    item = (SYNC / "ItemWorldSync.cs").read_text(encoding="utf-8")
    item = insert_after(item, "        public void BindVehicles(VehicleWorldSync vehicles) => _vehicles = vehicles;\n\n", ITEM_FIELDS)
    item = item.replace("const byte NoOwner = 255", "const byte NoOwner = WorldSyncIds.NoOwner")
    item = item.replace("item.LocallyOwned || item.RemoteOwner == NoOwner", "item.LocallyOwned || item.RemoteOwner == WorldSyncIds.NoOwner")
    item = remove_method_block(item, "private bool TryReadVehicleChecksum(SyncedItem item")
    item = remove_method_block(item, "private VehicleState? TryBuildVehicleStateMessage(SyncedItem item")
    # Delegate checksum to vehicles
    item = item.replace(
        "if (!_items.TryGetValue(id, out var item) || !TryReadVehicleChecksum(item, out byte flags,",
        "if (!_items.TryGetValue(id, out var item) || !_vehicles.TryReadVehicleChecksum(item, out byte flags,",
    )
    (SYNC / "ItemWorldSync.cs").write_text(item, encoding="utf-8")

    veh = (SYNC / "VehicleWorldSync.cs").read_text(encoding="utf-8")
    veh = veh.replace("_items.Values", "_items.Items.Values")
    veh = veh.replace("_items.TryGetValue", "_items.Items.TryGetValue")
    veh = insert_after(veh, "            return audio;\n        }\n\n\n", VEHICLE_API)
    (SYNC / "VehicleWorldSync.cs").write_text(veh, encoding="utf-8")

    wsm = (SYNC / "WorldSyncManager.cs").read_text(encoding="utf-8")
    wsm = wsm.replace(
        "        // FsmWorldSync does not expose ignition/control/starter counts on the facade yet.\n        private int _ignitionsPlaceholder() => 0;\n        private int _controlsPlaceholder() => 0;\n        private int _startersPlaceholder() => 0;\n",
        "",
    )
    wsm = wsm.replace("_ignitionsPlaceholder()", "_fsm.IgnitionCount")
    wsm = wsm.replace("_controlsPlaceholder()", "_fsm.ControlCount")
    wsm = wsm.replace("_startersPlaceholder()", "_fsm.StarterCount")
    wsm = wsm.replace(
        "        public void CollectVehicles(List<ItemWorldSync.VehicleInfo> results) => _items.CollectVehicles(results);\n",
        "        public struct VehicleInfo\n        {\n            public uint Id;\n            public Rigidbody Body;\n        }\n\n        public void CollectVehicles(List<VehicleInfo> results) => _items.CollectVehicles(results);\n",
    )
    (SYNC / "WorldSyncManager.cs").write_text(wsm, encoding="utf-8")

    pc = (SYNC / "PassengerController.cs").read_text(encoding="utf-8")
    pc = pc.replace("List<WorldSyncManager.VehicleInfo>", "List<WorldSyncManager.VehicleInfo>")
    # PassengerController already uses WorldSyncManager.VehicleInfo - good after we add nested struct

    print("Subsystem repair applied")


if __name__ == "__main__":
    main()
