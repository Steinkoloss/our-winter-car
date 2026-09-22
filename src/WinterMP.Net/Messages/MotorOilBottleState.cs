namespace WinterMP.Net.Messages
{
    public sealed class MotorOilBottleState : IMessage
    {
        public uint ItemId, Revision;
        public string NativeId = string.Empty;
        public float Fluid, Viscosity;
        public byte Grade;
        public bool Empty;
        public NetVector3 Position;
        public NetQuaternion Rotation = NetQuaternion.Identity;
        public MessageId Id => MessageId.MotorOilBottleState;
        public void Write(NetWriter w)
        {
            Validate();
            w.WriteUInt32(ItemId); w.WriteUInt32(Revision); w.WriteString(NativeId);
            w.WriteSingle(Fluid); w.WriteSingle(Viscosity); w.WriteByte(Grade); w.WriteBool(Empty);
            w.WriteVector3(Position); w.WriteQuaternion(Rotation);
        }
        public void Read(NetReader r)
        {
            ItemId=r.ReadUInt32(); Revision=r.ReadUInt32(); NativeId=r.ReadString();
            Fluid=r.ReadSingle(); Viscosity=r.ReadSingle(); Grade=r.ReadByte();
            byte empty=r.ReadByte(); if(empty>1)throw new ProtocolException("Invalid oil empty flag.");
            Empty=empty!=0; Position=r.ReadVector3(); Rotation=r.ReadQuaternion(); Validate();
        }
        private void Validate() { if(!Sync.MotorOilPolicy.Valid(this))throw new ProtocolException("Invalid motor-oil bottle."); }
    }
}
