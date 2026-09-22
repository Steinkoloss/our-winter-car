namespace WinterMP.Net.Messages
{
    /// <summary>Host Corris battery inputs; does not transfer ownership or operate a guest battery.</summary>
    public sealed class BatteryState : IMessage
    {
        public const byte Available = 1, Installed = 2;
        public uint Revision;
        public byte Flags;
        public float Charge, ChargeMax;
        public MessageId Id => MessageId.BatteryState;
        public bool Valid => (Flags == 0 || Flags == Available || Flags == (Available | Installed))
            && !float.IsNaN(Charge) && !float.IsInfinity(Charge) && !float.IsNaN(ChargeMax) && !float.IsInfinity(ChargeMax)
            && ((Flags & Installed) != 0 || Charge == 0 && ChargeMax == 0);
        public void Write(NetWriter w)
        {
            if (!Valid) throw new ProtocolException("Invalid battery state.");
            w.WriteUInt32(Revision); w.WriteByte(Flags); w.WriteSingle(Charge); w.WriteSingle(ChargeMax);
        }
        public void Read(NetReader r)
        {
            Revision = r.ReadUInt32(); Flags = r.ReadByte(); Charge = r.ReadSingle(); ChargeMax = r.ReadSingle();
            if (!Valid) throw new ProtocolException("Invalid battery state.");
        }
        public BatteryState Copy() => new BatteryState { Revision = Revision, Flags = Flags, Charge = Charge, ChargeMax = ChargeMax };
    }
}
