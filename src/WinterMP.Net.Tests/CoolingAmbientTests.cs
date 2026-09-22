using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class CoolingAmbientTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static EngineBlockState State(bool available = true, float value = -12.5f) => new EngineBlockState { Revision = uint.MaxValue, CoolingAmbientAvailable = available, CoolingAmbientTemperature = value };
        private static byte[] Raw(byte available, float value)
        {
            var prefix = new NetWriter(); new EngineBlockState { Revision = uint.MaxValue }.Write(prefix); var w = new NetWriter();
            foreach (byte b in prefix.ToArray().Take(204)) w.WriteByte(b); w.WriteByte(available); w.WriteSingle(value); return w.ToArray();
        }
        [Theory] [InlineData(false, 0)] [InlineData(true, -99)] [InlineData(true, -12.5)] [InlineData(true, 0)] [InlineData(true, 25)] [InlineData(true, 150)]
        public void AmbientAppendsFiveBytesIndependentOfAllPartFlags(bool available, float temperature)
        {
            var s = State(available, temperature); var w = new NetWriter(); s.Write(w); var raw = w.ToArray(); Assert.Equal(209, raw.Length); Assert.Equal(Raw((byte)(available ? 1 : 0), temperature), raw);
            Assert.Equal(available ? 1 : 0, raw[204]); Assert.Equal(temperature, BitConverter.ToSingle(raw, 205));
            var copy = (EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(s)); Assert.Equal(available, copy.CoolingAmbientAvailable); Assert.Equal(temperature, copy.CoolingAmbientTemperature);
        }
        [Theory] [InlineData(2)] [InlineData(128)] [InlineData(255)]
        public void AvailabilityIsAStrictBoolean(byte value) => Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(Raw(value, 0))));
        [Theory] [InlineData(false, 1)] [InlineData(false, -1)] [InlineData(false, float.NaN)] [InlineData(false, float.PositiveInfinity)]
        [InlineData(true, float.NaN)] [InlineData(true, float.PositiveInfinity)] [InlineData(true, float.NegativeInfinity)]
        public void InvalidAmbientCannotBeWrittenReadCachedOrPublished(bool available, float value)
        {
            var s = State(available, value); Assert.False(s.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s)); Assert.False(new EngineBlockReplica().Receive(s));
            Assert.Throws<ArgumentException>(() => new EngineBlockPublication().Observe(s)); Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(Raw((byte)(available ? 1 : 0), value))));
        }
        [Fact]
        public void TruncatedAmbientAppendageIsRejected()
        { var raw = Raw(1, -12.5f); for (int n = 204; n < 209; n++) Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(raw.Take(n).ToArray()))); }
        [Fact]
        public void AmbientAvailabilityAndTemperatureUseCopiedRevisionsAcrossWraparound()
        {
            var source = State(); var replica = new EngineBlockReplica(); Assert.True(replica.Receive(source)); source.CoolingAmbientTemperature = 5;
            Assert.False(replica.Receive(source)); var copy = replica.Get()!; copy.CoolingAmbientTemperature = 15; Assert.Equal(-12.5f, replica.Get()!.CoolingAmbientTemperature);
            source.Revision = 0; Assert.True(replica.Receive(source)); Assert.False(replica.Receive(State()));
            var missing = State(false, 0); missing.Revision = 1; Assert.True(replica.Receive(missing)); source.Revision = 1; Assert.False(replica.Receive(source));
            source.Revision = 2; source.CoolingAmbientTemperature = 0; Assert.True(replica.Receive(source)); Assert.True(replica.Get()!.CoolingAmbientAvailable);
        }
        [Fact]
        public void SnapshotDoesNotConsumeTemperatureUpdateAndZeroAvailabilityIsDistinct()
        {
            var pub = new EngineBlockPublication(); var first = pub.Observe(State()); pub.MarkBroadcast(first.Revision); var state = State(true, 0); var next = pub.Observe(state);
            Assert.Equal(first.Revision + 1, next.Revision); next.CoolingAmbientTemperature = 10; Assert.Equal(next.Revision, pub.Observe(state).Revision); Assert.True(pub.NeedsBroadcast);
            pub.MarkBroadcast(first.Revision); Assert.True(pub.NeedsBroadcast); var missing = pub.Observe(State(false, 0)); Assert.Equal(next.Revision + 1, missing.Revision);
            pub.MarkBroadcast(next.Revision); Assert.True(pub.NeedsBroadcast); pub.MarkBroadcast(missing.Revision); Assert.False(pub.NeedsBroadcast);
        }
        [Fact]
        public void CatalogPinsNonDataProducerAndSingleEntryOnlyReadWithoutPausingRoofCheck()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError); var inputs = parsed.GuestEngineInputs!;
            Assert.Equal(102, inputs.Entries.Count); Assert.Equal(182, inputs.Entries.Sum(e => e.Readers.Count)); Assert.Equal(23, parsed.GuestEngineProtection!.PausedFsms.Count);
            var entry = Assert.Single(inputs.Entries, e => e.CoolingAmbientSource != null); var source = entry.CoolingAmbientSource!;
            Assert.Same(inputs.Block!.CoolingAmbient, source); Assert.Equal("Raycast", source.Fsm); Assert.Equal("TempCar", source.Variable);
            Assert.Equal("CORRIS/Functions/RoofCheck", source.Path); Assert.Equal(source.Fsm, entry.InputFsm); Assert.Equal(source.Path, entry.MountPath); Assert.Equal("RoofCheck", entry.TargetVariable);
            Assert.Equal(new[] { "Cast ray", "Check roof", "Under roof", "Under sky" }, source.States); Assert.Empty(entry.Families); Assert.False(entry.DirectTarget);
            Assert.DoesNotContain(parsed.GuestEngineProtection.PausedFsms, p => p.Path == source.Path); var read = Assert.Single(entry.Readers);
            Assert.Equal("Reset", read.State); Assert.Equal(1, read.ActionIndex); Assert.Equal("TempArea", read.Output); Assert.Equal("GetFsmFloat", read.ActionType); Assert.False(read.EveryFrame);
        }
        [Theory] [InlineData("profile")] [InlineData("path")] [InlineData("fsm")] [InlineData("variable")] [InlineData("states")] [InlineData("state missing")]
        [InlineData("entry")] [InlineData("source")] [InlineData("readerPath")] [InlineData("mountPath")] [InlineData("targetVariable")] [InlineData("inputFsm")]
        [InlineData("state")] [InlineData("index")] [InlineData("output")] [InlineData("field")] [InlineData("cadence")] [InlineData("type")]
        [InlineData("direct")] [InlineData("family")] [InlineData("slot")] [InlineData("mountVariable")]
        public void InvalidAmbientCatalogDisablesOnlyEngineInputs(string fault)
        {
            var json = Catalog(); var input = json["guestEngineInputs"]!; var source = input["block"]!["coolingAmbient"]!;
            var entries = input["entries"]!.AsArray(); var entry = entries.Single(e => e!["coolingAmbient"] != null)!; var read = entry["readers"]![0]!;
            switch (fault)
            {
                case "profile": input["block"]!.AsObject().Remove("coolingAmbient"); break; case "path": case "fsm": case "variable": source[fault] = "Other"; break;
                case "states": source["states"]![0] = "Other"; break; case "state missing": source["states"]!.AsArray().RemoveAt(0); break;
                case "entry": entries.Remove(entry); break; case "source": entry["coolingAmbient"] = "Other"; break;
                case "readerPath": case "mountPath": case "targetVariable": case "inputFsm": case "mountVariable": entry[fault] = "Other"; break;
                case "state": case "output": read[fault] = "Other"; break; case "index": read["actionIndex"] = 0; break; case "field": read["variable"] = "TempArea"; break;
                case "cadence": read["everyFrame"] = true; break; case "type": read["actionType"] = "GetFsmBool"; break;
                case "direct": entry["directTarget"] = true; break; case "family": entry["alternateFamilies"] = new JsonArray("VIN413B"); break; case "slot": entry["slotIndex"] = 1; break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.GuestEngineInputsError!); Assert.NotNull(parsed.ReplacementParts); Assert.NotEmpty(parsed.Doors);
        }
    }
}
