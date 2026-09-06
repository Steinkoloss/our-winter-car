using System;
using System.Collections;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>Owns a complete pending board until its native collection proxies are ready.</summary>
    public sealed class HockeyBettingReplica
    {
        public HockeyBettingState? Current { get; private set; }

        public bool Receive(HockeyBettingState state)
        {
            if (!Valid(state)) return false;
            if (Current != null)
            {
                ushort delta = unchecked((ushort)(state.Sequence - Current.Sequence));
                if (delta == 0 || delta > short.MaxValue) return false;
            }
            Current = Copy(state);
            return true;
        }

        public void Clear() { Current = null; }

        public static bool Valid(HockeyBettingState s)
        {
            if (s.LatestRound < 0 || s.GamesPlayed < 0 || (s.Flags & ~HockeyBettingState.FlagKurPaWins) != 0
                || s.GameIndex < 0 || s.GameIndex > 6 || s.Team1Id < 0 || s.Team1Id >= 12 || s.Team2Id < 0 || s.Team2Id >= 12
                || !ScratchOdds(s.Team1Odds) || !ScratchOdds(s.Team2Odds) || !ScratchOdds(s.TieOdds)
                || !Text(s.Result, 256) || !Length(s.Pairs, 12) || !Length(s.PreviousPairs, 12)
                || !Length(s.Odds, 18) || !Length(s.Results, 6) || !Length(s.ResultOdds, 6)
                || !Length(s.Scores, 6) || !Length(s.Standings, 48)) return false;
            int teams = 0;
            for (int i = 0; i < 12; i++)
            {
                if (s.Pairs[i] >= 12 || (teams & (1 << s.Pairs[i])) != 0 || s.PreviousPairs[i] >= 12) return false;
                teams |= 1 << s.Pairs[i];
            }
            // The vanilla first round can have twelve zeroes in Pairs. Only PairsNew
            // is required to be a permutation; do not invent historical matchups.
            foreach (float odds in s.Odds) if (!ValidOdds(odds)) return false;
            for (int i = 0; i < 6; i++)
                if (!Symbol(s.Results[i]) || !ValidOdds(s.ResultOdds[i]) || !Text(s.Scores[i], 16)) return false;
            foreach (string text in s.Standings) if (!Text(text, 80)) return false;
            return true;
        }

        private static bool Length(Array? values, int count) => values != null && values.Length == count;
        private static bool Symbol(byte value) => value == (byte)'1' || value == (byte)'X' || value == (byte)'2';
        private static bool ScratchOdds(float value) => BankTransferPolicy.IsFinite(value) && value >= 0 && value <= 100;
        private static bool ValidOdds(float value) => ScratchOdds(value) && value >= 1;
        private static bool Text(string? value, int limit)
        {
            if (value == null || value.Length > limit) return false;
            foreach (char c in value) if (char.IsControl(c) || char.IsSurrogate(c)) return false;
            return true;
        }

        /// <summary>Slots: upcoming pairs, previous pairs, result symbols, result odds,
        /// scores, standings order, games, goals, points. Only exact native types are accepted.</summary>
        public static bool ReadCollections(HockeyBettingState state, IList[] lists, IDictionary[] tables, string[] keys)
        {
            int[] sizes = { 12, 12, 6, 6, 6, 12, 12, 12, 12 };
            if (lists == null || lists.Length != sizes.Length || tables == null || tables.Length != 6
                || keys == null || keys.Length != 3 || string.IsNullOrEmpty(keys[0]) || string.IsNullOrEmpty(keys[1])
                || string.IsNullOrEmpty(keys[2]) || keys[0] == keys[1] || keys[0] == keys[2] || keys[1] == keys[2]) return false;
            for (int i = 0; i < sizes.Length; i++) if (lists[i] == null || lists[i].Count != sizes[i]) return false;
            var result = Copy(state);
            for (int i = 0; i < 12; i++)
            {
                if (!(lists[0][i] is int next) || next < 0 || next >= 12
                    || !(lists[1][i] is int previous) || previous < 0 || previous >= 12) return false;
                result.Pairs[i] = (byte)next; result.PreviousPairs[i] = (byte)previous;
            }
            for (int i = 0; i < 6; i++)
            {
                if (!(lists[2][i] is string symbol) || symbol.Length != 1 || symbol[0] > 127
                    || !(lists[3][i] is float odds) || !(lists[4][i] is string score) || tables[i] == null) return false;
                result.Results[i] = (byte)symbol[0]; result.ResultOdds[i] = odds; result.Scores[i] = score;
                for (int key = 0; key < 3; key++)
                {
                    if (!(tables[i][keys[key]] is float value)) return false;
                    result.Odds[i * 3 + key] = value;
                }
            }
            for (int i = 0; i < 48; i++)
            {
                if (!(lists[5 + i / 12][i % 12] is string value)) return false;
                result.Standings[i] = value;
            }
            if (!Valid(result)) return false;
            state.Pairs = result.Pairs; state.PreviousPairs = result.PreviousPairs; state.Odds = result.Odds;
            state.Results = result.Results; state.ResultOdds = result.ResultOdds; state.Scores = result.Scores;
            state.Standings = result.Standings;
            return true;
        }

        public static bool SameBoard(HockeyBettingState a, HockeyBettingState b)
        {
            return a.LatestRound == b.LatestRound && a.GamesPlayed == b.GamesPlayed && a.Flags == b.Flags
                && a.GameIndex == b.GameIndex && a.Team1Id == b.Team1Id && a.Team2Id == b.Team2Id
                && a.Team1Odds == b.Team1Odds && a.Team2Odds == b.Team2Odds && a.TieOdds == b.TieOdds && a.Result == b.Result
                && Same(a.Pairs, b.Pairs) && Same(a.PreviousPairs, b.PreviousPairs) && Same(a.Odds, b.Odds)
                && Same(a.Results, b.Results) && Same(a.ResultOdds, b.ResultOdds) && Same(a.Scores, b.Scores)
                && Same(a.Standings, b.Standings);
        }
        private static bool Same<T>(T[] a, T[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!Equals(a[i], b[i])) return false;
            return true;
        }
        public static HockeyBettingState Copy(HockeyBettingState s) => new HockeyBettingState
        {
            Sequence = s.Sequence, LatestRound = s.LatestRound, GamesPlayed = s.GamesPlayed, Flags = s.Flags,
            GameIndex = s.GameIndex, Team1Id = s.Team1Id, Team2Id = s.Team2Id, Result = s.Result,
            Team1Odds = s.Team1Odds, Team2Odds = s.Team2Odds, TieOdds = s.TieOdds,
            Pairs = (byte[])s.Pairs.Clone(), PreviousPairs = (byte[])s.PreviousPairs.Clone(), Odds = (float[])s.Odds.Clone(),
            Results = (byte[])s.Results.Clone(), ResultOdds = (float[])s.ResultOdds.Clone(),
            Scores = (string[])s.Scores.Clone(), Standings = (string[])s.Standings.Clone(),
        };
    }
}
