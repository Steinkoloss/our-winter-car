namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: shared kitchen-appliance state (COVERAGE-ROADMAP 6.1 / 6.4). An
    /// unattended oven/stove is a house-fire hazard; the hotplate heats, fire-hazard sim and
    /// fuse run per-client, so a fire one player leaves burning is absent on the other. The
    /// <b>host</b> owns each appliance: it broadcasts the hotplate heats + fire + fuse on change
    /// + join; guests apply them (fire hazard is deterministic from the heats, so it converges).
    /// Knob settings are reflected in the synced heats. See ApplianceSync.
    /// </summary>
    public sealed class ApplianceState : IMessage
    {
        public const byte KindOven = 0;

        public const byte FlagFire = 1;
        public const byte FlagFuseOk = 2;

        public uint ApplianceId;
        public byte Kind;
        public byte Flags;
        public byte Heat1;
        public byte Heat2;
        public byte Heat3;
        public byte Heat4;

        public bool OnFire => (Flags & FlagFire) != 0;

        public MessageId Id => MessageId.ApplianceState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(ApplianceId);
            writer.WriteByte(Kind);
            writer.WriteByte(Flags);
            writer.WriteByte(Heat1);
            writer.WriteByte(Heat2);
            writer.WriteByte(Heat3);
            writer.WriteByte(Heat4);
        }

        public void Read(NetReader reader)
        {
            ApplianceId = reader.ReadUInt32();
            Kind = reader.ReadByte();
            Flags = reader.ReadByte();
            Heat1 = reader.ReadByte();
            Heat2 = reader.ReadByte();
            Heat3 = reader.ReadByte();
            Heat4 = reader.ReadByte();
        }
    }
}
