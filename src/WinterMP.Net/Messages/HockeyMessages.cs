namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: completed hockey betting board. v104 appends the live collections;
    /// the v88 scalar prefix describes native scratch variables, not the six-match board.
    /// </summary>
    public sealed class HockeyBettingState : IMessage
    {
        public const int MatchCount = 6, TeamCount = 12, OddsCount = 18, StandingCount = 48;
        /// <summary>Native Runkosarja KurPaWins flag for the completed round.</summary>
        public const byte FlagKurPaWins = 1;

        public ushort Sequence;
        public int LatestRound;
        public int GameIndex;
        public int Team1Id;
        public int Team2Id;
        public float Team1Odds;
        public float Team2Odds;
        public float TieOdds;
        public string Result = string.Empty;
        public byte Flags;
        public int GamesPlayed;
        public byte[] Pairs = new byte[TeamCount], PreviousPairs = new byte[TeamCount];
        // Match-major; each match uses keys 1, X, 2, in that order.
        public float[] Odds = new float[OddsCount], ResultOdds = new float[MatchCount];
        // ASCII 1/X/2. These are previous results, independent of the new odds above.
        public byte[] Results = new byte[MatchCount];
        public string[] Scores = new string[MatchCount], Standings = new string[StandingCount];

        public bool KurPaWins => (Flags & FlagKurPaWins) != 0;

        public MessageId Id => MessageId.HockeyBettingState;

        public void Write(NetWriter writer)
        {
            if (Pairs == null || Pairs.Length != TeamCount || PreviousPairs == null || PreviousPairs.Length != TeamCount
                || Odds == null || Odds.Length != OddsCount || ResultOdds == null || ResultOdds.Length != MatchCount
                || Results == null || Results.Length != MatchCount || Scores == null || Scores.Length != MatchCount
                || Standings == null || Standings.Length != StandingCount)
                throw new ProtocolException("Invalid hockey board dimensions.");
            writer.WriteUInt16(Sequence);
            writer.WriteInt32(LatestRound);
            writer.WriteInt32(GameIndex);
            writer.WriteInt32(Team1Id);
            writer.WriteInt32(Team2Id);
            writer.WriteSingle(Team1Odds);
            writer.WriteSingle(Team2Odds);
            writer.WriteSingle(TieOdds);
            writer.WriteString(Result);
            writer.WriteByte(Flags);
            writer.WriteInt32(GamesPlayed);
            foreach (byte value in Pairs) writer.WriteByte(value);
            foreach (byte value in PreviousPairs) writer.WriteByte(value);
            foreach (float value in Odds) writer.WriteSingle(value);
            foreach (byte value in Results) writer.WriteByte(value);
            foreach (float value in ResultOdds) writer.WriteSingle(value);
            foreach (string value in Scores) writer.WriteString(value);
            foreach (string value in Standings) writer.WriteString(value);
        }

        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt16();
            LatestRound = reader.ReadInt32();
            GameIndex = reader.ReadInt32();
            Team1Id = reader.ReadInt32();
            Team2Id = reader.ReadInt32();
            Team1Odds = reader.ReadSingle();
            Team2Odds = reader.ReadSingle();
            TieOdds = reader.ReadSingle();
            Result = reader.ReadString();
            Flags = reader.ReadByte();
            GamesPlayed = reader.ReadInt32();
            Pairs = new byte[TeamCount]; PreviousPairs = new byte[TeamCount];
            Odds = new float[OddsCount]; Results = new byte[MatchCount]; ResultOdds = new float[MatchCount];
            Scores = new string[MatchCount]; Standings = new string[StandingCount];
            for (int i = 0; i < TeamCount; i++) Pairs[i] = reader.ReadByte();
            for (int i = 0; i < TeamCount; i++) PreviousPairs[i] = reader.ReadByte();
            for (int i = 0; i < OddsCount; i++) Odds[i] = reader.ReadSingle();
            for (int i = 0; i < MatchCount; i++) Results[i] = reader.ReadByte();
            for (int i = 0; i < MatchCount; i++) ResultOdds[i] = reader.ReadSingle();
            for (int i = 0; i < MatchCount; i++) Scores[i] = reader.ReadString();
            for (int i = 0; i < StandingCount; i++) Standings[i] = reader.ReadString();
        }
    }
}
