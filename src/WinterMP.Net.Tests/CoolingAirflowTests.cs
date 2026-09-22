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
    public class CoolingAirflowTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static readonly string[] Fields = { "GrilleAirflow", "HoodAirflow", "FiberglassHoodAirflow" };
        private static readonly byte[] Bits = { 1, 4, 8 };
        private static void Set(EngineBlockState s, int i, float v) => typeof(EngineBlockState).GetField(Fields[i])!.SetValue(s, v);
        private static float Get(EngineBlockState s, int i) => (float)typeof(EngineBlockState).GetField(Fields[i])!.GetValue(s)!;
        private static EngineBlockState State(byte mask = 15)
        {
            var state = new EngineBlockState { Revision = uint.MaxValue, CoolingAirflowFlags = mask };
            for (int i = 0; i < 3; i++) if ((mask & Bits[i]) != 0) Set(state, i, (i + 1) * 100);
            return state;
        }
        private static byte[] Raw(EngineBlockState state)
        {
            var prefix = new NetWriter(); new EngineBlockState { Revision = state.Revision }.Write(prefix);
            var w = new NetWriter(); foreach (byte b in prefix.ToArray().Take(191)) w.WriteByte(b);
            w.WriteByte(state.CoolingAirflowFlags); for (int i = 0; i < 3; i++) w.WriteSingle(Get(state, i)); w.WriteBool(state.CoolingAmbientAvailable); w.WriteSingle(state.CoolingAmbientTemperature); return w.ToArray();
        }
        private static void Reject(EngineBlockState state)
        {
            Assert.False(state.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state)); Assert.False(new EngineBlockReplica().Receive(state));
            Assert.Throws<ArgumentException>(() => new EngineBlockPublication().Observe(state));
            Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(Raw(state))));
        }
        public static IEnumerable<object[]> Masks() => Enumerable.Range(0, 16).Select(i => new object[] { (byte)i });
        [Theory] [MemberData(nameof(Masks))]
        public void FourIndependentInstallationsAndThreeModifiersAppendTo191Bytes(byte mask)
        {
            var state = State(mask); var w = new NetWriter(); state.Write(w); var raw = w.ToArray(); Assert.Equal(209, raw.Length); Assert.Equal(Raw(state), raw);
            Assert.Equal(mask, raw[191]); var copy = (EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state)); Assert.Equal(mask, copy.CoolingAirflowFlags);
            for (int i = 0; i < 3; i++) { Assert.Equal(Get(state, i), BitConverter.ToSingle(raw, 192 + 4 * i)); Assert.Equal(Get(state, i), Get(copy, i)); }
        }
        [Theory] [InlineData(16)] [InlineData(128)] [InlineData(255)]
        public void UnknownAirflowFlagsAreRejected(byte flags) { var s = State(); s.CoolingAirflowFlags = flags; Reject(s); }
        [Fact]
        public void TruncatedAirflowAppendageIsRejected()
        { var raw = Raw(State()); for (int n = 191; n < raw.Length; n++) Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(raw.Take(n).ToArray()))); }
        public static IEnumerable<object[]> Nonfinite() => Enumerable.Range(0, 3).SelectMany(i => new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity }.Select(v => new object[] { i, v }));
        [Theory] [MemberData(nameof(Nonfinite))]
        public void EveryModifierMustBeFinite(int index, float value) { var s = State(); Set(s, index, value); Reject(s); }
        [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void AbsentModifiersMustBeZeroAndInstalledModifiersKeepNativeRange(int index)
        {
            var s = State(); s.CoolingAirflowFlags &= (byte)~Bits[index]; Reject(s);
            foreach (float v in new[] { -40f, 0, 900.5f }) { s = State(); Set(s, index, v); Assert.Equal(v, Get((EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(s)), index)); }
        }
        [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void ModifiersUseCopiedRevisionsAndSnapshotKeepsPendingBroadcast(int index)
        {
            var source = State(); var replica = new EngineBlockReplica(); Assert.True(replica.Receive(source)); Set(source, index, -99); Assert.False(replica.Receive(source));
            var copy = replica.Get()!; Set(copy, index, -99); Assert.True(replica.Receive(State())); Assert.NotEqual(-99, Get(replica.Get()!, index));
            source.Revision = 0; Assert.True(replica.Receive(source)); Assert.False(replica.Receive(State()));
            var pub = new EngineBlockPublication(); var first = pub.Observe(State()); pub.MarkBroadcast(first.Revision); var next = pub.Observe(source);
            Set(source, index, 100); Set(next, index, 101); var same = State(); Set(same, index, -99); Assert.Equal(next.Revision, pub.Observe(same).Revision); Assert.True(pub.NeedsBroadcast);
            pub.MarkBroadcast(first.Revision); Assert.True(pub.NeedsBroadcast); pub.MarkBroadcast(next.Revision); Assert.False(pub.NeedsBroadcast);
        }
        [Theory] [InlineData(1)] [InlineData(2)] [InlineData(4)] [InlineData(8)]
        public void ZeroModifierInstallationIsDistinctAndRemovalRejectsLateRefit(byte bit)
        {
            var source = new EngineBlockState { CoolingAirflowFlags = bit }; var pub = new EngineBlockPublication(); var fitted = pub.Observe(source);
            source.CoolingAirflowFlags = 0; var removed = pub.Observe(source); Assert.Equal(fitted.Revision + 1, removed.Revision);
            var replica = new EngineBlockReplica(); Assert.True(replica.Receive(removed)); Assert.False(replica.Receive(fitted)); fitted.Revision = removed.Revision; Assert.False(replica.Receive(fitted));
        }
        [Fact]
        public void CatalogPinsSevenAirflowReadsAndFourProtectedMounts()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError); var inputs = parsed.GuestEngineInputs!;
            Assert.Equal(102, inputs.Entries.Count); Assert.Equal(182, inputs.Entries.Sum(e => e.Readers.Count)); Assert.Equal(23, parsed.GuestEngineProtection!.PausedFsms.Count);
            var entries = inputs.Entries.Where(e => e.CoolingAirflowSource != null).ToArray(); Assert.Equal(4, entries.Length); Assert.Equal(7, entries.Sum(e => e.Readers.Count));
            for (int i = 0; i < 4; i++)
            {
                var entry = entries[i]; var source = entry.CoolingAirflowSource!; Assert.Equal(i, source.Index); Assert.Same(inputs.Block!.CoolingAirflow[i], source);
                Assert.Equal(new[] { "VIN413", "BLOCKOFF", "VIN411", "HOODa0" }[i], source.Mount.PartPrefix); Assert.Equal("Update 2", source.Mount.ReadyState);
                Assert.Empty(source.Mount.RootPrefix); Assert.Empty(entry.Families); Assert.False(entry.DirectTarget);
                var pause = Assert.Single(parsed.GuestEngineProtection.PausedFsms, p => p.Path == source.Mount.MountPath); Assert.Contains("Remove part", pause.RequiredStates);
                Assert.All(entry.Readers, r => Assert.False(r.EveryFrame));
            }
            Assert.Equal(new[] { "VIN413B", "VIN413C", "VIN413D" }, entries[0].CoolingAirflowSource!.Mount.AlternatePartPrefixes);
        }
        [Theory] [InlineData("profile")] [InlineData("name")] [InlineData("order")] [InlineData("mountPath")] [InlineData("rootPrefix")] [InlineData("relativePath")] [InlineData("partPrefix")]
        [InlineData("fsm")] [InlineData("readyState")] [InlineData("variants")] [InlineData("pause")] [InlineData("pause state")] [InlineData("entry")] [InlineData("source")]
        [InlineData("readerPath")] [InlineData("targetVariable")] [InlineData("state")] [InlineData("index")] [InlineData("output")] [InlineData("field")] [InlineData("cadence")] [InlineData("type")]
        [InlineData("cover modifier")] [InlineData("hood priority")] [InlineData("direct")] [InlineData("family")] [InlineData("slot")]
        public void InvalidAirflowCatalogDisablesOnlyEngineInputs(string fault)
        {
            var json = Catalog(); var input = json["guestEngineInputs"]!; var rows = input["block"]!["coolingAirflow"]!.AsArray(); var source = rows[0]!;
            var entries = input["entries"]!.AsArray(); var entry = entries.Single(e => e!["coolingAirflow"]?.GetValue<string>() == "Grille")!; var read = entry["readers"]![0]!;
            var pauses = json["guestEngineProtection"]!["pausedFsms"]!.AsArray(); var pause = pauses.Single(p => p!["path"]!.GetValue<string>() == source["mountPath"]!.GetValue<string>())!;
            switch (fault)
            {
                case "profile": input["block"]!.AsObject().Remove("coolingAirflow"); break; case "order": rows[0] = rows[1]!.DeepClone(); break;
                case "name": case "mountPath": case "rootPrefix": case "relativePath": case "partPrefix": case "fsm": case "readyState": source[fault] = "Other"; break;
                case "variants": source["alternatePartPrefixes"] = new JsonArray("VIN413B", "VIN413C", "VIN413"); break; case "pause": pauses.Remove(pause); break; case "pause state": pause["requiredStates"]!.AsArray().RemoveAt(0); break;
                case "entry": entries.Remove(entry); break; case "source": entry["coolingAirflow"] = "Other"; break; case "readerPath": case "targetVariable": entry[fault] = "Other"; break;
                case "state": case "output": read[fault] = "Other"; break; case "index": read["actionIndex"] = 1; break; case "field": read["variable"] = "Wear"; break;
                case "cadence": read["everyFrame"] = true; break; case "type": read["actionType"] = "GetFsmFloat"; break;
                case "cover modifier": entries.Single(e => e!["coolingAirflow"]?.GetValue<string>() == "GrilleBlockoff")!["readers"]!.AsArray().Add(entry["readers"]![1]!.DeepClone()); break;
                case "hood priority": entries.Single(e => e!["coolingAirflow"]?.GetValue<string>() == "FiberglassHood")!["readers"]![0]!["actionIndex"] = 0; break;
                case "direct": entry["directTarget"] = true; break; case "family": entry["alternateFamilies"] = new JsonArray("VIN413B"); break; case "slot": entry["slotIndex"] = 1; break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.GuestEngineInputsError!); Assert.NotNull(parsed.ReplacementParts); Assert.NotEmpty(parsed.Doors);
        }
    }
}
