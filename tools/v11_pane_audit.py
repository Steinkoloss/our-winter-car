#!/usr/bin/env python3
"""Bounded rendered native audit of parked Corris windshield (developer-only)."""
from __future__ import annotations
import argparse
from contextlib import nullcontext
from datetime import datetime, timezone
import fcntl
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import signal
import subprocess
import tarfile
import time
import uuid

SOURCE = Path(__file__).resolve().parents[1]
AUTO = SOURCE.parent
RIG = AUTO / 'test-rig'
GAME = RIG / 'game'
ROUND = None  # Explicit --run; importing this tool must not select an old round.
INSTALLED = Path.home() / '.steam/root/steamapps/common/My Winter Car'
PERSONAL = Path.home() / '.steam/root/steamapps/compatdata/4164420/pfx/drive_c/users/steamuser/AppData/LocalLow/Amistech/My Winter Car'
MARKER = None
ACTIVE_NATIVE = None

def utc():
    return datetime.now(timezone.utc).isoformat(timespec='milliseconds')

def boot_stage(text, name):
    lines = [line for line in text.splitlines() if name + ' ' in line]
    if any(name + ' error ' in line for line in lines): return 'managed error'
    if any(name + ' end' in line for line in lines): return 'returned'
    if any(name + ' begin' in line for line in lines): return 'entered without return'
    return 'not entered'

def ready_signal():
    return (GAME / 'WinterMP/hostlocal-ready.flag').is_file()

def configure_run(path):
    global ROUND, MARKER
    path = Path(path)
    if (not path.is_absolute() or path != path.resolve()
            or path.parent != (AUTO / 'rounds').resolve()
            or not path.is_dir() or not (path / 'contract.json').is_file()):
        raise ValueError('--run must be the absolute assigned round directory containing contract.json (no symlinks)')
    ROUND = path
    MARKER = 'V11 parked Corris windshield audit ' + path.name + '\n'

def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b''):
            h.update(chunk)
    return h.hexdigest()

def manifest(root):
    return {str(p.relative_to(root)): digest(p) for p in sorted(root.rglob('*')) if p.is_file()}

def protected():
    return {str(root): manifest(root) for root in (INSTALLED, PERSONAL)}

def save_json(path, value, replace=False):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('w' if replace else 'x') as output:
        output.write(json.dumps(value, indent=2) + '\n')

def run(command, name, env=None, timeout=300):
    with (ROUND / (name + '.log')).open('x') as log:
        log.write('COMMAND ' + repr(command) + '\n'); log.flush()
        result = subprocess.run(command, cwd=SOURCE, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=timeout)
        log.write('\nEXIT ' + str(result.returncode) + '\n')
    if result.returncode:
        raise RuntimeError(name + ' exited ' + str(result.returncode))

def prepare():
    """Claim an already prepared, idle disposable rig for this assigned round."""
    if ROUND is None: raise ValueError('Configure the assigned --run first')
    assert RIG == RIG.resolve() and GAME.resolve().is_relative_to(RIG)
    assert (RIG / 'ready.txt').is_file() and GAME.resolve() != INSTALLED.resolve()
    with (RIG / 'v11.lock').open('a') as lock, (GAME / 'WinterMP/local2p.lock').open('a') as game_lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        fcntl.flock(game_lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        assert not (RIG / 'v11-owner.txt').exists(), 'Another round owns the rig; do not overwrite its evidence'
        assert not (GAME / 'live-bag').exists(), 'Preserve previous command bus before reusing the rig'
        for profile in ('compatdata', 'guest-compatdata'):
            prefix = (RIG / profile / 'pfx').resolve()
            assert prefix.is_relative_to(RIG.resolve())
            save = prefix / 'drive_c/users/steamuser/AppData/LocalLow/Amistech/My Winter Car'
            assert save.is_dir() and all(p.resolve().is_relative_to(prefix) for p in [save, *save.rglob('*')])
        save_json(ROUND / 'protected-before.json', protected())
        with (RIG / 'v11-owner.txt').open('x') as owner: owner.write(MARKER)
    print('PREPARED', ROUND, 'existing isolated rig; no game launched')

def setup(resume=False):
    assert INSTALLED.is_dir() and PERSONAL.is_dir(), 'Protected inputs must exist'
    ROUND.mkdir(parents=True, exist_ok=True)
    if not resume:
        assert not RIG.exists(), 'Preserve existing/incomplete rig; setup is not destructive'
        save_json(ROUND / 'protected-before.json', protected())
        RIG.mkdir()
        (RIG / 'v11-owner.txt').write_text(MARKER)
    else:
        assert (RIG / 'v11-owner.txt').read_text() == MARKER
        assert not (RIG / 'ready.txt').exists()
    with (RIG / 'v11.lock').open('w') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        if resume:
            save_json(ROUND / 'incomplete-setup-manifest.json', manifest(RIG))
            vendor = Path('/home/jaimep/our-winter-car/vendor/BepInEx_win_x64_5.4.23.5.zip')
            run(['unzip', '-oq', str(vendor), '-d', str(GAME)], 'resume-loader')
            config = GAME / 'BepInEx/config'
            config.mkdir(parents=True, exist_ok=True)
            shutil.copy2(SOURCE / 'src/WinterMP.Launcher/Assets/BepInEx.cfg', config / 'BepInEx.cfg')
            (RIG / 'ready.txt').write_text('Isolated local 2P game and save profile.\n')
        env = dict(os.environ, WINTERMP_LOCAL2P_DIR=str(RIG))
        run(['bash', 'tools/install-desktop-local2p.sh', '--no-desktop', '--game-dir', str(INSTALLED)], 'setup-resume' if resume else 'setup', env, 360)
        for profile in [RIG / 'compatdata']:
            prefix = (profile / 'pfx').resolve()
            for low in prefix.glob('drive_c/users/*/AppData/LocalLow'):
                assert low.resolve().is_relative_to(prefix), str(low)
            for save in prefix.glob('drive_c/users/*/AppData/LocalLow/Amistech/My Winter Car'):
                assert all(p.resolve().is_relative_to(prefix) for p in [save, *save.rglob('*')]), str(save)
        shutil.copytree(RIG / 'compatdata', RIG / 'guest-compatdata', symlinks=True)
        for profile in ('compatdata', 'guest-compatdata'):
            save = RIG / profile / 'pfx/drive_c/users/steamuser/AppData/LocalLow/Amistech/My Winter Car'
            (save / 'wintermp-persistence-sandbox.txt').write_text('WinterMP persistence audit 20260913\n')
        (GAME / 'wintermp-live-bag-sandbox.txt').write_text(MARKER)
        (GAME / 'wintermp-v11-pane-sandbox.txt').write_text(MARKER)
        assert protected() == json.loads((ROUND / 'protected-before.json').read_text()), 'Protected inputs changed during setup'
    print('SETUP PASS: copied game and separate prefixes; protected inputs unchanged')

def command(role, *args, timeout=90):
    if ROUND is None: raise ValueError('Configure the assigned --run first')
    assert (RIG / 'v11-owner.txt').read_text() == MARKER, 'Command targets a different round owner'
    if role not in ('host', 'guest'): raise ValueError('Unknown role')
    bus = GAME / 'live-bag'
    bus.mkdir(exist_ok=True)
    old = bus / (role + '-command.txt')
    seq = int(old.read_text().split('\t')[0]) + 1 if old.exists() else 1
    response = bus / (role + '-' + str(seq) + '.txt')
    assert not response.exists(), 'Refuse stale evidence'
    pending = bus / (role + '-pending.txt')
    pending.write_text('\t'.join([str(seq), *args]) + '\n')
    pending.replace(old)
    if ACTIVE_NATIVE: ACTIVE_NATIVE.event('command sent', role=role, sequence=seq, args=args, timeout=timeout)
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if response.exists():
            text = response.read_text()
            if ACTIVE_NATIVE: ACTIVE_NATIVE.event('command response', role=role, sequence=seq, path=str(response), ok=text.startswith('OK|' + str(seq) + '\n'))
            if not text.startswith('OK|' + str(seq) + '\n'):
                raise RuntimeError(text)
            return text
        if ACTIVE_NATIVE:
            for launch, process in zip(ACTIVE_NATIVE.launches, ACTIVE_NATIVE.processes):
                if launch['role'] == role and process.poll() is not None:
                    ACTIVE_NATIVE.event('launcher exited before response', role=role, pid=process.pid, exit_code=process.returncode)
                    raise RuntimeError(role + ' launcher exited ' + str(process.returncode) + ' before ' + str(response))
        time.sleep(.2)
    if ACTIVE_NATIVE: ACTIVE_NATIVE.event('command timeout', role=role, sequence=seq)
    raise TimeoutError(str(response))

class NativeRun:
    def __init__(self, name):
        if ROUND is None: raise ValueError('Configure the assigned --run first')
        if not name or any(c not in 'abcdefghijklmnopqrstuvwxyz0123456789-' for c in name):
            raise ValueError('Use a unique lowercase run name without directory components')
        self.output = ROUND / name
        self.output.mkdir(exist_ok=False)
        self.token = uuid.uuid4().hex
        self.processes = []
        self.logs = []
        self.launches = []

    def __enter__(self):
        global ACTIVE_NATIVE
        self.new_markers = []
        try:
            self.lock = (RIG / 'v11.lock').open('a')
            fcntl.flock(self.lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            self.game_lock = (GAME / 'WinterMP/local2p.lock').open('a')
            fcntl.flock(self.game_lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            assert (RIG / 'v11-owner.txt').read_text() == MARKER
            result = self.prepare_locked()
            ACTIVE_NATIVE = self
            self.event('native locks acquired', owner=MARKER)
            return result
        except BaseException as error:
            # __exit__ is NOT called when __enter__ fails. No game has launched,
            # but locks and any markers created by this attempt still need cleanup.
            for marker in self.new_markers: marker.unlink(missing_ok=True)
            for key in ('game_lock', 'lock'):
                lock = getattr(self, key, None)
                if lock is not None: lock.close()
            save_json(self.output / 'entry-error.json', {'error': str(error), 'game_launched': False})
            raise

    def prepare_locked(self):
        assert not (GAME / 'live-bag').exists(), 'Preserve previous command bus before another run'
        assert not ready_signal(), 'Refuse stale host-ready flag before native launch'
        markers = [(GAME / name, MARKER) for name in ('wintermp-live-bag-sandbox.txt', 'wintermp-v11-pane-sandbox.txt')]
        for profile in ('compatdata', 'guest-compatdata'):
            save = RIG / profile / 'pfx/drive_c/users/steamuser/AppData/LocalLow/Amistech/My Winter Car'
            markers.append((save / 'wintermp-persistence-sandbox.txt', 'WinterMP persistence audit 20260913\n'))
        # Reused rigs must exercise the current source payload, not the binaries
        # left by an older audit. This runs only under both exclusive rig locks.
        payload = GAME / 'BepInEx/plugins/WinterMP'
        files = [(SOURCE / 'src' / project / 'bin/Release/net35' / (project + '.dll'), payload / (project + '.dll'))
                 for project in ('WinterMP.Core', 'WinterMP.Net')]
        files += [(SOURCE / 'catalog/sync-catalog.json', payload / 'sync-catalog.json'),
                  (SOURCE / 'tools/GuestSaveProbe/bin/Release/net35/WinterMP.GuestSaveProbe.dll', GAME / 'BepInEx/plugins/WinterMP.GuestSaveProbe.dll')]
        for source, target in files:
            assert source.is_file(), 'Build current payload before launch: ' + str(source)
            assert target.parent.is_dir() and target.resolve().is_relative_to(RIG.resolve()), str(target)
        for marker, text in markers:
            assert marker.parent.is_dir() and marker.resolve().is_relative_to(RIG.resolve()), str(marker)
            assert not marker.exists() or marker.read_text() == text, 'Preserve foreign marker: ' + str(marker)
        for source, target in files: shutil.copy2(source, target)
        for marker, text in markers:
            if not marker.exists():
                with marker.open('x') as stream: stream.write(text)
                self.new_markers.append(marker)
        save_json(self.output / 'binary-hashes.json', manifest(GAME / 'BepInEx/plugins'))
        save_json(self.output / 'source-hashes.json', {p: digest(SOURCE / p) for p in bridge_sources()})
        return self

    def launch(self, role):
        profile = RIG / ('compatdata' if role == 'host' else 'guest-compatdata')
        assert profile.resolve().is_relative_to(RIG.resolve())
        env = dict(os.environ, WINTERMP_COMPAT_DATA_PATH=str(profile), WINTERMP_GAME_DIR=str(GAME),
                   WINTERMP_PROTON=str(RIG / 'proton/proton'),
                   WINTERMP_LOCAL2P_BAG_TEST='1', WINTERMP_LOCAL2P_PERSIST_TEST='1', WINTERMP_LOCAL2P_V11_PANE_TEST='1',
                   WINTERMP_VIRTUAL_DESKTOP_SIZE='1280x720', WINTERMP_V11_RUN=self.token)
        # Shader/cache outputs are writable only in the disposable rig, not Steam.
        cache = RIG / ('cache-' + self.token)
        cache.mkdir(exist_ok=True)
        env.update(FOSSILIZE_DUMP_PATH=str(cache / 'fossilize'), MESA_SHADER_CACHE_DIR=str(cache / 'mesa'),
                   DXVK_STATE_CACHE_PATH=str(cache), STEAM_COMPAT_SHADER_PATH=str(cache))
        if os.environ.get('WINTERMP_V11_NATIVE_DISPLAY') == '1':
            env.pop('WINTERMP_VIRTUAL_DESKTOP_SIZE', None)
        log = (self.output / (role + '-launch-' + str(len(self.processes)) + '.log')).open('w')
        self.logs.append(log)
        windows_log = 'Z:' + str(GAME / 'BepInEx' / ('v11-' + role + '-unity.log')).replace('/', '\\')
        args = ['bash', '-c', 'source "$1"; shift; run_mwc "$@"', 'v11-audit', str(SOURCE / 'tools/linux-common.sh'),
                role, str(GAME), '-no-dialogs', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
                '-wintermp', 'hostlocal' if role == 'host' else 'joinlocal', '-logFile', windows_log]
        log.write('COMMAND ' + repr(args) + '\n'); log.flush()
        self.processes.append(subprocess.Popen(args, env=env, cwd=GAME, stdout=log, stderr=subprocess.STDOUT, start_new_session=True))
        self.launches.append({'role': role, 'launcher_pid': self.processes[-1].pid, 'prefix': str(profile), 'run_token': self.token, 'command': args, 'utc': utc(),
                              'display': env.get('DISPLAY'), 'runtime': env.get('XDG_RUNTIME_DIR')})
        self.event('native launched', **self.launches[-1])
        save_json(self.output / 'launches.json', self.launches, replace=True)

    def event(self, name, **values):
        with (self.output / 'timeline.jsonl').open('a') as stream:
            stream.write(json.dumps(dict(utc=utc(), monotonic=time.monotonic(), driver_pid=os.getpid(), event=name, values=values)) + '\n')

    def owned(self):
        found = []
        token = ('WINTERMP_V11_RUN=' + self.token).encode()
        for p in Path('/proc').iterdir():
            if not p.name.isdigit(): continue
            try:
                if token in (p / 'environ').read_bytes().split(b'\0'):
                    found.append(int(p.name))
            except (FileNotFoundError, PermissionError, ProcessLookupError): pass
        return found

    def __exit__(self, kind, value, tb):
        global ACTIVE_NATIVE
        cleanup = {'run_token': self.token, 'error': str(value) if value else None}
        self.event('cleanup begin', error=str(value)[:4096] if value else None,
                   launchers=[dict(pid=p.pid, exit_code=p.poll()) for p in self.processes], owned_pids=self.owned())
        for role in ('guest', 'host'):
            try: command(role, 'quit', timeout=5)
            except Exception as error: cleanup[role + '_quit'] = str(error)
        for sig in (signal.SIGTERM, signal.SIGKILL):
            for pid in self.owned():
                try: os.kill(pid, sig)
                except ProcessLookupError: pass
            deadline = time.monotonic() + 5
            while self.owned() and time.monotonic() < deadline: time.sleep(.2)
        for process in self.processes:
            try: process.wait(timeout=5)
            except subprocess.TimeoutExpired: pass
        for log in self.logs: log.close()
        cleanup['owned_processes_remaining'] = self.owned()
        if getattr(self, 'cleanup_display', None): self.cleanup_display()
        cleanup['launcher_exits'] = [dict(role=l['role'], pid=p.pid, exit_code=p.poll()) for l, p in zip(self.launches, self.processes)]
        for directory in ('live-bag', 'BepInEx', 'WinterMP'):
            root = GAME / directory
            dest = self.output / directory
            dest.mkdir(exist_ok=True)
            if not root.exists(): continue
            for p in root.iterdir():
                if p.is_file() and (p.suffix in ('.txt', '.log', '.flag') or p.name == 'hostlocal-ready.flag'):
                    shutil.copy2(p, dest / p.name)
                    p.unlink()
        bus = GAME / 'live-bag'
        if bus.exists(): bus.rmdir()
        for marker in ('wintermp-live-bag-sandbox.txt', 'wintermp-v11-pane-sandbox.txt'):
            (GAME / marker).unlink(missing_ok=True)
        (GAME / 'BepInEx/plugins/WinterMP.GuestSaveProbe.dll').unlink(missing_ok=True)
        for profile in ('compatdata', 'guest-compatdata'):
            save = RIG / profile / 'pfx/drive_c/users/steamuser/AppData/LocalLow/Amistech/My Winter Car'
            (save / 'wintermp-persistence-sandbox.txt').unlink(missing_ok=True)
        cleanup['profile_markers_removed'] = not any(RIG.glob('*/pfx/drive_c/users/*/AppData/LocalLow/Amistech/My Winter Car/wintermp-*-sandbox.txt'))
        cleanup['command_bus_removed'] = not bus.exists()
        cleanup['game_markers_removed'] = not any(GAME.glob('wintermp-*-sandbox.txt'))
        after = protected()
        save_json(self.output / 'protected-after.json', after)
        cleanup['protected_unchanged'] = after == json.loads((ROUND / 'protected-before.json').read_text())
        save_json(self.output / 'cleanup.json', cleanup)
        boot = self.output / 'WinterMP/boot-trace-host.log'
        text = boot.read_text(encoding='utf-8-sig') if boot.exists() else ''
        save_json(self.output / 'boot-stages.json', {name: boot_stage(text, name) for name in
                  ('TryReleaseHostLocalMutex', 'SingleInstanceUnlocker.Release', 'HostLocalReadySignal.MarkReady', 'FinalPluginLog')})
        self.event('cleanup end', **cleanup)
        ACTIVE_NATIVE = None
        self.game_lock.close(); self.lock.close()
        assert not cleanup['owned_processes_remaining'] and cleanup['protected_unchanged'], cleanup

def inspect(name):
    with NativeRun(name) as native:
        native.launch('host')
        print(command('host', 'pane-describe', timeout=180), flush=True)
        native.launch('guest')
        print(command('guest', 'pane-describe', timeout=180), flush=True)
        print('READY: bounded command-bus inspection; create stop.txt to finish', flush=True)
        deadline = time.monotonic() + 900
        while time.monotonic() < deadline and not (native.output / 'stop.txt').exists():
            time.sleep(1)

def runtime():
    assert (RIG / 'v11-owner.txt').read_text() == MARKER
    with (RIG / 'v11.lock').open('w') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        source = INSTALLED.parent / 'Proton - Experimental'
        assert not (RIG / 'proton').exists()
        shutil.copytree(source, RIG / 'proton', symlinks=False)
        save_json(ROUND / 'proton-copy.json', {'source': str(source), 'target': str(RIG / 'proton'),
                                             'proton_sha256': digest(RIG / 'proton/proton')})

PANE = 'CORRIS/BODY/Windshield/collider'
TOLERANCE = .5 / 255 + .000001

def pane_value(text):
    rows = [row.split('|') for row in text.splitlines() if row.startswith('pane|')]
    if len(rows) != 1 or rows[0][1:3] != [PANE, 'CutoffWindshield']:
        raise ValueError('Expected exactly the Corris windshield')
    value = float(rows[0][3])
    if not math.isfinite(value): raise ValueError('Nonfinite pane value')
    return value

def check_denied(host_before, guest_before, transient, host_after, guest_after):
    assert transient > guest_before + .004, 'Native guest stroke did not execute'
    assert abs(host_after - host_before) < .000001, 'Unauthorized guest changed host pane'
    assert abs(guest_after - host_before) <= TOLERANCE, 'Guest was not reconciled to host pane'

def check_authorized(before, after, guest):
    assert after > before + .004, 'Authorized native stroke did not change pane'
    assert abs(guest - after) <= TOLERANCE, 'Replica did not apply the authorized pane'

def bridge_sources():
    return ['src/WinterMP.Core/WinterMPPlugin.cs', 'src/WinterMP.Core/Util/BootTrace.cs',
            'src/WinterMP.Core/Util/SingleInstanceUnlocker.cs', 'src/WinterMP.Core/Util/HostLocalReadySignal.cs',
            'src/WinterMP.Core/Sync/PaneScrapeSync.cs', 'src/WinterMP.Core/Sync/PaneScrapeSync.Bindings.cs',
            'src/WinterMP.Core/Sync/PlayerSyncManager.cs', 'src/WinterMP.Core/Sync/PlayerPoseReader.cs',
            'src/WinterMP.Core/Sync/GuestSpawnRelocator.cs', 'src/WinterMP.Core/UI/GuestSpawnPrompt.cs',
            'src/WinterMP.Core/Session/SessionManager.cs', 'src/WinterMP.Core/Session/SessionManager.Handlers.cs',
            'src/WinterMP.Net/GuestResumePolicy.cs',
            'src/WinterMP.Core/Session/SessionManager.Messages.cs', 'src/WinterMP.Core/Sync/ItemWorldSync.Remote.cs',
            'src/WinterMP.Core/Sync/VehicleWorldSync.Climate.cs', 'src/WinterMP.Core/Catalog/SyncCatalogJson.PaneScrape.cs',
            'src/WinterMP.Net/Messages/PaneScrapeMessages.cs', 'src/WinterMP.Net/Sync/ScraperLease.cs',
            'src/WinterMP.Net/Sync/PaneScrapeAuthority.cs', 'src/WinterMP.Net/Sync/PaneScrapeReplica.cs',
            'src/WinterMP.Net/Sync/PaneScrapePolicy.cs', 'src/WinterMP.Net/Protocol.cs', 'catalog/sync-catalog.json',
            'tools/v11_pane_audit.py', 'tools/v11_rendered.py', 'tools/GuestSaveProbe/LiveBagProbe.PaneTrace.cs',
            'tools/GuestSaveProbe/LiveBagProbe.Pane.cs', 'tools/GuestSaveProbe/LiveBagProbe.cs',
            'tools/GuestSaveProbe/Plugin.cs', 'tools/GuestSaveProbe/GuestSaveProbe.csproj',
            'tools/PaneScrapeBridge.Tests/PaneScrapeBridge.Tests.csproj',
            'tools/PaneScrapeBridge.Tests/ProbeBoundary.cs', 'tools/PaneScrapeBridge.Tests/GuestStrokeWire.cs',
            'tools/PaneScrapeBridge.Tests/BridgeTests.Probe.cs', 'tools/PaneScrapeBridge.Tests/BridgeTests.WrongPane.cs']

def check_bridge(before, after, guest):
    assert all(math.isfinite(v) for v in (before, after, guest))
    assert abs((after - before) - .005) < .000001, 'Expected exactly one native .005 stroke'
    assert abs(guest - after) < .000001, 'Exact absolute result missing (climate bytes are insufficient)'

def await_guest_pose_ready(timeout=40):
    # Connected/no visible offer is also the pre-offer state. Only the production
    # resume state can say that selection AND relocation have really completed.
    deadline = time.monotonic() + timeout
    text = ''
    while time.monotonic() < deadline:
        text = command('guest', 'pane-snapshot')
        if 'pose-local|True|False|False|' in text: return text
        if any(row.startswith('offer|') and row != 'offer|none' for row in text.splitlines()):
            command('guest', 'spawn', 'host')
        time.sleep(.3)
    raise AssertionError('Guest publication not ready: ' + text)

def fresh_guest_pose(text):
    rows = [row.split('|') for row in text.splitlines() if row.startswith('pose-remote|1|')]
    if len(rows) != 1: return False
    row = rows[0]
    timestamp, age = float(row[3]), float(row[4])
    return math.isfinite(timestamp) and timestamp > 0 and math.isfinite(age) and 0 <= age <= .6 and row[5] == 'False'

def observed(text):
    rows = [row.split('|')[1:] for row in text.splitlines() if row.startswith('pane-observed|')]
    assert len(rows) == 1, 'Missing native observation'
    return int(rows[0][0]), int(rows[0][1]), float(rows[0][2])

def check_actor_effects(host, guest):
    hg, he, ht = observed(host)
    gg, ge, gt = observed(guest)
    assert (hg, he, gg, ge) == (1, 0, 0, 1), 'Host-only native glass and actor-only effect required'
    assert math.isfinite(gt) and gt > 0 and ht == 0, 'Native heat delta must belong only to accepted guest'

def readiness(name, existing_native=None):
    """Only the existing bridge prerequisite. Never park/seed/equip/scrape."""
    with (nullcontext(existing_native) if existing_native else NativeRun(name)) as native:
        receipt = dict(started_utc=utc(), host_ready_signal=False, host_command_response=False,
                       guest_command_response=False, fresh_host_observed_guest_pose=False, scraper_gameplay_tested=False)
        try:
            native.launch('host')
            receipt['host_snapshot'] = command('host', 'pane-snapshot', timeout=180)
            receipt['host_command_response'] = True
            receipt['host_ready_signal'] = ready_signal()
            assert receipt['host_ready_signal'], 'Host responded without ready flag'
            native.launch('guest')
            receipt['guest_snapshot'] = command('guest', 'pane-snapshot', timeout=240)
            receipt['guest_command_response'] = True
            snapshot_until('guest', lambda s: 'session|Connected|' in s)
            receipt['guest_resume'] = await_guest_pose_ready()
            receipt['host_pose'] = snapshot_until('host', fresh_guest_pose)
            receipt['fresh_host_observed_guest_pose'] = True
        except BaseException as error:
            receipt['error'] = str(error)[:4096]
            raise
        finally:
            receipt['host_ready_signal'] = ready_signal()
            receipt['ended_utc'] = utc()
            save_json(native.output / 'readiness.json', receipt)
            print(json.dumps(receipt), flush=True)

def bridge(name):
    assertions = []
    with NativeRun(name) as native:
        def passed(label, **values):
            assertions.append(dict(assertion=label, utc=time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()), result='PASS', **values))
            save_json(native.output / 'assertions.json', assertions, replace=len(assertions) > 1)
            print(label, values, flush=True)
        native.launch('host'); command('host', 'pane-snapshot', timeout=180)
        native.launch('guest'); command('guest', 'pane-snapshot', timeout=240)
        snapshot_until('guest', lambda s: 'session|Connected|' in s)
        await_guest_pose_ready()
        host_pose = snapshot_until('host', fresh_guest_pose)
        passed('guest spawn selection/relocation complete and authenticated live transform observed by host', snapshot=host_pose)
        command('host', 'pane-tool-describe')
        command('host', 'pane-fixture-tool')
        snapshot_until('host', lambda s: 'bridge|True|False|' in s and 'session|Hosting|1|0' in s)
        snapshot_until('guest', lambda s: 'bridge|True|False|' in s and 'session|Connected|' in s)
        passed('both rendered peers bind production pane/equipment adapter')
        command('host', 'pane-park'); command('guest', 'pane-park'); command('host', 'pane-seed')
        snapshot_until('guest', lambda s: abs(pane_value(s) - .25) < .000001)
        command('guest', 'pane-stroke-bridge')
        h = command('host', 'pane-snapshot'); g = command('guest', 'pane-snapshot')
        assert abs(pane_value(h) - .25) < .000001 and abs(pane_value(g) - .25) < .000001
        passed('native guest no-tool stroke suppressed before additive mutation')
        command('guest', 'pane-aim-tool')
        # Two sampled snapshots allow the ordinary player transform stream to catch up.
        command('host', 'pane-snapshot'); command('guest', 'pane-snapshot')
        command('guest', 'pane-pickup-tool')
        snapshot_until('host', lambda s: 'lease|1|False' in s, 15)
        command('guest', 'pane-release-aim'); command('guest', 'pane-equip-tool')
        snapshot_until('host', lambda s: 'lease|1|True' in s, 15)
        passed('real shared PickedObject pickup and SCRAPER_ON establish host exclusive equipped lease')
        command('guest', 'pane-aim-glass')
        command('host', 'pane-snapshot'); command('guest', 'pane-snapshot')
        before = pane_value(command('host', 'pane-snapshot'))
        command('guest', 'pane-stroke-bridge')
        h = snapshot_until('host', lambda s: pane_value(s) > before + .004, 15)
        g = snapshot_until('guest', lambda s: abs(pane_value(s) - pane_value(h)) < .000001, 15)
        check_bridge(before, pane_value(h), pane_value(g))
        passed('authorized native guest stroke changes host once and exact replica converges', before=before, host=pane_value(h), guest=pane_value(g))
        check_actor_effects(h, g)
        context = next(row.split('|') for row in h.splitlines() if row.startswith('pane-context|'))
        assert context[1:4] == ['True', 'True', 'True'] and float(context[4]) <= .6
        assert context[5:8] == ['1', 'True', 'True'] and 0 <= float(context[8]) <= .8 and context[9] == 'True'
        passed('host first-hit .8m contact; host-only glass and guest-only native sound/heat method', host=h, guest=g)
        command('guest', 'pane-replay-stroke')
        h = snapshot_until('host', lambda s: '|ReplayedSequence|' in s, 15)
        g = command('guest', 'pane-snapshot')
        assert abs(pane_value(h) - (before + .005)) < .000001 and abs(pane_value(g) - pane_value(h)) < .000001
        check_actor_effects(h, g)
        passed('duplicate real outgoing stroke rejected with unchanged canonical panes/effects', host=h, guest=g)
        command('guest', 'pane-aim-far')
        snapshot_until('host', fresh_guest_pose)
        command('guest', 'pane-snapshot'); command('host', 'pane-snapshot')
        command('guest', 'pane-stroke-bridge')
        h = snapshot_until('host', lambda s: '|InvalidContact|' in s, 15)
        g = command('guest', 'pane-snapshot')
        assert abs(pane_value(h) - (before + .005)) < .000001 and abs(pane_value(g) - pane_value(h)) < .000001
        check_actor_effects(h, g)
        decision = next(row.split('|') for row in h.splitlines() if row.startswith('pane-decision|'))
        assert int(decision[3]) >= int(decision[2]) > 0, 'Denied valid-epoch stroke did not consume high-water'
        passed('beyond .8m contact denied and valid-epoch sequence consumed; panes/effects unchanged', host=h, guest=g)
        command('guest', 'pane-replay-stroke')
        h = snapshot_until('host', lambda s: '|ReplayedSequence|' in s, 15)
        g = command('guest', 'pane-snapshot')
        assert abs(pane_value(h) - (before + .005)) < .000001 and abs(pane_value(g) - pane_value(h)) < .000001
        check_actor_effects(h, g)
        passed('denied contact cannot be retried with its consumed sequence', host=h, guest=g)
        command('guest', 'pane-release-aim')
    print('BRIDGE fixture only; ordinary input, rejoin/cold-load and Steam acceptance NOT TESTED', flush=True)

def snapshot_until(role, predicate, timeout=30):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        text = command(role, 'pane-snapshot')
        if predicate(text): return text
        time.sleep(.3)
    raise AssertionError('Timed out waiting for ' + role + ': ' + text)

def audit(name):
    assertions = []
    def passed(name, **values):
        assertions.append(dict(assertion=name, result='PASS', utc=time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()), **values))
        save_json(ROUND / (name_root + '-assertions.json'), assertions, replace=len(assertions) > 1)
        print(name, values, flush=True)
    name_root = name
    with NativeRun(name + '-live') as native:
        native.launch('host')
        command('host', 'pane-describe', timeout=180)
        native.launch('guest')
        command('guest', 'pane-describe', timeout=240)
        h = snapshot_until('host', lambda s: 'session|Hosting|1|0' in s)
        g = snapshot_until('guest', lambda s: 'session|Connected|' in s)
        assert 'ownership|2784793521|False|255' in h and 'publish|True' in h
        assert 'ownership|2784793521|False|255' in g and 'publish|False' in g
        passed('parked host publication and guest non-owner confirmed')
        snapshot_until('host', lambda s: 'player-ready|True' in s)
        snapshot_until('guest', lambda s: 'player-ready|True' in s)
        command('host', 'pane-park'); command('guest', 'pane-park')
        contact = command('host', 'pane-contact')
        assert 'contact|near|True|' in contact and 'contact|far|False|' in contact, contact
        passed('native target collider near hit and far miss; camera input NOT TESTED')
        command('host', 'pane-seed')
        g = snapshot_until('guest', lambda s: abs(pane_value(s) - .25) <= TOLERANCE)
        h = command('host', 'pane-snapshot')
        assert float(next(l.split('|')[1] for l in h.splitlines() if l.startswith('motion|'))) < .001
        host_before, guest_before = pane_value(h), pane_value(g)
        stroke = command('guest', 'pane-scrape')
        transient = float(next(l.split('|')[2] for l in stroke.splitlines() if l.startswith('stroke|')))
        g = snapshot_until('guest', lambda s: abs(pane_value(s) - host_before) <= TOLERANCE)
        host_after = pane_value(command('host', 'pane-snapshot'))
        check_denied(host_before, guest_before, transient, host_after, pane_value(g))
        passed('pre-fix negative guest stroke executes locally but is overwritten; no host scrape intent exists',
               host_before=host_before, guest_before=guest_before, guest_transient=transient, host_after=host_after, guest_after=pane_value(g))
        h = command('host', 'pane-scrape'); host_after = pane_value(h)
        g = snapshot_until('guest', lambda s: abs(pane_value(s) - host_after) <= TOLERANCE)
        check_authorized(host_before, host_after, pane_value(g))
        passed('authorized host native tool-stroke changes pane and replica', before=host_before, host=host_after, guest=pane_value(g))
        command('guest', 'disconnect')
        snapshot_until('host', lambda s: 'session|Hosting|0|0' in s)
        h = command('host', 'pane-scrape'); late = pane_value(h)
        assert late > host_after + .004
        command('guest', 'join')
        g = snapshot_until('guest', lambda s: 'session|Connected|' in s, 60)
        if 'offer|none' not in g: command('guest', 'spawn', 'host')
        g = snapshot_until('guest', lambda s: abs(pane_value(s) - late) <= TOLERANCE, 60)
        passed('same guest rejoin receives stroke made while absent', host=late, guest=pane_value(g))
        save = RIG / 'compatdata/pfx/drive_c/users/steamuser/AppData/LocalLow/Amistech/My Winter Car'
        before_files = manifest(save)
        command('host', 'save-description')
        command('host', 'save')
        snapshot_until('host', lambda s: 'level|GAME\n' not in s, 90)
        after_files = manifest(save)
        changed = [key for key, value in after_files.items() if before_files.get(key) != value]
        assert changed, 'Native save wrote no profile files'
        save_json(native.output / 'native-save.json', dict(before=before_files, after=after_files, changed=changed, pane_before_save=late))
        passed('native host save completed to menu and changed copied save files', files=changed)
    with NativeRun(name + '-reload') as native:
        native.launch('host')
        h = command('host', 'pane-describe', timeout=180)
        loaded = pane_value(h)
        native.launch('guest')
        command('guest', 'pane-describe', timeout=240)
        g = snapshot_until('guest', lambda s: 'session|Connected|' in s, 60)
        if 'offer|none' not in g: command('guest', 'spawn', 'host')
        g = snapshot_until('guest', lambda s: abs(pane_value(s) - loaded) <= TOLERANCE)
        passed('cold reload pane visibility observed and joining guest converges', before_save=late, loaded=loaded,
               guest=pane_value(g), scrape_persisted=abs(loaded - late) <= TOLERANCE)
    print('AUDIT COMPLETE: findings only, not a guest scraping implementation or ordinary-input acceptance', flush=True)

def finalize(name):
    assertions = json.loads((ROUND / (name + '-assertions.json')).read_text())
    assert len(assertions) == 7 and all(a['result'] == 'PASS' for a in assertions)
    for phase in ('live', 'reload'):
        cleanup = json.loads((ROUND / (name + '-' + phase) / 'cleanup.json').read_text())
        assert cleanup['error'] is None and cleanup['owned_processes_remaining'] == []
        assert all(cleanup[key] for key in ('profile_markers_removed', 'command_bus_removed', 'game_markers_removed', 'protected_unchanged'))
    assert (RIG / 'v11-owner.txt').read_text() == MARKER
    with (RIG / 'v11.lock').open('w') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        assert protected() == json.loads((ROUND / 'protected-before.json').read_text())
        assert not (GAME / 'live-bag').exists()
        (RIG / 'v11-owner.txt').unlink()
        evidence = ['tools/v11_pane_audit.py', 'tools/tests/test_v11_pane_audit.py',
                    'tools/GuestSaveProbe/LiveBagProbe.Pane.cs', 'tools/GuestSaveProbe/LiveBagProbe.cs',
                    'docs/V11-PANE-AUDIT.md', 'catalog/sync-catalog.json', 'src/WinterMP.Net/Protocol.cs',
                    'src/WinterMP.Core/bin/Release/net35/WinterMP.Core.dll',
                    'src/WinterMP.Net/bin/Release/net35/WinterMP.Net.dll',
                    'tools/GuestSaveProbe/bin/Release/net35/WinterMP.GuestSaveProbe.dll']
        save_json(ROUND / (name + '-final-receipt.json'), {'audit': name, 'evidence_level': 'rendered native injected-entry two-peer UDP',
                  'temporary_owner_marker_removed': not (RIG / 'v11-owner.txt').exists(),
                  'protected_inputs_unchanged': True, 'hashes': {p: digest(SOURCE / p) for p in evidence}})
    output = ROUND / (name + '-v11-corris-windshield-audit.tar.gz')
    assert not output.exists(), 'Preserve existing final artifact'
    with tarfile.open(output, 'w:gz') as bundle:
        for entry in ROUND.iterdir():
            if entry != output: bundle.add(entry, arcname='evidence/' + entry.name)
        bundle.add(SOURCE / 'docs/V11-PANE-AUDIT.md', arcname='REPORT.md')
        for path in evidence:
            if path.startswith('tools/') and not '/bin/' in path:
                bundle.add(SOURCE / path, arcname='source/' + path)
    with tarfile.open(output) as bundle:
        receipt = name + '-final-receipt.json'
        assert bundle.getmember('REPORT.md') and bundle.getmember('evidence/' + receipt)
        assert bundle.extractfile('evidence/' + receipt).read() == (ROUND / receipt).read_bytes()
    print('FINAL_ARTIFACT', output, 'SHA256', digest(output), 'BYTES', output.stat().st_size)

def interrupted(signum, frame):
    raise TimeoutError('Bounded native audit interrupted by signal ' + str(signum))

AUDITED_PANE_BINDING = dict(
    vehicle='CORRIS', panePath=PANE, paneFsm='Scrape',
    freezingPath='CORRIS/Simulation/CarTempCorris', freezingFsm='Freezing',
    cutoff='CutoffWindshield', deltaState='State 7', strokeState='Scrape 2', strokeEvent='WINDSHIELD',
    toolName='ice scraper(itemx)', handPath='PLAYER/Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand',
    handFsm='PickUp', picked='PickedObject', pickup='Set pivot 2', equip='Ice Scraper',
    off='Off', drop='Drop part', throw='Drop part 2',
    eyePath='PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Camera/Camera',
    insidePath='CORRIS/Functions/PlayerTrigger')


def discover_pane(catalog, dump):
    """Select identities, not live instances or native action/value evidence."""
    c = catalog['paneScrape']
    if any(c.get(key) != value for key, value in AUDITED_PANE_BINDING.items()):
        raise ValueError('Binding exceeds the audited one-pane scope')
    targets = dict(pane=(c['panePath'], c['paneFsm']), freezing=(c['freezingPath'], c['freezingFsm']),
                   interior_frost=(c['freezingPath'], 'GlassFrosting'), hand=(c['handPath'], c['handFsm']),
                   inside=(c['insidePath'], 'PlayerTrigger'), tool_factory=('Spawner/CreateItems', 'IceScraper'))
    selected = {}
    for key, (path, fsm) in targets.items():
        rows = [f for f in dump['fsms'] if f.get('path') == path and f.get('fsmName') == fsm]
        if len(rows) != 1:
            raise ValueError('Expected exactly one ' + path + '::' + fsm + '; found ' + str(len(rows)))
        selected[key] = rows[0]
    return dict(evidence_level='static catalog identity inspection, NOT native execution',
                native_executed=False, binding=c, dump_meta=dump.get('meta'), fsms=selected,
                global_transitions_available=all('globalTransitions' in f for f in selected.values()),
                # FsmDumper emits names only (actionTypes since 0.2.0), not a
                # native field schema. Only explicit empty actions are knowable;
                # a nonempty/unversioned object must not masquerade as field proof.
                action_fields_available=all(f.get('states') and all(s.get('actions') == [] for s in f['states'])
                                            for f in selected.values()),
                native_persistence='UNKNOWN', live_tool_instance='UNKNOWN',
                limits='FSM names/states/variables are not action parameters, physical contact, or save semantics. '
                       'A factory binding does not prove a shared live scraper exists. Missing metadata is unknown, not absent behavior.')


def static_check(name, baseline):
    """Read-only fallback. Does not prepare/rebaseline/deploy/launch/release a rig."""
    from v11_safety_receipt import differences, stability_receipt
    if ROUND is None: raise ValueError('Configure the assigned --run first')
    if not name or any(c not in 'abcdefghijklmnopqrstuvwxyz0123456789-' for c in name):
        raise ValueError('Use a unique lowercase output name without directory components')
    baseline = Path(baseline)
    if not baseline.is_absolute() or not baseline.is_file():
        raise ValueError('Pass the existing original protected-input baseline explicitly')
    output = ROUND / name
    output.mkdir(exist_ok=False)
    receipt = dict(started_utc=utc(), evidence_level='static discovery and read-only protected-input comparison',
                   baseline=str(baseline), native_launched=False, native_launch_allowed=False,
                   owned_processes_started=[], owned_processes_remaining=[], signals_sent=[],
                   rig_writes_performed=False, both_locks_acquired=False)
    try:
        baseline_hash = digest(baseline)
        before = json.loads(baseline.read_text())
        assert set(before) == {str(INSTALLED), str(PERSONAL)}, 'Unexpected protected roots'
        assert INSTALLED.is_dir() and PERSONAL.is_dir(), 'Protected inputs must exist'
        # Existing locks opened read-only: never create markers or replace the old baseline.
        with (RIG / 'v11.lock').open('rb') as lock, (GAME / 'WinterMP/local2p.lock').open('rb') as game_lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            fcntl.flock(game_lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            receipt['both_locks_acquired'] = True
            current = protected()
            save_json(output / 'protected-observation-before.json', current)
            delta = differences(before, current)
            save_json(output / 'original-baseline-differences.json', delta)
            stability = {str(Path(root) / path): stability_receipt(Path(root) / path)
                         for root, files in delta.items() for path in files}
            save_json(output / 'differing-input-stability.json', stability)

            catalog = json.loads((SOURCE / 'catalog/sync-catalog.json').read_text())
            dump = json.loads((SOURCE / 'catalog/dump-23268598.json').read_text())
            discovery = discover_pane(catalog, dump)
            save_json(output / 'discovery.json', discovery)
            f = discovery['fsms']
            checks = []
            def check(label, condition):
                checks.append(dict(assertion=label, passed=bool(condition)))
            def variable(key, kind, name):
                return name in f[key].get('variables', {}).get(kind + 'Variables', [])
            def state(key, name):
                return any(s['name'] == name for s in f[key].get('states', []))
            check('pane contact variable and alternating native states declared',
                  variable('pane', 'Float', 'Distance') and state('pane', 'Scrape 1') and state('pane', 'Scrape 2'))
            check('exterior cutoff, delta, material slot and reset/roof states declared',
                  all(variable('freezing', 'Float', v) for v in ('CutoffWindshield', 'ScrapeEfficiency', 'BodyTempAdd'))
                  and variable('freezing', 'Material', '6')
                  and all(state('freezing', s) for s in ('State 7', 'Sound', 'Check roof', 'Delay'))
                  and 'FREEZE' in f['freezing']['events'])
            check('interior Frost is a distinct FSM/material channel',
                  variable('interior_frost', 'Float', 'Frost') and variable('interior_frost', 'Material', 'FrostGlass'))
            check('native hand selected-object/equip and outside-player gate declared',
                  variable('hand', 'GameObject', 'PickedObject') and state('hand', 'Ice Scraper')
                  and state('hand', 'Off') and variable('inside', 'Bool', 'PlayerInside'))

            # Textual anchors are reproducible routing inspection, NOT execution or a C# proof.
            anchors = {
                'src/WinterMP.Net/VehicleStateStreamPolicy.cs': ['=> locallyOwned || (isHost && remoteOwner == NoOwner);'],
                'src/WinterMP.Core/Session/SessionManager.Messages.cs': ['case ScraperAction scraper when IsHost:',
                    'Sync.PaneScrapeSync.Instance?.OnAction(scraper, scraperActor);', 'case PaneScrapeUpdate paneUpdate when !IsHost:'],
                'src/WinterMP.Net/SessionMessagePolicy.cs': ['messageId == MessageId.ScraperAction',
                    'return receiverIsHost && senderIsAuthenticated;', 'return !receiverIsHost && senderIsSelectedHost && receiverHandshakeComplete;'],
                'src/WinterMP.Core/Sync/PaneScrapeSync.Bindings.cs': ['if (_owner.Active) _owner.Send(ScraperOperation.Stroke);',
                    'replacement[eventIndex] = new StrokeAction', 'delta.Actions.Length != 2',
                    '"floatVariable")?.Name != "PlayerTemp"', 'foreach (var action in _soundActions!) action.OnEnter();'],
                'src/WinterMP.Core/Sync/PaneScrapeSync.cs': ['_authority.Decide(actor, action.Intent())',
                    'Session.SendWorldMessage(request, Channel.ReliableOrdered);', '=> Physics.Raycast(',
                    'age > .6f', 'hit.collider == _pane', 'VehicleParked =',
                    '_glassActions![0].OnEnter(); _glassActions[1].OnEnter();',
                    '_cutoff.Value = _replica.Current.Cutoff;', 'ResumePersonalEffectsAfter(update.HighWater)'],
                'src/WinterMP.Net/Sync/PaneScrapeAuthority.cs': ['intent.Actor != authenticatedActor',
                    '_seen[authenticatedActor] = intent.Sequence;', 'intent.Pane != PaneScrapeIntent.Windshield',
                    '_host.ApplyWindshieldDelta();', 'float cutoff = _host.ReadWindshieldCutoff();'],
                'src/WinterMP.Core/Sync/VehicleWorldSync.Climate.cs': ['if (PaneScrapeSync.Instance?.OwnsPane(item.Id) != true)',
                    'WriteCutoff(item.CutoffWindshieldVar,', 'ApplyRemoteFrostLevel(item, frost);'],
            }
            extracts = {}
            for path, fragments in anchors.items():
                lines = (SOURCE / path).read_text().splitlines()
                extracts[path] = []
                for fragment in fragments:
                    matches = [dict(line=i + 1, text=line) for i, line in enumerate(lines) if fragment in line]
                    extracts[path].append(dict(fragment=fragment, matches=matches))
                    check('source anchor: ' + path + ' / ' + fragment, bool(matches))
            save_json(output / 'source-anchors.json', extracts)
            save_json(output / 'static-assertions.json', dict(level='text/catalog assertions only', assertions=checks))
            paths = sorted(set(bridge_sources() + list(anchors) + ['catalog/dump-23268598.json',
                           'tools/v11_safety_receipt.py', 'tools/tests/test_v11_pane_static.py']))
            save_json(output / 'source-hashes.json', {p: digest(SOURCE / p) for p in paths})
            binaries = ['src/' + p + '/bin/Release/net35/' + p + '.dll' for p in ('WinterMP.Core', 'WinterMP.Net')]
            save_json(output / 'existing-binary-hashes-not-deployed.json',
                      {p: digest(SOURCE / p) if (SOURCE / p).is_file() else None for p in binaries})
            after = protected()
            save_json(output / 'protected-observation-after.json', after)
            receipt.update(baseline_sha256=baseline_hash, baseline_file_unchanged=digest(baseline) == baseline_hash,
                           protected_matches_original=before == after, observation_unchanged=current == after,
                           protected_gate='BLOCKED: differs from original; stable reads do not reconcile provenance' if before != after
                           else 'Comparison matches; this static audit does not authorize native launch',
                           static_assertions_passed=all(c['passed'] for c in checks), static_assertion_count=len(checks),
                           owner_marker_present=(RIG / 'v11-owner.txt').exists(), command_bus_present=(GAME / 'live-bag').exists(),
                           game_markers=[str(p) for p in GAME.glob('wintermp-*-sandbox.txt')],
                           profile_markers=[str(p) for p in RIG.glob('*/pfx/drive_c/users/*/AppData/LocalLow/Amistech/My Winter Car/wintermp-*-sandbox.txt')])
            assert receipt['static_assertions_passed'], 'Static discovery/routing changed; inspect assertion receipt'
            assert receipt['observation_unchanged'] and receipt['baseline_file_unchanged'], 'Inputs changed during read-only audit'
    except BaseException as error:
        receipt['error'] = str(error)
        raise
    finally:
        receipt['ended_utc'] = utc()
        save_json(output / 'receipt.json', receipt)
        print(json.dumps(receipt, indent=2), flush=True)
    return receipt

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, required=True, help='Absolute assigned autonomous/rounds/<round>-work directory')
    parser.add_argument('phase', choices=['setup', 'setup-resume', 'prepare', 'runtime', 'inspect', 'command', 'audit', 'bridge', 'readiness', 'finalize', 'static'])
    parser.add_argument('extra', nargs='*')
    args = parser.parse_args()
    configure_run(args.run)
    signal.signal(signal.SIGTERM, interrupted)
    signal.signal(signal.SIGALRM, interrupted)
    signal.alarm(600 if args.phase in ('bridge', 'readiness') else 780 if args.phase == 'audit' else 1150)
    if args.phase.startswith('setup'): setup(args.phase == 'setup-resume')
    elif args.phase == 'prepare': prepare()
    elif args.phase == 'inspect': inspect(args.extra[0])
    elif args.phase == 'runtime': runtime()
    elif args.phase == 'audit': audit(args.extra[0])
    elif args.phase == 'bridge': bridge(args.extra[0])
    elif args.phase == 'readiness': readiness(args.extra[0])
    elif args.phase == 'static':
        if len(args.extra) != 2: parser.error('static requires a unique name and the original protected-before.json path')
        receipt = static_check(args.extra[0], args.extra[1])
        raise SystemExit(0 if receipt['protected_matches_original'] else 1)
    elif args.phase == 'finalize': finalize(args.extra[0])
    else: print(command(*args.extra))
