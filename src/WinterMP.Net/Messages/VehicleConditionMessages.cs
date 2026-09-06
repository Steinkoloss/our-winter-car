namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Owner -> host -> peers: continuous drivetrain wear + per-wheel tire condition of one
    /// vehicle. Wear accrues and tires puncture per-client off local physics, so the shared
    /// car has a flat tire for one player and not the other. Like <see cref="VehicleDamage"/>
    /// this is <b>owner-authoritative</b>: non-owners apply the streamed condition (writing
    /// health/pressure, and firing PUNCTURE/RIM/FIXED on each wheel) rather than simulating
    /// their own. Low-rate scalar state, re-sent on change + keepalive so a joiner converges.
    /// Wheel order is FL, FR, RL, RR. See VehicleWorldSync.Condition.
    /// </summary>
    public sealed class VehicleCondition : IMessage
    {
        public const byte FlagPunctureFL = 1;
        public const byte FlagPunctureFR = 2;
        public const byte FlagPunctureRL = 4;
        public const byte FlagPunctureRR = 8;
        public const byte FlagRimFL = 16;
        public const byte FlagRimFR = 32;
        public const byte FlagRimRL = 64;
        public const byte FlagRimRR = 128;

        public uint VehicleId;
        public byte OwnerPlayerId;
        public ushort Sequence;

        /// <summary>Tire pressure, rounded to hundredths of a bar (0..2.55 clamped; v96).</summary>
        public byte TirePressure;
        /// <summary>Aggregate drivetrain damage indicator (GearboxDamage <c>DamageType</c>, clamped).</summary>
        public byte DrivetrainDamage;
        /// <summary>Per-wheel tire health 0..255 (FL, FR, RL, RR).</summary>
        public byte HealthFL;
        public byte HealthFR;
        public byte HealthRL;
        public byte HealthRR;
        /// <summary>Per-wheel puncture/rim bits (see Flag* constants).</summary>
        public byte Flags;

        public MessageId Id => MessageId.VehicleCondition;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(VehicleId);
            writer.WriteByte(OwnerPlayerId);
            writer.WriteUInt16(Sequence);
            writer.WriteByte(TirePressure);
            writer.WriteByte(DrivetrainDamage);
            writer.WriteByte(HealthFL);
            writer.WriteByte(HealthFR);
            writer.WriteByte(HealthRL);
            writer.WriteByte(HealthRR);
            writer.WriteByte(Flags);
        }

        public void Read(NetReader reader)
        {
            VehicleId = reader.ReadUInt32();
            OwnerPlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            TirePressure = reader.ReadByte();
            DrivetrainDamage = reader.ReadByte();
            HealthFL = reader.ReadByte();
            HealthFR = reader.ReadByte();
            HealthRL = reader.ReadByte();
            HealthRR = reader.ReadByte();
            Flags = reader.ReadByte();
        }
    }
}
