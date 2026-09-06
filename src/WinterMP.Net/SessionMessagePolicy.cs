using WinterMP.Net.Messages;

namespace WinterMP.Net
{
    /// <summary>Admission rules shared by session transports before a decoded message reaches game state.</summary>
    public static class SessionMessagePolicy
    {
        /// <summary>
        /// Hosts may receive an initial handshake from a transport-connected peer;
        /// every other message requires the peer's completed handshake. Before a
        /// guest accepts its handshake response, its selected host may not mutate
        /// local world state.
        /// </summary>
        public static bool IsSenderAllowed(MessageId messageId, bool receiverIsHost,
            bool senderIsAuthenticated, bool senderIsSelectedHost, bool receiverHandshakeComplete)
        {
            if (receiverIsHost)
                return messageId == MessageId.HandshakeRequest || senderIsAuthenticated;

            if (!senderIsSelectedHost) return false;
            return receiverHandshakeComplete || messageId == MessageId.HandshakeResponse;
        }

        /// <summary>Only the three transport channels defined by the protocol are valid.</summary>
        public static bool IsKnownChannel(Channel channel)
        {
            return channel == Channel.ReliableOrdered
                || channel == Channel.UnreliableSequenced
                || channel == Channel.ReliableBulk;
        }

        /// <summary>
        /// Channel contract for the current wire messages. Live transform streams may
        /// also use reliable ordered for their final/snapshot state; all other current
        /// messages are ordered events. ReliableBulk remains reserved until a message
        /// explicitly opts into it.
        /// </summary>
        public static bool IsChannelAllowed(MessageId messageId, Channel channel)
        {
            if (!IsKnownChannel(channel)) return false;

            switch (messageId)
            {
                case MessageId.PlayerTransform:
                    return channel == Channel.UnreliableSequenced;

                case MessageId.ItemTransform:
                case MessageId.VehicleState:
                case MessageId.VehicleClimate:
                case MessageId.VehicleCargo:
                case MessageId.NpcTransform:
                case MessageId.VenttiSceneState:
                    return channel == Channel.ReliableOrdered || channel == Channel.UnreliableSequenced;

                default:
                    return channel == Channel.ReliableOrdered;
            }
        }

        /// <summary>Soft-resync requests must name at least one defined state group.</summary>
        public static bool IsValidResyncFlags(byte flags)
        {
            return flags != 0 && (flags & ~WorldResyncRequest.AllFlags) == 0;
        }
    }
}
