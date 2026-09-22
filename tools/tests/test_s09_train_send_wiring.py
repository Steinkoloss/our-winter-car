"""Static request boundary/reset audit; not native transport or teardown evidence."""
from pathlib import Path
import unittest
from test_s09_callback_wiring import method

SOURCE = Path(__file__).resolve().parents[2]


class TrainSendWiringTests(unittest.TestCase):
    def test_all_train_snapshot_request_handlers_contain_send_inside_iteration(self):
        text = (SOURCE / 'src/WinterMP.Core/Session/SessionManager.Handlers.cs').read_text()
        for signature, iterator, send in (
            ('private void HandleSnapshotRequest(', 'world.BuildWorldSnapshot()', 'peer, chunk, Channel.ReliableOrdered'),
            ('private void HandleResyncRequest(', 'world.BuildResyncMessages(request.Flags)', 'peer, chunk, Channel.ReliableOrdered'),
            ('private void HandleObjectStateRequest(', 'world.BuildObjectStateMessages(request.NetId)', 'peer, message, channel'),
        ):
            body = method(text, signature)
            self.assertEqual(1, body.count('TrySendSnapshotMessage('))
            self.assertIn('if (TrySendSnapshotMessage(' + send + ')) messages++;', body)
            self.assertLess(body.index('TryBeginHostRequest('), body.index(iterator))
            self.assertLess(body.index(iterator), body.index('TrySendSnapshotMessage('))
        body = method(text, 'private void HandleObjectStateRequest(')
        self.assertIn('ItemTransformPolicy.SelectSendChannel(t.IsFinal, t.IsVehicle)', body)

    def test_helper_is_stateless_single_attempt_train_only_and_preserves_exception(self):
        text = (SOURCE / 'src/WinterMP.Core/Session/SessionManager.Snapshots.cs').read_text()
        body = method(text, 'private bool TrySendSnapshotMessage(')
        self.assertEqual(1, body.count('SendTo(peer, message, channel);'))
        self.assertIn('if (!(message is TrainState)) throw;', body)
        self.assertIn('on channel {(byte)channel}: {e}', body)
        self.assertLess(body.index('SendTo('), body.index('return true;'))
        self.assertLess(body.index('LogError('), body.index('return false;'))
        for forbidden in ('HandleSyncError(', 'Queue<', 'Dictionary<', 'Action<', 'while (', 'foreach (', '.Clear(', 'Task.', 'IsHost', 'e.Message'):
            self.assertNotIn(forbidden, text)

    def test_existing_session_reset_clears_each_request_cooldown(self):
        text = (SOURCE / 'src/WinterMP.Core/Session/SessionManager.cs').read_text()
        body = method(text, 'private void ResetSessionRuntimeState(')
        for name in ('Snapshot', 'Resync', 'ObjectState'):
            self.assertIn('_next' + name + 'RequestAt.Clear();', body)
        # Preserve the real send ordering: encode/reset writer, payload, attempted
        # traffic accounting, then exactly one transport call.
        body = method(text, 'private void SendTo(')
        ordered = ['if (_transport == null) return;', 'PacketCodec.Encode(message, _sendWriter);',
                   '_sendWriter.ToArray()', 'RecordSent(channel, payload.Length)', '_transport.Send(peer, payload, channel);']
        self.assertEqual(sorted(body.index(x) for x in ordered), [body.index(x) for x in ordered])
        self.assertNotIn('catch', body)


if __name__ == '__main__':
    unittest.main()
