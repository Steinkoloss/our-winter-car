namespace WinterMP.Net.Messages
{
    public enum TaxiFareAction : byte { Charge, Collect, PrintReceipt, TakeReceipt, GiveReceipt }
    public enum TaxiReceiptStage : byte { Hidden, Printing, Ready, Loose, Customer, Returned }

    public sealed class TaxiFareState : IMessage
    {
        public const byte Active = 1, Arrived = 2, CanCharge = 4, CashVisible = 8,
            CanCollect = 16, Paid = 32, ReceiptRequested = 64, Charged = 128;
        public const byte CanPrint = 1, CanTake = 2, CanGive = 4, PrintVisible = 8, ReceiptTriggerVisible = 16;
        public uint Revision, ControlRevision, FareId;
        public TaxiReceiptStage ReceiptStage;
        public byte ReceiptFlags;
        public NetVector3 ReceiptPosition;
        public NetQuaternion ReceiptRotation = NetQuaternion.Identity;
        public byte Flags;
        public float QuotedCost, OfferedCost;
        public string TerminalDisplay = "", OfferLabel = "";
        public MessageId Id => MessageId.TaxiFareState;
        public void Write(NetWriter w)
        {
            Validate(); w.WriteUInt32(Revision); w.WriteUInt32(ControlRevision); w.WriteUInt32(FareId);
            w.WriteByte(Flags); w.WriteSingle(QuotedCost); w.WriteSingle(OfferedCost);
            w.WriteString(TerminalDisplay); w.WriteString(OfferLabel);
            w.WriteByte((byte)ReceiptStage); w.WriteByte(ReceiptFlags); w.WriteVector3(ReceiptPosition); w.WriteQuaternion(ReceiptRotation);
        }
        public void Read(NetReader r)
        {
            Revision = r.ReadUInt32(); ControlRevision = r.ReadUInt32(); FareId = r.ReadUInt32(); Flags = r.ReadByte();
            QuotedCost = r.ReadSingle(); OfferedCost = r.ReadSingle(); TerminalDisplay = r.ReadString(); OfferLabel = r.ReadString();
            ReceiptStage = (TaxiReceiptStage)r.ReadByte(); ReceiptFlags = r.ReadByte(); ReceiptPosition = r.ReadVector3(); ReceiptRotation = r.ReadQuaternion(); Validate();
        }
        private void Validate() { if (!Sync.TaxiFarePolicy.Valid(this)) throw new ProtocolException("Invalid taxi fare state."); }
    }

    public sealed class TaxiFareIntent : IMessage
    {
        public byte PlayerId;
        public uint Sequence, ExpectedControlRevision, FareId;
        public TaxiFareAction Action;
        public MessageId Id => MessageId.TaxiFareIntent;
        public void Write(NetWriter w)
        {
            Validate(); w.WriteByte(PlayerId); w.WriteUInt32(Sequence); w.WriteUInt32(ExpectedControlRevision); w.WriteUInt32(FareId); w.WriteByte((byte)Action);
        }
        public void Read(NetReader r)
        {
            PlayerId = r.ReadByte(); Sequence = r.ReadUInt32(); ExpectedControlRevision = r.ReadUInt32(); FareId = r.ReadUInt32(); Action = (TaxiFareAction)r.ReadByte(); Validate();
        }
        private void Validate()
        {
            if (PlayerId == byte.MaxValue || Sequence == 0 || FareId == 0 || (byte)Action > 4) throw new ProtocolException("Invalid taxi fare intent.");
        }
    }
}
