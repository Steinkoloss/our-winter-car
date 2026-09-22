namespace WinterMP.Net.Messages
{
    /// <summary>Persistent loose supply identity; item streams carry subsequent movement.</summary>
    public sealed class SupplyItemState : IMessage
    {
        public uint FactoryId;
        public string NativeId = string.Empty;
        public NetVector3 Position;
        public NetQuaternion Rotation;
        public MessageId Id => MessageId.SupplyItemState;
        public void Write(NetWriter w)
        {
            w.WriteUInt32(FactoryId); w.WriteString(NativeId);
            w.WriteVector3(Position); w.WriteQuaternion(Rotation);
        }
        public void Read(NetReader r)
        {
            FactoryId = r.ReadUInt32(); NativeId = r.ReadString();
            Position = r.ReadVector3(); Rotation = r.ReadQuaternion();
        }
    }
}
