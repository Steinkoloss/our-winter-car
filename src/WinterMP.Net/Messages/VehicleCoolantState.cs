namespace WinterMP.Net.Messages
{
    /// <summary>Host coolant and engine degrees, independent of the driver.</summary>
    public sealed class VehicleCoolantState : IMessage
    {
        public const byte Available = 1;
        public uint VehicleId, Revision;
        public byte Flags;
        public float Celsius;
        public float EngineCelsius;
        public MessageId Id => MessageId.VehicleCoolantState;
        public bool Valid => VehicleId != 0 && (Flags == 0 || Flags == Available)
            && !float.IsNaN(Celsius) && !float.IsInfinity(Celsius)
            && !float.IsNaN(EngineCelsius) && !float.IsInfinity(EngineCelsius)
            && (Flags != 0 || Celsius == 0 && EngineCelsius == 0);
        public void Write(NetWriter w)
        {
            if (!Valid) throw new ProtocolException("Invalid vehicle coolant state.");
            w.WriteUInt32(VehicleId); w.WriteUInt32(Revision); w.WriteByte(Flags); w.WriteSingle(Celsius);
            w.WriteSingle(EngineCelsius);
        }
        public void Read(NetReader r)
        {
            VehicleId = r.ReadUInt32(); Revision = r.ReadUInt32(); Flags = r.ReadByte(); Celsius = r.ReadSingle();
            EngineCelsius = r.ReadSingle();
            if (!Valid) throw new ProtocolException("Invalid vehicle coolant state.");
        }
        public VehicleCoolantState Copy() => new VehicleCoolantState { VehicleId = VehicleId, Revision = Revision, Flags = Flags, Celsius = Celsius, EngineCelsius = EngineCelsius };
    }
}
