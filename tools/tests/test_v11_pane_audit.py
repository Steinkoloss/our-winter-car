"""Portable audit-parser tests; input strings are NOT native evidence."""
import importlib.util
from pathlib import Path
import unittest
import tempfile
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('v11_pane_audit', Path(__file__).parents[1] / 'v11_pane_audit.py')
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)

class PaneAuditTests(unittest.TestCase):
    def test_boot_diagnostic_does_not_treat_awake_or_ready_as_command_response(self):
        old = '[01:11:00.000] Awake done\n'
        self.assertEqual('not entered', audit.boot_stage(old, 'TryReleaseHostLocalMutex'))
        entered = old + 'TryReleaseHostLocalMutex begin\nSingleInstanceUnlocker.Release begin\n'
        self.assertEqual('entered without return', audit.boot_stage(entered, 'SingleInstanceUnlocker.Release'))
        self.assertEqual('not entered', audit.boot_stage(entered, 'HostLocalReadySignal.MarkReady'))
        self.assertEqual('returned', audit.boot_stage(entered + 'SingleInstanceUnlocker.Release end result=False\n', 'SingleInstanceUnlocker.Release'))
        self.assertEqual('managed error', audit.boot_stage(entered + 'SingleInstanceUnlocker.Release error System.Exception: test\n', 'SingleInstanceUnlocker.Release'))

    def test_readiness_diagnostic_stops_before_any_scraper_fixture(self):
        with tempfile.TemporaryDirectory() as tmp, patch.object(audit, 'NativeRun') as run:
            native = run.return_value.__enter__.return_value
            native.output = Path(tmp)
            host = 'OK|1\nsession|Hosting|1|0\n'
            pose = 'pose-remote|1|3|20|0.08|False|0|(1, 2, 3)\n'
            with patch.object(audit, 'command', return_value=host) as command, \
                    patch.object(audit, 'await_guest_pose_ready', return_value='ready') as resume, \
                    patch.object(audit, 'snapshot_until', side_effect=['Connected', pose]) as snapshot, \
                    patch.object(audit, 'ready_signal', return_value=True):
                audit.readiness('portable-double')
            self.assertEqual([('host',), ('guest',)], [c.args for c in native.launch.call_args_list])
            self.assertEqual([('host', 'pane-snapshot'), ('guest', 'pane-snapshot')], [c.args for c in command.call_args_list])
            resume.assert_called_once()
            self.assertEqual(('host', audit.fresh_guest_pose), snapshot.call_args.args)
            receipt = (Path(tmp) / 'readiness.json').read_text()
            self.assertIn('"fresh_host_observed_guest_pose": true', receipt)
            self.assertIn('"scraper_gameplay_tested": false', receipt)

    def test_effect_assertion_requires_guest_only_native_effect_and_one_host_delta(self):
        host = 'pane-observed|1|0|0\n'
        guest = 'pane-observed|0|1|0.002\n'
        audit.check_actor_effects(host, guest)
        for h, g in ((host.replace('|1|0|', '|1|1|'), guest), (host, guest.replace('|0|1|', '|0|0|')),
                     (host, guest.replace('0.002', '0')), (host.replace('|1|0|', '|2|0|'), guest)):
            with self.assertRaises(AssertionError): audit.check_actor_effects(h, g)

    def test_spawn_wait_does_not_confuse_no_offer_yet_with_ready(self):
        pending = 'session|Connected|1|1\noffer|none\npose-local|False|False|False|0|AwaitingOffer|False\n'
        choosing = pending.replace('offer|none', 'offer|23|saved').replace('AwaitingOffer', 'Choosing')
        ready = pending.replace('pose-local|False', 'pose-local|True').replace('AwaitingOffer', 'Ready')
        with patch.object(audit, 'command', side_effect=[pending, choosing, ready, ready]) as command, patch.object(audit.time, 'sleep'):
            self.assertEqual(ready, audit.await_guest_pose_ready())
        self.assertIn((('guest', 'spawn', 'host'),), [call.args and (call.args,) for call in command.call_args_list])

    def test_host_pose_requires_live_fresh_matching_actor(self):
        good = 'pose-remote|1|3|20|0.08|False|0|(1, 2, 3)\n'
        self.assertTrue(audit.fresh_guest_pose(good))
        for bad in (good.replace('|1|3|', '|0|3|'), good.replace('|20|', '|0|'),
                    good.replace('|0.08|', '|0.61|'), good.replace('|0.08|', '|-0.1|'),
                    good.replace('|0.08|', '|NaN|'), good.replace('|False|', '|True|'), ''):
            self.assertFalse(audit.fresh_guest_pose(bad))

    def test_connected_result_requires_exact_absolute_float_not_climate_quantization(self):
        audit.check_bridge(.25, .255, .255)
        for values in ((.25, .25, .25), (.25, .255, .254901975), (.25, .26, .26)):
            with self.assertRaises(AssertionError): audit.check_bridge(*values)

    def test_bridge_manifest_includes_production_wire_catalog_and_native_adapter(self):
        paths = audit.bridge_sources()
        for required in ('src/WinterMP.Core/Sync/PaneScrapeSync.cs', 'src/WinterMP.Core/Sync/PaneScrapeSync.Bindings.cs',
                         'src/WinterMP.Net/Messages/PaneScrapeMessages.cs', 'src/WinterMP.Net/Sync/ScraperLease.cs', 'catalog/sync-catalog.json'):
            self.assertIn(required, paths)

    def test_exact_one_pane_required(self):
        line = 'pane|CORRIS/BODY/Windshield/collider|CutoffWindshield|0.25\n'
        self.assertEqual(.25, audit.pane_value(line))
        for bad in ('', line + line, line.replace('Windshield/collider', 'Rear/collider'), line.replace('0.25', 'NaN')):
            with self.assertRaises(ValueError): audit.pane_value(bad)

    def test_negative_guest_evidence_requires_transient_native_change_and_host_reconciliation(self):
        audit.check_denied(.25, .25098, .25598, .25, .25098)
        for values in ((.25, .25098, .25098, .25, .25098), (.25, .25098, .25598, .255, .2549), (.25, .25098, .25598, .25, .25598)):
            with self.assertRaises(AssertionError): audit.check_denied(*values)

    def test_authorized_stroke_must_change_both_native_and_replica(self):
        audit.check_authorized(.25, .255, .254902)
        for values in ((.25, .25, .25), (.25, .255, .25)):
            with self.assertRaises(AssertionError): audit.check_authorized(*values)

    def test_explicit_assigned_run_routes_new_output_and_marker(self):
        with tempfile.TemporaryDirectory() as tmp:
            auto = Path(tmp)
            run = auto / 'rounds/000005-work'
            run.mkdir(parents=True)
            (run / 'contract.json').write_text('{}')
            with patch.object(audit, 'AUTO', auto), patch.object(audit, 'ROUND', None), patch.object(audit, 'MARKER', None):
                audit.configure_run(run)
                self.assertEqual(run, audit.ROUND)
                self.assertIn('000005-work', audit.MARKER)
                native = audit.NativeRun('new-evidence')
                self.assertEqual(run / 'new-evidence', native.output)
                with self.assertRaises(FileExistsError): audit.NativeRun('new-evidence')

    def test_run_required_and_must_be_a_real_contract_directory(self):
        with tempfile.TemporaryDirectory() as tmp:
            auto = Path(tmp)
            with patch.object(audit, 'AUTO', auto), patch.object(audit, 'ROUND', None):
                with self.assertRaises(ValueError): audit.NativeRun('no-run')
                for bad in (auto, auto / 'rounds/missing', Path('/tmp')):
                    with self.assertRaises(ValueError): audit.configure_run(bad)
                real = auto / 'rounds/000005-work'
                real.mkdir(parents=True)
                (real / 'contract.json').write_text('{}')
                alias = auto / 'rounds/alias'
                alias.symlink_to(real, target_is_directory=True)
                with self.assertRaises(ValueError): audit.configure_run(alias)

    def test_json_artifacts_do_not_overwrite_prior_evidence_by_default(self):
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp) / 'receipt.json'
            audit.save_json(target, {'first': 1})
            before = target.read_bytes()
            with self.assertRaises(FileExistsError): audit.save_json(target, {'second': 2})
            self.assertEqual(before, target.read_bytes())

    def test_command_log_refuses_existing_name_before_subprocess(self):
        with tempfile.TemporaryDirectory() as tmp, patch.object(audit.subprocess, 'run') as execute:
            run = Path(tmp)
            (run / 'previous.log').write_text('keep')
            with patch.object(audit, 'ROUND', run):
                with self.assertRaises(FileExistsError): audit.run(['unused'], 'previous')
            execute.assert_not_called()
            self.assertEqual('keep', (run / 'previous.log').read_text())

    def test_command_bus_refuses_another_round_before_writing(self):
        with tempfile.TemporaryDirectory() as tmp:
            rig = Path(tmp)
            (rig / 'v11-owner.txt').write_text('previous owner')
            with patch.object(audit, 'ROUND', rig), patch.object(audit, 'RIG', rig), patch.object(audit, 'GAME', rig / 'game'), patch.object(audit, 'MARKER', 'current owner'):
                with self.assertRaises(AssertionError): audit.command('guest', 'pane-scrape')
            self.assertFalse((rig / 'game').exists())
            self.assertEqual('previous owner', (rig / 'v11-owner.txt').read_text())

    def test_prepare_claims_only_copied_prefixes_and_preserves_first_receipt(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            rig = root / 'test-rig'
            game = rig / 'game'
            (game / 'WinterMP').mkdir(parents=True)
            (rig / 'ready.txt').write_text('isolated test double; never a native launch')
            run = root / 'rounds/000005-work'
            run.mkdir(parents=True)
            for profile in ('compatdata', 'guest-compatdata'):
                (rig / profile / 'pfx/drive_c/users/steamuser/AppData/LocalLow/Amistech/My Winter Car').mkdir(parents=True)
            with patch.object(audit, 'ROUND', run), patch.object(audit, 'RIG', rig), patch.object(audit, 'GAME', game), patch.object(audit, 'MARKER', 'current run'), patch.object(audit, 'protected', return_value={'test-double': 'not native evidence'}):
                audit.prepare()
                receipt = (run / 'protected-before.json').read_bytes()
                self.assertEqual('current run', (rig / 'v11-owner.txt').read_text())
                with self.assertRaises(AssertionError): audit.prepare()
                self.assertEqual(receipt, (run / 'protected-before.json').read_bytes())

    def test_missing_current_payload_fails_before_markers_and_releases_locks(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            rig = root / 'test-rig'
            game = rig / 'game'
            (game / 'WinterMP').mkdir(parents=True)
            (rig / 'v11-owner.txt').write_text('this run')
            with patch.multiple(audit, ROUND=root, RIG=rig, GAME=game, SOURCE=root / 'missing-source', MARKER='this run'):
                native = audit.NativeRun('preflight')
                with self.assertRaisesRegex(AssertionError, 'Build current payload'): native.__enter__()
                self.assertTrue(native.lock.closed)
                self.assertTrue(native.game_lock.closed)
                self.assertEqual([], native.processes)
                self.assertFalse((game / 'wintermp-live-bag-sandbox.txt').exists())
                self.assertTrue((native.output / 'entry-error.json').is_file())

    def test_entry_failure_removes_only_its_new_markers(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            rig = root / 'test-rig'
            game = rig / 'game'
            (game / 'WinterMP').mkdir(parents=True)
            owner = rig / 'v11-owner.txt'
            owner.write_text('this run')
            marker = game / 'wintermp-v11-pane-sandbox.txt'
            with patch.multiple(audit, ROUND=root, RIG=rig, GAME=game, MARKER='this run'):
                native = audit.NativeRun('entry-failure')
                def fail_after_marker():
                    marker.write_text('this run')
                    native.new_markers.append(marker)
                    raise OSError('test-double failure, no game launched')
                with patch.object(native, 'prepare_locked', side_effect=fail_after_marker):
                    with self.assertRaises(OSError): native.__enter__()
                self.assertFalse(marker.exists())
                self.assertEqual('this run', owner.read_text())
                self.assertTrue(native.lock.closed)
                self.assertTrue(native.game_lock.closed)

if __name__ == '__main__': unittest.main()
