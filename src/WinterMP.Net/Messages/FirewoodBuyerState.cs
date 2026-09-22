namespace WinterMP.Net.Messages
{
    public sealed class FirewoodBuyerState : IMessage
    {
        public const byte BuyerPresent = 1, OfferReady = 2;
        public uint NetId, Revision;
        public byte Flags;
        public float Amount;
        public NetVector3 Position;
        public NetQuaternion Rotation = NetQuaternion.Identity;
        public MessageId Id => MessageId.FirewoodBuyerState;
        public void Write(NetWriter writer)
        {
            if (!Sync.FirewoodBuyerPolicy.Valid(this)) throw new ProtocolException("Invalid firewood buyer state.");
            writer.WriteUInt32(NetId); writer.WriteUInt32(Revision); writer.WriteByte(Flags); writer.WriteSingle(Amount);
            writer.WriteVector3(Position); writer.WriteQuaternion(Rotation);
        }
        public void Read(NetReader reader)
        {
            NetId = reader.ReadUInt32(); Revision = reader.ReadUInt32(); Flags = reader.ReadByte(); Amount = reader.ReadSingle();
            Position = reader.ReadVector3(); Rotation = reader.ReadQuaternion();
            if (!Sync.FirewoodBuyerPolicy.Valid(this)) throw new ProtocolException("Invalid firewood buyer state.");
        }
    }
}
