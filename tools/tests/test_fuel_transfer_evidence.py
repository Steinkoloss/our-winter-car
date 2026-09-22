"""Command receipt regressions; subprocess fixtures are NOT native evidence."""
import hashlib
import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import fuel_transfer_evidence as evidence

SCRIPT = Path(__file__).resolve().parents[1] / 'fuel_transfer_evidence.py'


class CommandReceiptTests(unittest.TestCase):
    def test_real_timeout_waits_owned_child_and_preserves_its_output(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            script = root / 'slow.py'
            script.write_text('import os, time\nprint(os.getpid(), flush=True)\ntime.sleep(30)\n')
            row = evidence.run_command(root / 'real-timeout', [sys.executable, str(script)], timeout=.2)
            self.assertEqual(124, row['exit_code'])
            pid = int((root / 'real-timeout/raw.log').read_text())
            with self.assertRaises(ProcessLookupError): os.kill(pid, 0)
            self.assertEqual(hashlib.sha256((root / 'real-timeout/raw.log').read_bytes()).hexdigest(), row['raw_sha256'])

    def test_real_missing_executable_and_nonzero_exit_are_retained(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            missing = evidence.run_command(root / 'real-missing', [str(root / 'absent')])
            self.assertEqual(127, missing['exit_code'])
            script = root / 'failed.py'
            script.write_text('print("real portable failure", flush=True)\nraise SystemExit(7)\n')
            row = evidence.run_command(root / 'real-failure', [sys.executable, str(script)])
            self.assertEqual(7, row['exit_code'])
            self.assertFalse(row['timed_out'])
            self.assertIn('real portable failure', (root / 'real-failure/raw.log').read_text())

    def invoke(self, output, execute):
        argv = [str(SCRIPT), 'run', str(output), '--', 'fixture-command']
        with patch.object(sys, 'argv', argv), patch('subprocess.run', side_effect=execute):
            try:
                runpy.run_path(str(SCRIPT), run_name='__main__')
            except (SystemExit, OSError, subprocess.TimeoutExpired):
                pass
        self.assertTrue((output / 'receipt.json').is_file(), 'Failure lost command receipt')
        return json.loads((output / 'receipt.json').read_text())

    def test_timeout_keeps_raw_output_exit_and_source_linkage(self):
        def timeout(argv, **kwargs):
            kwargs['stdout'].write('partial command output\n')
            kwargs['stdout'].flush()
            raise subprocess.TimeoutExpired(argv, kwargs['timeout'])
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp) / 'timeout'
            row = self.invoke(output, timeout)
            self.assertEqual(124, row['exit_code'])
            self.assertTrue(row['timed_out'])
            self.assertIn('TimeoutExpired', row['error'])
            self.assertEqual(['fixture-command'], row['argv'])
            self.assertEqual(str(SCRIPT.parents[1]), row['cwd'])
            self.assertEqual(b'partial command output\n', (output / 'raw.log').read_bytes())
            self.assertEqual(hashlib.sha256((output / 'raw.log').read_bytes()).hexdigest(), row['raw_sha256'])
            self.assertIn('tools/fuel_transfer_evidence.py', row['source_sha256'])
            self.assertIn('started_utc', row)
            self.assertIn('ended_utc', row)

    def test_missing_executable_keeps_127_receipt_not_native_success(self):
        with tempfile.TemporaryDirectory() as tmp:
            row = self.invoke(Path(tmp) / 'missing', FileNotFoundError('fixture missing executable'))
            self.assertEqual(127, row['exit_code'])
            self.assertFalse(row['timed_out'])
            self.assertIn('FileNotFoundError', row['error'])

    def test_existing_evidence_refused_before_command(self):
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp)
            (output / 'raw.log').write_text('original')
            argv = [str(SCRIPT), 'run', str(output), '--', 'unused']
            with patch.object(sys, 'argv', argv), patch('subprocess.run') as execute:
                with self.assertRaises(FileExistsError):
                    runpy.run_path(str(SCRIPT), run_name='__main__')
                execute.assert_not_called()
            self.assertEqual('original', (output / 'raw.log').read_text())


if __name__ == '__main__':
    unittest.main()
