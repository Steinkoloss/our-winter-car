"""Offline readiness fixtures. Constructed logs/dumps are NOT native evidence."""
import json
import os
from pathlib import Path
import struct
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import wiring_readiness_diagnostic as diagnostic


class WiringReadinessTests(unittest.TestCase):
    def test_ready_flag_and_zero_launcher_exit_do_not_prove_world_response(self):
        timeline = [dict(event='wiring launched', values=dict(role='host', launcher_pid=42)),
                    dict(event='command sent', values=dict(role='host', sequence=1, args=['wire-view'])),
                    dict(event='launcher exited before response', values=dict(role='host', exit_code=0))]
        result = diagnostic.assess('HostLocal ready\nLoadLevel: GAME\nWheel.Awake ()', timeline,
                                   'mono.dll caused an Access Violation (0xc0000005)')
        self.assertTrue(result['mutex_ready_logged'])
        self.assertTrue(result['game_load_requested'])
        self.assertFalse(result['game_level_observed'])
        self.assertFalse(result['host_command_response'])
        self.assertFalse(result['guest_launched'])
        self.assertEqual('CRASH_BEFORE_COMMAND_RESPONSE', result['status'])
        self.assertFalse(result['native_gameplay_pass'])

    def test_other_role_or_failed_response_cannot_satisfy_host_readiness(self):
        for values in (dict(role='guest', ok=True), dict(role='host', ok=False)):
            result = diagnostic.assess('', [dict(event='command response', values=values)], '')
            self.assertFalse(result['host_command_response'])
            self.assertEqual('NO_HOST_COMMAND_RESPONSE', result['status'])
        result = diagnostic.assess("Level changed: 'MainMenu' -> 'GAME'", [
            dict(event='command sent', values=dict(role='host', sequence=1, args=['wire-view'])),
            dict(event='command response', values=dict(role='host', sequence=1, ok=True))], '')
        self.assertTrue(result['host_command_response'])
        self.assertTrue(result['game_level_observed'])
        self.assertEqual('RETAINED_HOST_RESPONSE_ONLY', result['status'])
        self.assertFalse(result['native_gameplay_pass'])

    def test_quit_response_or_unmatched_sequence_is_not_wire_readiness(self):
        for verb, sequence in (('quit', 1), ('wire-view', 2)):
            result = diagnostic.assess('', [
                dict(event='command sent', values=dict(role='host', sequence=1, args=[verb])),
                dict(event='command response', values=dict(role='host', sequence=sequence, ok=True))], '')
            self.assertFalse(result['host_command_response'])

    def test_minidump_exception_maps_address_to_module_not_root_cause(self):
        data = bytearray(512)
        struct.pack_into('<4sII', data, 0, b'MDMP', 0, 2)
        struct.pack_into('<I', data, 12, 32)
        struct.pack_into('<III', data, 32, 6, 168, 64)
        struct.pack_into('<III', data, 44, 4, 112, 240)
        struct.pack_into('<I', data, 64, 7)
        struct.pack_into('<I', data, 72, 0xc0000005)
        struct.pack_into('<Q', data, 88, 0x101234)
        struct.pack_into('<I', data, 240, 1)
        struct.pack_into('<QI', data, 244, 0x100000, 0x20000)
        struct.pack_into('<I', data, 264, 360)
        name = 'fixture/mono.dll'.encode('utf-16-le')
        struct.pack_into('<I', data, 360, len(name))
        data[364:364+len(name)] = name
        result = diagnostic.minidump_exception(bytes(data))
        self.assertEqual('0xc0000005', result['exception_code'])
        self.assertEqual('0x1234', result['module_offset'])
        self.assertEqual('fixture/mono.dll', result['module'])
        for bad in (b'', b'not a dump', bytes(data[:90])):
            with self.assertRaises(ValueError): diagnostic.minidump_exception(bad)

    def test_retained_input_cannot_redirect_or_read_outside_rounds(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            rounds = root / 'rounds'
            rounds.mkdir()
            outside = root / 'protected.log'
            outside.write_text('must not read')
            alias = rounds / 'redirect.log'
            alias.symlink_to(outside)
            with patch.object(diagnostic.prov.diag, 'ROUNDS', rounds):
                for path in (outside, alias):
                    with self.assertRaises(ValueError): diagnostic.read_retained(path)
                real = rounds / 'retained.log'
                real.write_text('owned historical evidence')
                self.assertEqual(b'owned historical evidence', diagnostic.read_retained(real))

    def test_assigned_run_is_required_before_any_historical_read(self):
        with tempfile.TemporaryDirectory() as tmp:
            run = Path(tmp)
            with patch.dict(os.environ, {'RUN': ''}), patch.object(diagnostic, 'read_retained') as read:
                with self.assertRaises(ValueError):
                    diagnostic.investigate(run, 'new', run, run, run, run / 'report.json')
                read.assert_not_called()


if __name__ == '__main__':
    unittest.main()
