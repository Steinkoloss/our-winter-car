namespace WinterMP.Net.Messages
{
    /// <summary>Host attachment for the persistent cylinder head. Loose motion uses ItemTransform.</summary>
    public sealed class CylinderHeadState : IMessage
    {
        public uint NetId, Revision, ParentId;
        public NetVector3 Position;
        public NetQuaternion Rotation = NetQuaternion.Identity;
        public float Mass;
        public bool FastenersAvailable;
        public readonly byte[] Fasteners = new byte[Sync.CylinderHeadPolicy.BoltCount];
        public float Tightness;
        public MessageId Id => MessageId.CylinderHeadState;
        public void Write(NetWriter w)
        {
            if (!Sync.CylinderHeadPolicy.Valid(this)) throw new ProtocolException("Invalid cylinder head attachment.");
            w.WriteUInt32(NetId); w.WriteUInt32(Revision); w.WriteUInt32(ParentId);
            w.WriteVector3(Position); w.WriteQuaternion(Rotation); w.WriteSingle(Mass);
            w.WriteByte(FastenersAvailable ? (byte)1 : (byte)0);
            foreach (byte value in Fasteners) w.WriteByte(value);
            w.WriteSingle(Tightness);
        }
        public void Read(NetReader r)
        {
            NetId = r.ReadUInt32(); Revision = r.ReadUInt32(); ParentId = r.ReadUInt32();
            Position = r.ReadVector3(); Rotation = r.ReadQuaternion(); Mass = r.ReadSingle();
            byte available = r.ReadByte();
            if (available > 1) throw new ProtocolException("Invalid head fastener availability.");
            FastenersAvailable = available == 1;
            for (int i = 0; i < Fasteners.Length; i++) Fasteners[i] = r.ReadByte();
            Tightness = r.ReadSingle();
            if (!Sync.CylinderHeadPolicy.Valid(this)) throw new ProtocolException("Invalid cylinder head attachment.");
        }
    }
}
