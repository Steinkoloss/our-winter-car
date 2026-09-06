using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class PassengerSeatLedgerTests
{
    private static PassengerState Seat(byte player = 1, ushort sequence = 1, uint vehicle = 100, byte seat = 0) =>
        new() { PlayerId = player, Sequence = sequence, VehicleId = vehicle, SeatIndex = seat };

    [Fact]
    public void MovingCarKeepaliveDoesNotRequireAnotherEntryProximityProof()
    {
        var ledger = new PassengerSeatLedger();
        Assert.True(ledger.Apply(Seat(), (_, continuing) => !continuing)!.Accepted);

        // The passenger's latest pose can now be far behind the moving car, or stale
        // after a hitch. Only an existing exact seat can bypass entry proximity.
        var keepalive = ledger.Apply(Seat(sequence: 2), (_, continuing) => continuing);

        Assert.True(keepalive!.Accepted);
        Assert.Single(ledger.Occupants);
        Assert.Equal((ushort)2, ledger.Occupants.Single().Sequence);
    }

    [Theory]
    [InlineData(101u, (byte)0)]
    [InlineData(100u, (byte)1)]
    public void SwitchingCarOrSeatRequiresNewProximityProof(uint vehicle, byte seat)
    {
        var ledger = new PassengerSeatLedger();
        ledger.Apply(Seat(), (_, _) => true);
        var result = ledger.Apply(Seat(sequence: 2, vehicle: vehicle, seat: seat), (_, continuing) => continuing);

        Assert.False(result!.Accepted);
        Assert.False(result.State.IsSeated);
        Assert.Equal(0u, result.State.VehicleId);
        Assert.Equal((ushort)2, result.State.Sequence);
        Assert.Empty(ledger.Occupants);
    }

    [Fact]
    public void MissingVehicleClearsAcceptedOccupancyAndJoinSnapshot()
    {
        var ledger = new PassengerSeatLedger();
        ledger.Apply(Seat(), (_, _) => true);
        var result = ledger.Apply(Seat(sequence: 2), (_, _) => false);

        Assert.False(result!.Accepted);
        Assert.False(result.State.IsSeated);
        Assert.Empty(ledger.Occupants);
        Assert.Null(ledger.Apply(Seat(sequence: 2), (_, _) => true));
        Assert.Empty(ledger.Occupants);
    }

    [Theory]
    [InlineData((ushort)4)]
    [InlineData((ushort)5)]
    public void StaleAndDuplicateRequestsCannotEjectAnOccupant(ushort sequence)
    {
        var ledger = new PassengerSeatLedger();
        ledger.Apply(Seat(sequence: 5), (_, _) => true);
        var staleExit = Seat(sequence: sequence, vehicle: 0, seat: PassengerState.SeatNone);

        Assert.Null(ledger.Apply(staleExit, (_, _) => throw new Xunit.Sdk.XunitException("Stale request reached scene validation")));
        Assert.True(ledger.Occupants.Single().IsSeated);
    }

    [Theory]
    [InlineData(0u, (byte)0)]
    [InlineData(100u, (byte)3)]
    [InlineData(100u, PassengerState.SeatNone)]
    public void MalformedNewRequestClearsOccupancyWithoutSceneValidation(uint vehicle, byte seat)
    {
        var ledger = new PassengerSeatLedger();
        ledger.Apply(Seat(), (_, _) => true);
        var result = ledger.Apply(Seat(sequence: 2, vehicle: vehicle, seat: seat),
            (_, _) => throw new Xunit.Sdk.XunitException("Malformed request reached scene validation"));
        Assert.False(result!.Accepted);
        Assert.False(result.State.IsSeated);
        Assert.Empty(ledger.Occupants);
    }

    [Fact]
    public void ExitDoesNotNeedFreshPoseAndAllowsLaterReentry()
    {
        var ledger = new PassengerSeatLedger();
        ledger.Apply(Seat(), (_, _) => true);
        var result = ledger.Apply(Seat(sequence: 2, vehicle: 0, seat: PassengerState.SeatNone), (_, _) => false);
        Assert.True(result!.Accepted);
        Assert.Empty(ledger.Occupants);
        Assert.True(ledger.Apply(Seat(sequence: 3), (_, continuing) => !continuing)!.Accepted);
    }

    [Theory]
    [InlineData((byte)1, (byte)2)]
    [InlineData((byte)2, (byte)1)]
    public void LowestPlayerIdWinsRegardlessOfArrivalOrder(byte first, byte second)
    {
        var ledger = new PassengerSeatLedger();
        ledger.Apply(Seat(player: first), (_, _) => true);
        var result = ledger.Apply(Seat(player: second), (_, _) => true);
        Assert.Equal((byte)1, ledger.Occupants.Single().PlayerId);
        Assert.Equal(second == 1, result!.Accepted);
        if (second == 1)
        {
            Assert.Equal((byte)2, result.Evicted!.PlayerId);
            Assert.False(result.Evicted.IsSeated);
            Assert.Equal((ushort)1, result.Evicted.Sequence);
        }
        else Assert.Null(result.Evicted);
    }

    [Fact]
    public void HostOccupancyAlsoBlocksGuestSeatClaims()
    {
        var ledger = new PassengerSeatLedger();
        ledger.Record(Seat(player: 0));
        Assert.False(ledger.Apply(Seat(), (_, _) => true)!.Accepted);
        Assert.Equal((byte)0, ledger.Occupants.Single().PlayerId);
    }

    [Fact]
    public void SequenceWrapAndReconnectDoNotLoseSeats()
    {
        var ledger = new PassengerSeatLedger();
        ledger.Apply(Seat(sequence: ushort.MaxValue), (_, _) => true);
        Assert.True(ledger.Apply(Seat(sequence: 0), (_, continuing) => continuing)!.Accepted);
        Assert.Null(ledger.Apply(Seat(sequence: ushort.MaxValue), (_, _) => true));
        ledger.ForgetPlayer(1);
        Assert.Empty(ledger.Occupants);
        Assert.True(ledger.Apply(Seat(), (_, continuing) => !continuing)!.Accepted);
        ledger.Clear();
        Assert.Empty(ledger.Occupants);
        Assert.True(ledger.Apply(Seat(), (_, continuing) => !continuing)!.Accepted);
    }
}
