using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class ContainerFuelTests
    {
        private sealed class World : IContainerFuelWorld
        {
            public float Source = 10, Destination = 20, SourceCapacity = 20, Capacity = 40;
            public byte Owner = 2;
            public bool Compatible = true, Parked = true, Near = true, Commit = true;
            public int Writes;
            public bool TryRead(uint source, uint vehicle, byte actor, out ContainerFuelFacts facts)
            {
                facts = new ContainerFuelFacts { SourceLevel = Source, DestinationLevel = Destination,
                    SourceCapacity = SourceCapacity, DestinationCapacity = Capacity,
                    OwnsSource = actor == Owner, Compatible = Compatible, Parked = Parked, Near = Near };
                return source == 10 && vehicle == 20;
            }
            public bool TryCommit(ContainerFuelFacts before, ContainerFuelResult result)
            {
                if (!Commit) return false;
                Source = result.SourceLevel; Destination = result.DestinationLevel; Writes++; return true;
            }
        }
        private static ContainerFuelIntent Request(uint sequence = 1, float amount = 2) => new ContainerFuelIntent {
            SourceId = 10, VehicleId = 20, PlayerId = 2, Sequence = sequence, Amount = amount };

        [Fact]
        public void AuthenticatedGuestConservesAndBothPeersApplyOneAbsoluteResult()
        {
            var world = new World(); var authority = new ContainerFuelAuthority(world);
            var request = Assert.IsType<ContainerFuelIntent>(PacketCodec.Decode(PacketCodec.Encode(Request())));
            Assert.True(authority.TryAccept(request, 2, out var accepted));
            var result = Assert.IsType<ContainerFuelResult>(PacketCodec.Decode(PacketCodec.Encode(accepted!)));
            Assert.Equal(2, result.AcceptedAmount); Assert.Equal(8, world.Source); Assert.Equal(22, world.Destination);
            Assert.Equal(30d, (double)world.Source + world.Destination); Assert.Equal(1, world.Writes);
            var peers = new[] { new World(), new World() };
            foreach (var peer in peers) { peer.Source = result.SourceLevel; peer.Destination = result.DestinationLevel;
                Assert.Equal(world.Source, peer.Source); Assert.Equal(world.Destination, peer.Destination); }
            Assert.False(authority.TryAccept(request, 2, out _)); Assert.Equal(1, world.Writes);
        }
        [Theory]
        [InlineData("actor")] [InlineData("owner")] [InlineData("source")] [InlineData("vehicle")]
        [InlineData("compatible")] [InlineData("parked")] [InlineData("near")]
        [InlineData("zero")] [InlineData("negative")] [InlineData("nan")] [InlineData("infinity")]
        [InlineData("source-short")] [InlineData("capacity")] [InlineData("invalid-level")]
        [InlineData("invalid-capacity")] [InlineData("commit")]
        public void RejectedRequestsLeaveBothLevelsAndSequenceUntouched(string failure)
        {
            var w = new World(); var a = new ContainerFuelAuthority(w); var r = Request(); byte actor = 2;
            switch (failure) {
                case "actor": actor=3; break; case "owner": w.Owner=3; break;
                case "source": r.SourceId=999; break; case "vehicle": r.VehicleId=999; break;
                case "compatible": w.Compatible=false; break; case "parked": w.Parked=false; break;
                case "near": w.Near=false; break; case "zero": r.Amount=0; break;
                case "negative": r.Amount=-1; break; case "nan": r.Amount=float.NaN; break;
                case "infinity": r.Amount=float.PositiveInfinity; break; case "source-short": r.Amount=11; break;
                case "capacity": w.Destination=39; break; case "invalid-level": w.Source=float.NaN; break;
                case "invalid-capacity": w.Capacity=float.PositiveInfinity; break; case "commit": w.Commit=false; break;
            }
            float source=w.Source, destination=w.Destination;
            Assert.False(a.TryAccept(r, actor, out var result)); Assert.Null(result);
            Assert.Equal(source,w.Source); Assert.Equal(destination,w.Destination); Assert.Equal(0,w.Writes);
            w.Source=10; w.Destination=20; w.Owner=2; w.Capacity=40;
            w.Compatible=w.Parked=w.Near=w.Commit=true;
            Assert.True(a.TryAccept(Request(),2,out _)); Assert.Equal(1,w.Writes);
        }
        [Fact]
        public void MonotonicHistorySurvivesCompetingGuestAndOutOfOrderRequests()
        {
            var w=new World(); var a=new ContainerFuelAuthority(w);
            Assert.True(a.TryAccept(Request(5),2,out _));
            Assert.False(a.TryAccept(Request(4),2,out _));
            Assert.False(a.TryAccept(Request(5),2,out _));
            var other=Request(50); other.PlayerId=3;
            Assert.False(a.TryAccept(other,3,out _));
            Assert.True(a.TryAccept(Request(6),2,out _)); Assert.Equal(2,w.Writes);
        }
        [Fact]
        public void HostUsesSameValidationAndConservationPath()
        {
            var w=new World { Owner=0 }; var a=new ContainerFuelAuthority(w); var r=Request(); r.PlayerId=0;
            Assert.True(a.TryAccept(r,0,out _)); Assert.Equal(8,w.Source); Assert.Equal(22,w.Destination);
        }
        [Fact]
        public void RejectsUnrepresentableConservationInsteadOfRoundingFuelIntoExistence()
        {
            var w=new World { Source=100000000f, SourceCapacity=100000000f }; var a=new ContainerFuelAuthority(w);
            Assert.False(a.TryAccept(Request(amount:.25f),2,out _)); Assert.Equal(0,w.Writes);
        }
        [Theory]
        [InlineData(MessageId.ContainerFuelIntent,true)] [InlineData(MessageId.ContainerFuelResult,false)]
        public void AdmissionIsAuthenticatedDirectionalAndOrdered(MessageId id,bool intent)
        {
            Assert.True(SessionMessagePolicy.IsSenderAllowed(id,intent,true,true,true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id,!intent,true,true,true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id,intent,false,false,false));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(id,Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(id,Channel.UnreliableSequenced));
        }
    }
}
