"""Parser negatives are synthetic unit inputs, never native or behavioral receipts."""
import importlib.util
from pathlib import Path
import sys
import unittest

SOURCE = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(SOURCE / 'tools'))
SPEC = importlib.util.spec_from_file_location('disposal_manifest', SOURCE / 'tools/s09_shutdown_disposal_manifest.py')
MANIFEST = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MANIFEST)


class ShutdownDisposalManifestTests(unittest.TestCase):
    def test_red_requires_exact_new_disposal_failures_and_all_inherited_passes(self):
        expected = MANIFEST.red_failures()
        self.assertEqual(21, len(expected))
        self.assertEqual(21, len(set(expected)))
        self.assertEqual(12, sum('PreservesPrimary' in name for name in expected))
        self.assertEqual(8, sum('DeferredCleanup' in name for name in expected))
        self.assertEqual(1, sum('WithoutTransport' in name for name in expected))
        log = ''.join('[xUnit.net 00:00:01]     ' + name + ' [FAIL]\n' for name in expected)
        log += 'Total tests: 63\n     Passed: 42\n     Failed: 21\nTest Run Failed.\n'
        self.assertEqual(dict(total=63, passed=42, failed=21), MANIFEST.dotnet_result(log, expected))
        for invalid in (log.replace(expected[0], 'PaneResetFailure'), log.replace('Passed: 42', 'Passed: 41'),
                        log + '     Skipped: 1\n', 'error CS1061: missing callback\n'):
            with self.assertRaises(AssertionError):
                MANIFEST.dotnet_result(invalid, expected)

    def test_green_rejects_even_one_disposal_failure(self):
        good = 'Total tests: 63\n     Passed: 63\nTest Run Successful.\n'
        self.assertEqual(dict(total=63, passed=63, failed=0), MANIFEST.dotnet_result(good))
        with self.assertRaises(AssertionError):
            MANIFEST.dotnet_result(good + '[xUnit.net 00:00:01]     ' + MANIFEST.red_failures()[0] + ' [FAIL]\n')

    def test_production_delta_rejects_out_of_scope_send_and_reset_changes(self):
        before = 'private void DisposeSessionTransport() { OldDispose(); }\npublic void Shutdown() { Send(); }'
        after = before.replace('OldDispose();', 'NewDispose();')
        MANIFEST.verify_delta(before, after)
        for invalid in (before, after.replace('Send();', 'RetrySend();'), after + '\nvoid Reset() {}'):
            with self.assertRaises(AssertionError):
                MANIFEST.verify_delta(before, invalid)


if __name__ == '__main__':
    unittest.main()
