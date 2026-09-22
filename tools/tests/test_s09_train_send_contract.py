"""Contract-repair audit tests. Deliberately mutated strings are not execution receipts."""
from pathlib import Path
import sys
import unittest

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
import s09_train_send_manifest as manifest


class TrainSendContractTests(unittest.TestCase):
    def fixture(self):
        current = (manifest.SOURCE / manifest.ISOLATION).read_text()
        added = manifest.extract(current, '        [Fact]\n        public void ' + manifest.SELECTED + '()') + '\n\n'
        original = current.replace(added, '', 1).replace(manifest.FIXTURE_ADDITION, '', 1)
        return original, current

    def test_only_explicit_fixture_change_and_added_regression_are_allowed(self):
        original, current = self.fixture()
        result = manifest.verify_isolation_revision(original, current)
        self.assertTrue(result['unchanged_assertions'])
        self.assertEqual(manifest.FIXTURE_ADDITION, result['fixture_delta'])

    def test_all_send_fallback_assertion_cannot_be_relaxed(self):
        original, current = self.fixture()
        self.assertIn('Assert.Equal(1, attempts);', current)
        weakened = current.replace('Assert.Equal(1, attempts);', 'Assert.Equal(2, attempts);')
        with self.assertRaises(AssertionError):
            manifest.verify_isolation_revision(original, weakened)

    def test_unrelated_baseline_assertion_cannot_be_changed(self):
        original, current = self.fixture()
        weakened = current.replace('Assert.Equal(0, world.Prepared);', 'Assert.Equal(1, world.Prepared);', 1)
        self.assertNotEqual(current, weakened)
        with self.assertRaises(AssertionError):
            manifest.verify_isolation_revision(original, weakened)

    def test_missing_or_extra_fixture_change_is_rejected(self):
        original, current = self.fixture()
        for changed in (current.replace(manifest.FIXTURE_ADDITION, ''),
                        current.replace(manifest.FIXTURE_ADDITION, manifest.FIXTURE_ADDITION * 2)):
            with self.subTest(changed=changed[-30:]), self.assertRaises(AssertionError):
                manifest.verify_isolation_revision(original, changed)

    def test_result_gate_rejects_missing_skipped_or_unexpected_failures(self):
        # Parser-only sample, not represented as a test execution result.
        log = 'Test Run Successful.\nTotal tests: 2\n     Passed: 2\n'
        manifest.verify_test_result(log, 2)
        bad = (log.replace('Total tests: 2', 'Total tests: 1'),
               log.replace('Passed: 2', 'Passed: 1'),
               log + 'Skipped: 1\n',
               log + '[xUnit.net 00:00:00.01]     Unexpected.Case [FAIL]\n')
        for changed in bad:
            with self.subTest(changed=changed), self.assertRaises(AssertionError):
                manifest.verify_test_result(changed, 2)


if __name__ == '__main__':
    unittest.main()
