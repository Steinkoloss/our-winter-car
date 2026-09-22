#!/usr/bin/env python3
"""Fresh S09 single-callback evidence audit; portable only, not a controller gate."""
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
    SESSION, TEST + 'ShutdownTests.cs', TEST + 'ShutdownDoubles.cs',
    'tools/tests/test_s09_shutdown_fixture.py', 'docs/S09-SHUTDOWN-CLEANUP.md',
    'tools/tests/test_s09_train_send_wiring.py',
    'tools/s09_shutdown_callback_manifest.py', 'tools/tests/test_s09_shutdown_callback_manifest.py',
}


def red_failures():
    prefix = 'TrainSend.Tests.ShutdownTests.'
    return ([prefix + 'PaneResetFailureAfterDisconnectFailurePreservesPrimaryAndClearsSession(role: '
             + role + ', afterDelivery: ' + delivery + ')'
             for role in ('Hosting', 'Connected') for delivery in ('False', 'True')]
            + [prefix + 'PaneResetFailureWithoutDisconnectFailureStillClearsAndPropagatesCleanup(role: '
               + role + ')' for role in ('Hosting', 'Connected', 'Connecting')])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', required=True, type=Path)
    parser.add_argument('--out', required=True, type=Path)
    parser.add_argument('--final-suffix', default='', help='Preserve earlier attempts with fresh final receipt leaves.')
    args = parser.parse_args()
    run, out = args.run.resolve(strict=True), args.out.resolve()
    assert run.parent == SOURCE.parent / 'rounds' and (run / 'contract.json').is_file()
    assert out.is_relative_to(run) and out != run and not out.exists()
    assert '/' not in args.final_suffix and '\\' not in args.final_suffix
    before = json.loads((run / 'before.json').read_text())
    receipts, logs, checks, results = {}, {}, [], {}
    names = ('shutdown-baseline', 'shutdown-red-final', 'shutdown-green') + FINAL
    for name in names:
        leaf = name + args.final_suffix if name in FINAL else name
        receipt_path = run / leaf / 'receipt.json'
        r = json.loads(receipt_path.read_text())
        expected = 1 if name == 'shutdown-red-final' else 0
        assert r['exit_code'] == r['child_exit_code'] == expected, name
        assert r['cwd'] == str(SOURCE) and not r.get('timed_out') and r['owned_pid'] > 0
        assert r['started_utc'] <= r['ended_utc'] and r['cleanup']
        assert r['native'] == 'NOT_TESTED' and r['roles']
        log_path = run / leaf / 'output.log'
        assert digest(log_path) == r['output_sha256'], name
        log = log_path.read_text()
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
        checks.append(dict(name=leaf, command=r['command'], exit_code=expected, roles=r['roles'],
                           started_utc=r['started_utc'], ended_utc=r['ended_utc'], owned_pid=r['owned_pid'],
                           receipt_sha256=digest(receipt_path), output_sha256=r['output_sha256']))
        receipts[name], logs[name] = r, log
    assert results['shutdown-baseline'] == dict(total=35, passed=35, failed=0)
    assert results['shutdown-red-final'] == dict(total=42, passed=35, failed=7)
    assert results['shutdown-green'] == results['shutdown-final'] == dict(total=42, passed=42, failed=0)
    red, green = receipts['shutdown-red-final'], receipts['shutdown-green']
    for role, peers in (('Hosting', 3), ('Connected', 1)):
        assert ('observed: dispose=1, hasTransport=False, peers=' + str(peers)
                + ', pending=False, state=' + role) in logs['shutdown-red-final']
    assert 'callback observations: pane=1, player=0, clothing=0, death=0' in logs['shutdown-red-final']
    for name in ('shutdown-green', 'shutdown-final'):
        assert logs[name].count('PASS: one Pane callback fault logged;') == 4
        assert logs[name].count('PASS: cleanup-only fault remains observable') == 3
        assert 'observed: dispose=1, hasTransport=False, peers=0, pending=False, state=Idle' in logs[name]

    original, current = (run / 'baseline' / SESSION).read_text(), (SOURCE / SESSION).read_text()
    assert digest(run / 'baseline' / SESSION) == before[SESSION] == red['before_sha256'][SESSION] == red['after_sha256'][SESSION]
    assert digest(SOURCE / SESSION) == green['before_sha256'][SESSION] == green['after_sha256'][SESSION]
    patched = original
    for signature in ('public void Shutdown(', 'private void ResetSessionRuntimeState('):
        patched = patched.replace(extract(original, signature), extract(current, signature), 1)
    patched = patched.replace('/// <summary>Clears state tied to a transport without replacing the current user-facing session state.</summary>',
                              '/// <summary>Clears transport state, preserving failure text unless finishing an explicit shutdown.</summary>')
    assert patched == current, 'Production delta escaped the two selected methods/documentation'
    identical = {}
    for path, sha in red['before_sha256'].items():
        if path != SESSION and (path.endswith(('.cs', '.csproj')) or path == TEST + 'shutdown_fixture.py'):
            assert sha == red['after_sha256'][path] == green['before_sha256'][path] == green['after_sha256'][path] == digest(SOURCE / path), path
            identical[path] = sha
    changes, preserved = {}, {}
    for path, sha in before.items():
        if not path.startswith(('src/', 'tools/', 'docs/', 'catalog/', 'protocol/')) and path != 'PLAN.md':
            continue
        actual = digest(SOURCE / path)
        if actual != sha:
            changes[path] = actual
        else:
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
            assert sha == digest(SOURCE / path), (name, path)
    binaries = {path: sha for path, sha in receipts['core-final']['after_sha256'].items() if path.endswith('.dll')}
    # Receipt runner creates a separate session per command; inspect only those SIDs.
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
    out.mkdir()
    (out / 'Shutdown.g.cs').write_bytes(generated.read_bytes())
    (out / 'cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
    diffs = []
    for path in sorted(CHANGED):
        saved = run / 'baseline' / path
        prior = saved.read_text() if saved.exists() else ''
        diffs.extend(difflib.unified_diff(prior.splitlines(True), (SOURCE / path).read_text().splitlines(True), fromfile='baseline/' + path, tofile=path))
    (out / 'changes.diff').write_text(''.join(diffs))
    result = dict(status='PASS_PARTIAL_PORTABLE_S09_CALLBACK', results=results, commands=checks,
                  changed_files_sha256=changes, preserved_sha256=preserved, red_green_identical_sha256=identical,
                  binary_sha256=binaries, red_binary_sha256=red['after_sha256'][TEST + 'bin/Release/net8.0/TrainSend.Tests.dll'],
                  generated_sha256=digest(generated), cleanup=cleanup,
                  scope='Pane reset exception contained for SessionManager owned-state cleanup; primary preserved; no-primary callback rethrown after reset/Idle.',
                  evidence_level='Unmodified selected production methods; real codec/runtime policies; dependency doubles.',
                  not_tested=['native discovery/injected-state', 'protected/ordinary input', 'native matching peers/actions',
                              'late join/rejoin', 'different saves', 'save/reload', 'Steam/two-PC', 'four-player soak',
                              'native teardown/binding restoration', 'Dispose/other reset/logging/reentrant/Steam callback failures'])
    (out / 'verification.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(dict(status=result['status'], results=results, changed_files=sorted(changes),
                          verification=str(out / 'verification.json'), remaining_owned_processes=remaining), indent=2))


if __name__ == '__main__':
    main()
