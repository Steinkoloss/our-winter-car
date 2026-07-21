namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: shared Kela welfare / unemployment claim state
    /// (COVERAGE-ROADMAP 3.6). The benefit calc (unemployment days, rate, weekly payout) runs
    /// per-client off the shared clock, so peers disagree on claim status and the recurring
    /// benefit income diverges. The <b>host</b> owns the claim: it reads
    /// <c>Systems/Expenses :: Kela</c> and broadcasts it on change + join; guests apply. The
    /// weekly benefit credits the shared wallet through the host's own expenses logic. See
    /// WelfareSync.
    /// </summary>
    public sealed class WelfareState : IMessage
    {
        /// <summary>An unemployment claim is active (Kela <c>Continue</c>).</summary>
        public const byte FlagClaiming = 1;

        public ushort Sequence;
        public int UnemployDays;
        public float PaidAmount;
        public float Weekly;
        public byte Flags;

        public bool Claiming => (Flags & FlagClaiming) != 0;

        public MessageId Id => MessageId.WelfareState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16(Sequence);
            writer.WriteInt32(UnemployDays);
            writer.WriteSingle(PaidAmount);
            writer.WriteSingle(Weekly);
            writer.WriteByte(Flags);
        }

        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt16();
            UnemployDays = reader.ReadInt32();
            PaidAmount = reader.ReadSingle();
            Weekly = reader.ReadSingle();
            Flags = reader.ReadByte();
        }
    }
}
