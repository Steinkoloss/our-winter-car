#!/usr/bin/env python3
"""Audit fresh S09 disposal-boundary receipts; portable evidence, not a controller gate."""
import argparse
import datetime
import difflib
import importlib.util
import json
import os
from pathlib import Path
import re
from s09_shutdown_manifest import SOURCE, SESSION, TEST, PROJECTS, FINAL, digest, dotnet_result
from s09_update_manifest import extract

CHANGED = {
    SESSION, TEST + 'ShutdownTests.cs', TEST + 'ShutdownDoubles.cs', TEST + 'shutdown_fixture.py',
    'tools/tests/test_s09_shutdown_fixture.py', 'docs/S09-SHUTDOWN-CLEANUP.md',
    'tools/s09_shutdown_disposal_manifest.py', 'tools/tests/test_s09_shutdown_disposal_manifest.py',
}


def red_failures():
    prefix = 'TrainSend.Tests.ShutdownTests.'
    return ([prefix + 'OneDisposeFaultStillClearsShutdownAndPreservesPrimary(devFault: '
             + dev + ', role: ' + role + ', sendFault: ' + str(send) + ')'
             for dev in ('False', 'True') for role in ('Hosting', 'Connected') for send in range(3)]
            + [prefix + 'OneDisposeFaultDuringDeferredCleanupKeepsFailureOrPendingLaunch(devFault: '
               + dev + ', role: ' + role + ', pending: ' + pending + ')'
               for dev in ('False', 'True') for role, pending in (
                   ('Hosting', 'None'), ('Connected', 'None'), ('Connected', 'Host'), ('Connecting', 'JoinBrowse'))]
            + [prefix + 'DevDisposeFaultWithoutTransportStillResetsEmptyConnectingSession'])


def verify_delta(original, current):
    signature = 'private void DisposeSessionTransport('
    assert original != current
    assert original.replace(extract(original, signature), extract(current, signature), 1) == current, \
        'Production delta escaped DisposeSessionTransport'


def verify_receipt(run, leaf, exit_code):
    path = run / leaf / 'receipt.json'
    receipt = json.loads(path.read_text())
    assert receipt['exit_code'] == receipt['child_exit_code'] == exit_code, leaf
    assert receipt['cwd'] == str(SOURCE) and not receipt.get('timed_out') and receipt['owned_pid'] > 0
    assert receipt['started_utc'] <= receipt['ended_utc'] and receipt['cleanup']
    assert receipt['native'] == 'NOT_TESTED' and receipt['roles']
    log_path = run / leaf / 'output.log'
    assert digest(log_path) == receipt['output_sha256'], leaf
    return receipt, log_path.read_text(), dict(
        name=leaf, command=receipt['command'], exit_code=exit_code, roles=receipt['roles'],
        started_utc=receipt['started_utc'], ended_utc=receipt['ended_utc'], owned_pid=receipt['owned_pid'],
        receipt_sha256=digest(path), output_sha256=receipt['output_sha256'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', required=True, type=Path)
    parser.add_argument('--out', required=True, type=Path)
    parser.add_argument('--final-suffix', default='')
    args = parser.parse_args()
    run, out = args.run.resolve(strict=True), args.out.resolve()
    assert run.parent == SOURCE.parent / 'rounds' and (run / 'contract.json').is_file()
    assert out.is_relative_to(run) and out != run and not out.exists()
    assert '/' not in args.final_suffix and '\\' not in args.final_suffix
    before = json.loads((run / 'before.json').read_text())
    capture, _, capture_check = verify_receipt(run, 'baseline-capture', 0)
    for path, sha in before.items():
        if path in capture['before_sha256']:
            assert sha == capture['before_sha256'][path] == capture['after_sha256'][path]
    receipts, logs, checks, results = {}, {}, [capture_check], {}
    for name in ('shutdown-baseline', 'shutdown-red', 'shutdown-green') + FINAL:
        leaf = name + args.final_suffix if name in FINAL else name
        expected = 1 if name == 'shutdown-red' else 0
        r, log, check = verify_receipt(run, leaf, expected)
        if name in PROJECTS or name.startswith('shutdown-'):
            project = PROJECTS.get(name, 'tools/TrainSend.Tests')
            verbosity = 'normal' if name in ('net-final', 'launcher-final') else 'detailed'
            assert r['command'] == ['dotnet', 'test', project, '-c', 'Release', '-p:DeployToGame=false',
                                    '-p:UseSharedCompilation=false', '--logger', 'console;verbosity=' + verbosity], name
            results[name] = dotnet_result(log, red_failures() if expected else ())
        elif name == 'python-final':
            assert r['command'] == ['python3', '-B', '-m', 'unittest', 'discover', '-s', 'tools/tests', '-v']
            match = re.search(r'^Ran (\d+) tests in ', log, re.M)
            assert match and re.search(r'^OK$', log, re.M) and 'skipped=' not in log
            results[name] = dict(total=int(match[1]), passed=int(match[1]), failed=0)
        elif name == 'core-final':
            assert r['command'] == ['dotnet', 'build', 'src/WinterMP.Core/WinterMP.Core.csproj', '-c', 'Release',
                                    '-p:DeployToGame=false', '-p:UseSharedCompilation=false',
                                    '-p:MwcGamePath=/home/jaimep/.steam/root/steamapps/common/My Winter Car']
            assert 'net35/WinterMP.Core.dll' in log and re.search(r'^\s*0 (Error\(s\)|Fehler)', log, re.M)
        checks.append(check)
        receipts[name], logs[name] = r, log
    assert results['shutdown-baseline'] == dict(total=42, passed=42, failed=0)
    assert results['shutdown-red'] == dict(total=63, passed=42, failed=21)
    assert results['shutdown-green'] == results['shutdown-final'] == dict(total=63, passed=63, failed=0)
    red, green = receipts['shutdown-red'], receipts['shutdown-green']
    for dev, transport in (('True', '0'), ('False', '1')):
        assert ('disposal observations: devDispose=1, hasDev=' + dev + ', transportDispose=' + transport
                + ', hasTransport=True, pane=0, player=0, clothing=0, death=0, meter=0') in logs['shutdown-red']
    assert 'dev-only observations: devDispose=1, hasDev=True, state=Connecting, pane=0' in logs['shutdown-red']
    for role, peers in (('Hosting', 3), ('Connected', 1)):
        assert 'hasTransport=True, peers=' + str(peers) + ', pending=False, state=' + role in logs['shutdown-red']
    for name in ('shutdown-green', 'shutdown-final'):
        assert logs[name].count('PASS: one disposal fault logged;') == 12
        assert logs[name].count('PASS: deferred disposal fault logged once;') == 8
        assert logs[name].count('PASS: dev-only disposal fault contained') == 1
        assert logs[name].count('PASS: one Pane callback fault logged;') == 4
        assert logs[name].count('PASS: cleanup-only fault remains observable') == 3
        assert 'hasDev=False, transportDispose=1, hasTransport=False, pane=1, player=1, clothing=1, death=1, meter=1' in logs[name]

    original, current = (run / 'baseline' / SESSION).read_text(), (SOURCE / SESSION).read_text()
    assert digest(run / 'baseline' / SESSION) == before[SESSION] == red['before_sha256'][SESSION] == red['after_sha256'][SESSION]
    assert digest(SOURCE / SESSION) == green['before_sha256'][SESSION] == green['after_sha256'][SESSION]
    verify_delta(original, current)
    identical = {}
    for path, sha in red['before_sha256'].items():
        if path != SESSION and (path.endswith(('.cs', '.csproj')) or path == TEST + 'shutdown_fixture.py'):
            assert sha == red['after_sha256'][path] == green['before_sha256'][path] == green['after_sha256'][path] == digest(SOURCE / path), path
            identical[path] = sha
    changes, preserved = {}, {}
    for path, sha in before.items():
        actual = digest(SOURCE / path)
        (changes if actual != sha else preserved)[path] = actual
    for root in ('src', 'tools', 'docs', 'catalog', 'protocol'):
        for path in (SOURCE / root).rglob('*'):
            if path.is_symlink() or not path.is_file() or set(path.parts) & {'bin', 'obj', '__pycache__'}:
                continue
            relative = str(path.relative_to(SOURCE))
            if relative not in before:
                changes[relative] = digest(path)
    assert set(changes) == CHANGED, sorted(set(changes) ^ CHANGED)
    allowed = json.loads((run / 'contract.json').read_text())['allowed_paths']
    assert all(any(p == a or a.endswith('/') and p.startswith(a) for a in allowed) for p in changes)
    for name in FINAL:
        for path, sha in receipts[name]['after_sha256'].items():
            assert sha == digest(SOURCE / path), (name, path)
    binaries = {path: sha for path, sha in receipts['core-final']['after_sha256'].items() if path.endswith('.dll')}
    # Inspect only sessions created by this RUN's receipt wrapper, including earlier attempts.
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
    spec = importlib.util.spec_from_file_location('shutdown_fixture', SOURCE / TEST / 'shutdown_fixture.py')
    fixture = importlib.util.module_from_spec(spec); spec.loader.exec_module(fixture)
    generated = SOURCE / TEST / 'obj/Release/net8.0/production/Shutdown.g.cs'
    for signature in fixture.SIGNATURES:
        assert extract(generated.read_text(), signature) == extract(current, signature)
    for path, signature in fixture.AUX_METHODS:
        assert extract(generated.read_text(), signature) == extract(fixture.read(path), signature)
    out.mkdir()
    (out / 'Shutdown.g.cs').write_bytes(generated.read_bytes())
    (out / 'cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
    diffs = []
    for path in sorted(CHANGED):
        saved = run / 'baseline' / path
        prior = saved.read_text() if saved.exists() else ''
        diffs.extend(difflib.unified_diff(prior.splitlines(True), (SOURCE / path).read_text().splitlines(True), fromfile='baseline/' + path, tofile=path))
    (out / 'changes.diff').write_text(''.join(diffs))
    result = dict(status='PASS_PARTIAL_PORTABLE_S09_DISPOSAL', results=results, commands=checks,
                  changed_files_sha256=changes, preserved_sha256=preserved, red_green_identical_sha256=identical,
                  binary_sha256=binaries, red_binary_sha256=red['after_sha256'][TEST + 'bin/Release/net8.0/TrainSend.Tests.dll'],
                  generated_sha256=digest(generated), cleanup=cleanup,
                  scope='One dev-client OR transport Dispose failure logged/contained; owned refs detached; resets run; primary and deferred failure policy preserved.',
                  evidence_level='Unmodified selected production methods; real codec/runtime policies; dependency doubles.',
                  not_tested=['native discovery/injected-state', 'protected/ordinary input', 'native matching peers/actions',
                              'late join/rejoin', 'different saves', 'save/reload', 'Steam/two-PC', 'four-player soak',
                              'native teardown/resource release', 'other reset/logging/reentrant/Steam callbacks', 'OnDestroy singleton teardown'])
    (out / 'verification.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(dict(status=result['status'], results=results, changed_files=sorted(changes),
                          verification=str(out / 'verification.json'), remaining_owned_processes=remaining), indent=2))


if __name__ == '__main__':
    main()
