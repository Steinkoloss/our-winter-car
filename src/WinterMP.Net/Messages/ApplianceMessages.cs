namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: shared kitchen-appliance state (COVERAGE-ROADMAP 6.1 / 6.4 / R2.3).
    /// An unattended oven/stove is a house-fire hazard; the hotplate heats, fire-hazard sim
    /// and fuse run per-client. The <b>host</b> owns each appliance and broadcasts on change
    /// + join. Ignition (v85) is edge-carried: the "Start fire N" commit states are one-frame
    /// transients a poll can never see, so the host hooks them and bumps <c>FireCount</c>
    /// (with the igniting plate in <c>FirePlate</c>); a guest replays that plate's ignition
    /// when the count moves. FlagFire (level-sampled) is kept but is nearly always false.
    /// See ApplianceSync.
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
        /// <summary>Host-side ignition counter (wraps); appended v85.</summary>
        public byte FireCount;
        /// <summary>Plate (1-4) of the latest ignition, 0 = none yet; appended v85.</summary>
        public byte FirePlate;

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
            writer.WriteByte(FireCount);
            writer.WriteByte(FirePlate);
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
            FireCount = reader.ReadByte();
            FirePlate = reader.ReadByte();
        }
    }

    /// <summary>
    /// Guest -> host (v89): the sender's own oven sim rolled an ignition (its FireHazard
    /// RNG runs per-client on the synced heats). The host replays the same plate's
    /// ignition-commit state on its authoritative oven, so the shared house's fire is
    /// single-sourced and streams back to everyone via <see cref="ApplianceState"/>
    /// FireCount — the moose-kill report pattern. See ApplianceSync.
    /// </summary>
    public sealed class ApplianceFireReport : IMessage
    {
        public uint ApplianceId;
        public byte Plate;
        public byte PlayerId;
        public ushort Sequence;

        public MessageId Id => MessageId.ApplianceFireReport;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(ApplianceId);
            writer.WriteByte(Plate);
            writer.WriteByte(PlayerId);
            writer.WriteUInt16(Sequence);
        }

        public void Read(NetReader reader)
        {
            ApplianceId = reader.ReadUInt32();
            Plate = reader.ReadByte();
            PlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
        }
    }
}
