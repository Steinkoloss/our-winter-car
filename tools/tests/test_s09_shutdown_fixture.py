"""Portable lifecycle extraction integrity; no native gameplay or Steam assertion."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

SOURCE = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('shutdown_fixture', SOURCE / 'tools/TrainSend.Tests/shutdown_fixture.py')
FIXTURE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(FIXTURE)


class ShutdownFixtureTests(unittest.TestCase):
    def test_compiles_each_complete_production_lifecycle_body_without_rewrite(self):
        production = FIXTURE.read('Session/SessionManager.cs')
        with tempfile.TemporaryDirectory() as root:
            FIXTURE.generate(Path(root))
            generated = (Path(root) / 'Shutdown.g.cs').read_text()
        for signature in FIXTURE.SIGNATURES:
            self.assertEqual(FIXTURE.extract(production, signature), FIXTURE.extract(generated, signature))
            self.assertEqual(1, generated.count(signature))
        for path, signature in FIXTURE.AUX_METHODS:
            self.assertEqual(FIXTURE.extract(FIXTURE.read(path), signature), FIXTURE.extract(generated, signature))
            self.assertEqual(1, generated.count(signature))
        self.assertIn(FIXTURE.extract(FIXTURE.read('LaunchOptions.cs'), 'public enum LaunchMode'), generated)
        # Keep Steam cleanup source present even though portable compilation does
        # not define STEAMWORKS and cannot prove actual lobby departure.
        self.assertIn('#if STEAMWORKS', generated)
        self.assertIn('Steam.SteamLobbyManager.LeaveLobby();', generated)
        for field in FIXTURE.FIELDS:
            self.assertIn(field, generated)

    def test_doubles_do_not_replace_selected_methods_or_production_runtime_types(self):
        doubles = (SOURCE / 'tools/TrainSend.Tests/ShutdownDoubles.cs').read_text()
        for signature in FIXTURE.SIGNATURES:
            self.assertNotIn(signature, doubles)
        for _, signature in FIXTURE.AUX_METHODS:
            self.assertNotIn(signature, doubles)
        for name in ('JoinAttempt', 'PassengerSeatLedger', 'DeathSessionPolicy', 'ConnectionQuality'):
            self.assertNotIn('class ' + name, doubles)
        project = (SOURCE / 'tools/TrainSend.Tests/TrainSend.Tests.csproj').read_text()
        self.assertIn('/shutdown_fixture.py', project)
        self.assertIn('Session/ConnectionQuality.cs', project)

    def test_clothing_reset_fields_are_extracted_from_current_production_partial(self):
        clothing = FIXTURE.read('Session/SessionManager.Clothing.cs')
        with tempfile.TemporaryDirectory() as root:
            FIXTURE.generate(Path(root))
            generated = (Path(root) / 'Shutdown.g.cs').read_text()
        for name in FIXTURE.CLOTHING_FIELDS:
            declaration = FIXTURE.field_declaration(clothing, name)
            self.assertEqual(1, generated.count(declaration))
        doubles = (SOURCE / 'tools/TrainSend.Tests/ShutdownDoubles.cs').read_text()
        self.assertIn('ResetClothingSession()', doubles)

    def test_missing_or_duplicate_field_fails_closed(self):
        declaration = '        internal ulong LocalClothingAdmission { get; private set; }'
        for invalid in ('', declaration + '\n' + declaration):
            with self.assertRaises(AssertionError):
                FIXTURE.field_declaration(invalid, 'LocalClothingAdmission')

    def test_field_lookup_does_not_match_internal_expression_body(self):
        declaration = '        public SessionState State { get; private set; }'
        method = '        internal bool IsHosting() => State == SessionState.Hosting;'
        self.assertEqual(declaration, FIXTURE.field_declaration(declaration + '\n' + method, 'State'))

    def test_shutdown_contains_cleanup_in_finally_without_catching_or_retrying_send(self):
        body = FIXTURE.extract(FIXTURE.read('Session/SessionManager.cs'), 'public void Shutdown(')
        self.assertIn('try\n', body)
        self.assertNotIn('catch', body)
        self.assertNotIn('while (', body)
        self.assertEqual(1, body.count('SendTo('))
        self.assertLess(body.index('SendTo('), body.index('finally'))
        cleanup = ['_failedSessionCleanupPending = false;', 'DisposeSessionTransport();',
                   'ResetSessionRuntimeState(disconnectFailed, finishShutdown: true);']
        previous = body.index('finally')
        for call in cleanup:
            self.assertEqual(1, body.count(call))
            self.assertGreater(body.index(call), previous)
            previous = body.index(call)

    def test_disposal_detaches_both_references_before_independent_logged_boundaries(self):
        body = FIXTURE.extract(FIXTURE.read('Session/SessionManager.cs'), 'private void DisposeSessionTransport(')
        self.assertEqual(2, body.count('catch (Exception e)'))
        for reference in ('_devClient', '_transport'):
            self.assertLess(body.index(reference + ' = null;'), body.index('try\n'))
        for target, label in (('devClient', 'dev client'), ('transport', 'transport')):
            self.assertEqual(1, body.count(target + '?.Dispose();'))
            self.assertIn('try\n            {\n                ' + target + '?.Dispose();\n            }', body)
            self.assertIn('LogError("Session ' + label + ' dispose failed: " + e)', body)
        self.assertNotIn('throw', body)
        self.assertNotIn('SendTo(', body)
        self.assertNotIn('ResetSessionRuntimeState(', body)
        self.assertGreater(body.index('#if STEAMWORKS'), body.rindex('catch (Exception e)'))

    def test_deferred_cleanup_retains_failure_policy_and_has_no_send_or_catch(self):
        body = FIXTURE.extract(FIXTURE.read('Session/SessionManager.cs'), 'private void FlushFailedSessionCleanup(')
        for expected in ('if (!_failedSessionCleanupPending) return;',
                         'bool returnToBrowser = !IsHost && _joinBrowseActive;',
                         '_joinBrowseActive = returnToBrowser;',
                         'SetState(SessionState.Idle, PendingLaunchStatus(_pendingMode));'):
            self.assertIn(expected, body)
        self.assertEqual(1, body.count('DisposeSessionTransport();'))
        self.assertEqual(1, body.count('ResetSessionRuntimeState();'))
        self.assertNotIn('catch', body)
        self.assertNotIn('SendTo(', body)

    def test_only_pane_callback_is_caught_and_other_callers_keep_cleanup_exception(self):
        body = FIXTURE.extract(FIXTURE.read('Session/SessionManager.cs'), 'private void ResetSessionRuntimeState(')
        self.assertIn('bool disconnectFailed = false', body)
        self.assertEqual(1, body.count('catch (Exception e)'))
        caught = body[body.index('try'):body.index('catch (Exception e)')]
        self.assertIn('Sync.PaneScrapeSync.Instance?.ResetSession();', caught)
        self.assertNotIn('Sync.PlayerSyncManager', caught)
        self.assertIn('if (!disconnectFailed) throw;', body)
        self.assertIn('LogError("Session PaneScrape reset failed: " + e)', body)
        remaining = body[body.index('finally'):]
        self.assertIn('Sync.PlayerSyncManager.Instance?.ResetGuestSpawn();', remaining)
        self.assertIn('_playersByPeer.Clear();', remaining)
        self.assertIn('Sync.DeathSyncManager.Instance?.ResetSession();', remaining)
        self.assertIn('NetTrafficMeter.Instance.Reset();', remaining)
        self.assertIn('bool finishShutdown = false', body)
        self.assertEqual(1, remaining.count('SetState(SessionState.Idle, "Idle");'))
        self.assertGreater(remaining.index('if (finishShutdown) SetState'), remaining.index('NetTrafficMeter.Instance.Reset();'))


if __name__ == '__main__':
    unittest.main()
