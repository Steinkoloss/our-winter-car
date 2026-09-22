using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class PassengerSeatLedgerTests
{
    private static PassengerState Seat(byte player = 1, ushort sequence = 1, uint vehicle = 100, byte seat = 0) =>
        new() { PlayerId = player, Sequence = sequence, VehicleId = vehicle, SeatIndex = seat };

    [Theory]
    [InlineData((ushort)0)] [InlineData((ushort)10)] [InlineData(ushort.MaxValue)]
    public void DeathRetiresOnlyOccupancyAndRespawnRequiresANewClaim(ushort sequence)
    {
        var ledger = new PassengerSeatLedger();
        Assert.True(ledger.Apply(Seat(sequence: sequence), (_, _) => true)!.Accepted);
        ledger.RetirePlayer(1); ledger.RetirePlayer(1);
        Assert.Empty(ledger.Occupants); Assert.False(ledger.HasOccupant(100));
        Assert.Null(ledger.Apply(Seat(sequence: sequence), (_, _) => throw new Xunit.Sdk.XunitException("Dead claim replay reached validation")));
        Assert.Null(ledger.Apply(Seat(sequence: unchecked((ushort)(sequence - 1))), (_, _) => true));
        var fresh = ledger.Apply(Seat(sequence: unchecked((ushort)(sequence + 1))), (_, continuing) => !continuing);
        Assert.True(fresh!.Accepted); Assert.True(ledger.IsOccupant(1, 100));
    }

    [Fact]
    public void DeathLeavesOtherPassengersSeatedAndReleasesTheSeatForANewClaimant()
    {
        var ledger = new PassengerSeatLedger();
        ledger.Apply(Seat(player: 1, seat: 0), (_, _) => true); ledger.Apply(Seat(player: 2, seat: 1), (_, _) => true);
        ledger.RetirePlayer(1); ledger.RetirePlayer(90);
        Assert.Equal((byte)2, ledger.Occupants.Single().PlayerId);
        Assert.True(ledger.Apply(Seat(player: 3, seat: 0), (_, _) => true)!.Accepted);
    }

    [Fact]
    public void PermadeathRetiresEveryoneWithoutResettingAnyRequestHistory()
    {
        var ledger = new PassengerSeatLedger(); ledger.Record(Seat(player: 0));
        ledger.Apply(Seat(player: 1, seat: 1, sequence: 9), (_, _) => true);
        ledger.Apply(Seat(player: 2, seat: 2, sequence: 20), (_, _) => true);
        ledger.RetireAll(); ledger.RetireAll(); Assert.Empty(ledger.Occupants);
        Assert.Null(ledger.Apply(Seat(player: 1, seat: 1, sequence: 9), (_, _) => true));
        Assert.Null(ledger.Apply(Seat(player: 2, seat: 2, sequence: 20), (_, _) => true));
        Assert.True(ledger.Apply(Seat(player: 1, seat: 1, sequence: 10), (_, continuing) => !continuing)!.Accepted);
        ledger.Clear(); Assert.True(ledger.Apply(Seat(player: 1, sequence: 0), (_, _) => true)!.Accepted);
    }

    [Fact]
    public void RejectedDeadKeepaliveConsumesItsSequenceAndDoesNotReserveASeat()
    {
        var ledger = new PassengerSeatLedger(); ledger.Apply(Seat(sequence: 8), (_, _) => true); ledger.RetirePlayer(1);
        var rejected = ledger.Apply(Seat(sequence: 9), (_, _) => false);
        Assert.False(rejected!.Accepted); Assert.False(rejected.State.IsSeated); Assert.Empty(ledger.Occupants);
        Assert.Null(ledger.Apply(Seat(sequence: 9), (_, _) => true));
        ledger.ForgetPlayer(1); Assert.True(ledger.Apply(Seat(sequence: 0), (_, _) => true)!.Accepted);
    }

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

    [Fact]
    public void CabinOccupancyQueriesOnlyTheRequestedVehicleAndDoNotConsumeSequences()
    {
        var ledger = new PassengerSeatLedger();
        Assert.False(ledger.HasOccupant(100)); Assert.False(ledger.HasOccupant(0));
        ledger.Apply(Seat(sequence: 40), (_, _) => true);
        for (int i = 0; i < 5; i++) { Assert.True(ledger.HasOccupant(100)); Assert.False(ledger.HasOccupant(101)); }
        Assert.Null(ledger.Apply(Seat(sequence: 40), (_, _) => true));
        Assert.True(ledger.Apply(Seat(sequence: 41), (_, _) => true)!.Accepted);
    }

    [Theory]
    [InlineData((byte)0)] [InlineData((byte)1)] [InlineData((byte)2)]
    public void EveryAcceptedPassengerSeatOccupiesItsCabin(byte seat)
    {
        var ledger = new PassengerSeatLedger(); ledger.Apply(Seat(seat: seat), (_, _) => true);
        Assert.True(ledger.HasOccupant(100));
        ledger.Apply(Seat(sequence: 2, vehicle: 0, seat: PassengerState.SeatNone), (_, _) => true);
        Assert.False(ledger.HasOccupant(100));
    }

    [Theory]
    [InlineData((byte)3)] [InlineData((byte)254)] [InlineData(PassengerState.SeatNone)]
    public void InvalidRecordedSeatDoesNotOccupyTheCabin(byte seat)
    {
        var ledger = new PassengerSeatLedger(); ledger.Record(Seat(seat: seat));
        Assert.False(ledger.HasOccupant(100));
    }

    [Fact]
    public void RejectedMovedAndDisconnectedPassengersDoNotLeaveOccupiedCabins()
    {
        var ledger = new PassengerSeatLedger(); ledger.Apply(Seat(), (_, _) => true);
        ledger.Apply(Seat(sequence: 2, vehicle: 101), (_, _) => false);
        Assert.False(ledger.HasOccupant(100)); Assert.False(ledger.HasOccupant(101));
        ledger.Apply(Seat(sequence: 3), (_, _) => true);
        ledger.Apply(Seat(sequence: 4, vehicle: 101), (_, _) => true);
        Assert.False(ledger.HasOccupant(100)); Assert.True(ledger.HasOccupant(101));
        ledger.ForgetPlayer(1); Assert.False(ledger.HasOccupant(101));
        ledger.Record(Seat(player: 0)); Assert.True(ledger.HasOccupant(100));
        ledger.Clear(); Assert.False(ledger.HasOccupant(100));
    }

    [Fact]
    public void LastPassengerExitClearsOccupancyWithoutDroppingOtherSeats()
    {
        var ledger = new PassengerSeatLedger();
        ledger.Apply(Seat(player: 1, seat: 0), (_, _) => true); ledger.Apply(Seat(player: 2, seat: 1), (_, _) => true);
        ledger.ForgetPlayer(1); Assert.True(ledger.HasOccupant(100));
        ledger.ForgetPlayer(2); Assert.False(ledger.HasOccupant(100));
    }

    [Fact]
    public void SeatRaceAndStaleExitKeepOnlyTheAcceptedCabinOccupant()
    {
        var ledger = new PassengerSeatLedger(); ledger.Apply(Seat(player: 2, sequence: 10), (_, _) => true);
        ledger.Apply(Seat(player: 1, sequence: 10), (_, _) => true);
        Assert.True(ledger.HasOccupant(100)); ledger.ForgetPlayer(2); Assert.True(ledger.HasOccupant(100));
        ledger.Apply(Seat(player: 1, sequence: 9, vehicle: 0, seat: PassengerState.SeatNone), (_, _) => true);
        Assert.True(ledger.HasOccupant(100)); ledger.ForgetPlayer(1); Assert.False(ledger.HasOccupant(100));
    }

}
