namespace WinterMP.Net.Messages
{
    /// <summary>One native automatic shift-state oil-use callback; the host supplies the amount.</summary>
    public sealed class GearboxOilUseRequest : IMessage
    {
        public MessageId Id => MessageId.GearboxOilUseRequest;
        public uint VehicleId;
        public byte PlayerId, Phase;
        public ushort Sequence;
        public bool Valid => VehicleId != 0 && PlayerId != 0 && PlayerId != byte.MaxValue && (Phase == 1 || Phase == 3);
        public void Write(NetWriter writer)
        {
            if (!Valid) throw new ProtocolException("Invalid gearbox oil-use request.");
            writer.WriteUInt32(VehicleId); writer.WriteByte(PlayerId); writer.WriteUInt16(Sequence); writer.WriteByte(Phase);
        }
        public void Read(NetReader reader)
        {
            VehicleId = reader.ReadUInt32(); PlayerId = reader.ReadByte(); Sequence = reader.ReadUInt16(); Phase = reader.ReadByte();
            if (!Valid) throw new ProtocolException("Invalid gearbox oil-use request.");
        }
    }
}
