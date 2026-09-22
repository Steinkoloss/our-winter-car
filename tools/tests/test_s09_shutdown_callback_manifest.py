"""Selected cleanup-callback red must not accept a compile failure or old send-finally red."""
import importlib.util
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('callback_manifest', SOURCE / 'tools/s09_shutdown_callback_manifest.py')
MANIFEST = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MANIFEST)


class ShutdownCallbackManifestTests(unittest.TestCase):
    def test_seven_selected_callback_failures_leave_all_inherited_cases_green(self):
        expected = MANIFEST.red_failures()
        self.assertEqual(7, len(expected))
        self.assertEqual(7, len(set(expected)))
        self.assertEqual(4, sum('AfterDisconnectFailure' in name for name in expected))
        self.assertEqual(3, sum('WithoutDisconnectFailure' in name for name in expected))
        log = ''.join('[xUnit.net 00:00:01]     ' + name + ' [FAIL]\n' for name in expected)
        log += 'Total tests: 42\n     Passed: 35\n     Failed: 7\nTest Run Failed.\n'
        self.assertEqual(dict(total=42, passed=35, failed=7), MANIFEST.dotnet_result(log, expected))
        for invalid in (log.replace(expected[0], 'UnrelatedFailure'), log.replace('Passed: 35', 'Passed: 34'),
                        log + '     Skipped: 1\n', 'error CS1061: missing callback\n'):
            with self.assertRaises(AssertionError):
                MANIFEST.dotnet_result(invalid, expected)

    def test_green_rejects_even_one_callback_failure(self):
        good = 'Total tests: 42\n     Passed: 42\nTest Run Successful.\n'
        self.assertEqual(dict(total=42, passed=42, failed=0), MANIFEST.dotnet_result(good))
        with self.assertRaises(AssertionError):
            MANIFEST.dotnet_result(good + '[xUnit.net 00:00:01]     ' + MANIFEST.red_failures()[0] + ' [FAIL]\n')


if __name__ == '__main__':
    unittest.main()
