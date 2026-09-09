"""Verification's failure paths, without a game installation or .NET SDK."""
import importlib.util
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("wintermp_verify", Path(__file__).parents[1] / "verify.py")
verify = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(verify)


def trx(total=1, executed=1, passed=1, failed=0, outcome="Passed"):
    return ('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            '<Results><UnitTestResult outcome="%s" /></Results>'
            '<ResultSummary><Counters total="%s" executed="%s" passed="%s" failed="%s" />'
            '</ResultSummary></TestRun>') % (outcome, total, executed, passed, failed)


class VerifyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="wintermp test ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for name in verify.BUILDS + verify.TESTS + verify.GAME:
            path = self.root / verify.project(name)
            path.parent.mkdir(parents=True)
            path.write_text('<Project><PropertyGroup><TargetFrameworks>net35;netstandard2.0'
                            '</TargetFrameworks></PropertyGroup></Project>', encoding="utf-8")
        self.report = self.root / "test.trx"

    def test_existing_protocol_boundary_passes(self):
        verify.check_structure(self.root)

    def test_dropping_legacy_runtime_fails(self):
        path = self.root / verify.project("WinterMP.Net")
        path.write_text('<Project><TargetFramework>net8.0</TargetFramework></Project>')
        with self.assertRaises(verify.VerificationError):
            verify.check_structure(self.root)

    def test_engine_dependency_fails(self):
        path = self.root / verify.project("WinterMP.Net")
        text = path.read_text().replace('</Project>', '<ItemGroup><Reference Include="UnityEngine" /></ItemGroup></Project>')
        path.write_text(text)
        with self.assertRaises(verify.VerificationError):
            verify.check_structure(self.root)

    def test_project_dependency_fails(self):
        path = self.root / verify.project("WinterMP.Net")
        path.write_text(path.read_text().replace('</Project>', '<ProjectReference Include="../WinterMP.Core/x.csproj" /></Project>'))
        with self.assertRaises(verify.VerificationError):
            verify.check_structure(self.root)

    def test_missing_project_fails(self):
        (self.root / verify.project("WinterMP.Launcher.Tests")).unlink()
        with self.assertRaises(verify.VerificationError):
            verify.check_structure(self.root)

    def test_passing_report_counts_executed_tests(self):
        self.report.write_text(trx())
        self.assertEqual(verify.check_trx(self.report), 1)

    def test_bad_reports_never_pass(self):
        for text in ('', '<TestRun>', '<TestRun />', trx(total=0), trx(executed=0),
                     trx(passed=0), trx(failed=1), trx(total=2, executed=2, passed=2),
                     trx(outcome="NotExecuted"), trx(total="unknown")):
            with self.subTest(text=text):
                self.report.write_text(text)
                with self.assertRaises(verify.VerificationError):
                    verify.check_trx(self.report)

    def test_missing_report_fails(self):
        with self.assertRaises(verify.VerificationError):
            verify.check_trx(self.report)

    def fake_success(self, command, root):
        self.assertEqual(root, self.root)
        if command[1] == "test":
            out = Path(command[command.index("--results-directory") + 1])
            name = command[command.index("--logger") + 1].split("LogFileName=")[1]
            (out / name).write_text(trx())

    def test_portable_targets_explicit_projects_and_fresh_reports(self):
        calls = []
        def run(command, root):
            calls.append(command)
            self.fake_success(command, root)
        first = verify.verify(self.root, run_command=run)
        second = verify.verify(self.root, run_command=run)
        self.assertNotEqual(first, second)
        self.assertEqual(len(calls), 16)
        self.assertTrue(all(command[2].endswith('.csproj') for command in calls))
        self.assertFalse(any('WinterMP.Core' in ' '.join(command) for command in calls))

    def test_zero_exit_without_new_report_fails(self):
        old = self.root / '.artifacts/verification/old'
        old.mkdir(parents=True)
        for name in verify.TESTS:
            (old / (name + '.trx')).write_text(trx())
        with self.assertRaises(verify.VerificationError):
            verify.verify(self.root, run_command=lambda command, root: None)

    def test_failure_stops_before_later_commands(self):
        calls = []
        def fail(command, root):
            calls.append(command)
            raise verify.VerificationError("test failure")
        with self.assertRaises(verify.VerificationError):
            verify.verify(self.root, run_command=fail)
        self.assertEqual(len(calls), 1)

    def test_checks_only_does_not_run_builds(self):
        with patch.object(verify, 'run') as command:
            verify.verify(self.root, checks_only=True, run_command=command)
            command.assert_not_called()

    def test_game_mode_adds_three_plugin_builds(self):
        calls = []
        def run(command, root):
            calls.append(command)
            self.fake_success(command, root)
        verify.verify(self.root, game=True, run_command=run)
        self.assertEqual([command[2] for command in calls[-3:]],
                         [verify.project(name) for name in verify.GAME])

    def test_missing_sdk_is_failure(self):
        with patch.object(verify.subprocess, 'run', side_effect=FileNotFoundError):
            with self.assertRaises(verify.VerificationError):
                verify.run(['dotnet', 'restore', 'x.csproj'], self.root)


if __name__ == '__main__':
    unittest.main()
