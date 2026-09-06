using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class PartRemovalTests
    {
        private static ReplacementPartState Fitted() => new ReplacementPartState { NativeId = "VIN1331", FactoryId = 71,
            Revision = 10, Installed = true, AssemblyId = 1, Scalars = new[] { 99f, 0f }, Rotation = NetQuaternion.Identity,
            ParentKind = PartParentKind.Vehicle, ParentId = 5, ParentPath = "Mount", RemovalAllowed = true };
        private static PartFitRequest Request(byte actor = 1)
        {
            Assert.True(PartIdentity.TryItemId(Fitted().NativeId, out uint id));
            return new PartFitRequest { PlayerId = actor, Token = 123, Sequence = 1, ItemId = id,
                ExpectedRevision = 10, Operation = PartFitOperation.Remove };
        }
        private static ReplacementPartReplica Replica() => new ReplacementPartReplica(
            new[] { new ReplacementPartRule(71, "VIN133", 2, 1) }, new ItemSpawnLifecycle());

        [Fact]
        public void RemovalOperationAndAllowanceAreAppendedWithStrictFraming()
        {
            var request = Request(); var wire = PacketCodec.Encode(request);
            Assert.Equal(25, wire.Length); Assert.Equal(1, wire[23]); Assert.Equal(0, wire[24]);
            Assert.Equal(PartFitOperation.Remove, Assert.IsType<PartFitRequest>(PacketCodec.Decode(wire)).Operation);
            var receipt = PartFitLedger.Receipt(request, PartFitStatus.Bolted); wire = PacketCodec.Encode(receipt);
            Assert.Equal(22, wire.Length); Assert.Equal(10, wire[19]); Assert.Equal(1, wire[20]); Assert.Equal(0, wire[21]);
            Assert.Equal(PartFitOperation.Remove, Assert.IsType<PartFitReceipt>(PacketCodec.Decode(wire)).Operation);
            wire[19] = 11; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire));
            foreach (var message in new IMessage[] { request, receipt })
            {
                wire = PacketCodec.Encode(message); wire[wire.Length - 2] = 4;
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire));
            }
            request.Operation = (PartFitOperation)4; receipt.Operation = (PartFitOperation)4;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(request));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(receipt));
            var state = Fitted(); wire = PacketCodec.Encode(state);
            Assert.Equal(1, wire[wire.Length - 1]);
            Assert.True(Assert.IsType<ReplacementPartState>(PacketCodec.Decode(wire)).RemovalAllowed);
            state.RemovalAllowed = false;
            var blocked = PacketCodec.Encode(state);
            Assert.Equal(wire.Take(wire.Length - 1), blocked.Take(blocked.Length - 1));
            Assert.Equal(0, blocked[blocked.Length - 1]);
            wire[wire.Length - 1] = 2; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(wire));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RetriesCannotRemoveTwiceOrReplayAPartialNativeFailure(bool success)
        {
            var ledger = new PartFitLedger(); var request = Request(); int removals = 0;
            for (int i = 0; i < 50; i++)
            {
                var result = ledger.Inspect(request, 1, out bool begin);
                if (begin) { ledger.Begin(request, 1, PartFitStatus.Pending); removals++; }
                else Assert.Equal(i < 25 ? PartFitStatus.Pending : success ? PartFitStatus.Accepted : PartFitStatus.Failed, result!.Status);
                if (i == 24) ledger.Complete(request, success);
            }
            Assert.Equal(1, removals);
        }

        [Fact]
        public void OperationIsPartOfRequestIdentityAndReceiptMatching()
        {
            var ledger = new PartFitLedger(); var removal = Request(); ledger.Begin(removal, 1, PartFitStatus.Pending);
            var install = PartFitLedger.Copy(removal); install.Operation = PartFitOperation.Install;
            Assert.Null(ledger.Inspect(install, 1, out bool begin)); Assert.False(begin);
            Assert.Null(ledger.Complete(install, true));
            var client = new PartFitClient(removal.Token);
            Assert.True(client.TryBegin(1, removal.ItemId, 10, PartFitOperation.Remove));
            Assert.Equal(PartFitOperation.Remove, client.Operation);
            Assert.Equal(PartFitOperation.Remove, client.Poll(0)!.Operation);
            Assert.False(client.Receive(PartFitLedger.Receipt(install, PartFitStatus.Accepted)));
            Assert.True(client.Receive(ledger.Complete(removal, true)!));
            Assert.False(client.Pending);
            install.Sequence++;
            Assert.Equal(PartFitStatus.Pending, ledger.Begin(install, 1, PartFitStatus.Pending)!.Status);
        }

        [Fact]
        public void RemovalAndInstallationCannotUseEachOthersAdmissionRules()
        {
            var removal = Request(); var install = PartFitLedger.Copy(removal); install.Operation = PartFitOperation.Install;
            Assert.Equal(PartFitStatus.Unavailable, PartRemovalPolicy.Check(install, Fitted(), 1, true, true, true));
            Assert.Equal(PartFitStatus.Unavailable, PartFitLedger.Check(removal, Fitted(), true, true, true, true, true, false));
            foreach (var operation in new[] { PartFitOperation.RotateIncrease, PartFitOperation.RotateDecrease })
            {
                removal.Operation = operation;
                Assert.Equal(PartFitStatus.Unavailable, PartRemovalPolicy.Check(removal, Fitted(), 1, true, true, true));
                Assert.Equal(PartFitStatus.Unavailable, PartFitLedger.Check(removal, Fitted(), true, true, true, true, true, false));
            }
            removal.Operation = (PartFitOperation)4;
            Assert.Null(new PartFitLedger().Begin(removal, 1, PartFitStatus.Pending));
            Assert.False(new PartFitClient(1).TryBegin(1, 5, 10, (PartFitOperation)4));
        }

        [Fact]
        public void ConcurrentRemovalAndAnOldClickAfterRefittingCannotAlterTheNewInstallation()
        {
            var ledger = new PartFitLedger(); var first = Request(); var second = Request(2);
            ledger.Begin(first, 1, PartFitStatus.Pending); ledger.Begin(second, 2, PartFitStatus.Busy);
            ledger.Complete(first, true);
            Assert.Equal(PartFitStatus.Busy, ledger.Inspect(second, 2, out bool begin)!.Status); Assert.False(begin);
            var refitted = Fitted(); refitted.Revision += 2;
            second.Sequence++;
            Assert.Equal(PartFitStatus.Stale, PartRemovalPolicy.Check(second, refitted, 1, true, true, true));
            Assert.Equal(PartFitStatus.Accepted, ledger.Inspect(first, 1, out begin)!.Status); Assert.False(begin);
        }

        [Theory]
        [InlineData(0f, true)]
        [InlineData(.5f, true)]
        [InlineData(.999f, true)]
        [InlineData(1f, false)]
        [InlineData(24f, false)]
        [InlineData(-.1f, false)]
        [InlineData(float.NaN, false)]
        [InlineData(float.PositiveInfinity, false)]
        [InlineData(float.NegativeInfinity, false)]
        public void TightnessUsesTheNativeLessThanOneBoundary(float tightness, bool allowed)
        {
            Assert.Equal(allowed, PartRemovalPolicy.Unbolted(tightness));
            var state = Fitted(); state.Scalars[1] = tightness;
            Assert.Equal(allowed ? PartFitStatus.Pending : PartFitStatus.Bolted,
                PartRemovalPolicy.Check(Request(), state, 1, true, true, true));
        }

        [Theory]
        [InlineData("missing", PartFitStatus.Unavailable)]
        [InlineData("unavailable", PartFitStatus.Unavailable)]
        [InlineData("unrelated", PartFitStatus.Unavailable)]
        [InlineData("stale", PartFitStatus.Stale)]
        [InlineData("loose", PartFitStatus.NotFitted)]
        [InlineData("far", PartFitStatus.TooFar)]
        [InlineData("blocked", PartFitStatus.Blocked)]
        [InlineData("busy", PartFitStatus.Busy)]
        public void HostStateAndReadinessGateRemoval(string change, PartFitStatus expected)
        {
            var state = Fitted();
            if (change == "unrelated") state.NativeId = "VIN1332";
            if (change == "stale") state.Revision++;
            if (change == "loose") { state.Installed = false; state.AssemblyId = 0; }
            if (change == "blocked") state.RemovalAllowed = false;
            Assert.Equal(expected, PartRemovalPolicy.Check(Request(), change == "missing" ? null : state, 1,
                change != "unavailable", change != "far", change != "busy"));
        }

        [Fact]
        public void AvailabilityChangesAdvanceRevisionAndEqualRevisionCannotToggleIt()
        {
            var state = Fitted(); var publication = new ReplacementPartPublication(); var replica = Replica();
            state.Revision = publication.Observe(state); publication.MarkBroadcast(state.Revision);
            Assert.True(replica.Receive(state, out uint id));
            state.RemovalAllowed = false;
            Assert.False(replica.Receive(state, out _));
            state.Revision = publication.Observe(state);
            Assert.True(publication.NeedsBroadcast); Assert.True(replica.Receive(state, out _));
            Assert.False(replica.Get(id)!.RemovalAllowed);
            Assert.False(replica.AllowsLooseMotion(id));
        }

        [Theory]
        [InlineData("loose")]
        [InlineData("unresolved")]
        [InlineData("bolted")]
        public void ContradictoryRemovalAllowanceCannotEnterTheReplica(string change)
        {
            var state = Fitted();
            if (change == "loose") { state.Installed = false; state.AssemblyId = 0; state.ParentKind = PartParentKind.None; state.ParentId = 0; state.ParentPath = ""; }
            if (change == "unresolved") { state.ParentKind = PartParentKind.None; state.ParentId = 0; state.ParentPath = ""; }
            if (change == "bolted") state.Scalars[1] = 1;
            Assert.False(Replica().Receive(state, out _));
        }

        [Fact]
        public void BoxSelectionHonorsWorldRangeOcclusionAndTransformedDirectionScale()
        {
            var center = new NetVector3(); var size = new NetVector3(1, 1, 1);
            Assert.True(PartRemovalPolicy.RayBox(new NetVector3(0, 0, 1.5f), new NetVector3(0, 0, -1), center, size, 1, out float distance));
            Assert.Equal(1, distance);
            Assert.False(PartRemovalPolicy.RayBox(new NetVector3(0, 0, 1.5f), new NetVector3(0, 0, -1), center, size, .9f, out _));
            // A rotated/scaled part maps the world ray onto a non-unit local X axis.
            Assert.True(PartRemovalPolicy.RayBox(new NetVector3(-1, 0, 0), new NetVector3(.5f, 0, 0), center, size, 1, out distance));
            Assert.Equal(1, distance);
            Assert.False(PartRemovalPolicy.RayBox(new NetVector3(2, 0, 1), new NetVector3(0, 0, -1), center, size, 1, out _));
            Assert.False(PartRemovalPolicy.RayBox(center, new NetVector3(0, 0, 1), center, size, 1, out _));
            Assert.False(PartRemovalPolicy.RayBox(new NetVector3(0, 0, 1), new NetVector3(0, 0, 1), center, size, 1, out _));
        }

        [Fact]
        public void InvalidSelectionGeometryCannotProduceAPrompt()
        {
            var center = new NetVector3(); var size = new NetVector3(1, 1, 1); var direction = new NetVector3(0, 0, -1);
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Assert.False(PartRemovalPolicy.RayBox(new NetVector3(value, 0, 1), direction, center, size, 1, out _));
                Assert.False(PartRemovalPolicy.RayBox(new NetVector3(0, 0, 1), direction, center, size, value, out _));
                Assert.False(PartRemovalPolicy.RayBox(new NetVector3(0, 0, 1), new NetVector3(0, value, 1), center, size, 1, out _));
            }
            Assert.False(PartRemovalPolicy.RayBox(new NetVector3(0, 0, 1), center, center, size, 1, out _));
            Assert.False(PartRemovalPolicy.RayBox(new NetVector3(0, 0, 1), direction, center, new NetVector3(-1, 1, 1), 1, out _));
        }

        [Fact]
        public void AllRemovalCatalogBindingsAreRequired()
        {
            var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json"));
            foreach (string key in ReplacementPartsData.RequiredBindings.Where(k => k.StartsWith("remove", StringComparison.Ordinal)))
            {
                var missing = JsonNode.Parse(text)!; missing["replacementParts"]!.AsObject().Remove(key);
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(missing.ToJsonString()));
            }
        }
    }
}
