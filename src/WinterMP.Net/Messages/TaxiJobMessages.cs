namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: shared taxi-job (MACHTWAGEN) lifecycle + earnings state
    /// (COVERAGE-ROADMAP 3.2). The job stage, employment, kilometres driven and the fare
    /// account run per-client, so peers disagree on whether a job is active and what it paid.
    /// The taxi job belongs to the world, so the <b>host</b> owns it: it broadcasts the stage
    /// / employment / earnings on change + join; guests apply them. Fare payout reaches the
    /// shared wallet through the host's own payment logic. The taxi vehicle itself streams via
    /// the normal vehicle path (registered by the 0.2 structural check). See TaxiJobSync.
    /// </summary>
    public sealed class TaxiJobState : IMessage
    {
        /// <summary>The player is currently employed as a taxi driver.</summary>
        public const byte FlagEmployed = 1;

        public ushort Sequence;
        public int JobStage;
        public float Money;
        public float KMsDriven;
        public byte Flags;

        public bool Employed => (Flags & FlagEmployed) != 0;

        public MessageId Id => MessageId.TaxiJobState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16(Sequence);
            writer.WriteInt32(JobStage);
            writer.WriteSingle(Money);
            writer.WriteSingle(KMsDriven);
            writer.WriteByte(Flags);
        }

        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt16();
            JobStage = reader.ReadInt32();
            Money = reader.ReadSingle();
            KMsDriven = reader.ReadSingle();
            Flags = reader.ReadByte();
        }
    }
}
