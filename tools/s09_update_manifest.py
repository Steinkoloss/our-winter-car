#!/usr/bin/env python3
"""Validate bounded S09 vehicle Update evidence in the assigned RUN; no native access."""
import argparse
import difflib
import hashlib
import os
from pathlib import Path
import re
import json
from s09_portable_manifest import digest, load, retain

SOURCE = Path(__file__).resolve().parents[1]
WORLD = 'src/WinterMP.Core/Sync/WorldSyncManager.cs'
CALLBACKS = 'src/WinterMP.Core/Sync/WorldSyncManager.Callbacks.cs'
UPDATE = 'src/WinterMP.Core/Sync/WorldSyncManager.Update.cs'
EXPECTED_CHANGES = {
    WORLD, CALLBACKS, UPDATE, 'PLAN.md', 'docs/S09-LATEUPDATE-CONTAINMENT.md', 'docs/S09-UPDATE-CONTAINMENT.md',
    'tools/WorldSyncCallbacks.Tests/WorldSyncCallbacks.Tests.csproj',
    'tools/WorldSyncCallbacks.Tests/CallbackDoubles.cs', 'tools/WorldSyncCallbacks.Tests/UpdateDoubles.cs',
    'tools/WorldSyncCallbacks.Tests/UpdateTests.cs', 'tools/WorldSyncCallbacks.Tests/UpdateRetryTests.cs',
    'tools/tests/test_s09_callback_wiring.py', 'tools/v11_portable_receipt.py', 'tools/s09_update_manifest.py',
}


def extract(text, signature):
    start = text.index(signature)
    brace = text.index('{', start)
    end, depth = brace + 1, 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[start:end]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', required=True, type=Path)
    args = parser.parse_args()
    run = args.run.resolve(strict=True)
    assert args.run == run and os.environ.get('RUN') == str(run)
    assert run.parent == SOURCE.parent / 'rounds' and (run / 'contract.json').is_file()
    baseline = load(run / 'before.json')
    receipts, commands = {}, []
    for name in ('callbacks-red', 'callbacks-green', 'callbacks-expanded', 'python-initial',
                 'callbacks-final', 'net-final', 'launcher-final', 'python-final', 'core-final'):
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
    same_tests = {}
    for path, expected in red['before_sha256'].items():
        if path.startswith('tools/WorldSyncCallbacks.Tests/'):
            assert expected == red['after_sha256'][path] == green['before_sha256'][path] == green['after_sha256'][path]
            assert expected == digest(SOURCE / path), path
            same_tests[path] = expected
    assert len(same_tests) == 6, same_tests
    assert 'Failed: 22' in (run / 'callbacks-red/output.log').read_text()
    assert 'Passed: 36' in (run / 'callbacks-red/output.log').read_text()
    assert 'Passed: 58' in (run / 'callbacks-green/output.log').read_text()
    assert red['after_sha256'][UPDATE] != green['after_sha256'][UPDATE] == digest(SOURCE / UPDATE)
    assert red['after_sha256'][CALLBACKS] == baseline[CALLBACKS]
    for path in ('tools/WorldSyncCallbacks.Tests/CallbackTests.cs', 'tools/WorldSyncCallbacks.Tests/RetryTests.cs'):
        assert digest(SOURCE / path) == baseline[path], path

    # Verify the red production Update is an exact extraction, not an invented
    # failing implementation. Archive the baseline and extraction with their hashes.
    archive = SOURCE.parent / 'checkpoints/000051-work/files'
    assert digest(archive / WORLD) == baseline[WORLD]
    original = (archive / WORLD).read_text()
    body = extract(original, '        private void UpdateWorldSync()')
    red_update = ('using UnityEngine;\nusing WinterMP.Core.Session;\nusing WinterMP.Net;\n'
                  'using WinterMP.Net.Messages;\n\nnamespace WinterMP.Core.Sync\n{\n'
                  '    public sealed partial class WorldSyncManager\n    {\n' + body + '\n    }\n}\n')
    red_update_hash = hashlib.sha256(red_update.encode()).hexdigest()
    assert red_update_hash == red['after_sha256'][UPDATE]
    for signature in ('private void OnDestroy()', 'private void WatchLevelChanges()', 'private void ReleaseEverything()',
                      'private void FixedUpdate()'):
        before = extract(original, signature)
        after = extract((SOURCE / WORLD).read_text(), signature).replace('            ResetVehicleUpdateErrors();\n', '')
        assert before == after, signature

    final = {}
    for name in ('callbacks-final', 'net-final', 'launcher-final', 'python-final', 'core-final'):
        for path, expected in receipts[name]['after_sha256'].items():
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
    for path in baseline:
        if (path.startswith(('catalog/', 'protocol/', 'src/WinterMP.Net/', 'src/WinterMP.Core/Session/',
                            'src/WinterMP.Core/Sync/VehicleWorldSync')) or
                path in ('src/WinterMP.Core/Sync/WorldSyncManager.Handlers.cs',
                         'src/WinterMP.Core/Sync/WorldSyncManager.Snapshots.cs', 'src/WinterMP.Core/Sync/TrainSync.cs')):
            assert path in unchanged, path

    binaries = {}
    for path, name in (
        ('tools/WorldSyncCallbacks.Tests/bin/Release/net8.0/WorldSyncCallbacks.Tests.dll', 'callbacks-final'),
        ('src/WinterMP.Core/bin/Release/net35/WinterMP.Core.dll', 'core-final'),
        ('src/WinterMP.Net/bin/Release/net35/WinterMP.Net.dll', 'core-final'),
        ('src/WinterMP.Net/bin/Release/netstandard2.0/WinterMP.Net.dll', 'net-final'),
        ('src/WinterMP.Net.Tests/bin/Release/net8.0/WinterMP.Net.Tests.dll', 'net-final')):
        actual = digest(SOURCE / path)
        assert actual == receipts[name]['after_sha256'][path], path
        binaries[path] = actual

    retain(run / 'WorldSyncManager.before.cs.txt', original)
    retain(run / 'WorldSyncManager.Update.red.cs.txt', red_update)
    for path in (WORLD, CALLBACKS, UPDATE):
        before = (archive / path).read_text() if path in baseline else red_update
        retain(run / (Path(path).name + '.diff'), ''.join(difflib.unified_diff(
            before.splitlines(keepends=True), (SOURCE / path).read_text().splitlines(keepends=True),
            fromfile='baseline-or-red/' + path, tofile='current/' + path)))
    report = dict(commands=commands, changed_files_sha256=changed, unchanged_baseline_files=unchanged,
                  baseline_deletions=[], binary_sha256=binaries, byte_identical_red_green_tests_sha256=same_tests,
                  exact_baseline_update_extraction_sha256=red_update_hash,
                  baseline_native_cleanup_and_fixedupdate_unchanged=True,
                  evidence_level='Portable production Update/UpdateWorldSync/LateUpdate coordinator; engine/session/subsystems/discovery/native cleanup and message sinks are doubles. Core net35 compilation only.',
                  protected_input='NOT_TESTED: no protected log/save content access or current content equality check; historical provenance unresolved, no native permission inferred.',
                  cleanup='Owned bounded portable children waited; no native process, rig resource, deployment, protected write, controller edit, commit or publication.',
                  remaining_families=['Update prerequisites/discovery/control publication and all nonvehicle Update callbacks',
                                      'FixedUpdate TrainSync coordinator escapes', 'message handler / snapshot / player-admission fanout',
                                      'native teardown exception containment'],
                  not_tested=['native discovery/injected-state fixtures', 'protected-input content equality', 'ordinary input',
                              'native host/guest action/once-only authority/result convergence', 'Steam/two-PC', 'different saves',
                              'fresh-player late join/rejoin', 'save/reload', 'four-player soak', 'native teardown'])
    retain(run / 'verification.json', json.dumps(report, indent=2) + '\n')
    print(json.dumps(dict(verification=str(run / 'verification.json'), changed_files=sorted(changed),
                          commands=[{k: c[k] for k in ('name', 'exit_code', 'tests')} for c in commands],
                          binary_sha256=binaries), indent=2))


if __name__ == '__main__':
    main()
