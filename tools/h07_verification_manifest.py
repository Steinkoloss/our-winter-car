#!/usr/bin/env python3
"""Record source/binary hashes and compare read-only inputs with an existing observation.

This does not clear any native-launch gate or turn portable tests into gameplay.
"""
import argparse
import hashlib
import json
from pathlib import Path
import time


def digest(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run", type=Path, required=True)
    parser.add_argument("--reference", type=Path, required=True)
    parser.add_argument("--source", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    run = args.run.resolve(); source = args.source.resolve()
    if not (run / "contract.json").is_file():
        raise ValueError("A real assigned contract directory is required")
    before = json.loads((run / "before.json").read_text())
    allowed = json.loads((run / "contract.json").read_text())["allowed_paths"]
    current = {}
    for directory in ("src", "tools", "docs", "catalog", "protocol"):
        for path in (source / directory).rglob("*"):
            if not path.is_file() or any(p in ("bin", "obj", "__pycache__", ".venv", "build", ".pytest_cache") for p in path.parts):
                continue
            if path.name.startswith(".env"):
                continue
            current[str(path.relative_to(source))] = digest(path)
    current["PLAN.md"] = digest(source / "PLAN.md")
    changes = {name: sha for name, sha in current.items() if before.get(name) != sha}
    assert all(any(name == prefix or name.startswith(prefix) for prefix in allowed) for name in changes)
    binary_paths = ["src/WinterMP.Net/bin/Release/net35/WinterMP.Net.dll",
                    "src/WinterMP.Net/bin/Release/netstandard2.0/WinterMP.Net.dll",
                    "src/WinterMP.Core/bin/Release/net35/WinterMP.Core.dll",
                    "src/WinterMP.Net.Tests/bin/Release/net8.0/WinterMP.Net.Tests.dll"]
    reference = json.loads(args.reference.read_text())
    comparisons = []
    groups = dict(reference["protected"])
    groups["/home/jaimep/our-winter-car"] = reference["original_source"]
    for root, files in groups.items():
        changed = []; missing = []; skipped = []; checked = 0
        for name, expected in files.items():
            path = Path(root) / name
            if path.name.startswith(".env"):
                skipped.append(name); continue
            if not path.is_file():
                missing.append(name); continue
            checked += 1
            if digest(path) != expected:
                changed.append(name)
        comparisons.append({"root": root, "known_files_checked": checked, "changed": changed, "missing": missing,
                            "skipped_secret_files": skipped})
    receipt = {"task": "t_39c1f006", "created_unix": time.time(), "source_root": str(source),
               "source_sha256": changes, "binary_sha256": {p: digest(source / p) for p in binary_paths},
               "comparison_reference": str(args.reference), "reference_sha256": digest(args.reference),
               "input_comparison": comparisons,
               "native_launch_allowed": False,
               "inherited_protected_input_gate": "FAILED, unchanged; this observation does not rebaseline it",
               "evidence_levels": {"portable": "see command receipts", "static": "static-binding-assertions.json",
                    "native_injected": "NOT_TESTED", "ordinary_input": "NOT_TESTED", "native_cold_reload": "NOT_TESTED",
                    "Steam_two_PC": "NOT_TESTED", "four_player_soak": "NOT_TESTED"},
               "commands": [json.loads(p.read_text()) for p in sorted(run.glob("*.command.json"))]}
    (run / "verification-manifest.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print(json.dumps({"changed_paths": list(changes), "input_comparison": comparisons,
                      "binary_sha256": receipt["binary_sha256"]}, indent=2))
    assert all(not c["changed"] and not c["missing"] for c in comparisons), "Read-only inputs differ from previous observation"

if __name__ == "__main__":
    main()
