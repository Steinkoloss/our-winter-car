#!/usr/bin/env python3
"""Validate Train dispatch RUN evidence, scope and byte identity; never open native input."""
import argparse
import difflib
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
from s09_update_manifest import extract

SOURCE = Path(__file__).resolve().parents[1]
SYNC = 'src/WinterMP.Core/Sync/'
TEST = 'tools/TrainDispatch.Tests/'
CHANGED = {
    SYNC + 'WorldSyncManager.cs', SYNC + 'WorldSyncManager.Callbacks.cs',
    SYNC + 'WorldSyncManager.Handlers.cs', SYNC + 'WorldSyncManager.Snapshots.cs',
    'PLAN.md', 'docs/S09-TRAIN-DISPATCH-CONTAINMENT.md', 'tools/s09_train_fixture.py',
    'tools/s09_train_manifest.py', 'tools/v11_portable_receipt.py', 'tools/tests/test_s09_train_wiring.py',
    'tools/WorldSyncCallbacks.Tests/TrainMessageResetTests.cs',
    TEST + 'TrainDispatch.Tests.csproj', TEST + 'Doubles.cs', TEST + 'DispatchTests.cs',
    TEST + 'SnapshotDoubles.cs', TEST + 'IsolationTests.cs',
}
FINAL = ('train-final', 'callbacks-final', 'net-final', 'launcher-final', 'authority-final', 'python-final', 'core-final')
COMMANDS = ('train-compile-red', 'train-red', 'train-red-final', 'callbacks-baseline',
            'train-green', 'callbacks-green', 'train-expanded') + FINAL


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def save(path, text):
    if path.exists():
        assert path.read_text() == text, 'Refuse to overwrite differing evidence: ' + str(path)
    else:
        path.write_text(text)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--run', type=Path, required=True)
    args = p.parse_args()
    run = args.run.resolve(strict=True)
    assert str(run) == os.environ.get('RUN') and run.parent == SOURCE.parent / 'rounds'
    assert (run / 'contract.json').is_file()
    before = json.loads((run / 'before.json').read_text())
    receipts, commands = {}, []
    for name in COMMANDS:
        receipt = json.loads((run / name / 'receipt.json').read_text())
        wanted_exit = 1 if name in ('train-compile-red', 'train-red', 'train-red-final') else 0
        assert receipt['exit_code'] == receipt['child_exit_code'] == wanted_exit, name
        assert not receipt.get('timed_out') and receipt['owned_pid'] > 0
        assert receipt['cwd'] == str(SOURCE)
        assert receipt['output_sha256'] == digest(run / name / 'output.log')
        log = (run / name / 'output.log').read_text()
        total = re.search(r'Total tests: (\d+)', log) or re.search(r'Ran (\d+) tests', log)
        commands.append(dict(name=name, command=receipt['command'], exit_code=wanted_exit,
                             tests=int(total[1]) if total else None,
                             receipt_sha256=digest(run / name / 'receipt.json'), output_sha256=receipt['output_sha256'],
                             timeout_seconds=receipt['timeout_seconds'], owned_pid=receipt['owned_pid']))
        receipts[name] = receipt
    red, green = receipts['train-red-final'], receipts['train-green']
    redlog = (run / 'train-red-final/output.log').read_text()
    assert 'Failed: 3' in redlog and 'Passed: 6' in redlog
    for assertion in ('TrainSnapshotEscapeDoesNotTruncateProductionFanout(full: False)',
                      'TrainSnapshotEscapeDoesNotTruncateProductionFanout(full: True)',
                      'DirectReceivePreparationEscapeIsContainedWithDetail',
                      'System.InvalidOperationException: train dispatch escape', 'System.ArgumentException: original inner cause'):
        assert assertion in redlog, assertion
    assert 'Passed: 9' in (run / 'train-green/output.log').read_text()
    assert 'Passed: 116' in (run / 'callbacks-baseline/output.log').read_text()
    assert 'Passed: 116' in (run / 'callbacks-green/output.log').read_text()
    assert 'Passed: 119' in (run / 'callbacks-final/output.log').read_text()
    assert '0 Error(s)' in (run / 'core-final/output.log').read_text()
    redgreen_tests = {}
    for path, expected in red['before_sha256'].items():
        if (path.startswith(TEST) and not path.endswith('.dll')) or path == 'tools/s09_train_fixture.py':
            assert expected == red['after_sha256'][path] == green['before_sha256'][path] == green['after_sha256'][path] == digest(SOURCE / path), path
            redgreen_tests[path] = expected
    baseline_tests, changed = {}, {}
    for path, expected in before.items():
        current = digest(SOURCE / path)  # deletion is a failure, not silently excluded
        if current != expected:
            changed[path] = current
        if (path.startswith('src/WinterMP.Net.Tests/') or path.startswith('src/WinterMP.Launcher.Tests/')
                or path.startswith('tools/tests/') or re.match(r'tools/[^/]+\.Tests/', path)):
            assert current == expected, path
            baseline_tests[path] = expected
    for path in CHANGED:
        if path not in before:
            changed[path] = digest(SOURCE / path)
    assert set(changed) == CHANGED, sorted(set(changed) ^ CHANGED)
    for name in FINAL:
        for path, expected in receipts[name]['after_sha256'].items():
            if not path.endswith('.dll'):
                assert expected == digest(SOURCE / path), 'Source changed after final test: ' + path
                assert path in before or path in CHANGED, 'Unexpected new file: ' + path
    originals = {}
    for leaf in ('WorldSyncManager.cs', 'WorldSyncManager.Callbacks.cs', 'WorldSyncManager.Handlers.cs', 'WorldSyncManager.Snapshots.cs'):
        path = SYNC + leaf
        original = run / 'baseline' / leaf
        assert digest(original) == before[path] == red['before_sha256'][path] == red['after_sha256'][path]
        originals[leaf] = original.read_text()
        save(run / (leaf + '.diff'), ''.join(difflib.unified_diff(originals[leaf].splitlines(True),
             (SOURCE / path).read_text().splitlines(True), fromfile='baseline/' + path, tofile='current/' + path)))
    world = (SOURCE / (SYNC + 'WorldSyncManager.cs')).read_text()
    assert world.replace('            ResetTrainMessageErrors();\n', '') == originals['WorldSyncManager.cs']
    callbacks = (SOURCE / (SYNC + 'WorldSyncManager.Callbacks.cs')).read_text()
    for sig in ('private void Update()', 'private void FixedUpdate()', 'private void LateUpdate()',
                'private sealed class CallbackFailure', 'private void HandleSyncError('):
        assert extract(callbacks, sig) == extract(originals['WorldSyncManager.Callbacks.cs'], sig), sig
    handlers = (SOURCE / (SYNC + 'WorldSyncManager.Handlers.cs')).read_text()
    handlers = handlers.replace('using System;\n', '', 1).replace(extract(handlers, 'public void OnTrainState('),
                 extract(originals['WorldSyncManager.Handlers.cs'], 'public void OnTrainState('))
    assert handlers == originals['WorldSyncManager.Handlers.cs']
    snapshots = (SOURCE / (SYNC + 'WorldSyncManager.Snapshots.cs')).read_text()
    snapshots = snapshots.replace(extract(snapshots, '        private TrainState? BuildTrainSnapshot(') + '\n\n', '')
    snapshots = snapshots.replace('var train = BuildTrainSnapshot();', 'var train = _train.Snapshot();')
    snapshots = snapshots.replace('var trainState = BuildTrainSnapshot();', 'var trainState = _train.Snapshot();')
    snapshots = snapshots.replace('var train = BuildTrainSnapshot(netId); if (train != null) yield return train;',
        'if (netId == _train.NetId) { var train = _train.Snapshot(); if (train != null) yield return train; }')
    assert snapshots == originals['WorldSyncManager.Snapshots.cs']
    preserved = {path: sha for path, sha in before.items() if path.startswith((
        'src/WinterMP.Net/', 'src/WinterMP.Core/Session/', 'catalog/', 'protocol/',
        SYNC + 'TrainSync', SYNC + 'VehicleWorldSync', SYNC + 'PaneScrapeSync', SYNC + 'Guest'))}
    for path, expected in preserved.items():
        assert digest(SOURCE / path) == expected
    binaries = {}
    for path, name in (
        (TEST + 'bin/Release/net8.0/TrainDispatch.Tests.dll', 'train-final'),
        ('tools/WorldSyncCallbacks.Tests/bin/Release/net8.0/WorldSyncCallbacks.Tests.dll', 'callbacks-final'),
        ('tools/PaneScrapeBridge.Tests/bin/Release/net8.0/PaneScrapeBridge.Tests.dll', 'authority-final'),
        ('src/WinterMP.Core/bin/Release/net35/WinterMP.Core.dll', 'core-final'),
        ('src/WinterMP.Net/bin/Release/net35/WinterMP.Net.dll', 'core-final'),
        ('src/WinterMP.Net/bin/Release/netstandard2.0/WinterMP.Net.dll', 'net-final'),
    ):
        binaries[path] = digest(SOURCE / path)
        assert binaries[path] == receipts[name]['after_sha256'][path], path
    proc_command = ['ps', '--sid', ','.join(str(r['owned_pid']) for r in receipts.values()), '-o', 'pid,ppid,pgid,sid,comm']
    proc = subprocess.run(proc_command, capture_output=True, text=True, timeout=10)
    assert proc.returncode == 1 and len(proc.stdout.strip().splitlines()) == 1, proc.stdout
    save(run / 'owned-process-check.log', json.dumps(dict(command=proc_command, exit_code=proc.returncode,
                                                        stdout=proc.stdout, stderr=proc.stderr), indent=2) + '\n')
    report = dict(commands=commands, changed_files_sha256=changed, binary_sha256=binaries,
        baseline_test_sha256=baseline_tests, byte_identical_red_green_test_and_generator_sha256=redgreen_tests,
        preserved_gameplay_sha256=preserved, baseline_deletions=[],
        production_change='Only receive gate/catch, shared snapshot creation gate/catch, two failure records and lifecycle resets; reconstructed baseline validates unrelated source/order unchanged.',
        evidence_level='Portable exact selected production methods, Net codec/policy/TrainSync.Receive. Engine/session/preparation/native snapshot and other subsystem doubles. Selected dispatch arms only; complete object iterator static wiring plus dynamic shared helper.',
        early_train_binary_hash='NOT_RECORDED: new test DLL was registered in receipt tool after initial red/green. Final and expanded-run hashes are recorded; historical hash not fabricated.',
        cleanup='All receipt-owned portable process sessions are empty. No native process, rig resources, game deployment or protected-input access.',
        cleanup_log_sha256=digest(run / 'owned-process-check.log'),
        not_tested=['native discovery/injected-state', 'protected-input content equality', 'ordinary input',
                    'native host/guest once-only actions and matching peer results', 'Steam/two-PC', 'different saves',
                    'fresh-player late join/rejoin', 'save/reload', 'four-player soak', 'native teardown'],
        remaining_gaps=['Train SendTo/encoding failure still aborts current request fanout under original packet catch',
                        'Full object-state handler/iterator not dynamically exercised by this fixture',
                        'TrainSync native admission/Disable/Clear and diagnostic-sink faults',
                        'Other message/snapshot producers, nonselected Update prerequisites and native teardown fanout'],
        next_bounded_step='Audit and regression-test the train transport-send failure fanout separately without retransmitting messages or masking broken transport; native teardown remains separate.',
        ledger_verified=False)
    save(run / 'verification.json', json.dumps(report, indent=2) + '\n')
    print(json.dumps(dict(verification=str(run / 'verification.json'), changed_files=sorted(changed),
                         commands=[{k: c[k] for k in ('name', 'exit_code', 'tests')} for c in commands],
                         binary_sha256=binaries), indent=2))


if __name__ == '__main__':
    main()
