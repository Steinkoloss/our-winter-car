using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class PackageIdentityTests
    {
        private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!.AsObject();
        private static PartsPackagesData Packages() => SyncCatalogJson.Parse(Catalog().ToJsonString()).PartsPackages!;

        [Fact]
        public void EveryNativePackageTypeHasAnUnambiguousContentsBinding()
        {
            var packages = Packages();
            Assert.Equal(30, packages.Factories.Count);
            Assert.DoesNotContain(packages.Factories, r => r.Fsm == "Plugwires" || r.Fsm == "BrakeBiasRegulator");
            var ids = new HashSet<uint>();
            foreach (var rule in packages.Factories)
                for (int counter = 1; counter <= 200; counter++)
                {
                    string nativeId = rule.Prefix + counter.ToString(CultureInfo.InvariantCulture);
                    Assert.True(packages.Identities.TryResolve(nativeId, rule.ContentsPath, out uint id));
                    Assert.True(ids.Add(id));
                    Assert.Equal(FactoryItemIdentity.ItemId(FactoryItemIdentity.FactoryId(packages["factoryPath"], rule.Fsm), nativeId), id);
                    foreach (var wrong in packages.Factories.Where(other => other != rule))
                        Assert.False(packages.Identities.TryResolve(nativeId, wrong.ContentsPath, out _));
                }
        }

        [Theory]
        [InlineData("boxalternator01", true)]
        [InlineData("boxalternator010", true)]
        [InlineData("boxalternator02147483647", true)]
        [InlineData("boxalternator02147483648", false)]
        [InlineData("boxalternator00", false)]
        [InlineData("boxalternator001", false)]
        [InlineData("boxalternator0", false)]
        [InlineData("boxalternator0-1", false)]
        [InlineData("boxalternator0+1", false)]
        [InlineData("boxalternator01\n", false)]
        [InlineData("boxalternator0１", false)]
        [InlineData("package(Clone)", false)]
        [InlineData("empty(itemx)", false)]
        [InlineData("Boxalternator01", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void AZeroInThePrefabPrefixIsNotPartOfThePositiveNativeCounter(string? nativeId, bool valid)
        {
            var packages = Packages();
            var rule = packages.Factories.Single(r => r.Fsm == "Alternator");
            Assert.Equal(valid, packages.Identities.TryResolve(nativeId!, rule.ContentsPath, out _));
        }

        [Fact]
        public void TimingBeltsWithNoTrailingZeroUseTheirActualNativePrefix()
        {
            var packages = Packages(); var rule = packages.Factories.Single(r => r.Fsm == "TimingBelt");
            Assert.Equal("boxtimingbelt", rule.Prefix);
            Assert.True(packages.Identities.TryResolve("boxtimingbelt1", rule.ContentsPath, out _));
            Assert.False(packages.Identities.TryResolve("boxtimingbelt01", rule.ContentsPath, out _));
        }

        [Fact]
        public void IndependentlyOrderedAndPositionedPeersRouteSnapshotsToTheSamePackage()
        {
            var packages = Packages(); var random = new Random(108);
            var entries = packages.Factories.SelectMany(rule => Enumerable.Range(1, 4).Select(counter =>
                new { NativeId = rule.Prefix + counter, rule.ContentsPath })).ToArray();
            var host = new Dictionary<string, uint>(); var guest = new Dictionary<uint, string>();
            foreach (var entry in entries.OrderBy(_ => random.Next()))
            {
                Assert.True(packages.Identities.TryResolve(entry.NativeId, entry.ContentsPath, out uint id));
                host.Add(entry.NativeId, id);
            }
            foreach (var entry in entries.OrderBy(_ => random.Next()))
            {
                Assert.True(packages.Identities.TryResolve(entry.NativeId, entry.ContentsPath, out uint id));
                guest.Add(id, entry.NativeId);
            }
            var positions = new Dictionary<string, float>();
            foreach (var pair in host)
            {
                var message = new WorldItemSnapshot();
                message.Entries.Add(new WorldItemSnapshot.Entry { ItemId = pair.Value,
                    Position = new NetVector3(random.Next(-1000, 1000), 2, 8), Rotation = new NetQuaternion(0, 0, 0, 1) });
                var decoded = Assert.IsType<WorldItemSnapshot>(PacketCodec.Decode(PacketCodec.Encode(message))).Entries[0];
                Assert.Equal(pair.Key, guest[decoded.ItemId]);
                positions[guest[decoded.ItemId]] = decoded.Position.X;
            }
            Assert.Equal(120, positions.Count);
        }

        [Fact]
        public void OneRetiredBoxDoesNotRetireAnotherBoxOrPackageType()
        {
            var packages = Packages(); var rule = packages.Factories.First();
            Assert.True(packages.Identities.TryResolve(rule.Prefix + "1", rule.ContentsPath, out uint first));
            Assert.True(packages.Identities.TryResolve(rule.Prefix + "2", rule.ContentsPath, out uint second));
            var other = packages.Factories.Last();
            Assert.True(packages.Identities.TryResolve(other.Prefix + "1", other.ContentsPath, out uint different));
            var lifecycle = new ItemSpawnLifecycle(); lifecycle.Retire(first);
            Assert.False(lifecycle.ShouldMaterialize(first, false));
            Assert.True(lifecycle.ShouldMaterialize(second, false)); Assert.True(lifecycle.ShouldMaterialize(different, false));
            lifecycle.Clear(); Assert.True(lifecycle.ShouldMaterialize(first, false));
        }

        [Theory]
        [InlineData("missing-binding")]
        [InlineData("empty")]
        [InlineData("duplicate-factory")]
        [InlineData("duplicate-prefix")]
        [InlineData("overlapping-prefix")]
        [InlineData("invalid-prefix")]
        [InlineData("bad-path")]
        [InlineData("bad-fsm")]
        [InlineData("long-prefix")]
        [InlineData("invalid-resume")]
        public void BrokenCatalogsCannotSilentlyAliasTwoBoxes(string fault)
        {
            var catalog = Catalog(); var packages = catalog["partsPackages"]!.AsObject();
            var factories = packages["factories"]!.AsArray(); var first = factories[0]!.AsObject(); var second = factories[1]!.AsObject();
            switch (fault)
            {
                case "missing-binding": packages.Remove("contentsVariable"); break;
                case "empty": factories.Clear(); break;
                case "duplicate-factory": second["fsm"] = first["fsm"]!.GetValue<string>(); break;
                case "duplicate-prefix": second["prefix"] = first["prefix"]!.GetValue<string>(); break;
                case "overlapping-prefix": second["prefix"] = first["prefix"]!.GetValue<string>() + "1"; break;
                case "invalid-prefix": first["prefix"] = "package(Clone)"; break;
                case "bad-path": first["contentsPath"] = "CARPARTS/../Other"; break;
                case "bad-fsm": first["fsm"] = "Spawn/Other"; break;
                case "long-prefix": first["prefix"] = new string('x', 101); break;
                case "invalid-resume": packages["itemIdleState"] = "State 1"; break;
            }
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(catalog.ToJsonString()));
        }
    }
}
