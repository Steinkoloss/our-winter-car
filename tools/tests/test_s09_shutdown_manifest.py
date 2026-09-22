"""Fail-closed receipt summary parsing, independent of native/game availability."""
import importlib.util
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('shutdown_manifest', SOURCE / 'tools/s09_shutdown_manifest.py')
MANIFEST = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MANIFEST)


class ShutdownManifestTests(unittest.TestCase):
    def test_complete_unskipped_summaries_in_both_cli_languages(self):
        for log in ('Total tests: 35\n     Passed: 35\nTest Run Successful.\n',
                    'Gesamtzahl Tests: 35\n     Bestanden: 35\nDer Testlauf war erfolgreich.\n'):
            self.assertEqual(dict(total=35, passed=35, failed=0), MANIFEST.dotnet_result(log))

    def test_red_requires_all_and_only_expected_behavior_failures(self):
        expected = MANIFEST.red_failures()
        self.assertEqual(10, len(expected))
        self.assertEqual(10, len(set(expected)))
        log = ''.join('[xUnit.net 00:00:01]     ' + name + ' [FAIL]\n' for name in expected)
        log += 'Total tests: 35\n     Passed: 25\n     Failed: 10\nTest Run Failed.\n'
        self.assertEqual(dict(total=35, passed=25, failed=10), MANIFEST.dotnet_result(log, expected))
        with self.assertRaises(AssertionError):
            MANIFEST.dotnet_result(log.replace(expected[0], 'UnrelatedFailure'), expected)
        with self.assertRaises(AssertionError):
            MANIFEST.dotnet_result(log, expected[:-1])

    def test_incomplete_skipped_inconsistent_or_failed_green_rejected(self):
        good = 'Total tests: 35\n     Passed: 35\nTest Run Successful.\n'
        for bad in (good.replace('35', '0'), good.replace('Passed: 35', 'Passed: 34'),
                    good + '     Skipped: 1\n', good + '     Failed: 1\n',
                    good + 'Total tests: 35\n', good.replace('Test Run Successful.', ''),
                    'error CS1061: missing dependency\n',
                    good + '[xUnit.net 00:00:01]     unexpected [FAIL]\n'):
            with self.assertRaises(AssertionError, msg=bad):
                MANIFEST.dotnet_result(bad)


if __name__ == '__main__':
    unittest.main()
