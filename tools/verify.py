#!/usr/bin/env python3
"""Portable verification for WinterMP. Uses Python 3.9+ stdlib and .NET 8.

The default checks only what can be built without proprietary game DLLs.
--game additionally compiles the three in-game plugins; neither mode claims
that a live multiplayer session has been playtested.
"""
from __future__ import annotations

import argparse
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Callable, List, Sequence

BUILDS = ("WinterMP.Net", "WinterMP.Launcher")
TESTS = ("WinterMP.Net.Tests", "WinterMP.Launcher.Tests")
GAME = ("WinterMP.Core", "WinterMP.Tools", "WinterMP.FastBoot")
ROOT = Path(__file__).resolve().parents[1]


class VerificationError(RuntimeError):
    """A missing prerequisite or failed check; never a successful skip."""


def project(name: str) -> str:
    return "src/%s/%s.csproj" % (name, name)


def check_structure(root: Path) -> None:
    """Protect the existing protocol boundary, not a new architecture."""
    path = root / project("WinterMP.Net")
    try:
        tree = ET.parse(path)
    except (OSError, ET.ParseError) as exc:
        raise VerificationError("Cannot read protocol project: %s" % exc) from exc
    frameworks = set()
    for element in tree.iter():
        tag = element.tag.rsplit("}", 1)[-1]
        if tag in ("TargetFramework", "TargetFrameworks"):
            frameworks.update((element.text or "").strip().split(";"))
        if tag == "ProjectReference":
            raise VerificationError("WinterMP.Net must not depend on another project.")
        if tag in ("Reference", "PackageReference"):
            name = element.get("Include", element.get("Update", "")).lower()
            if any(word in name for word in
                   ("unity", "playmaker", "bepinex", "steamworks", "assembly-csharp")):
                raise VerificationError("Engine dependency in WinterMP.Net: %s" % name)
    if frameworks != {"net35", "netstandard2.0"}:
        raise VerificationError("WinterMP.Net must target net35 and netstandard2.0.")
    for name in BUILDS + TESTS:
        if not (root / project(name)).is_file():
            raise VerificationError("Missing portable project: %s" % project(name))


def check_trx(path: Path) -> int:
    """A successful process is insufficient: require actual passing tests."""
    try:
        tree = ET.parse(path)
        counters = tree.find(".//{*}Counters")
        if counters is None:
            raise ValueError("missing test counters")
        total = int(counters.get("total", "0"))
        executed = int(counters.get("executed", "0"))
        passed = int(counters.get("passed", "0"))
        failed = int(counters.get("failed", "0"))
        results = tree.findall(".//{*}UnitTestResult")
        if not (total > 0 and total == executed == passed == len(results)
                and failed == 0
                and all(item.get("outcome") == "Passed" for item in results)):
            raise ValueError("zero, failed, skipped, or incomplete tests")
    except (OSError, ET.ParseError, ValueError) as exc:
        raise VerificationError("Unverified test report %s: %s" % (path, exc)) from exc
    return total


def run(command: Sequence[str], root: Path) -> None:
    print("+ " + subprocess.list2cmdline(list(command)), flush=True)
    try:
        subprocess.run(list(command), cwd=root, check=True, timeout=900)
    except FileNotFoundError as exc:
        raise VerificationError("Missing executable: %s. Install the .NET 8 SDK."
                                % command[0]) from exc
    except subprocess.TimeoutExpired as exc:
        raise VerificationError("Command timed out; no successful verification.") from exc
    except subprocess.CalledProcessError as exc:
        raise VerificationError("Command failed with exit %d." % exc.returncode) from exc


def verify(root: Path, *, game: bool = False, checks_only: bool = False,
           run_command: Callable[[Sequence[str], Path], None] = run) -> Path | None:
    check_structure(root)
    if checks_only:
        print("Static structure checks passed. Builds, tests and gameplay NOT VERIFIED.")
        return None

    # A unique directory prevents a previous green TRX from validating this run.
    parent = root / ".artifacts" / "verification"
    parent.mkdir(parents=True, exist_ok=True)
    output = Path(tempfile.mkdtemp(prefix="run-", dir=parent))
    print("Verification reports: %s" % output, flush=True)
    for name in BUILDS + TESTS:
        run_command(["dotnet", "restore", project(name)], root)
    for name in BUILDS:
        run_command(["dotnet", "build", project(name), "-c", "Release", "--no-restore"], root)
    for name in TESTS:
        filename = name + ".trx"
        run_command(["dotnet", "test", project(name), "-c", "Release", "--no-restore",
                     "--logger", "trx;LogFileName=" + filename,
                     "--results-directory", str(output)], root)
        count = check_trx(output / filename)
        print("PASS %s: %d executed tests" % (name, count), flush=True)
    if game:
        for name in GAME:
            if not (root / project(name)).is_file():
                raise VerificationError("Missing game project: %s" % project(name))
            run_command(["dotnet", "build", project(name), "-c", "Release"], root)
        print("Game plugins COMPILED. Live host/guest behavior and save safety NOT VERIFIED.")
    else:
        print("Portable build/tests PASSED. Game plugins and live multiplayer NOT VERIFIED.")
    return output


def main(argv: List[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument("--game", action="store_true", help="also build plugins using your local game DLLs")
    modes.add_argument("--checks-only", action="store_true", help="structure checks only; not build verification")
    args = parser.parse_args(argv)
    try:
        verify(ROOT, game=args.game, checks_only=args.checks_only)
    except (VerificationError, OSError) as exc:
        print("FAIL: %s" % exc, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
