namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Retired v59 layout, retained for diagnostic decoding. Its string binding was a
    /// save key, not a winning line. Live Lotto replication uses LottoDrawState (180).
    /// </summary>
    public sealed class LotteryDrawState : IMessage
    {
        /// <summary>Native DrawDone flag; not a draw-completion indicator.</summary>
        public const byte FlagDrawDone = 1;

        public int Round;
        public int NationalPot;
        public string WinningNumbers = string.Empty;
        public byte Flags;

        public bool DrawDone => (Flags & FlagDrawDone) != 0;

        public MessageId Id => MessageId.LotteryDrawState;

        public void Write(NetWriter writer)
        {
            writer.WriteInt32(Round);
            writer.WriteInt32(NationalPot);
            writer.WriteString(WinningNumbers);
            writer.WriteByte(Flags);
        }

        public void Read(NetReader reader)
        {
            Round = reader.ReadInt32();
            NationalPot = reader.ReadInt32();
            WinningNumbers = reader.ReadString();
            Flags = reader.ReadByte();
        }
    }

    /// <summary>A complete native Lotto draw, sampled after all prize tiers are calculated.</summary>
    public sealed class LottoDrawState : IMessage
    {
        public const int MainCount = 7, BonusCount = 3, TierCount = 5, MaximumNumber = 39;
        public const byte FlagDrawDone = 1, FlagResultsVisible = 2, AllFlags = 3;
        public uint Sequence;
        public int Round, TicketRound, NationalPot, NationalPotMin, NationalPotFull;
        public byte[] Numbers = new byte[MainCount], Bonus = new byte[BonusCount];
        // Fixed tier order: 7, 6+bonus, 6, 5, 4. Native amounts retain full int32 precision.
        public int[] Prizes = new int[TierCount], Winners = new int[TierCount];
        public byte Flags;

        public MessageId Id => MessageId.LottoDrawState;
        public void Write(NetWriter writer)
        {
            if (Numbers == null || Numbers.Length != MainCount || Bonus == null || Bonus.Length != BonusCount
                || Prizes == null || Prizes.Length != TierCount || Winners == null || Winners.Length != TierCount)
                throw new ProtocolException("Invalid Lotto array lengths.");
            writer.WriteUInt32(Sequence);
            writer.WriteInt32(Round);
            writer.WriteInt32(TicketRound);
            writer.WriteInt32(NationalPot);
            writer.WriteInt32(NationalPotMin);
            writer.WriteInt32(NationalPotFull);
            foreach (byte number in Numbers) writer.WriteByte(number);
            foreach (byte number in Bonus) writer.WriteByte(number);
            foreach (int prize in Prizes) writer.WriteInt32(prize);
            foreach (int winners in Winners) writer.WriteInt32(winners);
            writer.WriteByte(Flags);
        }
        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt32();
            Round = reader.ReadInt32();
            TicketRound = reader.ReadInt32();
            NationalPot = reader.ReadInt32();
            NationalPotMin = reader.ReadInt32();
            NationalPotFull = reader.ReadInt32();
            Numbers = new byte[MainCount]; Bonus = new byte[BonusCount];
            Prizes = new int[TierCount]; Winners = new int[TierCount];
            for (int i = 0; i < MainCount; i++) Numbers[i] = reader.ReadByte();
            for (int i = 0; i < BonusCount; i++) Bonus[i] = reader.ReadByte();
            for (int i = 0; i < TierCount; i++) Prizes[i] = reader.ReadInt32();
            for (int i = 0; i < TierCount; i++) Winners[i] = reader.ReadInt32();
            Flags = reader.ReadByte();
        }
    }
}
