"""Promote coordinator-facing members to public on subsystem classes."""
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SYNC = ROOT / "src" / "WinterMP.Core" / "Sync"

FSM_PUBLIC = [
    "ClassifyBuy",
    "RegisterDoor", "RegisterPart", "RegisterBuy", "RegisterBolt",
    "RegisterIgnition", "RegisterControl", "RegisterStarter",
    "ComputeWorldCrc", "TryGetFsmSnapshotState", "ShouldIncludePartSnapshot", "ReadBoltVars",
    "ProcessPending", "BuildDoorSnapshotChunks", "BuildBoltSnapshotChunks", "BuildPartSnapshotChunks",
    "RunDoorTest", "MixFsmStates",
]

ITEM_PUBLIC = [
    "ScanItems", "UpdateItems", "ComputeItemCrc", "ComputeVehicleCrc", "BuildItemSnapshotChunks",
    "IsLocalPlayerDriving", "ReleaseSession", "Clear",
]

VEHICLE_PUBLIC = [
    "UpdateVehicleStates", "UpdateVehicleClimate", "EnsureVehicleSystemsProbe",
    "UpdateRemoteEngineAudio", "PrepareRemoteControl", "FinishRemoteControl",
    "TryBuildVehicleStateMessage", "TryBuildVehicleClimate", "BuildVehicleStateMessages",
    "LateUpdateClimate", "BuildVehicleResyncMessages",
]


def promote(path: Path, names):
    text = path.read_text(encoding="utf-8")
    for name in names:
        text = text.replace(f"private static bool {name}", f"public static bool {name}")
        text = text.replace(f"private static void {name}", f"public static void {name}")
        text = text.replace(f"private static byte {name}", f"public static byte {name}")
        text = text.replace(f"private static float {name}", f"public static float {name}")
        text = text.replace(f"private static string? {name}", f"public static string? {name}")
        text = text.replace(f"private static void ReadBoltVars", f"public static void ReadBoltVars")
        text = text.replace(f"private bool {name}", f"public bool {name}")
        text = text.replace(f"private void {name}", f"public void {name}")
        text = text.replace(f"private int {name}", f"public int {name}")
        text = text.replace(f"private uint {name}", f"public uint {name}")
        text = text.replace(f"private IEnumerable<{name}", f"public IEnumerable<{name}")
        text = text.replace(f"private VehicleState? {name}", f"public VehicleState? {name}")
        text = text.replace(f"private VehicleClimate? {name}", f"public VehicleClimate? {name}")
    path.write_text(text, encoding="utf-8")


def add_fsm_counts():
    path = SYNC / "FsmWorldSync.cs"
    text = path.read_text(encoding="utf-8")
    marker = "        public FsmWorldSync(WorldSyncBridge bridge, VehicleWorldSync vehicles)"
    insert = """
        public int DoorCount => _doors.Count;
        public int PartCount => _parts.Count;
        public int BuyCount => _buys.Count;
        public int BoltCount => _bolts.Count;

        public void Clear()
        {
            _doors.Clear();
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
"""
    if "public int DoorCount" not in text:
        text = text.replace(marker, insert + "\n" + marker)
    path.write_text(text, encoding="utf-8")


def add_item_counts():
    path = SYNC / "ItemWorldSync.cs"
    text = path.read_text(encoding="utf-8")
    marker = "        public IReadOnlyDictionary<uint, SyncedItem> Items => _items;"
    insert = """
        public int ItemCount => _items.Count;

        public void Clear()
        {
            _items.Clear();
            _trackedBodies.Clear();
            _pendingItemPoses.Clear();
            _sessionDespawnedItems.Clear();
            _pendingDespawnedItems.Clear();
        }

        public void ReleaseSession()
        {
            const byte NoOwner = 255;
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

            _pendingItemPoses.Clear();
            _pendingDespawnedItems.Clear();
        }
"""
    if "public int ItemCount" not in text:
        text = text.replace(marker, marker + insert)
    path.write_text(text, encoding="utf-8")


def main():
    promote(SYNC / "FsmWorldSync.cs", FSM_PUBLIC)
    promote(SYNC / "ItemWorldSync.cs", ITEM_PUBLIC)
    promote(SYNC / "VehicleWorldSync.cs", VEHICLE_PUBLIC)
    add_fsm_counts()
    add_item_counts()
    print("API promoted")


if __name__ == "__main__":
    main()
