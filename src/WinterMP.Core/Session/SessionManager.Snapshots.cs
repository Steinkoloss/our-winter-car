using System;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Transport;

namespace WinterMP.Core.Session
{
    public sealed partial class SessionManager
    {
        private bool TrySendSnapshotMessage(PeerId peer, IMessage message, Channel channel)
        {
            try
            {
                SendTo(peer, message, channel);
                return true;
            }
            catch (Exception e)
            {
                // A train result must not strand later snapshot chunks. Other failures
                // retain the caller's fallback; a send may have delivered before throwing,
                // so never retry it or retain it for a later request.
                if (!(message is TrainState)) throw;
                WinterMPPlugin.Log.LogError($"Error sending TrainState to {peer} on channel {(byte)channel}: {e}");
                return false;
            }
        }
    }
}
