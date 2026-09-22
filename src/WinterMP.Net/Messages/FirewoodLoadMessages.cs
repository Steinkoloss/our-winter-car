namespace WinterMP.Net.Messages
{
    public sealed class FirewoodPile
    {
        public NetVector3 Position;
        public NetQuaternion Rotation = NetQuaternion.Identity;
        public float Scale;
    }

    public sealed class FirewoodLoadState : IMessage
    {
        public uint Revision, Epoch;
        public float Logs, Firewood, BedScale, Unloaded;
        public float Mass = 400;
        public bool Unloading;
        public FirewoodPile[] Piles = new FirewoodPile[0];
        public MessageId Id => MessageId.FirewoodLoadState;
        public void Write(NetWriter w)
        {
            if (!Sync.FirewoodLoadPolicy.Valid(this)) throw new ProtocolException("Invalid firewood load.");
            w.WriteUInt32(Revision); w.WriteUInt32(Epoch);
            w.WriteSingle(Logs); w.WriteSingle(Firewood); w.WriteSingle(Mass); w.WriteSingle(BedScale); w.WriteSingle(Unloaded);
            w.WriteByte(Unloading ? (byte)1 : (byte)0); w.WriteByte((byte)Piles.Length);
            foreach (var p in Piles) { w.WriteVector3(p.Position); w.WriteQuaternion(p.Rotation); w.WriteSingle(p.Scale); }
        }
        public void Read(NetReader r)
        {
            Revision = r.ReadUInt32(); Epoch = r.ReadUInt32();
            Logs = r.ReadSingle(); Firewood = r.ReadSingle(); Mass = r.ReadSingle(); BedScale = r.ReadSingle(); Unloaded = r.ReadSingle();
            byte flag = r.ReadByte(); if (flag > 1) throw new ProtocolException("Invalid unloading flag.");
            Unloading = flag == 1; int count = r.ReadByte();
            if (count > Sync.FirewoodLoadPolicy.MaxPiles) throw new ProtocolException("Too many firewood piles.");
            Piles = new FirewoodPile[count];
            for (int i = 0; i < count; i++) Piles[i] = new FirewoodPile { Position = r.ReadVector3(), Rotation = r.ReadQuaternion(), Scale = r.ReadSingle() };
            if (!Sync.FirewoodLoadPolicy.Valid(this)) throw new ProtocolException("Invalid firewood load.");
        }
    }

    public sealed class FirewoodUnloadIntent : IMessage
    {
        public byte PlayerId;
        public uint Epoch, Sequence;
        public bool Unload;
        public MessageId Id => MessageId.FirewoodUnloadIntent;
        public void Write(NetWriter w)
        {
            if (PlayerId == 255) throw new ProtocolException("Invalid wood unloading player.");
            w.WriteByte(PlayerId); w.WriteUInt32(Epoch); w.WriteUInt32(Sequence); w.WriteByte(Unload ? (byte)1 : (byte)0);
        }
        public void Read(NetReader r)
        {
            PlayerId = r.ReadByte(); Epoch = r.ReadUInt32(); Sequence = r.ReadUInt32(); byte flag = r.ReadByte();
            if (PlayerId == 255 || flag > 1) throw new ProtocolException("Invalid wood unloading request.");
            Unload = flag == 1;
        }
    }
}
