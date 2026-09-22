namespace WinterMP.Net.Messages
{
    /// <summary>Host wiring inputs for protected engine calculations; never a guest save mutation.</summary>
    public sealed class WiringState : IMessage
    {
        public const byte Available = 1, Installed = 2, Bolted = 4, Connectable = 8;
        public uint SourceId, Revision;
        public byte Flags;
        public MessageId Id => MessageId.WiringState;

        public static bool ValidFlags(byte flags) => flags == 0 || (flags & Available) != 0 && (flags & ~15) == 0 && ((flags & Connectable) == 0 || (flags & (Installed | Bolted)) == 0);

        public void Write(NetWriter writer)
        {
            if (!ValidFlags(Flags) || SourceId != 5 && (Flags & Connectable) != 0) throw new ProtocolException("Invalid wiring flags.");
            writer.WriteUInt32(SourceId); writer.WriteUInt32(Revision); writer.WriteByte(Flags);
        }

        public void Read(NetReader reader)
        {
            SourceId = reader.ReadUInt32(); Revision = reader.ReadUInt32(); Flags = reader.ReadByte();
            if (!ValidFlags(Flags) || SourceId != 5 && (Flags & Connectable) != 0) throw new ProtocolException("Invalid wiring flags.");
        }
    }
}
