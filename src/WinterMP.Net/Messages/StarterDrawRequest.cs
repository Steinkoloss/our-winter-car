namespace WinterMP.Net.Messages
{
    public sealed class StarterDrawRequest : IMessage
    {
        public const byte Loaded = 1, Unloaded = 2;
        public const ushort MaximumCount = 512;
        public MessageId Id => MessageId.StarterDrawRequest;
        public uint VehicleId;
        public byte PlayerId, Kind;
        public ushort Sequence, Count;
        public bool Valid => VehicleId != 0 && PlayerId != 0 && PlayerId != byte.MaxValue
            && (Kind == Loaded || Kind == Unloaded) && Count > 0 && Count <= MaximumCount;
        public void Write(NetWriter writer)
        {
            if (!Valid) throw new ProtocolException("Invalid starter draw request.");
            writer.WriteUInt32(VehicleId); writer.WriteByte(PlayerId); writer.WriteUInt16(Sequence);
            writer.WriteByte(Kind); writer.WriteUInt16(Count);
        }
        public void Read(NetReader reader)
        {
            VehicleId = reader.ReadUInt32(); PlayerId = reader.ReadByte(); Sequence = reader.ReadUInt16();
            Kind = reader.ReadByte(); Count = reader.ReadUInt16();
            if (!Valid) throw new ProtocolException("Invalid starter draw request.");
        }
    }
}
