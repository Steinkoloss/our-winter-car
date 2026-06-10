using System;
using System.Collections.Generic;

namespace WinterMP.Net.Messages
{
    public static class MessageRegistry
    {
        private static readonly Dictionary<MessageId, Func<IMessage>> Factories =
            new Dictionary<MessageId, Func<IMessage>>
            {
                { MessageId.HandshakeRequest, () => new HandshakeRequest() },
                { MessageId.HandshakeResponse, () => new HandshakeResponse() },
                { MessageId.Ping, () => new PingMessage() },
                { MessageId.Pong, () => new PongMessage() },
                { MessageId.Disconnect, () => new DisconnectMessage() },
                { MessageId.Chat, () => new ChatMessage() },
                { MessageId.PlayerSpawn, () => new PlayerSpawn() },
                { MessageId.PlayerDespawn, () => new PlayerDespawn() },
                { MessageId.PlayerTransform, () => new PlayerTransform() },
                { MessageId.PassengerState, () => new PassengerState() },
                { MessageId.FsmStateEnter, () => new FsmStateEnter() },
                { MessageId.FsmRawEvent, () => new FsmRawEvent() },
                { MessageId.ItemTransform, () => new ItemTransform() },
                { MessageId.TimeSync, () => new TimeSync() },
                { MessageId.WorldSnapshotRequest, () => new WorldSnapshotRequest() },
                { MessageId.WorldDoorSnapshot, () => new WorldDoorSnapshot() },
                { MessageId.WorldItemSnapshot, () => new WorldItemSnapshot() },
            };

        public static IEnumerable<MessageId> KnownIds => Factories.Keys;

        public static IMessage Create(MessageId id)
        {
            if (Factories.TryGetValue(id, out var factory))
                return factory();
            throw new ProtocolException($"Unknown message id {(ushort)id}.");
        }
    }
}
