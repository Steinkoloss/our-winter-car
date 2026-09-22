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
    public class CoolantHoseTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static EngineBlockState State(byte mask = 15)
        {
            var state = new EngineBlockState { Revision = uint.MaxValue, Flags = 27, Wear = 90, CoolantHoseFlags = mask, CarburettorTightness = 32 };
            for (int i = 0; i < 4; i++) if ((mask & (1 << i)) != 0) state.CoolantHoseTightness[i] = (i + 1) * 4;
            return state;
        }
        private static byte[] Raw(EngineBlockState state)
        {
            var prefix = new NetWriter(); new EngineBlockState { Revision = state.Revision, Flags = state.Flags, Wear = state.Wear }.Write(prefix);
            var w = new NetWriter(); foreach (byte b in prefix.ToArray().Take(170)) w.WriteByte(b);
            w.WriteByte(state.CoolantHoseFlags); foreach (float value in state.CoolantHoseTightness) w.WriteSingle(value); w.WriteSingle(state.CarburettorTightness); w.WriteByte(state.CoolingAirflowFlags); w.WriteSingle(state.GrilleAirflow); w.WriteSingle(state.HoodAirflow); w.WriteSingle(state.FiberglassHoodAirflow); w.WriteBool(state.CoolingAmbientAvailable); w.WriteSingle(state.CoolingAmbientTemperature); return w.ToArray();
        }
        private static void Reject(EngineBlockState state, bool read = true)
        {
            Assert.False(state.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state)); Assert.False(new EngineBlockReplica().Receive(state));
            Assert.Throws<ArgumentException>(() => new EngineBlockPublication().Observe(state));
            if (read) Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(Raw(state))));
        }
        public static IEnumerable<object[]> Masks() => Enumerable.Range(0, 16).Select(i => new object[] { (byte)i });
        [Theory] [MemberData(nameof(Masks))]
        public void FourHosesAndCarburettorBoltsAppendToExisting170Bytes(byte mask)
        {
            var state = State(mask); var w = new NetWriter(); state.Write(w); var raw = w.ToArray(); Assert.Equal(209, raw.Length); Assert.Equal(Raw(state), raw);
            Assert.Equal(mask, raw[170]); for (int i = 0; i < 4; i++) Assert.Equal(state.CoolantHoseTightness[i], BitConverter.ToSingle(raw, 171 + 4 * i));
            Assert.Equal(32, BitConverter.ToSingle(raw, 187)); var copy = (EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state));
            Assert.Equal(mask, copy.CoolantHoseFlags); Assert.Equal(state.CoolantHoseTightness, copy.CoolantHoseTightness); Assert.Equal(32, copy.CarburettorTightness);
        }
        [Fact]
        public void HosesRemainIndependentOfRadiatorBlockHeadAndCarburettor()
        {
            var state = State(); state.Flags = 0; state.Wear = 0; state.CarburettorTightness = 0;
            Assert.Equal(15, ((EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state))).CoolantHoseFlags);
        }
        [Theory] [InlineData(16)] [InlineData(128)] [InlineData(255)]
        public void UnknownHoseFlagsAreRejected(byte flags) { var s = State(); s.CoolantHoseFlags = flags; Reject(s); }
        [Theory] [InlineData(0)] [InlineData(3)] [InlineData(5)]
        public void HoseArrayHasExactlyFourValues(int length) { var s = State(); s.CoolantHoseTightness = new float[length]; Reject(s, false); }
        [Fact]
        public void NullArraysAndTruncatedAppendagesAreRejected()
        {
            var s = State(); s.CoolantHoseTightness = null!; Reject(s, false); var raw = Raw(State());
            for (int length = 170; length < raw.Length; length++) Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(raw.Take(length).ToArray())));
        }
        public static IEnumerable<object[]> Nonfinite() => Enumerable.Range(0, 5).SelectMany(i => new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity }.Select(v => new object[] { i, v }));
        private static void Set(EngineBlockState s, int i, float v) { if (i == 4) s.CarburettorTightness = v; else s.CoolantHoseTightness[i] = v; }
        private static float Get(EngineBlockState s, int i) => i == 4 ? s.CarburettorTightness : s.CoolantHoseTightness[i];
        [Theory] [MemberData(nameof(Nonfinite))]
        public void AllFiveTightnessValuesMustBeFinite(int index, float value) { var s = State(); Set(s, index, value); Reject(s); }
        [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
        public void AbsentInputsMustBeZeroAndFiniteInstalledInputsKeepNativeRange(int index)
        {
            var s = State(); if (index == 4) s.Flags = 11; else s.CoolantHoseFlags &= (byte)~(1 << index); Reject(s);
            foreach (float value in new[] { -16f, 0, 150.5f }) { s = State(); Set(s, index, value); Assert.Equal(value, Get((EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(s)), index)); }
        }
        [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
        public void EachTightnessUsesCopiedRevisionsAndSnapshotKeepsPendingBroadcast(int index)
        {
            var source = State(); var replica = new EngineBlockReplica(); Assert.True(replica.Receive(source)); Set(source, index, 99); Assert.False(replica.Receive(source));
            var copy = replica.Get()!; Set(copy, index, 99); Assert.True(replica.Receive(State())); Assert.NotEqual(99, Get(replica.Get()!, index));
            source.Revision = 0; Assert.True(replica.Receive(source)); Assert.False(replica.Receive(State()));
            var pub = new EngineBlockPublication(); var first = pub.Observe(State()); pub.MarkBroadcast(first.Revision); var next = pub.Observe(source);
            Set(source, index, 100); Set(next, index, 101); var same = State(); Set(same, index, 99); Assert.Equal(next.Revision, pub.Observe(same).Revision); Assert.True(pub.NeedsBroadcast);
            pub.MarkBroadcast(first.Revision); Assert.True(pub.NeedsBroadcast); pub.MarkBroadcast(next.Revision); Assert.False(pub.NeedsBroadcast);
        }
        [Fact]
        public void InstalledLooseHoseAndMissingHoseAreDistinctAndRemovalRejectsLateRefit()
        {
            var source = new EngineBlockState { CoolantHoseFlags = 2 }; var pub = new EngineBlockPublication(); var fitted = pub.Observe(source);
            source.CoolantHoseFlags = 0; var removed = pub.Observe(source); Assert.Equal(fitted.Revision + 1, removed.Revision);
            var replica = new EngineBlockReplica(); Assert.True(replica.Receive(removed)); Assert.False(replica.Receive(fitted)); fitted.Revision = removed.Revision; Assert.False(replica.Receive(fitted));
        }
        [Fact]
        public void CatalogPinsFiveHoseReadsAndCarburettorClampWithFourProtectedMounts()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError); var inputs = parsed.GuestEngineInputs!;
            Assert.Equal(102, inputs.Entries.Count); Assert.Equal(182, inputs.Entries.Sum(e => e.Readers.Count)); Assert.Equal(23, parsed.GuestEngineProtection!.PausedFsms.Count);
            var entries = inputs.Entries.Where(e => e.CoolantHoseSource != null).ToArray(); Assert.Equal(4, entries.Length); Assert.Equal(5, entries.Sum(e => e.Readers.Count));
            for (int i = 0; i < 4; i++)
            {
                var entry = entries[i]; var hose = entry.CoolantHoseSource!; Assert.Equal(i, hose.Index); Assert.Same(inputs.Block!.CoolantHoses[i], hose);
                Assert.Equal(new[] { "VIN202", "VIN203", "VIN216", "VIN217" }[i], hose.Mount.PartPrefix); Assert.Equal(i < 2 ? "Update 2" : "Update", hose.Mount.ReadyState);
                Assert.Empty(hose.Mount.RootPrefix); Assert.Empty(hose.Mount.AlternatePartPrefixes); Assert.Empty(entry.Families); Assert.False(entry.DirectTarget);
                var pause = Assert.Single(parsed.GuestEngineProtection.PausedFsms, p => p.Path == hose.Mount.MountPath); Assert.Equal(10, pause.RequiredStates.Length); Assert.Contains(hose.Mount.ReadyState, pause.RequiredStates);
                var read = Assert.Single(entry.Readers, r => r.Variable == "Tightness"); Assert.Equal("Hoses", read.State); Assert.Equal(i, read.ActionIndex); Assert.Equal("Tightness" + (i + 1), read.Output); Assert.False(read.EveryFrame);
            }
            var bottom = Assert.Single(entries[1].Readers, r => r.Variable == "Installed"); Assert.Equal("Bottom hose", bottom.State); Assert.Equal(0, bottom.ActionIndex); Assert.Equal("Installed1", bottom.Output);
            var carb = Assert.Single(inputs.Entries, e => e.CarburettorSource != null && e.Fsm == "Cooling"); var bolts = Assert.Single(carb.Readers);
            Assert.Equal("Hoses", bolts.State); Assert.Equal(4, bolts.ActionIndex); Assert.Equal("Tightness5", bolts.Output); Assert.Equal("Tightness", bolts.Variable); Assert.False(bolts.EveryFrame);
        }
        [Theory] [InlineData("profile")] [InlineData("name")] [InlineData("order")] [InlineData("mountPath")] [InlineData("rootPrefix")] [InlineData("relativePath")] [InlineData("partPrefix")]
        [InlineData("fsm")] [InlineData("readyState")] [InlineData("variants")] [InlineData("pause")] [InlineData("pause state")] [InlineData("entry")] [InlineData("source")]
        [InlineData("readerPath")] [InlineData("targetVariable")] [InlineData("state")] [InlineData("index")] [InlineData("output")] [InlineData("field")] [InlineData("cadence")] [InlineData("type")] [InlineData("carb")]
        public void InvalidHoseCatalogDisablesOnlyEngineInputs(string fault)
        {
            var json = Catalog(); var input = json["guestEngineInputs"]!; var rows = input["block"]!["coolantHoses"]!.AsArray(); var hose = rows[0]!;
            var entries = input["entries"]!.AsArray(); var entry = entries.Single(e => e!["coolantHose"]?.GetValue<string>() == "CoolantHoseTop")!; var read = entry["readers"]![0]!;
            var pauses = json["guestEngineProtection"]!["pausedFsms"]!.AsArray(); var pause = pauses.Single(p => p!["path"]!.GetValue<string>() == hose["mountPath"]!.GetValue<string>())!;
            switch (fault)
            {
                case "profile": input["block"]!.AsObject().Remove("coolantHoses"); break; case "order": rows[0] = rows[1]!.DeepClone(); break;
                case "name": case "mountPath": case "rootPrefix": case "relativePath": case "partPrefix": case "fsm": case "readyState": hose[fault] = "Other"; break;
                case "variants": hose["alternatePartPrefixes"] = new JsonArray("VIN203"); break; case "pause": pauses.Remove(pause); break; case "pause state": pause["requiredStates"]!.AsArray().RemoveAt(0); break;
                case "entry": entries.Remove(entry); break; case "source": entry["coolantHose"] = "Other"; break; case "readerPath": case "targetVariable": entry[fault] = "Other"; break;
                case "state": case "output": read[fault] = "Other"; break; case "index": read["actionIndex"] = 1; break; case "field": read["variable"] = "Wear"; break;
                case "cadence": read["everyFrame"] = true; break; case "type": read["actionType"] = "GetFsmBool"; break;
                case "carb": entries.Single(e => e!["carburettor"] != null && e["fsm"]!.GetValue<string>() == "Cooling")!["readers"]![0]!["actionIndex"] = 3; break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.GuestEngineInputsError!); Assert.NotNull(parsed.ReplacementParts); Assert.NotEmpty(parsed.Doors);
        }
    }
}
