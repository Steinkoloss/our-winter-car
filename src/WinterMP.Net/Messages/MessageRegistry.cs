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
                { MessageId.GuestSpawn, () => new GuestSpawn() },
                { MessageId.PlayerNeedsReport, () => new PlayerNeedsReport() },
                { MessageId.SleepConsentRequest, () => new SleepConsentRequest() },
                { MessageId.SleepConsentResponse, () => new SleepConsentResponse() },
                { MessageId.SleepConsentResult, () => new SleepConsentResult() },
                { MessageId.PlayerDeathReport, () => new PlayerDeathReport() },
                { MessageId.PlayerDeathEvent, () => new PlayerDeathEvent() },
                { MessageId.PlayerRespawn, () => new PlayerRespawn() },
                { MessageId.PlayerClothingState, () => new PlayerClothingState() },
                { MessageId.NpcTransform, () => new NpcTransform() },
                { MessageId.FsmStateEnter, () => new FsmStateEnter() },
                { MessageId.FsmRawEvent, () => new FsmRawEvent() },
                { MessageId.ItemTransform, () => new ItemTransform() },
                { MessageId.TimeSync, () => new TimeSync() },
                { MessageId.BoltState, () => new BoltState() },
                { MessageId.PartState, () => new PartState() },
                { MessageId.ItemDespawn, () => new ItemDespawn() },
                { MessageId.WorldStateChecksum, () => new WorldStateChecksum() },
                { MessageId.WorldResyncRequest, () => new WorldResyncRequest() },
                { MessageId.WorldObjectStateRequest, () => new WorldObjectStateRequest() },
                { MessageId.HeatSourceState, () => new HeatSourceState() },
                { MessageId.HeatSourceIntent, () => new HeatSourceIntent() },
                { MessageId.ItemSpawn, () => new ItemSpawn() },
                { MessageId.SpawnIntent, () => new SpawnIntent() },
                { MessageId.VehicleState, () => new VehicleState() },
                { MessageId.VehicleClimate, () => new VehicleClimate() },
                { MessageId.VehicleCargo, () => new VehicleCargo() },
                { MessageId.WalletState, () => new WalletState() },
                { MessageId.PurchaseIntent, () => new PurchaseIntent() },
                { MessageId.WorldSnapshotRequest, () => new WorldSnapshotRequest() },
                { MessageId.WorldDoorSnapshot, () => new WorldDoorSnapshot() },
                { MessageId.WorldItemSnapshot, () => new WorldItemSnapshot() },
                { MessageId.WorldBoltSnapshot, () => new WorldBoltSnapshot() },
                { MessageId.WorldPartSnapshot, () => new WorldPartSnapshot() },
                { MessageId.WorldItemDespawnSnapshot, () => new WorldItemDespawnSnapshot() },
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
