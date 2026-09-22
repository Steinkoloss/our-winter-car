#!/usr/bin/env python3
"""Verify retained RUN-scoped I10 portable receipts, not native authorization."""
import argparse
import hashlib
import json
from pathlib import Path
import re


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, required=True)
    args = parser.parse_args()
    source = Path(__file__).resolve().parents[1]
    run = args.run.resolve(strict=True)
    if args.run.is_symlink() or run.parent != source.parent / 'rounds':
        parser.error('assigned RUN contract directory required')
    contract = json.loads((run / 'contract.json').read_text())
    if contract.get('ids') != ['I10']:
        parser.error('I10 contract required')
    baseline = json.loads((run / 'before.json').read_text())
    final = {}
    commands = []
    for name in ('bridge-final', 'net-final', 'core-final', 'launcher-final', 'python-final'):
        receipt_path = run / name / 'receipt.json'
        receipt = json.loads(receipt_path.read_text())
        output = run / name / 'output.log'
        assert digest(output) == receipt['output_sha256'], name
        assert receipt['exit_code'] == receipt['child_exit_code'] == 0, name
        text = output.read_text()
        count = re.search(r'Total tests: (\d+)', text) or re.search(r'Ran (\d+) tests', text)
        commands.append({'leaf': name, 'command': receipt['command'], 'cwd': receipt['cwd'],
                         'exit_code': 0, 'tests': int(count.group(1)) if count else None,
                         'roles': receipt['roles'], 'output_sha256': digest(output),
                         'receipt_sha256': digest(receipt_path)})
        final.update(receipt['after_sha256'])
    for name, expected in final.items():
        path = source / name
        assert not path.is_symlink() and path.is_file() and digest(path) == expected, 'changed after tests: ' + name
    bridge = (run / 'bridge-final/output.log').read_text()
    assert 'PORTABLE production ItemWorldSync partial' in bridge
    red = []
    for name, failure in (('beer-codec-red', 'Unknown message id 265'), ('bridge-red-snapshot', 'Expected: 255')):
        receipt = json.loads((run / name / 'receipt.json').read_text())
        output = run / name / 'output.log'
        assert receipt['exit_code'] == 1 and digest(output) == receipt['output_sha256'] and failure in output.read_text(), name
        red.append({'leaf': name, 'exit_code': 1, 'observed_failure': failure, 'output_sha256': digest(output)})
    changed = {name: value for name, value in final.items() if baseline.get(name) != value and not any(p in ('bin', 'obj') for p in Path(name).parts)}
    assert all(name == 'PLAN.md' or name.split('/')[0] in contract['allowed_paths'] or name.split('/')[0] + '/' in contract['allowed_paths'] for name in changed)
    unchanged = ['src/WinterMP.Core/Sync/PaneScrapeSync.cs', 'src/WinterMP.Core/Sync/PaneScrapeSync.Bindings.cs',
                 'src/WinterMP.Core/Sync/VehicleWorldSync.Climate.cs', 'src/WinterMP.Core/Session/SessionManager.cs']
    for name in unchanged:
        assert final[name] == baseline[name], name
    catalog = json.loads((source / 'catalog/sync-catalog.json').read_text())
    assert not any(r.get('objectName') == 'beercase' and r.get('fsmName') == 'Use' for r in catalog['controls'])
    report = {'task': 't_16dcfb48', 'run': str(run), 'protocol': 261,
              'commands': commands, 'red_regressions': red, 'changed_files_sha256': changed,
              'binary_sha256': {name: value for name, value in final.items() if name.endswith('.dll')},
              'unchanged_from_assignment': unchanged,
              'evidence_level': 'Portable protocol/authority and Core-source-linked ItemWorldSync bridge with explicit native/session/contact doubles; Core net35 compilation only.',
              'native_binding': 'UNBOUND: native capacity/actions/identity/save fields unestablished; no game caller installs the binding or intercepts native input.',
              'protected_input_provenance': 'BLOCKED; not rechecked or bypassed',
              'not_tested': ['native discovery/action', 'native injected-state fixtures', 'ordinary input', 'different saves',
                             'fresh-player native late join', 'native save/reload', 'Steam/two-PC', 'four-player soak'],
              'native_persistence': 'UNKNOWN for unopened, partial and empty case; no new save fields/writers',
              'scope': 'I10 partial; wood-carrier contents untouched; no generic beer inventory or drink effect',
              'cleanup': 'Owned portable commands bounded and waited. No native process, rig preparation, deployment, protected log access or personal-save access.'}
    with (run / 'verification.json').open('x') as f:
        json.dump(report, f, indent=2)
        f.write('\n')
    print(json.dumps({'verification': str(run / 'verification.json'), 'changed_files': len(changed),
                      'commands': commands, 'binary_sha256': report['binary_sha256']}, indent=2))


if __name__ == '__main__':
    main()
