namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: shared hitchhiker state (COVERAGE-ROADMAP 3.4). Which hiker variant
    /// spawns (ordinary vs. the dangerous KiljuMurderer / suicide) and its drunk/story stage
    /// branch per-client, so peers pick up a different hiker; the ride payout is local too.
    /// The <b>host</b> owns the hiker: it broadcasts the stage + variant + paid flag on change
    /// + join; guests apply it (the body pose streams over <see cref="NpcTransform"/> via the
    /// ScriptedMover path). Ride payout reaches the shared wallet through the host. See
    /// HitchhikerSync.
    /// </summary>
    public sealed class HitchhikerState : IMessage
    {
        public const byte FlagPaid = 1;
        public const byte FlagAngryKilju = 2;   // KiljuMurderer variant
        public const byte FlagSuicide = 4;
        public const byte FlagActive = 8;

        public ushort Sequence;
        public int DrunkStage;
        public int MovingStage;
        public int Money;
        public byte Flags;

        public MessageId Id => MessageId.HitchhikerState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16(Sequence);
            writer.WriteInt32(DrunkStage);
            writer.WriteInt32(MovingStage);
            writer.WriteInt32(Money);
            writer.WriteByte(Flags);
        }

        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt16();
            DrunkStage = reader.ReadInt32();
            MovingStage = reader.ReadInt32();
            Money = reader.ReadInt32();
            Flags = reader.ReadByte();
        }
    }
}
