namespace WinterMP.Net.Messages
{
    /// <summary>Host Corris gearbox type for the native starter interlock.</summary>
    public sealed class GearboxState : IMessage
    {
        public const byte Available = 1;
        public uint Revision;
        public byte Flags;
        public int Type;
        public MessageId Id => MessageId.GearboxState;
        public bool Valid => (Flags == 0 || Flags == Available) && (Flags != 0 || Type == 0);
        public void Write(NetWriter w)
        {
            if (!Valid) throw new ProtocolException("Invalid gearbox state.");
            w.WriteUInt32(Revision); w.WriteByte(Flags); w.WriteInt32(Type);
        }
        public void Read(NetReader r)
        {
            Revision = r.ReadUInt32(); Flags = r.ReadByte(); Type = r.ReadInt32();
            if (!Valid) throw new ProtocolException("Invalid gearbox state.");
        }
        public GearboxState Copy() => new GearboxState { Revision = Revision, Flags = Flags, Type = Type };
    }
}
