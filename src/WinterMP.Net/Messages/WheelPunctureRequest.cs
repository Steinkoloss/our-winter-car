namespace WinterMP.Net.Messages
{
    /// <summary>A native driver puncture; only the host records tyre damage.</summary>
    public sealed class WheelPunctureRequest : IMessage
    {
        public uint VehicleId, Epoch;
        public byte PlayerId, Wheel;
        public ushort Sequence;
        public MessageId Id => MessageId.WheelPunctureRequest;
        public bool Valid => VehicleId != 0 && PlayerId != 0 && PlayerId != 255 && Wheel < 4 && Epoch != 0;
        public bool Matches(VehicleWheelHealthState state) => Valid && state != null && state.Valid && state.VehicleId == VehicleId
            && state.HasWheel(Wheel) && state.Epoch(Wheel) == Epoch && state.Health(Wheel) > 0;
        public void Write(NetWriter w)
        {
            if (!Valid) throw new ProtocolException("Invalid wheel puncture request.");
            w.WriteUInt32(VehicleId); w.WriteByte(PlayerId); w.WriteByte(Wheel); w.WriteUInt16(Sequence); w.WriteUInt32(Epoch);
        }
        public void Read(NetReader r)
        {
            VehicleId = r.ReadUInt32(); PlayerId = r.ReadByte(); Wheel = r.ReadByte(); Sequence = r.ReadUInt16(); Epoch = r.ReadUInt32();
            if (!Valid) throw new ProtocolException("Invalid wheel puncture request.");
        }
    }
}
