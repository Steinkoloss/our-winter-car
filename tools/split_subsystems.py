"""Split FsmWorldSync and VehicleWorldSync into partial classes (<1000 lines each)."""
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SYNC = ROOT / "src" / "WinterMP.Core" / "Sync"

def header(class_name: str) -> str:
    return f"""using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{{
    internal sealed partial class {class_name}
    {{
"""

FOOTER = """    }
}
"""


def find_line(lines, pattern, start=0):
    for i in range(start, len(lines)):
        if pattern in lines[i]:
            return i
    raise ValueError(f"Pattern not found: {pattern!r} from {start}")


def write_partial(class_name, filename, body_lines):
    path = SYNC / filename
    path.write_text(
        header(class_name) + "".join(body_lines) + FOOTER,
        encoding="utf-8",
    )
    print(f"  {filename}: {len(body_lines)} body lines")


def split_class(source_path, class_name, ranges, main_range):
    lines = source_path.read_text(encoding="utf-8").splitlines(keepends=True)
    # Strip outer wrapper (using..namespace..class { .. })
    i_class = find_line(lines, f"internal sealed class {class_name}")
    i_open = find_line(lines, "{", i_class)
    i_close = len(lines) - 2  # closing class brace

    body = lines[i_open + 1 : i_close]

    for filename, start_pat, end_pat in ranges:
        s = find_line(body, start_pat)
        if end_pat:
            e = find_line(body, end_pat, s + 1) - 1
        else:
            e = len(body) - 1
        write_partial(class_name, filename, body[s : e + 1])

    ms, me = main_range[0], main_range[1]
    main_s = find_line(body, ms)
    chunks: list[str] = []
    if me:
        main_e = find_line(body, me, main_s + 1) - 1
        chunks.extend(body[main_s : main_e + 1])

    if len(main_range) == 4:
        ms2, me2 = main_range[2], main_range[3]
        s2 = find_line(body, ms2)
        e2 = find_line(body, me2, s2 + 1) - 1 if me2 else len(body) - 1
        chunks.extend(body[s2 : e2 + 1])

    source_path.write_text(
        header(class_name) + "".join(chunks) + FOOTER,
        encoding="utf-8",
    )
    print(f"  {source_path.name}: {len(chunks)} body lines (main)")


def main():
    print("FsmWorldSync ->")
    split_class(
        SYNC / "FsmWorldSync.cs",
        "FsmWorldSync",
        [
            ("FsmWorldSync.Registry.cs", "internal static bool ClassifyBuy", "internal uint ComputeWorldCrc"),
            ("FsmWorldSync.Checksum.cs", "internal uint ComputeWorldCrc", "// ------------------------------------------------------------------ local -> network"),
            ("FsmWorldSync.Local.cs", "// ------------------------------------------------------------------ local -> network", "// ------------------------------------------------------------------ network -> world"),
            ("FsmWorldSync.Remote.cs", "// ------------------------------------------------------------------ network -> world", "internal IEnumerable<WorldDoorSnapshot> BuildDoorSnapshotChunks"),
            ("FsmWorldSync.Snapshots.cs", "internal IEnumerable<WorldDoorSnapshot> BuildDoorSnapshotChunks", "internal void RunDoorTest"),
            ("FsmWorldSync.Tests.cs", "internal void RunDoorTest", None),
        ],
        ("private const float PendingTtlSeconds", "internal static bool ClassifyBuy"),
    )

    print("VehicleWorldSync ->")
    split_class(
        SYNC / "VehicleWorldSync.cs",
        "VehicleWorldSync",
        [
            ("VehicleWorldSync.Engine.cs", "// ------------------------------------------------------------------ engine & ignition", "private static void EnsureClimateProbe"),
            ("VehicleWorldSync.Climate.cs", "private static void EnsureClimateProbe", "internal void LateUpdateClimate"),
        ],
        (
            "private const float PendingTtlSeconds",
            "// ------------------------------------------------------------------ engine & ignition",
            "internal void LateUpdateClimate",
            None,
        ),
    )
    print("Done.")


if __name__ == "__main__":
    main()
