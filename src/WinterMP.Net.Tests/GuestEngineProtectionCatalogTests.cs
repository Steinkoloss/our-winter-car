using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class GuestEngineProtectionCatalogTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonObject Profile(JsonNode json) => json["guestEngineProtection"]!.AsObject();
        private static JsonObject Writer(JsonNode json) => Profile(json)["writers"]![0]!.AsObject();
        private static JsonObject Action(JsonNode json) => Writer(json)["actions"]![0]!.AsObject();
        private static JsonObject Paused(JsonNode json) => Profile(json)["pausedFsms"]![0]!.AsObject();

        private static void AssertIsolatedFailure(JsonNode json)
        {
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.GuestEngineProtection);
            Assert.False(string.IsNullOrEmpty(parsed.GuestEngineProtectionError));
            Assert.NotEmpty(parsed.Doors);
            Assert.NotNull(parsed.VehicleDamage);
            Assert.Equal(39, parsed.ReplacementParts!.Factories.Count);
            Assert.NotNull(parsed.ShoppingBags);
        }

        [Fact]
        public void CatalogTargetsAuditedPersistentWritesAndPausesTheDestructiveFallGraph()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString());
            Assert.Null(parsed.GuestEngineProtectionError);
            var profile = Assert.IsType<GuestEngineProtectionData>(parsed.GuestEngineProtection);
            Assert.Equal(19, profile.Writers.Count);
            Assert.Equal(90, profile.Writers.Sum(w => w.Actions.Count));
            Assert.All(profile.Writers.SelectMany(w => w.Actions), a => {
                Assert.Equal("Data", a.TargetFsm);
                Assert.DoesNotContain(a.TargetScalar, new[] { "FuelLevel", "CarbReserve", "FuelChamber" });
            });
            var wearing = Assert.Single(profile.Writers, w => w.Path == "CORRIS/Simulation/Engine/Oil" && w.Fsm == "Wearing");
            Assert.Equal(14, wearing.Actions.Count);
            var firstBearing = Assert.Single(wearing.Actions, a => a.State == "Parts" && a.Index == 0);
            Assert.Equal("SubtractFsmFloat", firstBearing.ActionType);
            Assert.Equal("db_MainBearing1", firstBearing.TargetVariable);
            Assert.Equal("Wear", firstBearing.TargetScalar);
            var combustion = Assert.Single(profile.Writers, w => w.Fsm == "Cylinders");
            var distributor = Assert.Single(combustion.Actions, a => a.State == "Random move" && a.Index == 6);
            Assert.Equal("Distributor", distributor.TargetVariable);
            Assert.Equal("SparkAngle", distributor.TargetScalar);
            var pose = Assert.Single(combustion.PoseActions);
            Assert.Equal("Random move", pose.State); Assert.Equal(8, pose.Index);
            Assert.Equal("DistributorMesh", pose.TargetVariable); Assert.Equal("Angle", pose.AngleVariable);
            Assert.Equal(1, profile.Writers.Sum(w => w.PoseActions.Count));
            Assert.Equal(23, profile.PausedFsms.Count);
            var paused = Assert.Single(profile.PausedFsms, p => p.Fsm == "Logic");
            Assert.Equal("CORRIS/Simulation/Systems/PartFallings", paused.Path);
            Assert.Equal("Logic", paused.Fsm);
            Assert.Contains("Loosen part", paused.RequiredStates);
            Assert.Contains("Pick tire", paused.RequiredStates);
        }

        [Fact]
        public void MissingOptionalProfileDoesNotInvalidateTheRestOfTheCatalog()
        {
            var json = Catalog(); json.AsObject().Remove("guestEngineProtection");
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.GuestEngineProtection); Assert.Null(parsed.GuestEngineProtectionError);
            Assert.NotNull(parsed.VehicleDamage); Assert.Equal(39, parsed.ReplacementParts!.Factories.Count);
        }

        [Theory]
        [InlineData("CORRIS/PhysicalAssemblies/FRONT/AxleFront/LinkFL/ToeFL/DamagePivotFL/WHEELc_FL")]
        [InlineData("CORRIS/PhysicalAssemblies/FRONT/AxleFront/LinkFR/ToeFR/DamagePivotFR/WHEELc_FR")]
        [InlineData("CORRIS/PhysicalAssemblies/REAR/AxleDamagePivot/RearWheelsStatic/WHEELc_RL")]
        [InlineData("CORRIS/PhysicalAssemblies/REAR/AxleDamagePivot/RearWheelsStatic/WHEELc_RR")]
        public void EachWheelProtectsBothContinuousWearAndTheFlatEntryWrite(string path)
        {
            var profile = SyncCatalogJson.Parse(Catalog().ToJsonString()).GuestEngineProtection!;
            var wheel = Assert.Single(profile.Writers, w => w.Path == path && w.Fsm == "Condition");
            Assert.Equal(2, wheel.Actions.Count);
            Assert.All(wheel.Actions, a => {
                Assert.Equal("ThisTire", a.TargetVariable); Assert.Equal("Data", a.TargetFsm); Assert.Equal("TireHealth", a.TargetScalar);
            });
            var wear = Assert.Single(wheel.Actions, a => a.State == "State 1" && a.Index == 11);
            Assert.Equal("SubtractFsmFloat", wear.ActionType);
            var flat = Assert.Single(wheel.Actions, a => a.State == "Flat friction" && a.Index == 0);
            Assert.Equal("SetFsmFloat", flat.ActionType);
            Assert.Empty(wheel.PoseActions);
            Assert.DoesNotContain(profile.PausedFsms, p => p.Path == path && p.Fsm == "Condition");
        }

        [Fact]
        public void ReverseGearFailureCannotWriteTheGuestsSavedGearbox()
        {
            var profile = SyncCatalogJson.Parse(Catalog().ToJsonString()).GuestEngineProtection!;
            var writer = Assert.Single(profile.Writers, w => w.Path == "CORRIS/Simulation/Systems/Drivetrain/GearboxDamage" && w.Fsm == "Damage");
            var action = Assert.Single(writer.Actions);
            Assert.Equal("Reverse", action.State); Assert.Equal(6, action.Index); Assert.Equal("SubtractFsmFloat", action.ActionType);
            Assert.Equal("db_Gearbox", action.TargetVariable); Assert.Equal("Data", action.TargetFsm); Assert.Equal("Wear", action.TargetScalar);
        }

        [Theory]
        [InlineData(1, "db_Driveshaft")]
        [InlineData(3, "db_Gearbox")]
        [InlineData(5, "db_RearAxle")]
        public void PeriodicDrivetrainWearProtectsEachSavedMount(int index, string target)
        {
            var profile = SyncCatalogJson.Parse(Catalog().ToJsonString()).GuestEngineProtection!;
            var writer = Assert.Single(profile.Writers, w => w.Path == "CORRIS/Simulation/Systems/Drivetrain" && w.Fsm == "Wear");
            Assert.Equal(3, writer.Actions.Count);
            var action = Assert.Single(writer.Actions, a => a.Index == index);
            Assert.Equal("Wear", action.State); Assert.Equal("SubtractFsmFloat", action.ActionType);
            Assert.Equal(target, action.TargetVariable); Assert.Equal("Data", action.TargetFsm); Assert.Equal("Wear", action.TargetScalar);
            Assert.Empty(writer.PoseActions); Assert.Empty(writer.WheelHealthReads); Assert.Null(writer.GearboxConditionRead);
            Assert.DoesNotContain(profile.PausedFsms, p => p.Path == writer.Path && p.Fsm == writer.Fsm);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("1")]
        [InlineData("true")]
        [InlineData("{}")]
        public void MalformedProfileIsIsolatedFromOtherCatalogSubsystems(string value)
        {
            var json = Catalog(); json["guestEngineProtection"] = JsonNode.Parse(value);
            AssertIsolatedFailure(json);
        }

        [Theory]
        [InlineData("writers")]
        [InlineData("pausedFsms")]
        public void MissingEmptyOrWrongContainerListsCannotPretendToProtectTheEngine(string key)
        {
            var json = Catalog(); Profile(json).Remove(key); AssertIsolatedFailure(json);
            foreach (string invalid in new[] { "[]", "null", "{}", "[null]", "[1]" })
            {
                json = Catalog(); Profile(json)[key] = JsonNode.Parse(invalid); AssertIsolatedFailure(json);
            }
        }

        [Theory]
        [InlineData("writers", 33)]
        [InlineData("pausedFsms", 33)]
        public void ProtectionDiscoveryListsAreBounded(string key, int count)
        {
            var json = Catalog(); var entries = Profile(json)[key]!.AsArray();
            var example = entries[0]!.DeepClone(); entries.Clear();
            for (int i = 0; i < count; i++) { var item = example.DeepClone(); item["path"] = "Engine/" + i; entries.Add(item); }
            AssertIsolatedFailure(json);
        }

        [Theory]
        [InlineData("state")]
        [InlineData("actionType")]
        [InlineData("targetVariable")]
        [InlineData("targetFsm")]
        [InlineData("targetScalar")]
        public void ActionSignaturesRequireExactUsableNativeNames(string key)
        {
            var json = Catalog(); Action(json).Remove(key); AssertIsolatedFailure(json);
            foreach (var invalid in new JsonNode?[] { null, JsonValue.Create(3), JsonValue.Create(""),
                JsonValue.Create("  "), JsonValue.Create("native\nvalue"), JsonValue.Create("fsm:field"), JsonValue.Create(new string('x', 129)) })
            {
                json = Catalog(); Action(json)[key] = invalid; AssertIsolatedFailure(json);
            }
        }

        [Theory]
        [InlineData("writers")]
        [InlineData("pausedFsms")]
        public void FsmBindingsRequireNamesAndScenePaths(string list)
        {
            var json = Catalog(); Profile(json)[list]![0]!.AsObject().Remove("fsm"); AssertIsolatedFailure(json);
            foreach (string field in new[] { "path", "fsm" })
            {
                json = Catalog(); Profile(json)[list]![0]![field] = " "; AssertIsolatedFailure(json);
                json = Catalog(); Profile(json)[list]![0]![field] = 1; AssertIsolatedFailure(json);
            }
        }

        [Theory]
        [InlineData("/CORRIS/Engine")]
        [InlineData("CORRIS/Engine/")]
        [InlineData("CORRIS//Engine")]
        [InlineData("CORRIS/../Engine")]
        [InlineData("CORRIS/./Engine")]
        [InlineData("CORRIS/ /Engine")]
        [InlineData("CORRIS\\Engine")]
        [InlineData("CORRIS:Engine")]
        [InlineData("CORRIS/\tEngine")]
        public void PathsCannotEscapeOrBecomeAmbiguous(string path)
        {
            var json = Catalog(); Writer(json)["path"] = path; AssertIsolatedFailure(json);
            json = Catalog(); Paused(json)["path"] = path; AssertIsolatedFailure(json);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("-1")]
        [InlineData("256")]
        [InlineData("0.5")]
        [InlineData("true")]
        [InlineData("\"0\"")]
        public void ActionIndexMustBeABoundedInteger(string value)
        {
            var json = Catalog(); Action(json)["index"] = JsonNode.Parse(value); AssertIsolatedFailure(json);
        }

        [Fact]
        public void DuplicateActionsAndOverlappingPauseProfilesAreRejected()
        {
            var json = Catalog(); Writer(json)["actions"]!.AsArray().Add(Action(json).DeepClone()); AssertIsolatedFailure(json);
            json = Catalog(); Profile(json)["writers"]!.AsArray().Add(Writer(json).DeepClone()); AssertIsolatedFailure(json);
            json = Catalog(); Profile(json)["pausedFsms"]!.AsArray().Add(Paused(json).DeepClone()); AssertIsolatedFailure(json);
            json = Catalog(); Paused(json)["path"] = (string?)Writer(json)["path"]; Paused(json)["fsm"] = (string?)Writer(json)["fsm"];
            AssertIsolatedFailure(json);
        }

        [Fact]
        public void DifferentFsmsMayShareOneObjectAndOneScalarMayHaveSeveralWriters()
        {
            var json = Catalog(); var parsed = SyncCatalogJson.Parse(json.ToJsonString()).GuestEngineProtection!;
            Assert.Equal(2, parsed.Writers.Count(w => w.Path == "CORRIS/Simulation/Engine/Oil"));
            var action = Action(json).DeepClone(); action["index"] = 255;
            Writer(json)["actions"]!.AsArray().Add(action);
            Assert.Null(SyncCatalogJson.Parse(json.ToJsonString()).GuestEngineProtectionError);
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("null")]
        [InlineData("[null]")]
        [InlineData("[\"\"]")]
        [InlineData("[\" \" ]")]
        [InlineData("[\"Wait\",\"Wait\"]")]
        public void PausedGraphRequiresDistinctNativeStates(string value)
        {
            var json = Catalog(); Paused(json)["requiredStates"] = JsonNode.Parse(value); AssertIsolatedFailure(json);
        }

        [Fact]
        public void UnknownActionsAndUnboundedOrMissingActionListsAreRejected()
        {
            var json = Catalog(); Action(json)["actionType"] = "SetRotation"; AssertIsolatedFailure(json);
            json = Catalog(); Action(json).Remove("index"); AssertIsolatedFailure(json);
            json = Catalog(); Writer(json).Remove("actions"); AssertIsolatedFailure(json);
            json = Catalog(); Writer(json)["actions"] = new JsonArray(); AssertIsolatedFailure(json);
            json = Catalog(); var actions = Writer(json)["actions"]!.AsArray(); var example = Action(json).DeepClone(); actions.Clear();
            for (int i = 0; i < 257; i++) { var a = example.DeepClone(); a["state"] = "Write " + i; actions.Add(a); }
            AssertIsolatedFailure(json);
        }

        [Fact]
        public void PoseProtectionIsOptionalAndCannotOverlapAScalarWrite()
        {
            var json = Catalog(); Writer(json).Remove("poseActions");
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.GuestEngineProtectionError);
            Assert.Empty(parsed.GuestEngineProtection!.Writers[0].PoseActions);
            json = Catalog(); var poses = Writer(json)["poseActions"]!.AsArray();
            poses.Add(poses[0]!.DeepClone()); AssertIsolatedFailure(json);
            json = Catalog(); var pose = Writer(json)["poseActions"]![0]!;
            pose["state"] = (string?)Action(json)["state"]; pose["index"] = (int?)Action(json)["index"];
            AssertIsolatedFailure(json);
        }

        [Theory]
        [InlineData("state")]
        [InlineData("index")]
        [InlineData("targetVariable")]
        [InlineData("angleVariable")]
        public void PoseBindingCannotOmitAnyNativeSignatureField(string key)
        {
            var json = Catalog(); Writer(json)["poseActions"]![0]!.AsObject().Remove(key); AssertIsolatedFailure(json);
            json = Catalog(); Writer(json)["poseActions"]![0]![key] = null; AssertIsolatedFailure(json);
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("null")]
        [InlineData("{}")]
        [InlineData("[null]")]
        public void MalformedPoseListsAreIsolated(string value)
        {
            var json = Catalog(); Writer(json)["poseActions"] = JsonNode.Parse(value); AssertIsolatedFailure(json);
        }
    }
}
