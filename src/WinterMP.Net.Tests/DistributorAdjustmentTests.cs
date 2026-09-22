using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class DistributorAdjustmentTests
    {
        private const PartAdjustmentProfile Profile = PartAdjustmentProfile.Distributor;

        private static PartFitRequest Request(PartFitOperation operation = PartFitOperation.RotateIncrease, byte player = 1)
        {
            PartIdentity.TryItemId("VIN1310", out uint id);
            return new PartFitRequest { PlayerId = player, Token = (ulong)(100 + player), Sequence = 1,
                ItemId = id, ExpectedRevision = 4, Operation = operation };
        }

        private static ReplacementPartState State(float angle = 12.345f) => new ReplacementPartState {
            NativeId = "VIN1310", Revision = 4, Installed = true, AssemblyId = 1,
            ParentKind = PartParentKind.NativePart, ParentId = 3, ParentPath = "Distributor",
            Scalars = new[] { 55f, 7f, angle }, Rotation = NetQuaternion.Identity };

        private static PartFitStatus Check(PartFitRequest request, ReplacementPartState? state)
            => PartAdjustmentPolicy.Check(Profile, request, state, 2, true, true, true, true, false);

        [Theory]
        [InlineData(0, PartFitOperation.RotateIncrease, .2f, true)]
        [InlineData(1.234f, PartFitOperation.RotateIncrease, 1.434f, true)]
        [InlineData(12.345f, PartFitOperation.RotateDecrease, 12.145f, true)]
        [InlineData(19.95f, PartFitOperation.RotateIncrease, 20, true)]
        [InlineData(20, PartFitOperation.RotateIncrease, 20, false)]
        [InlineData(20, PartFitOperation.RotateDecrease, 19.8f, true)]
        [InlineData(.05f, PartFitOperation.RotateDecrease, 0, true)]
        [InlineData(0, PartFitOperation.RotateDecrease, 0, false)]
        public void NativeTimingPreservesFractionalStartingAnglesAndClampsLimits(float current,
            PartFitOperation operation, float expected, bool changed)
        {
            Assert.True(PartAdjustmentPolicy.ValidRotation(Profile, current));
            Assert.Equal(changed, PartAdjustmentPolicy.TryRotation(Profile, current, operation, out float result));
            Assert.Equal(expected, result, 5);
            Assert.Equal(changed ? PartFitStatus.Pending : PartFitStatus.Blocked, Check(Request(operation), State(current)));
        }

        [Fact]
        public void HostSelectedProfilesKeepAlternatorStepsAndLimitsSeparate()
        {
            Assert.True(PartAdjustmentPolicy.TryRotation(3, PartFitOperation.RotateIncrease, out float legacy));
            Assert.True(PartAdjustmentPolicy.TryRotation(PartAdjustmentProfile.Alternator, 3, PartFitOperation.RotateIncrease, out float alternator));
            Assert.True(PartAdjustmentPolicy.TryRotation(Profile, 3, PartFitOperation.RotateIncrease, out float distributor));
            Assert.Equal(3.5f, legacy); Assert.Equal(legacy, alternator); Assert.Equal(3.2f, distributor);
            Assert.False(PartAdjustmentPolicy.ValidRotation(PartAdjustmentProfile.Alternator, 12));
            Assert.True(PartAdjustmentPolicy.ValidRotation(Profile, 12));
            Assert.Equal(PartFitStatus.Pending, Check(Request(), State()));
            Assert.Equal(PartFitStatus.Blocked, PartAdjustmentPolicy.Check(Request(), State(), 2, true, true, true, true, false));
            foreach (var unknown in new[] { (PartAdjustmentProfile)(-1), (PartAdjustmentProfile)2, (PartAdjustmentProfile)255 })
            {
                Assert.False(PartAdjustmentPolicy.ValidRotation(unknown, 3));
                Assert.False(PartAdjustmentPolicy.TryRotation(unknown, 3, PartFitOperation.RotateIncrease, out float result));
                Assert.Equal(3, result);
                Assert.Equal(PartFitStatus.Unavailable, PartAdjustmentPolicy.Check(unknown, Request(), State(), 2, true, true, true, true, false));
            }
        }

        [Fact]
        public void InvalidAnglesAndUnrelatedOperationsCannotTurn()
        {
            foreach (float invalid in new[] { -.001f, 20.001f, float.NaN, float.NegativeInfinity, float.PositiveInfinity })
            {
                Assert.False(PartAdjustmentPolicy.ValidRotation(Profile, invalid));
                Assert.False(PartAdjustmentPolicy.TryRotation(Profile, invalid, PartFitOperation.RotateIncrease, out float result));
                Assert.Equal(invalid, result);
                Assert.Equal(PartFitStatus.Blocked, Check(Request(), State(invalid)));
            }
            foreach (var operation in new[] { PartFitOperation.Install, PartFitOperation.Remove,
                PartFitOperation.HandTighten, PartFitOperation.HandLoosen, (PartFitOperation)6 })
            {
                Assert.False(PartAdjustmentPolicy.TryRotation(Profile, 5, operation, out _));
                Assert.Equal(PartFitStatus.Unavailable, Check(Request(operation), State()));
            }
        }

        [Fact]
        public void SavedParentTightnessControlsLoosenessWithoutRounding()
        {
            foreach (float loose in new[] { 0f, .5f, 7, 7.999f })
                Assert.True(PartAdjustmentPolicy.IsDistributorLoose(loose));
            foreach (float tight in new[] { -.001f, 8, 9, float.NaN, float.NegativeInfinity, float.PositiveInfinity })
            {
                Assert.False(PartAdjustmentPolicy.IsDistributorLoose(tight));
                Assert.Equal(PartFitStatus.Bolted, PartAdjustmentPolicy.Check(Profile, Request(), State(), 2,
                    true, true, true, PartAdjustmentPolicy.IsDistributorLoose(tight), false));
            }
        }

        [Theory]
        [InlineData(false, true, true, true, false, PartFitStatus.Unavailable)]
        [InlineData(true, false, true, true, false, PartFitStatus.TooFar)]
        [InlineData(true, true, false, true, false, PartFitStatus.Blocked)]
        [InlineData(true, true, true, false, false, PartFitStatus.Bolted)]
        [InlineData(true, true, true, true, true, PartFitStatus.Busy)]
        [InlineData(true, true, true, true, false, PartFitStatus.Pending)]
        public void HostChecksEveryAdmissionGate(bool available, bool near, bool mount, bool loose, bool busy, PartFitStatus expected)
            => Assert.Equal(expected, PartAdjustmentPolicy.Check(Profile, Request(), State(), 2, available, near, mount, loose, busy));

        [Fact]
        public void WrongIdentityRevisionAttachmentOrScalarCannotReachNativeExecution()
        {
            var request = Request(); var state = State();
            state.NativeId = "VIN1311";
            Assert.Equal(PartFitStatus.Unavailable, Check(request, state));
            state = State(); state.Revision++;
            Assert.Equal(PartFitStatus.Stale, Check(request, state));
            state = State(); state.Installed = false; state.AssemblyId = 0; state.ParentKind = PartParentKind.None;
            Assert.Equal(PartFitStatus.NotFitted, Check(request, state));
            Assert.Equal(PartFitStatus.Unavailable, Check(request, null));
            Assert.Equal(PartFitStatus.Unavailable, Check(null!, State()));
            state = State(); state.Scalars = null!;
            Assert.Equal(PartFitStatus.Unavailable, Check(request, state));
            state.Scalars = new float[2];
            Assert.Equal(PartFitStatus.Unavailable, Check(request, state));
            request.SlotIndex = 1;
            Assert.Equal(PartFitStatus.Unavailable, Check(request, State()));
        }

        [Theory]
        [InlineData(PartFitOperation.RotateIncrease, 2)]
        [InlineData(PartFitOperation.RotateDecrease, 3)]
        public void DistributorUsesExistingDirectionAndReceiptFraming(PartFitOperation operation, byte value)
        {
            var request = Request(operation);
            byte[] wire = PacketCodec.Encode(request);
            Assert.Equal(25, wire.Length); Assert.Equal(value, wire[23]); Assert.Equal(0, wire[24]);
            var decoded = Assert.IsType<PartFitRequest>(PacketCodec.Decode(wire));
            Assert.Equal(request.ItemId, decoded.ItemId); Assert.Equal(operation, decoded.Operation);
            Assert.Equal(wire, PacketCodec.Encode(decoded));
            var receipt = PartFitLedger.Receipt(request, PartFitStatus.Accepted);
            wire = PacketCodec.Encode(receipt);
            Assert.Equal(22, wire.Length); Assert.Equal(value, wire[20]); Assert.Equal(0, wire[21]);
            Assert.Equal(operation, Assert.IsType<PartFitReceipt>(PacketCodec.Decode(wire)).Operation);
            request.SlotIndex = 1; receipt.SlotIndex = 1;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(request));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(receipt));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AcceptedOrPartiallyFailedNativeTurnsCannotRepeatAfterLostReceipts(bool success)
        {
            var request = Request(); var client = new PartFitClient(request.Token); var ledger = new PartFitLedger();
            var state = State(); int turns = 0;
            Assert.True(client.TryBegin(1, request.ItemId, request.ExpectedRevision, request.Operation));
            for (int retry = 0; retry < 5; retry++)
            {
                request = client.Poll(retry * .5f)!;
                var receipt = ledger.Inspect(request, 1, out bool begin);
                if (begin)
                {
                    Assert.Equal(PartFitStatus.Pending, ledger.Begin(request, 1, Check(request, state))!.Status);
                    Assert.True(PartAdjustmentPolicy.TryRotation(Profile, state.Scalars[2], request.Operation, out state.Scalars[2]));
                    turns++; state.Revision++;
                    receipt = ledger.Complete(request, success);
                }
                var changed = PartFitLedger.Copy(request); changed.Operation = PartFitOperation.RotateDecrease;
                Assert.Null(ledger.Inspect(changed, 1, out bool changedBegin)); Assert.False(changedBegin);
                Assert.False(client.Receive(PartFitLedger.Receipt(changed, PartFitStatus.Accepted)));
                changed = PartFitLedger.Copy(request); changed.ExpectedRevision++;
                Assert.Null(ledger.Inspect(changed, 1, out changedBegin)); Assert.False(changedBegin);
                Assert.Equal(success ? PartFitStatus.Accepted : PartFitStatus.Failed, receipt!.Status);
                if (retry == 4) Assert.True(client.Receive(receipt));
            }
            Assert.Equal(1, turns); Assert.Equal(12.545f, state.Scalars[2], 5); Assert.False(client.Pending);
        }

        [Fact]
        public void TimingChangeInvalidatesCompetingClicksAndLeavesOtherSavedValuesAlone()
        {
            var state = State(); var publication = new ReplacementPartPublication();
            state.Revision = publication.Observe(state);
            var first = Request(); first.ExpectedRevision = state.Revision;
            var competing = Request(PartFitOperation.RotateDecrease, 2); competing.ExpectedRevision = state.Revision;
            var ledger = new PartFitLedger();
            Assert.Equal(PartFitStatus.Pending, ledger.Begin(first, 1, Check(first, state))!.Status);
            Assert.Equal(PartFitStatus.Busy, PartAdjustmentPolicy.Check(Profile, competing, state, 2, true, true, true, true, true));
            Assert.True(PartAdjustmentPolicy.TryRotation(Profile, state.Scalars[2], first.Operation, out state.Scalars[2]));
            state.Revision = publication.Observe(state);
            Assert.Equal(PartFitStatus.Accepted, ledger.Complete(first, true)!.Status);
            Assert.Equal(PartFitStatus.Stale, ledger.Begin(competing, 2, Check(competing, state))!.Status);
            Assert.Equal(55, state.Scalars[0]); Assert.Equal(7, state.Scalars[1]); Assert.Equal(12.545f, state.Scalars[2], 5);
            Assert.Equal(PartFitStatus.Accepted, ledger.Inspect(first, 1, out bool begin)!.Status); Assert.False(begin);
        }
    }
}
