namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> all: authoritative state of a utility meter (electricity / phone bill).
    /// Guests retain the host's meter, envelope and displayed invoice. Phone inputs
    /// follow Revision for meters 2/3; electricity layout is unchanged.
    /// </summary>
    public sealed class UtilityBillState : IMessage
    {
        public const byte MeterElectricity1 = 0;
        public const byte MeterElectricity2 = 1;
        public const byte MeterPhone1 = 2;
        public const byte MeterPhone2 = 3;

        /// <summary>Electricity: effective HouseElectricity supply. Phone: PhonePaid.</summary>
        public const byte FlagPowerOn = 1;
        public const byte FlagBillVisible = 2;
        public const byte FlagMainSwitchOn = 4;

        public byte Meter;
        public float UnpaidBills;
        public byte Flags;
        public uint Revision;
        public Sync.PhoneBillQuote? Phone;
        public float PaymentTotal => Meter < 2 ? UnpaidBills : Phone?.Total ?? 0;

        public bool PowerOn => (Flags & FlagPowerOn) != 0;
        public bool BillVisible => (Flags & FlagBillVisible) != 0;
        public bool MainSwitchOn => (Flags & FlagMainSwitchOn) != 0;

        public MessageId Id => MessageId.UtilityBillState;

        public void Write(NetWriter writer)
        {
            if (!Sync.UtilityBillPolicy.Valid(this)) throw new ProtocolException("Invalid utility bill state.");
            writer.WriteByte(Meter);
            writer.WriteSingle(UnpaidBills);
            writer.WriteByte(Flags);
            writer.WriteUInt32(Revision);
            if (Meter >= 2) Phone!.Write(writer);
        }

        public void Read(NetReader reader)
        {
            Meter = reader.ReadByte();
            UnpaidBills = reader.ReadSingle();
            Flags = reader.ReadByte();
            Revision = reader.ReadUInt32();
            Phone = Meter >= 2 && Meter <= 3 ? Sync.PhoneBillQuote.Read(reader) : null;
            if (!Sync.UtilityBillPolicy.Valid(this)) throw new ProtocolException("Invalid utility bill state.");
        }
    }

    public sealed class UtilityPaymentIntent : IMessage
    {
        public byte PlayerId, Meter;
        public ushort Sequence;
        public uint Revision;
        public MessageId Id => MessageId.UtilityPaymentIntent;
        private void Validate() { if (PlayerId == 255 || Meter > 3) throw new ProtocolException("Invalid utility payment."); }
        public void Write(NetWriter w) { Validate(); w.WriteByte(PlayerId); w.WriteByte(Meter); w.WriteUInt16(Sequence); w.WriteUInt32(Revision); }
        public void Read(NetReader r) { PlayerId = r.ReadByte(); Meter = r.ReadByte(); Sequence = r.ReadUInt16(); Revision = r.ReadUInt32(); Validate(); }
    }

    public sealed class UtilityPaymentResult : IMessage
    {
        public const byte Accepted = 0, Changed = 1, Unavailable = 2, Distant = 3, Funds = 4, Stale = 5;
        public byte PlayerId, Meter, Result;
        public ushort Sequence;
        public float Paid;
        public MessageId Id => MessageId.UtilityPaymentResult;
        private void Validate()
        {
            if (PlayerId == 255 || Meter > 3 || Result > Stale || !BankTransferPolicy.IsFinite(Paid)
                || Paid < 0 || (Result == Accepted ? Paid <= 0 : Paid != 0)) throw new ProtocolException("Invalid utility receipt.");
        }
        public void Write(NetWriter w) { Validate(); w.WriteByte(PlayerId); w.WriteByte(Meter); w.WriteUInt16(Sequence); w.WriteByte(Result); w.WriteSingle(Paid); }
        public void Read(NetReader r) { PlayerId = r.ReadByte(); Meter = r.ReadByte(); Sequence = r.ReadUInt16(); Result = r.ReadByte(); Paid = r.ReadSingle(); Validate(); }
    }
}
