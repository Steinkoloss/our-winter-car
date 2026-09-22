using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class PassengerSeatPolicyTests
{
    [Fact]
    public void ShippedTaxiCatalogSelectsTheAuditedNativeSeatAnchors()
    {
        var catalog = SyncCatalogJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")));
        Assert.Null(catalog.TaxiPassengersError); Assert.NotNull(catalog.TaxiPassengers);
        Assert.Equal("JOBS/TAXIJOB/MACHTWAGEN", catalog.TaxiPassengers!["path"]);
        Assert.Equal("Functions/MassDriver", catalog.TaxiPassengers["driverMass"]);
        Assert.Equal("Functions/MassPassenger", catalog.TaxiPassengers["customerMass"]);
    }

    [Theory]
    [InlineData("path")] [InlineData("driveTrigger")] [InlineData("driverMass")]
    [InlineData("customerMass")] [InlineData("tutorial")]
    public void MissingTaxiAnchorMetadataLeavesOtherSubsystemsAvailable(string missing)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        json["taxiPassengers"]!.AsObject().Remove(missing);
        var catalog = SyncCatalogJson.Parse(json.ToJsonString());
        Assert.Null(catalog.TaxiPassengers); Assert.NotNull(catalog.TaxiPassengersError);
        Assert.NotNull(catalog.TaxiService); Assert.NotNull(catalog.Sausages);
    }

    [Theory]
    [InlineData(0, true)] [InlineData(1, false)] [InlineData(2, true)]
    [InlineData(-1, false)] [InlineData(3, false)] [InlineData(255, false)]
    public void TaxiKeepsTheFareSeatReserved(int seat, bool expected) =>
        Assert.Equal(expected, PassengerSeatPolicy.Available(PassengerSeatPolicy.TaxiSeats, seat, true, false));

    [Theory]
    [InlineData(false, false)] [InlineData(true, true)] [InlineData(false, true)]
    public void InactiveCarOrTutorialRefusesEverySeat(bool active, bool tutorial)
    {
        for (int seat = 0; seat < 3; seat++)
            Assert.False(PassengerSeatPolicy.Available(PassengerSeatPolicy.AllSeats, seat, active, tutorial));
    }

    [Fact]
    public void OrdinaryCarsRetainAllThreeSeats()
    {
        for (int seat = 0; seat < 3; seat++)
            Assert.True(PassengerSeatPolicy.Available(PassengerSeatPolicy.AllSeats, seat, true, false));
    }

    [Fact]
    public void LosingAvailabilityClearsAcceptedKeepaliveAndCannotReplayItsClaim()
    {
        var ledger = new PassengerSeatLedger();
        bool active = true;
        bool Validate(PassengerState s, bool continuing) => PassengerSeatPolicy.Available(PassengerSeatPolicy.TaxiSeats, s.SeatIndex, active, false);
        var claim = new PassengerState { PlayerId = 1, VehicleId = 100, SeatIndex = 2, Sequence = 10 };
        Assert.True(ledger.Apply(claim, Validate)!.Accepted);
        active = false; claim = new PassengerState { PlayerId = 1, VehicleId = 100, SeatIndex = 2, Sequence = 11 };
        var correction = ledger.Apply(claim, Validate)!;
        Assert.False(correction.Accepted); Assert.False(correction.State.IsSeated); Assert.Empty(ledger.Occupants);
        active = true;
        Assert.Null(ledger.Apply(claim, Validate));
        Assert.Empty(ledger.Occupants);
    }

    [Fact]
    public void RejectedFareSeatCannotEvictSomeoneInTheOtherTaxiSeat()
    {
        var ledger = new PassengerSeatLedger();
        bool Validate(PassengerState s, bool continuing) => PassengerSeatPolicy.Available(PassengerSeatPolicy.TaxiSeats, s.SeatIndex, true, false);
        Assert.True(ledger.Apply(new PassengerState { PlayerId = 2, VehicleId = 100, SeatIndex = 2, Sequence = 1 }, Validate)!.Accepted);
        Assert.False(ledger.Apply(new PassengerState { PlayerId = 1, VehicleId = 100, SeatIndex = 1, Sequence = 1 }, Validate)!.Accepted);
        Assert.True(ledger.IsOccupant(2, 100)); Assert.False(ledger.IsOccupant(1, 100));
    }
}
