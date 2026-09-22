namespace WinterMP.Net.Messages
{
    public enum CoffeeAction : byte { OpenLid = 1, CloseLid = 2, FillCup = 3, Drink = 4 }
    public sealed class CoffeeIntent : IMessage
    {
        public uint ItemId, Sequence;
        public byte PlayerId;
        public CoffeeAction Action;
        public MessageId Id => MessageId.CoffeeIntent;
        public void Write(NetWriter w) { Validate(); w.WriteUInt32(ItemId); w.WriteUInt32(Sequence); w.WriteByte(PlayerId); w.WriteByte((byte)Action); }
        public void Read(NetReader r) { ItemId = r.ReadUInt32(); Sequence = r.ReadUInt32(); PlayerId = r.ReadByte(); Action = (CoffeeAction)r.ReadByte(); Validate(); }
        private void Validate() { if (!Sync.CoffeePolicy.Valid(this)) throw new ProtocolException("Invalid coffee intent."); }
    }
    public sealed class CoffeeState : IMessage
    {
        // Kind: pot, household cup, ground-coffee packet. Flags: lid open, boiling sound.
        public uint ItemId, Revision;
        public byte Kind, Flags;
        public float Water, Ground, Coffee, Caffeine, BoilVolume;
        public NetVector3 Position;
        public NetQuaternion Rotation = NetQuaternion.Identity;
        public MessageId Id => MessageId.CoffeeState;
        public void Write(NetWriter w) { Validate(); w.WriteUInt32(ItemId); w.WriteUInt32(Revision); w.WriteByte(Kind); w.WriteByte(Flags); w.WriteSingle(Water); w.WriteSingle(Ground); w.WriteSingle(Coffee); w.WriteSingle(Caffeine); w.WriteSingle(BoilVolume); w.WriteVector3(Position); w.WriteQuaternion(Rotation); }
        public void Read(NetReader r) { ItemId = r.ReadUInt32(); Revision = r.ReadUInt32(); Kind = r.ReadByte(); Flags = r.ReadByte(); Water = r.ReadSingle(); Ground = r.ReadSingle(); Coffee = r.ReadSingle(); Caffeine = r.ReadSingle(); BoilVolume = r.ReadSingle(); Position = r.ReadVector3(); Rotation = r.ReadQuaternion(); Validate(); }
        private void Validate() { if (!Sync.CoffeePolicy.Valid(this)) throw new ProtocolException("Invalid coffee state."); }
    }
    public sealed class CoffeeDrinkResult : IMessage
    {
        public uint ItemId, Sequence;
        public byte PlayerId;
        public float Amount, Caffeine;
        public MessageId Id => MessageId.CoffeeDrinkResult;
        public void Write(NetWriter w) { Validate(); w.WriteUInt32(ItemId); w.WriteUInt32(Sequence); w.WriteByte(PlayerId); w.WriteSingle(Amount); w.WriteSingle(Caffeine); }
        public void Read(NetReader r) { ItemId = r.ReadUInt32(); Sequence = r.ReadUInt32(); PlayerId = r.ReadByte(); Amount = r.ReadSingle(); Caffeine = r.ReadSingle(); Validate(); }
        private void Validate() { if (!Sync.CoffeePolicy.Valid(this)) throw new ProtocolException("Invalid coffee drink result."); }
    }
}
