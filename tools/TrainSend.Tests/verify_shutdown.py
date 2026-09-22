#!/usr/bin/env python3
"""Audit this bounded Shutdown slice's real receipts; never launches native processes."""
import argparse
import difflib
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

from shutdown_fixture import SOURCE, SIGNATURES, extract

CORE = 'src/WinterMP.Core/Session/SessionManager.cs'
FINAL = ('send-final', 'train-final', 'callbacks-final', 'net-final', 'launcher-final',
         'authority-final', 'python-final', 'core-final')
CHANGED = {
    CORE, 'tools/TrainSend.Tests/TrainSend.Tests.csproj',
    'tools/TrainSend.Tests/ShutdownTests.cs', 'tools/TrainSend.Tests/ShutdownDoubles.cs',
    'tools/TrainSend.Tests/shutdown_fixture.py', 'tools/TrainSend.Tests/verify_shutdown.py',
    'tools/tests/test_s09_shutdown_fixture.py', 'docs/S09-SHUTDOWN-CLEANUP.md',
}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    return json.loads(path.read_text())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', required=True, type=Path)
    args = parser.parse_args()
    run = args.run.resolve(strict=True)
    assert os.environ.get('RUN') == str(run)
    assert run.parent == SOURCE.parent / 'rounds' and (run / 'contract.json').is_file()
    baseline = load(run / 'before.json')
    receipts, commands = {}, []
    names = ('shutdown-red', 'shutdown-red-behavior', 'shutdown-green') + FINAL
    for name in names:
        receipt = load(run / name / 'receipt.json')
        expected_exit = 1 if name.startswith('shutdown-red') else 0
        assert receipt['exit_code'] == receipt['child_exit_code'] == expected_exit, name
        assert not receipt.get('timed_out') and receipt['owned_pid'] > 0, name
        assert receipt['cwd'] == str(SOURCE)
        assert receipt['output_sha256'] == digest(run / name / 'output.log')
        output = (run / name / 'output.log').read_text()
        count = re.search(r'Total tests: (\d+)', output) or re.search(r'Ran (\d+) tests', output)
        commands.append(dict(name=name, command=receipt['command'], exit_code=expected_exit,
                             tests=int(count[1]) if count else None, timeout=receipt['timeout_seconds'],
                             output_sha256=receipt['output_sha256'], receipt_sha256=digest(run / name / 'receipt.json')))
        receipts[name] = receipt
    red, green = receipts['shutdown-red-behavior'], receipts['shutdown-green']
    red_log = (run / 'shutdown-red-behavior/output.log').read_text()
    green_log = (run / 'shutdown-green/output.log').read_text()
    assert 'Passed: 25' in red_log and 'Failed: 8' in red_log and 'Total tests: 33' in red_log
    assert 'observed: dispose=0, hasTransport=True, peers=3, pending=True, state=Hosting' in red_log
    assert 'state=Connected' in red_log and 'ShutdownTests.AssertCleanup' in red_log
    assert 'Passed: 33' in green_log and 'Total tests: 33' in green_log
    assert 'observed: dispose=1, hasTransport=False, peers=0, pending=False, state=Idle' in green_log
    assert red['before_sha256'][CORE] == red['after_sha256'][CORE] == baseline[CORE]
    assert green['before_sha256'][CORE] == green['after_sha256'][CORE] == digest(SOURCE / CORE)
    stable_tests = {}
    for path, value in red['before_sha256'].items():
        if path.startswith('tools/TrainSend.Tests/') and not path.endswith('.dll'):
            assert value == red['after_sha256'][path] == green['before_sha256'][path] == green['after_sha256'][path] == digest(SOURCE / path), path
            stable_tests[path] = value
    assert 'tools/TrainSend.Tests/ShutdownTests.cs' in stable_tests
    original = (run / 'baseline/SessionManager.cs').read_text()
    current = (SOURCE / CORE).read_text()
    assert digest(run / 'baseline/SessionManager.cs') == baseline[CORE]
    before_method = extract(original, 'public void Shutdown(')
    after_method = extract(current, 'public void Shutdown(')
    assert original == current.replace(after_method, before_method), 'Production diff escaped Shutdown'
    for signature in SIGNATURES[1:]:
        assert extract(original, signature) == extract(current, signature), signature
    current_sources = receipts['core-final']['after_sha256']
    actual_changes = set()
    for path, value in current_sources.items():
        if path.endswith('.dll'):
            continue
        assert digest(SOURCE / path) == value, path
        if baseline.get(path) != value:
            actual_changes.add(path)
    assert actual_changes == CHANGED, (actual_changes - CHANGED, CHANGED - actual_changes)
    for name in FINAL:
        for path, value in receipts[name]['after_sha256'].items():
            if not path.endswith('.dll'):
                assert digest(SOURCE / path) == value, (name, path)
    produced = {
        'core-final': ['src/WinterMP.Core/bin/Release/net35/WinterMP.Core.dll', 'src/WinterMP.Net/bin/Release/net35/WinterMP.Net.dll'],
        'net-final': ['src/WinterMP.Net.Tests/bin/Release/net8.0/WinterMP.Net.Tests.dll', 'src/WinterMP.Net/bin/Release/netstandard2.0/WinterMP.Net.dll'],
        'send-final': ['tools/TrainSend.Tests/bin/Release/net8.0/TrainSend.Tests.dll'],
        'train-final': ['tools/TrainDispatch.Tests/bin/Release/net8.0/TrainDispatch.Tests.dll'],
        'callbacks-final': ['tools/WorldSyncCallbacks.Tests/bin/Release/net8.0/WorldSyncCallbacks.Tests.dll'],
        'authority-final': ['tools/PaneScrapeBridge.Tests/bin/Release/net8.0/PaneScrapeBridge.Tests.dll'],
    }
    binaries = {}
    for name, paths in produced.items():
        for path in paths:
            assert digest(SOURCE / path) == receipts[name]['after_sha256'][path], (name, path)
            binaries[path] = digest(SOURCE / path)
    # Source hash inventory from receipts excludes generated outputs. Retain the
    # exact compiled lifecycle extraction separately and compare every method.
    generated = SOURCE / 'tools/TrainSend.Tests/obj/Release/net8.0/production/Shutdown.g.cs'
    for signature in SIGNATURES:
        assert extract(generated.read_text(), signature) == extract(current, signature)
    shutil.copyfile(generated, run / 'Shutdown.compiled.cs')
    (run / 'production.diff').write_text(''.join(difflib.unified_diff(original.splitlines(True), current.splitlines(True), fromfile='assignment/SessionManager.cs', tofile='candidate/SessionManager.cs')))
    owned_ids = sorted({r['owned_pid'] for r in receipts.values()})
    ps = subprocess.run(['ps', '-s', ','.join(map(str, owned_ids)), '-o', 'pid,ppid,pgid,sid,stat,args'], capture_output=True, text=True)
    (run / 'owned-final-ps.log').write_text(ps.stdout + ps.stderr)
    assert ps.returncode in (0, 1), ps.stderr
    assert len(ps.stdout.splitlines()) == 1, 'Receipt-owned descendants remain; inspect owned-final-ps.log'
    cleanup = {'owned_session_ids': owned_ids, 'remaining_owned_processes': [], 'ps_exit_code': ps.returncode,
               'native_processes_rig_deployment_saves': 'NOT_CREATED', 'native_teardown': 'NOT_TESTED'}
    (run / 'cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
    result = {
        'status': 'PASS_PARTIAL_PORTABLE_S09_SHUTDOWN', 'commands': commands,
        'changed_paths_sha256': {path: digest(SOURCE / path) for path in sorted(CHANGED)},
        'behavior_fixtures_identical_red_green_current': stable_tests,
        'production_only_shutdown_changed': True,
        'red_core_sha256': red['after_sha256'][CORE], 'green_core_sha256': digest(SOURCE / CORE),
        'red_fixture_binary_sha256': red['after_sha256']['tools/TrainSend.Tests/bin/Release/net8.0/TrainSend.Tests.dll'],
        'green_fixture_binary_sha256': green['after_sha256']['tools/TrainSend.Tests/bin/Release/net8.0/TrainSend.Tests.dll'],
        'final_binary_sha256': binaries,
        'compiled_fixture_sha256': digest(generated), 'cleanup': cleanup,
        'assertions': [
            'Baseline failure: zero disposals, retained transport/peers/pending state, not Idle',
            'First/middle/last disconnect failure, pre/post delivery: original exception preserved, ordered prefix only, never retry',
            'Dispose/reset/Idle once; real join/seat/death/link state cleared; detached send inert',
            'Normal peers once in original enumeration order; empty sessions safe',
            'Same-manager fresh host/guest-role transport Train/Ping sends use real codec; no stale transport; clean next shutdown',
        ],
        'not_tested': ['native discovery/injected state', 'ordinary/protected input', 'native host/guest action/authority/matching',
                       'Steam/two-PC', 'different saves', 'late join/rejoin', 'save/reload', 'four-player soak',
                       'native teardown', 'exceptions inside cleanup operations', 'reentrant transport callbacks', 'live Broadcast'],
    }
    (run / 'verification.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({'status': result['status'], 'changed_paths': len(CHANGED), 'commands': commands, 'cleanup': cleanup}, indent=2))


if __name__ == '__main__':
    main()
