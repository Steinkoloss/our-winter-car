namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Owner -> host -> peers: the accumulated engine part-breakage set of one vehicle.
    /// The <c>PartBreakages :: Damages</c> FSM rolls its own <c>Chance</c> on every client,
    /// so the shared car's engine seizes for one player and runs fine for another. Here the
    /// vehicle <b>owner (driver)</b> is authoritative: non-owners zero their local roll and
    /// apply this mask by firing the matching breakage event, so a part breaks once and both
    /// peers + late joiners agree on the broken set. Re-sent on change + keepalive (a joiner
    /// converges within the keepalive window). Bits are the breakage events on that FSM.
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

        public uint VehicleId;
        public byte OwnerPlayerId;
        public uint DamageMask;
        public ushort Sequence;

        public MessageId Id => MessageId.VehicleDamage;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(VehicleId);
            writer.WriteByte(OwnerPlayerId);
            writer.WriteUInt32(DamageMask);
            writer.WriteUInt16(Sequence);
        }

        public void Read(NetReader reader)
        {
            VehicleId = reader.ReadUInt32();
            OwnerPlayerId = reader.ReadByte();
            DamageMask = reader.ReadUInt32();
            Sequence = reader.ReadUInt16();
        }
    }
}
