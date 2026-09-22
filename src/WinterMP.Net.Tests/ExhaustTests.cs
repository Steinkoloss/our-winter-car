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
    public class ExhaustTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static EngineBlockState State(byte mask = 15)
        {
            var state = new EngineBlockState { Revision = uint.MaxValue, Flags = 11, Wear = 90, ExhaustFlags = mask };
            for (int i = 0; i < 12; i++) if ((mask & (1 << (i / 3))) != 0) state.ExhaustPerformance[i] = (i % 3 == 2 ? -.01f : 1) * (i + 1);
            return state;
        }
        private static byte[] Raw(EngineBlockState state)
        {
            var writer = new NetWriter(); writer.WriteUInt32(state.Revision); writer.WriteByte(state.Flags); writer.WriteSingle(state.Wear);
            foreach (float scalar in new[] { state.FuelChamber, state.CarbReserve, state.SettingMixture, state.CarburettorPower, state.CarburettorTorque, state.CarburettorPowerAdd,
                state.AirCleanerPower, state.AirCleanerTorque, state.AirCleanerPowerAdd }) writer.WriteSingle(scalar);
            writer.WriteByte(state.ExhaustFlags); foreach (float value in state.ExhaustPerformance) writer.WriteSingle(value); writer.WriteBool(state.ValvesAvailable); foreach (float value in state.ValveSettings) writer.WriteSingle(value); writer.WriteBool(state.OilpanInstalled); foreach (float value in new[] { state.OilpanWear, state.OilpanTightness, state.Oil, state.OilContamination, state.OilViscosity }) writer.WriteSingle(value); writer.WriteBool(state.RockerCoverInstalled); writer.WriteSingle(state.RockerCoverTightness); writer.WriteBool(state.RadiatorInstalled); foreach (float value in new[] { state.RadiatorWear, state.RadiatorCoolant, state.RadiatorPressureCap, state.RadiatorFlectEfficiency }) writer.WriteSingle(value); writer.WriteByte(state.CoolantHoseFlags); foreach (float hose in state.CoolantHoseTightness) writer.WriteSingle(hose); writer.WriteSingle(state.CarburettorTightness); writer.WriteByte(state.CoolingAirflowFlags); writer.WriteSingle(state.GrilleAirflow); writer.WriteSingle(state.HoodAirflow); writer.WriteSingle(state.FiberglassHoodAirflow); writer.WriteBool(state.CoolingAmbientAvailable); writer.WriteSingle(state.CoolingAmbientTemperature); return writer.ToArray();
        }
        private static void Reject(EngineBlockState state, bool read = true)
        {
            Assert.False(state.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            if (read) Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(Raw(state))));
            Assert.False(new EngineBlockReplica().Receive(state)); Assert.Throws<ArgumentException>(() => new EngineBlockPublication().Observe(state));
        }
        public static IEnumerable<object[]> Masks() => Enumerable.Range(0, 16).Select(i => new object[] { (byte)i });
        [Theory] [MemberData(nameof(Masks))]
        public void EveryIndependentInstallationMaskHasFixedOneHundredNinetyOneBytePayload(byte mask)
        {
            var state = State(mask); var writer = new NetWriter(); state.Write(writer); var raw = writer.ToArray();
            Assert.Equal(209, raw.Length); Assert.Equal(Raw(state), raw); Assert.Equal(mask, raw[45]);
            var reader = new NetReader(raw.Skip(46).ToArray()); for (int i = 0; i < 12; i++) Assert.Equal(state.ExhaustPerformance[i], reader.ReadSingle());
            var copy = (EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state)); Assert.Equal(mask, copy.ExhaustFlags); Assert.Equal(state.ExhaustPerformance, copy.ExhaustPerformance);
        }
        [Theory] [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(7)]
        public void FixedPipesDoNotRequireAnEngineButHeadersRequireItsHead(byte flags)
        {
            var state = State(14); state.Flags = flags; state.Wear = (flags & 2) != 0 ? 90 : 0;
            Assert.True(state.Valid); Assert.Equal((byte)14, ((EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state))).ExhaustFlags);
            state.ExhaustFlags = 15; Reject(state);
        }
        [Theory] [InlineData(16)] [InlineData(31)] [InlineData(128)] [InlineData(255)]
        public void ReservedInstallationBitsAreRejected(byte flags) { var state = State(); state.ExhaustFlags = flags; Reject(state); }
        [Theory] [InlineData(-1)] [InlineData(0)] [InlineData(11)] [InlineData(13)]
        public void ArrayShapeCannotEnterPublicationOrReplica(int size)
        {
            var state = State(); state.ExhaustPerformance = size < 0 ? null! : new float[size]; Reject(state, false);
        }
        public static IEnumerable<object[]> Nonfinite() => Enumerable.Range(0, 12).SelectMany(i => new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity }.Select(v => new object[] { i, v }));
        [Theory] [MemberData(nameof(Nonfinite))]
        public void EveryNonfinitePerformanceFieldIsRejected(int index, float value) { var state = State(); state.ExhaustPerformance[index] = value; Reject(state); }
        public static IEnumerable<object[]> Indices() => Enumerable.Range(0, 12).Select(i => new object[] { i });
        [Theory] [MemberData(nameof(Indices))]
        public void MissingSectionsCannotRetainPerformanceAndInstalledSectionsPreserveFiniteValues(int index)
        {
            var absent = State((byte)(15 & ~(1 << (index / 3)))); absent.ExhaustPerformance[index] = .01f; Reject(absent);
            foreach (float value in new[] { -200f, 0, 987.25f }) { var state = State(); state.ExhaustPerformance[index] = value; Assert.Equal(value, ((EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state))).ExhaustPerformance[index]); }
        }
        [Theory] [MemberData(nameof(Indices))]
        public void EveryPerformanceFieldUsesDefensiveCopiesRevisionsAndIndependentBroadcastTracking(int index)
        {
            var replica = new EngineBlockReplica(); var source = State(); Assert.True(replica.Receive(source)); source.ExhaustPerformance[index] = 91;
            Assert.False(replica.Receive(source)); var copy = replica.Get()!; copy.ExhaustPerformance[index] = 92; Assert.False(replica.Receive(copy)); Assert.True(replica.Receive(State()));
            source.Revision = 0; Assert.True(replica.Receive(source)); Assert.False(replica.Receive(State())); source.ExhaustPerformance[index] = 93;
            Assert.Equal(91, replica.Get()!.ExhaustPerformance[index]);
            var publication = new EngineBlockPublication(); var first = publication.Observe(State()); publication.MarkBroadcast(first.Revision); source = State(); source.ExhaustPerformance[index] = 101;
            var changed = publication.Observe(source); Assert.Equal(first.Revision + 1, changed.Revision); Assert.True(publication.NeedsBroadcast);
            source.ExhaustPerformance[index] = 102; changed.ExhaustPerformance[index] = 103; var expected = State(); expected.ExhaustPerformance[index] = 101;
            Assert.Equal(changed.Revision, publication.Observe(expected).Revision); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(first.Revision); Assert.True(publication.NeedsBroadcast); publication.MarkBroadcast(changed.Revision); Assert.False(publication.NeedsBroadcast);
        }
        [Fact]
        public void ZeroPerformanceInstallationStillChangesRevisionAndOldSnapshotsCannotRestoreRemoval()
        {
            var state = State(0); var publication = new EngineBlockPublication(); var first = publication.Observe(state); state.ExhaustFlags = 15;
            var fitted = publication.Observe(state); Assert.Equal(first.Revision + 1, fitted.Revision); state.ExhaustFlags = 0;
            var removed = publication.Observe(state); Assert.Equal(fitted.Revision + 1, removed.Revision);
            var replica = new EngineBlockReplica(); Assert.True(replica.Receive(removed)); Assert.False(replica.Receive(fitted)); fitted.Revision = removed.Revision;
            Assert.False(replica.Receive(fitted)); Assert.Equal((byte)0, replica.Get()!.ExhaustFlags);
        }
        [Fact]
        public void AllTruncatedExhaustPayloadsAreRejected()
        {
            var raw = Raw(State()); for (int size = 45; size < 209; size++) Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(raw.Take(size).ToArray())));
        }
        [Fact]
        public void FourNativeSourcesHaveTwelveExactEntryReadsAndProtectedMounts()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError); var inputs = parsed.GuestEngineInputs!;
            Assert.Equal(102, inputs.Entries.Count); Assert.Equal(182, inputs.Entries.Sum(e => e.Readers.Count));
            var sources = inputs.Block!.Exhaust; Assert.Equal(4, sources.Length); var entries = inputs.Entries.Where(e => e.ExhaustSource != null).ToArray(); Assert.Equal(4, entries.Length);
            string[] names = { "ExhaustHeaders", "ExhaustFront", "ExhaustRear", "ExhaustMuffler" }, states = { "Headers", "Exhaust front", "Exhaust rear", "Muffler" };
            string[][] variants = { new[] { "VIN114B", "HEADERSa0", "HEADERSc0", "HEADERSd0" }, new[] { "VIN213B" }, new[] { "EXHAUSTRa0" }, new[] { "MUFFLERa0" } };
            for (int i = 0; i < 4; i++)
            {
                var source = sources[i]; Assert.Equal(i, source.Index); Assert.Equal(names[i], source.Name); Assert.Equal(states[i], source.State); Assert.Equal(variants[i], source.Mount.AlternatePartPrefixes);
                Assert.Equal(i == 0 ? "VIN111" : "", source.Mount.RootPrefix); Assert.Equal("Update 2", source.Mount.ReadyState);
                var entry = Assert.Single(entries, e => e.ExhaustSource == source); Assert.Empty(entry.Families); Assert.False(entry.DirectTarget); Assert.Equal(source.TargetVariable, entry.TargetVariable);
                Assert.Equal(new[] { 0, 1, 2 }, entry.Readers.Select(r => r.ActionIndex)); Assert.Equal(new[] { "DataPower", "DataTorque", "DataPowerAdd" }, entry.Readers.Select(r => r.Variable));
                Assert.Equal(new[] { "PartPower", "PartTorque", "PartPowerAdd" }, entry.Readers.Select(r => r.Output)); Assert.All(entry.Readers, r => { Assert.False(r.EveryFrame); Assert.Equal(states[i], r.State); });
                var pause = Assert.Single(parsed.GuestEngineProtection!.PausedFsms, p => p.Path == source.Mount.MountPath); Assert.Equal(10, pause.RequiredStates.Length); Assert.Equal(source.Mount.RootPrefix, pause.RootPrefix);
            }
        }
        [Theory]
        [InlineData("profile")] [InlineData("count")] [InlineData("order")] [InlineData("mount")] [InlineData("root")] [InlineData("missing root")] [InlineData("header root")] [InlineData("relative")]
        [InlineData("prefix")] [InlineData("variants")] [InlineData("fsm")] [InlineData("ready")] [InlineData("pause missing")] [InlineData("pause state")]
        [InlineData("entry")] [InlineData("source")] [InlineData("reader")] [InlineData("consumer")] [InlineData("target")] [InlineData("direct")]
        [InlineData("family")] [InlineData("slot")] [InlineData("index")] [InlineData("state")] [InlineData("output")] [InlineData("cadence")] [InlineData("field")] [InlineData("type")]
        public void MalformedExhaustBindingsFailLocally(string fault)
        {
            var json = Catalog(); var inputs = json["guestEngineInputs"]!; var block = inputs["block"]!; var exhaust = block["exhaust"]!.AsArray(); var front = exhaust[1]!;
            var entries = inputs["entries"]!.AsArray(); var entry = entries.Single(e => e!["exhaust"]?.GetValue<string>() == "ExhaustFront")!; var read = entry["readers"]![0]!;
            var pauses = json["guestEngineProtection"]!["pausedFsms"]!.AsArray(); var pause = pauses.Single(p => p!["path"]!.GetValue<string>() == front["mountPath"]!.GetValue<string>())!;
            switch (fault)
            {
                case "profile": block.AsObject().Remove("exhaust"); break; case "count": exhaust.RemoveAt(3); break; case "order": front["name"] = "Rear"; break;
                case "mount": front["mountPath"] = "CORRIS/Other"; break; case "root": front["rootPrefix"] = "VIN111"; break; case "missing root": front.AsObject().Remove("rootPrefix"); break;
                case "header root": exhaust[0]!["rootPrefix"] = ""; break; case "relative": front["relativePath"] = "VINP_ExhaustRear"; break; case "prefix": front["partPrefix"] = "VIN214"; break;
                case "variants": front["alternatePartPrefixes"] = new JsonArray("EXHAUSTFa0"); break; case "fsm": front["fsm"] = "Status"; break; case "ready": front["readyState"] = "Installed"; break;
                case "pause missing": pauses.Remove(pause); break; case "pause state": pause["requiredStates"]!.AsArray().RemoveAt(0); break;
                case "entry": entries.Remove(entry); break; case "source": entry["exhaust"] = "Other"; break; case "reader": entry["readerPath"] = "CORRIS/Other"; break;
                case "consumer": entry["fsm"] = "Oil"; break; case "target": entry["targetVariable"] = "db_ExhaustPipeRear"; break; case "direct": entry["directTarget"] = true; break;
                case "family": entry["familyPrefix"] = "VIN213"; break; case "slot": entry["slotIndex"] = 1; break; case "index": read["actionIndex"] = 3; break;
                case "state": read["state"] = "Exhaust rear"; break; case "output": read["output"] = "MaxPower"; break; case "cadence": read["everyFrame"] = true; break;
                case "field": read["variable"] = "Wear"; break; case "type": read["actionType"] = "GetFsmBool"; break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.GuestEngineInputsError!); Assert.NotNull(parsed.ReplacementParts); Assert.NotEmpty(parsed.Doors);
        }
    }
}
