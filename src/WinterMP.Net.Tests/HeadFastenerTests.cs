using System;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class HeadFastenerTests
    {
        private static CylinderHeadState State() => new CylinderHeadState { NetId = 4, ParentId = 5, Revision = 9, FastenersAvailable = true };
        private static PartFitRequest Request() => new PartFitRequest { ItemId = 4, ExpectedRevision = 9,
            PlayerId = 1, Token = 123, Sequence = 1, Operation = PartFitOperation.ToolTighten, SlotIndex = 10 };
        [Fact]
        public void TenNativeSlotsAndAggregateSurviveWireAndCopies()
        {
            var state = State(); for (int i = 0; i < 10; i++) state.Fasteners[i] = (byte)(i % 9); state.Tightness = 36.25f;
            var bytes = PacketCodec.Encode(state); Assert.Equal(61, bytes.Length); Assert.Equal(1, bytes[46]);
            Assert.Equal(state.Fasteners, bytes.Skip(47).Take(10)); Assert.Equal(state.Tightness, BitConverter.ToSingle(bytes, 57));
            var decoded = (CylinderHeadState)PacketCodec.Decode(bytes); Assert.Equal(bytes, PacketCodec.Encode(CylinderHeadPolicy.Copy(decoded)));
            var copy = CylinderHeadPolicy.Copy(state); copy.Fasteners[0] = 8; Assert.Equal(0, state.Fasteners[0]);
            for (int n = 46; n < 61; n++) Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(n).ToArray()));
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)] [InlineData(-1f)] [InlineData(10001f)]
        public void InvalidAggregateIsRejected(float value)
        {
            var state = State(); var bytes = PacketCodec.Encode(state); state.Tightness = value;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state)); Array.Copy(BitConverter.GetBytes(value), 0, bytes, 57, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Fact]
        public void AvailabilityAndEveryBoltAreStrict()
        {
            var state = State(); var bytes = PacketCodec.Encode(state); bytes[46] = 2;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            for (int i = 0; i < 10; i++)
            { bytes = PacketCodec.Encode(state); bytes[47 + i] = 9; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes)); }
            state.FastenersAvailable = false; state.Fasteners[9] = 1; Assert.False(CylinderHeadPolicy.Valid(state));
            state.Fasteners[9] = 0; state.Tightness = 1; Assert.False(CylinderHeadPolicy.Valid(state));
        }
        [Fact]
        public void ChangedBoltRequiresNewRevisionAndDoesNotChangeAttachment()
        {
            var before = State(); var after = CylinderHeadPolicy.Copy(before); after.Fasteners[9] = 1; after.Tightness = 1;
            Assert.True(CylinderHeadPolicy.SameAttachment(before, after)); Assert.False(CylinderHeadPolicy.SameFasteners(before, after));
            Assert.False(CylinderHeadPolicy.CanReceive(before, after)); after.Revision++;
            Assert.True(CylinderHeadPolicy.CanReceive(before, after)); Assert.False(CylinderHeadPolicy.CanReceive(after, before));
            Assert.NotEqual(CylinderHeadPolicy.MixChecksum(0, before), CylinderHeadPolicy.MixChecksum(0, after));
        }
        [Theory]
        [InlineData(1)] [InlineData(10)]
        public void IndexedToolRequestAndReceiptRoundTrip(byte slot)
        {
            var request = Request(); request.SlotIndex = slot;
            Assert.Equal(slot, ((PartFitRequest)PacketCodec.Decode(PacketCodec.Encode(request))).SlotIndex);
            var receipt = PartFitLedger.Receipt(request, PartFitStatus.Accepted);
            Assert.Equal(slot, ((PartFitReceipt)PacketCodec.Decode(PacketCodec.Encode(receipt))).SlotIndex);
            request.SlotIndex = 11; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(request));
        }
        [Fact]
        public void HostChecksRevisionInstallationDistanceReadinessAndNativeBounds()
        {
            var state = State(); var request = Request();
            Assert.Equal(PartFitStatus.Pending, CylinderHeadPolicy.CheckFastener(request, state, true, true, false));
            Assert.Equal(PartFitStatus.TooFar, CylinderHeadPolicy.CheckFastener(request, state, true, false, false));
            Assert.Equal(PartFitStatus.Busy, CylinderHeadPolicy.CheckFastener(request, state, true, true, true));
            Assert.Equal(PartFitStatus.Unavailable, CylinderHeadPolicy.CheckFastener(request, state, false, true, false));
            state.Revision++; Assert.Equal(PartFitStatus.Stale, CylinderHeadPolicy.CheckFastener(request, state, true, true, false)); state.Revision--;
            state.Fasteners[9] = 8; Assert.Equal(PartFitStatus.Blocked, CylinderHeadPolicy.CheckFastener(request, state, true, true, false));
            request.Operation = PartFitOperation.ToolLoosen; Assert.Equal(PartFitStatus.Pending, CylinderHeadPolicy.CheckFastener(request, state, true, true, false));
            state.Fasteners[9] = 0; Assert.Equal(PartFitStatus.Blocked, CylinderHeadPolicy.CheckFastener(request, state, true, true, false));
            state.ParentId = 0; state.Mass = 12; Assert.Equal(PartFitStatus.NotFitted, CylinderHeadPolicy.CheckFastener(request, state, true, true, false));
            request.SlotIndex = 0; Assert.Equal(PartFitStatus.Unavailable, CylinderHeadPolicy.CheckFastener(request, state, true, true, false));
        }
        [Fact]
        public void RetriedOrAlteredSlotCannotTurnTwice()
        {
            var request = Request(); var ledger = new PartFitLedger();
            Assert.NotNull(ledger.Begin(request, 1, PartFitStatus.Pending)); Assert.NotNull(ledger.Complete(request, true));
            Assert.Equal(PartFitStatus.Accepted, ledger.Inspect(request, 1, out bool begin)!.Status); Assert.False(begin);
            request.SlotIndex = 1; Assert.Null(ledger.Inspect(request, 1, out begin)); Assert.False(begin);
        }
    }
}
