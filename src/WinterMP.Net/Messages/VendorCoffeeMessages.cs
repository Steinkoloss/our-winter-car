namespace WinterMP.Net.Messages
{
    public enum VendorCoffeeAction : byte { Acquire = 1, Purchase = 2, Fill = 3, Drink = 4 }
    // Network serving lifecycle, not an assertion that vanilla destroys or saves a cup.
    public enum VendorCoffeeLifecycle : byte { Available = 1, Active = 2, Retired = 3 }

    public sealed class VendorCoffeeIntent : IMessage
    {
        public uint MachineId, CupId, Epoch, Generation, ExpectedRevision, Connection, Sequence;
        public byte PlayerId;
        public VendorCoffeeAction Action;
        public MessageId Id => MessageId.VendorCoffeeIntent;
        public VendorCoffeeIntent Copy() => (VendorCoffeeIntent)MemberwiseClone();
        public void Write(NetWriter w)
        {
            Validate(); w.WriteUInt32(MachineId); w.WriteUInt32(CupId); w.WriteUInt32(Epoch); w.WriteUInt32(Generation);
            w.WriteUInt32(ExpectedRevision); w.WriteUInt32(Connection); w.WriteUInt32(Sequence); w.WriteByte(PlayerId); w.WriteByte((byte)Action);
        }
        public void Read(NetReader r)
        {
            MachineId = r.ReadUInt32(); CupId = r.ReadUInt32(); Epoch = r.ReadUInt32(); Generation = r.ReadUInt32();
            ExpectedRevision = r.ReadUInt32(); Connection = r.ReadUInt32(); Sequence = r.ReadUInt32(); PlayerId = r.ReadByte(); Action = (VendorCoffeeAction)r.ReadByte(); Validate();
        }
        private void Validate() { if (!Sync.VendorCoffeePolicy.Valid(this)) throw new ProtocolException("Invalid vendor coffee intent."); }
    }

    public sealed class VendorCoffeeState : IMessage
    {
        public uint MachineId, CupId, Epoch, Generation, Revision, HolderConnection;
        public byte Holder, CompletedActions;
        public VendorCoffeeLifecycle Lifecycle;
        // Native units, supplied by a future audited adapter. No household limits.
        public float Contents;
        public NetVector3 Position;
        public NetQuaternion Rotation = NetQuaternion.Identity;
        public MessageId Id => MessageId.VendorCoffeeState;
        public VendorCoffeeState Copy() => (VendorCoffeeState)MemberwiseClone();
        public void Write(NetWriter w)
        {
            Validate(); w.WriteUInt32(MachineId); w.WriteUInt32(CupId); w.WriteUInt32(Epoch); w.WriteUInt32(Generation);
            w.WriteUInt32(Revision); w.WriteUInt32(HolderConnection); w.WriteByte(Holder); w.WriteByte(CompletedActions);
            w.WriteByte((byte)Lifecycle); w.WriteSingle(Contents); w.WriteVector3(Position); w.WriteQuaternion(Rotation);
        }
        public void Read(NetReader r)
        {
            MachineId = r.ReadUInt32(); CupId = r.ReadUInt32(); Epoch = r.ReadUInt32(); Generation = r.ReadUInt32();
            Revision = r.ReadUInt32(); HolderConnection = r.ReadUInt32(); Holder = r.ReadByte(); CompletedActions = r.ReadByte();
            Lifecycle = (VendorCoffeeLifecycle)r.ReadByte(); Contents = r.ReadSingle(); Position = r.ReadVector3(); Rotation = r.ReadQuaternion(); Validate();
        }
        private void Validate() { if (!Sync.VendorCoffeePolicy.Valid(this)) throw new ProtocolException("Invalid vendor coffee state."); }
    }

    public sealed class VendorCoffeeResult : IMessage
    {
        public uint MachineId, CupId, Epoch, Generation, Revision, Connection, Sequence;
        public byte PlayerId;
        public VendorCoffeeAction Action;
        // Correlation/consumption receipt only; no invented personal-effect opcode.
        public float Consumed;
        public MessageId Id => MessageId.VendorCoffeeResult;
        public void Write(NetWriter w)
        {
            Validate(); w.WriteUInt32(MachineId); w.WriteUInt32(CupId); w.WriteUInt32(Epoch); w.WriteUInt32(Generation);
            w.WriteUInt32(Revision); w.WriteUInt32(Connection); w.WriteUInt32(Sequence); w.WriteByte(PlayerId); w.WriteByte((byte)Action); w.WriteSingle(Consumed);
        }
        public void Read(NetReader r)
        {
            MachineId = r.ReadUInt32(); CupId = r.ReadUInt32(); Epoch = r.ReadUInt32(); Generation = r.ReadUInt32();
            Revision = r.ReadUInt32(); Connection = r.ReadUInt32(); Sequence = r.ReadUInt32(); PlayerId = r.ReadByte(); Action = (VendorCoffeeAction)r.ReadByte(); Consumed = r.ReadSingle(); Validate();
        }
        private void Validate() { if (!Sync.VendorCoffeePolicy.Valid(this)) throw new ProtocolException("Invalid vendor coffee result."); }
    }
}
