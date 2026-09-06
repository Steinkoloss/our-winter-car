using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class FactoryItemTests
    {
        private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!.AsObject();

        private static ItemSpawn Manifest(string fsm = "Gold", bool replay = false)
        {
            uint factory = FactoryItemIdentity.FactoryId("Spawner/CreateTrophiesIcerace", fsm);
            string nativeId = "icerace" + fsm.ToLowerInvariant() + "1";
            return new ItemSpawn { ContainerNetId = factory, Epoch = replay ? (ushort)0 : (ushort)1,
                StateName = "State 7", Flags = (byte)(ItemSpawn.FlagFactory | (replay ? ItemSpawn.FlagReplay : 0)),
                Items = new List<ItemSpawn.Entry> { new ItemSpawn.Entry {
                    NetId = FactoryItemIdentity.ItemId(factory, nativeId), TemplateName = nativeId,
                    Position = new NetVector3(10, 2, -50), Rotation = new NetQuaternion(0, 0, 0, 1) } } };
        }

        [Fact]
        public void FactoryManifestRoundTripsItsNativeIdentityAndFlags()
        {
            var original = Manifest(replay: true);
            var bytes = PacketCodec.Encode(original);
            var decoded = Assert.IsType<ItemSpawn>(PacketCodec.Decode(bytes));
            Assert.True(decoded.IsFactory); Assert.True(decoded.IsReplay);
            Assert.Equal(original.Items[0].NetId, decoded.Items[0].NetId);
            Assert.Equal("iceracegold1", decoded.Items[0].TemplateName);
            Assert.True(new ItemSpawnLifecycle().AcceptManifest(decoded));
            Assert.Equal(bytes, PacketCodec.Encode(decoded));
        }

        [Theory]
        [InlineData("trophygold1", "trophygold", true)]
        [InlineData("trophygold2147483647", "trophygold", true)]
        [InlineData("trophygold2147483648", "trophygold", false)]
        [InlineData("trophygoldjr1", "trophygold", false)]
        [InlineData("trophygoldjr1", "trophygoldjr", true)]
        [InlineData("trophygold0", "trophygold", false)]
        [InlineData("trophygold01", "trophygold", false)]
        [InlineData("trophygold-1", "trophygold", false)]
        [InlineData("trophygold+1", "trophygold", false)]
        [InlineData("trophygold1\n", "trophygold", false)]
        [InlineData("trophygold１", "trophygold", false)]
        [InlineData("trophygold", "trophygold", false)]
        [InlineData("Trophygold1", "trophygold", false)]
        [InlineData("trophygold1", "", false)]
        public void NativeIdentityRequiresTheExactPrefixAndCanonicalPositiveCounter(string id, string prefix, bool valid)
            => Assert.Equal(valid, FactoryItemIdentity.IsNativeId(id, prefix));

        [Fact]
        public void SameDisplayNamesNeverMergeDifferentRaceAwardsOrCounterValues()
        {
            var rules = SyncCatalogJson.Parse(Catalog().ToJsonString()).TrophyFactories!.Factories;
            Assert.Equal(15, rules.Count);
            Assert.Equal(3, rules.Select(r => r.ItemName).Distinct().Count());
            var ids = new HashSet<uint>();
            foreach (var rule in rules)
                for (int counter = 1; counter <= 100; counter++)
                {
                    string nativeId = rule.Prefix + counter;
                    Assert.True(FactoryItemIdentity.IsNativeId(nativeId, rule.Prefix));
                    Assert.True(ids.Add(FactoryItemIdentity.ItemId(FactoryItemIdentity.FactoryId(rule.Path, rule.Fsm), nativeId)));
                }
            Assert.NotEqual(FactoryItemIdentity.ItemId(1, "trophygold1"), FactoryItemIdentity.ItemId(2, "trophygold1"));
        }

        [Theory]
        [InlineData("id")]
        [InlineData("factory")]
        [InlineData("native")]
        [InlineData("offer")]
        [InlineData("empty")]
        public void ForgedFactoryMetadataCannotConsumeAValidReceipt(string fault)
        {
            var bad = Manifest(); var entry = bad.Items[0];
            switch (fault)
            {
                case "id": entry.NetId++; break;
                case "factory": bad.ContainerNetId++; break;
                case "native": entry.TemplateName = "iceracegold2"; break;
                case "offer": bad.OfferSequence = 1; break;
                case "empty": bad.Items.Clear(); break;
            }
            if (bad.Items.Count > 0) bad.Items[0] = entry;
            var lifecycle = new ItemSpawnLifecycle();
            Assert.False(lifecycle.AcceptManifest(bad));
            Assert.True(lifecycle.AcceptManifest(Manifest()));
        }

        [Fact]
        public void RepeatedJoinChunksRecoverMissingAwardsWithoutRevivingRetiredOnes()
        {
            var lifecycle = new ItemSpawnLifecycle(); var local = new HashSet<uint>();
            var gold = Manifest(replay: true); var silver = Manifest("Silver", true);
            var gold2 = Manifest(replay: true); var entry = gold2.Items[0];
            entry.TemplateName = "iceracegold2"; entry.NetId = FactoryItemIdentity.ItemId(gold2.ContainerNetId, entry.TemplateName);
            gold2.Items[0] = entry;
            // Replays can share epoch zero, including multiple chunks from one factory.
            void Deliver(ItemSpawn message)
            {
                Assert.True(lifecycle.AcceptManifest(message));
                foreach (var item in message.Items)
                    if (lifecycle.ShouldMaterialize(item.NetId, local.Contains(item.NetId))) Assert.True(local.Add(item.NetId));
            }
            Deliver(gold); Deliver(silver); Deliver(gold2); Assert.Equal(3, local.Count);
            lifecycle.Retire(gold.Items[0].NetId); local.Remove(gold.Items[0].NetId);
            local.Remove(silver.Items[0].NetId);
            for (int retry = 0; retry < 20; retry++) { Deliver(gold); Deliver(gold2); Deliver(silver); }
            Assert.Equal(2, local.Count); Assert.DoesNotContain(gold.Items[0].NetId, local);
            lifecycle.Clear(); local.Clear(); Deliver(gold); Deliver(gold2); Deliver(silver); Assert.Equal(3, local.Count);
        }

        [Fact]
        public void FactoryEpochReuseCannotDropANewNativeItem()
        {
            var lifecycle = new ItemSpawnLifecycle(); var first = Manifest(); var next = Manifest();
            var item = next.Items[0]; item.TemplateName = "iceracegold65537";
            item.NetId = FactoryItemIdentity.ItemId(next.ContainerNetId, item.TemplateName); next.Items[0] = item;
            Assert.True(lifecycle.AcceptManifest(first)); Assert.True(lifecycle.AcceptManifest(next));
            Assert.True(lifecycle.AcceptManifest(first));
            Assert.False(lifecycle.ShouldMaterialize(first.Items[0].NetId, true));
            Assert.True(lifecycle.ShouldMaterialize(next.Items[0].NetId, false));
        }

        [Fact]
        public void CatalogRequiresCompleteUniqueNativeFactoryBindings()
        {
            var data = SyncCatalogJson.Parse(Catalog().ToJsonString()).TrophyFactories!;
            Assert.Equal(5, data.Factories.Select(f => f.Path).Distinct().Count());
            foreach (string key in TrophyFactoriesData.RequiredBindings)
            {
                var broken = Catalog(); broken["trophyFactories"]!.AsObject().Remove(key);
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(broken.ToJsonString()));
            }
            foreach (string field in new[] { "path", "fsm", "prefix", "prefabName", "itemName" })
            {
                var broken = Catalog(); broken["trophyFactories"]!["factories"]![0]!.AsObject().Remove(field);
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(broken.ToJsonString()));
            }
            foreach (string field in new[] { "prefix", "prefabName" })
            {
                var broken = Catalog(); var rules = broken["trophyFactories"]!["factories"]!.AsArray();
                rules[1]![field] = rules[0]![field]!.GetValue<string>();
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(broken.ToJsonString()));
            }
            foreach (string prefix in new[] { "trophy1", "../trophy", "trophy\n", new string('a', 101) })
            {
                var broken = Catalog(); broken["trophyFactories"]!["factories"]![0]!["prefix"] = prefix;
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(broken.ToJsonString()));
            }
        }
    }
}
