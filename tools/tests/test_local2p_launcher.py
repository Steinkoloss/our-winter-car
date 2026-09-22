"""Hermetic checks for the Linux local two-player process launcher."""
import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import time
import unittest

TOOLS = Path(__file__).resolve().parents[1]


class Local2PTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="wintermp-local2p-")
        self.root = Path(self.temp.name)
        self.game = self.root / "Game with spaces"
        (self.game / "BepInEx/plugins/WinterMP").mkdir(parents=True)
        (self.game / "BepInEx/plugins/WinterMP/WinterMP.Core.dll").touch()
        (self.game / "mywintercar.exe").touch()
        (self.game / "mywintercar_Data").mkdir()
        data = bytearray(8192)
        data[:22] = b"Amistech My Winter Car!"
        struct.pack_into("<i", data, 4224, 0)
        (self.game / "mywintercar_Data/mainData").write_bytes(data)
        self.steam = self.root / "Steam with spaces"
        (self.steam / "steamapps/common").mkdir(parents=True)
        self.proton = self.root / "Fake Proton"
        self.proton.write_text('''#!/usr/bin/env python3
import json, os, pathlib, sys, time
root = pathlib.Path(os.environ["PROBE_ROOT"])
role = os.environ["WINTERMP_LOG_ROLE"]
print("Proton startup output must not become the PID", flush=True)
if role == "host" and os.environ.get("FAIL_HOST") == "1": sys.exit(4)
(root / (role + ".json")).write_text(json.dumps({"args":sys.argv[1:], "prefix":os.environ["STEAM_COMPAT_DATA_PATH"]}))
if role == "host":
    ready = pathlib.Path.cwd() / "WinterMP/hostlocal-ready.flag"
    ready.write_text("ready")
deadline = time.monotonic() + 8
while not (root / "stop").exists() and time.monotonic() < deadline: time.sleep(.05)
''')
        self.proton.chmod(0o755)
        self.bin = self.root / "bin"
        self.bin.mkdir()
        xrandr = self.bin / "xrandr"
        xrandr.write_text('''#!/usr/bin/env python3
import os, sys
if os.environ.get("PROBE_XRANDR_FAIL") == "1": sys.exit(1)
print("Monitors: " + os.environ.get("PROBE_MONITORS", "1"))
''')
        xrandr.chmod(0o755)
        self.env = dict(os.environ, STEAM_DIR=str(self.steam), WINTERMP_GAME_DIR=str(self.game),
                        WINTERMP_PROTON=str(self.proton), WINTERMP_COMPAT_DATA_PATH=str(self.root / "test prefix"),
                        WINTERMP_LOCAL2P_WAIT="2", PROBE_ROOT=str(self.root),
                        PATH=str(self.bin) + os.pathsep + os.environ["PATH"],
                        WINTERMP_LOCAL2P_DESKTOP="auto", WINTERMP_LOCAL2P_HEADLESS="0", WINTERMP_LOCAL2P_PROFILE="0")

    def tearDown(self):
        (self.root / "stop").touch()
        time.sleep(.15)
        self.temp.cleanup()

    def launch(self):
        return subprocess.run(["bash", str(TOOLS / "local2p-test.sh")], env=self.env,
                              capture_output=True, text=True, timeout=4)

    def test_host_and_guest_start_before_host_exits_and_use_the_isolated_prefix(self):
        self.env["WINTERMP_LOCAL2P_HEADLESS"] = "1"
        process = subprocess.Popen(["bash", str(TOOLS / "local2p-test.sh")], env=self.env,
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            deadline = time.monotonic() + 3
            while not (self.root / "guest.json").exists() and time.monotonic() < deadline: time.sleep(.02)
            for role, mode in [("host", "hostlocal"), ("guest", "joinlocal")]:
                row = json.loads((self.root / (role + ".json")).read_text())
                self.assertIn(mode, row["args"])
                self.assertIn("-batchmode", row["args"])
                self.assertNotIn("--wintermp-live-performance-probe", row["args"])
                self.assertNotIn("--wintermp-live-performance-frames-only", row["args"])
                self.assertNotIn("--wintermp-live-performance-heap", row["args"])
                self.assertEqual(row["prefix"], self.env["WINTERMP_COMPAT_DATA_PATH"])
            self.assertIsNone(process.poll(), "Launcher must stay alive while its games are running.")
            duplicate = self.launch()
            self.assertNotEqual(duplicate.returncode, 0)
            self.assertIn("already running", duplicate.stderr)
        finally:
            (self.root / "stop").touch()
            stdout, stderr = process.communicate(timeout=3)
        self.assertEqual(process.returncode, 0, stdout + stderr)
        self.assertNotIn("Proton startup output", stdout)

    def test_early_host_failure_is_reported_without_starting_a_guest(self):
        self.env["FAIL_HOST"] = "1"
        result = self.launch()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Host instance exited", result.stdout)
        self.assertFalse((self.root / "guest.json").exists())

    def check_display_launch(self, virtual, headless=False, profile="1"):
        for name in ["stop", "host.json", "guest.json"]:
            (self.root / name).unlink(missing_ok=True)
        self.env["WINTERMP_LOCAL2P_PROFILE"] = profile
        process = subprocess.Popen(["bash", str(TOOLS / "local2p-test.sh")], env=self.env,
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            deadline = time.monotonic() + 3
            while not (self.root / "guest.json").exists() and time.monotonic() < deadline: time.sleep(.02)
            for role, mode in [("host", "hostlocal"), ("guest", "joinlocal")]:
                row = json.loads((self.root / (role + ".json")).read_text())
                if virtual:
                    self.assertEqual(row["args"][:4], ["run", "explorer.exe", "/desktop=WinterMP-" + role + ",1280x720",
                                     "Z:" + str(self.game).replace("/", "\\") + "\\mywintercar.exe"])
                else:
                    self.assertEqual(row["args"][:2], ["run", "./mywintercar.exe"])
                self.assertIn(mode, row["args"])
                self.assertEqual("--wintermp-live-performance-probe" in row["args"], profile != "0")
                self.assertEqual("--wintermp-live-performance-frames-only" in row["args"], profile == "frames")
                self.assertEqual("--wintermp-live-performance-heap" in row["args"], profile == "heap")
                self.assertEqual("-batchmode" in row["args"], headless)
                self.assertEqual(row["prefix"], self.env["WINTERMP_COMPAT_DATA_PATH"])
            self.assertIsNone(process.poll())
        finally:
            (self.root / "stop").touch()
            stdout, stderr = process.communicate(timeout=3)
        self.assertEqual(process.returncode, 0, stdout + stderr)

    def test_zero_monitors_uses_separate_virtual_desktops_and_preserves_arguments(self):
        self.env["PROBE_MONITORS"] = "0"
        self.check_display_launch(virtual=True)
        self.env["WINTERMP_GAME_DIR"] = os.path.relpath(self.game)
        self.check_display_launch(virtual=True)

    def test_frame_only_profile_reaches_both_games(self):
        self.check_display_launch(virtual=False, profile="frames")

    def test_heap_profile_reaches_both_games(self):
        self.check_display_launch(virtual=False, profile="heap")

    def test_invalid_profile_starts_neither_game(self):
        self.env["WINTERMP_LOCAL2P_PROFILE"] = "invalid"
        result = self.launch()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Invalid local profile mode", result.stderr)
        self.assertFalse((self.root / "host.json").exists())
        self.assertFalse((self.root / "guest.json").exists())

    def test_a_connected_monitor_or_failed_detection_keeps_direct_launch(self):
        self.check_display_launch(virtual=False)
        self.env["PROBE_XRANDR_FAIL"] = "1"
        self.check_display_launch(virtual=False)

    def test_explicit_desktop_choice_and_headless_override(self):
        self.env.update(PROBE_MONITORS="0", WINTERMP_LOCAL2P_DESKTOP="0")
        self.check_display_launch(virtual=False)
        self.env.update(PROBE_MONITORS="1", WINTERMP_LOCAL2P_DESKTOP="1")
        self.check_display_launch(virtual=True)
        self.env["WINTERMP_LOCAL2P_HEADLESS"] = "1"
        self.check_display_launch(virtual=False, headless=True)

    def test_invalid_desktop_mode_starts_neither_game(self):
        self.env["WINTERMP_LOCAL2P_DESKTOP"] = "invalid"
        result = self.launch()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Invalid local desktop mode", result.stderr)
        self.assertFalse((self.root / "host.json").exists())
        self.assertFalse((self.root / "guest.json").exists())

    def test_numbered_proton_fallback_uses_newest_and_keeps_spaces(self):
        self.env.pop("WINTERMP_PROTON")
        for version in ["Proton 9.0", "Proton 11.0", "Proton - 10.0"]:
            path = self.steam / "steamapps/common" / version / "proton"
            path.parent.mkdir()
            path.write_text("#!/bin/sh\nexit 0\n")
            path.chmod(0o755)
        result = subprocess.run(["bash", "-c", 'source "$1"; find_proton "$2"', "bash",
                                 str(TOOLS / "linux-common.sh"), str(self.steam)],
                                env=self.env, capture_output=True, text=True, check=True)
        self.assertEqual(result.stdout.strip(), str(self.steam / "steamapps/common/Proton 11.0/proton"))


if __name__ == "__main__":
    unittest.main()
