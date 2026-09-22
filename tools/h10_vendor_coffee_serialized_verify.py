#!/usr/bin/env python3
"""RUN-scoped offline H10 reader verification. No game/build/rig/protected inputs."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from datetime import datetime, timezone

import h10_vendor_coffee_serialized as reader

SOURCE = reader.SOURCE


def save(path, value):
    with path.open("xb") as stream: stream.write(reader.canonical(value))


def fingerprint(path):
    raw = reader.read_permitted(str(path), [SOURCE, path.parent] if path.is_relative_to(SOURCE) else [path.parent])
    return {"bytes": len(raw), "sha256": reader.sha(raw)}


def inputs():
    paths = [SOURCE / name for name in (
        "catalog/dump-23268598.json", "catalog/sync-catalog.json", "PLAN.md", "protocol/PROTOCOL.md",
        "docs/H10-VENDOR-COFFEE-FOUNDATION.md", "docs/H10-VENDOR-COFFEE-SERIALIZED.md",
        "tools/h10_vendor_coffee_serialized.py", "tools/h10_vendor_coffee_serialized_verify.py",
        "tools/tests/test_h10_vendor_coffee_serialized.py", "tools/extract_fsm_assets.py")]
    for directory, folders, files in os.walk(SOURCE / "src", followlinks=False):
        folders[:] = sorted(n for n in folders if n not in ("bin", "obj") and not (Path(directory) / n).is_symlink())
        paths.extend(Path(directory) / n for n in files if n.endswith((".cs", ".csproj")))
    for project in ("WinterMP.Net", "WinterMP.Core"):
        for config in ("Debug", "Release"):
            for framework in ("net35", "netstandard2.0"):
                path = SOURCE / "src" / project / "bin" / config / framework / (project + ".dll")
                if path.exists(): paths.append(path)
    return {str(p.relative_to(SOURCE)): fingerprint(p) for p in sorted(set(paths))}


def command(output, name, argv, expected):
    log = output / (name + ".log")
    started = datetime.now(timezone.utc).isoformat()
    with log.open("xb") as stream:
        stream.write(("CWD " + str(SOURCE) + "\nARGV " + json.dumps(argv) + "\n").encode()); stream.flush()
        try:
            result = subprocess.run(argv, cwd=SOURCE, stdout=stream, stderr=subprocess.STDOUT, timeout=300,
                                    env=dict(os.environ, PYTHONDONTWRITEBYTECODE="1", TMPDIR=str(output / "scratch")))
            code = result.returncode
        except subprocess.TimeoutExpired:
            code = 124  # subprocess.run kills and waits only its owned child.
        stream.write(("\nEXIT " + str(code) + "\n").encode())
    receipt = {"argv": argv, "cwd": str(SOURCE), "started_utc": started,
               "finished_utc": datetime.now(timezone.utc).isoformat(), "exit_code": code,
               "expected_exit_code": expected, "matched_expected_exit": code == expected,
               "log": str(log), "log_sha256": reader.sha(log.read_bytes()), "role": "offline-portable-test"}
    save(output / (name + ".command.json"), receipt)
    print(name, "exit", code, "expected", expected, flush=True)
    return receipt


def tree_bytes(path):
    return {p.name: p.read_bytes() for p in sorted(path.iterdir())}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run", required=True)
    parser.add_argument("--name", required=True)
    args = parser.parse_args()
    run = reader.validate_run(args.run)
    reader.require(re.fullmatch(r"[a-z0-9][a-z0-9-]{0,64}", args.name), "new leaf name required")
    output = run / args.name
    output.mkdir(exist_ok=False); (output / "scratch").mkdir()
    before = inputs(); save(output / "inputs-before.json", before)
    receipts, assertions = [], {}
    save(output / "invocation.json", {"argv": [sys.executable, *sys.argv], "cwd": str(Path.cwd()),
                                       "assigned_run": str(run), "role": "offline-synthetic-and-historical-verification"})
    receipts.append(command(output, "focused", [sys.executable, "-B", "-m", "unittest", "discover", "-s", "tools/tests",
                                                "-p", "test_h10_vendor_coffee_serialized.py", "-v"], 0))
    receipts.append(command(output, "python-suite", [sys.executable, "-B", "-m", "unittest", "discover", "-s", "tools/tests", "-v"], 0))
    base = [sys.executable, "-B", "tools/h10_vendor_coffee_serialized.py", "--run", str(run)]
    legacy = base + ["--input", str(SOURCE / "catalog/dump-23268598.json"), "--input-sha256", reader.LEGACY_SHA256,
                     "--build-id", "23268598", "--source-id", "historical-dump-23268598", "--source-sha256", reader.LEGACY_SHA256]
    # The positive schema example is explicitly synthetic and is retained as such.
    sys.path.insert(0, str(SOURCE / "tools/tests"))
    from test_h10_vendor_coffee_serialized import fixture
    doc = fixture(); raw = reader.canonical(doc)
    permitted = run / "permitted-inputs"
    permitted.mkdir(exist_ok=True)
    fd = reader.open_directory(str(permitted)); os.close(fd)
    synthetic = permitted / (args.name + "-synthetic.json")
    with synthetic.open("xb") as stream: stream.write(raw)
    synthetic_args = base + ["--input", str(synthetic), "--input-sha256", reader.sha(raw),
                             "--build-id", "fixture-build", "--source-id", "fixture-source",
                             "--source-sha256", doc["provenance"]["sourceSha256"]]
    for kind, argv, expected in (("legacy", legacy, 2), ("synthetic", synthetic_args, 0)):
        for suffix in ("a", "b"):
            name = args.name + "-" + kind + "-" + suffix
            receipts.append(command(output, kind + "-" + suffix, argv + ["--name", name], expected))
        first, second = [run / (args.name + "-" + kind + "-" + suffix) for suffix in ("a", "b")]
        assertions[kind + "_deterministic_report"] = (first / "report.json").read_bytes() == (second / "report.json").read_bytes()
        snapshot = tree_bytes(first)
        receipts.append(command(output, kind + "-refuse-overwrite", argv + ["--name", first.name], 1))
        assertions[kind + "_existing_evidence_preserved"] = snapshot == tree_bytes(first)
        for target in (first, second):
            receipt = json.loads((target / "receipt.json").read_text())
            report = json.loads((target / "report.json").read_text())
            assertions[target.name + "_hashes"] = (receipt["input_sha256"] == reader.sha((target / "input.json").read_bytes()) == report["input_sha256"] and
                                                   receipt["output_sha256"] == reader.sha((target / "report.json").read_bytes()))
            assertions[target.name + "_not_gameplay"] = not report["production_ready"] and all(v == "NOT_TESTED" for v in report["gameplay_claims"].values())
    bad = synthetic_args.copy(); bad[bad.index("--build-id") + 1] = "wrong-build"
    receipts.append(command(output, "reject-build", bad + ["--name", args.name + "-reject-build"], 1))
    bad = synthetic_args.copy(); bad[bad.index("--input") + 1] = str(run / "contract.json")
    receipts.append(command(output, "reject-outside-permitted-root", bad + ["--name", args.name + "-reject-outside"], 1))
    assertions["outside_read_created_no_evidence"] = not (run / (args.name + "-reject-outside")).exists()
    after = inputs(); save(output / "inputs-after.json", after)
    assertions["source_catalog_tool_and_existing_binaries_unchanged_during_verification"] = before == after
    old = json.loads(reader.read_permitted(str(run / "before.json"), [run]))
    production = [p for p in before if (p.startswith("src/") and "/bin/" not in p) or p.startswith(("catalog/", "protocol/")) or p == "PLAN.md"]
    assertions["production_unchanged_since_worker_start"] = all(old.get(p) == after[p]["sha256"] for p in production)
    scratch = output / "scratch"
    assertions["scratch_empty"] = not any(scratch.iterdir())
    if assertions["scratch_empty"]: scratch.rmdir()
    summary = {"assigned_run": str(run), "commands_match_expected": all(r["matched_expected_exit"] for r in receipts),
               "assertions": assertions, "command_count": len(receipts), "fingerprint_count": len(before),
               "cleanup": {"all_owned_subprocesses_waited": True, "game_processes_started": 0, "protected_inputs_read": False,
                           "rig_touched": False, "builds_or_deployments": 0, "retained_synthetic_input": str(synthetic)},
               "limits": dict(reader.CLAIMS),
               "input_availability": "No richer permitted native input supplied. Historical catalog input is topology-only; positive fixture is synthetic, not extracted native data.",
               "status": "PASS" if all(assertions.values()) and all(r["matched_expected_exit"] for r in receipts) else "FAIL"}
    save(output / "verification.json", summary)
    print(json.dumps(summary, sort_keys=True))
    return 0 if summary["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
