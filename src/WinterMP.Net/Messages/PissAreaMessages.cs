namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: yard piss-stain scales (COVERAGE-ROADMAP 8.3). <c>YARD/PissAreas</c>
    /// scales five persistent, world-visible snow stains on the PISS action; they are
    /// host-saved and shared-visible but run per-client, so the yards disagree. The <b>host</b>
    /// owns the stains and broadcasts the five scales on change + join; guests apply them.
    /// See PissAreaSync.
    /// </summary>
    public sealed class PissAreaState : IMessage
    {
        public ushort Sequence;
        /// <summary>Stain scales, quantized *20 into a byte (0..~12.75 range clamped).</summary>
        public byte Scale1;
        public byte Scale2;
        public byte Scale3;
        public byte Scale4;
        public byte Scale5;

        public MessageId Id => MessageId.PissAreaState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16(Sequence);
            writer.WriteByte(Scale1);
            writer.WriteByte(Scale2);
            writer.WriteByte(Scale3);
            writer.WriteByte(Scale4);
            writer.WriteByte(Scale5);
        }

        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt16();
            Scale1 = reader.ReadByte();
            Scale2 = reader.ReadByte();
            Scale3 = reader.ReadByte();
            Scale4 = reader.ReadByte();
            Scale5 = reader.ReadByte();
        }
    }
}
