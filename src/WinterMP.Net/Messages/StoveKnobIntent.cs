namespace WinterMP.Net.Messages
{
    public sealed class StoveKnobIntent : IMessage
    {
        public uint ApplianceId;
        public byte PlayerId, Plate, Direction;
        public ushort Sequence;
        public MessageId Id => MessageId.StoveKnobIntent;
        public void Write(NetWriter writer)
        {
            if (!Sync.StovePolicy.Valid(this)) throw new ProtocolException("Invalid stove knob intent.");
            writer.WriteUInt32(ApplianceId); writer.WriteByte(PlayerId); writer.WriteByte(Plate);
            writer.WriteByte(Direction); writer.WriteUInt16(Sequence);
        }
        public void Read(NetReader reader)
        {
            ApplianceId = reader.ReadUInt32(); PlayerId = reader.ReadByte(); Plate = reader.ReadByte();
            Direction = reader.ReadByte(); Sequence = reader.ReadUInt16();
            if (!Sync.StovePolicy.Valid(this)) throw new ProtocolException("Invalid stove knob intent.");
        }
    }
}
