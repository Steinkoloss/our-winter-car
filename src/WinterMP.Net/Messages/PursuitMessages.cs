namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: police pursuit state (COVERAGE-ROADMAP 4.4). Whether a cop car is
    /// chasing, and its siren, are decided per-client, so a pursuit that is on for one player is
    /// absent for the other. The cop cars already stream their pose over
    /// <see cref="NpcTransform"/> (host-sim, guest AI frozen); this adds the host-owned
    /// chase-active + siren flags per car so the pursuit and sirens agree. DUI stop escalation
    /// runs through the shared wanted/jail records (4.1/4.2), not just the fine bit. See
    /// PursuitSync.
    /// </summary>
    public sealed class PursuitState : IMessage
    {
        public const byte FlagCar1Chase = 1;
        public const byte FlagCar2Chase = 2;
        public const byte FlagCar1Siren = 4;
        public const byte FlagCar2Siren = 8;

        public ushort Sequence;
        public byte Flags;

        public MessageId Id => MessageId.PursuitState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16(Sequence);
            writer.WriteByte(Flags);
        }

        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt16();
            Flags = reader.ReadByte();
        }
    }
}
