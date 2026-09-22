namespace WinterMP.Net.Messages
{
    public enum HouseholdFuseAction : byte { InsertFuse, FitHolder, RemoveHolder, Tighten, Loosen }

    public sealed class HouseholdFuseHolder
    {
        public uint ControlRevision = 1;
        public byte Slot = 255, Fuse, Tightness, Flags;
        public NetVector3 Position;
        public NetQuaternion Rotation = NetQuaternion.Identity;
    }

    public sealed class HouseholdFuseState : IMessage
    {
        public uint Revision;
        public ushort PowerMask;
        public HouseholdFuseHolder[] Holders = new HouseholdFuseHolder[11];
        public HouseholdFuseState() { for (int i = 0; i < Holders.Length; i++) Holders[i] = new HouseholdFuseHolder(); }
        public MessageId Id => MessageId.HouseholdFuseState;
        public void Write(NetWriter w)
        {
            Validate(); w.WriteUInt32(Revision); w.WriteUInt16(PowerMask);
            foreach (var h in Holders)
            { w.WriteUInt32(h.ControlRevision); w.WriteByte(h.Slot); w.WriteByte(h.Fuse); w.WriteByte(h.Tightness); w.WriteByte(h.Flags); w.WriteVector3(h.Position); w.WriteQuaternion(h.Rotation); }
        }
        public void Read(NetReader r)
        {
            Revision = r.ReadUInt32(); PowerMask = r.ReadUInt16(); Holders = new HouseholdFuseHolder[11];
            for (int i = 0; i < Holders.Length; i++) Holders[i] = new HouseholdFuseHolder { ControlRevision = r.ReadUInt32(), Slot = r.ReadByte(),
                Fuse = r.ReadByte(), Tightness = r.ReadByte(), Flags = r.ReadByte(), Position = r.ReadVector3(), Rotation = r.ReadQuaternion() };
            Validate();
        }
        private void Validate() { if (!Sync.HouseholdFusePolicy.Valid(this)) throw new ProtocolException("Invalid household fuse state."); }
    }

    public sealed class HouseholdFuseIntent : IMessage
    {
        public byte PlayerId, Holder, Slot = 255;
        public uint Sequence, ControlRevision, ItemId;
        public HouseholdFuseAction Action;
        public MessageId Id => MessageId.HouseholdFuseIntent;
        public void Write(NetWriter w)
        { Validate(); w.WriteByte(PlayerId); w.WriteUInt32(Sequence); w.WriteUInt32(ControlRevision); w.WriteByte(Holder); w.WriteByte(Slot); w.WriteByte((byte)Action); w.WriteUInt32(ItemId); }
        public void Read(NetReader r)
        { PlayerId = r.ReadByte(); Sequence = r.ReadUInt32(); ControlRevision = r.ReadUInt32(); Holder = r.ReadByte(); Slot = r.ReadByte(); Action = (HouseholdFuseAction)r.ReadByte(); ItemId = r.ReadUInt32(); Validate(); }
        private void Validate() { if (!Sync.HouseholdFusePolicy.Valid(this)) throw new ProtocolException("Invalid household fuse intent."); }
    }

    public sealed class HouseholdFuseResult : IMessage
    {
        public byte PlayerId, Holder;
        public uint Sequence;
        public bool Accepted, Shock;
        public MessageId Id => MessageId.HouseholdFuseResult;
        public void Write(NetWriter w) { Validate(); w.WriteByte(PlayerId); w.WriteUInt32(Sequence); w.WriteByte(Holder); w.WriteBool(Accepted); w.WriteBool(Shock); }
        public void Read(NetReader r) { PlayerId = r.ReadByte(); Sequence = r.ReadUInt32(); Holder = r.ReadByte(); Accepted = r.ReadBool(); Shock = r.ReadBool(); Validate(); }
        private void Validate() { if (PlayerId == 255 || Holder >= 11 || Sequence == 0 || Shock && !Accepted) throw new ProtocolException("Invalid household fuse result."); }
    }
}
