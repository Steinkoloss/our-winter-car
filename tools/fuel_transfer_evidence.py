"""Bounded fuel-transfer source manifest, catalog excerpts and command receipts."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import pathlib
import subprocess
import time

ROOT = pathlib.Path(__file__).resolve().parents[1]
SKIP = {"bin", "obj", "build", ".git", "__pycache__"}

def manifest():
    return {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in ROOT.rglob("*") if p.is_file() and not p.is_symlink()
            and not p.name.startswith(".env") and not SKIP.intersection(p.relative_to(ROOT).parts)}


def run_command(output, cmd, timeout=600):
    output.mkdir(parents=True, exist_ok=False)
    start = time.monotonic()
    row = dict(argv=cmd, cwd=str(ROOT), started_utc=datetime.now(timezone.utc).isoformat(),
               exit_code=127, timed_out=False, timeout_seconds=timeout, source_sha256=manifest())
    with (output / "raw.log").open("x") as log:
        try:
            done = subprocess.run(cmd, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, timeout=timeout)
            row['exit_code'] = done.returncode
        except subprocess.TimeoutExpired as error:
            # subprocess.run kills/waits its direct child only. Native drivers must
            # retain their shorter alarm and token-scoped descendant cleanup.
            row.update(exit_code=124, timed_out=True, error=type(error).__name__ + ': ' + str(error),
                       timeout_cleanup_scope='subprocess.run direct child only; descendants not certified')
        except OSError as error:
            row['error'] = type(error).__name__ + ': ' + str(error)
    candidates = list(ROOT.glob("src/*/bin/Release/**/*.dll")) + list(ROOT.glob("tools/*/bin/Release/**/*.dll"))
    row.update(seconds=time.monotonic() - start, ended_utc=datetime.now(timezone.utc).isoformat(),
               binaries={str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
                         for p in candidates if p.name.startswith('WinterMP.') or p.name.endswith('.Tests.dll')},
               raw_sha256=hashlib.sha256((output / "raw.log").read_bytes()).hexdigest())
    with (output / "receipt.json").open('x') as stream:
        json.dump(row, stream, indent=2)
    return row

if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["baseline", "check", "run", "catalog"])
    ap.add_argument("output", type=pathlib.Path)
    ap.add_argument("command", nargs=argparse.REMAINDER)
    args = ap.parse_args()
    if args.mode == "baseline":
        args.output.write_text(json.dumps(manifest(), indent=2))
        print(args.output)
    elif args.mode == "check":
        before = json.loads(args.output.read_text())
        after = manifest()
        changed = sorted(k for k in before.keys() | after.keys() if before.get(k) != after.get(k))
        allowed = ("src/WinterMP.Net/", "src/WinterMP.Core/Sync/", "src/WinterMP.Core/Session/", "src/WinterMP.Net.Tests/", "tools/", "docs/", "protocol/")
        bad = [k for k in changed if not k.startswith(allowed) and k != "PLAN.md"]
        receipt = {"changed": changed, "outside_scope": bad, "sha256": {k: after.get(k) for k in changed}}
        args.output.with_name("scope-check.json").write_text(json.dumps(receipt, indent=2))
        print(json.dumps(receipt, indent=2))
        raise SystemExit(bool(bad))
    elif args.mode == "run":
        cmd = args.command[1:] if args.command[:1] == ["--"] else args.command
        receipt = run_command(args.output, cmd)
        print(args.output, "exit", receipt['exit_code'])
        print((args.output / "raw.log").read_text()[-4500:])
        raise SystemExit(receipt['exit_code'])
    else:
        data = json.loads((ROOT / "catalog/dump-23268598.json").read_text())
        print(type(data).__name__, list(data)[:8] if isinstance(data, dict) else len(data))
        entries = data.get("fsms", []) if isinstance(data, dict) else data
        selected = [x for x in entries if "gasoline(itemx)" in x.get("path", "") or (x.get("path", "").startswith("SORBET") and any(w in x.get("path", "").lower() for w in ("fueltank", "fueltrigger", "captrigger")))]
        args.output.write_text(json.dumps(selected, indent=2))
        for x in selected:
            print(x["path"], x.get("fsmName"), [s["name"] for s in x.get("states", [])], x.get("variables", {}))
