namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: JOKKIS banger-race lifecycle (COVERAGE-ROADMAP 5.3). The JOKKIS car is
    /// vehicle-synced, but its <c>DB/RaceTrigger :: Data</c> lap/time/checkpoint progress runs
    /// per-client. The structure is identical to CORRIS (which <see cref="IceRaceState"/>
    /// hooks). Rather than refactor the CORRIS-coupled IceRaceSync, JOKKIS is host-authoritative
    /// here: the host broadcasts the lap/time/checkpoint state and guests apply it, so both see
    /// the same standings. (This complements IceRaceSync's documented limitation that a
    /// host-driven ice race doesn't sync — here the host drive IS the authority.) See
    /// JokkisRaceSync.
    /// </summary>
    public sealed class JokkisRaceState : IMessage
    {
        public const byte FlagCheckpoint1 = 1;
        public const byte FlagCheckpoint2 = 2;

        public ushort Sequence;
        public int Laps;
        /// <summary>Race time in centiseconds.</summary>
        public int TimeCentiseconds;
        public byte Flags;

        public MessageId Id => MessageId.JokkisRaceState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16(Sequence);
            writer.WriteInt32(Laps);
            writer.WriteInt32(TimeCentiseconds);
            writer.WriteByte(Flags);
        }

        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt16();
            Laps = reader.ReadInt32();
            TimeCentiseconds = reader.ReadInt32();
            Flags = reader.ReadByte();
        }
    }
}
