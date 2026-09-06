using System;
using System.IO;
using System.Linq;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class PartAdjustmentTests
    {
        private static PartFitRequest Request(PartFitOperation operation = PartFitOperation.RotateIncrease)
        {
            PartIdentity.TryItemId("VIN1331", out uint id);
            return new PartFitRequest { PlayerId = 1, Token = 99, Sequence = 1, ItemId = id, ExpectedRevision = 4, Operation = operation };
        }
        private static ReplacementPartState State() => new ReplacementPartState { NativeId = "VIN1331", Revision = 4,
            Installed = true, AssemblyId = 1, ParentKind = PartParentKind.NativePart, ParentId = 3, ParentPath = "Mount",
            Scalars = new[] { 50f, 12f, 3f } };

        [Theory]
        [InlineData(PartFitOperation.RotateIncrease)]
        [InlineData(PartFitOperation.RotateDecrease)]
        public void AdjustmentWireAndReceiptPreserveDirection(PartFitOperation operation)
        {
            var request = Request(operation);
            var wire = PacketCodec.Encode(request);
            var copy = Assert.IsType<PartFitRequest>(PacketCodec.Decode(wire));
            Assert.Equal(operation, copy.Operation); Assert.Equal((byte)0, copy.SlotIndex);
            Assert.Equal(wire, PacketCodec.Encode(copy));
            var receipt = PartFitLedger.Receipt(request, PartFitStatus.Accepted);
            Assert.Equal(operation, Assert.IsType<PartFitReceipt>(PacketCodec.Decode(PacketCodec.Encode(receipt))).Operation);
            wire[wire.Length - 1] = 1;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire));
            request.SlotIndex = 1;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(request));
            Assert.False(new PartFitClient(99).TryBegin(1, request.ItemId, 4, operation, 1));
        }

        [Theory]
        [InlineData(0, PartFitOperation.RotateIncrease, .5f, true)]
        [InlineData(6.8f, PartFitOperation.RotateIncrease, 7, true)]
        [InlineData(7, PartFitOperation.RotateIncrease, 7, false)]
        [InlineData(7, PartFitOperation.RotateDecrease, 6.5f, true)]
        [InlineData(.2f, PartFitOperation.RotateDecrease, 0, true)]
        [InlineData(0, PartFitOperation.RotateDecrease, 0, false)]
        [InlineData(-1, PartFitOperation.RotateIncrease, -1, false)]
        [InlineData(8, PartFitOperation.RotateDecrease, 8, false)]
        [InlineData(3, PartFitOperation.Install, 3, false)]
        public void NativeHalfDegreeStepClampsWithoutWrapping(float current, PartFitOperation operation, float expected, bool changed)
        {
            Assert.Equal(changed, PartAdjustmentPolicy.TryRotation(current, operation, out float result));
            Assert.Equal(expected, result);
        }

        [Fact]
        public void InvalidNumbersCannotBecomeAnAdjustment()
        {
            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                Assert.False(PartAdjustmentPolicy.TryRotation(bad, PartFitOperation.RotateIncrease, out _));
        }

        [Theory]
        [InlineData(false, true, true, true, false, PartFitStatus.Unavailable)]
        [InlineData(true, false, true, true, false, PartFitStatus.TooFar)]
        [InlineData(true, true, false, true, false, PartFitStatus.Blocked)]
        [InlineData(true, true, true, false, false, PartFitStatus.Bolted)]
        [InlineData(true, true, true, true, true, PartFitStatus.Busy)]
        [InlineData(true, true, true, true, false, PartFitStatus.Pending)]
        public void HostGatesEveryAdjustment(bool available, bool near, bool mount, bool loose, bool busy, PartFitStatus expected)
            => Assert.Equal(expected, PartAdjustmentPolicy.Check(Request(), State(), 2, available, near, mount, loose, busy));

        [Fact]
        public void StaleRemovedWrongPartAndInvalidScalarRequestsFail()
        {
            var r = Request(); var s = State();
            s.Revision++;
            Assert.Equal(PartFitStatus.Stale, Check(r, s));
            s.Revision = r.ExpectedRevision; s.ParentKind = PartParentKind.None;
            Assert.Equal(PartFitStatus.NotFitted, Check(r, s));
            s = State(); s.NativeId = "VIN1332";
            Assert.Equal(PartFitStatus.Unavailable, Check(r, s));
            s = State(); s.Scalars = new float[1];
            Assert.Equal(PartFitStatus.Unavailable, Check(r, s));
            s = State(); s.Scalars[2] = 7;
            Assert.Equal(PartFitStatus.Blocked, Check(r, s));
            r.SlotIndex = 1;
            Assert.Equal(PartFitStatus.Unavailable, Check(r, s));
        }
        private static PartFitStatus Check(PartFitRequest r, ReplacementPartState s)
            => PartAdjustmentPolicy.Check(r, s, 2, true, true, true, true, false);

        [Fact]
        public void LostReceiptCannotRepeatOrChangeTheRotation()
        {
            var client = new PartFitClient(99); var ledger = new PartFitLedger(); var state = State();
            var request = Request();
            Assert.True(client.TryBegin(1, request.ItemId, 4, request.Operation));
            request = client.Poll(0)!;
            ledger.Begin(request, 1, PartFitStatus.Pending);
            var changed = PartFitLedger.Copy(request); changed.Operation = PartFitOperation.RotateDecrease;
            Assert.Null(ledger.Inspect(changed, 1, out _));
            var competing = Request(); competing.PlayerId = 2; competing.Token = 100;
            PartAdjustmentPolicy.TryRotation(state.Scalars[2], request.Operation, out state.Scalars[2]); state.Revision++;
            ledger.Complete(request, true);
            Assert.Equal(PartFitStatus.Stale, Check(competing, state));
            var retry = client.Poll(.5f)!;
            var receipt = ledger.Inspect(retry, 1, out bool begin)!;
            Assert.False(begin); Assert.Equal(PartFitStatus.Accepted, receipt.Status);
            Assert.True(client.Receive(receipt)); Assert.False(client.Pending);
            Assert.Equal(3.5f, state.Scalars[2]);
            Assert.Null(ledger.Inspect(changed, 1, out _));
            ledger.ForgetPlayer(1); changed.Token = 101;
            Assert.Null(ledger.Inspect(changed, 1, out begin)); Assert.True(begin);
        }

        [Fact]
        public void SpherePickUsesNearestSurfaceAndRejectsInteriorAndBehind()
        {
            var origin = new NetVector3(0, 0, 0); var direction = new NetVector3(0, 0, 1); var center = new NetVector3(0, 0, 1);
            Assert.True(PartAdjustmentPolicy.RaySphere(origin, direction, center, .2f, 1, out float distance));
            Assert.Equal(.8f, distance, 5);
            Assert.False(PartAdjustmentPolicy.RaySphere(origin, direction, center, .2f, .7f, out _));
            Assert.False(PartAdjustmentPolicy.RaySphere(center, direction, center, .2f, 1, out _));
            Assert.False(PartAdjustmentPolicy.RaySphere(origin, direction, new NetVector3(0, 0, -1), .2f, 2, out _));
            Assert.False(PartAdjustmentPolicy.RaySphere(origin, direction, new NetVector3(1, 0, 1), .2f, 2, out _));
            Assert.False(PartAdjustmentPolicy.RaySphere(origin, origin, center, .2f, 1, out _));
            Assert.False(PartAdjustmentPolicy.RaySphere(origin, direction, center, float.NaN, 1, out _));
        }

        [Fact]
        public void CatalogBindsOnlyTheTwoAlternatorsAndRejectsBrokenReferences()
        {
            string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json"));
            var rules = SyncCatalogJson.Parse(text).ReplacementParts!;
            Assert.Equal(new[] { "ALTERNATOR0", "VIN133" }, rules.Factories.Where(f => f.HandRotation != null).Select(f => f.Prefix).OrderBy(x => x));
            Assert.All(rules.Factories.Where(f => f.HandRotation != null), f => {
                Assert.Equal("Pivot", f.HandRotation!.Path); Assert.Equal("HandRotate", f.HandRotation.Fsm);
                Assert.Equal("SettingRotation", f.HandRotation.Scalar); Assert.Contains(f.HandRotation.Scalar, f.Scalars);
            });
            foreach (var mutation in new[] { text.Replace("\"path\": \"Pivot\"", "\"path\": \"../Pivot\""),
                text.Replace("\"scalar\": \"SettingRotation\"", "\"scalar\": \"Missing\"") })
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(mutation));
        }
    }
}
