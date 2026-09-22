#!/usr/bin/env python3
"""Record one bounded portable command under an explicit assigned RUN directory."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run", type=Path, required=True)
    parser.add_argument("name")
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    run = args.run.resolve()
    if not (run / "contract.json").is_file() or not args.command or Path(args.name).name != args.name:
        parser.error("An assigned contract directory, leaf receipt name and command are required")
    output = run / (args.name + ".log")
    receipt_path = run / (args.name + ".command.json")
    if output.exists() or receipt_path.exists():
        parser.error("Refusing to overwrite prior evidence; choose a new receipt name")
    started = time.time()
    result = subprocess.run(args.command, cwd=root, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=600)
    output.write_bytes(result.stdout)
    receipt = {"argv": args.command, "cwd": str(root), "exit_code": result.returncode,
               "started": started, "ended": time.time(), "output": str(output),
               "sha256": hashlib.sha256(result.stdout).hexdigest()}
    receipt_path.write_text(json.dumps(receipt, indent=2) + "\n")
    print(json.dumps(receipt, indent=2))
    print(result.stdout.decode(errors="replace")[-7000:])
    return result.returncode

if __name__ == "__main__":
    sys.exit(main())
