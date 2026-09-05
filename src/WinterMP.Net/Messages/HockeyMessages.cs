namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests (v88): the hockey betting round (COVERAGE-ROADMAP R1.4, betting half).
    /// Each client simulates the season with its own RNG, so the matchup, odds and — money-
    /// relevant — the round RESULT diverge. The host owns <c>Systems/HockeyGames/Betting ::
    /// Logic</c> and broadcasts the round scalars on change + join; guests write them back so
    /// every payout-deciding input agrees. The season standings TABLE stays per-client
    /// (display-only residual: it lives in ES2 array save keys no FSM variable exposes).
    /// See HockeyBettingSync.
    /// </summary>
    public sealed class HockeyBettingState : IMessage
    {
        /// <summary>KurPa won the season (Runkosarja <c>KurPaWins</c>).</summary>
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

        public bool KurPaWins => (Flags & FlagKurPaWins) != 0;

        public MessageId Id => MessageId.HockeyBettingState;

        public void Write(NetWriter writer)
        {
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
        }
    }
}
