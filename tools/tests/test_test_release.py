"""Local packaging checks, not installer deployment or native gameplay evidence."""
import importlib.util
import json
from pathlib import Path
import re
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('test_release', ROOT / 'tools/build-test-release.py')
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class TestReleaseTests(unittest.TestCase):
    def test_current_manifest_matches_wire_constant(self):
        version = int(re.search(r'public const ushort Version = (\d+);',
                               (ROOT / 'src/WinterMP.Net/Protocol.cs').read_text()).group(1))
        manifest = json.loads((ROOT / 'src/WinterMP.Launcher/Assets/wintermp-compat.json').read_text())
        self.assertEqual(version, manifest['protocolVersion'], 'tester manifest is stale')

    def test_current_player_docs_match_source_without_historical_test_claims(self):
        manifest = json.loads((ROOT / 'src/WinterMP.Launcher/Assets/wintermp-compat.json').read_text())
        stamp = f"Local package: mod **{manifest['modVersion']}**, protocol **{manifest['protocolVersion']}**, channel **test**."
        for name in ('TESTING.md', 'PLAYERS.md', 'RELEASE-NOTES.md', 'BUILDING.md'):
            text = (ROOT / 'docs' / name).read_text()
            self.assertIn(stamp, text, name)
            self.assertIn('UNRELEASED LOCAL TEST PACKAGE', text, name)
            if name != 'BUILDING.md':
                self.assertNotRegex(text, r'1,067|66 isolated|protocol\s+\*?\*?120\b')
                self.assertIn('NOT_TESTED', text, name)

    def test_source_preflight_checks_all_versions_and_wire_spec(self):
        release.validate_source()

    def test_installer_welcome_is_current_and_unreleased(self):
        manifest = json.loads((ROOT / 'src/WinterMP.Launcher/Assets/wintermp-compat.json').read_text())
        text = (ROOT / 'installer/welcome.txt').read_text()
        self.assertIn(f"Local package: mod {manifest['modVersion']}, protocol {manifest['protocolVersion']}, channel test.", text)
        self.assertIn('UNRELEASED LOCAL TEST PACKAGE', text)
        self.assertIn('NOT_TESTED', text)

    def test_archive_rejects_game_assemblies_secrets_and_dev_probes(self):
        for name in ('UnityEngine.dll', 'unityengine.DLL', 'ES2.dll', 'PlayMaker.dll',
                     'Assembly-CSharp-firstpass.dll', '.env', '.env.local',
                     'Directory.Build.props.user', 'id_rsa', 'credentials.json',
                     'GuestSaveProbe.dll', 'WinterMP.Tools.dll', 'live-bag/host-command.txt'):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as tmp:
                source = Path(tmp) / 'publish'
                path = source / name
                path.parent.mkdir(parents=True)
                path.write_bytes(b'portable forbidden-path fixture, not a real dependency')
                with self.assertRaises(ValueError):
                    release.archive_tree(source, Path(tmp) / 'output.zip')

    def test_archive_rejects_symlinks_and_does_not_clobber(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / 'publish'
            source.mkdir()
            (root / 'outside').write_text('not payload')
            (source / 'notes.txt').symlink_to(root / 'outside')
            with self.assertRaises(ValueError):
                release.archive_tree(source, root / 'link.zip')
            (source / 'notes.txt').unlink()
            (source / 'notes.txt').write_text('valid fixture')
            target = root / 'output.zip'
            release.archive_tree(source, target)
            original = target.read_bytes()
            with self.assertRaises(FileExistsError):
                release.archive_tree(source, target)
            self.assertEqual(original, target.read_bytes())
            with zipfile.ZipFile(target) as archive:
                self.assertIsNone(archive.testzip())
                self.assertEqual(['notes.txt'], archive.namelist())

    def test_payload_requires_exact_build_bytes_and_hashes_every_file(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            payload = root / 'payload'
            payload.mkdir()
            sources = {}
            for name in release.REQUIRED[:-1]:
                source = root / name
                source.write_bytes(('fixture:' + name).encode())
                sources[name] = source
                (payload / name).write_bytes(source.read_bytes())
            manifest = {'modVersion': '1.2.3', 'protocolVersion': 9, 'releaseChannel': 'test'}
            (payload / 'wintermp-compat.json').write_text(json.dumps(manifest))
            result = release.verify_payload(payload, manifest, sources)
            self.assertEqual(set(sources), set(result['payloadSha256']))
            for name, source in sources.items():
                self.assertEqual(release.digest(source), result['payloadSha256'][name])
            (payload / 'WinterMP.FastBoot.dll').write_text('stale')
            with self.assertRaisesRegex(ValueError, 'differs'):
                release.verify_payload(payload, manifest, sources)

    def test_fresh_run_refuses_previous_receipts(self):
        with tempfile.TemporaryDirectory() as tmp:
            run = Path(tmp) / 'attempt'
            release.prepare_run(run)
            marker = run / 'receipt.json'
            marker.write_text('keep')
            with self.assertRaises(FileExistsError):
                release.prepare_run(run)
            self.assertEqual('keep', marker.read_text())


if __name__ == '__main__':
    unittest.main()
