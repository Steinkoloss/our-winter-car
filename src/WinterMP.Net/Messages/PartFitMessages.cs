namespace WinterMP.Net.Messages
{
    public enum PartFitOperation : byte { Install = 0, Remove = 1, RotateIncrease = 2, RotateDecrease = 3 }

    public enum PartFitStatus : byte
    {
        Pending = 0, Accepted = 1, Busy = 2, Unavailable = 3,
        Stale = 4, NotLoose = 5, TooFar = 6, Blocked = 7, Failed = 8, NotFitted = 9, Bolted = 10,
    }

    public sealed class PartFitRequest : IMessage
    {
        public byte PlayerId;
        public ulong Token;
        public uint Sequence, ItemId, ExpectedRevision;
        public PartFitOperation Operation;
        public byte SlotIndex;
        public MessageId Id => MessageId.PartFitRequest;
        public void Write(NetWriter w)
        {
            w.WriteByte(PlayerId); w.WriteUInt64(Token); w.WriteUInt32(Sequence);
            w.WriteUInt32(ItemId); w.WriteUInt32(ExpectedRevision);
            if (!Sync.PartSlotPolicy.ValidRequest(Operation, SlotIndex)) throw new ProtocolException("Invalid part operation or slot.");
            w.WriteByte((byte)Operation);
            w.WriteByte(SlotIndex);
        }
        public void Read(NetReader r)
        {
            PlayerId = r.ReadByte(); Token = r.ReadUInt64(); Sequence = r.ReadUInt32();
            ItemId = r.ReadUInt32(); ExpectedRevision = r.ReadUInt32();
            Operation = (PartFitOperation)r.ReadByte();
            SlotIndex = r.ReadByte();
            if (!Sync.PartSlotPolicy.ValidRequest(Operation, SlotIndex)) throw new ProtocolException("Invalid part operation or slot.");
        }
    }

    public sealed class PartFitReceipt : IMessage
    {
        public byte PlayerId;
        public ulong Token;
        public uint Sequence, ItemId;
        public PartFitStatus Status;
        public PartFitOperation Operation;
        public byte SlotIndex;
        public MessageId Id => MessageId.PartFitReceipt;
        public void Write(NetWriter w)
        {
            if ((byte)Status > (byte)PartFitStatus.Bolted) throw new ProtocolException("Invalid part-fitting result.");
            w.WriteByte(PlayerId); w.WriteUInt64(Token); w.WriteUInt32(Sequence);
            w.WriteUInt32(ItemId); w.WriteByte((byte)Status);
            if (!Sync.PartSlotPolicy.ValidRequest(Operation, SlotIndex)) throw new ProtocolException("Invalid part operation or slot.");
            w.WriteByte((byte)Operation);
            w.WriteByte(SlotIndex);
        }
        public void Read(NetReader r)
        {
            PlayerId = r.ReadByte(); Token = r.ReadUInt64(); Sequence = r.ReadUInt32();
            ItemId = r.ReadUInt32(); Status = (PartFitStatus)r.ReadByte();
            Operation = (PartFitOperation)r.ReadByte();
            SlotIndex = r.ReadByte();
            if (!Sync.PartSlotPolicy.ValidRequest(Operation, SlotIndex)) throw new ProtocolException("Invalid part operation or slot.");
            if ((byte)Status > (byte)PartFitStatus.Bolted) throw new ProtocolException("Invalid part-fitting result.");
        }
    }
}
