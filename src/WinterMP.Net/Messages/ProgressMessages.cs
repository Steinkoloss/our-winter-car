namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host-authoritative fluid amount carried by a tracked fuel or liquid container.
    /// The player currently simulating the item reports changes through the host; the
    /// host relays the accepted state to the other peers.
    /// </summary>
    public sealed class FluidContainerState : IMessage
    {
        public const byte FlagPouring = 1;

        public uint ItemId;
        public byte OwnerPlayerId;
        public ushort Sequence;
        public byte Flags;
        public float Level;
        public float Capacity;

        public bool IsPouring => (Flags & FlagPouring) != 0;

        public MessageId Id => MessageId.FluidContainerState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(ItemId);
            writer.WriteByte(OwnerPlayerId);
            writer.WriteUInt16(Sequence);
            writer.WriteByte(Flags);
            writer.WriteSingle(Level);
            writer.WriteSingle(Capacity);
        }

        public void Read(NetReader reader)
        {
            ItemId = reader.ReadUInt32();
            OwnerPlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Flags = reader.ReadByte();
            Level = reader.ReadSingle();
            Capacity = reader.ReadSingle();
        }
    }

    /// <summary>
    /// Compact host snapshot for a job or market subsystem. The meaning of the fields
    /// is fixed by <see cref="WorldProgressKind"/> and documented in PROTOCOL.md.
    /// </summary>
    public sealed class WorldProgressState : IMessage
    {
        public byte Kind;
        public byte Phase;
        public ushort Sequence;
        public int Primary;
        public int Secondary;
        public int Tertiary;
        public float Value;

        public MessageId Id => MessageId.WorldProgressState;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(Kind);
            writer.WriteByte(Phase);
            writer.WriteUInt16(Sequence);
            writer.WriteInt32(Primary);
            writer.WriteInt32(Secondary);
            writer.WriteInt32(Tertiary);
            writer.WriteSingle(Value);
        }

        public void Read(NetReader reader)
        {
            Kind = reader.ReadByte();
            Phase = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Primary = reader.ReadInt32();
            Secondary = reader.ReadInt32();
            Tertiary = reader.ReadInt32();
            Value = reader.ReadSingle();
        }
    }

    /// <summary>Stable identifiers for the known host-mirrored work systems.</summary>
    public static class WorldProgressKind
    {
        public const byte Classifieds = 1;
        public const byte Factory = 2;
        public const byte MarkettiMagazine = 3;
    }
}
