namespace WinterMP.Net.Messages
{
    public sealed class BeerCaseExtractIntent : IMessage
    {
        public uint CaseId, Epoch, Connection, Sequence, ExpectedRevision;
        public string NativeId = string.Empty;
        public int ExpectedRemaining;
        public byte PlayerId;
        public MessageId Id => MessageId.BeerCaseExtractIntent;
        public BeerCaseExtractIntent Copy() => (BeerCaseExtractIntent)MemberwiseClone();
        public void Write(NetWriter w)
        {
            Validate(); w.WriteUInt32(CaseId); w.WriteString(NativeId); w.WriteUInt32(Epoch);
            w.WriteUInt32(Connection); w.WriteUInt32(Sequence); w.WriteUInt32(ExpectedRevision);
            w.WriteInt32(ExpectedRemaining); w.WriteByte(PlayerId);
        }
        public void Read(NetReader r)
        {
            CaseId = r.ReadUInt32(); NativeId = r.ReadString(); Epoch = r.ReadUInt32();
            Connection = r.ReadUInt32(); Sequence = r.ReadUInt32(); ExpectedRevision = r.ReadUInt32();
            ExpectedRemaining = r.ReadInt32(); PlayerId = r.ReadByte(); Validate();
        }
        private void Validate() { if (!Sync.BeerCasePolicy.Valid(this)) throw new ProtocolException("Invalid beer-case extraction."); }
    }

    // Absolute contents, also used for snapshots. No relative event, spawned bottle,
    // actor-local drink permission or save record is represented by this message.
    public sealed class BeerCaseUpdate : IMessage
    {
        public uint CaseId, Epoch, Revision, Connection, Sequence;
        public string NativeId = string.Empty;
        public int Capacity, Remaining;
        public bool Available;
        public byte PlayerId = 255;
        public MessageId Id => MessageId.BeerCaseUpdate;
        public BeerCaseUpdate Copy() => (BeerCaseUpdate)MemberwiseClone();
        public void Write(NetWriter w)
        {
            Validate(); w.WriteUInt32(CaseId); w.WriteString(NativeId); w.WriteUInt32(Epoch);
            w.WriteUInt32(Revision); w.WriteInt32(Capacity); w.WriteInt32(Remaining);
            w.WriteBool(Available); w.WriteByte(PlayerId); w.WriteUInt32(Connection); w.WriteUInt32(Sequence);
        }
        public void Read(NetReader r)
        {
            CaseId = r.ReadUInt32(); NativeId = r.ReadString(); Epoch = r.ReadUInt32();
            Revision = r.ReadUInt32(); Capacity = r.ReadInt32(); Remaining = r.ReadInt32();
            byte available = r.ReadByte();
            if (available > 1) throw new ProtocolException("Invalid beer-case availability.");
            Available = available == 1; PlayerId = r.ReadByte(); Connection = r.ReadUInt32(); Sequence = r.ReadUInt32(); Validate();
        }
        private void Validate() { if (!Sync.BeerCasePolicy.Valid(this)) throw new ProtocolException("Invalid beer-case update."); }
    }
}
