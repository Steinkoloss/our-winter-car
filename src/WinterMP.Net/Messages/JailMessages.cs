namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Shared jail-sentence state (COVERAGE-ROADMAP 4.2). The arrest→jail flow is
    /// offender-local — one player is jailed while the others roam free, and the
    /// day-countdown runs only on the jailed client's <c>JAIL/Functions :: Time</c> FSM.
    /// So the JAILED client owns the countdown (v83): while its local DaysLeft is
    /// positive it sends this message to the host, which adopts the record and relays it
    /// to everyone; when the host itself is jailed (or nobody is) the host broadcasts its
    /// own FSM. Non-jailed clients apply DaysLeft for presentation only; the jailed
    /// client ignores broadcasts about itself. The jailed player's confinement position
    /// rides the normal player transform stream. See JailSync.
    /// </summary>
    public sealed class JailState : IMessage
    {
        /// <summary>A jail sentence is currently being served.</summary>
        public const byte FlagJailed = 1;

        /// <summary>No player is serving a sentence.</summary>
        public const byte NoPlayer = 255;

        public ushort Sequence;
        public int DaysLeft;
        public int Sentence;
        public byte Flags;
        /// <summary>Which player serves the sentence (its client owns the countdown). Appended v83.</summary>
        public byte JailedPlayerId = NoPlayer;

        public bool Jailed => (Flags & FlagJailed) != 0;

        public MessageId Id => MessageId.JailState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16(Sequence);
            writer.WriteInt32(DaysLeft);
            writer.WriteInt32(Sentence);
            writer.WriteByte(Flags);
            writer.WriteByte(JailedPlayerId);
        }

        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt16();
            DaysLeft = reader.ReadInt32();
            Sentence = reader.ReadInt32();
            Flags = reader.ReadByte();
            JailedPlayerId = reader.ReadByte();
        }
    }
}
