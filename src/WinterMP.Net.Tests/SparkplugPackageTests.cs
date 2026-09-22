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
    public class SparkplugPackageTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
        private static JsonNode Box(JsonNode json) => json["partsPackages"]!["factories"]!.AsArray().Single(f => f!["fsm"]!.GetValue<string>() == "Sparkplugs")!;
        private static JsonNode Plug(JsonNode json) => json["replacementParts"]!["factories"]!.AsArray().Single(f => f!["prefix"]!.GetValue<string>() == "SPRKPLUG0")!;

        [Fact]
        public void BoxAndIndividualFactoriesShareAnObjectButHaveDistinctIdentitiesAndSpawnPoints()
        {
            var catalog = SyncCatalogJson.Parse(Catalog().ToJsonString()); var box = Assert.Single(catalog.PartsPackages!.Factories, f => f.Fsm == "Sparkplugs");
            var plug = Assert.Single(catalog.ReplacementParts!.Factories, f => f.Prefix == "SPRKPLUG0");
            Assert.Equal("Spawner/CreateItems", box.Path); Assert.Equal(box.Path, box.ContentsPath); Assert.Equal(box.Path, plug.Path);
            Assert.Equal("sparkplugbox0", box.Prefix); Assert.Equal("Sparkplug", box.ContentsFsm); Assert.Equal(box.ContentsFsm, plug.Fsm);
            Assert.True(box.FixedCapacity); Assert.Equal(4, box.Capacity); Assert.Equal("spark plug box(Clone)", box.ItemName);
            Assert.Equal("SpawnPoint", box.ContentsSpawnPointVariable); Assert.Equal(box.ContentsSpawnPointVariable, plug.SpawnPointVariable);
            Assert.False(plug.BagOutput); Assert.Equal(new[] { "Wear", "Tightness", "Durability" }, plug.Scalars);
            Assert.Equal("Sparkplugs", plug.SlotReference); Assert.Equal(4, plug.SlotCount);
            Assert.Contains(plug.References, r => r.Source == "VINP" && r.Target == "InstallPoint");
            Assert.Equal(new[] { "BuildStringFast", "SetName", "SetScale", "IntCompare" }, plug.StatusActions);
            Assert.NotEqual(FactoryItemIdentity.FactoryId(box.Path, box.Fsm), plug.Identity.FactoryId);
        }

        [Theory]
        [InlineData("factory path")] [InlineData("contents FSM")] [InlineData("spawn point")] [InlineData("fixed type")]
        [InlineData("missing fixed capacity")] [InlineData("missing direct spawn")]
        public void InvalidDirectBoxProfileCannotFallBackToStandardBoxBindings(string fault)
        {
            var json = Catalog(); var box = Box(json);
            switch (fault)
            {
                case "factory path": box["path"] = "Spawner/../CreateItems"; break;
                case "contents FSM": box["contentsFsm"] = "Sparkplug/Other"; break;
                case "spawn point": box["contentsSpawnPointVariable"] = " "; break;
                case "fixed type": box["fixedCapacity"] = "true"; break;
                case "missing fixed capacity": box.AsObject().Remove("fixedCapacity"); break;
                case "missing direct spawn": box.AsObject().Remove("contentsSpawnPointVariable"); break;
            }
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Theory]
        [InlineData("bag mode")] [InlineData("invalid spawn")]
        public void IndividualPlugCannotAccidentallyUseTheShoppingBagSpawnPath(string fault)
        {
            var json = Catalog(); var plug = Plug(json);
            if (fault == "bag mode") plug["bagOutput"] = true; else plug["spawnPointVariable"] = "Spawn/Point";
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Fact]
        public void OpenReplayAndLateSnapshotsKeepOneBoxAndFourUniqueParts()
        {
            var catalog = SyncCatalogJson.Parse(Catalog().ToJsonString()); var boxes = catalog.PartsPackages!;
            var box = boxes.Factories.Single(f => f.Fsm == "Sparkplugs"); var plug = catalog.ReplacementParts!.Factories.Single(f => f.Prefix == "SPRKPLUG0");
            uint factoryId = FactoryItemIdentity.FactoryId(box.Path, box.Fsm);
            var state = new PackageState { FactoryId = factoryId, NativeId = box.Prefix + "7", Revision = 1, Quantity = 4, Rotation = NetQuaternion.Identity };
            var replica = new PackageReplica(boxes.Identities, new ItemSpawnLifecycle()); Assert.True(replica.Receive(state, out uint id));
            var parts = new ReplacementPartReplica(catalog.ReplacementParts.IdentityRules(), new ItemSpawnLifecycle());
            var ledger = new PackageOpenLedger(); var ids = new System.Collections.Generic.HashSet<uint>();
            for (uint sequence = 1; sequence <= 4; sequence++)
            {
                var request = new PackageOpenRequest { PlayerId = 1, Token = 7, Sequence = sequence, ItemId = id, ExpectedRevision = state.Revision };
                Assert.Equal(PackageOpenStatus.Pending, PackageOpenLedger.Check(request, state, true, true, true)); ledger.Begin(request, 1, PackageOpenStatus.Pending);
                var part = new ReplacementPartState { FactoryId = plug.Identity.FactoryId, NativeId = "SPRKPLUG0" + sequence,
                    Revision = 1, Scalars = new[] { 99f, 0f, .75f }, Rotation = NetQuaternion.Identity };
                var decoded = Assert.IsType<ReplacementPartState>(PacketCodec.Decode(PacketCodec.Encode(part)));
                Assert.True(parts.Receive(decoded, out uint partId)); Assert.True(ids.Add(partId));
                ledger.Complete(request, true, partId); state.Revision++; state.Quantity--; Assert.True(replica.Receive(state, out _));
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    var receipt = ledger.Inspect(request, 1, out bool begin); Assert.False(begin);
                    Assert.Equal(PackageOpenStatus.Accepted, receipt!.Status); Assert.Equal(partId, receipt.ProducedItemId);
                    Assert.True(parts.Receive(decoded, out uint replayId)); Assert.Equal(partId, replayId);
                    Assert.Equal(decoded.Scalars, parts.Get(partId)!.Scalars);
                }
            }
            var late = new PackageReplica(boxes.Identities, new ItemSpawnLifecycle()); Assert.True(late.Receive(state, out uint lateId));
            Assert.Equal(id, lateId); Assert.Equal(0, late.Get(id)!.Quantity); Assert.Equal(4, ids.Count);
            var next = new PackageOpenRequest { PlayerId = 1, Token = 7, Sequence = 5, ItemId = id, ExpectedRevision = state.Revision };
            Assert.Equal(PackageOpenStatus.Empty, PackageOpenLedger.Check(next, state, true, true, true));
        }
    }
}
