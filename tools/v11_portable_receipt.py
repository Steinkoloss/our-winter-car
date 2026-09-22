#!/usr/bin/env python3
"""Run a bounded portable command into a new RUN leaf. Never opens native logs/saves."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--run', required=True, type=Path)
    p.add_argument('--name', required=True)
    p.add_argument('--timeout', type=int, default=300)
    p.add_argument('command', nargs=argparse.REMAINDER)
    a = p.parse_args()
    source = Path(__file__).resolve().parents[1]
    rounds = source.parent / 'rounds'
    run = a.run.resolve(strict=True)
    if run.parent != rounds or a.run.is_symlink() or not (run / 'contract.json').is_file():
        p.error('RUN must be the assigned contract directory')
    if not a.name or Path(a.name).name != a.name or a.name in ('.', '..'):
        p.error('name must be a new leaf')
    command = a.command[1:] if a.command[:1] == ['--'] else a.command
    if not command or command[0] not in ('dotnet', 'python3'):
        p.error('portable dotnet/python3 command required')
    out = run / a.name
    out.mkdir()  # exclusive, preserve every earlier attempt
    receipt = {'command': command, 'cwd': str(source), 'started_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
               'roles': (['compiler-only'] if command[:2] == ['dotnet', 'build'] else
                         ['portable-test-process; game actors, where present, are doubles only']),
               'native': 'NOT_TESTED', 'protected_input_gate': 'BLOCKED', 'timeout_seconds': a.timeout}
    def hashes():
        paths = []
        for root in ('src', 'tools', 'docs', 'catalog', 'protocol'):
            for f in (source / root).rglob('*'):
                if f.is_symlink() or not f.is_file():
                    continue
                if f.suffix in ('.cs', '.csproj', '.py', '.md') and not any(x in f.parts for x in ('bin', 'obj', '__pycache__')):
                    paths.append(f)
        paths += [source / 'catalog/sync-catalog.json', source / 'protocol/PROTOCOL.md', source / 'PLAN.md',
                  source / 'src/WinterMP.Core/bin/Release/net35/WinterMP.Core.dll',
                  source / 'src/WinterMP.Net/bin/Release/net35/WinterMP.Net.dll',
                  source / 'src/WinterMP.Net/bin/Release/netstandard2.0/WinterMP.Net.dll']
        paths += [source / 'tools/PaneScrapeBridge.Tests/bin/Release/net8.0/PaneScrapeBridge.Tests.dll',
                  source / 'tools/TrainDispatch.Tests/bin/Release/net8.0/TrainDispatch.Tests.dll',
                  source / 'tools/TrainSend.Tests/bin/Release/net8.0/TrainSend.Tests.dll',
                  source / 'tools/WorldSyncCallbacks.Tests/bin/Release/net8.0/WorldSyncCallbacks.Tests.dll',
                  source / 'tools/BeerCaseBridge.Tests/bin/Release/net8.0/BeerCaseBridge.Tests.dll',
                  source / 'src/WinterMP.Net.Tests/bin/Release/net8.0/WinterMP.Net.Tests.dll']
        return {str(f.relative_to(source)): hashlib.sha256(f.read_bytes()).hexdigest() for f in paths if f.is_file()}
    receipt['before_sha256'] = hashes()
    exit_code = 125
    with (out / 'output.log').open('xb') as log:
        child = subprocess.Popen(command, cwd=source, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        receipt['owned_pid'] = child.pid
        try:
            exit_code = child.wait(timeout=a.timeout)
        except subprocess.TimeoutExpired:
            os.killpg(child.pid, 15)
            try:
                child.wait(timeout=10)
            except subprocess.TimeoutExpired:
                os.killpg(child.pid, 9)
                child.wait()
            receipt['timed_out'] = True
            exit_code = 124
        receipt['child_exit_code'] = child.returncode
    receipt.update(exit_code=exit_code, ended_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                   cleanup='Owned bounded portable command waited; no native processes/rig resources created.',
                   after_sha256=hashes(), output_sha256=hashlib.sha256((out / 'output.log').read_bytes()).hexdigest())
    with (out / 'receipt.json').open('x') as f:
        json.dump(receipt, f, indent=2)
        f.write('\n')
    print(json.dumps({'receipt': str(out / 'receipt.json'), 'exit_code': exit_code}))
    return exit_code


if __name__ == '__main__':
    sys.exit(main())
