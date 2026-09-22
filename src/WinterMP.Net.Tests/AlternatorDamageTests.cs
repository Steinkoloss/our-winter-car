using System;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class AlternatorDamageTests
    {
        private static readonly ReplacementPartRule Rule = new ReplacementPartRule(71, "VIN133", 6, 1, supportsAlternatorDamage: true);
        private static ReplacementPartState State(bool? damaged = false) => new ReplacementPartState {
            FactoryId = 71, NativeId = "VIN1337", Revision = 1, Installed = true, AssemblyId = 1,
            Scalars = new[] { 90f, 16f, 7f, 3.4f, .7f, 350f }, Rotation = NetQuaternion.Identity,
            ParentKind = PartParentKind.NativePart, ParentId = 12, ParentPath = "VINP_Alternator", AlternatorDamaged = damaged };
        private static ReplacementPartReplica Replica() => new ReplacementPartReplica(new[] { Rule }, new ItemSpawnLifecycle());

        [Theory]
        [InlineData(null, 0)] [InlineData(false, 1)] [InlineData(true, 2)]
        public void TrailingFlagDistinguishesUnknownHealthyAndDamaged(bool? damaged, byte expected)
        {
            var state = State(damaged); byte[] bytes = PacketCodec.Encode(state);
            Assert.Equal(expected, bytes[bytes.Length - 1]);
            var decoded = Assert.IsType<ReplacementPartState>(PacketCodec.Decode(bytes));
            Assert.Equal(damaged, decoded.AlternatorDamaged); Assert.Equal(state.Scalars, decoded.Scalars);
            Assert.Equal(state.ParentPath, decoded.ParentPath); Assert.Equal(state.CamProfile, decoded.CamProfile);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(bytes.Length - 1).ToArray()));
        }

        [Theory]
        [InlineData(3)] [InlineData(4)] [InlineData(255)]
        public void InvalidFlagCannotDecode(byte flag)
        {
            byte[] bytes = PacketCodec.Encode(State()); bytes[bytes.Length - 1] = flag;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }

        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void BothActualFlagsAllowFittedCreationAndSurviveDefensiveCopies(bool damaged)
        {
            var state = State(damaged); var replica = Replica(); Assert.True(replica.Receive(state, out uint id));
            Assert.True(Rule.CanCreateFitted(state)); state.AlternatorDamaged = !damaged;
            Assert.Equal(damaged, replica.Get(id)!.AlternatorDamaged);
            var copy = replica.Get(id)!; copy.AlternatorDamaged = null; Assert.Equal(damaged, replica.Get(id)!.AlternatorDamaged);
        }

        [Fact]
        public void MissingFlagCannotReplaceAnAttachedAlternatorOrPermitFittedCreation()
        {
            var state = State(); var replica = Replica(); Assert.True(replica.Receive(state, out uint id));
            state.Revision++; state.AlternatorDamaged = null;
            Assert.False(replica.Receive(state, out _)); Assert.False(Rule.CanCreateFitted(state));
            Assert.Equal(false, replica.Get(id)!.AlternatorDamaged);
        }

        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void LooseAndUnresolvedPartsCannotCarryAStaleMountFlag(bool fitted)
        {
            var state = State(); state.ParentKind = PartParentKind.None; state.ParentId = 0; state.ParentPath = "";
            state.Installed = fitted; state.AssemblyId = fitted ? 1 : 0; state.Scalars[1] = 0;
            var replica = Replica(); Assert.False(replica.Receive(state, out _)); Assert.False(Rule.CanCreate(state));
            state.AlternatorDamaged = null; Assert.True(replica.Receive(state, out _));
            Assert.Equal(!fitted, Rule.CanCreate(state)); Assert.False(Rule.CanCreateFitted(state));
        }

        [Fact]
        public void OtherFamiliesCannotCarryAlternatorMountState()
        {
            var other = new ReplacementPartRule(71, "VIN132", 6, 1); var state = State(true); state.NativeId = "VIN1327";
            var replica = new ReplacementPartReplica(new[] { other }, new ItemSpawnLifecycle());
            Assert.False(replica.Receive(state, out _)); Assert.False(other.CanCreateFitted(state));
            state.AlternatorDamaged = null; Assert.True(replica.Receive(state, out _));
        }

        [Fact]
        public void DamageAndRepairAdvanceGameplayRevisionIndependentlyOfWear()
        {
            var state = State(); var publication = new ReplacementPartPublication(); var replica = Replica();
            state.Revision = publication.Observe(state); Assert.True(replica.Receive(state, out uint id));
            foreach (bool damaged in new[] { true, false })
            {
                publication.MarkBroadcast(state.Revision, state.PresentationRevision);
                state.AlternatorDamaged = damaged; state.PresentationRevision++;
                Assert.False(replica.Receive(state, out _));
                uint previous = state.Revision; state.Revision = publication.Observe(state);
                Assert.Equal(previous + 1, state.Revision); Assert.True(publication.NeedsBroadcast);
                Assert.True(replica.Receive(state, out _)); Assert.Equal(damaged, replica.Get(id)!.AlternatorDamaged);
                Assert.Equal(90, replica.Get(id)!.Scalars[0]);
            }
            var stale = State(true); Assert.False(replica.Receive(stale, out _)); Assert.Equal(false, replica.Get(id)!.AlternatorDamaged);
        }
    }
}
