namespace WinterMP.Net.Messages
{
    public sealed class StarterWearRequest : IMessage
    {
        public const float MaximumSeconds = 1f;
        public MessageId Id => MessageId.StarterWearRequest;
        public uint VehicleId;
        public byte PlayerId;
        public ushort Sequence;
        public float Seconds;
        public bool Valid => VehicleId != 0 && PlayerId != 0 && PlayerId != byte.MaxValue
            && Seconds > 0 && Seconds <= MaximumSeconds && !float.IsNaN(Seconds);
        public void Write(NetWriter writer)
        {
            if (!Valid) throw new ProtocolException("Invalid starter wear request.");
            writer.WriteUInt32(VehicleId); writer.WriteByte(PlayerId); writer.WriteUInt16(Sequence); writer.WriteSingle(Seconds);
        }
        public void Read(NetReader reader)
        {
            VehicleId = reader.ReadUInt32(); PlayerId = reader.ReadByte(); Sequence = reader.ReadUInt16(); Seconds = reader.ReadSingle();
            if (!Valid) throw new ProtocolException("Invalid starter wear request.");
        }
    }
}
