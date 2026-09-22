"""Parser fixtures only: never native responses or execution evidence."""
import copy
import importlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))


def sample():
    return dict(host_is_host=True, guest_is_host=False, host_id=0, guest_id=2, observed_actor_id=2,
                pose_now=10.2, pose_timestamp=10.2, guest_alive=True, guest_pose_matches=True,
                host_guest_distance_squared=10000, host_car_distance_squared=10000,
                host_has_tool=False, host_lease_holder=2, guest_move_state=0,
                guest_passenger=False, guest_inside_trigger=False, guest_local_inside=False,
                host_vehicle_id=9, guest_vehicle_id=9, host_bound_vehicle_id=9, guest_bound_vehicle_id=9,
                host_vehicle_path='CORRIS', guest_vehicle_path='CORRIS',
                host_vehicle_body='CORRIS', guest_vehicle_body='CORRIS',
                host_is_vehicle=True, guest_is_vehicle=True, host_local_owner=False, guest_local_owner=False,
                host_remote_owner=255, guest_remote_owner=255,
                host_linear_squared=0, guest_linear_squared=0, host_angular_squared=0, guest_angular_squared=0,
                host_tool_id=77, guest_tool_id=77, host_tool_name='ice scraper(itemx)', guest_tool_name='ice scraper(itemx)')


def parser_records():
    # Deliberately constructed PARSER unit inputs; never emitted as run receipts.
    records = []
    def add(kind, stage, data):
        records.append(dict(kind=kind, stage=stage, data=data, run_token='unit-token',
                            evidence_level='portable-engine-session-physics-fixture'))
    add('preconditions', 'before-pickup', sample())
    for i, (stage, status, seq) in enumerate((('positive', 'Accepted', 4), ('wrong-pane', 'WrongPane', 6),
            ('corrected-replay', 'ReplayedSequence', 6), ('fresh-recovery', 'Accepted', 7),
            ('recovery-duplicate', 'ReplayedSequence', 7))):
        if i in (1, 3): add('preconditions', 'before-wrong' if i == 1 else 'before-recovery', sample())
        strokes = 1 if i < 3 else 2
        cutoff = .255 if i < 3 else .26
        add('decision', stage, dict(stage=stage, status=status, actor=2, is_decision=True, sequence=seq,
            high_water=seq, lease_seen=seq, guest_sequence=seq, revision=strokes + 1, cutoff=cutoff,
            epoch=42, vehicle_id=9, tool_id=77, holder=2, equipped=True, host_decisions=i+1,
            accepted_for_sequence=1 if i in (0, 3, 4) else 0, host_glass=strokes, guest_glass=0,
            host_material_writes=strokes, host_effects=0, guest_effects=strokes, host_heat=0,
            guest_heat=strokes*.47, host_cutoff=cutoff, guest_cutoff=cutoff, host_material=cutoff, guest_material=cutoff))
    add('cleanup', 'finally', dict(mutation_disarmed=True, observer_detached=True, bridges_reset=True))
    return records


class BridgeReadinessTests(unittest.TestCase):
    def setUp(self):
        self.audit = importlib.import_module('v11_bridge_readiness')

    def test_every_required_operand_missing_or_null_is_not_ready(self):
        for key in sample():
            for null in (False, True):
                with self.subTest(key=key, null=null):
                    data = sample()
                    if null:
                        data[key] = None
                    else:
                        del data[key]
                    matrix = self.audit.preconditions(data)
                    self.assertFalse(matrix['ready'])
                    self.assertIn(key, matrix['missing_fields'])
                    self.assertEqual('NOT_READY', matrix['status'])

    def test_pane_park_and_fresh_pose_alone_never_satisfy_bridge_contract(self):
        for data in ({}, {'pane-park': 'OK'}, {'pose_now': 10.2, 'pose_timestamp': 10.2, 'guest_alive': True}):
            matrix = self.audit.preconditions(data)
            self.assertFalse(matrix['ready'])
            self.assertGreaterEqual(len(matrix['missing_fields']), 6)
        valid = self.audit.preconditions(sample())
        self.assertTrue(valid['ready'])
        self.assertEqual('FIXTURE_READY', valid['status'])
        self.assertTrue(all(row['status'] == 'SATISFIED_FIXTURE' for row in valid['matrix'].values()))

    def test_invalid_host_ownership_pose_identity_seat_trigger_motion_fail_closed(self):
        changes = dict(host_is_host=False, guest_is_host=True, observed_actor_id=3,
                       host_guest_distance_squared=4, host_car_distance_squared=4,
                       host_has_tool=True, host_lease_holder=0,
                       host_local_owner=True, guest_local_owner=True, host_remote_owner=2, guest_remote_owner=0,
                       pose_timestamp=9, guest_alive=False, guest_pose_matches=False,
                       guest_vehicle_id=10, guest_bound_vehicle_id=10, host_vehicle_path='SORBET',
                       guest_vehicle_body='other', guest_is_vehicle=False,
                       guest_move_state=8, guest_passenger=True, guest_inside_trigger=True, guest_local_inside=True,
                       host_linear_squared=.00001, guest_linear_squared=1, host_angular_squared=1, guest_angular_squared=.001,
                       guest_tool_id=78, guest_tool_name='camera scraper')
        for key, value in changes.items():
            with self.subTest(key=key, value=value):
                data = sample(); data[key] = value
                self.assertFalse(self.audit.preconditions(data)['ready'])
        for key in ('pose_now', 'pose_timestamp', 'host_guest_distance_squared', 'host_linear_squared'):
            for value in (float('nan'), float('inf'), -1, '0', True):
                with self.subTest(key=key, value=value):
                    data = sample(); data[key] = value
                    self.assertFalse(self.audit.preconditions(data)['ready'])
        for timestamp in (0, 11, 9.59):
            data = sample(); data['pose_timestamp'] = timestamp
            self.assertFalse(self.audit.preconditions(data)['ready'])

    def test_raw_trx_missing_matrix_is_rejected_even_when_test_is_passed(self):
        # This is a parser unit-test XML document, not a native or fixture result.
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'unit.trx'
            path.write_text('<TestRun><Results><UnitTestResult outcome="Passed" testName="' +
                            self.audit.TEST_NAME + '"><Output><StdOut>preconditions: pane-park OK</StdOut>' +
                            '</Output></UnitTestResult></Results></TestRun>')
            with self.assertRaisesRegex(ValueError, 'preconditions'):
                self.audit.parse_fixture(path, 'unit-token')

    def parse_records(self, records, token='unit-token', outcome='Passed'):
        with tempfile.TemporaryDirectory() as tmp:
            root = ET.Element('TestRun'); results = ET.SubElement(root, 'Results')
            result = ET.SubElement(results, 'UnitTestResult', testName=self.audit.TEST_NAME, outcome=outcome)
            stdout = ET.SubElement(ET.SubElement(result, 'Output'), 'StdOut')
            stdout.text = '\n'.join(self.audit.PREFIX + json.dumps(r) for r in records)
            path = Path(tmp) / 'parser-unit.trx'; ET.ElementTree(root).write(path)
            return self.audit.parse_fixture(path, token)

    def test_recovery_report_rejects_stale_wrong_duplicate_or_mutating_results(self):
        self.assertEqual('PASS_FIXTURE_ONLY', self.parse_records(parser_records())['status'])
        bad_changes = ((1, 'sequence', 0), (3, 'status', 'Accepted'), (3, 'high_water', 4),
                       (3, 'guest_cutoff', .26), (3, 'revision', 3), (4, 'guest_effects', 2),
                       (6, 'sequence', 6), (6, 'host_decisions', 9), (6, 'accepted_for_sequence', 2),
                       (6, 'host_glass', 1), (6, 'host_effects', 1), (7, 'cutoff', .265))
        for index, key, value in bad_changes:
            with self.subTest(index=index, key=key):
                records = parser_records(); records[index]['data'][key] = value
                with self.assertRaises(ValueError): self.parse_records(records)
        with self.assertRaises(ValueError): self.parse_records(parser_records(), token='previous-run')
        with self.assertRaises(ValueError): self.parse_records(parser_records(), outcome='Failed')
        for index in range(9):
            records = parser_records(); del records[index]
            with self.subTest(missing_record=index), self.assertRaises(ValueError): self.parse_records(records)
        records = parser_records(); records[-1]['data']['observer_detached'] = False
        with self.assertRaises(ValueError): self.parse_records(records)

    def test_missing_precondition_rejects_otherwise_passed_recovery(self):
        for index in (0, 2, 5):
            for key in sample():
                with self.subTest(stage=index, missing=key):
                    records = parser_records(); del records[index]['data'][key]
                    with self.assertRaisesRegex(ValueError, 'preconditions NOT_READY'): self.parse_records(records)

    def test_cannot_promote_fixture_or_change_identity_between_stages(self):
        for index in range(9):
            records = parser_records(); records[index]['evidence_level'] = 'native'
            with self.subTest(index=index), self.assertRaises(ValueError): self.parse_records(records)
        records = parser_records()
        for key in ('host_vehicle_id', 'guest_vehicle_id', 'host_bound_vehicle_id', 'guest_bound_vehicle_id'):
            records[5]['data'][key] = 10
        with self.assertRaises(ValueError): self.parse_records(records)
        records = parser_records(); records[0], records[1] = records[1], records[0]
        with self.assertRaises(ValueError): self.parse_records(records)

    def test_fresh_leaf_is_exclusive_and_aliases_are_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            rounds = Path(tmp) / 'rounds'; run = rounds / 'assigned'; run.mkdir(parents=True)
            (run / 'contract.json').write_text('{}')
            with patch.object(self.audit, 'ROUNDS', rounds):
                self.assertEqual(run / 'fresh', self.audit.new_output(run, 'fresh'))
                with self.assertRaises(FileExistsError): self.audit.new_output(run, 'fresh')
                for name in ('../escape', '.', ''):
                    with self.assertRaises(ValueError): self.audit.new_output(run, name)
                alias = rounds / 'alias'; alias.symlink_to(run, target_is_directory=True)
                with self.assertRaises(ValueError): self.audit.new_output(alias, 'not-created')
                with self.assertRaises(ValueError): self.audit.new_output(Path(tmp), 'not-created')
                self.assertFalse((run / 'not-created').exists())

    def test_native_limits_are_unconditionally_separate(self):
        limits = self.audit.native_limits()
        self.assertEqual('NOT_READY', limits['status'])
        self.assertEqual('BLOCKED', limits['protected_input']['status'])
        self.assertFalse(limits['native_executed'])
        self.assertIn('guest-spawn-readiness', [a['id'] for a in limits['missing_artifacts']])
        self.assertIn('independent', limits['next_requirement'].lower())
        self.assertTrue(all(value == 'NOT_TESTED' for value in limits['acceptance'].values()))


if __name__ == '__main__':
    unittest.main()
