namespace WinterMP.Net.Messages
{
    /// <summary>Native host meat identity, creation pose and food presentation.</summary>
    public sealed class MooseMeatState : IMessage
    {
        public uint FactoryId, Revision;
        public string NativeId = string.Empty;
        public NetVector3 Position;
        public NetQuaternion Rotation;
        public float Condition;
        public byte Kind;
        public MessageId Id => MessageId.MooseMeatState;
        public void Write(NetWriter w)
        {
            if (!Sync.MooseMeatPolicy.Valid(this)) throw new ProtocolException("Invalid moose meat state.");
            w.WriteUInt32(FactoryId); w.WriteString(NativeId); w.WriteUInt32(Revision);
            w.WriteVector3(Position); w.WriteQuaternion(Rotation); w.WriteSingle(Condition); w.WriteByte(Kind);
        }
        public void Read(NetReader r)
        {
            FactoryId = r.ReadUInt32(); NativeId = r.ReadString(); Revision = r.ReadUInt32();
            Position = r.ReadVector3(); Rotation = r.ReadQuaternion(); Condition = r.ReadSingle(); Kind = r.ReadByte();
            if (!Sync.MooseMeatPolicy.Valid(this)) throw new ProtocolException("Invalid moose meat state.");
        }
    }
}
