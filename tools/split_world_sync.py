"""Split WorldSyncManager.cs into partial class files."""
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src" / "WinterMP.Core" / "Sync" / "WorldSyncManager.cs"
SYNC_DIR = SRC.parent

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


def slice_lines(lines, ranges):
    out = []
    for start, end in ranges:
        out.extend(lines[start : end + 1])
    return out


def main():
    text = SRC.read_text(encoding="utf-8")
    lines = text.splitlines(keepends=True)

    i_item_consts = find_line(lines, "private const float ItemSendRateHz")
    i_fsm_door = find_line(lines, "private sealed class SyncedDoor")
    i_synced_item = find_line(lines, "private sealed class SyncedItem")
    i_pending_fsm = find_line(lines, "private struct PendingFsmApply")
    i_pending_pose = find_line(lines, "private struct PendingPose")
    i_no_owner = find_line(lines, "private const byte NoOwner")
    i_fields = find_line(lines, "private readonly Dictionary<uint, SyncedDoor>")
    i_classify_buy = find_line(lines, "private static bool ClassifyBuy")
    i_scan_items = find_line(lines, "private int ScanItems")
    i_recompute = find_line(lines, "private void RecomputeIdHash")
    i_compute_world = find_line(lines, "private uint ComputeWorldCrc")
    i_compute_item = find_line(lines, "private uint ComputeItemCrc")
    i_mix_fsm = find_line(lines, "private static void MixFsmStates")
    i_build_checksum = find_line(lines, "public WorldStateChecksum? BuildStateChecksum")
    i_try_fsm_snap = find_line(lines, "private string? TryGetFsmSnapshotState")
    i_try_build_vehicle = find_line(lines, "private VehicleState? TryBuildVehicleStateMessage")
    i_local_net = find_line(lines, "// ------------------------------------------------------------------ local -> network")
    i_join_snap = find_line(lines, "// ------------------------------------------------------------------ join snapshot & time")
    i_build_door_chunks = find_line(lines, "private IEnumerable<WorldDoorSnapshot> BuildDoorSnapshotChunks")
    i_build_item_chunks = find_line(lines, "private IEnumerable<WorldItemSnapshot> BuildItemSnapshotChunks")
    i_build_world = find_line(lines, "public IEnumerable<IMessage> BuildWorldSnapshot")
    i_remote_door_snap = find_line(lines, "public void OnRemoteDoorSnapshot")
    i_remote_item_snap = find_line(lines, "public void OnRemoteItemSnapshot")
    i_remote_bolt_snap = find_line(lines, "public void OnRemoteBoltSnapshot")
    i_apply_snap_pose = find_line(lines, "private static void ApplySnapshotPose")
    i_items = find_line(lines, "// ------------------------------------------------------------------ items")
    i_find_player = find_line(lines, "private void FindLocalPlayer")
    i_run_door_test = find_line(lines, "private void RunDoorTest")

    for i, line in enumerate(lines):
        if "public sealed class WorldSyncManager" in line:
            lines[i] = line.replace(
                "public sealed class WorldSyncManager",
                "public sealed partial class WorldSyncManager",
            )
            break

    fsm_ranges = [
        (i_fsm_door, i_synced_item - 1),
        (i_pending_fsm, i_pending_pose - 1),
        (i_classify_buy, i_scan_items - 1),
        (i_compute_world, i_compute_item - 1),
        (i_mix_fsm, i_build_checksum - 1),
        (i_try_fsm_snap, i_try_build_vehicle - 1),
        (i_local_net, i_join_snap - 1),
        (i_build_door_chunks, i_build_item_chunks - 1),
        (i_remote_door_snap, i_remote_item_snap - 1),
        (i_remote_bolt_snap, i_apply_snap_pose - 1),
        (i_run_door_test, find_line(lines, "_doorTestStep++;", i_run_door_test)),
    ]

    item_ranges = [
        (i_item_consts, i_fsm_door - 1),
        (i_synced_item, i_pending_fsm - 1),
        (i_pending_pose, i_no_owner - 1),
        (i_scan_items, i_recompute - 1),
        (i_compute_item, i_mix_fsm - 1),
        (i_try_build_vehicle, i_local_net - 1),
        (i_build_item_chunks, i_build_world - 1),
        (i_remote_item_snap, i_remote_bolt_snap - 1),
        (i_apply_snap_pose, i_find_player - 1),
    ]

    fsm_set = set()
    for start, end in fsm_ranges:
        fsm_set.update(range(start, end + 1))
    item_set = set()
    for start, end in item_ranges:
        item_set.update(range(start, end + 1))

    overlap = fsm_set & item_set
    if overlap:
        raise RuntimeError(f"Overlapping line ranges: {sorted(overlap)[:20]}...")

    main_lines = [line for i, line in enumerate(lines) if i not in fsm_set and i not in item_set]
    fsm_body = slice_lines(lines, fsm_ranges)
    item_body = slice_lines(lines, item_ranges)

    (SYNC_DIR / "WorldSyncManager.FsmSync.cs").write_text(
        HEADER + "".join(fsm_body) + FOOTER, encoding="utf-8"
    )
    (SYNC_DIR / "WorldSyncManager.ItemSync.cs").write_text(
        HEADER + "".join(item_body) + FOOTER, encoding="utf-8"
    )
    SRC.write_text("".join(main_lines), encoding="utf-8")

    print(f"Main: {len(main_lines)} lines")
    print(f"FsmSync: {len(fsm_body)} lines")
    print(f"ItemSync: {len(item_body)} lines")


if __name__ == "__main__":
    main()
