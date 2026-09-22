namespace WinterMP.Net.Messages
{
    public sealed class TrailerBodyPose
    {
        public NetVector3 Position, Velocity, AngularVelocity;
        public NetQuaternion Rotation = NetQuaternion.Identity;
    }

    public sealed class TractorTrailerState : IMessage
    {
        public uint Revision;
        public byte Owner;
        public bool Attached;
        public NetVector3 ConnectedAnchor;
        public TrailerBodyPose[] Bodies = TractorTrailerMotion.EmptyBodies();
        public MessageId Id => MessageId.TractorTrailerState;
        public void Write(NetWriter w)
        {
            Validate(); w.WriteUInt32(Revision); w.WriteByte(Owner); w.WriteBool(Attached); w.WriteVector3(ConnectedAnchor); TractorTrailerMotion.WriteBodies(w, Bodies);
        }
        public void Read(NetReader r)
        {
            Revision = r.ReadUInt32(); Owner = r.ReadByte(); Attached = r.ReadBool(); ConnectedAnchor = r.ReadVector3(); Bodies = TractorTrailerMotion.ReadBodies(r); Validate();
        }
        private void Validate() { if (!Sync.TractorTrailerPolicy.Valid(this)) throw new ProtocolException("Invalid tractor trailer state."); }
    }

    public sealed class TractorTrailerIntent : IMessage
    {
        public byte PlayerId;
        public uint Revision, Sequence;
        public MessageId Id => MessageId.TractorTrailerIntent;
        public void Write(NetWriter w) { Validate(); w.WriteByte(PlayerId); w.WriteUInt32(Revision); w.WriteUInt32(Sequence); }
        public void Read(NetReader r) { PlayerId = r.ReadByte(); Revision = r.ReadUInt32(); Sequence = r.ReadUInt32(); Validate(); }
        private void Validate() { if (!Sync.TractorTrailerPolicy.Valid(this)) throw new ProtocolException("Invalid trailer release intent."); }
    }

    public sealed class TractorTrailerMotion : IMessage
    {
        public uint Revision, Sequence;
        public byte Owner;
        // Fixed native order: chassis, tipping bed, detached hitch support.
        public TrailerBodyPose[] Bodies = EmptyBodies();
        public MessageId Id => MessageId.TractorTrailerMotion;
        public void Write(NetWriter w) { Validate(); w.WriteUInt32(Revision); w.WriteUInt32(Sequence); w.WriteByte(Owner); WriteBodies(w, Bodies); }
        public void Read(NetReader r) { Revision = r.ReadUInt32(); Sequence = r.ReadUInt32(); Owner = r.ReadByte(); Bodies = ReadBodies(r); Validate(); }
        private void Validate() { if (!Sync.TractorTrailerPolicy.Valid(this)) throw new ProtocolException("Invalid trailer motion."); }
        internal static TrailerBodyPose[] EmptyBodies() => new[] { new TrailerBodyPose(), new TrailerBodyPose(), new TrailerBodyPose() };
        internal static void WriteBodies(NetWriter w, TrailerBodyPose[] bodies)
        {
            foreach (var b in bodies) { w.WriteVector3(b.Position); w.WriteQuaternion(b.Rotation); w.WriteVector3(b.Velocity); w.WriteVector3(b.AngularVelocity); }
        }
        internal static TrailerBodyPose[] ReadBodies(NetReader r)
        {
            var bodies = EmptyBodies();
            for (int i = 0; i < bodies.Length; i++) bodies[i] = new TrailerBodyPose { Position = r.ReadVector3(), Rotation = r.ReadQuaternion(), Velocity = r.ReadVector3(), AngularVelocity = r.ReadVector3() };
            return bodies;
        }
    }
}
