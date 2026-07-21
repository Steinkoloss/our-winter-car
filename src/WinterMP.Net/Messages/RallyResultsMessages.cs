namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: the rally (Suvi-Sprint) results ledger, enrollment and penalties
    /// (COVERAGE-ROADMAP 5.1). <c>RallySync</c> syncs only stage timing; the
    /// <c>ResultsWeekend</c> scoring/placement, <c>RegisterRally</c> enroll and
    /// <c>ParcFerme</c> penalties are unsynced, so peers see different standings and rewards.
    /// The <b>host</b> owns the board: it computes placement from the stage times it already
    /// owns and broadcasts the standings + enroll + penalty on change + join; guests apply.
    /// Reward payout rides the existing host-gated race price triggers (v50). See
    /// RallyResultsSync.
    /// </summary>
    public sealed class RallyResultsState : IMessage
    {
        public const byte FlagRaceOver = 1;
        public const byte FlagWinner = 2;
        public const byte FlagRegistered = 4;
        public const byte FlagSecondDay = 8;

        public ushort Sequence;
        public int TimeSS1;
        public int TimeSS2;
        public int TimeSS3;
        public float PlayerTimeTotal;
        public int PlayerClassLevel;
        public float TimePenalty;
        public byte Flags;

        public MessageId Id => MessageId.RallyResultsState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16(Sequence);
            writer.WriteInt32(TimeSS1);
            writer.WriteInt32(TimeSS2);
            writer.WriteInt32(TimeSS3);
            writer.WriteSingle(PlayerTimeTotal);
            writer.WriteInt32(PlayerClassLevel);
            writer.WriteSingle(TimePenalty);
            writer.WriteByte(Flags);
        }

        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt16();
            TimeSS1 = reader.ReadInt32();
            TimeSS2 = reader.ReadInt32();
            TimeSS3 = reader.ReadInt32();
            PlayerTimeTotal = reader.ReadSingle();
            PlayerClassLevel = reader.ReadInt32();
            TimePenalty = reader.ReadSingle();
            Flags = reader.ReadByte();
        }
    }
}
