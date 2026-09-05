namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Current fitted engine-part condition, streamed by the owner through the host.
    /// The host owns parked cars. Only concrete failures are replayed; SEIZE/CAMFAIL
    /// are random selectors, not durable breakage. Known wear values also carry repairs
    /// and clear old damage. Unbound slots preserve the previous condition.
    /// </summary>
    public sealed class VehicleDamage : IMessage
    {
        public const uint Bearing1 = 1u << 0;
        public const uint Bearing2 = 1u << 1;
        public const uint Bearing3 = 1u << 2;
        public const uint Bearing4 = 1u << 3;
        public const uint Bearing5 = 1u << 4;
        public const uint Crankshaft = 1u << 5;
        public const uint Headgasket = 1u << 6;
        public const uint Piston1 = 1u << 7;
        public const uint Piston2 = 1u << 8;
        public const uint Piston3 = 1u << 9;
        public const uint Piston4 = 1u << 10;
        public const uint Oilpan = 1u << 11;
        public const uint Timingbelt = 1u << 12;
        public const uint Seize = 1u << 13;
        public const uint Block = 1u << 14;
        public const uint Camfail = 1u << 15;
        // Seize/Camfail are retired trigger bits: replaying either rerolls random
        // damage. Their resulting concrete part failures occupy the other slots.
        public const uint ConcretePartsMask = 0x5FFF;
        public const int PartSlots = 16;

        public uint VehicleId;
        public byte OwnerPlayerId;
        public uint DamageMask;
        public ushort Sequence;
        public uint KnownPartsMask;
        public float[] Wear = new float[PartSlots];

        public MessageId Id => MessageId.VehicleDamage;

        public void Write(NetWriter writer)
        {
            if (Wear == null || Wear.Length != PartSlots)
                throw new ProtocolException("VehicleDamage requires 16 wear slots.");
            writer.WriteUInt32(VehicleId);
            writer.WriteByte(OwnerPlayerId);
            writer.WriteUInt32(DamageMask);
            writer.WriteUInt16(Sequence);
            writer.WriteUInt32(KnownPartsMask);
            for (int i = 0; i < PartSlots; i++) writer.WriteSingle(Wear[i]);
        }

        public void Read(NetReader reader)
        {
            VehicleId = reader.ReadUInt32();
            OwnerPlayerId = reader.ReadByte();
            DamageMask = reader.ReadUInt32();
            Sequence = reader.ReadUInt16();
            KnownPartsMask = reader.ReadUInt32();
            Wear = new float[PartSlots];
            for (int i = 0; i < PartSlots; i++) Wear[i] = reader.ReadSingle();
        }
    }
}
