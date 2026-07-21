namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: shared jail-sentence state (COVERAGE-ROADMAP 4.2). The arrest→jail flow
    /// is offender-local — one player is jailed while the other roams free, and the
    /// day-countdown runs only on the jailed client. The <b>host</b> owns the sentence: when
    /// its (host-authoritative, see 4.1) wanted logic jails a player it broadcasts the
    /// remaining days + sentence; guests apply the countdown so the jail agrees and everyone is
    /// released together. The jailed player's confinement position rides the normal player
    /// transform stream. See JailSync.
    /// </summary>
    public sealed class JailState : IMessage
    {
        /// <summary>A jail sentence is currently being served.</summary>
        public const byte FlagJailed = 1;

        public ushort Sequence;
        public int DaysLeft;
        public int Sentence;
        public byte Flags;

        public bool Jailed => (Flags & FlagJailed) != 0;

        public MessageId Id => MessageId.JailState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16(Sequence);
            writer.WriteInt32(DaysLeft);
            writer.WriteInt32(Sentence);
            writer.WriteByte(Flags);
        }

        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt16();
            DaysLeft = reader.ReadInt32();
            Sentence = reader.ReadInt32();
            Flags = reader.ReadByte();
        }
    }
}
