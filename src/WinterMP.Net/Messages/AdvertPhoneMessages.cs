namespace WinterMP.Net.Messages
{
    public sealed class AdvertPhoneIntent : IMessage
    {
        public const byte Begin = 0, KeepAlive = 1, Complete = 2, Cancel = 3;
        public uint Call;
        public byte PlayerId, Phone, Action;
        public MessageId Id => MessageId.AdvertPhoneIntent;
        public void Write(NetWriter w) { Validate(); w.WriteUInt32(Call); w.WriteByte(PlayerId); w.WriteByte(Phone); w.WriteByte(Action); }
        public void Read(NetReader r) { Call = r.ReadUInt32(); PlayerId = r.ReadByte(); Phone = r.ReadByte(); Action = r.ReadByte(); Validate(); }
        private void Validate()
        { if (Call == 0 || PlayerId == 0 || PlayerId == 255 || Phone > 2 || Action > Cancel) throw new ProtocolException("Invalid advert phone intent."); }
    }
    public sealed class AdvertPhoneResult : IMessage
    {
        public const byte Accepted = 0, Completed = 1, Rejected = 2;
        public uint Call;
        public byte PlayerId, Phone, Status;
        public MessageId Id => MessageId.AdvertPhoneResult;
        public void Write(NetWriter w) { Validate(); w.WriteUInt32(Call); w.WriteByte(PlayerId); w.WriteByte(Phone); w.WriteByte(Status); }
        public void Read(NetReader r) { Call = r.ReadUInt32(); PlayerId = r.ReadByte(); Phone = r.ReadByte(); Status = r.ReadByte(); Validate(); }
        private void Validate()
        { if (Call == 0 || PlayerId == 255 || Phone > 2 || Status > Rejected) throw new ProtocolException("Invalid advert phone result."); }
    }
}
