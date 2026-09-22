using System;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class UtilityBillTests
    {
        [Theory]
        [InlineData(0)] [InlineData(1)]
        public void CutoffSurvivesAnOnSwitchAndPreservesTheVisibleBill(byte meter)
        {
            var state = new UtilityBillState { Meter = meter, UnpaidBills = 12.375f, Revision = 0x12345678,
                Flags = UtilityBillState.FlagBillVisible | UtilityBillState.FlagMainSwitchOn };
            byte[] bytes = PacketCodec.Encode(state);
            Assert.Equal(12, bytes.Length); Assert.Equal(95, BitConverter.ToUInt16(bytes, 0));
            Assert.Equal(meter, bytes[2]); Assert.Equal(12.375f, BitConverter.ToSingle(bytes, 3)); Assert.Equal(6, bytes[7]);
            Assert.Equal(0x12345678u, BitConverter.ToUInt32(bytes, 8));
            var received = Assert.IsType<UtilityBillState>(PacketCodec.Decode(bytes));
            Assert.False(received.PowerOn); Assert.True(received.MainSwitchOn); Assert.True(received.BillVisible);
            Assert.True(UtilityBillPolicy.Same(state, received));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(11).ToArray()));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
        public void OnlyDefinedFlagsAreAcceptedForEachMeter(byte meter)
        {
            for (int flags = 0; flags <= byte.MaxValue; flags++)
            {
                var state = new UtilityBillState { Meter = meter, UnpaidBills = 1, Flags = (byte)flags, Phone = meter < 2 ? null : new PhoneBillQuote() };
                bool valid = flags <= (meter < 2 ? 7 : 3);
                Assert.Equal(valid, UtilityBillPolicy.Valid(state));
                if (valid) Assert.True(UtilityBillPolicy.Same(state, Assert.IsType<UtilityBillState>(PacketCodec.Decode(PacketCodec.Encode(state)))));
                else Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
                var packet = new byte[] { 95, 0, meter, 0, 0, 128, 63, (byte)flags, 0, 0, 0, 0 };
                if (!valid) Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet));
            }
        }

        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)] [InlineData(-.01f)]
        public void InvalidDebtIsRejectedInBothDirections(float value)
        {
            var state = new UtilityBillState { Meter = 0, UnpaidBills = value };
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            var packet = new byte[] { 95, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            Array.Copy(BitConverter.GetBytes(value), 0, packet, 3, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet));
        }

        [Fact]
        public void UnknownMeterCannotIndexThePendingStateSlots()
        {
            for (int meter = 4; meter <= byte.MaxValue; meter++)
            {
                Assert.Throws<ProtocolException>(() => PacketCodec.Encode(new UtilityBillState { Meter = (byte)meter }));
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(new byte[] { 95, 0, (byte)meter, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
            }
        }

        [Fact]
        public void RetainedStateIsIndependentAndChangesIncludePowerAndEnvelope()
        {
            var state = new UtilityBillState { Meter = 1, UnpaidBills = 123.25f, Flags = 6 };
            var copy = UtilityBillPolicy.Copy(state); Assert.True(UtilityBillPolicy.Same(state, copy));
            state.Flags = 5; Assert.False(UtilityBillPolicy.Same(state, copy));
            Assert.False(copy.PowerOn); Assert.True(copy.BillVisible);
            state = UtilityBillPolicy.Copy(copy); state.UnpaidBills += .25f; Assert.False(UtilityBillPolicy.Same(state, copy));
        }

        [Fact]
        public void OnlyTheAdmittedHostCanSupplyUtilityStateOnTheOrderedChannel()
        {
            var id = MessageId.UtilityBillState;
            Assert.True(SessionMessagePolicy.IsSenderAllowed(id, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, false, true, true, false));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, true, true, false, true));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableBulk));
        }
    }
}
