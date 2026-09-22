namespace WinterMP.Net.Messages
{
    public sealed class TrainState : IMessage
    {
        public uint Sequence, HornSequence;
        // Phases: westbound, west wait, eastbound, east wait. Flags: root, mesh, lights.
        public byte Phase, Flags;
        public ushort ColliderMask;
        public NetVector3 Position, Velocity;
        public NetQuaternion Rotation = NetQuaternion.Identity;
        public float Volume;
        public MessageId Id => MessageId.TrainState;
        public void Write(NetWriter w)
        {
            Validate(); w.WriteUInt32(Sequence); w.WriteByte(Phase); w.WriteByte(Flags); w.WriteUInt16(ColliderMask);
            w.WriteUInt32(HornSequence); w.WriteVector3(Position); w.WriteQuaternion(Rotation); w.WriteVector3(Velocity); w.WriteSingle(Volume);
        }
        public void Read(NetReader r)
        {
            Sequence = r.ReadUInt32(); Phase = r.ReadByte(); Flags = r.ReadByte(); ColliderMask = r.ReadUInt16();
            HornSequence = r.ReadUInt32(); Position = r.ReadVector3(); Rotation = r.ReadQuaternion(); Velocity = r.ReadVector3(); Volume = r.ReadSingle(); Validate();
        }
        private void Validate() { if (!Sync.TrainPolicy.Valid(this)) throw new ProtocolException("Invalid train state."); }
    }
}
