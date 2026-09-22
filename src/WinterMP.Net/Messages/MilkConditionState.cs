namespace WinterMP.Net.Messages
{
    public sealed class MilkConditionState : IMessage
    {
        public uint NetId, Revision;
        public float Condition;
        public byte Spoiled;
        public MessageId Id => MessageId.MilkConditionState;
        public void Write(NetWriter writer)
        {
            if (!Sync.MilkConditionPolicy.Valid(this)) throw new ProtocolException("Invalid milk condition.");
            writer.WriteUInt32(NetId); writer.WriteUInt32(Revision); writer.WriteSingle(Condition); writer.WriteByte(Spoiled);
        }
        public void Read(NetReader reader)
        {
            NetId = reader.ReadUInt32(); Revision = reader.ReadUInt32(); Condition = reader.ReadSingle(); Spoiled = reader.ReadByte();
            if (!Sync.MilkConditionPolicy.Valid(this)) throw new ProtocolException("Invalid milk condition.");
        }
    }
}
