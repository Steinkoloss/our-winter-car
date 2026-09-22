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
    public class RockerCoverTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static EngineBlockState State() => new EngineBlockState { Revision = uint.MaxValue, Flags = 11, Wear = 90, RockerCoverInstalled = true, RockerCoverTightness = 63.5f };
        private static byte[] Raw(EngineBlockState state)
        {
            var prefix = new NetWriter(); new EngineBlockState { Revision = state.Revision, Flags = state.Flags, Wear = state.Wear }.Write(prefix);
            var w = new NetWriter(); foreach (byte value in prefix.ToArray().Take(148)) w.WriteByte(value);
            w.WriteBool(state.RockerCoverInstalled); w.WriteSingle(state.RockerCoverTightness); w.WriteBool(state.RadiatorInstalled); foreach (float value in new[] { state.RadiatorWear, state.RadiatorCoolant, state.RadiatorPressureCap, state.RadiatorFlectEfficiency }) w.WriteSingle(value); w.WriteByte(state.CoolantHoseFlags); foreach (float hose in state.CoolantHoseTightness) w.WriteSingle(hose); w.WriteSingle(state.CarburettorTightness); w.WriteByte(state.CoolingAirflowFlags); w.WriteSingle(state.GrilleAirflow); w.WriteSingle(state.HoodAirflow); w.WriteSingle(state.FiberglassHoodAirflow); w.WriteBool(state.CoolingAmbientAvailable); w.WriteSingle(state.CoolingAmbientTemperature); return w.ToArray();
        }
        private static void Reject(EngineBlockState state)
        {
            Assert.False(state.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(Raw(state))));
            Assert.False(new EngineBlockReplica().Receive(state)); Assert.Throws<ArgumentException>(() => new EngineBlockPublication().Observe(state));
        }
        [Fact]
        public void CoverAppendsStrictInstallationAndTightnessAfter148ExistingBytes()
        {
            var state = State(); var w = new NetWriter(); state.Write(w); var raw = w.ToArray(); Assert.Equal(209, raw.Length); Assert.Equal(Raw(state), raw);
            Assert.Equal(1, raw[148]); Assert.Equal(63.5f, BitConverter.ToSingle(raw, 149));
            var copy = (EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state)); Assert.True(copy.RockerCoverInstalled); Assert.Equal(63.5f, copy.RockerCoverTightness);
            for (int size = 148; size < 209; size++) Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(raw.Take(size).ToArray())));
            foreach (byte invalid in new byte[] { 2, 128, 255 }) { raw[148] = invalid; Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(raw))); }
        }
        [Theory] [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(7)]
        public void CoverRequiresInstalledHeadAndBlock(byte flags) { var state = State(); state.Flags = flags; state.Wear = (flags & 2) == 0 ? 0 : 90; Reject(state); }
        [Theory] [InlineData(11)] [InlineData(15)] [InlineData(27)] [InlineData(31)] [InlineData(43)] [InlineData(47)] [InlineData(59)] [InlineData(63)]
        public void CoverIsIndependentOfOtherAttachmentsAndBlockDamage(byte flags)
        { var state = State(); state.Flags = flags; Assert.True(state.Valid); Assert.True(((EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state))).RockerCoverInstalled); }
        [Theory] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void NonfiniteTightnessCannotReachWireCacheOrPublication(float value) { var state = State(); state.RockerCoverTightness = value; Reject(state); }
        [Theory] [InlineData(-8f)] [InlineData(0f)] [InlineData(64f)] [InlineData(72.5f)]
        public void FiniteNativeTightnessIsNotClamped(float value)
        { var state = State(); state.RockerCoverTightness = value; Assert.Equal(value, ((EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state))).RockerCoverTightness); }
        [Fact]
        public void AbsentCoverCannotCarryTightnessButInstalledZeroRemainsDistinct()
        {
            var state = State(); state.RockerCoverInstalled = false; Reject(state); state.RockerCoverInstalled = true; state.RockerCoverTightness = 0;
            var publication = new EngineBlockPublication(); var installed = publication.Observe(state); state.RockerCoverInstalled = false;
            var absent = publication.Observe(state); Assert.Equal(installed.Revision + 1, absent.Revision);
            var replica = new EngineBlockReplica(); Assert.True(replica.Receive(absent)); Assert.False(replica.Receive(installed)); installed.Revision = absent.Revision; Assert.False(replica.Receive(installed));
        }
        [Fact]
        public void CopiedTightnessUsesRevisionOrderingAndSnapshotsPreservePendingBroadcast()
        {
            var source = State(); var replica = new EngineBlockReplica(); Assert.True(replica.Receive(source)); source.RockerCoverTightness = 48; Assert.False(replica.Receive(source));
            var copy = replica.Get()!; copy.RockerCoverTightness = 48; Assert.False(replica.Receive(copy)); Assert.Equal(63.5f, replica.Get()!.RockerCoverTightness);
            source.Revision = 0; Assert.True(replica.Receive(source)); Assert.False(replica.Receive(State()));
            var publication = new EngineBlockPublication(); var first = publication.Observe(State()); publication.MarkBroadcast(first.Revision);
            var changed = publication.Observe(source); source.RockerCoverTightness = 32; changed.RockerCoverTightness = 1;
            var same = State(); same.RockerCoverTightness = 48; var snapshot = publication.Observe(same);
            Assert.Equal(first.Revision + 1, snapshot.Revision); Assert.Equal(changed.Revision, snapshot.Revision); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(first.Revision); Assert.True(publication.NeedsBroadcast); publication.MarkBroadcast(changed.Revision); Assert.False(publication.NeedsBroadcast);
        }
        [Fact]
        public void CatalogPinsNativeLeakReadAndMovingHeadProtection()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError); var inputs = parsed.GuestEngineInputs!;
            Assert.Equal(102, inputs.Entries.Count); Assert.Equal(182, inputs.Entries.Sum(e => e.Readers.Count));
            var entry = Assert.Single(inputs.Entries, e => e.RockerCoverSource != null); var cover = entry.RockerCoverSource!; Assert.Same(inputs.Block!.RockerCover, cover);
            Assert.Empty(entry.Families); Assert.False(entry.DirectTarget); Assert.Equal("Oil", entry.Fsm); Assert.Equal("db_Rockercover1", entry.TargetVariable);
            Assert.Equal("CORRIS/Simulation/Engine/Oil", entry.ReaderPath); Assert.Equal("VIN111", cover.RootPrefix); Assert.Equal("VIN118", cover.PartPrefix); Assert.Equal("Update 2", cover.ReadyState); Assert.Empty(cover.AlternatePartPrefixes);
            var pause = Assert.Single(parsed.GuestEngineProtection!.PausedFsms, p => p.RelativePath == "VINP_RockerCover"); Assert.Equal(cover.MountPath, pause.Path); Assert.Equal(10, pause.RequiredStates.Length); Assert.Equal(23, parsed.GuestEngineProtection.PausedFsms.Count);
            var read = Assert.Single(entry.Readers); Assert.Equal("Valve Cover", read.State); Assert.Equal(0, read.ActionIndex); Assert.Equal("GetFsmFloat", read.ActionType);
            Assert.Equal("Tightness", read.Variable); Assert.Equal("Tightness", read.Output); Assert.False(read.EveryFrame);
        }
        [Theory]
        [InlineData("profile")] [InlineData("mountPath")] [InlineData("rootPrefix")] [InlineData("relativePath")] [InlineData("partPrefix")] [InlineData("variants")] [InlineData("fsm")] [InlineData("readyState")]
        [InlineData("pause")] [InlineData("pause root")] [InlineData("pause state")] [InlineData("entry")] [InlineData("source")] [InlineData("readerPath")] [InlineData("targetVariable")] [InlineData("directTarget")] [InlineData("slotIndex")]
        [InlineData("state")] [InlineData("actionIndex")] [InlineData("variable")] [InlineData("output")] [InlineData("everyFrame")] [InlineData("actionType")] [InlineData("mountVariable")] [InlineData("alternateFamilies")]
        public void MalformedCoverDisablesOnlyEngineInputs(string fault)
        {
            var json = Catalog(); var inputs = json["guestEngineInputs"]!; var block = inputs["block"]!; var cover = block["rockerCover"]!; var entries = inputs["entries"]!.AsArray();
            var entry = entries.Single(e => e!["rockerCover"] != null)!; var read = entry["readers"]![0]!;
            var pauses = json["guestEngineProtection"]!["pausedFsms"]!.AsArray(); var pause = pauses.Single(p => p!["relativePath"]?.GetValue<string>() == "VINP_RockerCover")!;
            switch (fault)
            {
                case "profile": block.AsObject().Remove("rockerCover"); break;
                case "mountPath": case "rootPrefix": case "relativePath": case "partPrefix": case "fsm": case "readyState": cover[fault] = "Other"; break;
                case "variants": cover["alternatePartPrefixes"] = new JsonArray("VIN119"); break;
                case "pause": pauses.Remove(pause); break; case "pause root": pause["rootPrefix"] = "VIN101"; break; case "pause state": pause["requiredStates"]!.AsArray().RemoveAt(0); break;
                case "entry": entries.Remove(entry); break; case "source": entry["rockerCover"] = "Other"; break;
                case "readerPath": case "targetVariable": case "mountVariable": entry[fault] = "Other"; break; case "alternateFamilies": entry[fault] = new JsonArray("VIN118"); break;
                case "directTarget": entry[fault] = true; break; case "slotIndex": entry[fault] = 1; break;
                case "actionIndex": read[fault] = 1; break; case "everyFrame": read[fault] = true; break; default: read[fault] = "Other"; break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.GuestEngineInputsError!); Assert.NotNull(parsed.ReplacementParts); Assert.NotEmpty(parsed.Doors);
        }
    }
}
