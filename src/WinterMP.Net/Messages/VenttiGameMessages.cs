namespace WinterMP.Net.Messages
{
    /// <summary>Host command; Core authenticates PlayerId before applying it.</summary>
    public sealed class VenttiRequest : IMessage
    {
        public uint TableId, Revision;
        public byte PlayerId;
        public ushort Sequence;
        public VenttiAction Action;
        public MessageId Id => MessageId.VenttiRequest;
        public void Write(NetWriter w)
        {
            w.WriteUInt32(TableId); w.WriteUInt32(Revision); w.WriteByte(PlayerId);
            w.WriteUInt16(Sequence); w.WriteByte((byte)Action);
        }
        public void Read(NetReader r)
        {
            TableId = r.ReadUInt32(); Revision = r.ReadUInt32(); PlayerId = r.ReadByte();
            Sequence = r.ReadUInt16(); Action = (VenttiAction)r.ReadByte();
        }
    }

    public sealed class VenttiReceipt : IMessage
    {
        public uint TableId, Revision;
        public byte PlayerId;
        public ushort Sequence;
        public VenttiStatus Status;
        /// <summary>Audit value only; re-reading a receipt never applies this transfer again.</summary>
        public float CashDelta;
        public uint Round;
        public byte Outcome;
        public MessageId Id => MessageId.VenttiReceipt;
        public void Write(NetWriter w)
        {
            w.WriteUInt32(TableId); w.WriteUInt32(Revision); w.WriteByte(PlayerId); w.WriteUInt16(Sequence);
            w.WriteByte((byte)Status); w.WriteSingle(CashDelta); w.WriteUInt32(Round); w.WriteByte(Outcome);
        }
        public void Read(NetReader r)
        {
            TableId = r.ReadUInt32(); Revision = r.ReadUInt32(); PlayerId = r.ReadByte(); Sequence = r.ReadUInt16();
            Status = (VenttiStatus)r.ReadByte(); CashDelta = r.ReadSingle(); Round = r.ReadUInt32(); Outcome = r.ReadByte();
        }
    }

    /// <summary>Public hand/progression only. The remaining deck is private to the ledger.</summary>
    public sealed class VenttiLedgerState : IMessage
    {
        public const byte NoPlayer = 255;
        public uint TableId, Revision, Round;
        public byte PlayerId = NoPlayer, Outcome;
        public VenttiPhase Phase;
        public VenttiWager Wager;
        /// <summary>Paid cash stake in Betting/Playing; historical stake after resolution.</summary>
        public float Stake, BetMaximum, OpponentLoss;
        /// <summary>Committed return still owed to the shared wallet, including during teardown.</summary>
        public float PendingCash;
        public int PropertyStage;
        public byte[] PlayerCards = new byte[0], HouseCards = new byte[0];
        public int PlayerTotal, HouseTotal;
        public MessageId Id => MessageId.VenttiLedgerState;
        public void Write(NetWriter w)
        {
            w.WriteUInt32(TableId); w.WriteUInt32(Revision); w.WriteUInt32(Round);
            w.WriteByte(PlayerId); w.WriteByte(Outcome); w.WriteByte((byte)Phase); w.WriteByte((byte)Wager);
            w.WriteSingle(Stake); w.WriteSingle(BetMaximum); w.WriteSingle(OpponentLoss); w.WriteSingle(PendingCash);
            w.WriteInt32(PropertyStage); w.WriteInt32(PlayerTotal); w.WriteInt32(HouseTotal);
            WriteCards(w, PlayerCards); WriteCards(w, HouseCards);
        }
        public void Read(NetReader r)
        {
            TableId = r.ReadUInt32(); Revision = r.ReadUInt32(); Round = r.ReadUInt32();
            PlayerId = r.ReadByte(); Outcome = r.ReadByte(); Phase = (VenttiPhase)r.ReadByte(); Wager = (VenttiWager)r.ReadByte();
            Stake = r.ReadSingle(); BetMaximum = r.ReadSingle(); OpponentLoss = r.ReadSingle(); PendingCash = r.ReadSingle();
            PropertyStage = r.ReadInt32(); PlayerTotal = r.ReadInt32(); HouseTotal = r.ReadInt32();
            PlayerCards = ReadCards(r); HouseCards = ReadCards(r);
        }
        private static void WriteCards(NetWriter w, byte[] cards)
        {
            if (cards == null || cards.Length > 52) throw new ProtocolException("Invalid Ventti hand length.");
            w.WriteByte((byte)cards.Length);
            foreach (byte card in cards) w.WriteByte(card);
        }
        private static byte[] ReadCards(NetReader r)
        {
            int count = r.ReadByte();
            if (count > 52 || count > r.Remaining) throw new ProtocolException("Invalid Ventti hand length.");
            var cards = new byte[count];
            for (int i = 0; i < count; i++) cards[i] = r.ReadByte();
            return cards;
        }
    }

}
