namespace WinterMP.Net.Messages
{
    /// <summary>Host Corris heater inputs; does not transfer ownership or operate a guest heater.</summary>
    public sealed class HeaterState : IMessage
    {
        public const byte Available = 1, Installed = 2;
        public uint Revision;
        public byte Flags;
        public float Wear;
        public byte RearWindowFlags;
        public MessageId Id => MessageId.HeaterState;
        public bool Valid => (Flags == 0 || Flags == Available || Flags == (Available | Installed))
            && !float.IsNaN(Wear) && !float.IsInfinity(Wear)
            && ((Flags & Installed) != 0 || Wear == 0)
            && (RearWindowFlags == 0 || RearWindowFlags == Available || RearWindowFlags == (Available | Installed));
        public void Write(NetWriter w)
        {
            if (!Valid) throw new ProtocolException("Invalid heater state.");
            w.WriteUInt32(Revision); w.WriteByte(Flags); w.WriteSingle(Wear); w.WriteByte(RearWindowFlags);
        }
        public void Read(NetReader r)
        {
            Revision = r.ReadUInt32(); Flags = r.ReadByte(); Wear = r.ReadSingle(); RearWindowFlags = r.ReadByte();
            if (!Valid) throw new ProtocolException("Invalid heater state.");
        }
        public HeaterState Copy() => new HeaterState { Revision = Revision, Flags = Flags, Wear = Wear, RearWindowFlags = RearWindowFlags };
    }
}
