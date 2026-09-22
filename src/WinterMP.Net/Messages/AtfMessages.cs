namespace WinterMP.Net.Messages
{
    public sealed class AtfBottleState : IMessage
    {
        public uint ItemId, Revision;
        public string NativeId = string.Empty;
        public float Fluid;
        public bool Empty;
        public NetVector3 Position;
        public NetQuaternion Rotation;
        public MessageId Id => MessageId.AtfBottleState;
        public void Write(NetWriter w)
        {
            if (!Sync.AtfPolicy.Valid(this)) throw new ProtocolException("Invalid ATF bottle state.");
            w.WriteUInt32(ItemId); w.WriteUInt32(Revision); w.WriteString(NativeId);
            w.WriteSingle(Fluid); w.WriteBool(Empty); w.WriteVector3(Position); w.WriteQuaternion(Rotation);
        }
        public void Read(NetReader r)
        {
            ItemId = r.ReadUInt32(); Revision = r.ReadUInt32(); NativeId = r.ReadString(); Fluid = r.ReadSingle();
            byte empty = r.ReadByte();
            if (empty > 1) throw new ProtocolException("Invalid ATF empty flag.");
            Empty = empty != 0; Position = r.ReadVector3(); Rotation = r.ReadQuaternion();
            if (!Sync.AtfPolicy.Valid(this)) throw new ProtocolException("Invalid ATF bottle state.");
        }
    }

    public sealed class AtfFillerState : IMessage
    {
        public const byte FlagAvailable = 1;
        public uint VehicleId, Revision;
        public float Rotation, OilLevel;
        public byte Flags;
        public NetVector3 CapLocalPosition;
        public NetQuaternion CapLocalRotation;
        public bool Available => (Flags & FlagAvailable) != 0;
        public MessageId Id => MessageId.AtfFillerState;
        public void Write(NetWriter w)
        {
            if (!Sync.AtfPolicy.Valid(this)) throw new ProtocolException("Invalid ATF filler state.");
            w.WriteUInt32(VehicleId); w.WriteUInt32(Revision); w.WriteSingle(Rotation); w.WriteSingle(OilLevel); w.WriteByte(Flags);
            w.WriteVector3(CapLocalPosition); w.WriteQuaternion(CapLocalRotation);
        }
        public void Read(NetReader r)
        {
            VehicleId = r.ReadUInt32(); Revision = r.ReadUInt32(); Rotation = r.ReadSingle(); OilLevel = r.ReadSingle(); Flags = r.ReadByte();
            CapLocalPosition = r.ReadVector3(); CapLocalRotation = r.ReadQuaternion();
            if (!Sync.AtfPolicy.Valid(this)) throw new ProtocolException("Invalid ATF filler state.");
        }
    }

    public sealed class AtfRefillIntent : IMessage
    {
        public const byte StopPour = 0, Pour = 1, Unscrew = 2, Screw = 3;
        public uint VehicleId, BottleId;
        public byte PlayerId, Action;
        public ushort Sequence;
        public MessageId Id => MessageId.AtfRefillIntent;
        public void Write(NetWriter w)
        {
            if (!Sync.AtfPolicy.Valid(this)) throw new ProtocolException("Invalid ATF refill intent.");
            w.WriteUInt32(VehicleId); w.WriteUInt32(BottleId); w.WriteByte(PlayerId); w.WriteUInt16(Sequence); w.WriteByte(Action);
        }
        public void Read(NetReader r)
        {
            VehicleId = r.ReadUInt32(); BottleId = r.ReadUInt32(); PlayerId = r.ReadByte(); Sequence = r.ReadUInt16(); Action = r.ReadByte();
            if (!Sync.AtfPolicy.Valid(this)) throw new ProtocolException("Invalid ATF refill intent.");
        }
    }
}
