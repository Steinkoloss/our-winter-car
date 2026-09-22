#!/usr/bin/env python3
"""Verify S09 RUN receipts and source scope; never open native logs/saves or launch a game."""
import argparse
import difflib
import hashlib
import json
import os
from pathlib import Path
import re

SOURCE = Path(__file__).resolve().parents[1]
EXPECTED_CHANGES = {
    'PLAN.md', 'src/WinterMP.Core/Sync/WorldSyncManager.cs',
    'src/WinterMP.Core/Sync/WorldSyncManager.Callbacks.cs',
    'tools/WorldSyncCallbacks.Tests/WorldSyncCallbacks.Tests.csproj',
    'tools/WorldSyncCallbacks.Tests/CallbackDoubles.cs',
    'tools/WorldSyncCallbacks.Tests/CallbackTests.cs',
    'tools/WorldSyncCallbacks.Tests/RetryTests.cs',
    'tools/tests/test_s09_callback_wiring.py',
    'tools/s09_portable_manifest.py', 'docs/S09-LATEUPDATE-CONTAINMENT.md',
}


def digest(path):
    assert path.is_file() and not path.is_symlink(), str(path)
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    return json.loads(path.read_text())


def retain(path, content):
    with path.open('x') as stream:
        stream.write(content)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, required=True)
    args = parser.parse_args()
    run = args.run.resolve(strict=True)
    assert args.run == run and os.environ.get('RUN') == str(run)
    assert run.parent == SOURCE.parent / 'rounds' and (run / 'contract.json').is_file()
    baseline = load(run / 'before.json')
    receipts, commands = {}, []
    for name in ('callbacks-red', 'callbacks-green', 'callbacks-final', 'net-final',
                 'core-final', 'launcher-final', 'python-final',
                 'provenance-before-command', 'provenance-after-command'):
        receipt = load(run / name / 'receipt.json')
        expected_exit = 1 if name in ('callbacks-red', 'provenance-before-command', 'provenance-after-command') else 0
        assert receipt['exit_code'] == receipt['child_exit_code'] == expected_exit, name
        assert not receipt.get('timed_out'), name
        assert receipt['output_sha256'] == digest(run / name / 'output.log'), name
        text = (run / name / 'output.log').read_text()
        count = re.search(r'Total tests: (\d+)', text) or re.search(r'Ran (\d+) tests', text)
        commands.append(dict(name=name, command=receipt['command'], exit_code=expected_exit,
                             tests=int(count[1]) if count else None, timeout=receipt['timeout_seconds'],
                             cleanup=receipt['cleanup'], output_sha256=receipt['output_sha256'],
                             receipt_sha256=digest(run / name / 'receipt.json')))
        receipts[name] = receipt
    red, green = receipts['callbacks-red'], receipts['callbacks-green']
    tests = 'tools/WorldSyncCallbacks.Tests/CallbackTests.cs'
    for path in (tests, 'tools/WorldSyncCallbacks.Tests/CallbackDoubles.cs',
                 'tools/WorldSyncCallbacks.Tests/WorldSyncCallbacks.Tests.csproj'):
        assert red['after_sha256'][path] == green['before_sha256'][path] == green['after_sha256'][path], path
    assert digest(SOURCE / tests) == red['after_sha256'][tests], 'Original regression tests changed'
    callback = 'src/WinterMP.Core/Sync/WorldSyncManager.Callbacks.cs'
    assert red['after_sha256'][callback] != green['after_sha256'][callback]
    assert digest(SOURCE / callback) == green['after_sha256'][callback]
    assert 'Failed: 7' in (run / 'callbacks-red/output.log').read_text()
    assert 'Passed: 8' in (run / 'callbacks-green/output.log').read_text()

    final = {}
    for name in ('callbacks-final', 'net-final', 'core-final', 'launcher-final', 'python-final'):
        for path, expected in receipts[name]['after_sha256'].items():
            # Builds of other projects may refresh their binaries; the latest
            # relevant final receipt pins each produced DLL below.
            if path.endswith('.dll'):
                continue
            assert digest(SOURCE / path) == expected, 'Source changed after final check: ' + path
            final[path] = expected
    changed, unchanged = {}, []
    for path, expected in baseline.items():
        actual = digest(SOURCE / path)
        if actual == expected:
            unchanged.append(path)
        else:
            changed[path] = actual
    for path in set(final) | EXPECTED_CHANGES:
        if path not in baseline:
            changed[path] = digest(SOURCE / path)
    assert set(changed) == EXPECTED_CHANGES, sorted(set(changed) ^ EXPECTED_CHANGES)
    for path in ('catalog/sync-catalog.json', 'protocol/PROTOCOL.md', 'src/WinterMP.Net/Protocol.cs',
                 'src/WinterMP.Core/Session/SessionManager.cs', 'src/WinterMP.Core/Session/SessionManager.Messages.cs',
                 'src/WinterMP.Core/Sync/VehicleWorldSync.cs', 'src/WinterMP.Core/Sync/TractorTrailerSync.cs',
                 'src/WinterMP.Core/Sync/VenttiSync.Reactions.cs'):
        assert path in unchanged, path

    binaries = {}
    for path, receipt_name in (
        ('src/WinterMP.Core/bin/Release/net35/WinterMP.Core.dll', 'core-final'),
        ('src/WinterMP.Net/bin/Release/net35/WinterMP.Net.dll', 'core-final'),
        ('src/WinterMP.Net/bin/Release/netstandard2.0/WinterMP.Net.dll', 'net-final'),
        ('src/WinterMP.Net.Tests/bin/Release/net8.0/WinterMP.Net.Tests.dll', 'net-final')):
        actual = digest(SOURCE / path)
        assert actual == receipts[receipt_name]['after_sha256'][path], path
        binaries[path] = actual
    binaries['tools/WorldSyncCallbacks.Tests/bin/Release/net8.0/WorldSyncCallbacks.Tests.dll'] = digest(
        SOURCE / 'tools/WorldSyncCallbacks.Tests/bin/Release/net8.0/WorldSyncCallbacks.Tests.dll')

    protected = [load(run / name / 'report.json') for name in ('provenance-before', 'provenance-after')]
    for report in protected:
        assert report['gate_status'] == 'BLOCKED' and not report['native_launch_allowed']
        assert report['baseline_pinned_unchanged'] and report['retained_evidence_unchanged']
        assert report['target_metadata_stable'] and report['target_content_bytes_read'] == 0
    assert protected[0]['observed_target'] == protected[1]['observed_target']
    assert protected[0]['expected_baseline_sha256'] == protected[1]['expected_baseline_sha256']

    # Retain an independently hash-matched pre-task coordinator, not a hand-written
    # "old implementation". This makes the extraction and cleanup diff inspectable.
    original = SOURCE.parent / 'checkpoints/000049-work/files/src/WinterMP.Core/Sync/WorldSyncManager.cs'
    world = 'src/WinterMP.Core/Sync/WorldSyncManager.cs'
    assert digest(original) == baseline[world], 'Archived coordinator is not this task baseline'
    original_text = original.read_text()
    retain(run / 'WorldSyncManager.before.cs.txt', original_text)
    retain(run / 'WorldSyncManager.diff', ''.join(difflib.unified_diff(
        original_text.splitlines(keepends=True), (SOURCE / world).read_text().splitlines(keepends=True),
        fromfile='assignment/WorldSyncManager.cs', tofile='current/WorldSyncManager.cs')))

    report = dict(commands=commands, changed_files_sha256=changed, unchanged_baseline_files=unchanged,
                  binary_sha256=binaries, red_green_identical_tests=True, original_regression_tests_unchanged=True,
                  baseline_coordinator_sha256=baseline[world],
                  evidence_level='Portable actual coordinator callbacks; Unity/session/subsystem/diagnostic sinks are doubles. Core net35 compilation only.',
                  protected_input_provenance='BLOCKED; baseline pin/retained receipt hashes and target metadata unchanged; zero target log bytes read. Current protected-content equality NOT_TESTED.',
                  cleanup='Owned bounded portable children waited; no native process, rig resource, deployment, protected-input write, controller edit, commit or publication.',
                  not_tested=['native discovery', 'native injected state', 'ordinary input', 'native host/guest authority or convergence',
                              'Steam/two-PC', 'different saves', 'native late join/rejoin', 'native save/reload', 'four-player soak'])
    retain(run / 'verification.json', json.dumps(report, indent=2) + '\n')
    print(json.dumps(dict(verification=str(run / 'verification.json'), changed_files=sorted(changed),
                         commands=[{k: c[k] for k in ('name', 'exit_code', 'tests')} for c in commands],
                         binary_sha256=binaries), indent=2))


if __name__ == '__main__':
    main()
