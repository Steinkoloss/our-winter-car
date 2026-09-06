using System;
using System.Collections;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class HockeyBettingTests
{
    private static readonly string[] Keys = { "1", "X", "2" };
    private static HockeyBettingState Board(ushort sequence = 1) => new()
    {
        Sequence = sequence, LatestRound = 4, GamesPlayed = 5, Flags = 1,
        Team1Id = 7, Team2Id = 8, Team1Odds = 2, Team2Odds = 3, TieOdds = 4, Result = "1",
        Pairs = Enumerable.Range(0, 12).Select(i => (byte)i).ToArray(),
        PreviousPairs = Enumerable.Range(0, 12).Reverse().Select(i => (byte)i).ToArray(),
        Odds = Enumerable.Range(0, 18).Select(i => 1.25f + i / 4f).ToArray(),
        Results = new byte[] { (byte)'1', (byte)'X', (byte)'2', (byte)'2', (byte)'X', (byte)'1' },
        ResultOdds = new[] { 3f, 4f, 5f, 6f, 7f, 8f },
        Scores = Enumerable.Repeat("2-1", 6).ToArray(),
        Standings = Enumerable.Range(0, 48).Select(i => (i % 12).ToString()).ToArray(),
    };

    private static (IList[] lists, IDictionary[] tables) Native(HockeyBettingState b)
    {
        IList[] lists = {
            new ArrayList(b.Pairs.Select(i => (int)i).ToArray()), new ArrayList(b.PreviousPairs.Select(i => (int)i).ToArray()),
            new ArrayList(b.Results.Select(i => ((char)i).ToString()).ToArray()), new ArrayList(b.ResultOdds), new ArrayList(b.Scores),
            new ArrayList(b.Standings.Take(12).ToArray()), new ArrayList(b.Standings.Skip(12).Take(12).ToArray()),
            new ArrayList(b.Standings.Skip(24).Take(12).ToArray()), new ArrayList(b.Standings.Skip(36).ToArray()),
        };
        IDictionary[] tables = Enumerable.Range(0, 6).Select(i => (IDictionary)new Hashtable
            { ["2"] = b.Odds[i * 3 + 2], ["X"] = b.Odds[i * 3 + 1], ["1"] = b.Odds[i * 3] }).ToArray();
        return (lists, tables);
    }

    [Fact]
    public void WireKeepsScalarPrefixAndAppendsWholeBoardInFixedSlotOrder()
    {
        var b = Board(0x0102); var wire = PacketCodec.Encode(b);
        Assert.Equal(348, wire.Length);
        Assert.Equal(new byte[] { 160, 0, 2, 1, 4, 0, 0, 0 }, wire.Take(8));
        Assert.Equal(new byte[] { 1, 0, (byte)'1', 1, 5, 0, 0, 0 }, wire.Skip(32).Take(8));
        Assert.Equal(b.Pairs, wire.Skip(40).Take(12));
        Assert.Equal(b.PreviousPairs, wire.Skip(52).Take(12));
        Assert.Equal(new byte[] { 0, 0, 160, 63 }, wire.Skip(64).Take(4));
        Assert.Equal(b.Results, wire.Skip(136).Take(6));
        var decoded = Assert.IsType<HockeyBettingState>(PacketCodec.Decode(wire));
        Assert.True(HockeyBettingReplica.SameBoard(b, decoded));
        Assert.Equal(b.Sequence, decoded.Sequence);
        for (int length = 2; length < wire.Length; length++)
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire.Take(length).ToArray()));
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire.Concat(new byte[1]).ToArray()));
        b.Odds = new float[17];
        Assert.Throws<ProtocolException>(() => PacketCodec.Encode(b));
    }

    [Theory]
    [InlineData("pairs-length")]
    [InlineData("previous-range")]
    [InlineData("duplicate-team")]
    [InlineData("odds-length")]
    [InlineData("odds-missing")]
    [InlineData("odds-nan")]
    [InlineData("odds-infinity")]
    [InlineData("odds-zero")]
    [InlineData("odds-large")]
    [InlineData("symbol")]
    [InlineData("result-length")]
    [InlineData("result-odds")]
    [InlineData("result-odds-length")]
    [InlineData("score-length")]
    [InlineData("score-control")]
    [InlineData("score-null")]
    [InlineData("standing-count")]
    [InlineData("standing-length")]
    [InlineData("standing-surrogate")]
    [InlineData("round")]
    [InlineData("games")]
    [InlineData("flags")]
    [InlineData("scratch-nan")]
    [InlineData("cursor")]
    public void MalformedBoardsNeverConsumeSequenceOrReplacePendingData(string fault)
    {
        var replica = new HockeyBettingReplica(); Assert.True(replica.Receive(Board()));
        var bad = Board(2);
        switch (fault)
        {
            case "pairs-length": bad.Pairs = new byte[11]; break;
            case "previous-range": bad.PreviousPairs[0] = 12; break;
            case "duplicate-team": bad.Pairs[0] = 1; break;
            case "odds-length": bad.Odds = new float[19]; break;
            case "odds-missing": bad.Odds = null!; break;
            case "odds-nan": bad.Odds[5] = float.NaN; break;
            case "odds-infinity": bad.Odds[17] = float.PositiveInfinity; break;
            case "odds-zero": bad.Odds[0] = 0; break;
            case "odds-large": bad.Odds[0] = 101; break;
            case "symbol": bad.Results[0] = 1; break;
            case "result-length": bad.Results = new byte[5]; break;
            case "result-odds": bad.ResultOdds[0] = -1; break;
            case "result-odds-length": bad.ResultOdds = new float[5]; break;
            case "score-length": bad.Scores[0] = new string('1', 17); break;
            case "score-control": bad.Scores[0] = "1\n2"; break;
            case "score-null": bad.Scores[0] = null!; break;
            case "standing-count": bad.Standings = new string[47]; break;
            case "standing-length": bad.Standings[0] = new string('1', 81); break;
            case "standing-surrogate": bad.Standings[0] = "\ud800"; break;
            case "round": bad.LatestRound = -1; break;
            case "games": bad.GamesPlayed = -1; break;
            case "flags": bad.Flags = 2; break;
            case "scratch-nan": bad.Team1Odds = float.NaN; break;
            case "cursor": bad.GameIndex = 7; break;
        }
        Assert.False(replica.Receive(bad));
        Assert.True(HockeyBettingReplica.SameBoard(Board(), replica.Current!));
        Assert.Equal((ushort)1, replica.Current!.Sequence);
        Assert.True(replica.Receive(Board(2)));
    }

    [Fact]
    public void DelayedBindingOwnsEveryCollectionAndZeroIsAnOrdinarySequenceAfterWrap()
    {
        var replica = new HockeyBettingReplica(); var b = Board(ushort.MaxValue);
        Assert.True(replica.Receive(b));
        b.Pairs[0] = 99; b.PreviousPairs[0] = 99; b.Odds[0] = 99; b.ResultOdds[0] = 99;
        b.Results[0] = 0; b.Scores[0] = "99-0"; b.Standings[0] = "changed";
        Assert.True(HockeyBettingReplica.SameBoard(Board(), replica.Current!));
        Assert.False(replica.Receive(Board(ushort.MaxValue)));
        Assert.True(replica.Receive(Board(0)));
        Assert.False(replica.Receive(Board(0)));
        Assert.False(replica.Receive(Board(0x8000)));
        Assert.False(replica.Receive(Board(ushort.MaxValue)));
        replica.Clear(); Assert.Null(replica.Current); Assert.True(replica.Receive(Board(ushort.MaxValue - 1)));
    }

    [Fact]
    public void NativeTablesUseExplicitKeysAndExactTypesWithoutPublishingAPartialRead()
    {
        var b = Board(); var (lists, tables) = Native(b); var sample = Board();
        Assert.True(HockeyBettingReplica.ReadCollections(sample, lists, tables, Keys));
        Assert.True(HockeyBettingReplica.SameBoard(b, sample));
        tables[0]["unrelated"] = 42;
        Assert.True(HockeyBettingReplica.ReadCollections(sample, lists, tables, Keys));
        foreach (object bad in new object[] { 1, "1.25", 1.25d, float.NaN })
        {
            tables[5]["2"] = bad; Assert.False(HockeyBettingReplica.ReadCollections(sample, lists, tables, Keys));
            Assert.True(HockeyBettingReplica.SameBoard(b, sample));
        }
        tables[5]["2"] = b.Odds[17]; tables[3].Remove("X");
        Assert.False(HockeyBettingReplica.ReadCollections(sample, lists, tables, Keys));
        tables[3]["X"] = b.Odds[10];
        foreach (object bad in new object[] { 0f, 256, -256, (byte)0, "0" })
        {
            lists[0][0] = bad; Assert.False(HockeyBettingReplica.ReadCollections(sample, lists, tables, Keys));
        }
        lists[0][0] = 0; lists[2][0] = "\u0131";
        Assert.False(HockeyBettingReplica.ReadCollections(sample, lists, tables, Keys));
        lists[2][0] = "1"; lists[8].RemoveAt(11);
        Assert.False(HockeyBettingReplica.ReadCollections(sample, lists, tables, Keys));
        Assert.True(HockeyBettingReplica.SameBoard(b, sample));
    }

    [Fact]
    public void EveryOddsAndDisplayChangeInTheSameRoundRequiresBroadcast()
    {
        var original = Board();
        foreach (var name in new[] { "Pairs", "PreviousPairs", "Odds", "Results", "ResultOdds", "Scores", "Standings" })
        {
            var field = typeof(HockeyBettingState).GetField(name)!;
            var length = ((Array)field.GetValue(original)!).Length;
            for (int i = 0; i < length; i++)
            {
                var changed = Board(); var array = (Array)field.GetValue(changed)!;
                if (array is byte[] bytes) bytes[i]++;
                else if (array is float[] floats) floats[i] += .25f;
                else ((string[])array)[i] += "x";
                Assert.False(HockeyBettingReplica.SameBoard(original, changed));
            }
        }
        Assert.True(HockeyBettingReplica.SameBoard(original, Board(345)));
    }

    [Fact]
    public void LoadedRoundAndFirstRoundHistoryRemainNativeWithoutInventedValues()
    {
        var b = Board(); b.LatestRound = 0; b.GamesPlayed = 37;
        b.Team1Odds = b.Team2Odds = b.TieOdds = 0; b.Result = "";
        b.PreviousPairs = new byte[12]; b.Scores = Enumerable.Repeat("", 6).ToArray();
        b.Standings[0] = "Läkijätkät";
        Assert.True(HockeyBettingReplica.Valid(b));
        Assert.True(HockeyBettingReplica.SameBoard(b, Assert.IsType<HockeyBettingState>(PacketCodec.Decode(PacketCodec.Encode(b)))));
    }
}
