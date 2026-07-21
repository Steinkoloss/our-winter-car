namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: the shared wanted level (COVERAGE-ROADMAP 4.1). The
    /// <c>Systems/PlayerWanted :: Activate</c> crime counters + sentence accrue per-client, so
    /// peers disagree on how wanted the group is and cops dispatch inconsistently. The
    /// <b>host</b> owns the counters and broadcasts them on change + join; guests apply them.
    /// A guest's locally-observed crime reaches the host via <see cref="CrimeReport"/>. This is
    /// the foundation for arrest/jail (4.2) and pursuit (4.4). See WantedSync.
    ///
    /// Counter order matches <see cref="CrimeReport.CrimeType"/>:
    /// 0 manslaughter, 1 attempted manslaughter, 2 police evasion, 3 traffic fatality,
    /// 4 days-fines. Sentence + daysInJail follow.
    /// </summary>
    public sealed class WantedState : IMessage
    {
        public const byte FlagCousin = 1;

        public ushort Sequence;
        public int Manslaughter;
        public int AttemptedManslaughter;
        public int PoliceEvasion;
        public int TrafficFatality;
        public int DaysFines;
        public int Sentence;
        public int DaysInJail;
        public byte Flags;

        public MessageId Id => MessageId.WantedState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16(Sequence);
            writer.WriteInt32(Manslaughter);
            writer.WriteInt32(AttemptedManslaughter);
            writer.WriteInt32(PoliceEvasion);
            writer.WriteInt32(TrafficFatality);
            writer.WriteInt32(DaysFines);
            writer.WriteInt32(Sentence);
            writer.WriteInt32(DaysInJail);
            writer.WriteByte(Flags);
        }

        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt16();
            Manslaughter = reader.ReadInt32();
            AttemptedManslaughter = reader.ReadInt32();
            PoliceEvasion = reader.ReadInt32();
            TrafficFatality = reader.ReadInt32();
            DaysFines = reader.ReadInt32();
            Sentence = reader.ReadInt32();
            DaysInJail = reader.ReadInt32();
            Flags = reader.ReadByte();
        }
    }

    /// <summary>
    /// Guest -> host: a locally-observed crime the guest committed (its local
    /// <c>PlayerWanted</c> counter rose). The host validates a fresh pose, adds the delta to
    /// its authoritative counter, and the resulting <see cref="WantedState"/> carries it back,
    /// so a guest's crime is never erased by the host's broadcast.
    /// </summary>
    public sealed class CrimeReport : IMessage
    {
        public const byte CrimeManslaughter = 0;
        public const byte CrimeAttemptedManslaughter = 1;
        public const byte CrimePoliceEvasion = 2;
        public const byte CrimeTrafficFatality = 3;
        public const byte CrimeDaysFines = 4;

        public byte PlayerId;
        public ushort Sequence;
        public byte CrimeType;
        public int Delta;

        public MessageId Id => MessageId.CrimeReport;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteUInt16(Sequence);
            writer.WriteByte(CrimeType);
            writer.WriteInt32(Delta);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            CrimeType = reader.ReadByte();
            Delta = reader.ReadInt32();
        }
    }
}
