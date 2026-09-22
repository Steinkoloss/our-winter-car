#!/usr/bin/env python3
"""Portable H10 audit receipts. No native launch, builds, deployment or protected reads."""
from __future__ import annotations
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

SOURCE = Path(__file__).resolve().parents[1]


def utc():
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds")


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def save(path, value):
    with path.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2, sort_keys=True)
        stream.write("\n")


def baseline():
    # Source-only paths: do not follow game-reference symlinks or traverse the rig.
    files = []
    for root in (SOURCE / "src", SOURCE / "tools/GuestSaveProbe"):
        for directory, folders, names in os.walk(root, followlinks=False):
            folders[:] = sorted(n for n in folders if not (Path(directory) / n).is_symlink())
            for name in names:
                p = Path(directory) / name
                if p.suffix in (".cs", ".csproj", ".dll", ".exe", ".pdb") and not p.is_symlink():
                    files.append(p)
    files += [SOURCE / n for n in ("catalog/dump-23268598.json", "catalog/sync-catalog.json",
                                   "protocol/PROTOCOL.md", "PLAN.md", "docs/BUILDING.md")]
    for p in files:
        if p != p.resolve() or not p.is_relative_to(SOURCE):
            raise ValueError("Refuse redirected baseline input: " + str(p))
    return {str(p.relative_to(SOURCE)): {"sha256": digest(p), "bytes": p.stat().st_size}
            for p in sorted(set(files))}


def command(output, name, args, expected):
    started = utc()
    log = output / (name + ".log")
    timed_out = False
    with log.open("x", encoding="utf-8") as stream:
        stream.write("UTC " + started + "\nCWD " + str(SOURCE) + "\nARGV " + json.dumps(args) + "\n")
        stream.flush()
        try:
            env = dict(os.environ, PYTHONDONTWRITEBYTECODE="1", TMPDIR=str(output / "scratch"))
            result = subprocess.run(args, cwd=SOURCE, env=env, stdout=stream,
                                    stderr=subprocess.STDOUT, timeout=300)
            code = result.returncode
        except subprocess.TimeoutExpired:
            # subprocess.run kills and waits its child on timeout. No native descendants.
            code, timed_out = 124, True
        stream.write("\nEXIT " + str(code) + "\nUTC " + utc() + "\n")
    receipt = {"argv": args, "cwd": str(SOURCE), "started_utc": started,
               "finished_utc": utc(), "exit_code": code, "expected_exit_code": expected,
               "matched_expected_exit": code == expected, "timed_out": timed_out,
               "raw_log": str(log), "sha256": digest(log)}
    save(output / (name + ".receipt.json"), receipt)
    print(name, "exit", code, "expected", expected, flush=True)
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run", type=Path, required=True)
    parser.add_argument("--name", required=True, help="New immutable receipt directory inside --run")
    parser.add_argument("--phase", choices=("red", "final"), required=True)
    parser.add_argument("--baseline-name", help="Earlier receipt leaf in this RUN to compare production inputs against")
    args = parser.parse_args()
    run = args.run
    if (not run.is_absolute() or run != run.resolve() or run.parent != SOURCE.parent / "rounds"
            or not (run / "contract.json").is_file() or (run / "contract.json").is_symlink()):
        parser.error("Use the absolute assigned RUN containing its contract (no symlinks)")
    contract = json.loads((run / "contract.json").read_text())
    if contract.get("work_type") != "audit" or "H10" not in contract.get("ids", []):
        parser.error("RUN is not an H10 audit contract")
    if not re.fullmatch(r"[a-z0-9][a-z0-9-]*", args.name): parser.error("Use a new leaf name")
    output = run / args.name
    output.mkdir(exist_ok=False)
    (output / "scratch").mkdir()
    before = baseline()
    save(output / "inputs-before.json", before)
    original_unchanged = None
    if args.baseline_name:
        if not re.fullmatch(r"[a-z0-9][a-z0-9-]*", args.baseline_name): parser.error("Invalid baseline leaf")
        original = run / args.baseline_name / "inputs-before.json"
        if original != original.resolve(): parser.error("Refuse redirected baseline")
        original_unchanged = before == json.loads(original.read_text())
    save(output / "invocation.json", {"argv": [sys.executable, *sys.argv], "cwd": str(Path.cwd()),
                                      "utc": utc(), "script_sha256": digest(Path(__file__)),
                                      "role": "portable-static-audit", "assigned_run": str(run)})
    receipts = []
    focused = [sys.executable, "-B", "-m", "unittest", "discover", "-s", "tools/tests",
               "-p", "test_h10_vendor_coffee_audit.py", "-v"]
    receipts.append(command(output, "focused", focused, 1 if args.phase == "red" else 0))
    if args.phase == "final":
        receipts.append(command(output, "python-suite", [sys.executable, "-B", "-m", "unittest",
                                  "discover", "-s", "tools/tests", "-v"], 0))
        audit = [sys.executable, "-B", "tools/h10_vendor_coffee_audit.py", "--run", str(run)]
        for suffix in ("a", "b"):
            receipts.append(command(output, "audit-" + suffix,
                            audit + ["--name", args.name + "-audit-" + suffix], 2))
        first = run / (args.name + "-audit-a") / "report.json"
        second = run / (args.name + "-audit-b") / "report.json"
        same = first.is_file() and second.is_file() and first.read_bytes() == second.read_bytes()
        save(output / "determinism.json", {"identical_reports": same,
                                           "first": str(first), "second": str(second)})
        prior = first.parent
        prior_hashes = {p.name: digest(p) for p in sorted(prior.iterdir()) if p.is_file()}
        receipts.append(command(output, "refuse-existing-output",
                                audit + ["--name", args.name + "-audit-a"], 1))
        preserved = prior_hashes == {p.name: digest(p) for p in sorted(prior.iterdir()) if p.is_file()}
        save(output / "immutable-evidence.json", {"unchanged_after_refused_overwrite": preserved,
                                                  "hashes": prior_hashes})
    else:
        same = None
        preserved = None
    after = baseline()
    save(output / "inputs-after.json", after)
    scratch = output / "scratch"
    empty = not any(scratch.iterdir())
    if empty: scratch.rmdir()
    unchanged = before == after
    summary = {"phase": args.phase, "commands_match_expected": all(r["matched_expected_exit"] for r in receipts),
               "source_catalog_and_binary_inputs_unchanged": unchanged,
               "unchanged_since_baseline": original_unchanged, "baseline_name": args.baseline_name,
               "existing_evidence_preserved": preserved,
               "deterministic_reports": same, "receipt_count": len(receipts),
               "cleanup": {"all_owned_subprocesses_waited": True, "scratch_empty_removed": empty,
                           "game_processes_started": 0, "builds_or_deployments": 0,
                           "protected_files_read": False, "rig_touched": False},
               "limits": "Static dump/code only. Native/ordinary-input/Steam/two-PC/soak NOT TESTED. No H10 or V11 promotion."}
    save(output / "verification.json", summary)
    print(json.dumps(summary, sort_keys=True))
    return 0 if (summary["commands_match_expected"] and unchanged and empty and same is not False
                 and preserved is not False and original_unchanged is not False) else 1


if __name__ == "__main__":
    raise SystemExit(main())
