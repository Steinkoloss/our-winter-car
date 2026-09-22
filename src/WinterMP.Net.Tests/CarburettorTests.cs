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
    public class CarburettorTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static EngineBlockState State() => new EngineBlockState { Revision = uint.MaxValue, Flags = 27, Wear = 90, FuelChamber = 25, CarbReserve = .1f, SettingMixture = 14.5f };
        private static void Change(EngineBlockState state, string field, float value)
        {
            if (field == "FuelChamber") state.FuelChamber = value;
            else if (field == "CarbReserve") state.CarbReserve = value;
            else state.SettingMixture = value;
        }
        [Theory]
        [InlineData(27, -1, -.1f, -2)] [InlineData(31, 35, .25f, 18)] [InlineData(27, 25, .1f, 14.5f)]
        public void FiniteNativeInputsAppendTwelveBytesWithoutPrematureClamps(byte flags, float chamber, float reserve, float mixture)
        {
            var state = State(); state.Flags = flags; state.FuelChamber = chamber; state.CarbReserve = reserve; state.SettingMixture = mixture;
            var writer = new NetWriter(); state.Write(writer); Assert.Equal(209, writer.ToArray().Length);
            var copy = (EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state)); Assert.Equal(flags, copy.Flags);
            Assert.Equal(90, copy.Wear); Assert.Equal(chamber, copy.FuelChamber); Assert.Equal(reserve, copy.CarbReserve); Assert.Equal(mixture, copy.SettingMixture);
            var reader = new NetReader(writer.ToArray()); Assert.Equal(uint.MaxValue, reader.ReadUInt32()); Assert.Equal(flags, reader.ReadByte());
            Assert.Equal(90, reader.ReadSingle()); Assert.Equal(chamber, reader.ReadSingle()); Assert.Equal(reserve, reader.ReadSingle()); Assert.Equal(mixture, reader.ReadSingle());
        }
        [Theory]
        [InlineData(16)] [InlineData(17)] [InlineData(19)] [InlineData(23)] [InlineData(25)] [InlineData(26)] [InlineData(28)] [InlineData(29)] [InlineData(30)] [InlineData(123)]
        public void CarburettorRequiresAvailableInstalledBlockAndHead(byte flags)
        {
            var state = State(); state.Flags = flags; Reject(state);
        }
        [Theory]
        [InlineData("FuelChamber", float.NaN)] [InlineData("FuelChamber", float.PositiveInfinity)] [InlineData("FuelChamber", float.NegativeInfinity)]
        [InlineData("CarbReserve", float.NaN)] [InlineData("CarbReserve", float.PositiveInfinity)] [InlineData("CarbReserve", float.NegativeInfinity)]
        [InlineData("SettingMixture", float.NaN)] [InlineData("SettingMixture", float.PositiveInfinity)] [InlineData("SettingMixture", float.NegativeInfinity)]
        public void EveryNonfiniteScalarIsRejected(string field, float value) { var state = State(); Change(state, field, value); Reject(state); }
        [Theory]
        [InlineData("FuelChamber")] [InlineData("CarbReserve")] [InlineData("SettingMixture")]
        public void MissingCarburettorCannotRetainAnyFuelOrTuning(string field)
        {
            foreach (byte flags in new byte[] { 0, 1, 3, 7, 11, 15 })
            {
                var state = new EngineBlockState { Revision = 1, Flags = flags }; Change(state, field, .1f); Reject(state);
            }
        }
        private static void Reject(EngineBlockState state)
        {
            Assert.False(state.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state)); Assert.False(new EngineBlockReplica().Receive(state));
            Assert.Throws<ArgumentException>(() => new EngineBlockPublication().Observe(state.Flags, state.Wear, state.FuelChamber, state.CarbReserve, state.SettingMixture));
            var writer = new NetWriter(); writer.WriteUInt32(state.Revision); writer.WriteByte(state.Flags); writer.WriteSingle(state.Wear);
            writer.WriteSingle(state.FuelChamber); writer.WriteSingle(state.CarbReserve); writer.WriteSingle(state.SettingMixture);
            for (int i = 0; i < 6; i++) writer.WriteSingle(0);
            writer.WriteByte(0); for (int i = 0; i < 12; i++) writer.WriteSingle(0); writer.WriteBool(false); for (int i = 0; i < 8; i++) writer.WriteSingle(0); writer.WriteBool(false); for (int i = 0; i < 5; i++) writer.WriteSingle(0); writer.WriteBool(false); writer.WriteSingle(0); writer.WriteBool(false); for (int i = 0; i < 4; i++) writer.WriteSingle(0); writer.WriteByte(0); for (int i = 0; i < 5; i++) writer.WriteSingle(0);
            Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(writer.ToArray())));
        }
        [Theory]
        [InlineData("FuelChamber")] [InlineData("CarbReserve")] [InlineData("SettingMixture")]
        public void EachFieldIsCopiedRejectsConflictsAndSurvivesPublicationSnapshots(string field)
        {
            var source = State(); var replica = new EngineBlockReplica(); Assert.True(replica.Receive(source));
            Change(source, field, 99); Assert.False(replica.Receive(source)); var copy = replica.Get()!; Change(copy, field, 99); Assert.False(replica.Receive(copy));
            Assert.True(replica.Receive(State())); source.Revision = 0; Assert.True(replica.Receive(source)); Assert.False(replica.Receive(State()));
            var publication = new EngineBlockPublication(); var first = publication.Observe(27, 90, 25, .1f, 14.5f); publication.MarkBroadcast(first.Revision);
            var changed = first.Copy(); Change(changed, field, 99);
            var next = publication.Observe(changed.Flags, changed.Wear, changed.FuelChamber, changed.CarbReserve, changed.SettingMixture);
            Assert.Equal(first.Revision + 1, next.Revision); Assert.True(publication.NeedsBroadcast); Change(next, field, 100);
            var snapshot = publication.Observe(changed.Flags, changed.Wear, changed.FuelChamber, changed.CarbReserve, changed.SettingMixture);
            Assert.Equal(next.Revision, snapshot.Revision); Assert.True(publication.NeedsBroadcast); publication.MarkBroadcast(first.Revision); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(snapshot.Revision); Assert.False(publication.NeedsBroadcast);
        }
        [Fact]
        public void BothConsumersPinTheFourNativeReadsAndAllCarburettorMountStates()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            var entries = parsed.GuestEngineInputs!.Entries.Where(e => e.CarburettorSource != null && (e.Fsm == "FuelLine" || e.Fsm == "Mixture")).ToArray(); Assert.Equal(2, entries.Length);
            var rule = parsed.GuestEngineInputs.Block!.Carburettor; Assert.Equal(new[] { "CARB2BRLa0", "CARB4BRLa0" }, rule.AlternatePartPrefixes);
            Assert.Equal("VIN113", rule.PartPrefix); Assert.Equal("VIN111", rule.RootPrefix); Assert.Equal("Update 2", rule.ReadyState);
            Assert.All(entries, e => { Assert.Same(rule, e.CarburettorSource); Assert.Empty(e.Families); Assert.False(e.DirectTarget); });
            var fuel = entries.Single(e => e.Fsm == "FuelLine"); Assert.Equal("db_Carb1", fuel.TargetVariable);
            Assert.Equal(new[] { 0, 2, 4 }, fuel.Readers.Select(r => r.ActionIndex)); Assert.Equal(new[] { "Installed", "FuelChamber", "CarbReserve" }, fuel.Readers.Select(r => r.Variable));
            Assert.All(fuel.Readers, r => { Assert.Equal("Carburator", r.State); Assert.False(r.EveryFrame); });
            var mixture = entries.Single(e => e.Fsm == "Mixture"); Assert.Equal("db_Carburettor", mixture.TargetVariable); var read = Assert.Single(mixture.Readers);
            Assert.Equal("Calculate density", read.State); Assert.Equal(0, read.ActionIndex); Assert.Equal("SettingMixture", read.Variable); Assert.Equal("CarbSetting", read.Output); Assert.False(read.EveryFrame);
            Assert.Equal(14, Assert.Single(parsed.GuestEngineProtection!.PausedFsms, p => p.RelativePath == "VINP_Carburettor").RequiredStates.Length);
        }
        [Theory]
        [InlineData("profile")] [InlineData("mount")] [InlineData("root")] [InlineData("relative")] [InlineData("part")] [InlineData("variant missing")] [InlineData("variant foreign")]
        [InlineData("fsm")] [InlineData("ready")] [InlineData("pause missing")] [InlineData("pause root")] [InlineData("pause state")]
        [InlineData("entry")] [InlineData("source")] [InlineData("reader")] [InlineData("consumer")] [InlineData("target")] [InlineData("direct")] [InlineData("family")]
        [InlineData("slot")] [InlineData("index")] [InlineData("state")] [InlineData("output")] [InlineData("cadence")] [InlineData("field")] [InlineData("type")]
        [InlineData("mixture index")] [InlineData("mixture field")] [InlineData("mixture output")]
        public void ForeignOrIncompleteCarburettorProfilesFailWithinInputProtection(string fault)
        {
            var json = Catalog(); var inputs = json["guestEngineInputs"]!; var block = inputs["block"]!; var carb = block["carburettor"]!;
            var entries = inputs["entries"]!.AsArray(); var entry = entries.Single(e => e!["carburettor"] != null && e["fsm"]!.GetValue<string>() == "FuelLine")!;
            var read = entry["readers"]![0]!; var mix = entries.Single(e => e!["carburettor"] != null && e["fsm"]!.GetValue<string>() == "Mixture")!["readers"]![0]!;
            var pauses = json["guestEngineProtection"]!["pausedFsms"]!.AsArray(); var pause = pauses.Single(p => p!["relativePath"]?.GetValue<string>() == "VINP_Carburettor")!;
            switch (fault)
            {
                case "profile": block.AsObject().Remove("carburettor"); break;
                case "mount": carb["mountPath"] = "CORRIS/Other"; break;
                case "root": carb["rootPrefix"] = "VIN101"; break;
                case "relative": carb["relativePath"] = "VINP_AirCleaner"; break;
                case "part": carb["partPrefix"] = "VIN114"; break;
                case "variant missing": carb.AsObject().Remove("alternatePartPrefixes"); break;
                case "variant foreign": carb["alternatePartPrefixes"]![0] = "CARB2BRLa"; break;
                case "fsm": carb["fsm"] = "Status"; break;
                case "ready": carb["readyState"] = "Installed"; break;
                case "pause missing": pauses.Remove(pause); break;
                case "pause root": pause["rootPrefix"] = "VIN101"; break;
                case "pause state": pause["requiredStates"]!.AsArray().RemoveAt(0); break;
                case "entry": entries.Remove(entry); break;
                case "source": entry["carburettor"] = "Other"; break;
                case "reader": entry["readerPath"] = "CORRIS/Other"; break;
                case "consumer": entry["fsm"] = "Oil"; break;
                case "target": entry["targetVariable"] = "db_Carburettor"; break;
                case "direct": entry["directTarget"] = true; break;
                case "family": entry["familyPrefix"] = "VIN113"; break;
                case "slot": entry["slotIndex"] = 1; break;
                case "index": read["actionIndex"] = 1; break;
                case "state": read["state"] = "Fuel Pump"; break;
                case "output": read["output"] = "Installed2"; break;
                case "cadence": read["everyFrame"] = true; break;
                case "field": read["variable"] = "Wear"; break;
                case "type": read["actionType"] = "GetFsmFloat"; break;
                case "mixture index": mix["actionIndex"] = 2; break;
                case "mixture field": mix["variable"] = "CarbReserve"; break;
                case "mixture output": mix["output"] = "Multiplier"; break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.GuestEngineInputsError!);
            Assert.NotNull(parsed.ReplacementParts); Assert.NotEmpty(parsed.Doors);
        }
    }
}
