namespace WinterMP.Net.Messages
{
    public sealed class MotorOilFillerState : IMessage
    {
        public uint Revision, Epoch, HeadId, PanId;
        public float Rotation = 359, Oil, Contamination, Viscosity;
        public NetVector3 CapPosition;
        public NetQuaternion CapRotation = NetQuaternion.Identity;
        public bool Available => HeadId != 0 && PanId != 0;
        public MessageId Id => MessageId.MotorOilFillerState;
        public void Write(NetWriter w)
        {
            Validate(); w.WriteUInt32(Revision); w.WriteUInt32(Epoch); w.WriteUInt32(HeadId); w.WriteUInt32(PanId);
            w.WriteSingle(Rotation); w.WriteSingle(Oil); w.WriteSingle(Contamination); w.WriteSingle(Viscosity);
            w.WriteVector3(CapPosition); w.WriteQuaternion(CapRotation);
        }
        public void Read(NetReader r)
        {
            Revision=r.ReadUInt32(); Epoch=r.ReadUInt32(); HeadId=r.ReadUInt32(); PanId=r.ReadUInt32();
            Rotation=r.ReadSingle(); Oil=r.ReadSingle(); Contamination=r.ReadSingle(); Viscosity=r.ReadSingle();
            CapPosition=r.ReadVector3(); CapRotation=r.ReadQuaternion(); Validate();
        }
        private void Validate() { if(!Sync.MotorOilRefillPolicy.Valid(this))throw new ProtocolException("Invalid engine-oil filler."); }
    }
    public sealed class MotorOilRefillIntent : IMessage
    {
        public const byte Stop = 0, Pour = 1, Unscrew = 2, Screw = 3;
        public uint Epoch, BottleId;
        public ushort Sequence;
        public byte PlayerId, Action;
        public MessageId Id => MessageId.MotorOilRefillIntent;
        public void Write(NetWriter w)
        { Validate(); w.WriteUInt32(Epoch); w.WriteUInt32(BottleId); w.WriteUInt16(Sequence); w.WriteByte(PlayerId); w.WriteByte(Action); }
        public void Read(NetReader r)
        { Epoch=r.ReadUInt32(); BottleId=r.ReadUInt32(); Sequence=r.ReadUInt16(); PlayerId=r.ReadByte(); Action=r.ReadByte(); Validate(); }
        private void Validate() { if(!Sync.MotorOilRefillPolicy.Valid(this))throw new ProtocolException("Invalid engine-oil intent."); }
    }
}
