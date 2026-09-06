namespace WinterMP.Net.Messages
{
    /// <summary>Host-owned property keys and cabin access; never a request to resolve a wager.</summary>
    public sealed class VenttiPropertyState : IMessage
    {
        // Fixed catalog order: Ruscko, Satsuma, Home; cabin sleep, stove hatch, logging.
        public const byte AllKeys = 7;
        public const byte AllAccess = 7;
        public uint Sequence;
        public byte Keys;
        public byte KnownAccess;
        public byte Access;

        public MessageId Id => MessageId.VenttiPropertyState;
        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(Sequence);
            writer.WriteByte(Keys);
            writer.WriteByte(KnownAccess);
            writer.WriteByte(Access);
        }
        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt32();
            Keys = reader.ReadByte();
            KnownAccess = reader.ReadByte();
            Access = reader.ReadByte();
        }
    }
}
