"""Hoist nested sync types to namespace-level internal types."""
from pathlib import Path
import re

SYNC = Path(__file__).resolve().parent.parent / "src" / "WinterMP.Core" / "Sync"


def extract_block(text, start_pattern, end_line_pattern=None):
    start = text.index(start_pattern)
    if end_line_pattern:
        # find closing brace at same indent level - naive: next line after start that is "        }" alone before next member
        pass
    return start


def remove_nested_class(content, class_header):
    """Remove one nested type block starting with class_header line."""
    idx = content.index(class_header)
    # find opening brace
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
                # include trailing newline
                while end < len(content) and content[end] in "\r\n":
                    end += 1
                return content[:idx] + content[end:]
        i += 1
    raise ValueError("unclosed")


def main():
    item_path = SYNC / "ItemWorldSync.cs"
    item = item_path.read_text(encoding="utf-8")

    # Extract SyncedItem
    si_start = item.index("        private sealed class SyncedItem")
    si_end = item.index("        private struct PendingPose")
    synced_item_body = item[si_start:si_end]
    synced_item_body = synced_item_body.replace("        private sealed class SyncedItem", "    internal sealed class SyncedItem")
    synced_item_body = re.sub(r"^        ", "", synced_item_body, flags=re.M)
    synced_item_body = synced_item_body.replace(
        "public float ClaimRadius => IsVehicle ? VehicleClaimRadius : ItemClaimRadius;",
        "public float ClaimRadius => IsVehicle ? 7f : 4f;",
    ).replace(
        "public float SendRateHz => IsVehicle ? VehicleSendRateHz : ItemSendRateHz;",
        "public float SendRateHz => IsVehicle ? 15f : 10f;",
    ).replace(
        "public float StillSeconds => IsVehicle ? VehicleStillSeconds : ItemStillSeconds;",
        "public float StillSeconds => IsVehicle ? 3f : 1.5f;",
    ).replace(
        "public float MoveThresholdSqr => IsVehicle ? VehicleMoveEpsilonSqr : MoveEpsilonSqr;",
        "public float MoveThresholdSqr => IsVehicle ? 2.5e-3f : 1e-6f;",
    )

    pp_start = item.index("        private struct PendingPose")
    pp_end = item.index("        public int ItemCount")
    pending_pose = item[pp_start:pp_end]
    pending_pose = pending_pose.replace("        private struct PendingPose", "    internal struct PendingPose")
    pending_pose = re.sub(r"^        ", "", pending_pose, flags=re.M)

    item = item[:si_start] + item[pp_end:]
    item_path.write_text(item, encoding="utf-8")

    types_cs = f"""using UnityEngine;

namespace WinterMP.Core.Sync
{{
    internal static class WorldSyncIds
    {{
        public const byte NoOwner = 255;
    }}

{synced_item_body}
{pending_pose}
    public struct VehicleInfo
    {{
        public uint Id;
        public Rigidbody Body;
    }}
}}
"""
    (SYNC / "WorldSyncTypes.cs").write_text(types_cs, encoding="utf-8")

    fsm_path = SYNC / "FsmWorldSync.cs"
    fsm = fsm_path.read_text(encoding="utf-8")
    # Extract types block from SyncedDoor through PendingFsmApply
    t_start = fsm.index("        private sealed class SyncedDoor")
    t_end = fsm.index("        public static bool ClassifyBuy")
    types_block = fsm[t_start:t_end]
    types_block = types_block.replace("        private sealed class", "    internal sealed class")
    types_block = types_block.replace("        private struct", "    internal struct")
    types_block = re.sub(r"^        ", "", types_block, flags=re.M)

    fsm_types = f"""using UnityEngine;

namespace WinterMP.Core.Sync
{{
{types_block}}}
"""
    existing = (SYNC / "WorldSyncTypes.cs").read_text(encoding="utf-8")
    (SYNC / "WorldSyncTypes.cs").write_text(existing.rstrip() + "\n\n" + types_block + "}\n", encoding="utf-8")

    fsm = fsm[:t_start] + fsm[t_end:]
    # Fix access on methods using nested types
    fsm = fsm.replace("public static bool ClassifyBuy", "internal static bool ClassifyBuy")
    fsm = fsm.replace("public bool Register", "internal bool Register")
    fsm = fsm.replace("public uint ComputeWorldCrc", "internal uint ComputeWorldCrc")
    fsm = fsm.replace("public string? TryGetFsmSnapshotState", "internal string? TryGetFsmSnapshotState")
    fsm = fsm.replace("public static bool ShouldIncludePartSnapshot", "internal static bool ShouldIncludePartSnapshot")
    fsm = fsm.replace("public static void ReadBoltVars", "internal static void ReadBoltVars")
    fsm = fsm.replace("public void ProcessPending", "internal void ProcessPending")
    fsm = fsm.replace("public IEnumerable<WorldDoorSnapshot> BuildDoorSnapshotChunks", "internal IEnumerable<WorldDoorSnapshot> BuildDoorSnapshotChunks")
    fsm = fsm.replace("public IEnumerable<WorldBoltSnapshot> BuildBoltSnapshotChunks", "internal IEnumerable<WorldBoltSnapshot> BuildBoltSnapshotChunks")
    fsm = fsm.replace("public IEnumerable<WorldPartSnapshot> BuildPartSnapshotChunks", "internal IEnumerable<WorldPartSnapshot> BuildPartSnapshotChunks")
    fsm = fsm.replace("public void RunDoorTest", "internal void RunDoorTest")
    fsm_path.write_text(fsm, encoding="utf-8")

    # ItemWorldSync fixes
    item = item_path.read_text(encoding="utf-8")
    if "using System.Collections.Generic;" not in item:
        item = item.replace("using System;\n", "using System;\nusing System.Collections.Generic;\n")
    item = item.replace("public IReadOnlyDictionary", "internal IReadOnlyDictionary")
    item = item.replace("public int ScanItems", "internal int ScanItems")
    item = item.replace("public void UpdateItems", "internal void UpdateItems")
    item = item.replace("public uint ComputeItemCrc", "internal uint ComputeItemCrc")
    item = item.replace("public uint ComputeVehicleCrc", "internal uint ComputeVehicleCrc")
    item = item.replace("public IEnumerable<WorldItemSnapshot> BuildItemSnapshotChunks", "internal IEnumerable<WorldItemSnapshot> BuildItemSnapshotChunks")
    item = item.replace("public bool IsLocalPlayerDriving", "internal bool IsLocalPlayerDriving")
    item = item.replace("public void ReleaseSession", "internal void ReleaseSession")
    item = item.replace("public void Clear()", "internal void Clear()")
    item = item.replace("private const byte NoOwner = 255;", "")
    item = item.replace("        public struct VehicleInfo", "        // VehicleInfo moved to WorldSyncTypes")
    item = item.replace("        {\n            public uint Id;\n            public Rigidbody Body;\n        }\n\n", "")
    item_path.write_text(item, encoding="utf-8")

    print("Types extracted")


if __name__ == "__main__":
    main()
