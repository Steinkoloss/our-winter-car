using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class MooseMeatTests
    {
        private static MooseMeatState State() => new MooseMeatState { FactoryId = 123, NativeId = "moosemeat01",
            Revision = 4, Condition = 40, Rotation = NetQuaternion.Identity };
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
        public void NativeFoodVariantsRoundTrip(int kind)
        {
            var s = State(); s.Kind = (byte)kind;
            var bytes = PacketCodec.Encode(s); var copy = Assert.IsType<MooseMeatState>(PacketCodec.Decode(bytes));
            Assert.Equal(bytes, PacketCodec.Encode(copy)); Assert.True(MooseMeatPolicy.SameFood(s, copy));
        }
        [Fact]
        public void OldOrConflictingFoodCannotUndoCookingButEqualRevisionCanRefreshCreationPose()
        {
            var old = State(); var next = State(); next.Kind = 2; next.Condition = 100;
            Assert.False(MooseMeatPolicy.CanReceive(old, next));
            next.Revision++; Assert.True(MooseMeatPolicy.CanReceive(old, next));
            Assert.False(MooseMeatPolicy.CanReceive(next, old));
            old.Kind = 2; old.Condition = 100; old.Revision = next.Revision; old.Position.X = 400;
            Assert.True(MooseMeatPolicy.CanReceive(next, old));
            old.NativeId = "moosemeat02"; Assert.False(MooseMeatPolicy.CanReceive(next, old));
            next.Revision = uint.MaxValue; old = State(); old.Revision = 0;
            Assert.True(MooseMeatPolicy.CanReceive(next, old));
            old.Revision = unchecked(next.Revision + 0x80000000u); Assert.False(MooseMeatPolicy.CanReceive(next, old));
        }
        [Theory]
        [InlineData("condition")] [InlineData("nan")] [InlineData("pose")] [InlineData("rotation")]
        [InlineData("kind")] [InlineData("factory")] [InlineData("native")]
        public void InvalidStateNeverReachesGameplay(string fault)
        {
            var s = State();
            switch (fault)
            {
                case "condition": s.Condition = -1; break;
                case "nan": s.Condition = float.NaN; break;
                case "pose": s.Position.X = float.PositiveInfinity; break;
                case "rotation": s.Rotation = new NetQuaternion(); break;
                case "kind": s.Kind = 5; break;
                case "factory": s.FactoryId = 0; break;
                case "native": s.NativeId += "\n"; break;
            }
            Assert.False(MooseMeatPolicy.Valid(s)); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
        }
        [Fact]
        public void OnlyAdmittedHostCanCreateMeatOnTheOrderedChannel()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.MooseMeatState, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.MooseMeatState, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.MooseMeatState, false, true, true, false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.MooseMeatState, false, true, true, true));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.MooseMeatState, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.MooseMeatState, Channel.UnreliableSequenced));
        }
        [Fact]
        public void NativeIdentitiesDeduplicateAcrossRejoinAndRetirement()
        {
            var lifecycle = new ItemSpawnLifecycle(); var s = State();
            uint id = FactoryItemIdentity.ItemId(s.FactoryId, s.NativeId);
            Assert.True(lifecycle.ShouldMaterialize(id, false)); Assert.False(lifecycle.ShouldMaterialize(id, true));
            lifecycle.Retire(id); Assert.False(lifecycle.ShouldMaterialize(id, false));
            Assert.NotEqual(id, FactoryItemIdentity.ItemId(s.FactoryId, "moosemeat02"));
        }
        [Fact]
        public void InvalidNativeBindingsDisableOnlyMeat()
        {
            string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json"));
            var valid = SyncCatalogJson.Parse(json); Assert.NotNull(valid.MooseMeat); Assert.Null(valid.MooseMeatError);
            foreach (string key in MooseMeatData.Required)
            {
                var root = JsonNode.Parse(json)!; root["mooseMeat"]!.AsObject().Remove(key);
                var data = SyncCatalogJson.Parse(root.ToJsonString()); Assert.Null(data.MooseMeat);
                Assert.NotNull(data.MooseMeatError); Assert.NotNull(data.ShoppingBags);
            }
        }
    }
}
