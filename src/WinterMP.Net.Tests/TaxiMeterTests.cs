using System;
using System.IO;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class TaxiMeterTests
    {
        private static TaxiMeterState State() => new TaxiMeterState { Revision = 4, ControlRevision = 8, Flags = TaxiMeterState.Available, Mode = 2,
            Values = new float[] { 21, 21, .2f, 12, 56, 40, 2, 3, 10 }, Display = "21.00", ModeDisplay = "1" };
        private static TaxiMeterIntent Intent() => new TaxiMeterIntent { PlayerId = 1, Sequence = 3, ExpectedControlRevision = 8, Action = TaxiMeterAction.ToggleLight };
        [Fact]
        public void MeterAndIntentRoundTripWithoutChangingNativeValues()
        {
            var s = State(); s.KnobRotation = new NetQuaternion(1, 0, 0, 0); var bytes = PacketCodec.Encode(s); var copy = Assert.IsType<TaxiMeterState>(PacketCodec.Decode(bytes));
            Assert.Equal(bytes, PacketCodec.Encode(copy)); Assert.Equal(s.Values, copy.Values); Assert.Equal(s.Display, copy.Display); Assert.Equal(1f, copy.KnobRotation.X); Assert.Equal(0f, copy.KnobRotation.W);
            var intent = Intent(); var restored = Assert.IsType<TaxiMeterIntent>(PacketCodec.Decode(PacketCodec.Encode(intent)));
            Assert.Equal(intent.ExpectedControlRevision, restored.ExpectedControlRevision); Assert.Equal(intent.Action, restored.Action);
        }
        [Theory]
        [InlineData(TaxiMeterAction.IncreaseMode)] [InlineData(TaxiMeterAction.DecreaseMode)]
        [InlineData(TaxiMeterAction.ToggleLight)] [InlineData(TaxiMeterAction.ResetTotals)]
        public void EachControlRequiresCurrentAvailableIdleMeterAndNearbyAuthenticatedActor(TaxiMeterAction action)
        {
            var s = State(); var intent = Intent(); intent.Action = action;
            Assert.True(TaxiMeterPolicy.CanAct(s, intent, 1, true, false));
            Assert.False(TaxiMeterPolicy.CanAct(s, intent, 2, true, false));
            Assert.False(TaxiMeterPolicy.CanAct(s, intent, 1, false, false));
            Assert.False(TaxiMeterPolicy.CanAct(s, intent, 1, true, true));
            s.Flags = 0; Assert.False(TaxiMeterPolicy.CanAct(s, intent, 1, true, false));
            s.Flags = TaxiMeterState.Available; s.ControlRevision++;
            Assert.False(TaxiMeterPolicy.CanAct(s, intent, 1, true, false));
            s.ControlRevision--; intent.Sequence = 0; Assert.False(TaxiMeterPolicy.CanAct(s, intent, 1, true, false));
        }
        [Fact]
        public void DisplayUpdatesDoNotInvalidateControlsButAnotherControlDoes()
        {
            var s = State(); var intent = Intent(); s.Revision++; s.Values[0] += 2.483f;
            Assert.True(TaxiMeterPolicy.CanAct(s, intent, 1, true, false));
            s.ControlRevision++; Assert.False(TaxiMeterPolicy.CanAct(s, intent, 1, true, false));
        }
        [Fact]
        public void SequenceConsumptionCoversRejectedRequestsWrapAndRejoin()
        {
            var order = new TaxiMeterIntentOrder(); Assert.True(order.Accept(1, uint.MaxValue));
            Assert.False(order.Accept(1, uint.MaxValue)); Assert.False(order.Accept(1, uint.MaxValue - 1));
            Assert.True(order.Accept(1, 1)); Assert.False(order.Accept(1, uint.MaxValue));
            Assert.True(order.Accept(2, 1)); Assert.False(order.Accept(1, 0)); Assert.False(order.Accept(255, 1));
            order.Forget(1); Assert.True(order.Accept(1, 1)); Assert.False(order.Accept(2, 1));
        }
        [Theory]
        [InlineData(0, TaxiMeterAction.DecreaseMode, false)] [InlineData(0, TaxiMeterAction.IncreaseMode, true)]
        [InlineData(4, TaxiMeterAction.IncreaseMode, true)] [InlineData(5, TaxiMeterAction.IncreaseMode, false)]
        [InlineData(6, TaxiMeterAction.IncreaseMode, false)] [InlineData(6, TaxiMeterAction.DecreaseMode, true)]
        [InlineData(6, TaxiMeterAction.ResetTotals, false)] [InlineData(6, TaxiMeterAction.ToggleLight, false)]
        public void NormalModeLimitsKeepAuxiliaryControlsHostOperated(byte mode, TaxiMeterAction action, bool accepted)
        {
            var s = State(); s.Mode = mode; var intent = Intent(); intent.Action = action;
            Assert.Equal(accepted, TaxiMeterPolicy.CanAct(s, intent, 1, true, false));
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        [InlineData(-1f)] [InlineData(10000001f)]
        public void MalformedNativeValuesAreRejectedInBothWireDirections(float value)
        {
            var s = State(); var bytes = PacketCodec.Encode(s); s.Values[0] = value;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            Array.Copy(BitConverter.GetBytes(value), 0, bytes, 13, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(3f)]
        public void InvalidKnobPoseCannotReachGuestInEitherWireDirection(float value)
        {
            var s = State(); var bytes = PacketCodec.Encode(s); s.KnobRotation.X = value;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            Array.Copy(BitConverter.GetBytes(value), 0, bytes, bytes.Length - 16, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Fact]
        public void BadFlagsModesStringsArraysAndIntentFieldsAreRejected()
        {
            var s = State(); s.Flags = 512; Assert.False(TaxiMeterPolicy.Valid(s));
            s = State(); s.Mode = 7; Assert.False(TaxiMeterPolicy.Valid(s));
            s = State(); s.Display = new string('a', 129); Assert.False(TaxiMeterPolicy.Valid(s));
            s = State(); s.ModeDisplay = "1\0"; Assert.False(TaxiMeterPolicy.Valid(s));
            s = State(); s.Values = new float[8]; Assert.False(TaxiMeterPolicy.Valid(s));
            var intent = Intent(); intent.Sequence = 0; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(intent));
            intent = Intent(); intent.PlayerId = 255; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(intent));
            intent = Intent(); intent.Action = (TaxiMeterAction)4; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(intent));
        }
        [Fact]
        public void DistanceInputRequiresFreshAcceptedMatchingOwnerAndCannotUseJoinSnapshot()
        {
            var s = new VehicleState { VehicleId = 12, OwnerPlayerId = 1, Sequence = 4, SpeedTenthsKmh = 360 };
            Assert.Equal(10, TaxiMeterPolicy.DelegatedSpeed(s, 12, 1, true));
            Assert.Equal(0, TaxiMeterPolicy.DelegatedSpeed(s, 12, 1, false));
            Assert.Equal(0, TaxiMeterPolicy.DelegatedSpeed(s, 12, 2, true));
            Assert.Equal(0, TaxiMeterPolicy.DelegatedSpeed(s, 13, 1, true));
            Assert.Equal(0, TaxiMeterPolicy.DelegatedSpeed(null, 12, 1, true));
            s.Sequence = VehicleState.SnapshotSequence; Assert.Equal(0, TaxiMeterPolicy.DelegatedSpeed(s, 12, 1, true));
        }
        [Fact]
        public void OnlyAdmittedHostStateAndAuthenticatedGuestIntentsUseOrderedChannel()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiMeterState, true, true, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiMeterState, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiMeterState, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiMeterState, false, true, true, false));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiMeterIntent, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiMeterIntent, true, false, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiMeterIntent, true, true, false, true));
            foreach (var id in new[] { MessageId.TaxiMeterState, MessageId.TaxiMeterIntent })
            {
                Assert.True(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableOrdered));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.UnreliableSequenced));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableBulk));
            }
        }
        [Fact]
        public void CatalogRetainsReversedNativeWheelNamesAndContainsMissingConfig()
        {
            var data = SyncCatalogJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")));
            Assert.Null(data.TaxiMeterError); Assert.NotNull(data.TaxiMeter);
            Assert.Equal("Volume dec", data.TaxiMeter!["increase"]); Assert.Equal("Volume inc", data.TaxiMeter["decrease"]);
            var bad = SyncCatalogJson.Parse("{\"taxiMeter\":{}}"); Assert.Null(bad.TaxiMeter); Assert.NotNull(bad.TaxiMeterError);
        }
    }
}
