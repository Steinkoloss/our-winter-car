namespace WinterMP.Net.Messages
{
    public sealed class ContainerFuelIntent : IMessage
    {
        public uint SourceId, VehicleId, Sequence;
        public byte PlayerId;
        public float Amount;
        public MessageId Id => MessageId.ContainerFuelIntent;
        public void Write(NetWriter w) { w.WriteUInt32(SourceId); w.WriteUInt32(VehicleId); w.WriteByte(PlayerId); w.WriteUInt32(Sequence); w.WriteSingle(Amount); }
        public void Read(NetReader r) { SourceId=r.ReadUInt32(); VehicleId=r.ReadUInt32(); PlayerId=r.ReadByte(); Sequence=r.ReadUInt32(); Amount=r.ReadSingle(); }
    }

    public sealed class ContainerFuelResult : IMessage
    {
        public uint SourceId, VehicleId, Sequence, Revision;
        public byte PlayerId;
        public float AcceptedAmount, SourceLevel, DestinationLevel;
        public MessageId Id => MessageId.ContainerFuelResult;
        public void Write(NetWriter w) {
            w.WriteUInt32(SourceId); w.WriteUInt32(VehicleId); w.WriteByte(PlayerId); w.WriteUInt32(Sequence);
            w.WriteUInt32(Revision); w.WriteSingle(AcceptedAmount); w.WriteSingle(SourceLevel); w.WriteSingle(DestinationLevel);
        }
        public void Read(NetReader r) {
            SourceId=r.ReadUInt32(); VehicleId=r.ReadUInt32(); PlayerId=r.ReadByte(); Sequence=r.ReadUInt32();
            Revision=r.ReadUInt32(); AcceptedAmount=r.ReadSingle(); SourceLevel=r.ReadSingle(); DestinationLevel=r.ReadSingle();
        }
    }
}
