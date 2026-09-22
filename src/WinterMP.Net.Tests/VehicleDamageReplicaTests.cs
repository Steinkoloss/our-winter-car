using System;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class VehicleDamageReplicaTests
    {
        private static VehicleDamage State(uint vehicleId = 91, ushort sequence = 1) => new VehicleDamage {
            VehicleId = vehicleId, OwnerPlayerId = VehicleDamageReplica.HostPlayerId, Sequence = sequence,
            KnownPartsMask = VehicleDamage.Crankshaft | VehicleDamage.Headgasket, DamageMask = VehicleDamage.Crankshaft,
            Wear = new[] { 0f, 0, 0, 0, 0, -1.5f, 96.25f, 0, 0, 0, 0, 0, 0, 0, 0, 0 } };

        [Theory]
        [InlineData(false, false, false, true)]
        [InlineData(false, false, true, true)]
        [InlineData(false, true, false, false)]
        [InlineData(false, true, true, true)]
        [InlineData(true, false, false, false)]
        [InlineData(true, false, true, false)]
        [InlineData(true, true, false, false)]
        [InlineData(true, true, true, false)]
        public void DamageAuthorityDoesNotFollowVehicleOwnershipAndProtectionOutlivesSession(
            bool protectedGuestWorld, bool sessionActive, bool isHost, bool authority)
        {
            Assert.Equal(authority, VehicleDamagePolicy.IsAuthority(protectedGuestWorld, sessionActive, isHost));
        }

        [Fact]
        public void AcceptedHostConditionAndReadsOwnTheirWearArrays()
        {
            var replica = new VehicleDamageReplica(); var incoming = State();
            Assert.Null(replica.Get(incoming.VehicleId)); Assert.True(replica.Receive(incoming));
            incoming.DamageMask = 0; incoming.Wear[5] = 99;
            var retained = replica.Get(incoming.VehicleId)!;
            Assert.Equal(VehicleDamage.Crankshaft, retained.DamageMask); Assert.Equal(-1.5f, retained.Wear[5]);
            retained.Wear[6] = 0; retained.KnownPartsMask = 0;
            Assert.Equal(96.25f, replica.Get(incoming.VehicleId)!.Wear[6]);
            Assert.Equal(VehicleDamage.Crankshaft | VehicleDamage.Headgasket, replica.Get(incoming.VehicleId)!.KnownPartsMask);
            var copied = VehicleDamagePolicy.Copy(replica.Get(incoming.VehicleId)!);
            Assert.Equal(1, copied.Sequence); copied.Wear[5] = 50;
            Assert.Equal(-1.5f, replica.Get(incoming.VehicleId)!.Wear[5]);
        }

        [Theory]
        [InlineData(0, 0, false)]
        [InlineData(0, 1, true)]
        [InlineData(0, 32767, true)]
        [InlineData(0, 32768, false)]
        [InlineData(0, 65535, false)]
        [InlineData(65535, 0, true)]
        [InlineData(5, 4, false)]
        [InlineData(65534, 1, true)]
        public void SequencesRejectDuplicatesOldSnapshotsAndTheAmbiguousHalfRange(ushort first, ushort next, bool accepted)
        {
            var replica = new VehicleDamageReplica(); Assert.True(replica.Receive(State(sequence: first)));
            var later = State(sequence: next); later.Wear[6] = 90;
            Assert.Equal(accepted, replica.Receive(later));
            Assert.Equal(accepted ? next : first, replica.Get(later.VehicleId)!.Sequence);
            Assert.Equal(accepted ? 90f : 96.25f, replica.Get(later.VehicleId)!.Wear[6]);
        }

        [Fact]
        public void DriverChangesCannotReplaceOrResetTheHostSequence()
        {
            var replica = new VehicleDamageReplica(); Assert.True(replica.Receive(State(sequence: 100)));
            foreach (byte driver in new byte[] { 1, 2, 254, 255 })
            {
                var guest = State(sequence: 101); guest.OwnerPlayerId = driver;
                Assert.False(replica.Receive(guest));
                guest.Sequence = 1; Assert.False(replica.Receive(guest));
            }
            Assert.False(replica.Receive(State(sequence: 1)));
            Assert.True(replica.Receive(State(sequence: 101)));
            Assert.Equal(0, replica.Get(91)!.OwnerPlayerId);
        }

        [Fact]
        public void RepairAndUnknownSlotsUseTheCompleteHostSnapshotWithoutGuestMerging()
        {
            var replica = new VehicleDamageReplica(); Assert.True(replica.Receive(State()));
            var repaired = State(sequence: 2); repaired.DamageMask = 0; repaired.Wear[5] = 98.75f;
            Assert.True(replica.Receive(repaired)); Assert.Equal(0u, replica.Get(91)!.DamageMask);
            var unknown = State(sequence: 3); unknown.KnownPartsMask = 0;
            unknown.DamageMask = VehicleDamage.Block; unknown.Wear[5] = float.NaN;
            Assert.True(replica.Receive(unknown));
            var retained = replica.Get(91)!;
            Assert.Equal(0u, retained.KnownPartsMask); Assert.Equal(VehicleDamage.Block, retained.DamageMask);
            Assert.True(float.IsNaN(retained.Wear[5]));
            var hostCleared = State(sequence: 4); hostCleared.KnownPartsMask = 0; hostCleared.DamageMask = 0;
            Assert.True(replica.Receive(hostCleared)); Assert.Equal(0u, replica.Get(91)!.DamageMask);
            Assert.False(replica.Receive(repaired)); Assert.Equal(0u, replica.Get(91)!.KnownPartsMask);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("vehicle-zero")]
        [InlineData("owner")]
        [InlineData("wear-null")]
        [InlineData("wear-short")]
        [InlineData("wear-long")]
        [InlineData("wear-nan")]
        [InlineData("wear-infinity")]
        [InlineData("broken-with-health")]
        [InlineData("healthy-with-failure")]
        [InlineData("selector")]
        [InlineData("unknown-known-bit")]
        [InlineData("unknown-damage-bit")]
        public void MalformedOrUnauthorizedSnapshotsCannotChangeStateOrSequence(string fault)
        {
            var replica = new VehicleDamageReplica(); Assert.True(replica.Receive(State()));
            VehicleDamage? invalid = State(sequence: 3);
            switch (fault)
            {
                case "null": invalid = null; break;
                case "vehicle-zero": invalid.VehicleId = 0; break;
                case "owner": invalid.OwnerPlayerId = 1; break;
                case "wear-null": invalid.Wear = null!; break;
                case "wear-short": invalid.Wear = new float[15]; break;
                case "wear-long": invalid.Wear = new float[17]; break;
                case "wear-nan": invalid.Wear[5] = float.NaN; break;
                case "wear-infinity": invalid.Wear[6] = float.PositiveInfinity; break;
                case "broken-with-health": invalid.Wear[5] = 90; break;
                case "healthy-with-failure": invalid.Wear[6] = 0; break;
                case "selector": invalid.DamageMask |= VehicleDamage.Seize; break;
                case "unknown-known-bit": invalid.KnownPartsMask |= 1u << 25; break;
                case "unknown-damage-bit": invalid.DamageMask |= 1u << 25; break;
            }
            Assert.False(replica.Receive(invalid)); Assert.Null(replica.Get(0));
            Assert.Equal(1, replica.Get(91)!.Sequence);
            Assert.True(replica.Receive(State(sequence: 2)));
        }

        [Fact]
        public void VehicleSequencesAreIndependentAndOnlyClearAllowsARejoinedHostToRestart()
        {
            var replica = new VehicleDamageReplica();
            Assert.True(replica.Receive(State(91, 200))); Assert.True(replica.Receive(State(92, 0)));
            Assert.False(replica.Receive(State(91, 0))); Assert.True(replica.Receive(State(92, 1)));
            Assert.Equal(200, replica.Get(91)!.Sequence); Assert.Equal(1, replica.Get(92)!.Sequence);
            replica.Clear(); Assert.Null(replica.Get(91)); Assert.Null(replica.Get(92));
            Assert.True(replica.Receive(State(91, 0))); Assert.True(replica.Receive(State(92, 0)));
        }

        [Fact]
        public void HostSnapshotsRetainTheExistingEightyOneByteWireLayout()
        {
            var state = State(sequence: ushort.MaxValue); byte[] bytes = PacketCodec.Encode(state);
            Assert.Equal(81, bytes.Length); Assert.Equal(0, bytes[6]);
            var replica = new VehicleDamageReplica();
            Assert.True(replica.Receive(Assert.IsType<VehicleDamage>(PacketCodec.Decode(bytes))));
            Assert.Equal(bytes, PacketCodec.Encode(replica.Get(state.VehicleId)!));
            var duplicate = State(sequence: ushort.MaxValue); duplicate.Wear[6] = 10;
            Assert.False(replica.Receive(duplicate)); Assert.Equal(96.25f, replica.Get(state.VehicleId)!.Wear[6]);
        }
    }
}
