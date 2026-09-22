using System;
using System.IO;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class PartBeltVisualTests
    {
        private static readonly ReplacementPartRule Rule = new ReplacementPartRule(71, "FANBELT0", 2, 1, supportsBeltVisual: true);
        private static ReplacementPartReplica Replica(ItemSpawnLifecycle? life = null) =>
            new ReplacementPartReplica(new[] { Rule }, life ?? new ItemSpawnLifecycle());
        private static ReplacementPartState State() => new ReplacementPartState {
            FactoryId = Rule.FactoryId, NativeId = "FANBELT01", Revision = 1, PresentationRevision = 1, Scalars = new[] { 98f, 0f },
            Installed = true, AssemblyId = 1, Rotation = NetQuaternion.Identity,
            ParentKind = PartParentKind.NativePart, ParentId = 999, ParentPath = "Mounts/Fanbelt",
            BeltVisual = new PartBeltVisualState { Visible = true, Running = true, Scale = .8f, Pitch = 1.2f, Volume = .3f, ScrollSpeed = -24f } };

        [Theory]
        [InlineData(false, false, 1)]
        [InlineData(true, false, 3)]
        [InlineData(true, true, 7)]
        public void OptionalVisualExtendsTheCompleteReplacementPrefix(bool visible, bool running, byte flags)
        {
            var state = State(); state.BeltVisual!.Visible = visible; state.BeltVisual.Running = running;
            byte[] present = PacketCodec.Encode(state);
            var absentState = ReplacementPartReplica.Copy(state); absentState.BeltVisual = null;
            byte[] absent = PacketCodec.Encode(absentState);
            Assert.Equal(102 + state.NativeId.Length + state.ParentPath.Length + 4 * state.Scalars.Length, absent.Length);
            Assert.Equal(absent.Length + 16, present.Length);
            Assert.Equal(absent.Take(absent.Length - 4), present.Take(absent.Length - 4));
            Assert.Equal(0, absent[absent.Length - 4]);
            using var reader = new BinaryReader(new MemoryStream(present));
            reader.BaseStream.Position = absent.Length - 8;
            Assert.Equal(state.PresentationRevision, reader.ReadUInt32());
            Assert.Equal(flags, reader.ReadByte()); Assert.Equal(.8f, reader.ReadSingle());
            Assert.Equal(1.2f, reader.ReadSingle()); Assert.Equal(.3f, reader.ReadSingle());
            Assert.Equal(-24f, reader.ReadSingle());
            Assert.Equal(0, reader.ReadUInt16());
            Assert.Equal(0, reader.ReadByte());
            Assert.Equal(present.Length, reader.BaseStream.Position);
            var decoded = Assert.IsType<ReplacementPartState>(PacketCodec.Decode(present));
            Assert.True(PartBeltVisualPolicy.Same(state.BeltVisual, decoded.BeltVisual));
            Assert.Equal(present, PacketCodec.Encode(decoded));
            Assert.Null(Assert.IsType<ReplacementPartState>(PacketCodec.Decode(absent)).BeltVisual);
        }

        [Fact]
        public void UnknownOrContradictoryFlagsAndIncompleteExtensionsAreRejected()
        {
            byte[] bytes = PacketCodec.Encode(State()); int offset = bytes.Length - 20;
            for (int flags = 0; flags <= byte.MaxValue; flags++)
            {
                if (flags == 1 || flags == 3 || flags == 7) continue;
                byte[] bad = (byte[])bytes.Clone(); bad[offset] = (byte)flags;
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bad));
            }
            for (int length = offset - 4; length < bytes.Length; length++)
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(length).ToArray()));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
        }

        [Theory]
        [InlineData(0, .0998f)] [InlineData(0, 1.1002f)]
        [InlineData(1, -.001f)] [InlineData(1, 3.001f)]
        [InlineData(2, -.001f)] [InlineData(2, 1.001f)]
        [InlineData(3, -10000.1f)] [InlineData(3, 10000.1f)]
        [InlineData(0, float.NaN)] [InlineData(1, float.NaN)] [InlineData(2, float.NaN)] [InlineData(3, float.NaN)]
        [InlineData(0, float.PositiveInfinity)] [InlineData(1, float.NegativeInfinity)]
        [InlineData(2, float.PositiveInfinity)] [InlineData(3, float.NegativeInfinity)]
        public void InvalidHostValuesCannotBeWrittenDecodedOrReplaceAcceptedState(int field, float value)
        {
            var state = State(); byte[] bytes = PacketCodec.Encode(state);
            var replica = Replica(); Assert.True(replica.Receive(state, out uint id));
            state.Revision++;
            if (field == 0) state.BeltVisual!.Scale = value;
            else if (field == 1) state.BeltVisual!.Pitch = value;
            else if (field == 2) state.BeltVisual!.Volume = value;
            else state.BeltVisual!.ScrollSpeed = value;
            Assert.False(PartBeltVisualPolicy.Valid(state.BeltVisual));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            Assert.False(replica.Receive(state, out _)); Assert.Equal(1u, replica.Get(id)!.Revision);
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, bytes.Length - 19 + 4 * field, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }

        [Fact]
        public void NativeFloatRoundingAndAudioEndpointsAreAccepted()
        {
            var state = State(); var visual = state.BeltVisual!;
            foreach (float scale in new[] { PartBeltVisualPolicy.MinimumScale, 1f - .9f, .1f, 1.1f, PartBeltVisualPolicy.MaximumScale })
                foreach (float pitch in new[] { 0f, 3f })
                    foreach (float volume in new[] { 0f, 1f })
                    {
                        visual.Scale = scale; visual.Pitch = pitch; visual.Volume = volume;
                        Assert.True(PartBeltVisualPolicy.Valid(visual));
                        Assert.True(Replica().Receive(Assert.IsType<ReplacementPartState>(PacketCodec.Decode(PacketCodec.Encode(state))), out _));
                    }
            foreach (float scroll in new[] { -10000f, 0f, 10000f })
            {
                visual.ScrollSpeed = scroll;
                Assert.True(PartBeltVisualPolicy.Valid(visual));
                Assert.True(Replica().Receive(Assert.IsType<ReplacementPartState>(PacketCodec.Decode(PacketCodec.Encode(state))), out _));
            }
            visual.Visible = false;
            Assert.False(PartBeltVisualPolicy.Valid(visual));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            Assert.False(Replica().Receive(state, out _));
        }

        [Fact]
        public void OnlyAnAttachedProfiledReplacementAcceptsVisuals()
        {
            var state = State();
            var unprofiledRule = new ReplacementPartRule(Rule.FactoryId, Rule.Prefix, 2, 1);
            Assert.False(unprofiledRule.SupportsBeltVisual); Assert.True(Rule.SupportsBeltVisual);
            var unprofiled = new ReplacementPartReplica(new[] { unprofiledRule }, new ItemSpawnLifecycle());
            Assert.False(unprofiled.Receive(state, out _)); Assert.False(unprofiledRule.CanCreateFitted(state));
            state.BeltVisual = null;
            Assert.True(unprofiled.Receive(state, out _)); Assert.True(unprofiledRule.CanCreateFitted(state));

            state = State(); state.ParentKind = PartParentKind.None; state.ParentId = 0; state.ParentPath = "";
            Assert.False(Replica().Receive(state, out _)); Assert.False(Rule.CanCreateFitted(state));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            state.BeltVisual = null;
            Assert.True(Replica().Receive(state, out _));
            state.Installed = false; state.AssemblyId = 0;
            Assert.True(Rule.CanCreate(state)); Assert.True(Replica().Receive(state, out _));
            state.BeltVisual = new PartBeltVisualState();
            Assert.False(Rule.CanCreate(state)); Assert.False(Replica().Receive(state, out _));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
        }

        [Fact]
        public void EveryVisualChangeRequiresANewPresentationRevisionWithoutConsumingBroadcast()
        {
            var state = State(); var publication = new ReplacementPartPublication(); var replica = Replica();
            state.Revision = publication.Observe(state); Assert.True(replica.Receive(state, out uint id));
            publication.MarkBroadcast(state.Revision, state.PresentationRevision);
            state.Position.X = 20; Assert.Equal(state.Revision, publication.Observe(state)); Assert.False(publication.NeedsBroadcast);
            Assert.True(replica.Receive(state, out _));
            foreach (Action<ReplacementPartState> change in new Action<ReplacementPartState>[] {
                s => s.BeltVisual!.Scale = .7f, s => s.BeltVisual!.Pitch = 2f, s => s.BeltVisual!.Volume = 1f,
                s => s.BeltVisual!.ScrollSpeed = -60f,
                s => s.BeltVisual!.Running = false, s => s.BeltVisual!.Visible = false,
                s => s.BeltVisual = null, s => s.BeltVisual = new PartBeltVisualState { Visible = true } })
            {
                uint previous = state.PresentationRevision;
                change(state); Assert.False(replica.Receive(state, out _));
                state.Revision = publication.Observe(state); Assert.Equal(1u, state.Revision);
                Assert.Equal(previous + 1, state.PresentationRevision);
                Assert.True(publication.NeedsBroadcast);
                Assert.Equal(state.Revision, publication.Observe(ReplacementPartReplica.Copy(state)));
                Assert.True(publication.NeedsBroadcast);
                publication.MarkBroadcast(state.Revision, previous); Assert.True(publication.NeedsBroadcast);
                Assert.True(replica.Receive(state, out _));
                Assert.True(PartBeltVisualPolicy.Same(state.BeltVisual, replica.Get(id)!.BeltVisual));
                publication.MarkBroadcast(state.Revision, state.PresentationRevision); Assert.False(publication.NeedsBroadcast);
            }
        }

        [Fact]
        public void DeferredAndLateSnapshotsKeepIndependentVisualsAndCannotReviveRemovedBelt()
        {
            var life = new ItemSpawnLifecycle(); var replica = Replica(life); var incoming = State();
            Assert.True(replica.Receive(incoming, out uint id));
            var deferred = replica.Get(id)!;
            incoming.BeltVisual!.Scale = 1.1f; deferred.BeltVisual!.Volume = 1f;
            Assert.Equal(.8f, replica.Get(id)!.BeltVisual!.Scale); Assert.Equal(.3f, replica.Get(id)!.BeltVisual!.Volume);
            var copy = ReplacementPartReplica.Copy(replica.Get(id)!); copy.BeltVisual!.Pitch = 3f;
            Assert.Equal(1.2f, replica.Get(id)!.BeltVisual!.Pitch);
            var latest = State(); latest.PresentationRevision = 3; latest.BeltVisual!.Running = false; latest.BeltVisual.Volume = 0;
            Assert.True(replica.Receive(latest, out _));
            Assert.Equal(3u, replica.Get(id)!.PresentationRevision);
            var delayed = State(); delayed.PresentationRevision = 2;
            Assert.False(replica.Receive(delayed, out _)); Assert.False(replica.Get(id)!.BeltVisual!.Running);
            var loose = new ReplacementPartState { FactoryId = Rule.FactoryId, NativeId = incoming.NativeId,
                Revision = 2, Scalars = new[] { 1f, 0f }, Rotation = NetQuaternion.Identity };
            Assert.True(replica.Receive(loose, out _)); Assert.Null(replica.Get(id)!.BeltVisual);
            Assert.True(replica.AllowsLooseMotion(id)); Assert.False(replica.Receive(latest, out _));
            life.Retire(id); latest.Revision = 3;
            Assert.False(replica.Receive(latest, out _)); Assert.Null(replica.Get(id));
        }

        [Fact]
        public void CosmeticUpdatesPreserveAnObservedRemovalWhileGameplayChangesInvalidateIt()
        {
            var state = State(); state.RemovalAllowed = true;
            var publication = new ReplacementPartPublication(); state.Revision = publication.Observe(state);
            Assert.True(Rule.TryId(state.NativeId, out uint id));
            var request = new PartFitRequest { ItemId = id, Operation = PartFitOperation.Remove, ExpectedRevision = state.Revision };
            Assert.Equal(PartFitStatus.Pending, PartRemovalPolicy.Check(request, state, 1, true, true, true));
            state.BeltVisual!.ScrollSpeed = -100f; state.BeltVisual.Pitch = 2f;
            state.Revision = publication.Observe(state);
            Assert.Equal(request.ExpectedRevision, state.Revision); Assert.Equal(2u, state.PresentationRevision);
            Assert.Equal(PartFitStatus.Pending, PartRemovalPolicy.Check(request, state, 1, true, true, true));
            foreach (Action<ReplacementPartState> change in new Action<ReplacementPartState>[] {
                s => s.Scalars[0] -= 1, s => s.Scalars[1] = .5f, s => s.ParentPath += "/Other",
                s => s.LocalPosition.X += .1f, s => s.RemovalAllowed = false })
            {
                request.ExpectedRevision = state.Revision;
                uint previousPresentation = state.PresentationRevision;
                change(state); state.Revision = publication.Observe(state);
                Assert.Equal(request.ExpectedRevision + 1, state.Revision);
                Assert.Equal(previousPresentation + 1, state.PresentationRevision);
                Assert.Equal(PartFitStatus.Stale, PartRemovalPolicy.Check(request, state, 1, true, true, true));
            }
            request.ExpectedRevision = state.Revision;
            uint beforeBodyCycle = state.PresentationRevision;
            state.Revision = publication.Observe(state, bodyChanged: true);
            Assert.Equal(request.ExpectedRevision + 1, state.Revision);
            Assert.Equal(beforeBodyCycle + 1, state.PresentationRevision);
            Assert.Equal(PartFitStatus.Stale, PartRemovalPolicy.Check(request, state, 1, true, true, true));
            publication.MarkBroadcast(request.ExpectedRevision, state.PresentationRevision); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(state.Revision, state.PresentationRevision); Assert.False(publication.NeedsBroadcast);
        }

        [Fact]
        public void ObservationOwnsBothCountersAndDoesNotPublishChangedInputMetadata()
        {
            var publication = new ReplacementPartPublication(); var state = State();
            state.Revision = publication.Observe(state); publication.MarkBroadcast(state.Revision, state.PresentationRevision);
            var unstamped = ReplacementPartReplica.Copy(state);
            unstamped.Revision = 999; unstamped.PresentationRevision = 999;
            Assert.True(ReplacementPartReplica.SameValues(state, unstamped));
            Assert.True(ReplacementPartReplica.SameGameplayValues(state, unstamped));
            Assert.Equal(1u, publication.Observe(unstamped)); Assert.Equal(1u, unstamped.PresentationRevision);
            Assert.False(publication.NeedsBroadcast);
        }

        [Fact]
        public void PresentationSequenceCannotChangeGameplayAndEqualSequencesCannotChangeVisuals()
        {
            var replica = Replica(); var state = State(); Assert.True(replica.Receive(state, out uint id));
            foreach (Action<ReplacementPartState> change in new Action<ReplacementPartState>[] {
                s => s.Scalars[0]--, s => s.Scalars[1] = .5f, s => s.ParentPath += "/Other", s => s.AssemblyId++ })
            {
                var bad = ReplacementPartReplica.Copy(state); bad.PresentationRevision++;
                change(bad); Assert.False(replica.Receive(bad, out _));
                Assert.True(ReplacementPartReplica.SameValues(state, replica.Get(id)!));
            }
            var cosmetic = ReplacementPartReplica.Copy(state); cosmetic.BeltVisual!.Volume = .8f;
            Assert.True(ReplacementPartReplica.SameGameplayValues(state, cosmetic));
            Assert.False(ReplacementPartReplica.SameValues(state, cosmetic));
            Assert.False(replica.Receive(cosmetic, out _)); cosmetic.PresentationRevision++;
            Assert.True(replica.Receive(cosmetic, out _));
            var gameplay = ReplacementPartReplica.Copy(cosmetic); gameplay.Revision++; gameplay.PresentationRevision = 0;
            gameplay.Scalars[0]--;
            Assert.True(replica.Receive(gameplay, out _)); Assert.Equal(0u, replica.Get(id)!.PresentationRevision);
        }

        [Fact]
        public void GameplayAndPresentationCountersWrapIndependentlyWithoutAcceptingOldSnapshots()
        {
            var replica = Replica(); var state = State(); state.Revision = uint.MaxValue; state.PresentationRevision = uint.MaxValue;
            Assert.True(replica.Receive(state, out uint id));
            var beforeWrap = ReplacementPartReplica.Copy(state);
            state.PresentationRevision = 0; state.BeltVisual!.ScrollSpeed = -50;
            Assert.True(replica.Receive(state, out _)); Assert.False(replica.Receive(beforeWrap, out _));
            Assert.Equal(0u, replica.Get(id)!.PresentationRevision); Assert.Equal(uint.MaxValue, replica.Get(id)!.Revision);
            var presentationAmbiguous = ReplacementPartReplica.Copy(state); presentationAmbiguous.PresentationRevision = 0x80000000;
            Assert.False(replica.Receive(presentationAmbiguous, out _));
            state.Revision = 0; state.PresentationRevision = uint.MaxValue - 1; state.Scalars[0]--;
            Assert.True(replica.Receive(state, out _)); Assert.False(replica.Receive(beforeWrap, out _));
            var encoded = PacketCodec.Encode(replica.Get(id)!);
            var decoded = Assert.IsType<ReplacementPartState>(PacketCodec.Decode(encoded));
            Assert.Equal(0u, decoded.Revision); Assert.Equal(uint.MaxValue - 1, decoded.PresentationRevision);
            var gameplayAmbiguous = ReplacementPartReplica.Copy(state); gameplayAmbiguous.Revision = 0x80000000;
            Assert.False(replica.Receive(gameplayAmbiguous, out _));
        }
    }
}
