#!/usr/bin/env python3
"""Audit assigned S09 shutdown portable receipts; not a native/controller gate."""
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
SESSION = 'src/WinterMP.Core/Session/SessionManager.cs'
TEST = 'tools/TrainSend.Tests/'
CHANGED = {
    TEST + 'ShutdownTests.cs', TEST + 'ShutdownDoubles.cs', TEST + 'shutdown_fixture.py',
    'tools/tests/test_s09_shutdown_fixture.py', 'tools/tests/test_s09_shutdown_manifest.py',
    'tools/s09_shutdown_manifest.py', 'docs/S09-SHUTDOWN-CLEANUP.md',
    'docs/S09-TRAIN-SEND-CONTAINMENT.md',
}
PROJECTS = {
    'shutdown-final': 'tools/TrainSend.Tests', 'train-final': 'tools/TrainDispatch.Tests',
    'callbacks-final': 'tools/WorldSyncCallbacks.Tests', 'net-final': 'src/WinterMP.Net.Tests',
    'launcher-final': 'src/WinterMP.Launcher.Tests', 'authority-final': 'tools/PaneScrapeBridge.Tests',
}
FINAL = tuple(PROJECTS) + ('python-final', 'core-final')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def dotnet_result(log, failures=()):
    """Require a complete unskipped English/German test summary and exact failures."""
    def number(label):
        values = re.findall(r'^\s*(?:' + label + r'): (\d+)\s*$', log, re.M)
        assert len(values) == 1, (label, values)
        return int(values[0])
    total = number('Total tests|Gesamtzahl Tests')
    passed = number('Passed|Bestanden')
    failed = re.findall(r'^\[xUnit.net [^\]]+\]\s+(\S.*) \[FAIL\]$', log, re.M)
    assert sorted(failed) == sorted(failures), failed
    assert not re.search(r'^\s*(Skipped|Übersprungen):', log, re.M)
    assert total > 0 and total == passed + len(failures), (total, passed, failures)
    if failures:
        assert number('Failed|Nicht bestanden') == len(failures)
        assert 'Test Run Failed.' in log or 'Fehler beim Testlauf.' in log
    else:
        assert not re.search(r'^\s*(Failed|Nicht bestanden):', log, re.M)
        assert 'Test Run Successful.' in log or 'Der Testlauf war erfolgreich.' in log
    return dict(total=total, passed=passed, failed=len(failures))


def red_failures():
    prefix = 'TrainSend.Tests.ShutdownTests.'
    return ([prefix + 'DisconnectFailureStillDisposesResetsAndTransitionsIdleExactlyOnce(failureIndex: '
             + str(i) + ', afterDelivery: ' + delivery + ')' for i in range(3) for delivery in ('False', 'True')]
            + [prefix + 'SameManagerCanAttachFreshSessionAndSendAfterFailedShutdown(role: ' + role + ')'
               for role in ('Hosting', 'Connected')]
            + [prefix + 'GuestDisconnectFailureClearsSingleHostAndCurrentClothingState(afterDelivery: ' + delivery + ')'
               for delivery in ('False', 'True')])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', required=True, type=Path)
    parser.add_argument('--out', required=True, type=Path)
    parser.add_argument('--final-suffix', default='', help='Append to final receipt names for a preserved rerun.')
    args = parser.parse_args()
    run = args.run.resolve(strict=True)
    out = args.out.resolve()
    assert run.parent == SOURCE.parent / 'rounds' and (run / 'contract.json').is_file()
    assert out.is_relative_to(run) and out != run and not out.exists()
    assert '/' not in args.final_suffix and '\\' not in args.final_suffix
    before = json.loads((run / 'before.json').read_text())
    receipts, logs, checks = {}, {}, []
    names = ('shutdown-baseline', 'capture-baseline', 'capture-red', 'shutdown-red',
             'shutdown-red-behavior', 'shutdown-green') + FINAL
    for name in names:
        leaf = name + args.final_suffix if name in FINAL else name
        receipt_path = run / leaf / 'receipt.json'
        r = json.loads(receipt_path.read_text())
        expected = 1 if name in ('shutdown-baseline', 'shutdown-red', 'shutdown-red-behavior') else 0
        assert r['exit_code'] == r['child_exit_code'] == expected, name
        assert r['cwd'] == str(SOURCE) and not r.get('timed_out') and r['owned_pid'] > 0
        assert r['started_utc'] <= r['ended_utc'] and r['cleanup']
        assert r['native'] == 'NOT_TESTED'
        log_path = run / leaf / 'output.log'
        assert digest(log_path) == r['output_sha256'], name
        log = log_path.read_text()
        if name in PROJECTS or name.startswith('shutdown-'):
            project = PROJECTS.get(name, 'tools/TrainSend.Tests')
            verbosity = 'normal' if name in ('net-final', 'launcher-final') else 'detailed'
            assert r['command'] == ['dotnet', 'test', project, '-c', 'Release', '-p:DeployToGame=false',
                                    '--logger', 'console;verbosity=' + verbosity], name
        elif name == 'python-final':
            assert r['command'] == ['python3', '-B', '-m', 'unittest', 'discover', '-s', 'tools/tests', '-v']
        elif name == 'core-final':
            assert r['command'] == ['dotnet', 'build', 'src/WinterMP.Core/WinterMP.Core.csproj', '-c', 'Release',
                                    '-p:DeployToGame=false', '-p:MwcGamePath=/home/jaimep/.steam/root/steamapps/common/My Winter Car']
        checks.append(dict(name=leaf, command=r['command'], exit_code=expected, roles=r['roles'],
                           started_utc=r['started_utc'], ended_utc=r['ended_utc'], owned_pid=r['owned_pid'],
                           receipt_sha256=digest(receipt_path), output_sha256=r['output_sha256']))
        receipts[name], logs[name] = r, log
    assert 'ResetClothingSession' in logs['shutdown-baseline'] and 'error CS1061' in logs['shutdown-baseline']
    assert 'AssertionError' in logs['shutdown-red']  # retained compile attempt, not behavioral red
    results = {'shutdown-red-behavior': dotnet_result(logs['shutdown-red-behavior'], red_failures())}
    assert results['shutdown-red-behavior'] == dict(total=35, passed=25, failed=10)
    for name in ('shutdown-green',) + tuple(PROJECTS):
        results[name] = dotnet_result(logs[name])
    assert results['shutdown-green']['total'] == results['shutdown-final']['total'] == 35
    assert 'observed: dispose=0, hasTransport=True, peers=1, pending=True, state=Connected' in logs['shutdown-red-behavior']
    for name in ('shutdown-green', 'shutdown-final'):
        assert 'observed: dispose=1, hasTransport=False, peers=0, pending=False, state=Idle' in logs[name]
        assert 'PASS: original exception preserved; ordered prefix only; no retry;' in logs[name]
        assert 'PASS: guest single-host disconnect not retried;' in logs[name]
    python = logs['python-final']
    match = re.search(r'^Ran (\d+) tests in ', python, re.M)
    assert match and re.search(r'^OK$', python, re.M) and 'skipped=' not in python
    results['python-final'] = dict(total=int(match[1]), passed=int(match[1]), failed=0)
    core = logs['core-final']
    assert 'net35/WinterMP.Core.dll' in core and re.search(r'^\s*0 (Error\(s\)|Fehler)', core, re.M)

    # The final game source must be identical to assignment, not a newly claimed fix.
    original = (run / 'baseline' / SESSION).read_text()
    current = (SOURCE / SESSION).read_text()
    red_source = (run / 'SessionManager.pre-fix.cs').read_text()
    assert digest(run / 'baseline' / SESSION) == before[SESSION] == digest(SOURCE / SESSION)
    signature = 'public void Shutdown('
    red_method = extract(red_source, signature)
    assert red_method == (run / 'Shutdown.historical-pre-fix.txt').read_text()
    assert red_source.replace(red_method, extract(original, signature), 1) == original
    red, green = receipts['shutdown-red-behavior'], receipts['shutdown-green']
    assert red['before_sha256'][SESSION] == red['after_sha256'][SESSION] == digest(run / 'SessionManager.pre-fix.cs')
    compared = {}
    for path, sha in red['before_sha256'].items():
        if path.endswith('.dll') or not path.startswith(('src/', 'tools/')):
            continue
        assert red['after_sha256'][path] == sha
        if path != SESSION:
            assert sha == green['before_sha256'][path] == green['after_sha256'][path] == digest(SOURCE / path), path
            compared[path] = sha
    changes, preserved = {}, {}
    for path, sha in before.items():
        if not path.startswith(('src/', 'tools/', 'docs/', 'catalog/', 'protocol/')):
            continue
        actual = digest(SOURCE / path)
        if actual != sha:
            changes[path] = actual
        elif path.startswith(('src/', 'catalog/', 'protocol/')):
            preserved[path] = sha
    for root in ('src', 'tools', 'docs', 'catalog', 'protocol'):
        for path in (SOURCE / root).rglob('*'):
            if path.is_symlink() or not path.is_file() or set(path.parts) & {'bin', 'obj', '__pycache__'}:
                continue
            relative = str(path.relative_to(SOURCE))
            if relative not in before:
                changes[relative] = digest(path)
    assert set(changes) == CHANGED, sorted(set(changes) ^ CHANGED)
    for name in FINAL:
        for path, sha in receipts[name]['after_sha256'].items():
            if not path.endswith('.dll'):
                assert sha == digest(SOURCE / path), (name, path)
    binaries = {path: sha for path, sha in receipts['core-final']['after_sha256'].items() if path.endswith('.dll')}
    for path, sha in binaries.items():
        assert digest(SOURCE / path) == sha, path
    for name, project in PROJECTS.items():
        binary = project + '/bin/Release/net8.0/' + Path(project).name + '.dll'
        if binary in receipts[name]['after_sha256']:
            assert receipts[name]['after_sha256'][binary] == digest(SOURCE / binary)
    # Include preserved earlier attempts as well, not just the final receipt set.
    owned = {json.loads(p.read_text())['owned_pid'] for p in run.glob('*/receipt.json')}
    remaining = []
    for path in Path('/proc').iterdir():
        if not path.name.isdigit():
            continue
        try:
            if os.getsid(int(path.name)) in owned:
                remaining.append(int(path.name))
        except (ProcessLookupError, PermissionError):
            pass
    assert not remaining, remaining
    cleanup = dict(checked_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                   owned_sessions=sorted(owned), remaining=remaining, native_resources='NOT_CREATED')
    out.mkdir()
    (out / 'cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
    (out / 'negative-control.diff').write_text(''.join(difflib.unified_diff(
        red_source.splitlines(True), current.splitlines(True), fromfile='historical-shutdown-in-current-source', tofile='restored-inherited-source')))
    diffs = []
    for path in sorted(CHANGED):
        saved = run / 'baseline' / path
        prior = saved.read_text() if saved.exists() else ''
        diffs.extend(difflib.unified_diff(prior.splitlines(True), (SOURCE / path).read_text().splitlines(True), fromfile='baseline/' + path, tofile=path))
    (out / 'changes.diff').write_text(''.join(diffs))
    generated = out / 'generated'; generated.mkdir()
    for path in (SOURCE / TEST / 'obj/Release/net8.0/production').glob('*.g.cs'):
        (generated / path.name).write_bytes(path.read_bytes())
    result = dict(status='PASS_PARTIAL_PORTABLE_S09', results=results, commands=checks,
                  changed_files_sha256=changes, preserved_gameplay_sha256=preserved,
                  red_green_identical_source_sha256=compared, binary_sha256=binaries,
                  red_binary_sha256=red['after_sha256'][TEST + 'bin/Release/net8.0/TrainSend.Tests.dll'],
                  generated_sha256={p.name: digest(p) for p in generated.iterdir()}, cleanup=cleanup,
                  scope='Inherited production finally verified; current clothing reset fixture repaired; no net game-source changes.',
                  red_scope='Fresh historical pre-fix Shutdown negative control, not inherited-source failure.',
                  evidence_level='Selected unmodified production lifecycle bodies/real codec and runtime policies; dependency doubles.',
                  not_tested=['native/injected-state', 'protected-input equality/provenance', 'ordinary input',
                              'native peer actions/results', 'different saves', 'late join/rejoin', 'save/reload',
                              'Steam/two-PC', 'four-player soak', 'native teardown',
                              'exceptions in cleanup callbacks, Dispose, logging, reentrant transport or Steam lobby departure'])
    (out / 'verification.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(dict(status=result['status'], results=results, changed_files=sorted(changes),
                          verification=str(out / 'verification.json'), remaining_owned_processes=remaining), indent=2))


if __name__ == '__main__':
    main()
