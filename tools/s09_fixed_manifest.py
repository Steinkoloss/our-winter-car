#!/usr/bin/env python3
"""Validate bounded S09 Train FixedUpdate RUN receipts; never access native logs/saves."""
import argparse
import difflib
import hashlib
import json
import os
from pathlib import Path
import re
from s09_portable_manifest import digest, load, retain
from s09_update_manifest import extract

SOURCE = Path(__file__).resolve().parents[1]
WORLD = 'src/WinterMP.Core/Sync/WorldSyncManager.cs'
CALLBACKS = 'src/WinterMP.Core/Sync/WorldSyncManager.Callbacks.cs'
TESTS = 'tools/WorldSyncCallbacks.Tests/'
EXPECTED_CHANGES = {
    WORLD, CALLBACKS, 'PLAN.md', 'docs/S09-UPDATE-CONTAINMENT.md', 'docs/S09-FIXEDUPDATE-CONTAINMENT.md',
    TESTS + 'UpdateDoubles.cs', TESTS + 'FixedUpdateDoubles.cs', TESTS + 'FixedUpdateTests.cs',
    TESTS + 'FixedUpdateRetryTests.cs', 'tools/tests/test_s09_fixed_wiring.py', 'tools/s09_fixed_manifest.py',
}
FINAL = ('callbacks-final', 'net-final', 'launcher-final', 'python-final', 'core-final')
COMMANDS = ('callbacks-red', 'callbacks-green', 'callbacks-expanded', 'python-initial') + FINAL
NOT_TESTED = [
    'native discovery/injected-state fixtures', 'protected-input content equality', 'ordinary input',
    'native host/guest action/once-only authority/matching peer results', 'Steam/two-PC', 'different saves',
    'fresh-player late join/rejoin', 'save/reload', 'four-player soak', 'native teardown',
]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, required=True)
    args = parser.parse_args()
    run = args.run.resolve(strict=True)
    assert args.run == run and os.environ.get('RUN') == str(run)
    assert run.parent == SOURCE.parent / 'rounds' and (run / 'contract.json').is_file()
    baseline = load(run / 'before.json')
    receipts, commands = {}, []
    for name in COMMANDS:
        receipt = load(run / name / 'receipt.json')
        expected_exit = 1 if name == 'callbacks-red' else 0
        assert receipt['exit_code'] == receipt['child_exit_code'] == expected_exit, name
        assert not receipt.get('timed_out') and receipt['owned_pid'] > 0, name
        assert receipt['cwd'] == str(SOURCE)
        assert receipt['output_sha256'] == digest(run / name / 'output.log'), name
        text = (run / name / 'output.log').read_text()
        count = re.search(r'Total tests: (\d+)', text) or re.search(r'Ran (\d+) tests', text)
        commands.append(dict(name=name, command=receipt['command'], exit_code=expected_exit,
                             tests=int(count[1]) if count else None, timeout=receipt['timeout_seconds'],
                             cleanup=receipt['cleanup'], output_sha256=receipt['output_sha256'],
                             receipt_sha256=digest(run / name / 'receipt.json')))
        receipts[name] = receipt
    red, green = receipts['callbacks-red'], receipts['callbacks-green']
    red_log = (run / 'callbacks-red/output.log').read_text()
    assert 'Failed: 2' in red_log and 'Passed: 96' in red_log
    assert 'System.InvalidOperationException: train coordinator escape' in red_log
    assert 'System.ArgumentException: original inner cause' in red_log
    for role in ('Hosting', 'Connected'):
        assert 'Failed WorldSyncCallbacks.Tests.FixedUpdateTests.' in red_log
        assert 'TrainEscapeIsBoundedAndDoesNotPauseHealthyFramesOrSessionRelease(role: ' + role + ')' in red_log
    assert 'Passed: 98' in (run / 'callbacks-green/output.log').read_text()
    for name in ('callbacks-expanded', 'callbacks-final'):
        assert 'Passed: 116' in (run / name / 'output.log').read_text()
    assert '0 Warning(s)' in (run / 'core-final/output.log').read_text()
    assert '0 Error(s)' in (run / 'core-final/output.log').read_text()
    same_tests = {}
    for path, expected in red['before_sha256'].items():
        if path.startswith(TESTS) and not path.endswith('.dll'):
            assert expected == red['after_sha256'][path] == green['before_sha256'][path] == green['after_sha256'][path]
            assert expected == digest(SOURCE / path), path
            same_tests[path] = expected
    baseline_tests = {}
    for path, expected in baseline.items():
        if path.startswith(TESTS) and path.endswith('Tests.cs') or path == 'tools/tests/test_s09_callback_wiring.py':
            assert expected == digest(SOURCE / path), path
            baseline_tests[path] = expected

    # Pin the actual red source to an exact method relocation from this RUN's
    # assignment baseline, never to a synthetic faulty implementation.
    original = {}
    for path in (WORLD, CALLBACKS):
        saved = run / 'baseline' / Path(path).name
        assert digest(saved) == baseline[path], path
        original[path] = saved.read_text()
    fixed = extract(original[WORLD], '        private void FixedUpdate()')
    red_callbacks = original[CALLBACKS].replace('        private void LateUpdate()', fixed + '\n\n        private void LateUpdate()')
    red_world = original[WORLD].replace(fixed + '\n\n\n', '')
    for path, text in ((WORLD, red_world), (CALLBACKS, red_callbacks)):
        assert hashlib.sha256(text.encode()).hexdigest() == red['before_sha256'][path] == red['after_sha256'][path], path
    assert red['after_sha256'][CALLBACKS] != green['after_sha256'][CALLBACKS] == digest(SOURCE / CALLBACKS)
    for signature in ('private void OnDestroy()', 'private void WatchLevelChanges()', 'private void ReleaseEverything()'):
        current = extract((SOURCE / WORLD).read_text(), signature).replace('            ResetFixedUpdateErrors();\n', '')
        assert current == extract(original[WORLD], signature), signature
    # Unselected callbacks and shared policy/diagnostics have no behavioral edits.
    for signature in ('private void Update()', 'private void LateUpdate()', 'private sealed class CallbackFailure',
                      'private void HandleSyncError(', 'private void ResetLateUpdateErrors()'):
        assert extract((SOURCE / CALLBACKS).read_text(), signature) == extract(original[CALLBACKS], signature), signature
    reset = extract((SOURCE / CALLBACKS).read_text(), 'private void ResetSyncErrors()')
    assert reset.replace('            ResetFixedUpdateErrors();\n', '') == extract(original[CALLBACKS], 'private void ResetSyncErrors()')

    final = {}
    for name in FINAL:
        for path, expected in receipts[name]['after_sha256'].items():
            if path.endswith('.dll'):
                continue
            assert expected == digest(SOURCE / path), 'Source changed after final check: ' + path
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
    preserved = {}
    for path in baseline:
        if path.startswith(('catalog/', 'protocol/', 'src/WinterMP.Net/', 'src/WinterMP.Core/Session/',
                            'src/WinterMP.Core/Sync/TrainSync', 'src/WinterMP.Core/Sync/VehicleWorldSync',
                            'src/WinterMP.Core/Sync/Guest')) or path in (
                                'src/WinterMP.Core/Sync/WorldSyncManager.Update.cs',
                                'src/WinterMP.Core/Sync/WorldSyncManager.Handlers.cs',
                                'src/WinterMP.Core/Sync/WorldSyncManager.Snapshots.cs'):
            assert path in unchanged, path
            preserved[path] = baseline[path]
    binaries = {}
    for path, name in (
        (TESTS + 'bin/Release/net8.0/WorldSyncCallbacks.Tests.dll', 'callbacks-final'),
        ('src/WinterMP.Core/bin/Release/net35/WinterMP.Core.dll', 'core-final'),
        ('src/WinterMP.Net/bin/Release/net35/WinterMP.Net.dll', 'core-final'),
        ('src/WinterMP.Net/bin/Release/netstandard2.0/WinterMP.Net.dll', 'net-final'),
        ('src/WinterMP.Net.Tests/bin/Release/net8.0/WinterMP.Net.Tests.dll', 'net-final')):
        actual = digest(SOURCE / path)
        assert actual == receipts[name]['after_sha256'][path], path
        binaries[path] = actual
    retain(run / 'WorldSyncManager.Callbacks.red.cs.txt', red_callbacks)
    for path in (WORLD, CALLBACKS):
        retain(run / (Path(path).name + '.diff'), ''.join(difflib.unified_diff(
            original[path].splitlines(keepends=True), (SOURCE / path).read_text().splitlines(keepends=True),
            fromfile='baseline/' + path, tofile='current/' + path)))
    report = dict(
        commands=commands, changed_files_sha256=changed, unchanged_baseline_files=unchanged, baseline_deletions=[],
        binary_sha256=binaries, byte_identical_red_green_tests_sha256=same_tests,
        byte_identical_assignment_baseline_tests_sha256=baseline_tests, preserved_gameplay_sha256=preserved,
        exact_red_baseline_fixed_extraction_sha256=hashlib.sha256(fixed.encode()).hexdigest(),
        baseline_cleanup_unchanged_except_local_reset_hooks=True,
        baseline_update_lateupdate_common_policy_and_diagnostics_unchanged=True,
        evidence_level='Portable production FixedUpdate/Update/UpdateWorldSync/LateUpdate/error/reset methods; Unity, train, sessions, subsystems, discovery and native release are doubles. Scene/destruction/native cleanup wiring is static evidence, not native execution. Core net35 compilation only.',
        protected_input='NOT_TESTED: no protected log/save content access or current equality check; historical provenance remains unresolved.',
        cleanup='Owned bounded portable children waited; no native process, rig resource, deployment, protected write, controller edit, commit or publication.',
        remaining_families=[
            'No additional Core coordinator FixedUpdate sibling found; TrainSync native admission/body/Disable/Clear and diagnostic failures remain unproved',
            'WorldSyncManager.OnTrainState -> EnsureSyncReady/TrainSync.Receive, train snapshot and player-admission fanout; packet dispatch catches errors but has no bounded handler budget',
            'Update prerequisites and all nonvehicle Update callbacks',
            'Native teardown fanout in OnDestroy/WatchLevelChanges/ReleaseEverything'],
        next_bounded_step='Audit train OnTrainState/snapshot failure family and its validation/result ordering before a selected production-linked regression; no blanket handler retries.',
        not_tested=NOT_TESTED, ledger_verified=False)
    retain(run / 'verification.json', json.dumps(report, indent=2) + '\n')
    print(json.dumps(dict(verification=str(run / 'verification.json'), changed_files=sorted(changed),
                          commands=[{k: c[k] for k in ('name', 'exit_code', 'tests')} for c in commands],
                          binary_sha256=binaries), indent=2))


if __name__ == '__main__':
    main()
