namespace WinterMP.Net.Messages
{
    /// <summary>One native damaged-gearbox kick-out callback; only the host subtracts saved wear.</summary>
    public sealed class GearboxWearRequest : IMessage
    {
        public MessageId Id => MessageId.GearboxWearRequest;
        public uint VehicleId;
        public byte PlayerId;
        public ushort Sequence;
        public bool Valid => VehicleId != 0 && PlayerId != 0 && PlayerId != byte.MaxValue;
        public void Write(NetWriter writer)
        {
            if (!Valid) throw new ProtocolException("Invalid gearbox wear request.");
            writer.WriteUInt32(VehicleId); writer.WriteByte(PlayerId); writer.WriteUInt16(Sequence);
        }
        public void Read(NetReader reader)
        {
            VehicleId = reader.ReadUInt32(); PlayerId = reader.ReadByte(); Sequence = reader.ReadUInt16();
            if (!Valid) throw new ProtocolException("Invalid gearbox wear request.");
        }
    }
}
