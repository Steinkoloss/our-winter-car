namespace WinterMP.Net.Messages
{
    /// <summary>Session-only loose bulb identity and native condition; vanilla does not save loose bulbs.</summary>
    public sealed class BulbState : IMessage
    {
        public uint ItemId, Revision;
        public float Wear;
        public NetVector3 Position;
        public NetQuaternion Rotation = NetQuaternion.Identity;
        public MessageId Id => MessageId.BulbState;
        public void Write(NetWriter w)
        {
            Validate(); w.WriteUInt32(ItemId); w.WriteUInt32(Revision); w.WriteSingle(Wear);
            w.WriteVector3(Position); w.WriteQuaternion(Rotation);
        }
        public void Read(NetReader r)
        {
            ItemId = r.ReadUInt32(); Revision = r.ReadUInt32(); Wear = r.ReadSingle();
            Position = r.ReadVector3(); Rotation = r.ReadQuaternion(); Validate();
        }
        private void Validate() { if (!Sync.BulbPolicy.Valid(this)) throw new ProtocolException("Invalid loose bulb state."); }
    }
}
