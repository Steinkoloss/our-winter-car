using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class GearboxConditionReadTests
{
    private static VehicleCondition State(byte owner = 1, byte value = 2) => new() {
        VehicleId = 12, OwnerPlayerId = owner, DrivetrainDamage = value, Availability = VehicleCondition.AvailableDrivetrain };

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(255)]
    public void LiveAndApprovedParkedReadsPreserveKnownZeroAndByteValues(byte value)
    {
        var state = State(value: value); var bytes = PacketCodec.Encode(state);
        Assert.True(VehicleConditionStreamPolicy.TryGetObserverDrivetrainDamage(state, 12, false, 1, out byte damage)); Assert.Equal(value, damage);
        var parked = VehicleConditionStreamPolicy.CaptureReleasedCondition(state, 1,
            new ItemTransform { ItemId = 12, OwnerPlayerId = 1, Flags = ItemTransform.FlagFinal });
        Assert.True(VehicleConditionStreamPolicy.TryGetParkedObserverDrivetrainDamage(parked, 12, false, 255, out damage)); Assert.Equal(value, damage);
        Assert.Equal(bytes, PacketCodec.Encode(state));
    }

    [Theory]
    [InlineData(0, 0, true)] [InlineData(0, 255, true)] [InlineData(1, 1, true)]
    [InlineData(1, 0, false)] [InlineData(1, 255, false)] [InlineData(0, 1, false)] [InlineData(1, 2, false)]
    public void OnlyCurrentAuthoritySuppliesLiveDamage(byte source, byte owner, bool allowed)
    {
        Assert.Equal(allowed, VehicleConditionStreamPolicy.TryGetObserverDrivetrainDamage(State(source), 12, false, owner, out byte damage));
        Assert.Equal(allowed ? 2 : 0, damage);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(4)] [InlineData(61)]
    public void MissingDamageAvailabilityNeverInventsZero(byte mask)
    {
        var state = State(); state.Availability = mask;
        Assert.False(VehicleConditionStreamPolicy.TryGetObserverDrivetrainDamage(state, 12, false, 1, out _));
        Assert.False(VehicleConditionStreamPolicy.TryGetParkedObserverDrivetrainDamage(state, 12, false, 255, out _));
    }

    [Fact]
    public void InvalidIdentityLocalDrivingAndSupersededParkedStateCannotRead()
    {
        var state = State();
        Assert.False(VehicleConditionStreamPolicy.TryGetObserverDrivetrainDamage(null, 12, false, 1, out _));
        Assert.False(VehicleConditionStreamPolicy.TryGetObserverDrivetrainDamage(state, 13, false, 1, out _));
        Assert.False(VehicleConditionStreamPolicy.TryGetObserverDrivetrainDamage(state, 12, true, 1, out _));
        Assert.False(VehicleConditionStreamPolicy.TryGetParkedObserverDrivetrainDamage(state, 12, true, 255, out _));
        Assert.False(VehicleConditionStreamPolicy.TryGetParkedObserverDrivetrainDamage(state, 12, false, 2, out _));
        state.Flags = 17; Assert.False(VehicleConditionStreamPolicy.TryGetObserverDrivetrainDamage(state, 12, false, 1, out _));
        state.Flags = 0; state.Availability = 255; Assert.False(VehicleConditionStreamPolicy.TryGetObserverDrivetrainDamage(state, 12, false, 1, out _));
    }

    [Fact]
    public void HostSnapshotAndLiveHistoryAreUnaffectedByReadingDamage()
    {
        var state = State(0); state.Sequence = 65535; var p = new VehicleConditionStreamPolicy();
        Assert.True(p.Receive(state, false, false, 255));
        Assert.True(VehicleConditionStreamPolicy.TryGetObserverDrivetrainDamage(state, 12, false, 255, out _));
        state.Sequence = 0; Assert.True(p.Receive(state, false, false, 255)); state.Availability = 0;
        Assert.False(p.Receive(state, false, false, 255)); state.Sequence = 1; Assert.True(p.Receive(state, false, false, 255));
        Assert.False(VehicleConditionStreamPolicy.TryGetObserverDrivetrainDamage(state, 12, false, 255, out _));
    }

    private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
    private static JsonObject Gearbox(JsonNode root) => root["guestEngineProtection"]!["writers"]!.AsArray()
        .Single(w => (string?)w!["path"] == "CORRIS/Simulation/Systems/Drivetrain/GearboxDamage")!.AsObject();

    [Fact]
    public void CatalogPairsTheNativeDamageReaderWithItsReverseWearGuard()
    {
        var profile = SyncCatalogJson.Parse(Catalog().ToJsonString()).GuestEngineProtection!;
        var writer = Assert.Single(profile.Writers, w => w.GearboxConditionRead != null);
        Assert.Equal("Damage", writer.Fsm); Assert.Equal("Damage type", writer.GearboxConditionRead!.State); Assert.Equal(0, writer.GearboxConditionRead.Index);
        Assert.Contains(writer.Actions, a => a.State == "Reverse" && a.Index == 6 && a.TargetScalar == "Wear" && a.TargetVariable == "db_Gearbox");
    }

    [Theory]
    [InlineData("null")] [InlineData("[]")] [InlineData("true")] [InlineData("{}")]
    [InlineData("{\"state\":\"Damage type\",\"index\":1}")]
    [InlineData("{\"state\":\"Reverse\",\"index\":0}")]
    [InlineData("{\"state\":\"Damage type\",\"index\":-1}")]
    [InlineData("{\"state\":\"Damage type\",\"index\":0.5}")]
    public void InvalidReaderMetadataDisablesOnlyTheProtectionProfile(string value)
    {
        var json = Catalog(); Gearbox(json)["gearboxCondition"] = JsonNode.Parse(value);
        var parsed = SyncCatalogJson.Parse(json.ToJsonString());
        Assert.Null(parsed.GuestEngineProtection); Assert.NotNull(parsed.GuestEngineProtectionError); Assert.NotEmpty(parsed.Doors); Assert.NotNull(parsed.VehicleDamage);
    }

    [Theory]
    [InlineData("path")] [InlineData("fsm")] [InlineData("state")] [InlineData("index")]
    [InlineData("actionType")] [InlineData("targetVariable")] [InlineData("targetFsm")] [InlineData("targetScalar")]
    public void WrongConsumerOrChangedSavedWriterCannotAdmitTheReader(string field)
    {
        var json = Catalog(); var writer = Gearbox(json);
        if (field is "path" or "fsm") writer[field] = "Different";
        else { var guard = writer["actions"]![0]!; guard[field] = field == "index" ? JsonValue.Create(7) : JsonValue.Create("Different"); }
        Assert.Null(SyncCatalogJson.Parse(json.ToJsonString()).GuestEngineProtection);
    }
}
