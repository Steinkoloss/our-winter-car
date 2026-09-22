using System;
using System.IO;
using System.Linq;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class TaxiServiceTests
    {
        private static TaxiServiceState Ringing() => new TaxiServiceState { Revision = 12, CallId = 7,
            Flags = TaxiServiceState.Car | TaxiServiceState.Phone, CallPhase = TaxiCallPhase.Ringing };
        private static TaxiCallIntent Answer(byte player = 1) => new TaxiCallIntent { PlayerId = player, CallId = 7, Action = TaxiCallAction.Answer };

        [Fact]
        public void OutgoingKeypadRequiresAnAvailableIdleUnownedTaxiPhone()
        {
            var state = Ringing(); Assert.False(TaxiServicePolicy.CanDial(state));
            state.CallPhase = TaxiCallPhase.Speaking; Assert.False(TaxiServicePolicy.CanDial(state));
            state.CallPhase = TaxiCallPhase.Finished; Assert.False(TaxiServicePolicy.CanDial(state));
            state.CallPhase = TaxiCallPhase.Silent; Assert.True(TaxiServicePolicy.CanDial(state));
            state.CallOwner = 1; Assert.False(TaxiServicePolicy.CanDial(state));
            state.CallOwner = TaxiServiceState.Nobody; state.Flags = TaxiServiceState.Car; Assert.False(TaxiServicePolicy.CanDial(state));
            state.Flags = TaxiServiceState.Phone; Assert.False(TaxiServicePolicy.CanDial(state));
        }

        [Fact]
        public void AcceptedAnswerCannotBeDuplicatedOrStolenAndOldCallCannotAnswerNewCustomer()
        {
            var state = Ringing(); var intent = Answer();
            Assert.True(TaxiServicePolicy.CanAct(state, intent, 1, true));
            state.CallOwner = 1;
            Assert.False(TaxiServicePolicy.CanAct(state, intent, 1, true));
            Assert.False(TaxiServicePolicy.CanAct(state, Answer(2), 2, true));
            state.CallPhase = TaxiCallPhase.Silent; state.CallOwner = TaxiServiceState.Nobody;
            Assert.False(TaxiServicePolicy.CanAct(state, intent, 1, true));
            state.CallId++; state.CallPhase = TaxiCallPhase.Ringing;
            Assert.False(TaxiServicePolicy.CanAct(state, intent, 1, true));
            intent.CallId++;
            Assert.True(TaxiServicePolicy.CanAct(state, intent, 1, true));
        }
        [Fact]
        public void OnlyTheOwnerCanHangUpAndCanDoSoAfterLeavingThePhone()
        {
            var state = Ringing(); state.CallOwner = 1; state.CallPhase = TaxiCallPhase.Speaking;
            var intent = Answer(); intent.Action = TaxiCallAction.HangUp;
            Assert.True(TaxiServicePolicy.CanAct(state, intent, 1, false));
            Assert.False(TaxiServicePolicy.CanAct(state, intent, 2, true));
            intent.PlayerId = 2;
            Assert.False(TaxiServicePolicy.CanAct(state, intent, 2, true));
            state.CallOwner = TaxiServiceState.Nobody; intent.PlayerId = 1;
            Assert.False(TaxiServicePolicy.CanAct(state, intent, 1, true));
        }
        [Theory]
        [InlineData(TaxiCallPhase.Silent)] [InlineData(TaxiCallPhase.Speaking)] [InlineData(TaxiCallPhase.Finished)]
        public void OnlyAnAudiblyRingingCallCanBeAnswered(TaxiCallPhase phase)
        { var state = Ringing(); state.CallPhase = phase; Assert.False(TaxiServicePolicy.CanAct(state, Answer(), 1, true)); }
        [Fact]
        public void AnswerRequiresAuthenticatedActorNearbyAndEnabledService()
        {
            var state = Ringing(); var intent = Answer();
            Assert.False(TaxiServicePolicy.CanAct(state, intent, 2, true));
            Assert.False(TaxiServicePolicy.CanAct(state, intent, 1, false));
            state.Flags = TaxiServiceState.Car;
            Assert.False(TaxiServicePolicy.CanAct(state, intent, 1, true));
            state.Flags = TaxiServiceState.Phone;
            Assert.False(TaxiServicePolicy.CanAct(state, intent, 1, true));
        }
        [Fact]
        public void AbsoluteCustomerStateRoundTripsIncludingBoardingAndFinnishAddress()
        {
            var state = Ringing(); state.Flags = 255; state.ColliderFlags = 7;
            state.Pickup = "Rykipohjantie 11"; state.Destination = "Peräjärvi"; state.IndicatorText = "DESTINATION: Peräjärvi";
            state.Subtitle = "Please pick me up."; state.Voice = "TaxiMan1A";
            state.WalkerPosition = new NetVector3(.1f, -.2f, .3f); state.CustomerPosition = new NetVector3(23, 12, -540);
            state.WalkerRotation = new NetQuaternion(0, 1, 0, 0); state.RootClip = "TaxiGetIn"; state.RootTime = 2.5f;
            state.SkeletonClip = "fat_walk"; state.SkeletonTime = 1.2f;
            byte[] bytes = PacketCodec.Encode(state);
            var copy = Assert.IsType<TaxiServiceState>(PacketCodec.Decode(bytes));
            Assert.Equal(bytes, PacketCodec.Encode(copy)); Assert.Equal(state.Destination, copy.Destination);
            Assert.Equal(state.WalkerPosition.Y, copy.WalkerPosition.Y); Assert.Equal(255u, copy.Flags);
            Assert.IsType<TaxiCallIntent>(PacketCodec.Decode(PacketCodec.Encode(Answer())));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(new ArraySegment<byte>(bytes, 0, bytes.Length - 1).ToArray()));
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void InvalidPoseAndAnimationClocksCannotReachPresentation(float invalid)
        {
            var state = Ringing(); state.WalkerPosition.X = invalid;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            state = Ringing(); state.RootTime = invalid; Assert.False(TaxiServicePolicy.Valid(state));
            state = Ringing(); state.CustomerRotation.W = invalid; Assert.False(TaxiServicePolicy.Valid(state));
        }
        [Fact]
        public void MalformedFlagsStringsAndIntentsAreRejectedOnBothWireDirections()
        {
            var state = Ringing(); byte[] bytes = PacketCodec.Encode(state);
            state.Flags = 256; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            // Two-byte message ID, revision, call ID, then the flags.
            Array.Copy(BitConverter.GetBytes(256u), 0, bytes, 10, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            state = Ringing(); state.Destination = new string('x', 129); Assert.False(TaxiServicePolicy.Valid(state));
            state = Ringing(); state.Voice = "a\0b"; Assert.False(TaxiServicePolicy.Valid(state));
            var intent = Answer(); intent.CallId = 0; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(intent));
            intent = Answer(); intent.Action = (TaxiCallAction)2; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(intent));
        }
        [Theory]
        [InlineData(1u, 1u, false)] [InlineData(2u, 1u, false)] [InlineData(1u, 2u, true)]
        [InlineData(uint.MaxValue, 0u, true)] [InlineData(0u, uint.MaxValue, false)] [InlineData(0u, 0x80000000u, false)]
        public void OldOrRepeatedPresentationCannotRollBackCustomer(uint previous, uint next, bool expected)
            => Assert.Equal(expected, TaxiServicePolicy.Newer(previous, next));
        [Fact]
        public void StateOnlyComesFromAdmittedHostAndIntentsOnlyFromAuthenticatedGuests()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiServiceState, true, true, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiServiceState, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiServiceState, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiServiceState, false, true, true, false));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiCallIntent, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiCallIntent, true, false, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiCallIntent, true, true, false, true));
            foreach (var id in new[] { MessageId.TaxiCallIntent, MessageId.TaxiServiceState })
            {
                Assert.True(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableOrdered));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.UnreliableSequenced));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableBulk));
            }
        }
        [Fact]
        public void ServiceCatalogUsesStableNativeCustomerReferenceAndContainsBadConfiguration()
        {
            var data = SyncCatalogJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")));
            Assert.Null(data.TaxiServiceError); Assert.NotNull(data.TaxiService);
            Assert.Equal("Customer", data.TaxiService!["customerVariable"]);
            Assert.Equal("Pick phone", data.TaxiService["answerState"]);
            var bad = SyncCatalogJson.Parse("{\"taxiService\":{}}");
            Assert.Null(bad.TaxiService); Assert.NotNull(bad.TaxiServiceError);
        }
    }
}
