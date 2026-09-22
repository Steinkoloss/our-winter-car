using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class CylinderHeadInteractionTests
    {
        private static CylinderHeadState State(bool fitted = false) => new CylinderHeadState {
            NetId = 42, Revision = 7, ParentId = fitted ? 43u : 0, Mass = fitted ? 0 : 12 };
        private static PartFitRequest Request(PartFitOperation operation = PartFitOperation.Install) => new PartFitRequest {
            PlayerId = 1, ItemId = 42, ExpectedRevision = 7, Operation = operation, Token = 9, Sequence = 1 };
        [Theory]
        [InlineData(true, true, true, true, true, PartFitStatus.Pending)]
        [InlineData(false, true, true, true, true, PartFitStatus.Unavailable)]
        [InlineData(true, false, true, true, true, PartFitStatus.TooFar)]
        [InlineData(true, true, false, true, true, PartFitStatus.TooFar)]
        [InlineData(true, true, true, false, true, PartFitStatus.Busy)]
        [InlineData(true, true, true, true, false, PartFitStatus.Busy)]
        public void FittingRequiresAvailableNearbyOwnedLooseHead(bool ready, bool near, bool range, bool owns, bool idle, PartFitStatus expected)
        { Assert.Equal(expected, CylinderHeadPolicy.CheckRequest(Request(), State(), ready, near, range, owns, idle, 0)); }
        [Theory]
        [InlineData(0f, PartFitStatus.Pending)] [InlineData(.99f, PartFitStatus.Pending)]
        [InlineData(1f, PartFitStatus.Bolted)] [InlineData(72f, PartFitStatus.Bolted)]
        [InlineData(-1f, PartFitStatus.Bolted)] [InlineData(float.NaN, PartFitStatus.Bolted)]
        [InlineData(float.PositiveInfinity, PartFitStatus.Bolted)]
        public void RemovalUsesHostTightnessAndDoesNotRequireLooseBodyOwnership(float tightness, PartFitStatus expected)
        { Assert.Equal(expected, CylinderHeadPolicy.CheckRequest(Request(PartFitOperation.Remove), State(true), true, true, false, false, true, tightness)); }
        [Fact]
        public void StaleRevisionWrongIdentityAndUnsupportedSlotOrOperationCannotMutateHead()
        {
            var r = Request(); r.ExpectedRevision = 6; Assert.Equal(PartFitStatus.Stale, Check(r));
            r = Request(); r.ItemId = 50; Assert.Equal(PartFitStatus.Unavailable, Check(r));
            r = Request(); r.SlotIndex = 1; Assert.Equal(PartFitStatus.Unavailable, Check(r));
            r = Request(); r.Operation = (PartFitOperation)2; Assert.Equal(PartFitStatus.Unavailable, Check(r));
            Assert.Equal(PartFitStatus.NotLoose, CylinderHeadPolicy.CheckRequest(Request(), State(true), true, true, true, true, true, 0));
            Assert.Equal(PartFitStatus.NotFitted, Check(Request(PartFitOperation.Remove)));
        }
        [Fact]
        public void SharedPartLedgerReplaysReceiptWithoutBeginningAnotherHeadOperation()
        {
            var ledger = new PartFitLedger(); var request = Request();
            Assert.Equal(PartFitStatus.Pending, ledger.Begin(request, 1, Check(request))!.Status);
            Assert.Equal(PartFitStatus.Accepted, ledger.Complete(request, true)!.Status);
            Assert.Equal(PartFitStatus.Accepted, ledger.Inspect(request, 1, out bool begin)!.Status); Assert.False(begin);
            var altered = Request(PartFitOperation.Remove); Assert.Null(ledger.Inspect(altered, 1, out begin)); Assert.False(begin);
            Assert.Null(ledger.Inspect(request, 2, out begin)); Assert.False(begin);
        }
        private static PartFitStatus Check(PartFitRequest r) => CylinderHeadPolicy.CheckRequest(r, State(), true, true, true, true, true, 0);
    }
}
