namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Current owner -> host -> peers: available pressure, drivetrain damage and
    /// per-wheel health/discrete state. Missing native inputs remain unavailable;
    /// observers apply only declared fields. Wheel order is FL, FR, RL, RR.
    /// </summary>
    public sealed class VehicleCondition : IMessage
    {
        /// <summary>Host join/resync state; never advances live sender history.</summary>
        public const ushort SnapshotSequence = ushort.MaxValue;
        public const byte FlagPunctureFL = 1;
        public const byte FlagPunctureFR = 2;
        public const byte FlagPunctureRL = 4;
        public const byte FlagPunctureRR = 8;
        public const byte FlagRimFL = 16;
        public const byte FlagRimFR = 32;
        public const byte FlagRimRL = 64;
        public const byte FlagRimRR = 128;
        public const byte AvailablePressure = 1;
        public const byte AvailableDrivetrain = 2;
        public const byte AvailableFL = 4;
        public const byte AvailableFR = 8;
        public const byte AvailableRL = 16;
        public const byte AvailableRR = 32;
        public const byte AvailableAll = 63;

        public uint VehicleId;
        public byte OwnerPlayerId;
        public ushort Sequence;

        /// <summary>Tire pressure, rounded to hundredths of a bar (0..2.55 clamped; v96). Native Data uses kPa, one per wire unit.</summary>
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
        /// <summary>v197: known fields; zero means unavailable, not known zero. Wheel bits cover health and discrete flags together.</summary>
        public byte Availability;

        public bool HasPressure => (Availability & AvailablePressure) != 0;
        public bool HasDrivetrain => (Availability & AvailableDrivetrain) != 0;
        public bool HasWheel(int wheel) => wheel >= 0 && wheel < 4 && (Availability & (AvailableFL << wheel)) != 0;

        public MessageId Id => MessageId.VehicleCondition;

        public void Write(NetWriter writer)
        {
            if ((Availability & ~AvailableAll) != 0) throw new ProtocolException("Unknown vehicle condition availability bits.");
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
            writer.WriteByte(Availability);
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
            Availability = reader.ReadByte();
            if ((Availability & ~AvailableAll) != 0) throw new ProtocolException("Unknown vehicle condition availability bits.");
        }
    }
}
