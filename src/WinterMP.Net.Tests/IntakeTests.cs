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
    public class IntakeTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static readonly string[] Fields = { "CarburettorPower", "CarburettorTorque", "CarburettorPowerAdd", "AirCleanerPower", "AirCleanerTorque", "AirCleanerPowerAdd" };
        private static EngineBlockState State(byte flags = 59) => new EngineBlockState { Revision = uint.MaxValue, Flags = flags, Wear = (flags & 2) != 0 ? 90 : 0,
            FuelChamber = (flags & 16) != 0 ? 25 : 0, CarbReserve = (flags & 16) != 0 ? .1f : 0, SettingMixture = (flags & 16) != 0 ? 14.5f : 0,
            CarburettorPower = (flags & 16) != 0 ? 140 : 0, CarburettorTorque = (flags & 16) != 0 ? 210 : 0, CarburettorPowerAdd = (flags & 16) != 0 ? .07f : 0,
            AirCleanerPower = (flags & 32) != 0 ? 100 : 0, AirCleanerTorque = (flags & 32) != 0 ? 250 : 0, AirCleanerPowerAdd = (flags & 32) != 0 ? -.04f : 0 };
        private static void Set(EngineBlockState state, string field, float value) => typeof(EngineBlockState).GetField(field)!.SetValue(state, value);
        private static float Get(EngineBlockState state, string field) => (float)typeof(EngineBlockState).GetField(field)!.GetValue(state)!;
        private static byte[] Raw(EngineBlockState state)
        {
            var w = new NetWriter(); w.WriteUInt32(state.Revision); w.WriteByte(state.Flags); w.WriteSingle(state.Wear);
            w.WriteSingle(state.FuelChamber); w.WriteSingle(state.CarbReserve); w.WriteSingle(state.SettingMixture);
            foreach (string field in Fields) w.WriteSingle(Get(state, field));
            w.WriteByte(state.ExhaustFlags); foreach (float value in state.ExhaustPerformance) w.WriteSingle(value); w.WriteBool(state.ValvesAvailable); foreach (float value in state.ValveSettings) w.WriteSingle(value); w.WriteBool(state.OilpanInstalled); foreach (float value in new[] { state.OilpanWear, state.OilpanTightness, state.Oil, state.OilContamination, state.OilViscosity }) w.WriteSingle(value); w.WriteBool(state.RockerCoverInstalled); w.WriteSingle(state.RockerCoverTightness); w.WriteBool(state.RadiatorInstalled); foreach (float value in new[] { state.RadiatorWear, state.RadiatorCoolant, state.RadiatorPressureCap, state.RadiatorFlectEfficiency }) w.WriteSingle(value); w.WriteByte(state.CoolantHoseFlags); foreach (float hose in state.CoolantHoseTightness) w.WriteSingle(hose); w.WriteSingle(state.CarburettorTightness); w.WriteByte(state.CoolingAirflowFlags); w.WriteSingle(state.GrilleAirflow); w.WriteSingle(state.HoodAirflow); w.WriteSingle(state.FiberglassHoodAirflow); w.WriteBool(state.CoolingAmbientAvailable); w.WriteSingle(state.CoolingAmbientTemperature); return w.ToArray();
        }
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(7)] [InlineData(11)] [InlineData(15)]
        [InlineData(27)] [InlineData(31)] [InlineData(43)] [InlineData(47)] [InlineData(59)] [InlineData(63)]
        public void IndependentIntakeInstallationAndDamageRoundTripWithAppendedExhaustFields(byte flags)
        {
            var state = State(flags); var w = new NetWriter(); state.Write(w); Assert.Equal(Raw(state), w.ToArray()); Assert.Equal(209, w.ToArray().Length);
            var copy = (EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state)); Assert.Equal(flags, copy.Flags);
            foreach (string field in Fields) Assert.Equal(Get(state, field), Get(copy, field)); Assert.True(new EngineBlockReplica().Receive(copy));
        }
        [Theory]
        [InlineData(32)] [InlineData(33)] [InlineData(35)] [InlineData(39)] [InlineData(40)] [InlineData(41)] [InlineData(42)] [InlineData(48)] [InlineData(51)] [InlineData(127)]
        public void AirCleanerRequiresAvailableInstalledHeadAndBlock(byte flags) => Reject(State(flags));
        public static IEnumerable<object[]> NonfiniteFields() => Fields.SelectMany(f => new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity }.Select(v => new object[] { f, v }));
        [Theory] [MemberData(nameof(NonfiniteFields))]
        public void NonfiniteIntakeCannotEnterWireCacheOrPublication(string field, float value) { var state = State(); Set(state, field, value); Reject(state); }
        private static void Reject(EngineBlockState state)
        {
            Assert.False(state.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state)); Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(Raw(state))));
            Assert.False(new EngineBlockReplica().Receive(state)); Assert.Throws<ArgumentException>(() => new EngineBlockPublication().Observe(state));
        }
        [Theory]
        [InlineData("CarburettorPower")] [InlineData("CarburettorTorque")] [InlineData("CarburettorPowerAdd")]
        [InlineData("AirCleanerPower")] [InlineData("AirCleanerTorque")] [InlineData("AirCleanerPowerAdd")]
        public void EveryFieldIsZeroWhenItsPartIsAbsentAndPreservesFiniteValuesWhenPresent(string field)
        {
            var absent = State((byte)(field.StartsWith("Carburettor", StringComparison.Ordinal) ? 43 : 27)); Set(absent, field, .1f); Reject(absent);
            foreach (float value in new[] { -120f, 0, 800.5f }) { var state = State(); Set(state, field, value); var copy = (EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(state)); Assert.Equal(value, Get(copy, field)); }
        }
        [Theory]
        [InlineData("CarburettorPower")] [InlineData("CarburettorTorque")] [InlineData("CarburettorPowerAdd")]
        [InlineData("AirCleanerPower")] [InlineData("AirCleanerTorque")] [InlineData("AirCleanerPowerAdd")]
        public void PerformanceChangesUseCopiedRevisionOrderingAndSnapshotsCannotConsumeBroadcast(string field)
        {
            var source = State(); var replica = new EngineBlockReplica(); Assert.True(replica.Receive(source)); Set(source, field, 99); Assert.False(replica.Receive(source));
            var copy = replica.Get()!; Set(copy, field, 99); Assert.False(replica.Receive(copy)); Assert.True(replica.Receive(State()));
            source.Revision = 0; Assert.True(replica.Receive(source)); Assert.False(replica.Receive(State()));
            var publication = new EngineBlockPublication(); var first = publication.Observe(State()); publication.MarkBroadcast(first.Revision);
            var state = State(); Set(state, field, 99); var changed = publication.Observe(state); Assert.Equal(first.Revision + 1, changed.Revision);
            Set(state, field, 100); Set(changed, field, 101); var unchanged = State(); Set(unchanged, field, 99);
            Assert.Equal(changed.Revision, publication.Observe(unchanged).Revision); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(first.Revision); Assert.True(publication.NeedsBroadcast); publication.MarkBroadcast(changed.Revision); Assert.False(publication.NeedsBroadcast);
        }
        [Fact]
        public void TruncatedAppendedPerformanceFieldsCannotDecode()
        {
            var raw = Raw(State()); for (int size = 21; size < raw.Length; size++) Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(raw.Take(size).ToArray())));
        }
        [Fact]
        public void SevenNativeReadsAndMovingFilterProtectionHaveExactShape()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            var inputs = parsed.GuestEngineInputs!; var entries = inputs.Entries.Where(e => e.AirCleanerSource != null || e.CarburettorSource != null && e.Fsm == "Valves").ToArray();
            Assert.Equal(3, entries.Length); Assert.Equal(7, entries.Sum(e => e.Readers.Count)); Assert.All(entries, e => { Assert.Empty(e.Families); Assert.False(e.DirectTarget); });
            var air = inputs.Block!.AirCleaner; Assert.Equal("VIN135", air.PartPrefix); Assert.Empty(air.AlternatePartPrefixes); Assert.Equal("VIN111", air.RootPrefix);
            var pause = Assert.Single(parsed.GuestEngineProtection!.PausedFsms, p => p.RelativePath == "VINP_AirCleaner"); Assert.Equal(10, pause.RequiredStates.Length); Assert.Equal(air.MountPath, pause.Path);
            var fuel = entries.Single(e => e.Fsm == "FuelLine"); Assert.Equal("db_Airfilter", fuel.TargetVariable); var filterRead = Assert.Single(fuel.Readers);
            Assert.Equal("Airfilter", filterRead.State); Assert.Equal(1, filterRead.ActionIndex); Assert.Equal("Installed", filterRead.Variable); Assert.Equal("Installed1", filterRead.Output);
            foreach (var entry in entries.Where(e => e.Fsm == "Valves"))
            {
                Assert.Equal(new[] { 0, 1, 2 }, entry.Readers.Select(r => r.ActionIndex)); Assert.Equal(new[] { "DataPower", "DataTorque", "DataPowerAdd" }, entry.Readers.Select(r => r.Variable));
                Assert.Equal(new[] { "PartPower", "PartTorque", "PartPowerAdd" }, entry.Readers.Select(r => r.Output));
                Assert.All(entry.Readers, r => Assert.Equal(entry.AirCleanerSource != null ? "AirFilter" : "Carburettor", r.State));
            }
            Assert.All(entries.SelectMany(e => e.Readers), r => Assert.False(r.EveryFrame));
        }
        [Theory]
        [InlineData("profile")] [InlineData("mount")] [InlineData("root")] [InlineData("relative")] [InlineData("part")] [InlineData("variants")] [InlineData("fsm")] [InlineData("ready")]
        [InlineData("pause missing")] [InlineData("pause root")] [InlineData("pause state")] [InlineData("fuel entry")] [InlineData("valves entry")] [InlineData("source")]
        [InlineData("reader")] [InlineData("consumer")] [InlineData("target")] [InlineData("direct")] [InlineData("family")] [InlineData("slot")]
        [InlineData("index")] [InlineData("state")] [InlineData("output")] [InlineData("cadence")] [InlineData("field")] [InlineData("type")]
        [InlineData("carb field")] [InlineData("carb output")] [InlineData("carb state")] [InlineData("carb cadence")] [InlineData("carb entry")]
        public void MalformedIntakeFailsLocallyWithoutDiscardingOtherCatalogSystems(string fault)
        {
            var json = Catalog(); var inputs = json["guestEngineInputs"]!; var block = inputs["block"]!; var air = block["airCleaner"]!; var entries = inputs["entries"]!.AsArray();
            var entry = entries.Single(e => e!["airCleaner"] != null && e["fsm"]!.GetValue<string>() == "Valves")!;
            var fuel = entries.Single(e => e!["airCleaner"] != null && e["fsm"]!.GetValue<string>() == "FuelLine")!;
            var carb = entries.Single(e => e!["carburettor"] != null && e["fsm"]!.GetValue<string>() == "Valves")!; var read = entry["readers"]![0]!;
            var pauses = json["guestEngineProtection"]!["pausedFsms"]!.AsArray(); var pause = pauses.Single(p => p!["relativePath"]?.GetValue<string>() == "VINP_AirCleaner")!;
            switch (fault)
            {
                case "profile": block.AsObject().Remove("airCleaner"); break;
                case "mount": air["mountPath"] = "CORRIS/Other"; break; case "root": air["rootPrefix"] = "VIN101"; break;
                case "relative": air["relativePath"] = "VINP_Carburettor"; break; case "part": air["partPrefix"] = "VIN114"; break;
                case "variants": air["alternatePartPrefixes"] = new JsonArray("VIN114"); break;
                case "fsm": air["fsm"] = "Status"; break; case "ready": air["readyState"] = "Installed"; break;
                case "pause missing": pauses.Remove(pause); break; case "pause root": pause["rootPrefix"] = "VIN101"; break; case "pause state": pause["requiredStates"]!.AsArray().RemoveAt(0); break;
                case "fuel entry": entries.Remove(fuel); break; case "valves entry": entries.Remove(entry); break; case "source": entry["airCleaner"] = "Other"; break;
                case "reader": entry["readerPath"] = "CORRIS/Other"; break; case "consumer": entry["fsm"] = "Oil"; break; case "target": entry["targetVariable"] = "db_Carburettor"; break;
                case "direct": entry["directTarget"] = true; break; case "family": entry["familyPrefix"] = "VIN135"; break; case "slot": entry["slotIndex"] = 1; break;
                case "index": read["actionIndex"] = 4; break; case "state": read["state"] = "Carburettor"; break; case "output": read["output"] = "PowerAdd"; break;
                case "cadence": read["everyFrame"] = true; break; case "field": read["variable"] = "Wear"; break; case "type": read["actionType"] = "GetFsmBool"; break;
                case "carb field": carb["readers"]![1]!["variable"] = "DataPower"; break; case "carb output": carb["readers"]![2]!["output"] = "PartPower"; break;
                case "carb state": carb["readers"]![0]!["state"] = "AirFilter"; break; case "carb cadence": carb["readers"]![2]!["everyFrame"] = true; break; case "carb entry": entries.Remove(carb); break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.GuestEngineInputsError!); Assert.NotNull(parsed.ReplacementParts); Assert.NotEmpty(parsed.Doors);
        }
    }
}
