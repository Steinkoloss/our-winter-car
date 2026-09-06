namespace WinterMP.Net.Messages
{
    /// <summary>Host bag identity and remaining contents; item streams carry subsequent movement.</summary>
    public sealed class BagState : IMessage
    {
        public uint ItemId, FactoryId, Revision;
        public string NativeId = string.Empty;
        public ushort Remaining;
        public float Condition;
        public NetVector3 Position;
        public NetQuaternion Rotation;
        public MessageId Id => MessageId.BagState;
        public void Write(NetWriter w)
        {
            w.WriteUInt32(ItemId); w.WriteUInt32(FactoryId); w.WriteString(NativeId); w.WriteUInt32(Revision);
            w.WriteUInt16(Remaining); w.WriteSingle(Condition); w.WriteVector3(Position); w.WriteQuaternion(Rotation);
        }
        public void Read(NetReader r)
        {
            ItemId = r.ReadUInt32(); FactoryId = r.ReadUInt32(); NativeId = r.ReadString(); Revision = r.ReadUInt32();
            Remaining = r.ReadUInt16(); Condition = r.ReadSingle(); Position = r.ReadVector3(); Rotation = r.ReadQuaternion();
        }
    }

    public enum BagOpenStatus : byte
    {
        Pending = 0, Applied = 1, Stale = 2, Unavailable = 3,
        Busy = 4, OutOfReach = 5, NotOwner = 6, Failed = 7,
    }

    public sealed class BagOpenRequest : IMessage
    {
        public byte PlayerId;
        public uint Sequence, ItemId, ExpectedRevision;
        public bool OpenAll;
        public MessageId Id => MessageId.BagOpenRequest;
        public void Write(NetWriter w)
        {
            w.WriteByte(PlayerId); w.WriteUInt32(Sequence); w.WriteUInt32(ItemId);
            w.WriteUInt32(ExpectedRevision); w.WriteBool(OpenAll);
        }
        public void Read(NetReader r)
        {
            PlayerId = r.ReadByte(); Sequence = r.ReadUInt32(); ItemId = r.ReadUInt32();
            ExpectedRevision = r.ReadUInt32(); OpenAll = r.ReadBool();
        }
    }

    public sealed class BagOpenReceipt : IMessage
    {
        public byte PlayerId;
        public uint Sequence, ItemId, ExpectedRevision;
        public bool OpenAll;
        public BagOpenStatus Status;
        public MessageId Id => MessageId.BagOpenReceipt;
        public void Write(NetWriter w)
        {
            if ((byte)Status > (byte)BagOpenStatus.Failed) throw new ProtocolException("Invalid bag-opening result.");
            w.WriteByte(PlayerId); w.WriteUInt32(Sequence); w.WriteUInt32(ItemId);
            w.WriteUInt32(ExpectedRevision); w.WriteBool(OpenAll); w.WriteByte((byte)Status);
        }
        public void Read(NetReader r)
        {
            PlayerId = r.ReadByte(); Sequence = r.ReadUInt32(); ItemId = r.ReadUInt32();
            ExpectedRevision = r.ReadUInt32(); OpenAll = r.ReadBool(); Status = (BagOpenStatus)r.ReadByte();
            if ((byte)Status > (byte)BagOpenStatus.Failed) throw new ProtocolException("Invalid bag-opening result.");
        }
    }
}
