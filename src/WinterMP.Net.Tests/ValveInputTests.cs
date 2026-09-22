using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class ValveInputTests
    {
        private static EngineBlockState State() => new EngineBlockState { Flags = 11, Wear = 90, Revision = uint.MaxValue, ValvesAvailable = true, ValveSettings = new float[] { 1, 2, 3, 4, 5, 6, 7, 8 } };
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static void Reject(EngineBlockState state)
        {
            Assert.False(state.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state)); Assert.False(new EngineBlockReplica().Receive(state));
            Assert.Throws<ArgumentException>(() => new EngineBlockPublication().Observe(state));
        }
        [Fact]
        public void EightOrderedValveFloatsAndAvailabilityAppendToTheExhaustPayload()
        {
            var state = State(); var writer = new NetWriter(); state.Write(writer); var raw = writer.ToArray(); Assert.Equal(209, raw.Length); Assert.Equal(1, raw[94]);
            var reader = new NetReader(raw.Skip(95).ToArray()); for (int i = 1; i <= 8; i++) Assert.Equal((float)i, reader.ReadSingle());
            var copy = (EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state)); Assert.True(copy.ValvesAvailable); Assert.Equal(state.ValveSettings, copy.ValveSettings);
            for (int size = 94; size < 209; size++) Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(raw.Take(size).ToArray())));
            foreach (byte value in new byte[] { 2, 127, 255 }) { raw[94] = value; Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(raw))); }
        }
        [Theory] [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(7)]
        public void AvailableValveSettingsRequireTheInstalledHead(byte flags)
        { var state = State(); state.Flags = flags; state.Wear = (flags & 2) != 0 ? 90 : 0; Reject(state); state.ValvesAvailable = false; state.ValveSettings = new float[8]; Assert.True(state.Valid); }
        [Theory] [InlineData(-1)] [InlineData(0)] [InlineData(7)] [InlineData(9)]
        public void MalformedArrayShapeCannotReachWireOrCache(int length) { var state = State(); state.ValveSettings = length < 0 ? null! : new float[length]; Reject(state); }
        public static IEnumerable<object[]> InvalidValues() => Enumerable.Range(0, 8).SelectMany(i => new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity }.Select(v => new object[] { i, v }));
        [Theory] [MemberData(nameof(InvalidValues))]
        public void NonfiniteSettingsAreRejectedOnEncodeDecodeAndPublication(int index, float value)
        {
            var state = State(); state.ValveSettings[index] = value; Reject(state); var writer = new NetWriter(); State().Write(writer); var raw = writer.ToArray();
            var scalar = new NetWriter(); scalar.WriteSingle(value); Array.Copy(scalar.ToArray(), 0, raw, 95 + 4 * index, 4);
            Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(raw)));
        }
        public static IEnumerable<object[]> Indices() => Enumerable.Range(0, 8).Select(i => new object[] { i });
        [Theory] [MemberData(nameof(Indices))]
        public void EachSettingHasIndependentRevisionsCopyIsolationAndSnapshotPublication(int index)
        {
            var state = State(); var replica = new EngineBlockReplica(); Assert.True(replica.Receive(state)); state.ValveSettings[index] = 99;
            Assert.False(replica.Receive(state)); var copy = replica.Get()!; copy.ValveSettings[index] = 98; Assert.False(replica.Receive(copy)); Assert.Equal(index + 1, replica.Get()!.ValveSettings[index]);
            state.Revision = 0; Assert.True(replica.Receive(state)); Assert.False(replica.Receive(State())); state.ValveSettings[index] = 97; Assert.Equal(99, replica.Get()!.ValveSettings[index]);
            var publication = new EngineBlockPublication(); var first = publication.Observe(State()); publication.MarkBroadcast(first.Revision); state = State(); state.ValveSettings[index] = 55;
            var changed = publication.Observe(state); Assert.Equal(first.Revision + 1, changed.Revision); state.ValveSettings[index] = 88; changed.ValveSettings[index] = 89;
            state.ValveSettings[index] = 55; Assert.Equal(changed.Revision, publication.Observe(state).Revision); Assert.True(publication.NeedsBroadcast); publication.MarkBroadcast(first.Revision); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(changed.Revision); Assert.False(publication.NeedsBroadcast);
            state.ValvesAvailable = false; state.ValveSettings = new float[8]; state.ValveSettings[index] = .01f; Reject(state);
        }
        [Fact]
        public void UnavailableAndAvailableZeroSettingsHaveDistinctRevisions()
        {
            var state = State(); state.ValveSettings = new float[8]; var publication = new EngineBlockPublication(); var fitted = publication.Observe(state);
            state.ValvesAvailable = false; var removed = publication.Observe(state); Assert.Equal(fitted.Revision + 1, removed.Revision);
            var replica = new EngineBlockReplica(); Assert.True(replica.Receive(removed)); Assert.False(replica.Receive(fitted)); fitted.Revision = removed.Revision; Assert.False(replica.Receive(fitted));
        }
        [Fact]
        public void CatalogPinsNativeHeadReadAndAlternatingCylinderValveOrder()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError); var rule = parsed.GuestEngineInputs!.Valves;
            Assert.Equal("CORRIS/Simulation/Engine/Valves", rule.ReaderPath); Assert.Equal("Valves", rule.Reference); Assert.Equal("Cylinderhead", rule.TargetVariable);
            Assert.Equal("Data", rule.Output); Assert.Equal("Get cam profile", rule.HeadState); Assert.Equal(8, rule.HeadActionIndex);
            Assert.Equal(Enumerable.Range(0, 8).Select(i => "Cyl " + (i / 2 + 1) + (i % 2 == 0 ? " intake" : " exhaust")), rule.States);
        }
        [Theory]
        [InlineData("missing")] [InlineData("readerPath")] [InlineData("fsm")] [InlineData("targetVariable")] [InlineData("reference")] [InlineData("output")]
        [InlineData("headState")] [InlineData("headActionIndex")] [InlineData("count")] [InlineData("state")] [InlineData("actionIndex")] [InlineData("arrayIndex")]
        public void MalformedValveBindingsDisableOnlyEngineInputs(string field)
        {
            var json = Catalog(); var inputs = json["guestEngineInputs"]!; var rule = inputs["valves"]!;
            if (field == "missing") inputs.AsObject().Remove("valves");
            else if (field == "count") rule["readers"]!.AsArray().RemoveAt(7);
            else if (field == "state") rule["readers"]![0]![field] = "Cyl 1 exhaust";
            else if (field == "actionIndex" || field == "arrayIndex") rule["readers"]![0]![field] = 1;
            else if (field == "headActionIndex") rule[field] = 7;
            else rule[field] = "Other";
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.GuestEngineInputsError!); Assert.NotNull(parsed.ReplacementParts); Assert.NotEmpty(parsed.Doors);
        }
    }
}
