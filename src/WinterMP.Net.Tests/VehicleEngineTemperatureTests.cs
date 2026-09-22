using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class VehicleEngineTemperatureTests
    {
        private static VehicleCoolantState State(float engine, uint revision = 1) => new VehicleCoolantState {
            VehicleId = 42, Revision = revision, Flags = 1, Celsius = 87.125f, EngineCelsius = engine };
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;

        [Theory]
        [InlineData(-25.375f)] [InlineData(0f)] [InlineData(3f)] [InlineData(90.125f)]
        [InlineData(220f)] [InlineData(float.MaxValue)] [InlineData(float.MinValue)]
        public void EngineTemperatureAppendsASeparateNativeFloat(float engine)
        {
            var bytes = PacketCodec.Encode(State(engine)); Assert.Equal(19, bytes.Length);
            Assert.Equal(87.125f, BitConverter.ToSingle(bytes, 11)); Assert.Equal(engine, BitConverter.ToSingle(bytes, 15));
            var copy = Assert.IsType<VehicleCoolantState>(PacketCodec.Decode(bytes));
            Assert.Equal(engine, copy.EngineCelsius); Assert.Equal(87.125f, copy.Celsius);
            var old = new byte[15]; Array.Copy(bytes, old, old.Length); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(old));
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void NonfiniteEngineHeatCannotHideBehindValidCoolant(float engine)
        {
            Assert.False(State(engine).Valid); Assert.False(new VehicleCoolantReplica().Receive(State(engine)));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(State(engine)));
            var bytes = PacketCodec.Encode(State(80)); Array.Copy(BitConverter.GetBytes(engine), 0, bytes, 15, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            Assert.Throws<ArgumentException>(() => new VehicleCoolantPublication().Observe(42, 1, 87, engine));
        }

        [Fact]
        public void UnavailableRequiresBothTemperaturesToBeZero()
        {
            var state = State(80); state.Flags = 0; state.Celsius = 0;
            Assert.False(state.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            state.EngineCelsius = 0; Assert.True(state.Valid);
        }

        [Fact]
        public void EngineOnlyChangesAdvancePublicationAndRejectConflictingReplays()
        {
            var publication = new VehicleCoolantPublication(); var first = publication.Observe(42, 1, 87, 80);
            publication.MarkBroadcast(first.Revision); var next = publication.Observe(42, 1, 87, 81);
            Assert.Equal(first.Revision + 1, next.Revision); Assert.True(publication.NeedsBroadcast);
            var replica = new VehicleCoolantReplica(); Assert.True(replica.Receive(next)); next.EngineCelsius = 222;
            Assert.Equal(81, replica.Get()!.EngineCelsius); replica.Get()!.EngineCelsius = 333;
            var equal = publication.Observe(42, 1, 87, 81); Assert.True(replica.Receive(equal));
            equal.EngineCelsius = 82; Assert.False(replica.Receive(equal)); Assert.False(replica.Receive(first));
            var unavailable = publication.Observe(42, 0, 0, 0); Assert.True(replica.Receive(unavailable));
            Assert.Equal(0, replica.Get()!.EngineCelsius); Assert.Equal(0, replica.Get()!.Celsius);
        }

        [Fact]
        public void NativeCatalogIdentifiesDistinctFuelMixtureOilAndPressureReaders()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.VehicleTemperatureError);
            var source = parsed.VehicleTemperature!.Sources[0]; Assert.Equal("EngineTemp", source.EngineGlobal);
            var inputs = source.EngineInputs!; Assert.Equal("CORRIS/Simulation/Engine/Fuel", inputs.FuelPath);
            Assert.Equal("CORRIS/Simulation/Engine/Oil", inputs.OilPath); Assert.Equal("FuelLine", inputs.FuelFsm);
            Assert.Equal("Mixture", inputs.MixtureFsm); Assert.Equal("Oil", inputs.OilFsm); Assert.Equal("Pressure", inputs.PressureFsm);
            Assert.Equal("Carburator", inputs.CarburettorState); Assert.Equal("Priming", inputs.PrimingState);
            Assert.Equal("Calculate density", inputs.DensityState); Assert.Equal("Viscosity", inputs.ViscosityState);
            Assert.Equal("Oil pressure", inputs.PressureState);
        }

        [Theory]
        [InlineData("fuelPath")] [InlineData("oilPath")] [InlineData("fuelFsm")] [InlineData("mixtureFsm")]
        [InlineData("oilFsm")] [InlineData("pressureFsm")] [InlineData("carburettorState")] [InlineData("primingState")]
        [InlineData("densityState")] [InlineData("viscosityState")] [InlineData("pressureState")]
        public void EngineReaderGroupRequiresEveryBinding(string field)
        {
            var json = Catalog(); json["vehicleTemperature"]!["sources"]![0]!["engineInputs"]!.AsObject().Remove(field); Reject(json);
        }

        [Theory]
        [InlineData("missing global")] [InlineData("missing inputs")] [InlineData("alias global")]
        [InlineData("foreign fuel")] [InlineData("foreign oil")] [InlineData("alias path")]
        [InlineData("alias fuel")] [InlineData("alias oil")] [InlineData("alias state")]
        [InlineData("driver global")] [InlineData("driver inputs")]
        public void ForeignAmbiguousAndIncompleteEngineProfilesFailAsAGroup(string fault)
        {
            var json = Catalog(); var sources = json["vehicleTemperature"]!["sources"]!; var source = sources[0]!; var inputs = source["engineInputs"]!;
            switch (fault)
            {
                case "missing global": source.AsObject().Remove("engineGlobal"); break;
                case "missing inputs": source.AsObject().Remove("engineInputs"); break;
                case "alias global": source["engineGlobal"] = "CoolantTemp"; break;
                case "foreign fuel": inputs["fuelPath"] = "SORBET/Fuel"; break;
                case "foreign oil": inputs["oilPath"] = "SORBET/Oil"; break;
                case "alias path": inputs["fuelPath"] = "CORRIS/Simulation/Engine/Oil"; break;
                case "alias fuel": inputs["mixtureFsm"] = "FuelLine"; break;
                case "alias oil": inputs["pressureFsm"] = "Oil"; break;
                case "alias state": inputs["primingState"] = "Carburator"; break;
                case "driver global": sources[1]!["engineGlobal"] = "EngineTemp"; break;
                case "driver inputs": sources[1]!["engineInputs"] = inputs.DeepClone(); break;
            }
            Reject(json);
        }

        private static void Reject(JsonNode json)
        {
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.VehicleTemperature);
            Assert.NotEmpty(parsed.VehicleTemperatureError!); Assert.NotNull(parsed.VehicleCooling);
            Assert.NotNull(parsed.GuestEngineInputs); Assert.NotNull(parsed.GuestEngineProtection); Assert.NotEmpty(parsed.Doors);
        }
    }
}
