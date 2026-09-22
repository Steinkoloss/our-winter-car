namespace WinterMP.Net.Messages
{
    /// <summary>Exact host tyre-health inputs, independent of the current driver.</summary>
    public sealed class VehicleWheelHealthState : IMessage
    {
        public uint VehicleId, Revision;
        public byte Availability;
        public float HealthFL, HealthFR, HealthRL, HealthRR;
        public uint EpochFL, EpochFR, EpochRL, EpochRR;
        public MessageId Id => MessageId.VehicleWheelHealthState;
        public bool HasWheel(int wheel) => wheel >= 0 && wheel < 4 && (Availability & (1 << wheel)) != 0;
        public float Health(int wheel) => wheel == 0 ? HealthFL : wheel == 1 ? HealthFR : wheel == 2 ? HealthRL : wheel == 3 ? HealthRR : 0;
        public uint Epoch(int wheel) => wheel == 0 ? EpochFL : wheel == 1 ? EpochFR : wheel == 2 ? EpochRL : wheel == 3 ? EpochRR : 0;
        public void SetEpoch(int wheel, uint value)
        { switch (wheel) { case 0: EpochFL = value; break; case 1: EpochFR = value; break; case 2: EpochRL = value; break; case 3: EpochRR = value; break; } }
        public bool Valid
        {
            get
            {
                if (VehicleId == 0 || (Availability & ~15) != 0) return false;
                for (int i = 0; i < 4; i++)
                    if (float.IsNaN(Health(i)) || float.IsInfinity(Health(i)) || !HasWheel(i) && Health(i) != 0) return false;
                return true;
            }
        }
        public bool SameHealth(VehicleWheelHealthState other) => VehicleId == other.VehicleId && Availability == other.Availability
            && HealthFL == other.HealthFL && HealthFR == other.HealthFR && HealthRL == other.HealthRL && HealthRR == other.HealthRR;
        public bool SameState(VehicleWheelHealthState other) => SameHealth(other) && EpochFL == other.EpochFL
            && EpochFR == other.EpochFR && EpochRL == other.EpochRL && EpochRR == other.EpochRR;
        public VehicleWheelHealthState Copy() => new VehicleWheelHealthState { VehicleId = VehicleId, Revision = Revision,
            Availability = Availability, HealthFL = HealthFL, HealthFR = HealthFR, HealthRL = HealthRL, HealthRR = HealthRR,
            EpochFL = EpochFL, EpochFR = EpochFR, EpochRL = EpochRL, EpochRR = EpochRR };
        public void Write(NetWriter writer)
        {
            if (!Valid) throw new ProtocolException("Invalid wheel health state.");
            writer.WriteUInt32(VehicleId); writer.WriteUInt32(Revision); writer.WriteByte(Availability);
            writer.WriteSingle(HealthFL); writer.WriteSingle(HealthFR); writer.WriteSingle(HealthRL); writer.WriteSingle(HealthRR);
            writer.WriteUInt32(EpochFL); writer.WriteUInt32(EpochFR); writer.WriteUInt32(EpochRL); writer.WriteUInt32(EpochRR);
        }
        public void Read(NetReader reader)
        {
            VehicleId = reader.ReadUInt32(); Revision = reader.ReadUInt32(); Availability = reader.ReadByte();
            HealthFL = reader.ReadSingle(); HealthFR = reader.ReadSingle(); HealthRL = reader.ReadSingle(); HealthRR = reader.ReadSingle();
            EpochFL = reader.ReadUInt32(); EpochFR = reader.ReadUInt32(); EpochRL = reader.ReadUInt32(); EpochRR = reader.ReadUInt32();
            if (!Valid) throw new ProtocolException("Invalid wheel health state.");
        }
    }
}
