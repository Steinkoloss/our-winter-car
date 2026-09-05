#!/usr/bin/env python3
"""Build a versioned tester kit without deploying, committing or publishing it."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
LAUNCHER = ROOT / 'src/WinterMP.Launcher/WinterMP.Launcher.csproj'
REQUIRED = ('WinterMP.Core.dll', 'WinterMP.Net.dll', 'WinterMP.FastBoot.dll',
            'sync-catalog.json', 'wintermp-compat.json')
FORBIDDEN = {'UnityEngine.dll', 'Assembly-CSharp.dll', 'Assembly-CSharp-firstpass.dll', 'PlayMaker.dll'}


def run(args, **kwargs):
    print('Running ' + ' '.join(map(str, args[:3])), flush=True)
    subprocess.run(list(map(str, args)), cwd=kwargs.pop('cwd', ROOT), check=True, **kwargs)


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def project_version(path):
    return ET.parse(path).findtext('./PropertyGroup/Version')


def archive_tree(source, target, prefix=''):
    with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for path in sorted(source.rglob('*')):
            if path.is_file():
                if path.name in FORBIDDEN:
                    raise ValueError('Refusing to package game assembly ' + str(path))
                archive.write(path, prefix + path.relative_to(source).as_posix())
    with zipfile.ZipFile(target) as archive:
        if archive.testzip() is not None:
            raise ValueError('Archive integrity check failed: ' + str(target))


def verify_publish(publish, source_manifest):
    sources = {
        'WinterMP.Core.dll': ROOT / 'src/WinterMP.Core/bin/Release/net35/WinterMP.Core.dll',
        'WinterMP.Net.dll': ROOT / 'src/WinterMP.Net/bin/Release/net35/WinterMP.Net.dll',
        'WinterMP.FastBoot.dll': ROOT / 'src/WinterMP.FastBoot/bin/Release/net35/WinterMP.FastBoot.dll',
        'sync-catalog.json': ROOT / 'catalog/sync-catalog.json',
    }
    payload = publish / 'payload'
    manifest = json.loads((payload / 'wintermp-compat.json').read_text(encoding='utf-8-sig'))
    for key, value in source_manifest.items():
        if manifest.get(key) != value:
            raise ValueError('Published manifest differs from source: ' + key)
    for name, source in sources.items():
        if source.read_bytes() != (payload / name).read_bytes():
            raise ValueError('Published payload differs from its build: ' + name)
    manifest['payloadSha256'] = {name: digest(payload / name) for name in sources}
    (payload / 'wintermp-compat.json').write_text(json.dumps(manifest, indent=2) + '\n')
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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--skip-build', action='store_true', help='Verify and package the existing versioned publish folders')
    parser.add_argument('--skip-tests', action='store_true', help='Require existing passing TRX reports instead of rerunning tests')
    parser.add_argument('--inno-prefix', type=Path, help='Separate Wine prefix containing drive_c/inno/ISCC.exe')
    parser.add_argument('--cosmocc', type=Path, help='Build the universal installer using this compiler')
    parser.add_argument('--appimage', action='store_true', help='Also build the Linux AppImage')
    args = parser.parse_args()
    source_manifest = json.loads((ROOT / 'src/WinterMP.Launcher/Assets/wintermp-compat.json').read_text(encoding='utf-8-sig'))
    version = source_manifest['modVersion']
    if source_manifest.get('releaseChannel') != 'test':
        raise ValueError('Use a test-channel compatibility manifest for a tester kit')
    for project in (LAUNCHER, ROOT / 'src/WinterMP.Core/WinterMP.Core.csproj'):
        if project_version(project) != version:
            raise ValueError('Release versions disagree: ' + str(project))
    if 'AppVersion=' + version + '\n' not in (ROOT / 'installer/WinterMP.iss').read_text():
        raise ValueError('Installer version differs from the mod')
    work = ROOT / 'build/test-release' / ('v' + version)
    output = ROOT / 'dist' / ('test-v' + version)
    publish = {rid: work / 'publish' / rid for rid in ('win-x64', 'linux-x64')}
    reports = ROOT / 'build/test-results'
    output.mkdir(parents=True, exist_ok=True)
    if not args.skip_build:
        for name in ('WinterMP.Net', 'WinterMP.Core', 'WinterMP.Tools', 'WinterMP.FastBoot'):
            run(['dotnet', 'build', ROOT / 'src' / name / (name + '.csproj'), '-c', 'Release',
                 '-t:Rebuild', '-p:DeployToGame=false', '-m:1', '-nr:false'])
    if not args.skip_tests:
        for name, report in (('WinterMP.Net.Tests', 'protocol-release.trx'), ('WinterMP.Launcher.Tests', 'launcher-release.trx')):
            run(['dotnet', 'test', ROOT / 'src' / name / (name + '.csproj'), '-c', 'Release',
                 '-p:DeployToGame=false', '-m:1', '-nr:false', '--logger', 'trx;LogFileName=' + report,
                 '--results-directory', reports])
        audit = subprocess.check_output(['dotnet', 'list', str(LAUNCHER), 'package', '--vulnerable',
                                         '--include-transitive', '--format', 'json', '--no-restore'], cwd=ROOT)
        (reports / 'launcher-advisories.json').write_bytes(audit)
    tests = {name: read_test_result(reports / (name + '-release.trx')) for name in ('protocol', 'launcher')}
    audit = json.loads((reports / 'launcher-advisories.json').read_text())
    if not audit.get('projects') or audit.get('problems'):
        raise ValueError('Package advisory verification did not complete')
    for project in audit['projects']:
        for framework in project.get('frameworks', []):
            for key in ('topLevelPackages', 'transitivePackages'):
                for package in framework.get(key, []):
                    if package.get('vulnerabilities'):
                        raise ValueError('Resolve reported package vulnerabilities before shipping: ' + package['id'])
    run([sys.executable, '-m', 'unittest', 'discover', '-s', 'tools/tests'])
    manifests = {}
    for rid, directory in publish.items():
        if not args.skip_build:
            if directory.exists():
                shutil.rmtree(directory)
            run(['dotnet', 'publish', LAUNCHER, '-c', 'Release', '-r', rid, '--self-contained', 'true',
                 '-p:PublishSingleFile=false', '-p:DeployToGame=false', '-m:1', '-nr:false', '-o', directory])
        manifests[rid] = verify_publish(directory, source_manifest)
    if manifests['win-x64'] != manifests['linux-x64']:
        raise ValueError('Windows and Linux payloads differ')
    for rid, directory in publish.items():
        archive_tree(directory, output / ('OurWinterCar-Launcher-' + rid + '.zip'))
    archive_tree(publish['win-x64'] / 'payload', output / 'OurWinterCar-payload.zip')
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
    files = subprocess.check_output(['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'], cwd=ROOT).decode().split('\0')
    source_zip = output / ('OurWinterCar-source-v' + version + '.zip')
    with zipfile.ZipFile(source_zip, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name in sorted(set(files)):
            path = ROOT / name
            if name and path.is_file():
                if path.name in FORBIDDEN or name == 'Directory.Build.props.user':
                    raise ValueError('Local game files must not enter the source package')
                archive.write(path, 'OurWinterCar-source-v' + version + '/' + name)
    with zipfile.ZipFile(source_zip) as archive:
        if archive.testzip() is not None:
            raise ValueError('Source archive integrity check failed')
    validation = {
        'version': version, 'channel': 'test', 'protocol': source_manifest['protocolVersion'],
        'targetGameBuildIds': source_manifest['targetGameBuildIds'],
        'baseCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT).decode().strip(),
        'workingTreeChangesIncluded': bool(subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT).strip()),
        'tests': tests, 'evidenceToolTests': 5,
        'reportedDependencyVulnerabilities': 0,
        'runtime': json.loads((publish['win-x64'] / 'WinterMPLauncher.runtimeconfig.json').read_text())['runtimeOptions'].get('includedFrameworks'),
        'payloadSha256': manifests['win-x64']['payloadSha256'],
        'twoPlayerGameplay': 'pending tester validation',
    }
    smoke = ROOT / 'build/runtime-smoke/result.json'
    if smoke.exists():
        validation['runtimeSmoke'] = json.loads(smoke.read_text())
    (output / 'validation.json').write_text(json.dumps(validation, indent=2) + '\n')
    sums = ''.join(digest(path) + '  ' + path.name + '\n' for path in sorted(output.iterdir())
                   if path.is_file() and path.name != 'SHA256SUMS.txt' and not path.name.endswith(('.dbg', '.elf')))
    (output / 'SHA256SUMS.txt').write_text(sums)
    print('Tester kit ready: ' + str(output), flush=True)


if __name__ == '__main__':
    main()
