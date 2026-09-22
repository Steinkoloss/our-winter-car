#!/usr/bin/env python3
"""Independently inspect a local kit and exercise read-only verification on mutated copies.

No installation, settings, UI, game process or personal save is accessed. This is
package evidence, not native gameplay. The evidence directory must be new.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def tree_hashes(path):
    return {str(p.relative_to(path)): digest(p) for p in path.rglob('*') if p.is_file()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--attempt', type=Path, required=True)
    parser.add_argument('--evidence-dir', type=Path, required=True)
    parser.add_argument('--game-path', type=Path, required=True, help='Read-only compilation assemblies only')
    args = parser.parse_args()
    attempt, evidence = args.attempt.resolve(), args.evidence_dir.resolve()
    evidence.mkdir(parents=True, exist_ok=False)
    package = attempt / 'package'
    checks, commands = [], []

    def check(name, condition):
        if not condition:
            raise ValueError('Assertion failed: ' + name)
        checks.append(name)

    def command(argv, expected, label):
        argv = list(map(str, argv))
        result = subprocess.run(argv, cwd=ROOT, capture_output=True, timeout=60)
        log = evidence / (label + '.log')
        with log.open('xb') as stream:
            stream.write(result.stdout + result.stderr)
        commands.append({'argv': argv, 'cwd': str(ROOT), 'exitCode': result.returncode,
                         'log': str(log), 'logSha256': digest(log)})
        check(label, result.returncode == expected)

    report = {'argv': sys.argv, 'scriptSha256': digest(Path(__file__)), 'checks': checks, 'commands': commands}
    try:
        validation = json.loads((package / 'validation.json').read_text())
        result = json.loads((attempt / 'result.json').read_text())
        kit = attempt / 'OurWinterCar-local-test-kit.zip'
        check('builder_exit_zero', result['exitCode'] == 0)
        check('outer_kit_hash', digest(kit) == result['kitSha256'])
        check('validation_hash', digest(package / 'validation.json') == result['validationSha256'])
        check('checksums_hash', digest(package / 'SHA256SUMS.txt') == result['checksumsSha256'])
        for line in (package / 'SHA256SUMS.txt').read_text().splitlines():
            value, name = line.split('  ', 1)
            check('safe_checksum_path:' + name, Path(name).name == name)
            check('file_hash:' + name, digest(package / name) == value)
        with zipfile.ZipFile(kit) as outer:
            check('outer_integrity', outer.testzip() is None)
            check('outer_exact_entries', set(outer.namelist()) == {f.name for f in package.iterdir()}
                  and len(outer.namelist()) == len(set(outer.namelist())))
            for name in outer.namelist():
                check('outer_bytes:' + name, outer.read(name) == (package / name).read_bytes())
            with zipfile.ZipFile(io.BytesIO(outer.read('OurWinterCar-payload.zip'))) as inner:
                check('inner_integrity', inner.testzip() is None)
                required = {'WinterMP.Core.dll', 'WinterMP.Net.dll', 'WinterMP.FastBoot.dll',
                            'sync-catalog.json', 'wintermp-compat.json'}
                check('inner_exact_mod_only_entries', len(inner.namelist()) == len(required)
                      and set(inner.namelist()) == required == set(validation['payloadSha256']))
                for name, value in validation['payloadSha256'].items():
                    check('inner_hash:' + name, hashlib.sha256(inner.read(name)).hexdigest() == value)
        payload = attempt / 'work/payload'
        manifest = json.loads((payload / 'wintermp-compat.json').read_text())
        for name, value in manifest['payloadSha256'].items():
            check('manifest_hash:' + name, value == validation['payloadSha256'][name])
        for name in ('TESTING.md', 'RELEASE-NOTES.md', 'PLAYERS.md'):
            check('current_doc:' + name, (package / name).read_bytes() == (ROOT / 'docs' / name).read_bytes())
        check('current_license', (package / 'LICENSE').read_bytes() == (ROOT / 'LICENSE').read_bytes())
        protocol = int(re.search(r'public const ushort Version = (\d+);',
                                (ROOT / 'src/WinterMP.Net/Protocol.cs').read_text()).group(1))
        check('current_protocol', protocol == manifest['protocolVersion'] == validation['protocol'])
        for name, value in json.loads((package / 'source-hashes.json').read_text()).items():
            path = (ROOT / name).resolve()
            if not path.is_relative_to(ROOT) or digest(path) != value:
                raise ValueError('Source drift or unsafe path: ' + name)
        checks.append('all_recorded_source_hashes_current')
        for receipt in json.loads((attempt / 'commands.json').read_text()):
            log = attempt / Path(receipt['log']).name
            check('command_pass_and_log_hash:' + log.name,
                  receipt['exitCode'] == 0 and digest(log) == receipt['logSha256'])
        for name, value in json.loads((attempt / 'read-only-build-inputs.json').read_text()).items():
            check('safe_input_name:' + name, name in ('UnityEngine.dll', 'PlayMaker.dll', 'ES2.dll', 'Assembly-CSharp-firstpass.dll'))
            check('readonly_assembly:' + name, digest(args.game_path / 'mywintercar_Data/Managed' / name) == value)
        check('no_tested_game_build_claim', validation['testedGameBuildIds'] == [])
        check('gameplay_not_tested', validation['gameplay'] == {key: 'NOT_TESTED' for key in
              ('nativeDiscovery', 'ordinaryInput', 'differentSaves', 'freshPlayerLateJoin',
               'nativeSaveReload', 'steamTwoPc', 'fourPlayerSoak')})
        for name in ('WinterMP.Core', 'WinterMP.Net', 'WinterMP.FastBoot'):
            project = ROOT / 'src' / name / (name + '.csproj')
            version = ET.parse(project).findtext('./PropertyGroup/Version')
            if version is None:
                version = ET.parse(ROOT / 'Directory.Build.props').findtext('./PropertyGroup/Version')
            parts = version.split('.')
            expected = '.'.join(parts + ['0'] * (4 - len(parts)))
            check('binary_name_and_version:' + name,
                  validation['binaryVerification']['assemblies'][name + '.dll'] == {'name': name, 'version': expected})
        verifier = ROOT / 'src/WinterMP.Launcher/bin/Release/net8.0/WinterMPLauncher.dll'
        check('current_verifier_hash', digest(verifier) == validation['verifierSha256'])
        with tempfile.TemporaryDirectory(prefix='owned-package-mutants-', dir=evidence) as temp:
            temp = Path(temp)
            for label in ('valid', 'wrong-protocol', 'wrong-version', 'tampered-fastboot', 'missing-hash',
                          'null-hashes', 'malformed-json', 'conflicting-install-flag'):
                dest = temp / label
                shutil.copytree(payload, dest)
                meta = dest / 'wintermp-compat.json'
                data = json.loads(meta.read_text())
                if label == 'wrong-protocol':
                    data['protocolVersion'] -= 1
                if label == 'wrong-version':
                    data['modVersion'] = '9.9.9'
                if label == 'missing-hash':
                    data['payloadSha256'].pop('sync-catalog.json')
                if label == 'null-hashes':
                    data['payloadSha256'] = None
                if label in ('wrong-protocol', 'wrong-version', 'missing-hash', 'null-hashes'):
                    meta.write_text(json.dumps(data))
                if label == 'tampered-fastboot':
                    with (dest / 'WinterMP.FastBoot.dll').open('ab') as stream:
                        stream.write(b'controlled negative package fixture')
                if label == 'malformed-json':
                    meta.write_text('{not-json')
                before = tree_hashes(dest)
                argv = ['dotnet', verifier, '--verify-payload', dest]
                if label == 'conflicting-install-flag':
                    argv.append('--install-mod')
                command(argv, 0 if label == 'valid' else 2, label)
                check('verifier_readonly:' + label, before == tree_hashes(dest))
        check('mutant_cleanup', not temp.exists())
        before = tree_hashes(attempt)
        command([sys.executable, ROOT / 'tools/build-test-release.py', '--run-dir', attempt,
                 '--game-path', args.game_path, '--payload-only'], 1, 'existing-run-refused')
        check('existing_run_unchanged', before == tree_hashes(attempt))
        report.update(status='PASS', tests=validation['tests'], pythonTests=validation['evidenceToolTests'],
                      kitSha256=result['kitSha256'])
    except Exception as error:
        report.update(status='FAIL', error=str(error))
        raise
    finally:
        report.update(checkCount=len(checks), cleanup='Temporary mutated payloads removed; no game or installer execution.')
        with (evidence / 'audit.json').open('x') as stream:
            json.dump(report, stream, indent=2)
    print(json.dumps({key: report[key] for key in ('status', 'checkCount', 'tests', 'pythonTests', 'kitSha256', 'cleanup')}, indent=2))


if __name__ == '__main__':
    main()
