namespace WinterMP.Net.Messages
{
    public enum PackageOpenStatus : byte
    {
        Pending = 0, Accepted = 1, Busy = 2, Unavailable = 3,
        Stale = 4, Empty = 5, TooFar = 6, Failed = 7,
    }

    public sealed class PackageOpenRequest : IMessage
    {
        public byte PlayerId;
        public ulong Token;
        public uint Sequence, ItemId, ExpectedRevision;
        public MessageId Id => MessageId.PackageOpenRequest;
        public void Write(NetWriter w)
        {
            w.WriteByte(PlayerId); w.WriteUInt64(Token); w.WriteUInt32(Sequence);
            w.WriteUInt32(ItemId); w.WriteUInt32(ExpectedRevision);
        }
        public void Read(NetReader r)
        {
            PlayerId = r.ReadByte(); Token = r.ReadUInt64(); Sequence = r.ReadUInt32();
            ItemId = r.ReadUInt32(); ExpectedRevision = r.ReadUInt32();
        }
    }

    public sealed class PackageOpenReceipt : IMessage
    {
        public byte PlayerId;
        public ulong Token;
        public uint Sequence, ItemId, ProducedItemId;
        public PackageOpenStatus Status;
        public MessageId Id => MessageId.PackageOpenReceipt;
        public void Write(NetWriter w)
        {
            if ((byte)Status > (byte)PackageOpenStatus.Failed) throw new ProtocolException("Invalid box-opening result.");
            w.WriteByte(PlayerId); w.WriteUInt64(Token); w.WriteUInt32(Sequence);
            w.WriteUInt32(ItemId); w.WriteByte((byte)Status); w.WriteUInt32(ProducedItemId);
        }
        public void Read(NetReader r)
        {
            PlayerId = r.ReadByte(); Token = r.ReadUInt64(); Sequence = r.ReadUInt32();
            ItemId = r.ReadUInt32(); Status = (PackageOpenStatus)r.ReadByte(); ProducedItemId = r.ReadUInt32();
            if ((byte)Status > (byte)PackageOpenStatus.Failed) throw new ProtocolException("Invalid box-opening result.");
        }
    }
}
