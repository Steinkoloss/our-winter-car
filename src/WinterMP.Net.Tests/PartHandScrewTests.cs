using System;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class PartHandScrewTests
    {
        private static PartFitRequest Request(PartFitOperation operation = PartFitOperation.HandTighten, byte player = 1)
        {
            PartIdentity.TryItemId("OILFILTR01", out uint id);
            return new PartFitRequest { PlayerId = player, Token = (ulong)(100 + player), Sequence = 1,
                ItemId = id, ExpectedRevision = 7, Operation = operation };
        }

        private static ReplacementPartState State(float tightness = 4) => new ReplacementPartState {
            FactoryId = FactoryItemIdentity.FactoryId("Spawner/CreateItems", "Oilfilter"),
            NativeId = "OILFILTR01", Revision = 7, Installed = true, AssemblyId = 1,
            ParentKind = PartParentKind.NativePart, ParentId = 3, ParentPath = "Oilfilter",
            Scalars = new[] { 25f, tightness }, Rotation = NetQuaternion.Identity };

        private static PartFitStatus Check(PartFitRequest request, ReplacementPartState? state) =>
            PartHandScrewPolicy.Check(request, state, 1, true, true, true, false);

        [Theory]
        [InlineData(PartFitOperation.HandTighten, 4)]
        [InlineData(PartFitOperation.HandLoosen, 5)]
        public void DedicatedOperationsPreserveExistingFramingAndRequireSlotZero(PartFitOperation operation, byte value)
        {
            var request = Request(operation);
            var receipt = PartFitLedger.Receipt(request, PartFitStatus.Accepted);
            byte[] requestWire = PacketCodec.Encode(request), receiptWire = PacketCodec.Encode(receipt);
            Assert.Equal(25, requestWire.Length); Assert.Equal(value, requestWire[23]); Assert.Equal(0, requestWire[24]);
            Assert.Equal(22, receiptWire.Length); Assert.Equal(value, receiptWire[20]); Assert.Equal(0, receiptWire[21]);
            Assert.Equal(operation, Assert.IsType<PartFitRequest>(PacketCodec.Decode(requestWire)).Operation);
            Assert.Equal(operation, Assert.IsType<PartFitReceipt>(PacketCodec.Decode(receiptWire)).Operation);
            foreach (byte[] wire in new[] { requestWire, receiptWire })
            {
                Assert.Equal(wire, PacketCodec.Encode(PacketCodec.Decode(wire)));
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire.Take(wire.Length - 1).ToArray()));
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire.Concat(new byte[] { 0 }).ToArray()));
                foreach (byte slot in new byte[] { 1, 32, 255 })
                {
                    var invalid = (byte[])wire.Clone(); invalid[invalid.Length - 1] = slot;
                    Assert.Throws<ProtocolException>(() => PacketCodec.Decode(invalid));
                    Assert.False(new PartFitClient(request.Token).TryBegin(1, request.ItemId, 7, operation, slot));
                }
                foreach (byte unknown in new byte[] { 8, 255 })
                {
                    var invalid = (byte[])wire.Clone(); invalid[invalid.Length - 2] = unknown;
                    Assert.Throws<ProtocolException>(() => PacketCodec.Decode(invalid));
                }
            }
            request.SlotIndex = 1; receipt.SlotIndex = 1;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(request));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(receipt));
        }

        [Theory]
        [InlineData(PartFitOperation.HandTighten, 1)]
        [InlineData(PartFitOperation.HandLoosen, -1)]
        public void EveryNativeStepIsExactAndLimitsNeverWrap(PartFitOperation operation, int direction)
        {
            for (int current = 0; current <= 8; current++)
            {
                bool canTurn = current + direction >= 0 && current + direction <= 8;
                Assert.True(PartHandScrewPolicy.ValidTightness(current));
                Assert.Equal(canTurn, PartHandScrewPolicy.TryTurn(current, operation, out float result));
                Assert.Equal(canTurn ? current + direction : current, result);
                Assert.Equal(canTurn ? PartFitStatus.Pending : PartFitStatus.Blocked, Check(Request(operation), State(current)));
            }
        }

        [Fact]
        public void InvalidOrFractionalNativeValuesCannotBeRoundedIntoATurn()
        {
            foreach (float value in new[] { -1f, 9, -.1f, .1f, 4.5f, 7.999f,
                float.NaN, float.NegativeInfinity, float.PositiveInfinity })
            {
                Assert.False(PartHandScrewPolicy.ValidTightness(value));
                foreach (var operation in new[] { PartFitOperation.HandTighten, PartFitOperation.HandLoosen })
                {
                    Assert.False(PartHandScrewPolicy.TryTurn(value, operation, out float result));
                    Assert.Equal(value, result);
                    Assert.Equal(PartFitStatus.Unavailable, Check(Request(operation), State(value)));
                }
            }
        }

        [Theory]
        [InlineData(false, true, true, false, PartFitStatus.Unavailable)]
        [InlineData(true, false, true, false, PartFitStatus.TooFar)]
        [InlineData(true, true, false, false, PartFitStatus.Blocked)]
        [InlineData(true, true, true, true, PartFitStatus.Busy)]
        [InlineData(true, true, true, false, PartFitStatus.Pending)]
        public void HostReadinessAndProximityGateTheNativeTurn(bool available, bool near, bool mount, bool busy, PartFitStatus expected)
            => Assert.Equal(expected, PartHandScrewPolicy.Check(Request(), State(), 1, available, near, mount, busy));

        [Fact]
        public void WrongIdentityRevisionAttachmentAndScalarAreRejected()
        {
            var request = Request(); var state = State();
            state.NativeId = "OILFILTR02";
            Assert.Equal(PartFitStatus.Unavailable, Check(request, state));
            state = State(); state.Revision++;
            Assert.Equal(PartFitStatus.Stale, Check(request, state));
            state = State(); state.Installed = false; state.AssemblyId = 0; state.ParentKind = PartParentKind.None;
            Assert.Equal(PartFitStatus.NotFitted, Check(request, state));
            Assert.Equal(PartFitStatus.Unavailable, Check(request, null));
            Assert.Equal(PartFitStatus.Unavailable, PartHandScrewPolicy.Check(request, State(), -1, true, true, true, false));
            Assert.Equal(PartFitStatus.Unavailable, PartHandScrewPolicy.Check(request, State(), 2, true, true, true, false));
            state = State(); state.Scalars = null!;
            Assert.Equal(PartFitStatus.Unavailable, Check(request, state));
            request.SlotIndex = 1;
            Assert.Equal(PartFitStatus.Unavailable, Check(request, State()));
        }

        [Fact]
        public void HandScrewsCannotUseAlternatorOrInstallationAdmission()
        {
            foreach (var operation in new[] { PartFitOperation.Install, PartFitOperation.Remove,
                PartFitOperation.RotateIncrease, PartFitOperation.RotateDecrease, (PartFitOperation)6 })
            {
                Assert.False(PartHandScrewPolicy.IsTurn(operation));
                Assert.False(PartHandScrewPolicy.TryTurn(4, operation, out _));
                Assert.Equal(PartFitStatus.Unavailable, Check(Request(operation), State()));
            }
            foreach (var operation in new[] { PartFitOperation.HandTighten, PartFitOperation.HandLoosen })
            {
                Assert.False(PartAdjustmentPolicy.IsRotation(operation));
                Assert.False(PartAdjustmentPolicy.TryRotation(4, operation, out _));
                Assert.Equal(PartFitStatus.Unavailable, PartAdjustmentPolicy.Check(Request(operation), State(), 1, true, true, true, true, false));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void LostReceiptOrPartialNativeFailureCannotRepeatATurn(bool success)
        {
            var request = Request(); var client = new PartFitClient(request.Token); var ledger = new PartFitLedger();
            Assert.True(client.TryBegin(1, request.ItemId, 7, request.Operation));
            int nativeTurns = 0;
            for (int retry = 0; retry < 20; retry++)
            {
                var pending = client.Poll(retry * .5f)!;
                var receipt = ledger.Inspect(pending, 1, out bool begin);
                if (begin)
                {
                    ledger.Begin(pending, 1, PartFitStatus.Pending); nativeTurns++;
                    receipt = ledger.Complete(pending, success);
                }
                Assert.Equal(success ? PartFitStatus.Accepted : PartFitStatus.Failed, receipt!.Status);
                if (retry == 19) Assert.True(client.Receive(receipt));
            }
            Assert.Equal(1, nativeTurns); Assert.False(client.Pending);
        }

        [Fact]
        public void DirectionAndObservedRevisionCannotChangeDuringRetry()
        {
            var request = Request(); var client = new PartFitClient(request.Token); var ledger = new PartFitLedger();
            Assert.True(client.TryBegin(1, request.ItemId, request.ExpectedRevision, request.Operation));
            request = client.Poll(0)!;
            ledger.Begin(request, 1, PartFitStatus.Pending);
            var changed = PartFitLedger.Copy(request); changed.Operation = PartFitOperation.HandLoosen;
            Assert.Null(ledger.Inspect(changed, 1, out bool begin)); Assert.False(begin);
            Assert.Null(ledger.Complete(changed, true));
            Assert.False(client.Receive(PartFitLedger.Receipt(changed, PartFitStatus.Accepted)));
            changed = PartFitLedger.Copy(request); changed.ExpectedRevision++;
            Assert.Null(ledger.Inspect(changed, 1, out begin)); Assert.False(begin);
            Assert.False(client.TryBegin(1, request.ItemId, 7, PartFitOperation.HandLoosen));
            Assert.True(client.Receive(ledger.Complete(request, true)!));
        }

        [Fact]
        public void AcceptedTurnMakesACompetingClickStaleWithoutChangingDirt()
        {
            var state = State(); var publication = new ReplacementPartPublication();
            state.Revision = publication.Observe(state);
            var first = Request(); first.ExpectedRevision = state.Revision;
            var competing = Request(PartFitOperation.HandLoosen, 2); competing.ExpectedRevision = state.Revision;
            var ledger = new PartFitLedger();
            Assert.Equal(PartFitStatus.Pending, ledger.Begin(first, 1, Check(first, state))!.Status);
            Assert.True(PartHandScrewPolicy.TryTurn(state.Scalars[1], first.Operation, out state.Scalars[1]));
            state.Revision = publication.Observe(state);
            Assert.Equal(PartFitStatus.Accepted, ledger.Complete(first, true)!.Status);
            Assert.Equal(PartFitStatus.Stale, ledger.Begin(competing, 2, Check(competing, state))!.Status);
            Assert.Equal(5, state.Scalars[1]); Assert.Equal(25, state.Scalars[0]);
            Assert.Equal(PartFitStatus.Accepted, ledger.Inspect(first, 1, out bool begin)!.Status); Assert.False(begin);
        }

        [Fact]
        public void DeferredOlderParentObservationCannotUndoTheAcceptedHandTightness()
        {
            var request = Request(); var state = State(7); var receipts = new PartTightnessReceipts();
            Assert.Equal(7, receipts.Resolve(request.ItemId, 1, state.Scalars[1]));
            Assert.True(PartHandScrewPolicy.TryTurn(state.Scalars[1], request.Operation, out state.Scalars[1]));
            Assert.Equal(8, receipts.Resolve(request.ItemId, 3, state.Scalars[1]));
            Assert.Equal(8, receipts.Resolve(request.ItemId, 2, 7));
            Assert.Equal(8, receipts.Latest(request.ItemId, 7));
        }
    }
}
