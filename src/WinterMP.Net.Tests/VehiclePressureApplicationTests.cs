using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class VehiclePressureApplicationTests
    {
        [Theory]
        [InlineData(0, 0, false, true)] [InlineData(0, 255, false, true)] [InlineData(1, 1, false, true)]
        [InlineData(1, 255, false, false)] [InlineData(1, 2, false, false)] [InlineData(0, 1, false, false)]
        [InlineData(1, 1, true, false)] [InlineData(0, 255, true, false)]
        public void PressureUsesTheSameEstablishedOwnerAsHealth(byte source, byte owner, bool local, bool allowed)
        {
            var state = new VehicleCondition { Availability = VehicleCondition.AvailableAll, VehicleId = 12, OwnerPlayerId = source, TirePressure = 230 };
            Assert.Equal(allowed, VehicleConditionStreamPolicy.TryGetObserverPressure(state, 12, local, owner, out byte pressure));
            Assert.Equal(allowed ? 230 : 0, pressure);
        }

        [Theory]
        [InlineData(0)] [InlineData(190)] [InlineData(255)]
        public void ParkedCopyRetainsExactNativeUnitsWithoutMutatingThePacket(byte value)
        {
            var state = new VehicleCondition { Availability = VehicleCondition.AvailableAll, VehicleId = 12, OwnerPlayerId = 1, TirePressure = value };
            var parked = VehicleConditionStreamPolicy.CaptureReleasedCondition(state, 1, new ItemTransform {
                ItemId = 12, OwnerPlayerId = 1, Flags = ItemTransform.FlagFinal });
            var bytes = PacketCodec.Encode(state);
            Assert.True(VehicleConditionStreamPolicy.TryGetParkedObserverPressure(parked, 12, false, 255, out byte pressure));
            Assert.Equal(value, pressure); Assert.Equal(bytes, PacketCodec.Encode(state));
        }

        [Fact]
        public void MissingInvalidWrongVehicleAndNewOwnershipCannotSupplyPressure()
        {
            var state = new VehicleCondition { Availability = VehicleCondition.AvailableAll, VehicleId = 12, OwnerPlayerId = 1, TirePressure = 230 };
            Assert.False(VehicleConditionStreamPolicy.TryGetObserverPressure(null, 12, false, 1, out _));
            Assert.False(VehicleConditionStreamPolicy.TryGetObserverPressure(state, 13, false, 1, out _));
            Assert.False(VehicleConditionStreamPolicy.TryGetParkedObserverPressure(null, 12, false, 255, out _));
            Assert.False(VehicleConditionStreamPolicy.TryGetParkedObserverPressure(state, 13, false, 255, out _));
            Assert.False(VehicleConditionStreamPolicy.TryGetParkedObserverPressure(state, 12, true, 255, out _));
            Assert.False(VehicleConditionStreamPolicy.TryGetParkedObserverPressure(state, 12, false, 2, out _));
            state.Flags = 17;
            Assert.False(VehicleConditionStreamPolicy.TryGetObserverPressure(state, 12, false, 1, out _));
            Assert.False(VehicleConditionStreamPolicy.TryGetParkedObserverPressure(state, 12, false, 255, out _));
        }

        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static void Invalid(JsonNode json)
        {
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.VehicleTirePressure); Assert.NotNull(parsed.VehicleTirePressureError);
            Assert.NotNull(parsed.VehicleTemperature); Assert.NotNull(parsed.GuestEngineProtection); Assert.NotEmpty(parsed.Doors);
        }

        [Fact]
        public void CatalogBindsTheEightAuditedPropertiesToFourDistinctWheels()
        {
            var rule = SyncCatalogJson.Parse(Catalog().ToJsonString()).VehicleTirePressure!;
            Assert.Equal("CORRIS", rule.RootPath); Assert.Equal("CORRIS/Simulation/Systems/TirePressure", rule.Path);
            Assert.Equal("Data", rule.Fsm); Assert.Equal("WheelFriction", rule.State); Assert.Equal("TIRES", rule.Event);
            Assert.Equal("Pressure", rule.Pressure); Assert.Equal("PressureOptimal", rule.Optimum);
            Assert.Equal(4, rule.Wheels.Count);
            for (int i = 0; i < 4; i++)
            {
                string suffix = new[] { "FL", "FR", "RL", "RR" }[i];
                Assert.Equal("Wheel" + suffix, rule.Wheels[i].ObjectVariable); Assert.EndsWith("/WHEELc_" + suffix, rule.Wheels[i].Path);
                Assert.Equal(i * 2, rule.Wheels[i].PressureIndex); Assert.Equal(i * 2 + 1, rule.Wheels[i].OptimumIndex);
                Assert.Equal(i == 0, rule.Wheels[i].Enabled);
            }
        }

        [Theory]
        [InlineData("null")] [InlineData("true")] [InlineData("[]")] [InlineData("{}")]
        public void MalformedProfileOnlyDisablesPressureApplication(string value)
        { var json = Catalog(); json["vehicleTirePressure"] = JsonNode.Parse(value); Invalid(json); }

        [Theory]
        [InlineData("rootPath")] [InlineData("path")] [InlineData("fsm")] [InlineData("state")]
        [InlineData("event")] [InlineData("pressure")] [InlineData("optimum")]
        public void MissingRequiredNamesCannotPartiallyBindTheGraph(string key)
        { var json = Catalog(); json["vehicleTirePressure"]!.AsObject().Remove(key); Invalid(json); }

        [Theory]
        [InlineData("sourceOutside")] [InlineData("wheelOutside")] [InlineData("sameScalar")] [InlineData("samePath")]
        [InlineData("sameTarget")] [InlineData("sameIndex")] [InlineData("extraWheel")] [InlineData("missingWheel")]
        [InlineData("largeIndex")] [InlineData("negativeIndex")] [InlineData("fractionIndex")]
        [InlineData("missingEnabled")] [InlineData("invalidEnabled")]
        public void AmbiguousAndIncompleteBindingsAreRejected(string kind)
        {
            var json = Catalog(); var r = json["vehicleTirePressure"]!; var wheels = r["wheels"]!.AsArray(); var first = wheels[0]!;
            switch (kind)
            {
                case "sourceOutside": r["path"] = "ANOTHERCAR/Pressure"; break;
                case "wheelOutside": first["path"] = "ANOTHERCAR/Wheel"; break;
                case "sameScalar": r["optimum"] = r["pressure"]!.DeepClone(); break;
                case "samePath": wheels[1]!["path"] = first["path"]!.DeepClone(); break;
                case "sameTarget": wheels[1]!["objectVariable"] = first["objectVariable"]!.DeepClone(); break;
                case "sameIndex": first["optimumIndex"] = first["pressureIndex"]!.DeepClone(); break;
                case "extraWheel": wheels.Add(first.DeepClone()); break;
                case "missingWheel": wheels.RemoveAt(3); break;
                case "largeIndex": first["pressureIndex"] = 8; break;
                case "negativeIndex": first["pressureIndex"] = -1; break;
                case "fractionIndex": first["pressureIndex"] = 0.5; break;
                case "missingEnabled": first.AsObject().Remove("enabled"); break;
                case "invalidEnabled": first["enabled"] = "false"; break;
            }
            Invalid(json);
        }
    }
}
