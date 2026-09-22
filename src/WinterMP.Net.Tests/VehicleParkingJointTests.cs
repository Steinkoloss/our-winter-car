using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class VehicleParkingJointTests
{
    private static string Catalog => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json"));

    [Fact]
    public void CatalogSelectsTheNativeCorrisWorldParkingRelease()
    {
        var data = SyncCatalogJson.Parse(Catalog);
        Assert.Null(data.ParkingJointError); Assert.NotNull(data.ParkingJoint);
        Assert.Equal("CORRIS", data.ParkingJoint!.RootPath); Assert.Equal("LOD", data.ParkingJoint.Fsm);
        Assert.Equal("Remove joint", data.ParkingJoint.ReleaseState); Assert.Equal(0, data.ParkingJoint.ReleaseIndex);
    }

    [Theory]
    [InlineData("rootPath")][InlineData("fsm")][InlineData("releaseState")][InlineData("releaseIndex")]
    public void MissingNativeBindingFieldsDisableOnlyParkingPoseMetadata(string field)
    {
        var json = JsonNode.Parse(Catalog)!; json["vehicleParkingJoint"]!.AsObject().Remove(field);
        var data = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(data.ParkingJoint); Assert.NotNull(data.ParkingJointError);
        Assert.NotEmpty(data.Doors); Assert.NotNull(data.VehicleClimate); Assert.NotNull(data.ParkingBrake);
    }

    [Theory]
    [InlineData(-1)][InlineData(256)][InlineData(0.5)]
    public void InvalidNativeActionIndexCannotSelectAnotherAction(double index)
    {
        var json = JsonNode.Parse(Catalog)!; json["vehicleParkingJoint"]!["releaseIndex"] = index;
        var data = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(data.ParkingJoint); Assert.NotNull(data.ParkingJointError);
    }
}
