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
    public class RadiatorTests
    {
        private static readonly string[] Fields = { "RadiatorWear", "RadiatorCoolant", "RadiatorPressureCap", "RadiatorFlectEfficiency" };
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static EngineBlockState State() => new EngineBlockState { Revision = uint.MaxValue, Flags = 0, Wear = 0, RadiatorInstalled = true,
            RadiatorWear = 77, RadiatorCoolant = 6.2f, RadiatorPressureCap = .8f, RadiatorFlectEfficiency = .6f };
        private static void Set(EngineBlockState s, string field, float value) => typeof(EngineBlockState).GetField(field)!.SetValue(s, value);
        private static float Get(EngineBlockState s, string field) => (float)typeof(EngineBlockState).GetField(field)!.GetValue(s)!;
        private static byte[] Raw(EngineBlockState state)
        {
            var prefix = new NetWriter(); new EngineBlockState { Revision = state.Revision, Flags = state.Flags, Wear = state.Wear }.Write(prefix);
            var w = new NetWriter(); foreach (byte value in prefix.ToArray().Take(153)) w.WriteByte(value);
            w.WriteBool(state.RadiatorInstalled); foreach (string field in Fields) w.WriteSingle(Get(state, field)); w.WriteByte(state.CoolantHoseFlags); foreach (float hose in state.CoolantHoseTightness) w.WriteSingle(hose); w.WriteSingle(state.CarburettorTightness); w.WriteByte(state.CoolingAirflowFlags); w.WriteSingle(state.GrilleAirflow); w.WriteSingle(state.HoodAirflow); w.WriteSingle(state.FiberglassHoodAirflow); w.WriteBool(state.CoolingAmbientAvailable); w.WriteSingle(state.CoolingAmbientTemperature); return w.ToArray();
        }
        private static void Reject(EngineBlockState state)
        {
            Assert.False(state.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(Raw(state))));
            Assert.False(new EngineBlockReplica().Receive(state)); Assert.Throws<ArgumentException>(() => new EngineBlockPublication().Observe(state));
        }
        [Fact]
        public void RadiatorAppendsStrictInstallationAndFourOrderedFloatsToExisting153Bytes()
        {
            var state = State(); var w = new NetWriter(); state.Write(w); var raw = w.ToArray(); Assert.Equal(209, raw.Length); Assert.Equal(Raw(state), raw);
            Assert.Equal(1, raw[153]); for (int i = 0; i < Fields.Length; i++) Assert.Equal(Get(state, Fields[i]), BitConverter.ToSingle(raw, 154 + i * 4));
            var copy = (EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state)); Assert.True(copy.RadiatorInstalled); Assert.Equal(0, copy.Flags);
            foreach (string field in Fields) Assert.Equal(Get(state, field), Get(copy, field));
            for (int size = 153; size < raw.Length; size++) Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(raw.Take(size).ToArray())));
            foreach (byte invalid in new byte[] { 2, 128, 255 }) { raw[153] = invalid; Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(raw))); }
        }
        [Theory] [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(7)] [InlineData(11)] [InlineData(15)]
        public void RadiatorIsIndependentOfHeadAndBlock(byte flags)
        { var state = State(); state.Flags = flags; state.Wear = (flags & 2) == 0 ? 0 : 90; Assert.True(state.Valid); Assert.True(((EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state))).RadiatorInstalled); }
        public static IEnumerable<object[]> Nonfinite() => Fields.SelectMany(f => new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity }.Select(v => new object[] { f, v }));
        [Theory] [MemberData(nameof(Nonfinite))]
        public void NonfiniteRadiatorFieldsCannotEnterWireCacheOrPublication(string field, float value) { var state = State(); Set(state, field, value); Reject(state); }
        [Theory] [InlineData("RadiatorWear")] [InlineData("RadiatorCoolant")] [InlineData("RadiatorPressureCap")] [InlineData("RadiatorFlectEfficiency")]
        public void AbsentFieldsMustBeZeroAndPresentFieldsPreserveNativeRange(string field)
        {
            var state = new EngineBlockState { Flags = 3, Wear = 90 }; Set(state, field, .1f); Reject(state);
            foreach (float value in new[] { -100f, 0, 1000.5f }) { state = State(); Set(state, field, value); Assert.Equal(value, Get((EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state)), field)); }
        }
        [Theory] [InlineData("RadiatorWear")] [InlineData("RadiatorCoolant")] [InlineData("RadiatorPressureCap")] [InlineData("RadiatorFlectEfficiency")]
        public void EveryRadiatorFieldUsesCopiedRevisionOrderingAndSnapshotDoesNotConsumeBroadcast(string field)
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
        public void EmptyInstalledRadiatorAndAbsentRadiatorRemainDistinct()
        {
            var state = new EngineBlockState { Flags = 3, RadiatorInstalled = true }; var publication = new EngineBlockPublication(); var first = publication.Observe(state);
            state.RadiatorInstalled = false; var removed = publication.Observe(state); Assert.Equal(first.Revision + 1, removed.Revision);
            var replica = new EngineBlockReplica(); Assert.True(replica.Receive(removed)); Assert.False(replica.Receive(first)); first.Revision = removed.Revision; Assert.False(replica.Receive(first));
        }
        [Fact]
        public void CatalogPinsFiveNativeCoolingReadsAndAllThreeRadiators()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError); var inputs = parsed.GuestEngineInputs!;
            Assert.Equal(102, inputs.Entries.Count); Assert.Equal(182, inputs.Entries.Sum(e => e.Readers.Count));
            var entry = Assert.Single(inputs.Entries, e => e.RadiatorSource != null); var radiator = entry.RadiatorSource!; Assert.Same(inputs.Block!.Radiator, radiator);
            Assert.Empty(entry.Families); Assert.False(entry.DirectTarget); Assert.Equal("db_Radiator", entry.TargetVariable); Assert.Equal("Cooling", entry.Fsm);
            Assert.Equal("CORRIS/Simulation/Systems/Cooling", entry.ReaderPath); Assert.Empty(radiator.RootPrefix); Assert.Equal("VIN201", radiator.PartPrefix);
            Assert.Equal(new[] { "RADIATORa0", "RADIATORb0" }, radiator.AlternatePartPrefixes); Assert.Equal("Update 2", radiator.ReadyState);
            var pause = Assert.Single(parsed.GuestEngineProtection!.PausedFsms, p => p.Path == radiator.MountPath); Assert.Equal(11, pause.RequiredStates.Length); Assert.Contains("Remove other", pause.RequiredStates);
            Assert.Equal(new[] { "Radiator installed?:0:Installed:Installed1", "Radiator Data:0:Coolant:WaterLevel", "Radiator Data:1:PressureCap:PressureCap", "Radiator Data:3:Wear:Wear", "Flect:1:FlectEfficiency:FlectEff" }, entry.Readers.Select(r => r.State + ":" + r.ActionIndex + ":" + r.Variable + ":" + r.Output));
            Assert.All(entry.Readers, r => { Assert.False(r.EveryFrame); Assert.Equal(r.Variable == "Installed" ? "GetFsmBool" : "GetFsmFloat", r.ActionType); });
        }
        [Theory]
        [InlineData("profile")] [InlineData("mountPath")] [InlineData("rootPrefix")] [InlineData("relativePath")] [InlineData("partPrefix")] [InlineData("variants")] [InlineData("fsm")] [InlineData("readyState")]
        [InlineData("pause")] [InlineData("pause root")] [InlineData("pause state")] [InlineData("entry")] [InlineData("source")] [InlineData("readerPath")] [InlineData("targetVariable")] [InlineData("directTarget")] [InlineData("slotIndex")]
        [InlineData("state")] [InlineData("actionIndex")] [InlineData("variable")] [InlineData("output")] [InlineData("everyFrame")] [InlineData("actionType")] [InlineData("mountVariable")] [InlineData("alternateFamilies")]
        public void MalformedRadiatorDisablesOnlyEngineInputs(string fault)
        {
            var json = Catalog(); var inputs = json["guestEngineInputs"]!; var block = inputs["block"]!; var cover = block["radiator"]!; var entries = inputs["entries"]!.AsArray();
            var entry = entries.Single(e => e!["radiator"] != null)!; var read = entry["readers"]![0]!;
            var pauses = json["guestEngineProtection"]!["pausedFsms"]!.AsArray(); var pause = pauses.Single(p => p!["path"]?.GetValue<string>() == "CORRIS/Assemblies/VINP_Radiator")!;
            switch (fault)
            {
                case "profile": block.AsObject().Remove("radiator"); break;
                case "mountPath": case "rootPrefix": case "relativePath": case "partPrefix": case "fsm": case "readyState": cover[fault] = "Other"; break;
                case "variants": cover["alternatePartPrefixes"] = new JsonArray("VIN119"); break;
                case "pause": pauses.Remove(pause); break; case "pause root": pause["rootPrefix"] = "VIN101"; break; case "pause state": pause["requiredStates"]!.AsArray().RemoveAt(0); break;
                case "entry": entries.Remove(entry); break; case "source": entry["radiator"] = "Other"; break;
                case "readerPath": case "targetVariable": case "mountVariable": entry[fault] = "Other"; break; case "alternateFamilies": entry[fault] = new JsonArray("VIN118"); break;
                case "directTarget": entry[fault] = true; break; case "slotIndex": entry[fault] = 1; break;
                case "actionIndex": read[fault] = 1; break; case "everyFrame": read[fault] = true; break; default: read[fault] = "Other"; break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.GuestEngineInputsError!); Assert.NotNull(parsed.ReplacementParts); Assert.NotEmpty(parsed.Doors);
        }
    }
}
