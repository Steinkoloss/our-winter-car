"""Split WorldSyncManager.FsmSync.cs and ItemSync.cs into smaller partials (<1000 lines each)."""
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SYNC = ROOT / "src" / "WinterMP.Core" / "Sync"

HEADER = """using System;
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
    public sealed partial class WorldSyncManager
    {
"""

FOOTER = """    }
}
"""


def find_line(lines, pattern, start=0):
    for i in range(start, len(lines)):
        if pattern in lines[i]:
            return i
    raise ValueError(f"Pattern not found: {pattern!r} from {start}")


def write_partial(name, parts):
    body = []
    for start, end in parts:
        body.extend(lines[start : end + 1])
    path = SYNC / name
    path.write_text(HEADER + "".join(body) + FOOTER, encoding="utf-8")
    print(f"  {name}: {len(body)} lines")


def split_fsm(lines):
    i_types = find_line(lines, "private sealed class SyncedDoor")
    i_classify = find_line(lines, "private static bool ClassifyBuy")
    i_compute = find_line(lines, "private uint ComputeWorldCrc")
    i_local = find_line(lines, "// ------------------------------------------------------------------ local -> network")
    i_network = find_line(lines, "// ------------------------------------------------------------------ network -> world")
    i_snap = find_line(lines, "private IEnumerable<WorldDoorSnapshot> BuildDoorSnapshotChunks")
    i_door_test = find_line(lines, "private void RunDoorTest")

    print("FsmSync ->")
    write_partial("WorldSyncManager.FsmTypes.cs", [(i_types, i_classify - 1)])
    write_partial("WorldSyncManager.FsmRegistry.cs", [(i_classify, i_compute - 1)])
    write_partial("WorldSyncManager.FsmChecksum.cs", [(i_compute, i_local - 1)])
    write_partial("WorldSyncManager.FsmLocal.cs", [(i_local, i_network - 1)])
    write_partial("WorldSyncManager.FsmRemote.cs", [(i_network, i_snap - 1)])
    write_partial("WorldSyncManager.FsmSnapshots.cs", [(i_snap, i_door_test - 1)])
    write_partial("WorldSyncManager.FsmTests.cs", [(i_door_test, len(lines) - 3)])


def split_item(lines):
    i_item_const = find_line(lines, "private const float ItemSendRateHz")
    i_vehicle_const = find_line(lines, "private const float VehicleStateRateHz")
    i_dup = find_line(lines, "private const float TimeSyncIntervalSeconds")
    i_synced = find_line(lines, "private sealed class SyncedItem")
    i_pending_pose = find_line(lines, "private struct PendingPose")
    i_scan = find_line(lines, "private int ScanItems")
    i_items = find_line(lines, "// ------------------------------------------------------------------ items")
    i_engine = find_line(lines, "// ------------------------------------------------------------------ engine & ignition")
    i_climate = find_line(lines, "private void UpdateVehicleClimate")
    i_find_vehicle = find_line(lines, "private SyncedItem? FindVehicleItemForFsm")

    print("ItemSync ->")
    write_partial(
        "WorldSyncManager.ItemTypes.cs",
        [
            (i_item_const, i_vehicle_const - 1),
            (i_synced, i_scan - 1),
        ],
    )
    write_partial("WorldSyncManager.ItemRegistry.cs", [(i_scan, i_items - 1)])
    write_partial("WorldSyncManager.ItemTransform.cs", [(i_items, i_engine - 1)])
    write_partial(
        "WorldSyncManager.VehicleEngine.cs",
        [
            (i_vehicle_const, i_dup - 1),
            (i_engine, i_climate - 1),
        ],
    )
    write_partial("WorldSyncManager.VehicleClimate.cs", [(i_climate, i_find_vehicle - 1)])
    write_partial("WorldSyncManager.VehicleControls.cs", [(i_find_vehicle, len(lines) - 3)])


def patch_main():
    main_path = SYNC / "WorldSyncManager.cs"
    text = main_path.read_text(encoding="utf-8")
    if "public static WorldSyncManager? Instance" not in text:
        insert = """
        public static WorldSyncManager? Instance { get; private set; }

        public int DoorCount => _doors.Count;
        public int PartCount => _parts.Count;
        public int BuyCount => _buys.Count;
        public int BoltCount => _bolts.Count;
        public int ItemCount => _items.Count;
        public uint IdHash { get; private set; }

        private const float TimeSyncIntervalSeconds = 30f;
        private const float ChecksumIntervalSeconds = 20f;
        private const float ResyncCooldownSeconds = 15f;
        private const float ObjectRequestCooldownSeconds = 5f;
        private const float SnapshotPoseTtlSeconds = 300f;
        private const int DoorSnapshotChunk = 60;
        private const int BoltSnapshotChunk = 80;
        private const int PartSnapshotChunk = 80;
        private const int ItemSnapshotChunk = 40;
        private const int DespawnSnapshotChunk = 80;
"""
        marker = "        private bool _worldSyncDisabled;\n"
        text = text.replace(marker, marker + insert)
    text = text.replace(
        "Implementation is split across partial class files (Fsm*, Item*, Vehicle*) — each under ~1000 lines.",
        "Implementation is split across partial class files (Fsm*, Item*, Vehicle*), each under ~1000 lines.",
    )
    if "Fsm*, Item*, Vehicle*" not in text:
        text = text.replace(
            "Implementation is split across partials: FsmSync (doors, parts, bolts, buys,\n"
            "    /// FSM hooks) and ItemSync (items, vehicles, climate, engine).",
            "Implementation is split across partial class files (Fsm*, Item*, Vehicle*), each under ~1000 lines.",
        )
    main_path.write_text(text, encoding="utf-8")
    print("  WorldSyncManager.cs patched")


def main():
    global lines
    fsm_path = SYNC / "WorldSyncManager.FsmSync.cs"
    item_path = SYNC / "WorldSyncManager.ItemSync.cs"
    lines = fsm_path.read_text(encoding="utf-8").splitlines(keepends=True)
    split_fsm(lines)
    fsm_path.unlink()

    lines = item_path.read_text(encoding="utf-8").splitlines(keepends=True)
    split_item(lines)
    item_path.unlink()

    patch_main()


if __name__ == "__main__":
    main()
