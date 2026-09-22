using WinterMP.Net.Sync;

namespace WinterMP.Net.Messages
{
    public sealed class FleaSaleState : IMessage
    {
        public const byte FlagRented = 1, FlagCollectable = 2;
        public ushort Sequence;
        public float MoneyTotal;
        public ushort RentDays;
        public byte Flags;
        public uint Revision;
        public float WeekPrice;
        public MessageId Id => MessageId.FleaSaleState;
        public void Write(NetWriter w)
        {
            if (!FleaSalePolicy.Valid(this)) throw new ProtocolException("Invalid flea table state.");
            w.WriteUInt16(Sequence); w.WriteSingle(MoneyTotal); w.WriteUInt16(RentDays); w.WriteByte(Flags);
            w.WriteUInt32(Revision); w.WriteSingle(WeekPrice);
        }
        public void Read(NetReader r)
        {
            Sequence = r.ReadUInt16(); MoneyTotal = r.ReadSingle(); RentDays = r.ReadUInt16(); Flags = r.ReadByte();
            Revision = r.ReadUInt32(); WeekPrice = r.ReadSingle();
            if (!FleaSalePolicy.Valid(this)) throw new ProtocolException("Invalid flea table state.");
        }
    }

    public sealed class FleaSaleIntent : IMessage
    {
        // Retired/reserved: never reuse the old unpaid rent or unused collection action.
        public const byte ActionRent = 0, ActionCollect = 1;
        public const byte PayRent = 2, CollectProceeds = 3;
        public byte Action, PlayerId;
        public ushort Sequence;
        public uint Revision;
        public ushort Weeks;
        public MessageId Id => MessageId.FleaSaleIntent;
        public void Write(NetWriter w)
        {
            if (!FleaSalePolicy.Valid(this)) throw new ProtocolException("Invalid flea transaction.");
            w.WriteByte(Action); w.WriteByte(PlayerId); w.WriteUInt16(Sequence); w.WriteUInt32(Revision); w.WriteUInt16(Weeks);
        }
        public void Read(NetReader r)
        {
            Action = r.ReadByte(); PlayerId = r.ReadByte(); Sequence = r.ReadUInt16(); Revision = r.ReadUInt32(); Weeks = r.ReadUInt16();
            if (!FleaSalePolicy.Valid(this)) throw new ProtocolException("Invalid flea transaction.");
        }
    }

    public sealed class FleaSaleResult : IMessage
    {
        public const byte Accepted = 0, Changed = 1, Unavailable = 2, Distant = 3, Funds = 4, Stale = 5;
        public byte PlayerId, Action, Result;
        public ushort Sequence;
        public MessageId Id => MessageId.FleaSaleResult;
        private bool Valid => PlayerId != 255 && (Action == FleaSaleIntent.PayRent || Action == FleaSaleIntent.CollectProceeds) && Result <= Stale;
        public void Write(NetWriter w)
        {
            if (!Valid) throw new ProtocolException("Invalid flea receipt.");
            w.WriteByte(PlayerId); w.WriteByte(Action); w.WriteUInt16(Sequence); w.WriteByte(Result);
        }
        public void Read(NetReader r)
        {
            PlayerId = r.ReadByte(); Action = r.ReadByte(); Sequence = r.ReadUInt16(); Result = r.ReadByte();
            if (!Valid) throw new ProtocolException("Invalid flea receipt.");
        }
    }
}
