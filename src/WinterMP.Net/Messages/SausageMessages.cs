namespace WinterMP.Net.Messages
{
    public sealed class SausageOpenIntent : IMessage
    {
        public uint SourceId, PackageId, Sequence;
        public byte PlayerId;
        public MessageId Id => MessageId.SausageOpenIntent;
        public void Write(NetWriter w) { Validate(); w.WriteUInt32(SourceId); w.WriteUInt32(PackageId); w.WriteUInt32(Sequence); w.WriteByte(PlayerId); }
        public void Read(NetReader r) { SourceId = r.ReadUInt32(); PackageId = r.ReadUInt32(); Sequence = r.ReadUInt32(); PlayerId = r.ReadByte(); Validate(); }
        private void Validate() { if (!Sync.SausagePolicy.Valid(this)) throw new ProtocolException("Invalid sausage opening intent."); }
    }
    public sealed class SausageState : IMessage
    {
        public uint ItemId, Revision;
        public float Condition;
        // Name/appearance: fresh, grilled, charred, spoiled. Grilled is also a
        // native food-effects variable and can survive a subsequent burn/spoil.
        public byte Kind;
        public bool Grilled;
        public NetVector3 Position;
        public NetQuaternion Rotation = NetQuaternion.Identity;
        public MessageId Id => MessageId.SausageState;
        public void Write(NetWriter w) { Validate(); w.WriteUInt32(ItemId); w.WriteUInt32(Revision); w.WriteSingle(Condition); w.WriteByte(Kind); w.WriteBool(Grilled); w.WriteVector3(Position); w.WriteQuaternion(Rotation); }
        public void Read(NetReader r) { ItemId = r.ReadUInt32(); Revision = r.ReadUInt32(); Condition = r.ReadSingle(); Kind = r.ReadByte(); Grilled = r.ReadBool(); Position = r.ReadVector3(); Rotation = r.ReadQuaternion(); Validate(); }
        private void Validate() { if (!Sync.SausagePolicy.Valid(this)) throw new ProtocolException("Invalid sausage state."); }
    }
}
