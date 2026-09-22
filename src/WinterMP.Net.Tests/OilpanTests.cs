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
    public class OilpanTests
    {
        private static readonly string[] Fields = { "OilpanWear", "OilpanTightness", "Oil", "OilContamination", "OilViscosity" };
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static EngineBlockState State() => new EngineBlockState { Revision = uint.MaxValue, Flags = 3, Wear = 90, OilpanInstalled = true,
            OilpanWear = 77, OilpanTightness = 60, Oil = 3.2f, OilContamination = .8f, OilViscosity = .6f };
        private static void Set(EngineBlockState s, string field, float value) => typeof(EngineBlockState).GetField(field)!.SetValue(s, value);
        private static float Get(EngineBlockState s, string field) => (float)typeof(EngineBlockState).GetField(field)!.GetValue(s)!;
        private static byte[] Raw(EngineBlockState state)
        {
            var prefix = new NetWriter(); new EngineBlockState { Revision = state.Revision, Flags = state.Flags, Wear = state.Wear }.Write(prefix);
            var w = new NetWriter(); foreach (byte value in prefix.ToArray().Take(127)) w.WriteByte(value);
            w.WriteBool(state.OilpanInstalled); foreach (string field in Fields) w.WriteSingle(Get(state, field)); w.WriteBool(state.RockerCoverInstalled); w.WriteSingle(state.RockerCoverTightness); w.WriteBool(state.RadiatorInstalled); foreach (float value in new[] { state.RadiatorWear, state.RadiatorCoolant, state.RadiatorPressureCap, state.RadiatorFlectEfficiency }) w.WriteSingle(value); w.WriteByte(state.CoolantHoseFlags); foreach (float hose in state.CoolantHoseTightness) w.WriteSingle(hose); w.WriteSingle(state.CarburettorTightness); w.WriteByte(state.CoolingAirflowFlags); w.WriteSingle(state.GrilleAirflow); w.WriteSingle(state.HoodAirflow); w.WriteSingle(state.FiberglassHoodAirflow); w.WriteBool(state.CoolingAmbientAvailable); w.WriteSingle(state.CoolingAmbientTemperature); return w.ToArray();
        }
        private static void Reject(EngineBlockState state)
        {
            Assert.False(state.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(Raw(state))));
            Assert.False(new EngineBlockReplica().Receive(state)); Assert.Throws<ArgumentException>(() => new EngineBlockPublication().Observe(state));
        }
        [Fact]
        public void OilpanAppendsStrictInstallationAndFiveOrderedFloatsToExisting127Bytes()
        {
            var state = State(); var w = new NetWriter(); state.Write(w); var raw = w.ToArray(); Assert.Equal(209, raw.Length); Assert.Equal(Raw(state), raw);
            Assert.Equal(1, raw[127]); for (int i = 0; i < Fields.Length; i++) Assert.Equal(Get(state, Fields[i]), BitConverter.ToSingle(raw, 128 + i * 4));
            var copy = (EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state)); Assert.True(copy.OilpanInstalled); Assert.Equal(3, copy.Flags);
            foreach (string field in Fields) Assert.Equal(Get(state, field), Get(copy, field));
            for (int size = 127; size < raw.Length; size++) Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(raw.Take(size).ToArray())));
            foreach (byte invalid in new byte[] { 2, 128, 255 }) { raw[127] = invalid; Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(raw))); }
        }
        [Theory] [InlineData(0)] [InlineData(1)]
        public void OilpanRequiresInstalledBlock(byte flags) { var state = State(); state.Flags = flags; state.Wear = 0; Reject(state); }
        [Theory] [InlineData(3)] [InlineData(7)] [InlineData(11)] [InlineData(15)]
        public void OilpanDoesNotRequireHeadAndSurvivesBlockDamage(byte flags)
        { var state = State(); state.Flags = flags; Assert.True(state.Valid); Assert.True(((EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state))).OilpanInstalled); }
        public static IEnumerable<object[]> Nonfinite() => Fields.SelectMany(f => new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity }.Select(v => new object[] { f, v }));
        [Theory] [MemberData(nameof(Nonfinite))]
        public void NonfiniteOilpanFieldsCannotEnterWireCacheOrPublication(string field, float value) { var state = State(); Set(state, field, value); Reject(state); }
        [Theory] [InlineData("OilpanWear")] [InlineData("OilpanTightness")] [InlineData("Oil")] [InlineData("OilContamination")] [InlineData("OilViscosity")]
        public void AbsentFieldsMustBeZeroAndPresentFieldsPreserveNativeRange(string field)
        {
            var state = new EngineBlockState { Flags = 3, Wear = 90 }; Set(state, field, .1f); Reject(state);
            foreach (float value in new[] { -100f, 0, 1000.5f }) { state = State(); Set(state, field, value); Assert.Equal(value, Get((EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state)), field)); }
        }
        [Theory] [InlineData("OilpanWear")] [InlineData("OilpanTightness")] [InlineData("Oil")] [InlineData("OilContamination")] [InlineData("OilViscosity")]
        public void EveryOilFieldUsesCopiedRevisionOrderingAndSnapshotDoesNotConsumeBroadcast(string field)
        {
            var source = State(); var replica = new EngineBlockReplica(); Assert.True(replica.Receive(source)); Set(source, field, 99); Assert.False(replica.Receive(source));
            var copy = replica.Get()!; Set(copy, field, 99); Assert.False(replica.Receive(copy)); Assert.True(replica.Receive(State()));
            source.Revision = 0; Assert.True(replica.Receive(source)); Assert.False(replica.Receive(State()));
            var publication = new EngineBlockPublication(); var first = publication.Observe(State()); publication.MarkBroadcast(first.Revision);
            var changed = publication.Observe(source); Assert.Equal(first.Revision + 1, changed.Revision); Set(source, field, 100); Set(changed, field, 101);
            var same = State(); Set(same, field, 99); Assert.Equal(changed.Revision, publication.Observe(same).Revision); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(first.Revision); Assert.True(publication.NeedsBroadcast); publication.MarkBroadcast(changed.Revision); Assert.False(publication.NeedsBroadcast);
        }
        [Fact]
        public void EmptyInstalledPanAndAbsentPanRemainDistinct()
        {
            var state = new EngineBlockState { Flags = 3, OilpanInstalled = true }; var publication = new EngineBlockPublication(); var first = publication.Observe(state);
            state.OilpanInstalled = false; var removed = publication.Observe(state); Assert.Equal(first.Revision + 1, removed.Revision);
            var replica = new EngineBlockReplica(); Assert.True(replica.Receive(removed)); Assert.False(replica.Receive(first)); first.Revision = removed.Revision; Assert.False(replica.Receive(first));
        }
        [Fact]
        public void CatalogPinsSevenNativeReadsAndMovingBlockProtection()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError); var inputs = parsed.GuestEngineInputs!;
            var entries = inputs.Entries.Where(e => e.OilpanSource != null).ToArray(); Assert.Equal(3, entries.Length); Assert.Equal(7, entries.Sum(e => e.Readers.Count));
            Assert.All(entries, e => { Assert.Empty(e.Families); Assert.False(e.DirectTarget); Assert.Equal("db_Oilpan", e.TargetVariable); Assert.All(e.Readers, r => { Assert.False(r.EveryFrame); Assert.Equal("GetFsmFloat", r.ActionType); }); });
            var pan = inputs.Block!.Oilpan; Assert.Equal("VIN101", pan.RootPrefix); Assert.Equal("VIN106", pan.PartPrefix); Assert.Equal("Update 2", pan.ReadyState); Assert.Empty(pan.AlternatePartPrefixes);
            var pause = Assert.Single(parsed.GuestEngineProtection!.PausedFsms, p => p.RelativePath == "VINP_Oilpan"); Assert.Equal(pan.MountPath, pause.Path); Assert.Equal(10, pause.RequiredStates.Length);
            Assert.Equal(new[] { "Oil:Major damage?:2:Wear:Wear", "Oil:Friction:0:OilViscosity:OilViscosity", "Oil:Oilpan leak:0:Tightness:Tightness", "Oil:Get oil:0:Oil:Oil", "Wearing:Oil level:0:Oil:Oil", "Wearing:Oil contamination:0:OilContamination:Math1", "Cylinders:Plug data:1:OilContamination:SparkPlugOilCont" }, entries.SelectMany(e => e.Readers.Select(r => e.Fsm + ":" + r.State + ":" + r.ActionIndex + ":" + r.Variable + ":" + r.Output)));
        }
        [Theory]
        [InlineData("profile")] [InlineData("mountPath")] [InlineData("rootPrefix")] [InlineData("relativePath")] [InlineData("partPrefix")] [InlineData("variants")] [InlineData("fsm")] [InlineData("readyState")]
        [InlineData("pause")] [InlineData("pause root")] [InlineData("pause state")] [InlineData("source")] [InlineData("readerPath")] [InlineData("targetVariable")] [InlineData("directTarget")] [InlineData("slotIndex")]
        [InlineData("Oil")] [InlineData("Wearing")] [InlineData("Cylinders")]
        [InlineData("state")] [InlineData("actionIndex")] [InlineData("variable")] [InlineData("output")] [InlineData("everyFrame")] [InlineData("actionType")]
        public void MalformedOilpanDisablesOnlyEngineInputs(string fault)
        {
            var json = Catalog(); var inputs = json["guestEngineInputs"]!; var block = inputs["block"]!; var pan = block["oilpan"]!; var entries = inputs["entries"]!.AsArray();
            var entry = entries.Single(e => e!["oilpan"] != null && e["fsm"]!.GetValue<string>() == "Oil")!; var read = entry["readers"]![0]!;
            var pauses = json["guestEngineProtection"]!["pausedFsms"]!.AsArray(); var pause = pauses.Single(p => p!["relativePath"]?.GetValue<string>() == "VINP_Oilpan")!;
            switch (fault)
            {
                case "profile": block.AsObject().Remove("oilpan"); break;
                case "mountPath": case "rootPrefix": case "relativePath": case "partPrefix": case "fsm": case "readyState": pan[fault] = "Other"; break;
                case "variants": pan["alternatePartPrefixes"] = new JsonArray("VIN107"); break;
                case "pause": pauses.Remove(pause); break; case "pause root": pause["rootPrefix"] = "VIN111"; break; case "pause state": pause["requiredStates"]!.AsArray().RemoveAt(0); break;
                case "source": entry["oilpan"] = "Other"; break; case "readerPath": case "targetVariable": entry[fault] = "Other"; break;
                case "directTarget": entry[fault] = true; break; case "slotIndex": entry[fault] = 1; break;
                case "Oil": case "Wearing": case "Cylinders": entries.Remove(entries.Single(e => e!["oilpan"] != null && e["fsm"]!.GetValue<string>() == fault)); break;
                case "actionIndex": read[fault] = 1; break; case "everyFrame": read[fault] = true; break; default: read[fault] = "Other"; break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.GuestEngineInputsError!); Assert.NotNull(parsed.ReplacementParts); Assert.NotEmpty(parsed.Doors);
        }
    }
}
