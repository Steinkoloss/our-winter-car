namespace WinterMP.Net.Messages
{
    public sealed class PokerIntent : IMessage
    {
        public const byte Insert = 0, Bet = 1, Deal = 2, Double = 3, Low = 4, High = 5,
            Hold1 = 6, Hold2 = 7, Hold3 = 8, Hold4 = 9, Hold5 = 10, TakeWin = 11;
        public uint MachineId, Round;
        public byte PlayerId, Action;
        public ushort Sequence;
        public MessageId Id => MessageId.PokerIntent;
        public void Write(NetWriter w)
        {
            w.WriteUInt32(MachineId); w.WriteByte(PlayerId); w.WriteUInt16(Sequence);
            w.WriteByte(Action); w.WriteUInt32(Round);
        }
        public void Read(NetReader r)
        {
            MachineId = r.ReadUInt32(); PlayerId = r.ReadByte(); Sequence = r.ReadUInt16();
            Action = r.ReadByte(); Round = r.ReadUInt32();
        }
    }

    public sealed class PokerState : IMessage
    {
        public const byte Ready = 0, Holding = 1, WinOffer = 2, Guessing = 3, NoPlayer = 255;
        public uint MachineId, Revision, Round;
        public byte PlayerId = NoPlayer, Phase, Bet = 1, HoldMask, Hand;
        public int Credit, Winnings, PendingWin;
        // 0 = covered/unset; 1..52 = suit * 13 + rank (ace = 1).
        public byte[] Cards = new byte[5];
        public byte DoubleCard;
        public MessageId Id => MessageId.PokerState;
        public void Write(NetWriter w)
        {
            w.WriteUInt32(MachineId); w.WriteUInt32(Revision); w.WriteUInt32(Round);
            w.WriteByte(PlayerId); w.WriteByte(Phase); w.WriteByte(Bet); w.WriteByte(HoldMask);
            w.WriteByte(Hand); w.WriteInt32(Credit); w.WriteInt32(Winnings); w.WriteInt32(PendingWin);
            for (int i = 0; i < 5; i++) w.WriteByte(Cards[i]);
            w.WriteByte(DoubleCard);
        }
        public void Read(NetReader r)
        {
            MachineId = r.ReadUInt32(); Revision = r.ReadUInt32(); Round = r.ReadUInt32();
            PlayerId = r.ReadByte(); Phase = r.ReadByte(); Bet = r.ReadByte(); HoldMask = r.ReadByte();
            Hand = r.ReadByte(); Credit = r.ReadInt32(); Winnings = r.ReadInt32(); PendingWin = r.ReadInt32();
            Cards = new byte[5];
            for (int i = 0; i < 5; i++) Cards[i] = r.ReadByte();
            DoubleCard = r.ReadByte();
        }
    }

    public sealed class PokerResult : IMessage
    {
        public const byte Accepted = 0, Busy = 1, Funds = 2, Invalid = 3, Distant = 4, Stale = 255;
        public const byte RoyalAchievement = 1, CashoutAchievement = 2;
        public uint MachineId;
        public byte PlayerId, Result, Achievements;
        public ushort Sequence;
        public int CashDelta;
        public MessageId Id => MessageId.PokerResult;
        public void Write(NetWriter w)
        {
            w.WriteUInt32(MachineId); w.WriteByte(PlayerId); w.WriteUInt16(Sequence);
            w.WriteByte(Result); w.WriteInt32(CashDelta); w.WriteByte(Achievements);
        }
        public void Read(NetReader r)
        {
            MachineId = r.ReadUInt32(); PlayerId = r.ReadByte(); Sequence = r.ReadUInt16();
            Result = r.ReadByte(); CashDelta = r.ReadInt32(); Achievements = r.ReadByte();
        }
    }
}
