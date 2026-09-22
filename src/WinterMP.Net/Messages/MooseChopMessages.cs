namespace WinterMP.Net.Messages
{
    public sealed class MooseChopIntent : IMessage
    {
        public byte PlayerId, Section, ExpectedPieces;
        public uint Corpse;
        public MessageId Id => MessageId.MooseChopIntent;
        public void Write(NetWriter w)
        {
            if (!Sync.MooseChopPolicy.Valid(this)) throw new ProtocolException("Invalid moose chop.");
            w.WriteByte(PlayerId); w.WriteUInt32(Corpse); w.WriteByte(Section); w.WriteByte(ExpectedPieces);
        }
        public void Read(NetReader r)
        {
            PlayerId = r.ReadByte(); Corpse = r.ReadUInt32(); Section = r.ReadByte(); ExpectedPieces = r.ReadByte();
            if (!Sync.MooseChopPolicy.Valid(this)) throw new ProtocolException("Invalid moose chop.");
        }
    }

    public sealed class MooseCorpseState : IMessage
    {
        public const int BodyCount = 11;
        public uint Corpse, Revision;
        public bool Dead;
        public byte FrontPieces, RearPieces;
        public NetVector3[] Positions = new NetVector3[0];
        public NetQuaternion[] Rotations = new NetQuaternion[0];
        public MessageId Id => MessageId.MooseCorpseState;
        public void Write(NetWriter w)
        {
            if (!Sync.MooseChopPolicy.Valid(this)) throw new ProtocolException("Invalid moose corpse.");
            w.WriteUInt32(Corpse); w.WriteUInt32(Revision); w.WriteByte(Dead ? (byte)1 : (byte)0);
            w.WriteByte(FrontPieces); w.WriteByte(RearPieces);
            for (int i = 0; i < Positions.Length; i++) { w.WriteVector3(Positions[i]); w.WriteQuaternion(Rotations[i]); }
        }
        public void Read(NetReader r)
        {
            Corpse = r.ReadUInt32(); Revision = r.ReadUInt32(); byte dead = r.ReadByte();
            if (dead > 1) throw new ProtocolException("Invalid corpse flag.");
            Dead = dead == 1; FrontPieces = r.ReadByte(); RearPieces = r.ReadByte();
            Positions = new NetVector3[Dead ? BodyCount : 0]; Rotations = new NetQuaternion[Positions.Length];
            for (int i = 0; i < Positions.Length; i++) { Positions[i] = r.ReadVector3(); Rotations[i] = r.ReadQuaternion(); }
            if (!Sync.MooseChopPolicy.Valid(this)) throw new ProtocolException("Invalid moose corpse.");
        }
    }
}
