"""Merge WorldSyncManager partials into FsmWorldSync, ItemWorldSync, VehicleWorldSync."""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parent.parent
SYNC = ROOT / "src" / "WinterMP.Core" / "Sync"

FSM_FILES = [
    "WorldSyncManager.FsmTypes.cs",
    "WorldSyncManager.FsmRegistry.cs",
    "WorldSyncManager.FsmChecksum.cs",
    "WorldSyncManager.FsmLocal.cs",
    "WorldSyncManager.FsmRemote.cs",
    "WorldSyncManager.FsmSnapshots.cs",
    "WorldSyncManager.FsmTests.cs",
]

ITEM_FILES = [
    "WorldSyncManager.ItemTypes.cs",
    "WorldSyncManager.ItemRegistry.cs",
    "WorldSyncManager.ItemTransform.cs",
]

VEHICLE_FILES = [
    "WorldSyncManager.VehicleEngine.cs",
    "WorldSyncManager.VehicleClimate.cs",
    "WorldSyncManager.VehicleControls.cs",
]


def extract_body(path: Path) -> str:
    text = path.read_text(encoding="utf-8")
    start = text.index("public sealed partial class WorldSyncManager")
    start = text.index("{", start) + 1
    end = text.rindex("    }\r\n}") if "\r\n" in text else text.rindex("    }\n}")
    return text[start:end]


def patch_fsm(body: str) -> str:
    body = body.replace("_applyingRemote", "_bridge.ApplyingRemote")
    body = body.replace("_selfTest", "_bridge.SelfTest")
    body = body.replace("PrepareRemoteControl(control)", "_vehicles.PrepareRemoteControl(control)")
    body = body.replace("FinishRemoteControl(control, stateName)", "_vehicles.FinishRemoteControl(control, stateName)")
    body = body.replace("_firstDoorRegisteredAt", "_bridge.FirstDoorRegisteredAt")
    body = body.replace("_doorTestDelay", "_bridge.DoorTestDelay")
    body = body.replace("_doorTestStep", "_bridge.DoorTestStep")
    return body


def patch_item(body: str) -> str:
    body = body.replace("_applyingRemote", "_bridge.ApplyingRemote")
    body = body.replace("EnsureVehicleSystemsProbe(item)", "_vehicles.EnsureVehicleSystemsProbe(item)")
    body = body.replace("UpdateRemoteEngineAudio(item, now)", "_vehicles.UpdateRemoteEngineAudio(item, now)")
    body = body.replace("FindLocalPlayer();", "_bridge.FindLocalPlayer();")
    body = body.replace("_localPlayer", "_bridge.LocalPlayer")
    body = body.replace("PlayerSearchIntervalSeconds", "WorldSyncBridge.PlayerSearchIntervalSeconds")
    body = body.replace('GameObject.Find(PlayerObjectName)', 'GameObject.Find(WorldSyncBridge.PlayerObjectName)')
    body = body.replace("_nextPlayerSearchAt", "_bridge.NextPlayerSearchAt")
    return body


def patch_vehicle(body: str) -> str:
    body = body.replace("_applyingRemote", "_bridge.ApplyingRemote")
    body = body.replace("HasAllStates(", "FsmWorldSync.HasAllStates(")
    body = body.replace("FindLocalPlayer();", "_bridge.FindLocalPlayer();")
    body = body.replace("_localPlayer", "_bridge.LocalPlayer")
    body = body.replace("SessionManager.Instance?.LocalPlayerId", "_bridge.Session?.LocalPlayerId")
    body = body.replace(
        "return item.Body != null && Instance != null && Instance.IsLocalPlayerDriving(item)",
        "return item.Body != null && _bridge.IsLocalPlayerDriving(item)",
    )
    body = re.sub(
        r"var inst = Instance;\s*if \(inst != null\) inst\._bridge\.ApplyingRemote = true;",
        "_bridge.ApplyingRemote = true;",
        body,
    )
    body = re.sub(
        r"if \(inst != null\) inst\._bridge\.ApplyingRemote = false;",
        "_bridge.ApplyingRemote = false;",
        body,
    )
    body = body.replace("var inst = Instance;", "")
    return body


def write_class(name: str, body: str, extra_fields: str, usings: str = ""):
    header = f"""using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
{usings}
namespace WinterMP.Core.Sync
{{
    internal sealed class {name}
    {{
        private const float PendingTtlSeconds = 120f;
        private const float SnapshotPoseTtlSeconds = 300f;
        private const int DoorSnapshotChunk = 60;
        private const int BoltSnapshotChunk = 80;
        private const int PartSnapshotChunk = 80;
        private const int ItemSnapshotChunk = 40;
        private static readonly string[] AllowedRawEvents = {{ "TIGHTEN", "UNTIGHTEN" }};

        private readonly WorldSyncBridge _bridge;
{extra_fields}
"""
    path = SYNC / f"{name}.cs"
    path.write_text(header + body + "\n    }\n}\n", encoding="utf-8")
    lines = path.read_text(encoding="utf-8").count("\n")
    print(f"  {name}.cs: {lines} lines")


def main():
    fsm_body = patch_fsm("".join(extract_body(SYNC / f) for f in FSM_FILES))
    item_body = patch_item("".join(extract_body(SYNC / f) for f in ITEM_FILES))
    vehicle_body = patch_vehicle("".join(extract_body(SYNC / f) for f in VEHICLE_FILES))

    write_class(
        "FsmWorldSync",
        fsm_body,
        """        private readonly VehicleWorldSync _vehicles;

        public FsmWorldSync(WorldSyncBridge bridge, VehicleWorldSync vehicles)
        {
            _bridge = bridge;
            _vehicles = vehicles;
        }
""",
    )

    write_class(
        "ItemWorldSync",
        item_body,
        """        private VehicleWorldSync _vehicles = null!;

        public ItemWorldSync(WorldSyncBridge bridge)
        {
            _bridge = bridge;
        }

        public void BindVehicles(VehicleWorldSync vehicles) => _vehicles = vehicles;

        public IReadOnlyDictionary<uint, SyncedItem> Items => _items;
""",
    )

    write_class(
        "VehicleWorldSync",
        vehicle_body,
        """        private readonly ItemWorldSync _items;

        public VehicleWorldSync(WorldSyncBridge bridge, ItemWorldSync items)
        {
            _bridge = bridge;
            _items = items;
        }
""",
    )

    for f in FSM_FILES + ITEM_FILES + VEHICLE_FILES:
        (SYNC / f).unlink(missing_ok=True)

    print("Removed partial files")


if __name__ == "__main__":
    main()
