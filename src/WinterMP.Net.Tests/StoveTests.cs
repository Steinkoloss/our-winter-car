using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class StoveTests
{
    private static ApplianceState State(uint revision = 1) => new() { ApplianceId = 42, Flags = 2, StoveRevision = revision,
        StoveHeat = new float[] { 23, 151.25f, 400, 750 }, StoveRotation = new float[] { -359.8f, 0, 51.4f, 308.4f }, GrillMask = 2, BurnMask = 12 };
    private static StoveKnobIntent Intent(ushort sequence = 1) => new() { ApplianceId = 42, PlayerId = 1, Plate = 3, Direction = 1, Sequence = sequence };
    [Fact]
    public void WirePreservesNativeTemperatureAndKnobPrecision()
    {
        var state = State(); var bytes = PacketCodec.Encode(state); Assert.Equal(52, bytes.Length);
        var decoded = Assert.IsType<ApplianceState>(PacketCodec.Decode(bytes)); Assert.True(StovePolicy.Same(state, decoded));
        using var r = new BinaryReader(new MemoryStream(bytes)); r.BaseStream.Position = 14;
        Assert.Equal(1u, r.ReadUInt32()); Assert.Equal(23, r.ReadSingle()); Assert.Equal(151.25f, r.ReadSingle());
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes[..^1]));
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
    }
    [Fact]
    public void NativeSmokeUsesTheExistingFlagsByte()
    {
        var state = State(); state.Flags = ApplianceState.FlagStoveSmoke;
        Assert.True(StovePolicy.Valid(state));
        state.Flags |= ApplianceState.FlagFire | ApplianceState.FlagFuseOk | ApplianceState.FlagStoveLight;
        var bytes = PacketCodec.Encode(state); Assert.Equal(52, bytes.Length); Assert.Equal(15, bytes[7]);
        var decoded = Assert.IsType<ApplianceState>(PacketCodec.Decode(bytes));
        Assert.Equal(state.Flags, decoded.Flags); Assert.True(StovePolicy.Same(state, decoded));
    }
    [Fact]
    public void SmokeChangesRequireANewerRevision()
    {
        var clear = State(); var smoke = State(); smoke.Flags |= ApplianceState.FlagStoveSmoke;
        Assert.False(StovePolicy.Same(clear, smoke));
        Assert.False(StovePolicy.CanReceive(clear, smoke)); Assert.False(StovePolicy.CanReceive(smoke, clear));
        Assert.True(StovePolicy.CanReceive(null, smoke)); Assert.True(StovePolicy.CanReceive(smoke, smoke));
        smoke.StoveRevision = 2; Assert.True(StovePolicy.CanReceive(clear, smoke));
        clear.StoveRevision = 3; Assert.True(StovePolicy.CanReceive(smoke, clear));
    }
    [Fact]
    public void TheNextReservedStoveFlagCannotReachTheGame()
    {
        var state = State(); var bytes = PacketCodec.Encode(state); state.Flags |= 16;
        Assert.False(StovePolicy.Valid(state)); Assert.False(StovePolicy.CanReceive(null, state));
        Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
        bytes[7] |= 16; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
    }
    [Fact]
    public void IntentWireContainsOnlyIdentityAndDirection()
    {
        var bytes = PacketCodec.Encode(Intent()); Assert.Equal(11, bytes.Length);
        var decoded = Assert.IsType<StoveKnobIntent>(PacketCodec.Decode(bytes));
        Assert.Equal((uint)42, decoded.ApplianceId); Assert.Equal(3, decoded.Plate); Assert.Equal(1, decoded.Direction);
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes[..^1]));
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
    }
    [Theory]
    [InlineData(float.NaN)][InlineData(float.PositiveInfinity)][InlineData(-1)][InlineData(750.01f)]
    public void InvalidNativeHeatCannotReachTheGame(float heat)
    {
        var state = State(); var bytes = PacketCodec.Encode(state); state.StoveHeat[0] = heat;
        Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
        Array.Copy(BitConverter.GetBytes(heat), 0, bytes, 18, 4); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
    }
    [Theory]
    [InlineData(float.NaN)][InlineData(float.NegativeInfinity)][InlineData(-360.1f)][InlineData(330.1f)]
    public void InvalidNativeKnobCannotReachTheGame(float rotation)
    {
        var state = State(); var bytes = PacketCodec.Encode(state); state.StoveRotation[0] = rotation;
        Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
        Array.Copy(BitConverter.GetBytes(rotation), 0, bytes, 34, 4); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
    }
    [Theory]
    [InlineData(16,0)][InlineData(0,16)][InlineData(1,1)]
    public void InvalidCookingTriggerMasksAreRejected(byte grill, byte burn)
    {
        var state = State(); state.GrillMask = grill; state.BurnMask = burn; Assert.False(StovePolicy.Valid(state));
        var bytes = PacketCodec.Encode(State()); bytes[50] = grill; bytes[51] = burn;
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
    }
    [Theory]
    [InlineData(0,1,0,0)][InlineData(42,0,0,0)][InlineData(42,255,0,0)][InlineData(42,1,4,0)][InlineData(42,1,0,2)]
    public void InvalidIntentCannotReachTheGame(uint id, byte player, byte plate, byte direction)
    {
        var request = Intent(); request.ApplianceId = id; request.PlayerId = player; request.Plate = plate; request.Direction = direction;
        Assert.Throws<ProtocolException>(() => PacketCodec.Encode(request));
    }
    [Fact]
    public void RevisionsRejectStaleConflictingAndUnboundReceipts()
    {
        Assert.True(StovePolicy.CanReceive(State(1), State(2))); Assert.True(StovePolicy.CanReceive(State(), State()));
        Assert.False(StovePolicy.CanReceive(State(2), State(1))); Assert.False(StovePolicy.CanReceive(null, State(0)));
        Assert.True(StovePolicy.CanReceive(State(uint.MaxValue), State(1)));
        Assert.False(StovePolicy.CanReceive(State(1), State(0x80000001)));
        var changed = State(); changed.StoveRotation[1] = 51.4f; Assert.False(StovePolicy.CanReceive(State(), changed));
        changed = State(); changed.StoveHeat[1] = 23; Assert.False(StovePolicy.CanReceive(State(), changed));
        changed = State(2); changed.ApplianceId = 43; Assert.False(StovePolicy.CanReceive(State(), changed));
    }
    [Fact]
    public void SequencesRetireRejectedDistantTurnsAndResetOnReadmission()
    {
        var ledger = new StoveIntentLedger(); Assert.True(ledger.Accept(Intent(65535), true));
        Assert.False(ledger.Accept(Intent(65535), true)); Assert.True(ledger.Accept(Intent(0), true));
        Assert.False(ledger.Accept(Intent(32768), true)); Assert.False(ledger.Accept(Intent(1), false));
        Assert.False(ledger.Accept(Intent(1), true)); Assert.True(ledger.Accept(Intent(2), true));
        ledger.Forget(1); Assert.True(ledger.Accept(Intent(1), true));
    }
    [Fact]
    public void OnlyAuthenticatedGuestsSendTurnsAndOnlyTheHostSendsState()
    {
        Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.ApplianceState, true, true, false, true));
        Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.ApplianceState, false, true, true, true));
        Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.ApplianceState, false, true, true, false));
        Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.StoveKnobIntent, true, false, false, true));
        Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.StoveKnobIntent, true, true, false, true));
        Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.StoveKnobIntent, false, true, true, true));
        foreach (var id in new[] { MessageId.StoveKnobIntent, MessageId.ApplianceState })
        { Assert.True(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableOrdered)); Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.UnreliableSequenced)); }
    }
    [Fact]
    public void CatalogBindsBothHomeStovesAndRejectsIncompleteBindings()
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json"));
        var data = SyncCatalogJson.Parse(text); Assert.Null(data.StovesError); Assert.Equal(2, data.Stoves!.Paths.Count);
        Assert.Equal("Rot", data.Stoves["rotation"]); Assert.Equal("Data", data.Stoves["data"]);
        Assert.Equal(new[] { false, true }, data.Stoves.IgnitionEnabled);
        foreach (var key in data.Stoves.Names.Keys.Append("paths").Append("ignitionEnabled"))
        {
            var json = JsonNode.Parse(text)!; json["stoves"]!.AsObject().Remove(key);
            var broken = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(broken.Stoves); Assert.NotNull(broken.StovesError); Assert.NotNull(broken.ParkingJoint);
        }
    }
}
