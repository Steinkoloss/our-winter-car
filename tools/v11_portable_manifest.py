#!/usr/bin/env python3
"""Summarize retained portable receipts; no native inputs are opened."""
import argparse
import hashlib
import json
from pathlib import Path
import re


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--run', required=True, type=Path)
    a = p.parse_args()
    source = Path(__file__).resolve().parents[1]
    run = a.run.resolve(strict=True)
    if a.run.is_symlink() or run.parent != source.parent / 'rounds' or not (run / 'contract.json').is_file():
        p.error('assigned RUN required')
    baseline = json.loads((run / 'before.json').read_text())
    commands = []
    final = {}
    for name in ('bridge-final', 'net-final', 'core-final', 'launcher-final', 'python-final'):
        receipt = json.loads((run / name / 'receipt.json').read_text())
        raw = (run / name / 'output.log').read_bytes()
        assert hashlib.sha256(raw).hexdigest() == receipt['output_sha256'], name
        assert receipt['exit_code'] == 0 and receipt['child_exit_code'] == 0, name
        text = raw.decode(errors='replace')
        count = re.search(r'Total tests: (\d+)', text) or re.search(r'Ran (\d+) tests', text)
        commands.append({'name': name, 'command': receipt['command'], 'exit_code': receipt['exit_code'],
                         'tests': int(count.group(1)) if count else None, 'raw_sha256': receipt['output_sha256'],
                         'receipt_sha256': hashlib.sha256((run / name / 'receipt.json').read_bytes()).hexdigest()})
        final.update(receipt['after_sha256'])
    current = {}
    for name, expected in final.items():
        path = source / name
        assert not path.is_symlink() and path.is_file(), name
        observed = hashlib.sha256(path.read_bytes()).hexdigest()
        assert observed == expected, 'changed after verification: ' + name
        current[name] = observed
    changed = {name: value for name, value in current.items()
               if not any(part in ('bin', 'obj') for part in Path(name).parts) and baseline.get(name) != value}
    unchanged = ['catalog/sync-catalog.json', 'src/WinterMP.Net/Protocol.cs', 'src/WinterMP.Net/Messages/PaneScrapeMessages.cs',
                 'src/WinterMP.Net/Sync/PaneScrapeReplica.cs', 'src/WinterMP.Core/Session/SessionManager.Messages.cs',
                 'src/WinterMP.Core/Session/SessionManager.cs', 'src/WinterMP.Core/Sync/VehicleWorldSync.Climate.cs']
    for name in unchanged:
        assert current[name] == baseline[name], name
    report = {'commands': commands, 'changed_files_sha256': changed,
              'binary_sha256': {name: value for name, value in current.items() if name.endswith('.dll')},
              'unchanged_from_assignment': unchanged,
              'evidence_level': 'portable production-source bridge with explicit engine/session/action doubles; Core compilation only',
              'protected_input_provenance': 'BLOCKED; no native authorization',
              'not_tested': ['native gameplay', 'native injected-state fixtures', 'ordinary input', 'different saves',
                             'fresh-player native late join', 'native save/reload', 'Steam/two-PC', 'four-player soak'],
              'cleanup': 'All owned portable command children waited. No rig resources or game processes created; protected log contents and personal saves not accessed.'}
    with (run / 'verification.json').open('x') as f:
        json.dump(report, f, indent=2)
        f.write('\n')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
