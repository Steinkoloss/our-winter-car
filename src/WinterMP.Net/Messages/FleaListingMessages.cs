using System.Collections.Generic;
using WinterMP.Net.Sync;

namespace WinterMP.Net.Messages
{
    public sealed class FleaListingState : IMessage
    {
        public sealed class Entry
        {
            public uint ItemId, NativeNumber;
            public ushort Price;
            public NetVector3 Position;
            public NetQuaternion Rotation = NetQuaternion.Identity;
        }
        public uint Revision;
        public readonly List<Entry> Items = new List<Entry>();
        public MessageId Id => MessageId.FleaListingState;
        public void Write(NetWriter w)
        {
            if (!FleaListingPolicy.Valid(this)) throw new ProtocolException("Invalid flea listings.");
            w.WriteUInt32(Revision); w.WriteByte((byte)Items.Count);
            foreach (var e in Items) { w.WriteUInt32(e.ItemId); w.WriteUInt32(e.NativeNumber); w.WriteUInt16(e.Price);
                w.WriteVector3(e.Position); w.WriteQuaternion(e.Rotation); }
        }
        public void Read(NetReader r)
        {
            Revision = r.ReadUInt32(); int count = r.ReadByte(); Items.Clear();
            if (count > FleaListingPolicy.Capacity) throw new ProtocolException("Too many flea listings.");
            for (int i = 0; i < count; i++) Items.Add(new Entry { ItemId = r.ReadUInt32(), NativeNumber = r.ReadUInt32(),
                Price = r.ReadUInt16(), Position = r.ReadVector3(), Rotation = r.ReadQuaternion() });
            if (!FleaListingPolicy.Valid(this)) throw new ProtocolException("Invalid flea listings.");
        }
    }
    public sealed class FleaListingIntent : IMessage
    {
        public byte PlayerId;
        public ushort Sequence, Price;
        public uint Revision, ItemId;
        public MessageId Id => MessageId.FleaListingIntent;
        public void Write(NetWriter w)
        {
            if (!FleaListingPolicy.Valid(this)) throw new ProtocolException("Invalid flea listing request.");
            w.WriteByte(PlayerId); w.WriteUInt16(Sequence); w.WriteUInt32(Revision); w.WriteUInt32(ItemId); w.WriteUInt16(Price);
        }
        public void Read(NetReader r)
        {
            PlayerId = r.ReadByte(); Sequence = r.ReadUInt16(); Revision = r.ReadUInt32(); ItemId = r.ReadUInt32(); Price = r.ReadUInt16();
            if (!FleaListingPolicy.Valid(this)) throw new ProtocolException("Invalid flea listing request.");
        }
    }
    public sealed class FleaListingResult : IMessage
    {
        public const byte Accepted = 0, Changed = 1, Unavailable = 2, Distant = 3, Claimed = 4, Stale = 5;
        public byte PlayerId, Result;
        public ushort Sequence;
        public uint ItemId;
        public MessageId Id => MessageId.FleaListingResult;
        private bool Valid => PlayerId != 255 && ItemId != 0 && Result <= Stale;
        public void Write(NetWriter w)
        {
            if (!Valid) throw new ProtocolException("Invalid flea listing receipt.");
            w.WriteByte(PlayerId); w.WriteUInt16(Sequence); w.WriteUInt32(ItemId); w.WriteByte(Result);
        }
        public void Read(NetReader r)
        {
            PlayerId = r.ReadByte(); Sequence = r.ReadUInt16(); ItemId = r.ReadUInt32(); Result = r.ReadByte();
            if (!Valid) throw new ProtocolException("Invalid flea listing receipt.");
        }
    }
}
