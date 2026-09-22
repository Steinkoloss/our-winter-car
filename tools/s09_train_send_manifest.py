#!/usr/bin/env python3
"""Verify fresh Train snapshot-send receipts and baseline identity; no native/protected input.

This is task-specific evidence tooling, not a controller or a substitute for the
independent review gates. --out must be a new directory beneath the assigned RUN.
"""
import argparse
import datetime
import difflib
import hashlib
import json
import os
from pathlib import Path
import re
from s09_update_manifest import extract

SOURCE = Path(__file__).resolve().parents[1]
SESSION = 'src/WinterMP.Core/Session/'
TEST = 'tools/TrainSend.Tests/'
ISOLATION = 'tools/TrainDispatch.Tests/IsolationTests.cs'
SELECTED = 'SelectedTrainSendFailureContinuesHealthyResyncWithoutRetry'
FALLBACK = 'ExistingSessionFallbackContainsTransportFailureWithoutAnyRetry'
FIXTURE_ADDITION = ('            // No Train output: this preserves the original all-send-failure fallback assertions.\n'
                    '            world._train.OnSnapshot = () => null;\n')
CHANGED = {
    ISOLATION, TEST + 'FallbackTests.cs', 'docs/S09-TRAIN-SEND-CONTAINMENT.md',
    'tools/s09_train_send_manifest.py', 'tools/tests/test_s09_train_send_contract.py',
}
FINAL = ('send-final', 'train-final', 'callbacks-final', 'net-final', 'launcher-final',
         'authority-final', 'python-final', 'core-final')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def test_path(path):
    return path.startswith(('src/WinterMP.Net.Tests/', 'src/WinterMP.Launcher.Tests/', 'tools/tests/')) or bool(re.match(r'tools/[^/]+\.Tests/', path))


def verify_isolation_revision(original, current):
    """Permit one new regression and one explicit fixture line, never an assertion edit."""
    signature = '        [Fact]\n        public void ' + SELECTED + '()'
    assert current.count(signature) == 1
    added = extract(current, signature) + '\n\n'
    assert current.count(FIXTURE_ADDITION) == 1
    restored = current.replace(added, '', 1).replace(FIXTURE_ADDITION, '', 1)
    assert restored == original, 'Unrelated baseline test/assertion changed'
    signature = '        public void ' + FALLBACK + '()'
    old_fallback = extract(original, signature)
    new_fallback = extract(current, signature)
    assert new_fallback.replace(FIXTURE_ADDITION, '', 1) == old_fallback
    return dict(original_fallback_sha256=hashlib.sha256(old_fallback.encode()).hexdigest(),
                new_regression_sha256=hashlib.sha256(added.encode()).hexdigest(),
                unchanged_assertions=True, fixture_delta=FIXTURE_ADDITION)


def verify_test_result(log, total, failures=()):
    assert re.search(r'^Total tests: ' + str(total) + r'$', log, re.M)
    assert re.search(r'^\s+Passed: ' + str(total - len(failures)) + r'$', log, re.M)
    actual = re.findall(r'^\[xUnit.net [^\]]+\]\s+(\S.*) \[FAIL\]$', log, re.M)
    assert sorted(actual) == sorted(failures), actual
    assert 'Skipped:' not in log
    assert ('Test Run Failed.' if failures else 'Test Run Successful.') in log


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--run', required=True, type=Path)
    p.add_argument('--out', required=True, type=Path)
    a = p.parse_args()
    run = a.run.resolve(strict=True)
    assert run.parent == SOURCE.parent / 'rounds' and (run / 'contract.json').is_file()
    out = a.out.resolve()
    assert out.is_relative_to(run) and out != run and not out.exists()
    before = json.loads((run / 'before.json').read_text())
    receipts, commands = {}, []
    for name in ('train-baseline', 'send-red', 'train-red', 'send-green', 'train-green') + FINAL:
        r = json.loads((run / name / 'receipt.json').read_text())
        expected_exit = 1 if name in ('train-baseline', 'send-red', 'train-red') else 0
        assert r['exit_code'] == r['child_exit_code'] == expected_exit, name
        assert not r.get('timed_out') and r['owned_pid'] > 0 and r['cwd'] == str(SOURCE)
        suite = name.split('-')[0]
        projects = dict(send=TEST.rstrip('/'), train='tools/TrainDispatch.Tests', callbacks='tools/WorldSyncCallbacks.Tests',
                        net='src/WinterMP.Net.Tests', launcher='src/WinterMP.Launcher.Tests', authority='tools/PaneScrapeBridge.Tests')
        if suite in projects:
            verbosity = 'normal' if suite in ('net', 'launcher') else 'detailed'
            assert r['command'] == ['dotnet', 'test', projects[suite], '-c', 'Release', '-p:DeployToGame=false',
                                    '--logger', 'console;verbosity=' + verbosity], name
        elif suite == 'python':
            assert r['command'] == ['python3', '-B', '-m', 'unittest', 'discover', '-s', 'tools/tests', '-v']
        else:
            assert suite == 'core'
            assert r['command'] == ['dotnet', 'build', 'src/WinterMP.Core/WinterMP.Core.csproj', '-c', 'Release',
                                    '-p:DeployToGame=false', '-p:MwcGamePath=/home/jaimep/.steam/root/steamapps/common/My Winter Car']
        assert digest(run / name / 'output.log') == r['output_sha256']
        log = (run / name / 'output.log').read_text()
        total = re.search(r'Total tests: (\d+)', log) or re.search(r'Ran (\d+) tests', log)
        commands.append(dict(name=name, command=r['command'], exit_code=expected_exit,
                             tests=int(total[1]) if total else None, owned_pid=r['owned_pid'],
                             timeout_seconds=r['timeout_seconds'], receipt_sha256=digest(run / name / 'receipt.json'),
                             output_sha256=r['output_sha256']))
        receipts[name] = r
    red = receipts['send-red']; green = receipts['send-green']
    log = (run / 'send-red/output.log').read_text()
    failures = []
    for assertion in ('TrainSendFailurePreservesLaterChunksRecipientsOrderingAndException',
                      'RealTrainEncodingFailureDoesNotContaminateNextPayload', 'AmbiguousPostDeliveryExceptionNeverRetriesTrain'):
        for kind in range(3):
            failures.append('TrainSend.Tests.SendTests.' + assertion + '(kind: ' + str(kind) + ')')
    failures += ['TrainSend.Tests.FallbackTests.EveryMessageTransportFailureStopsAtFirstNonTrainWithoutRetry(kind: ' + str(kind) + ')' for kind in (1, 2)]
    verify_test_result(log, 22, failures)
    for detail in ('Error handling WorldSnapshotRequest', 'Error handling WorldResyncRequest',
                   'Error handling WorldObjectStateRequest', 'selected TrainState send failure',
                   'System.ArgumentException: original transport cause', 'WinterMP.Net.ProtocolException: Invalid train state.',
                   'SessionManager.SendTo', 'Assert.Equal() Failure: Collections differ'):
        assert detail in log, detail
    for name in ('send-green', 'send-final'):
        verify_test_result((run / name / 'output.log').read_text(), 22)
    verify_test_result((run / 'train-baseline/output.log').read_text(), 29, ['TrainDispatch.Tests.IsolationTests.' + FALLBACK])
    verify_test_result((run / 'train-red/output.log').read_text(), 30, ['TrainDispatch.Tests.IsolationTests.' + SELECTED])
    for name in ('train-red', 'train-green', 'train-final'):
        assert 'Passed TrainDispatch.Tests.IsolationTests.' + FALLBACK in (run / name / 'output.log').read_text()
    for name, count in (('train-green', 30), ('train-final', 30), ('callbacks-final', 119), ('net-final', 5309),
                        ('launcher-final', 20), ('authority-final', 84)):
        verify_test_result((run / name / 'output.log').read_text(), count)
    core = (run / 'core-final/output.log').read_text()
    assert '0 Error(s)' in core and 'net35/WinterMP.Core.dll' in core
    python = (run / 'python-final/output.log').read_text()
    assert re.search(r'^Ran 175 tests in ', python, re.M) and re.search(r'^OK$', python, re.M)
    redgreen_tests = {}
    for path, sha in red['before_sha256'].items():
        if (test_path(path) and not path.endswith('.dll')) or path in ('tools/s09_train_fixture.py', 'tools/s09_train_send_fixture.py'):
            assert sha == red['after_sha256'][path] == green['before_sha256'][path] == green['after_sha256'][path] == digest(SOURCE / path), path
            redgreen_tests[path] = sha
    baseline_tests, changes = {}, {}
    original_tests = (run / 'baseline/IsolationTests.cs').read_text()
    assert digest(run / 'baseline/IsolationTests.cs') == before[ISOLATION]
    revision = verify_isolation_revision(original_tests, (SOURCE / ISOLATION).read_text())
    for path, sha in before.items():
        current = digest(SOURCE / path)  # deleted baseline files are failures
        if current != sha:
            changes[path] = current
        if test_path(path) and path != ISOLATION:
            assert current == sha, 'Baseline test changed: ' + path
            baseline_tests[path] = sha
    for root in ('src', 'tools', 'docs', 'catalog', 'protocol'):
        for f in (SOURCE / root).rglob('*'):
            if not f.is_file() or f.is_symlink() or set(f.parts) & {'bin', 'obj', '__pycache__'}:
                continue
            path = str(f.relative_to(SOURCE))
            if path not in before:
                changes[path] = digest(f)
    assert set(changes) == CHANGED, sorted(set(changes) ^ CHANGED)
    for name in FINAL:
        for path, sha in receipts[name]['after_sha256'].items():
            if not path.endswith('.dll'):
                assert sha == digest(SOURCE / path), 'Source changed after final check: ' + path
    original = (run / 'baseline/SessionManager.Handlers.pre-fix.cs').read_text()
    path = SESSION + 'SessionManager.Handlers.cs'
    assert digest(run / 'baseline/SessionManager.Handlers.cs') == before[path] == digest(SOURCE / path)
    assert digest(run / 'baseline/SessionManager.Handlers.pre-fix.cs') == red['before_sha256'][path] == red['after_sha256'][path]
    current = (SOURCE / path).read_text()
    restored = current.replace('if (TrySendSnapshotMessage(peer, chunk, Channel.ReliableOrdered)) messages++;',
                               'SendTo(peer, chunk, Channel.ReliableOrdered);\n                messages++;')
    restored = restored.replace('if (TrySendSnapshotMessage(peer, message, channel)) messages++;',
                                'SendTo(peer, message, channel);\n                messages++;')
    assert restored == original, 'Unrelated handler/order change'
    helper = SESSION + 'SessionManager.Snapshots.cs'
    assert digest(run / 'baseline/SessionManager.Snapshots.cs') == before[helper] == digest(SOURCE / helper)
    assert before[helper] == red['before_sha256'][helper] == red['after_sha256'][helper]
    # Red removes only the three calls to the inherited helper. Every other game
    # source and all tests/generators are identical, including non-Train fallback.
    for key, sha in red['before_sha256'].items():
        if not key.endswith('.dll') and (key.startswith('src/') or test_path(key) or key in ('tools/s09_train_fixture.py', 'tools/s09_train_send_fixture.py')):
            assert red['after_sha256'][key] == sha
            if key != path:
                assert green['before_sha256'][key] == green['after_sha256'][key] == digest(SOURCE / key) == sha, key
            for name in ('train-red',):
                assert receipts[name]['before_sha256'][key] == receipts[name]['after_sha256'][key] == sha, key
    for key, sha in before.items():
        if test_path(key):
            assert receipts['train-baseline']['before_sha256'][key] == receipts['train-baseline']['after_sha256'][key] == sha
    assert digest(run / 'baseline/SessionManager.cs') == before[SESSION + 'SessionManager.cs'] == digest(SOURCE / (SESSION + 'SessionManager.cs'))
    preserved = {path: sha for path, sha in before.items() if path.startswith(('src/', 'catalog/', 'protocol/')) and path not in CHANGED}
    assert all(digest(SOURCE / path) == sha for path, sha in preserved.items())
    binaries = {path: sha for path, sha in receipts['core-final']['after_sha256'].items() if path.endswith('.dll')}
    for path, sha in binaries.items():
        assert sha == digest(SOURCE / path), 'Binary drift: ' + path
    for name, path in (
        ('send-final', TEST + 'bin/Release/net8.0/TrainSend.Tests.dll'),
        ('train-final', 'tools/TrainDispatch.Tests/bin/Release/net8.0/TrainDispatch.Tests.dll'),
        ('callbacks-final', 'tools/WorldSyncCallbacks.Tests/bin/Release/net8.0/WorldSyncCallbacks.Tests.dll'),
        ('authority-final', 'tools/PaneScrapeBridge.Tests/bin/Release/net8.0/PaneScrapeBridge.Tests.dll'),
        ('net-final', 'src/WinterMP.Net.Tests/bin/Release/net8.0/WinterMP.Net.Tests.dll'),
    ):
        assert receipts[name]['after_sha256'][path] == binaries[path], name
    test_binary = TEST + 'bin/Release/net8.0/TrainSend.Tests.dll'
    assert test_binary in red['after_sha256'] and test_binary in green['after_sha256']
    # Receipt children each own a new POSIX session. Inspect only those sessions;
    # never enumerate/kill unrelated Wine/Steam/game sessions.
    owned = {r['owned_pid'] for r in receipts.values()}
    remaining = []
    for entry in Path('/proc').iterdir():
        if not entry.name.isdigit():
            continue
        try:
            pid = int(entry.name)
            if os.getsid(pid) in owned:
                remaining.append(dict(pid=pid, session=os.getsid(pid)))
        except (ProcessLookupError, PermissionError):
            pass
    assert not remaining, 'Receipt-owned process still live: ' + str(remaining)
    out.mkdir()
    (out / 'SessionManager.Handlers.cs.diff').write_text(''.join(difflib.unified_diff(original.splitlines(True), current.splitlines(True), fromfile='baseline/SessionManager.Handlers.cs', tofile='current/SessionManager.Handlers.cs')))
    (out / 'IsolationTests.cs.diff').write_text(''.join(difflib.unified_diff(original_tests.splitlines(True), (SOURCE / ISOLATION).read_text().splitlines(True), fromfile='baseline/IsolationTests.cs', tofile='current/IsolationTests.cs')))
    # Preserve actual generated source, not just claimed extraction provenance.
    generated = out / 'generated'; generated.mkdir()
    generated_hashes = {}
    for f in (SOURCE / (TEST + 'obj/Release/net8.0/production')).iterdir():
        if f.is_file():
            (generated / f.name).write_bytes(f.read_bytes())
            generated_hashes[f.name] = digest(f)
    cleanup = dict(checked_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                   receipt_owned_sessions=sorted(owned), remaining=remaining,
                   native_resources='NOT_CREATED; no native launch/deployment', native_teardown='NOT_TESTED')
    (out / 'cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
    result = dict(status='PASS_PARTIAL_PORTABLE_S09', commands=commands, changed_files_sha256=changes, binary_sha256=binaries,
                  red_binary_sha256=red['after_sha256'][test_binary], green_binary_sha256=green['after_sha256'][test_binary],
                  baseline_test_sha256=baseline_tests, byte_identical_red_green_tests_and_generators=redgreen_tests,
                  baseline_fixture_revision=revision,
                  preserved_gameplay_sha256=preserved, generated_sha256=generated_hashes,
                  production_diff='No net production change relative to inherited candidate. Fresh red removes exactly three iteration send/count helper calls; green restores the byte-identical TrainState-only helper and handlers. All game source, world iterators, SendTo, codec and ingress are unchanged.',
                  evidence_level='Portable exact selected production methods, real codec/TrainState/TrainSync.Receive, explicit engine/session/transport/producer doubles. Full object iterator static-only; object request handler dynamic.',
                  cleanup=cleanup,
                  not_tested=['native discovery/injected-state', 'protected-input equality/provenance', 'ordinary input',
                              'native host/guest actions, authority-once and matching peers', 'Steam/two-PC', 'different saves',
                              'fresh-player late join/rejoin', 'save/reload', 'four-player soak', 'native teardown'],
                  remaining_boundaries=['Live TrainSync.Update -> SendWorldMessage -> Broadcast encoding and peer-loop sends',
                                        'Non-Train producers/sends, snapshot adjuncts and forced broadcasts',
                                        'Shutdown disconnect fanout before Dispose/Reset; diagnostic sink failure and native cleanup fanout',
                                        'Full production object-state iterator not dynamically executed'],
                  next_bounded_step='Regression-test Shutdown disconnect-send failure skipping disposal/reset, preserving session/authority semantics and scoped cleanup; live broadcast remains separate.',
                  ledger_verified=False)
    (out / 'verification.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(dict(status='PASS', verification=str(out / 'verification.json'), changed_files=len(changes),
                          baseline_tests_byte_identical=len(baseline_tests), redgreen_files_byte_identical=len(redgreen_tests),
                          checks=commands, receipt_owned_sessions_empty=len(owned)), indent=2))


if __name__ == '__main__':
    main()
