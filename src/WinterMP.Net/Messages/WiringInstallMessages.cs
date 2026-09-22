namespace WinterMP.Net.Messages
{
    public enum WiringInstallStatus : byte
    {
        Pending = 0, Accepted = 1, Busy = 2, Unavailable = 3,
        Stale = 4, Installed = 5, TooFar = 6, Failed = 7,
    }

    public sealed class WiringInstallRequest : IMessage
    {
        public byte PlayerId;
        public ulong Token;
        public uint Sequence, SourceId, ExpectedRevision;
        public MessageId Id => MessageId.WiringInstallRequest;
        public void Write(NetWriter w)
        {
            w.WriteByte(PlayerId); w.WriteUInt64(Token); w.WriteUInt32(Sequence);
            w.WriteUInt32(SourceId); w.WriteUInt32(ExpectedRevision);
        }
        public void Read(NetReader r)
        {
            PlayerId = r.ReadByte(); Token = r.ReadUInt64(); Sequence = r.ReadUInt32();
            SourceId = r.ReadUInt32(); ExpectedRevision = r.ReadUInt32();
        }
    }

    public sealed class WiringInstallReceipt : IMessage
    {
        public byte PlayerId;
        public ulong Token;
        public uint Sequence, SourceId;
        public WiringInstallStatus Status;
        public MessageId Id => MessageId.WiringInstallReceipt;
        public void Write(NetWriter w)
        {
            if ((byte)Status > (byte)WiringInstallStatus.Failed) throw new ProtocolException("Invalid wire-installation result.");
            w.WriteByte(PlayerId); w.WriteUInt64(Token); w.WriteUInt32(Sequence);
            w.WriteUInt32(SourceId); w.WriteByte((byte)Status);
        }
        public void Read(NetReader r)
        {
            PlayerId = r.ReadByte(); Token = r.ReadUInt64(); Sequence = r.ReadUInt32();
            SourceId = r.ReadUInt32(); Status = (WiringInstallStatus)r.ReadByte();
            if ((byte)Status > (byte)WiringInstallStatus.Failed) throw new ProtocolException("Invalid wire-installation result.");
        }
    }
}
