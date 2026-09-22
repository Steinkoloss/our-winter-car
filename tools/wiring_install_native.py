#!/usr/bin/env python3
"""Bounded, opt-in ignition-wire native fixture in the existing disposable rig.

Uses the existing lock/protection/scoped-cleanup harness. Selection and positioning
are injected fixture input, NOT ordinary input, Steam or different-save acceptance.
"""
import argparse
import json
import os
from pathlib import Path
import signal
import shutil
import subprocess
import time
import v11_pane_audit as audit


class WiringRun(audit.NativeRun):
    def prepare_locked(self):
        self.crashes_before = {p.name for p in audit.GAME.iterdir() if p.is_dir() and (p / 'error.log').is_file()}
        return super().prepare_locked()

    def __exit__(self, kind, value, tb):
        try:
            return super().__exit__(kind, value, tb)
        finally:
            crashes = {}
            for path in audit.GAME.iterdir():
                if not path.is_dir() or path.name in self.crashes_before or not (path / 'error.log').is_file(): continue
                target = self.output / 'crashes' / path.name
                shutil.copytree(path, target)
                crashes[path.name] = audit.manifest(target)
            audit.save_json(self.output / 'crash-hashes.json', crashes)

    def launch(self, role):
        profile = audit.RIG / ('compatdata' if role == 'host' else 'guest-compatdata')
        assert profile.resolve().is_relative_to(audit.RIG.resolve())
        env = dict(os.environ, WINTERMP_COMPAT_DATA_PATH=str(profile), WINTERMP_GAME_DIR=str(audit.GAME),
                   WINTERMP_PROTON=str(audit.RIG / 'proton/proton'), WINTERMP_LOCAL2P_BAG_TEST='1',
                   WINTERMP_LOCAL2P_PERSIST_TEST='1', WINTERMP_LOCAL2P_WIRING_TEST='1',
                   WINTERMP_VIRTUAL_DESKTOP_SIZE='1280x720', WINTERMP_V11_RUN=self.token)
        env.pop('WINTERMP_LOCAL2P_V11_PANE_TEST', None)
        cache = audit.RIG / ('cache-' + self.token)
        cache.mkdir(exist_ok=True)
        env.update(FOSSILIZE_DUMP_PATH=str(cache / 'fossilize'), MESA_SHADER_CACHE_DIR=str(cache / 'mesa'),
                   DXVK_STATE_CACHE_PATH=str(cache), STEAM_COMPAT_SHADER_PATH=str(cache))
        log = (self.output / (role + '-launch.log')).open('x')
        self.logs.append(log)
        unity_log = 'Z:' + str(audit.GAME / 'BepInEx' / ('wiring-' + role + '-unity.log')).replace('/', '\\')
        argv = ['bash', '-c', 'source "$1"; shift; run_mwc "$@"', 'wiring-audit', str(audit.SOURCE / 'tools/linux-common.sh'),
                role, str(audit.GAME), '-no-dialogs', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
                '-wintermp', 'hostlocal' if role == 'host' else 'joinlocal', '-logFile', unity_log]
        process = subprocess.Popen(argv, cwd=audit.GAME, env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        self.processes.append(process)
        self.launches.append(dict(role=role, launcher_pid=process.pid, argv=argv, cwd=str(audit.GAME), prefix=str(profile), utc=audit.utc()))
        audit.save_json(self.output / 'launches.json', self.launches, replace=True)
        self.event('wiring launched', **self.launches[-1])


def fields(text, key):
    return [line.split('|')[1:] for line in text.splitlines() if line.startswith(key + '|')]


def wire(text):
    rows = fields(text, 'wire')
    assert len(rows) == 1, text
    return rows[0]


def until(role, predicate, timeout=60):
    deadline = time.monotonic() + timeout
    while True:
        text = audit.command(role, 'wire-view', timeout=min(timeout, 180))
        if predicate(text): return text
        if time.monotonic() >= deadline: raise TimeoutError('Wiring condition: ' + text)
        time.sleep(.3)


def spawn():
    deadline = time.monotonic() + 60
    while True:
        text = audit.command('guest', 'wire-view')
        offer = fields(text, 'offer')
        if offer and offer != [['none']]:
            audit.command('guest', 'spawn', 'host')
            until('guest', lambda t: fields(t, 'offer') == [['none']])
            return
        if time.monotonic() >= deadline: raise TimeoutError('No guest spawn offer: ' + text)
        time.sleep(.3)


def journey(native):
    checks = []
    def check(name, condition):
        assert condition, name
        checks.append(name)
        audit.save_json(native.output / 'assertions.json', checks, replace=True)
        print('PASS', name, flush=True)
    native.launch('host')
    until('host', lambda t: fields(t, 'wire-binding') == [['False']] and fields(t, 'wire-player') != [['none']], 180)
    audit.command('host', 'wire-fixture', '1', '0')
    native.launch('guest')
    until('guest', lambda t: fields(t, 'wire-binding') == [['False']] and fields(t, 'wire-player') != [['none']], 180)
    spawn()
    g = until('guest', lambda t: wire(t)[6] == '9')
    original = wire(g)[0]
    check('host uninstalled state presents cable absent and endpoints available', wire(g)[3:5] == ['False', 'True'])
    audit.command('guest', 'wire-tool')
    until('host', lambda t: any(row[2] == '1' for row in fields(t, 'wire-tool')))
    for endpoint in ('Fusebox', 'Ignition'):
        audit.command('guest', 'wire-align', endpoint)
        until('guest', lambda t: any(row[0] == endpoint and row[1:3] == ['True', 'Assemble'] for row in fields(t, 'wire-end')))
        audit.command('guest', 'wire-select', endpoint)
    h = until('host', lambda t: wire(t)[0] == 'True' and fields(t, 'wire-pending') == [['False']])
    g = until('guest', lambda t: wire(t)[6] == '3' and fields(t, 'wire-client-pending') == [['False']])
    check('guest native selections install and replicate cable', wire(h)[3:5] == wire(g)[3:5] == ['True', 'False'])
    check('host native Finish assembly runs once', fields(h, 'wire-finish-count') == [['1']])
    check('guest native saved Installed is protected', wire(g)[0:2] == [original, 'False'])
    check('matching authoritative revision', wire(h)[5] == wire(g)[5])
    token, sequence = fields(g, 'wire-client')[0]
    check('authenticated operation has matching host Accepted receipt',
          ['1', token, sequence, '5', 'Accepted'] in fields(h, 'wire-receipt'))
    old_revision = str(int(wire(h)[5]) - 1)
    audit.command('guest', 'wire-request', old_revision, sequence, token)
    time.sleep(.7)
    check('duplicate intent does not rerun host Finish', fields(audit.command('host', 'wire-view'), 'wire-finish-count') == [['1']])
    audit.command('guest', 'wire-drop')
    audit.command('guest', 'disconnect')
    until('guest', lambda t: fields(t, 'wire-binding') == [['none']])
    audit.command('guest', 'join')
    spawn()
    joined = until('guest', lambda t: wire(t)[6] == '3')
    check('fresh join snapshot reconstructs installation', wire(joined)[3:5] == ['True', 'False'] and wire(joined)[5] == wire(h)[5])
    audit.save_json(native.output / 'limits.json', dict(level='native injected-state localhost UDP fixture',
                    ordinary_input='NOT_TESTED', steam_two_pc='NOT_TESTED', different_save='NOT_TESTED',
                    four_player_soak='NOT_TESTED', persistence_reload='NOT_TESTED'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', required=True, type=Path)
    parser.add_argument('--name', required=True)
    args = parser.parse_args()
    audit.configure_run(args.run)
    signal.signal(signal.SIGTERM, audit.interrupted)
    signal.signal(signal.SIGALRM, audit.interrupted)
    signal.alarm(540)
    prepared = False
    try:
        native = WiringRun(args.name)
        # Each exclusive evidence leaf owns its baseline. A second named attempt
        # must neither overwrite nor silently reuse an earlier protection hash.
        audit.ROUND = native.output
        audit.MARKER = 'Ignition wiring audit ' + args.run.name + '/' + args.name + '\n'
        audit.prepare()
        prepared = True
        with native:
            sources = ['src/WinterMP.Core/Sync/ItemWorldSync.Wiring.cs', 'src/WinterMP.Core/Sync/ItemWorldSync.WiringConnection.cs',
                       'src/WinterMP.Core/Sync/ItemWorldSync.WiringValidation.cs', 'src/WinterMP.Net/Sync/WiringInstallLedger.cs',
                       'tools/GuestSaveProbe/LiveBagProbe.Wiring.cs', 'tools/wiring_install_native.py']
            audit.save_json(native.output / 'wiring-source-hashes.json', {p: audit.digest(audit.SOURCE / p) for p in sources})
            journey(native)
    finally:
        marker = audit.RIG / 'v11-owner.txt'
        if prepared and marker.is_file() and marker.read_text() == audit.MARKER: marker.unlink()
        signal.alarm(0)


if __name__ == '__main__':
    main()
