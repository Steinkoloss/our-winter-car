using System;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class TaxiPaydayTests
    {
        [Fact]
        public void EightNativeRundownValuesAndIndependentUnreadEnvelopeFlagsRoundTrip()
        {
            var state = new TaxiServiceState { PaydayId = 29, PaydayFlags = 3,
                PaydayRundown = new[] { 1000f, 400f, 900f, 120f, 100f, 444f, 18.3f, 825.7f } };
            var copy = Assert.IsType<TaxiServiceState>(PacketCodec.Decode(PacketCodec.Encode(state)));
            Assert.Equal(state.PaydayId, copy.PaydayId); Assert.Equal(state.PaydayFlags, copy.PaydayFlags);
            Assert.Equal(state.PaydayRundown, copy.PaydayRundown);
            var bytes = PacketCodec.Encode(state);
            Array.Resize(ref bytes, bytes.Length - 1);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void InvalidRundownValuesAreRejectedInBothDirections(float value)
        {
            var state = new TaxiServiceState(); state.PaydayRundown[7] = value;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            var bytes = PacketCodec.Encode(new TaxiServiceState());
            Array.Copy(BitConverter.GetBytes(value), 0, bytes, bytes.Length - 4, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Fact]
        public void InvalidReportIdentityFlagsAndShapeCannotReachPresentation()
        {
            Assert.False(TaxiServicePolicy.Valid(new TaxiServiceState { PaydayId = 0 }));
            Assert.False(TaxiServicePolicy.Valid(new TaxiServiceState { PaydayFlags = 4 }));
            Assert.False(TaxiServicePolicy.Valid(new TaxiServiceState { PaydayRundown = new float[7] }));
            Assert.False(TaxiServicePolicy.Valid(new TaxiServiceState { PaydayRundown = new float[9] }));
            var bytes = PacketCodec.Encode(new TaxiServiceState()); bytes[bytes.Length - 33] = 4;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Fact]
        public void ReadRequiresTheCurrentUnreadVisibleReportAndAuthenticatedNearbyPlayer()
        {
            var state = new TaxiServiceState { PaydayId = 4, PaydayFlags = 3 };
            var intent = new TaxiPaydayReadIntent { PlayerId = 1, PaydayId = 4 };
            Assert.True(TaxiServicePolicy.CanReadPayday(state, intent, 1, true));
            Assert.False(TaxiServicePolicy.CanReadPayday(state, intent, 2, true));
            Assert.False(TaxiServicePolicy.CanReadPayday(state, intent, 1, false));
            state.PaydayId++;
            Assert.False(TaxiServicePolicy.CanReadPayday(state, intent, 1, true));
            state.PaydayId--;
            foreach (byte flags in new byte[] { 0, 1, 2 })
            { state.PaydayFlags = flags; Assert.False(TaxiServicePolicy.CanReadPayday(state, intent, 1, true)); }
        }
        [Fact]
        public void AcknowledgmentHasNoMoneyPayloadAndCannotBeSentAsHostState()
        {
            var intent = new TaxiPaydayReadIntent { PlayerId = 1, PaydayId = uint.MaxValue };
            byte[] bytes = PacketCodec.Encode(intent); Assert.Equal(7, bytes.Length);
            var copy = Assert.IsType<TaxiPaydayReadIntent>(PacketCodec.Decode(bytes));
            Assert.Equal(intent.PaydayId, copy.PaydayId); Assert.Equal(intent.PlayerId, copy.PlayerId);
            Assert.True(SessionMessagePolicy.IsSenderAllowed(intent.Id, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(intent.Id, false, true, true, true));
            intent.PaydayId = 0; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(intent));
            intent.PaydayId = 1; intent.PlayerId = 255; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(intent));
        }
    }
}
