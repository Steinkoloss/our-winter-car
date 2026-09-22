#!/usr/bin/env python3
"""Build a versioned tester kit without deploying, committing or publishing it."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
LAUNCHER = ROOT / 'src/WinterMP.Launcher/WinterMP.Launcher.csproj'
REQUIRED = ('WinterMP.Core.dll', 'WinterMP.Net.dll', 'WinterMP.FastBoot.dll',
            'sync-catalog.json', 'wintermp-compat.json')
FORBIDDEN = {'unityengine.dll', 'assembly-csharp.dll', 'assembly-csharp-firstpass.dll',
             'playmaker.dll', 'es2.dll', 'guestsaveprobe.dll', 'wintermp.tools.dll',
             'directory.build.props.user', 'credentials.json', 'id_rsa', 'id_ed25519'}
DOCS = ('TESTING.md', 'RELEASE-NOTES.md', 'PLAYERS.md', 'LICENSE')
SOURCES = {
    'WinterMP.Core.dll': ROOT / 'src/WinterMP.Core/bin/Release/net35/WinterMP.Core.dll',
    'WinterMP.Net.dll': ROOT / 'src/WinterMP.Net/bin/Release/net35/WinterMP.Net.dll',
    'WinterMP.FastBoot.dll': ROOT / 'src/WinterMP.FastBoot/bin/Release/net35/WinterMP.FastBoot.dll',
    'sync-catalog.json': ROOT / 'catalog/sync-catalog.json',
}
VENDOR = ROOT / 'vendor/BepInEx_win_x64_5.4.23.5.zip'
RUN = None
COMMANDS = []


def run(args, **kwargs):
    argv = list(map(str, args))
    cwd = kwargs.pop('cwd', ROOT)
    print('Running ' + json.dumps(argv), flush=True)
    log = RUN / ('command-%03d.log' % (len(COMMANDS) + 1))
    receipt = {'argv': argv, 'cwd': str(cwd), 'startedUnix': time.time(), 'log': str(log)}
    with log.open('xb') as stream:
        try:
            result = subprocess.run(argv, cwd=cwd, stdout=stream, stderr=subprocess.STDOUT,
                                    timeout=900, **kwargs)
            receipt['exitCode'] = result.returncode
        except (OSError, subprocess.TimeoutExpired) as error:
            receipt.update(exitCode=None, error=str(error))
            raise
        finally:
            receipt['endedUnix'] = time.time()
            COMMANDS.append(receipt)
            (RUN / 'commands.json').write_text(json.dumps(COMMANDS, indent=2) + '\n')
    receipt['logSha256'] = digest(log)
    (RUN / 'commands.json').write_text(json.dumps(COMMANDS, indent=2) + '\n')
    if result.returncode:
        raise subprocess.CalledProcessError(result.returncode, argv)
    return log


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def project_version(path):
    return ET.parse(path).findtext('./PropertyGroup/Version')


def prepare_run(path):
    # Never overwrite a previous attempt, even when its package failed.
    path.mkdir(parents=True, exist_ok=False)
    return path


def validate_source():
    manifest = json.loads((ROOT / 'src/WinterMP.Launcher/Assets/wintermp-compat.json').read_text(encoding='utf-8-sig'))
    version = manifest['modVersion']
    protocol = int(re.search(r'public const ushort Version = (\d+);',
                            (ROOT / 'src/WinterMP.Net/Protocol.cs').read_text()).group(1))
    if manifest.get('releaseChannel') != 'test' or manifest['protocolVersion'] != protocol:
        raise ValueError('Test manifest channel/protocol differs from source')
    for project in (LAUNCHER, ROOT / 'src/WinterMP.Core/WinterMP.Core.csproj'):
        if project_version(project) != version:
            raise ValueError('Release versions disagree: ' + str(project))
    if 'AppVersion=' + version + '\n' not in (ROOT / 'installer/WinterMP.iss').read_text():
        raise ValueError('Installer version differs from the mod')
    welcome = (ROOT / 'installer/welcome.txt').read_text()
    if (f'Local package: mod {version}, protocol {protocol}, channel test.' not in welcome
            or 'UNRELEASED LOCAL TEST PACKAGE' not in welcome or 'NOT_TESTED' not in welcome):
        raise ValueError('Installer welcome is stale')
    if f'Protocol version: **{protocol}**' not in (ROOT / 'protocol/PROTOCOL.md').read_text():
        raise ValueError('Wire specification is stale')
    json.loads(SOURCES['sync-catalog.json'].read_text())
    stamp = f'Local package: mod **{version}**, protocol **{protocol}**, channel **test**.'
    for name in (*DOCS[:-1], 'BUILDING.md'):
        text = (ROOT / 'docs' / name).read_text()
        if stamp not in text or 'UNRELEASED LOCAL TEST PACKAGE' not in text:
            raise ValueError('Current tester documentation is stale: ' + name)
    return manifest


def check_path(path):
    for part in path.parts:
        name = part.lower()
        if (name in FORBIDDEN or name.startswith(('.env', 'wintermp-live-'))
                or name in ('.ssh', '.git', 'live-bag', 'backups', 'saves')
                or name.endswith(('.pem', '.key', '.pfx', '.log'))):
            raise ValueError('Refusing forbidden package path: ' + str(path))


def verify_payload(payload, source_manifest, sources=None):
    sources = SOURCES if sources is None else sources
    if {p.name for p in payload.iterdir()} != set(REQUIRED):
        raise ValueError('Payload must contain exactly the required mod files')
    manifest = json.loads((payload / 'wintermp-compat.json').read_text(encoding='utf-8-sig'))
    if {k: v for k, v in manifest.items() if k != 'payloadSha256'} != source_manifest:
        raise ValueError('Published manifest differs from source')
    for name, source in sources.items():
        if source.read_bytes() != (payload / name).read_bytes():
            raise ValueError('Published payload differs from its build: ' + name)
    hashes = {name: digest(payload / name) for name in sources}
    if 'payloadSha256' in manifest and manifest['payloadSha256'] != hashes:
        raise ValueError('Payload manifest hashes differ from content')
    manifest['payloadSha256'] = hashes
    (payload / 'wintermp-compat.json').write_text(json.dumps(manifest, indent=2) + '\n')
    return manifest


def archive_tree(source, target, prefix=''):
    paths = sorted(source.rglob('*'))
    for path in paths:
        if path.is_symlink():
            raise ValueError('Refusing package symlink: ' + str(path))
        check_path(path.relative_to(source))
    with zipfile.ZipFile(target, 'x', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for path in paths:
            if path.is_file():
                archive.write(path, prefix + path.relative_to(source).as_posix())
    with zipfile.ZipFile(target) as archive:
        if archive.testzip() is not None:
            raise ValueError('Archive integrity check failed: ' + str(target))


def verify_publish(publish, source_manifest):
    payload = publish / 'payload'
    manifest = verify_payload(payload, source_manifest)
    for name in ('TESTING.md', 'RELEASE-NOTES.md', 'PLAYERS.md', 'LICENSE'):
        source = ROOT / name if name == 'LICENSE' else ROOT / 'docs' / name
        if not (publish / name).is_file() or source.read_bytes() != (publish / name).read_bytes():
            raise ValueError('Missing or stale player documentation: ' + name)
    vendor = publish / 'vendor/BepInEx_win_x64_5.4.23.5.zip'
    if digest(vendor) != digest(ROOT / 'vendor/BepInEx_win_x64_5.4.23.5.zip'):
        raise ValueError('BepInEx archive changed during publication')
    with zipfile.ZipFile(vendor) as archive:
        if archive.testzip() is not None:
            raise ValueError('BepInEx archive is corrupt')
    return manifest


def read_test_result(path):
    counters = next(node for node in ET.parse(path).iter() if node.tag.endswith('}Counters'))
    result = {key: int(counters.get(key, 0)) for key in ('total', 'passed', 'failed')}
    if result['total'] == 0 or result['total'] != result['passed'] or result['failed']:
        raise ValueError('Release tests did not all pass: ' + str(path))
    return result


def wine_path(path):
    return 'Z:' + str(path.resolve()).replace('/', '\\')


def source_snapshot():
    files = [ROOT / name for name in ('Directory.Build.props', 'Directory.Build.targets',
             'NuGet.Config', 'global.json', 'LICENSE', 'PLAN.md') if (ROOT / name).is_file()]
    for directory in ('src', 'tools', 'docs', 'catalog', 'protocol', 'installer'):
        for parent, dirs, names in os.walk(ROOT / directory):
            dirs[:] = [d for d in dirs if d not in ('bin', 'obj', '__pycache__', 'build', 'dist', 'TestResults')
                       and not d.startswith('.') and not (Path(parent) / d).is_symlink()]
            for name in names:
                path = Path(parent) / name
                if path.is_symlink() or path.suffix in ('.pyc', '.dll', '.exe', '.zip', '.pdb'):
                    continue
                try:
                    check_path(path.relative_to(ROOT))
                except ValueError:
                    continue
                files.append(path)
    return {str(p.relative_to(ROOT)): digest(p) for p in sorted(files)}


def main():
    global RUN
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run-dir', required=True, type=Path, help='New attempt directory; existing paths are refused')
    parser.add_argument('--game-path', required=True, type=Path, help='Read-only MwcGamePath used only for compilation')
    parser.add_argument('--payload-only', action='store_true', help='Build a mod-only kit, NOT a launcher/installer')
    parser.add_argument('--inno-prefix', type=Path, help='Separate Wine prefix containing drive_c/inno/ISCC.exe')
    parser.add_argument('--cosmocc', type=Path, help='Build the universal installer using this compiler')
    parser.add_argument('--appimage', action='store_true', help='Also build the Linux AppImage')
    args = parser.parse_args()
    RUN = prepare_run(args.run_dir.resolve())
    (RUN / 'invocation.json').write_text(json.dumps({'argv': sys.argv, 'cwd': str(Path.cwd())}, indent=2) + '\n')
    if args.payload_only and (args.appimage or args.inno_prefix or args.cosmocc):
        raise ValueError('Payload-only cannot produce optional installers')
    source_manifest = validate_source()
    version = source_manifest['modVersion']
    if not args.payload_only and not VENDOR.is_file():
        raise FileNotFoundError('Full launcher kit BLOCKED: required vendor archive missing: ' + str(VENDOR))
    work = RUN / 'work'
    output = RUN / 'package'
    publish = {rid: work / 'publish' / rid for rid in ('win-x64', 'linux-x64')}
    reports = work / 'test-results'
    output.mkdir(parents=True)
    reports.mkdir(parents=True)
    before = source_snapshot()
    (RUN / 'source-hashes.json').write_text(json.dumps(before, indent=2) + '\n')
    managed = args.game_path.resolve() / 'mywintercar_Data/Managed'
    inputs = {name: digest(managed / name) for name in
              ('UnityEngine.dll', 'PlayMaker.dll', 'ES2.dll', 'Assembly-CSharp-firstpass.dll')}
    (RUN / 'read-only-build-inputs.json').write_text(json.dumps(inputs, indent=2) + '\n')
    flags = ['-c', 'Release', '--no-restore', '-p:DeployToGame=false',
             '-p:MwcGamePath=' + str(args.game_path.resolve()), '-m:1', '-nr:false']
    for name in ('WinterMP.Net', 'WinterMP.Core', 'WinterMP.FastBoot'):
        run(['dotnet', 'build', ROOT / 'src' / name / (name + '.csproj'), *flags, '-t:Rebuild'])
    for name, report in (('WinterMP.Net.Tests', 'protocol-release.trx'), ('WinterMP.Launcher.Tests', 'launcher-release.trx')):
        run(['dotnet', 'test', ROOT / 'src' / name / (name + '.csproj'), *flags,
             '--logger', 'trx;LogFileName=' + report, '--results-directory', reports])
    tests = {name: read_test_result(reports / (name + '-release.trx')) for name in ('protocol', 'launcher')}
    tool_log = run([sys.executable, '-m', 'unittest', 'discover', '-s', ROOT / 'tools/tests', '-v'])
    tool_result = tool_log.read_text()
    tool_count = re.search(r'Ran (\d+) tests?', tool_result)
    if tool_count is None or int(tool_count.group(1)) == 0 or 'skipped=' in tool_result:
        raise ValueError('Release evidence-tool tests did not all pass')
    # The mod-only fallback does not claim a clean vulnerability audit of a launcher
    # it does not distribute. Full kits retain the existing advisory gate.
    advisory = 'NOT_TESTED (payload-only; no launcher distributed)'
    if not args.payload_only:
        audit_log = run(['dotnet', 'list', LAUNCHER, 'package', '--vulnerable',
                         '--include-transitive', '--format', 'json', '--no-restore'])
        audit = json.loads(audit_log.read_text())
        if not audit.get('projects') or audit.get('problems'):
            raise ValueError('Package advisory verification did not complete')
        for project in audit['projects']:
            for framework in project.get('frameworks', []):
                for key in ('topLevelPackages', 'transitivePackages'):
                    if any(p.get('vulnerabilities') for p in framework.get(key, [])):
                        raise ValueError('Resolve package vulnerabilities before shipping')
        advisory = 0
    payload = work / 'payload'
    payload.mkdir()
    for name, source in SOURCES.items():
        shutil.copy2(source, payload / name)
    shutil.copy2(ROOT / 'src/WinterMP.Launcher/Assets/wintermp-compat.json', payload / 'wintermp-compat.json')
    manifest = verify_payload(payload, source_manifest)
    verifier = ROOT / 'src/WinterMP.Launcher/bin/Release/net8.0/WinterMPLauncher.dll'
    metadata_log = run(['dotnet', verifier, '--verify-payload', payload])
    manifests = {}
    if not args.payload_only:
        for rid, directory in publish.items():
            run(['dotnet', 'publish', LAUNCHER, '-c', 'Release', '-r', rid, '--self-contained', 'true',
                 '--no-restore', '-p:PublishSingleFile=false', '-p:DeployToGame=false',
                 '-p:MwcGamePath=' + str(args.game_path.resolve()), '-m:1', '-nr:false', '-o', directory])
            manifests[rid] = verify_publish(directory, source_manifest)
            run(['dotnet', verifier, '--verify-payload', directory / 'payload'])
            if manifests[rid] != manifest:
                raise ValueError('Launcher and local payloads differ')
            archive_tree(directory, output / ('OurWinterCar-Launcher-' + rid + '.zip'))
    archive_tree(payload, output / 'OurWinterCar-payload.zip')
    env = os.environ.copy()
    env.update(OWC_RELEASE_DIR=str(output), OWC_WIN_PUBLISH_DIR=str(publish['win-x64']),
               OWC_LINUX_PUBLISH_DIR=str(publish['linux-x64']), OWC_APPDIR=str(work / 'AppDir'))
    if args.appimage:
        run(['bash', ROOT / 'tools/build-appimage.sh'], env=env)
    if args.inno_prefix:
        prefix = args.inno_prefix.resolve()
        compiler = prefix / 'drive_c/inno/ISCC.exe'
        if not compiler.is_file():
            raise ValueError('Inno Setup compiler missing: ' + str(compiler))
        inno_env = env.copy()
        inno_env.update(WINEPREFIX=str(prefix), WINEDLLOVERRIDES='mscoree,mshtml=', WINEDEBUG='-all')
        setup = output / 'OurWinterCar-Setup.exe'
        setup.unlink(missing_ok=True)
        run(['wine', compiler, '/Qp', '/DSourceDir=' + wine_path(publish['win-x64']),
             '/O' + wine_path(output), 'WinterMP.iss'], cwd=ROOT / 'installer', env=inno_env)
        if not setup.is_file():
            raise ValueError('Inno Setup did not produce the installer')
        with setup.open('rb') as stream:
            if stream.read(2) != b'MZ':
                raise ValueError('Inno Setup output is not a Windows executable')
    if args.cosmocc:
        env['COSMOCC'] = str(args.cosmocc.resolve())
        run(['bash', ROOT / 'tools/build-ape-installer.sh'], env=env)
        universal = output / 'OurWinterCar-Installer.com'
        with zipfile.ZipFile(universal) as archive:
            if archive.testzip() is not None:
                raise ValueError('Universal installer integrity check failed')
            for os_name, rid in (('win', 'win-x64'), ('linux', 'linux-x64')):
                for name in REQUIRED:
                    if archive.read(os_name + '/payload/' + name) != (publish[rid] / 'payload' / name).read_bytes():
                        raise ValueError('Universal installer contains a stale payload: ' + name)
        compiler_output = work / 'compiler-output'
        compiler_output.mkdir(exist_ok=True)
        for path in output.iterdir():
            if path.name.endswith(('.dbg', '.elf')):
                shutil.move(str(path), str(compiler_output / path.name))
    for name in ('TESTING.md', 'RELEASE-NOTES.md', 'PLAYERS.md'):
        shutil.copy2(ROOT / 'docs' / name, output / name)
    shutil.copy2(ROOT / 'LICENSE', output / 'LICENSE')
    if before != source_snapshot():
        raise ValueError('Source changed during the package build; use a fresh attempt')
    if inputs != {name: digest(managed / name) for name in inputs}:
        raise ValueError('Read-only game assembly inputs changed during compilation')
    shutil.copy2(RUN / 'source-hashes.json', output / 'source-hashes.json')
    payload_hashes = {name: digest(payload / name) for name in REQUIRED}
    with zipfile.ZipFile(output / 'OurWinterCar-payload.zip') as archive:
        if set(archive.namelist()) != set(REQUIRED):
            raise ValueError('Payload archive entries differ from the verified payload')
        for name in REQUIRED:
            if hashlib.sha256(archive.read(name)).hexdigest() != payload_hashes[name]:
                raise ValueError('Payload archive hash differs: ' + name)
    validation = {
        'version': version, 'channel': 'test', 'protocol': source_manifest['protocolVersion'],
        'status': 'UNRELEASED LOCAL TEST PACKAGE',
        'packageKind': 'payload-only' if args.payload_only else 'launcher-kit',
        'targetGameBuildIds': source_manifest['targetGameBuildIds'],
        'testedGameBuildIds': source_manifest['testedGameBuildIds'],
        'sourceProvenance': 'directory snapshot; no Git operations; source-hashes.json excludes local overrides and build/runtime artifacts',
        'sourceHashesSha256': digest(output / 'source-hashes.json'),
        'tests': tests, 'evidenceToolTests': int(tool_count.group(1)),
        'reportedDependencyVulnerabilities': advisory,
        'payloadSha256': payload_hashes,
        'binaryVerification': json.loads(metadata_log.read_text()),
        'verifierSha256': digest(verifier),
        'assertions': {'sourceUnchanged': True, 'readOnlyBuildInputsUnchanged': True,
                       'payloadMatchesBuild': True, 'archiveIntegrity': True,
                       'exactPayloadEntries': True, 'archivePayloadHashesMatch': True},
        'fullLauncherKit': ('BLOCKED: missing ' + str(VENDOR)) if not VENDOR.is_file()
                           else ('NOT_TESTED: payload-only requested' if args.payload_only else 'BUILT'),
        'optionalInstallers': {'setup': 'BUILT' if args.inno_prefix else 'NOT_TESTED: not requested',
                               'appimage': 'BUILT' if args.appimage else 'NOT_TESTED: not requested',
                               'universal': 'BUILT' if args.cosmocc else 'NOT_TESTED: not requested'},
        'gameplay': {key: 'NOT_TESTED' for key in ('nativeDiscovery', 'ordinaryInput', 'differentSaves',
                     'freshPlayerLateJoin', 'nativeSaveReload', 'steamTwoPc', 'fourPlayerSoak')},
        'installation': 'NOT_TESTED; no deployment or native launch performed',
        'cleanup': 'No game processes started. Attempt artifacts retained; no existing output deleted.',
    }
    (output / 'validation.json').write_text(json.dumps(validation, indent=2) + '\n')
    sums = ''.join(digest(path) + '  ' + path.name + '\n' for path in sorted(output.iterdir())
                   if path.is_file() and path.name != 'SHA256SUMS.txt' and not path.name.endswith(('.dbg', '.elf')))
    (output / 'SHA256SUMS.txt').write_text(sums)
    kit = RUN / 'OurWinterCar-local-test-kit.zip'
    archive_tree(output, kit)
    (RUN / 'result.json').write_text(json.dumps({'exitCode': 0, 'package': str(output),
        'kit': str(kit), 'kitSha256': digest(kit),
        'validationSha256': digest(output / 'validation.json'),
        'checksumsSha256': digest(output / 'SHA256SUMS.txt'), 'commands': COMMANDS}, indent=2) + '\n')
    print('Tester kit ready: ' + str(output), flush=True)


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        if RUN is not None:
            with (RUN / 'failure.json').open('x') as stream:
                json.dump({'exitCode': 1, 'error': str(error), 'type': type(error).__name__,
                           'commands': COMMANDS, 'packageStatus': 'BLOCKED / NOT_TESTED',
                           'cleanup': 'No game launch/deployment; partial artifacts retained.'}, stream, indent=2)
        raise
