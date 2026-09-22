namespace WinterMP.Net.Messages
{
    public enum TaxiMeterAction : byte { IncreaseMode, DecreaseMode, ToggleLight, ResetTotals }

    public sealed class TaxiMeterState : IMessage
    {
        public const ushort Available = 1, MeterOn = 2, MeterOff = 4, TaxiLight = 8,
            CustomerEnabled = 16, DriverBreak = 32, Indicators = 64, LightIndicator = 128, KnobOn = 256;
        public const int ValueCount = 9;
        public uint Revision, ControlRevision;
        public ushort Flags;
        public byte Mode;
        // Price, BaseCost, OdoTrip, OdoTotal, IncomeTotal, IncomeReceipts,
        // Odo100meters, Interval, MpS. Native units; not a client fare estimate.
        public float[] Values = new float[ValueCount];
        public string Display = "", ModeDisplay = "";
        public NetQuaternion KnobRotation = NetQuaternion.Identity;
        public MessageId Id => MessageId.TaxiMeterState;
        public void Write(NetWriter w)
        {
            Validate(); w.WriteUInt32(Revision); w.WriteUInt32(ControlRevision); w.WriteUInt16(Flags); w.WriteByte(Mode);
            for (int i = 0; i < ValueCount; i++) w.WriteSingle(Values[i]);
            w.WriteString(Display); w.WriteString(ModeDisplay); w.WriteQuaternion(KnobRotation);
        }
        public void Read(NetReader r)
        {
            Revision = r.ReadUInt32(); ControlRevision = r.ReadUInt32(); Flags = r.ReadUInt16(); Mode = r.ReadByte();
            Values = new float[ValueCount]; for (int i = 0; i < ValueCount; i++) Values[i] = r.ReadSingle();
            Display = r.ReadString(); ModeDisplay = r.ReadString(); KnobRotation = r.ReadQuaternion(); Validate();
        }
        private void Validate() { if (!Sync.TaxiMeterPolicy.Valid(this)) throw new ProtocolException("Invalid taxi meter state."); }
    }

    public sealed class TaxiMeterIntent : IMessage
    {
        public byte PlayerId;
        public uint Sequence, ExpectedControlRevision;
        public TaxiMeterAction Action;
        public MessageId Id => MessageId.TaxiMeterIntent;
        public void Write(NetWriter w)
        {
            Validate(); w.WriteByte(PlayerId); w.WriteUInt32(Sequence); w.WriteUInt32(ExpectedControlRevision); w.WriteByte((byte)Action);
        }
        public void Read(NetReader r)
        {
            PlayerId = r.ReadByte(); Sequence = r.ReadUInt32(); ExpectedControlRevision = r.ReadUInt32(); Action = (TaxiMeterAction)r.ReadByte(); Validate();
        }
        private void Validate()
        {
            if (PlayerId == byte.MaxValue || Sequence == 0 || (byte)Action > 3) throw new ProtocolException("Invalid taxi meter intent.");
        }
    }
}
