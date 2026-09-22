#!/usr/bin/env python3
"""Bounded V11 rendered readiness diagnostic; never executes scraper fixtures.

An isolated compositor hands its actual display environment back to the lock
owner. Both rig locks cover deployment, compositor/game launch and cleanup.
No desktop socket/configuration or production readiness policy is changed.
"""
import argparse
import json
import os
from pathlib import Path
import signal
import shlex
import socket
import subprocess
import sys
import time
import uuid
import v11_pane_audit as audit

def handoff(root):
    root = Path(root)
    assert root.resolve().parent == audit.RIG.resolve()
    assert (root / 'owner.txt').read_text() == os.environ['WINTERMP_V11_RENDER_TOKEN']
    values = {key: os.environ[key] for key in ('DISPLAY', 'WAYLAND_DISPLAY', 'XAUTHORITY', 'DBUS_SESSION_BUS_ADDRESS') if key in os.environ}
    audit.save_json(root / 'handoff.json', dict(utc=audit.utc(), pid=os.getpid(), environment=values))
    deadline = time.monotonic() + 660
    while time.monotonic() < deadline and not (root / 'stop').exists(): time.sleep(.2)

def owned(token):
    result = []
    for path in Path('/proc').iterdir():
        if not path.name.isdigit(): continue
        try:
            if ('WINTERMP_V11_RENDER_TOKEN=' + token).encode() in (path / 'environ').read_bytes().split(b'\0'):
                result.append(int(path.name))
        except (FileNotFoundError, PermissionError, ProcessLookupError): pass
    return result

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, required=True)
    parser.add_argument('phase', choices=('readiness',))
    parser.add_argument('name')
    parser.add_argument('--compositor', choices=('gamescope', 'kwin'), default='kwin')
    args = parser.parse_args()
    audit.configure_run(args.run)
    signal.signal(signal.SIGTERM, audit.interrupted)
    signal.signal(signal.SIGALRM, audit.interrupted)
    signal.alarm(600)
    with audit.NativeRun(args.name) as native:
        root = audit.RIG / ('r' + uuid.uuid4().hex[:8])
        root.mkdir(mode=0o700)
        token = uuid.uuid4().hex
        (root / 'owner.txt').write_text(token)
        env = dict(os.environ)
        for key in ('WAYLAND_DISPLAY', 'DISPLAY', 'XAUTHORITY', 'DBUS_SESSION_BUS_ADDRESS'): env.pop(key, None)
        env.update(WINTERMP_V11_NATIVE_DISPLAY='1', WINTERMP_V11_RENDER_TOKEN=token)
        for key, leaf in [('XDG_RUNTIME_DIR', 'r'), ('XDG_CACHE_HOME', 'cache'), ('XDG_CONFIG_HOME', 'config')]:
            path = root / leaf
            path.mkdir(mode=0o700)
            env[key] = str(path)
        session = [sys.executable, str(Path(__file__).resolve()), '--handoff', str(root)]
        listener = None
        process = None
        log = None
        receipt = dict(started_utc=audit.utc(), runtime=str(root), token=token, driver_pid=os.getpid(),
                       evidence_level='rendered offscreen readiness only; not scraper gameplay or ordinary input')
        def cleanup():
            nonlocal log
            if receipt.get('cleanup_done'): return
            receipt['cleanup_started_utc'] = audit.utc()
            receipt['exit_before_cleanup'] = process.poll() if process else None
            (root / 'stop').touch()
            if process:
                try: process.wait(timeout=10)
                except subprocess.TimeoutExpired: pass
            for sig in (signal.SIGTERM, signal.SIGKILL):
                for pid in owned(token):
                    try: os.kill(pid, sig)
                    except ProcessLookupError: pass
                if process:
                    try: process.wait(timeout=5)
                    except subprocess.TimeoutExpired: pass
                deadline = time.monotonic() + 5
                while owned(token) and time.monotonic() < deadline: time.sleep(.1)
            if listener: listener.close()
            if log: log.close(); log = None
            receipt.update(exit_code=process.poll() if process else None, owned_processes_remaining=owned(token),
                           cleanup_done=True, ended_utc=audit.utc())
            (root / 'owner.txt').unlink()
            receipt['owner_marker_removed'] = not (root / 'owner.txt').exists()
            audit.save_json(native.output / 'render.json', receipt)
            assert not receipt['owned_processes_remaining'], receipt
        native.cleanup_display = cleanup
        try:
            if args.compositor == 'kwin':
                # Abstract listener, never alter the desktop's socket directory.
                listener = socket.socket(socket.AF_UNIX)
                display = ':' + str(10000 + int(uuid.uuid4().hex[:4], 16))
                listener.bind('\0/tmp/.X11-unix/X' + display[1:]); listener.listen(16)
                command = ['dbus-run-session', '--', 'kwin_wayland', '--virtual', '--width', '1280', '--height', '720',
                           '--no-lockscreen', '--no-global-shortcuts', '--no-kactivities', '--socket', 'v11',
                           '--xwayland', '--xwayland-fd', str(listener.fileno()), '--xwayland-display', display,
                           '--exit-with-session', shlex.join(session)]
            else:
                command = ['gamescope', '--backend', 'headless', '-W', '1280', '-H', '720', '-r', '60', '--', *session]
            receipt['argv'] = command
            log = (native.output / 'render.log').open('x')
            native.event('compositor launch begin', argv=command, runtime=str(root))
            process = subprocess.Popen(command, cwd=audit.SOURCE, env=env, stdout=log, stderr=subprocess.STDOUT,
                                       start_new_session=True, pass_fds=(listener.fileno(),) if listener else ())
            receipt['pid'] = process.pid
            native.event('compositor launched', pid=process.pid)
            deadline = time.monotonic() + 60
            while not (root / 'handoff.json').exists():
                if process.poll() is not None: raise RuntimeError('Compositor exited before display handoff: ' + str(process.returncode))
                if time.monotonic() >= deadline: raise TimeoutError('Compositor did not hand off its display in 60s')
                time.sleep(.2)
            receipt['handoff'] = json.loads((root / 'handoff.json').read_text())
            env.update(receipt['handoff']['environment'])
            assert env.get('DISPLAY'), 'Compositor handed off no X display'
            native.event('display handoff received', **receipt['handoff'])
            with (native.output / 'display-query.log').open('x') as query:
                result = subprocess.run(['xrandr', '--query'], env=env, stdout=query, stderr=subprocess.STDOUT, timeout=15)
            receipt['display_query_exit'] = result.returncode
            assert result.returncode == 0, 'Isolated display query failed'
            os.environ.update({key: value for key, value in env.items() if key in
                               ('DISPLAY', 'WAYLAND_DISPLAY', 'XAUTHORITY', 'DBUS_SESSION_BUS_ADDRESS', 'XDG_RUNTIME_DIR',
                                'XDG_CACHE_HOME', 'XDG_CONFIG_HOME', 'WINTERMP_V11_NATIVE_DISPLAY')})
            native.event('readiness scenario begin')
            audit.readiness(args.name, existing_native=native)
            native.event('readiness scenario end')
        except BaseException as error:
            receipt['error'] = str(error)[:4096]
            native.event('rendered diagnostic error', error=receipt['error'])
            raise

if __name__ == '__main__':
    if len(sys.argv) == 3 and sys.argv[1] == '--handoff': handoff(sys.argv[2])
    else: main()
