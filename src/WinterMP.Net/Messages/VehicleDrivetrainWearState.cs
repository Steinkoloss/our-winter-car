namespace WinterMP.Net.Messages
{
    /// <summary>Host-owned native saved wear, independent of the vehicle's driver.</summary>
    public sealed class VehicleDrivetrainWearState : IMessage
    {
        public const byte Available = 1;
        public uint VehicleId;
        public uint Revision;
        public byte Flags;
        public float DriveshaftWear, GearboxWear, RearAxleWear;
        public bool GearboxOilAvailable;
        public float GearboxOilLevel;
        public MessageId Id => MessageId.VehicleDrivetrainWearState;

        public bool Valid => VehicleId != 0 && (Flags == 0 || Flags == Available)
            && Finite(DriveshaftWear) && Finite(GearboxWear) && Finite(RearAxleWear)
            && (Flags != 0 || DriveshaftWear == 0 && GearboxWear == 0 && RearAxleWear == 0)
            && Finite(GearboxOilLevel) && (GearboxOilAvailable ? Flags == Available : GearboxOilLevel == 0);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public bool SameWear(VehicleDrivetrainWearState state) => VehicleId == state.VehicleId && Flags == state.Flags
            && DriveshaftWear == state.DriveshaftWear && GearboxWear == state.GearboxWear && RearAxleWear == state.RearAxleWear
            && GearboxOilAvailable == state.GearboxOilAvailable && GearboxOilLevel == state.GearboxOilLevel;
        public VehicleDrivetrainWearState Copy() => new VehicleDrivetrainWearState { VehicleId = VehicleId, Revision = Revision,
            Flags = Flags, DriveshaftWear = DriveshaftWear, GearboxWear = GearboxWear, RearAxleWear = RearAxleWear,
            GearboxOilAvailable = GearboxOilAvailable, GearboxOilLevel = GearboxOilLevel };

        public void Write(NetWriter writer)
        {
            if (!Valid) throw new ProtocolException("Invalid drivetrain wear state.");
            writer.WriteUInt32(VehicleId); writer.WriteUInt32(Revision); writer.WriteByte(Flags);
            writer.WriteSingle(DriveshaftWear); writer.WriteSingle(GearboxWear); writer.WriteSingle(RearAxleWear);
            writer.WriteByte(GearboxOilAvailable ? (byte)1 : (byte)0); writer.WriteSingle(GearboxOilLevel);
        }
        public void Read(NetReader reader)
        {
            VehicleId = reader.ReadUInt32(); Revision = reader.ReadUInt32(); Flags = reader.ReadByte();
            DriveshaftWear = reader.ReadSingle(); GearboxWear = reader.ReadSingle(); RearAxleWear = reader.ReadSingle();
            byte oil = reader.ReadByte();
            if (oil > 1) throw new ProtocolException("Invalid gearbox oil availability.");
            GearboxOilAvailable = oil == 1; GearboxOilLevel = reader.ReadSingle();
            if (!Valid) throw new ProtocolException("Invalid drivetrain wear state.");
        }
    }
}
