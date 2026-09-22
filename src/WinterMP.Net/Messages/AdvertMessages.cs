namespace WinterMP.Net.Messages
{
    public sealed class AdvertJobState : IMessage
    {
        public uint Revision, CompletedMask;
        public int Delivered;
        public byte Sheets, Stage, NextDay, Flags;
        public float Scale, Salary;
        public NetVector3 Position;
        public NetQuaternion Rotation = NetQuaternion.Identity;
        public MessageId Id => MessageId.AdvertJobState;
        public void Write(NetWriter w)
        {
            Validate(); w.WriteUInt32(Revision); w.WriteInt32(Delivered); w.WriteByte(Sheets); w.WriteByte(Stage); w.WriteByte(NextDay); w.WriteByte(Flags);
            w.WriteUInt32(CompletedMask); w.WriteSingle(Scale); w.WriteSingle(Salary); w.WriteVector3(Position); w.WriteQuaternion(Rotation);
        }
        public void Read(NetReader r)
        {
            Revision = r.ReadUInt32(); Delivered = r.ReadInt32(); Sheets = r.ReadByte(); Stage = r.ReadByte(); NextDay = r.ReadByte(); Flags = r.ReadByte();
            CompletedMask = r.ReadUInt32(); Scale = r.ReadSingle(); Salary = r.ReadSingle(); Position = r.ReadVector3(); Rotation = r.ReadQuaternion(); Validate();
        }
        private void Validate() { if (!Sync.AdvertPolicy.Valid(this)) throw new ProtocolException("Invalid advert job state."); }
    }
    public sealed class AdvertSheetState : IMessage
    {
        public uint ItemId;
        public NetVector3 Position;
        public NetQuaternion Rotation = NetQuaternion.Identity;
        public MessageId Id => MessageId.AdvertSheetState;
        public void Write(NetWriter w) { Validate(); w.WriteUInt32(ItemId); w.WriteVector3(Position); w.WriteQuaternion(Rotation); }
        public void Read(NetReader r) { ItemId = r.ReadUInt32(); Position = r.ReadVector3(); Rotation = r.ReadQuaternion(); Validate(); }
        private void Validate() { if (ItemId == 0 || !Sync.AdvertPolicy.Pose(Position, Rotation)) throw new ProtocolException("Invalid advert sheet."); }
    }
    public sealed class AdvertIntent : IMessage
    {
        public uint Sequence, ExpectedRevision, ItemId;
        public byte PlayerId, Box = 255;
        public MessageId Id => MessageId.AdvertIntent;
        public void Write(NetWriter w) { Validate(); w.WriteUInt32(Sequence); w.WriteUInt32(ExpectedRevision); w.WriteUInt32(ItemId); w.WriteByte(PlayerId); w.WriteByte(Box); }
        public void Read(NetReader r) { Sequence = r.ReadUInt32(); ExpectedRevision = r.ReadUInt32(); ItemId = r.ReadUInt32(); PlayerId = r.ReadByte(); Box = r.ReadByte(); Validate(); }
        private void Validate() { if (!Sync.AdvertPolicy.Valid(this)) throw new ProtocolException("Invalid advert request."); }
    }
}
