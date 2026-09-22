using System;
using System.Linq;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class HouseholdFuseTests
    {
        [Fact]
        public void MissingFuseCatalogListsDisableOnlyHouseholdFuses()
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.HouseholdFusesError);
            Assert.Equal(7, parsed.HouseholdFuses!.Tables[0].Holders.Length);
            Assert.Equal(4, parsed.HouseholdFuses.Tables[1].Holders.Length);
            var original = json.ToJsonString();
            foreach (bool missingTables in new[] { true, false })
            {
                json = JsonNode.Parse(original)!;
                if (missingTables) json["householdFuses"]!.AsObject().Remove("tables");
                else json["householdFuses"]!["tables"]![0]!.AsObject().Remove("holders");
                parsed = SyncCatalogJson.Parse(json.ToJsonString());
                Assert.Null(parsed.HouseholdFuses);
                Assert.NotNull(parsed.HouseholdFusesError);
                Assert.NotNull(parsed.PartsPackages);
            }
        }
        private static HouseholdFuseState State() => new HouseholdFuseState { Revision = 1 };
        private static HouseholdFuseIntent Intent(HouseholdFuseAction action) => new HouseholdFuseIntent
        { PlayerId = 1, Sequence = 1, ControlRevision = 1, Holder = 0, Action = action, ItemId = action == HouseholdFuseAction.InsertFuse ? 12u : 0 };
        private static bool Accept(HouseholdFuseState state, HouseholdFuseIntent intent) => HouseholdFusePolicy.CanAct(state, intent, 1, true, true, false);
        [Fact]
        public void AllElevenPersistentHoldersAndCircuitBitsRoundTrip()
        {
            var s = State(); s.PowerMask = 2047;
            for (byte i = 0; i < 11; i++) s.Holders[i] = new HouseholdFuseHolder { Slot = i, Fuse = 1, Tightness = 8, Flags = 3,
                ControlRevision = (uint)i + 1, Position = new NetVector3(i, 2, -3) };
            var bytes = PacketCodec.Encode(s); Assert.Equal(404, bytes.Length);
            var copy = Assert.IsType<HouseholdFuseState>(PacketCodec.Decode(bytes));
            Assert.Equal(s.PowerMask, copy.PowerMask);
            for (int i = 0; i < 11; i++) { Assert.Equal(s.Holders[i].ControlRevision, copy.Holders[i].ControlRevision); Assert.Equal(i, copy.Holders[i].Slot); Assert.Equal(i, copy.Holders[i].Position.X); }
            Array.Resize(ref bytes, bytes.Length - 1); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            Assert.Equal(11, Enumerable.Range(0, 11).Select(HouseholdFusePolicy.ItemId).Distinct().Count());
        }
        [Fact]
        public void HolderReplacementRequiresEachPhysicalPrerequisite()
        {
            var s = State(); var h = s.Holders[0]; h.Fuse = 0; h.Flags = 4;
            var insert = Intent(HouseholdFuseAction.InsertFuse); Assert.True(Accept(s, insert));
            h.Fuse = 1; Assert.False(Accept(s, insert));
            var fit = Intent(HouseholdFuseAction.FitHolder); fit.Slot = 1; Assert.True(Accept(s, fit));
            s.Holders[1].Slot = 1; s.Holders[1].Tightness = 8; Assert.False(Accept(s, fit)); s.Holders[1].Slot = 255; s.Holders[1].Tightness = 0;
            h.Slot = 1; h.Tightness = 1;
            Assert.True(Accept(s, Intent(HouseholdFuseAction.RemoveHolder)));
            Assert.True(Accept(s, Intent(HouseholdFuseAction.Tighten)));
            Assert.False(Accept(s, Intent(HouseholdFuseAction.Loosen)));
            h.Tightness = 8;
            Assert.False(Accept(s, Intent(HouseholdFuseAction.RemoveHolder)));
            Assert.False(Accept(s, Intent(HouseholdFuseAction.Tighten)));
            Assert.True(Accept(s, Intent(HouseholdFuseAction.Loosen)));
        }
        [Fact]
        public void FreshNearbyOwnerAndCurrentHolderRevisionAreRequired()
        {
            var s = State(); s.Holders[0].Flags = 4; var i = Intent(HouseholdFuseAction.InsertFuse);
            Assert.False(HouseholdFusePolicy.CanAct(s, i, 2, true, true, false));
            Assert.False(HouseholdFusePolicy.CanAct(s, i, 1, false, true, false));
            Assert.False(HouseholdFusePolicy.CanAct(s, i, 1, true, false, false));
            Assert.False(HouseholdFusePolicy.CanAct(s, i, 1, true, true, true));
            s.Holders[0].ControlRevision++; Assert.False(Accept(s, i));
        }
        [Fact]
        public void InvalidSlotsFlagsCountsAndConditionNeverReachNativePresentation()
        {
            var s = State(); s.Holders[0].Slot = 7; Assert.False(HouseholdFusePolicy.Valid(s));
            s.Holders[0].Slot = 0; s.Holders[1].Slot = 0; Assert.False(HouseholdFusePolicy.Valid(s));
            s = State(); s.Holders[0].Fuse = 3; Assert.False(HouseholdFusePolicy.Valid(s));
            s = State(); s.Holders[0].Flags = 8; Assert.False(HouseholdFusePolicy.Valid(s));
            s = State(); s.Holders[0].Tightness = 1; Assert.False(HouseholdFusePolicy.Valid(s));
            s = State(); s.Holders[0].Slot = 0; Assert.False(HouseholdFusePolicy.Valid(s));
            s = State(); s.Holders[0].ControlRevision = 0; Assert.False(HouseholdFusePolicy.Valid(s));
            s = State(); s.Holders = new HouseholdFuseHolder[10]; Assert.False(HouseholdFusePolicy.Valid(s));
            s = State(); s.PowerMask = 2048; Assert.False(HouseholdFusePolicy.Valid(s));
            s = State(); s.Revision = 0; Assert.False(HouseholdFusePolicy.Valid(s));
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)] [InlineData(100001f)]
        public void MalformedWorldPoseRejectedOnWriteAndRead(float value)
        {
            var s = State(); s.Holders[0].Position = new NetVector3(value, 0, 0);
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            var bytes = PacketCodec.Encode(State()); Array.Copy(BitConverter.GetBytes(value), 0, bytes, 16, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Theory]
        [InlineData(HouseholdFuseAction.InsertFuse)] [InlineData(HouseholdFuseAction.FitHolder)] [InlineData(HouseholdFuseAction.RemoveHolder)]
        [InlineData(HouseholdFuseAction.Tighten)] [InlineData(HouseholdFuseAction.Loosen)]
        public void EachIntentHasOnlyItsRequiredFields(HouseholdFuseAction action)
        {
            var i = Intent(action); if (action == HouseholdFuseAction.FitHolder) i.Slot = 3;
            var copy = Assert.IsType<HouseholdFuseIntent>(PacketCodec.Decode(PacketCodec.Encode(i))); Assert.Equal(action, copy.Action); Assert.Equal(i.ItemId, copy.ItemId);
            i.Sequence = 0; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(i)); i.Sequence = 1;
            i.ItemId = i.ItemId == 0 ? 12u : 0; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(i));
        }
        [Fact]
        public void CrossHomeAndUnknownIntentsCannotTargetOtherNativeTables()
        {
            var i = Intent(HouseholdFuseAction.FitHolder); i.Slot = 7; Assert.False(HouseholdFusePolicy.Valid(i));
            i.Slot = 0; i.Holder = 11; Assert.False(HouseholdFusePolicy.Valid(i));
            i.Holder = 0; i.Action = (HouseholdFuseAction)5; Assert.False(HouseholdFusePolicy.Valid(i));
            i.Action = HouseholdFuseAction.RemoveHolder; i.Slot = 255; i.PlayerId = 255; Assert.False(HouseholdFusePolicy.Valid(i));
        }
        [Fact]
        public void AuthorityAndShockReceiptCannotBeForgedByAnotherPeer()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.HouseholdFuseState, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.HouseholdFuseResult, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.HouseholdFuseIntent, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.HouseholdFuseResult, false, true, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.HouseholdFuseIntent, true, true, false, true));
            var result = new HouseholdFuseResult { PlayerId = 1, Holder = 2, Sequence = 3, Accepted = true, Shock = true };
            Assert.True(Assert.IsType<HouseholdFuseResult>(PacketCodec.Decode(PacketCodec.Encode(result))).Shock);
            result.Accepted = false; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(result));
        }
    }
}
