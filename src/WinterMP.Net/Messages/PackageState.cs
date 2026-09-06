namespace WinterMP.Net.Messages
{
    /// <summary>Host box identity and quantity; existing item streams carry movement.</summary>
    public sealed class PackageState : IMessage
    {
        public uint Revision, FactoryId;
        public string NativeId = string.Empty;
        public ushort Quantity;
        public NetVector3 Position;
        public NetQuaternion Rotation;
        public MessageId Id => MessageId.PackageState;
        public void Write(NetWriter w)
        {
            w.WriteUInt32(Revision); w.WriteUInt32(FactoryId); w.WriteString(NativeId);
            w.WriteUInt16(Quantity); w.WriteVector3(Position); w.WriteQuaternion(Rotation);
        }
        public void Read(NetReader r)
        {
            Revision = r.ReadUInt32(); FactoryId = r.ReadUInt32(); NativeId = r.ReadString();
            Quantity = r.ReadUInt16(); Position = r.ReadVector3(); Rotation = r.ReadQuaternion();
        }
    }
}
