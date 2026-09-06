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
    public sealed class PartIdentityTests
    {
        private static uint ItemId(string nativeId)
        {
            Assert.True(PartIdentity.TryItemId(nativeId, out uint id));
            return id;
        }
        private static uint FsmId(string nativeId, string path, string name)
        {
            Assert.True(PartIdentity.TryFsmId(ItemId(nativeId), path, name, out uint id));
            return id;
        }

        [Theory]
        [InlineData("VIN1330")]
        [InlineData("VIN1331")]
        [InlineData("VIN1332147483647")]
        [InlineData("ALTERNATOR00")]
        [InlineData("ALTERNATOR01")]
        [InlineData("VIN114B0")]
        [InlineData("GEARBOX5SPD1")]
        [InlineData("CAMTUNEa01")]
        [InlineData("RIM14STEELb012")]
        public void NativeSaveIdsIncludeOriginalZeroCounterAndAlphanumericPrefixes(string nativeId)
        {
            Assert.True(PartIdentity.TryPersistentId(nativeId, nativeId + "AID", nativeId + "POS", "AID", "POS", out uint id));
            Assert.Equal(ItemId(nativeId), id);
            Assert.NotEqual(FsmId(nativeId, "", "Data"), id);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1")]
        [InlineData("133VIN0")]
        [InlineData("VIN133")]
        [InlineData("Alternator(VINXX)")]
        [InlineData("VIN1331(Clone)")]
        [InlineData("VIN133/1")]
        [InlineData("VIN133:1")]
        [InlineData("VIN1331\n")]
        [InlineData("VＩN1331")]
        [InlineData("VIN133１")]
        public void TransientNamesAndUnsafeIdsNeverAcquireAPersistentIdentity(string? nativeId)
        {
            // A prefab's bare VIN133 name has the same shape as a saved ID;
            // its uninitialized/mismatched save tags must still prevent binding.
            if (nativeId == "VIN133")
                Assert.False(PartIdentity.TryPersistentId(nativeId, "", "", "AID", "POS", out _));
            else Assert.False(PartIdentity.TryItemId(nativeId!, out _));
        }

        [Fact]
        public void SaveProofsMustBothBelongToTheExactInstance()
        {
            Assert.False(PartIdentity.TryPersistentId("VIN1331", "VIN1332AID", "VIN1331POS", "AID", "POS", out _));
            Assert.False(PartIdentity.TryPersistentId("VIN1331", "VIN1331AID", "VIN1332POS", "AID", "POS", out _));
            Assert.False(PartIdentity.TryPersistentId("VIN1331", "VIN1331AID", "VIN1331AID", "AID", "AID", out _));
            Assert.False(PartIdentity.TryPersistentId("VIN1331", "VIN1331", "VIN1331POS", "", "POS", out _));
            Assert.False(PartIdentity.TryItemId("VIN" + new string('1', 126), out _));
            Assert.True(PartIdentity.TryPersistentId("VIN1331", "VIN1331ASSEMBLY", "VIN1331POSITION", "ASSEMBLY", "POSITION", out _));
        }

        [Fact]
        public void AllThirtyPackageContentsHaveIndependentBodiesDataAndBoltGroups()
        {
            string[] prefixes = { "VIN133", "ALTERNATOR0", "VIN109", "VIN110", "VIN115", "VIN116", "CAMTUNEa0",
                "CAMTUNEb0", "CAMTUNEc0", "CAMTUNEd0", "CARB2BRLa0", "CARB4BRLa0", "VIN121", "VIN105", "VIN102",
                "VIN131", "VIN125", "FUELPUMP0", "VIN134", "VIN104", "VIN132", "VIN103", "REVLIMITER0", "VIN117",
                "VIN130", "VIN129", "VIN128", "VIN107", "VIN126", "VIN127" };
            var ids = new HashSet<uint>();
            foreach (string prefix in prefixes)
                for (int counter = 0; counter < 30; counter++)
                {
                    string nativeId = prefix + counter;
                    Assert.True(ids.Add(ItemId(nativeId)));
                    Assert.True(ids.Add(FsmId(nativeId, "", "Data")));
                    Assert.True(ids.Add(FsmId(nativeId, "Bolts/BoltPM[0]", "Screw")));
                    Assert.True(ids.Add(FsmId(nativeId, "Bolts/BoltPM[1]", "Screw")));
                    Assert.True(ids.Add(FsmId(nativeId, "Pivot/alternator/BoltPM", "Screw")));
                }
            Assert.Equal(4500, ids.Count);
        }

        [Fact]
        public void ShuffledPeersRouteMotionConditionAndBoltPacketsToTheSameReplacement()
        {
            string[] nativeIds = { "VIN1330", "VIN1331", "VIN1332", "ALTERNATOR01", "ALTERNATOR02" };
            var guestBodies = nativeIds.AsEnumerable().Reverse().ToDictionary(ItemId);
            var guestData = nativeIds.AsEnumerable().Reverse().ToDictionary(id => FsmId(id, "", "Data"));
            var guestBolts = nativeIds.AsEnumerable().Reverse().ToDictionary(id => FsmId(id, "Bolts/BoltPM", "Screw"));
            for (int i = 0; i < nativeIds.Length; i++)
            {
                string nativeId = nativeIds[i];
                var movement = new WorldItemSnapshot();
                movement.Entries.Add(new WorldItemSnapshot.Entry { ItemId = ItemId(nativeId),
                    Position = new NetVector3(i * 10, 2, 4), Rotation = new NetQuaternion(0, 0, 0, 1) });
                var pose = Assert.IsType<WorldItemSnapshot>(PacketCodec.Decode(PacketCodec.Encode(movement))).Entries[0];
                Assert.Equal(nativeId, guestBodies[pose.ItemId]);
                var condition = PartStatePolicy.Capture(FsmId(nativeId, "", "Data"), i % 2 == 0, i, 95.5f - i)!;
                var part = Assert.IsType<PartState>(PacketCodec.Decode(PacketCodec.Encode(condition)));
                Assert.Equal(nativeId, guestData[part.NetId]); Assert.Equal(95.5f - i, part.WearValue);
                var bolt = Assert.IsType<BoltState>(PacketCodec.Decode(PacketCodec.Encode(new BoltState
                    { NetId = FsmId(nativeId, "Bolts/BoltPM", "Screw"), BoltTightness = (ushort)(i + 1), ScrewInt = (ushort)(i + 1) })));
                Assert.Equal(nativeId, guestBolts[bolt.NetId]); Assert.Equal(i + 1, bolt.BoltTightness);
            }
        }

        [Theory]
        [InlineData("/Bolts", "Screw")]
        [InlineData("Bolts/", "Screw")]
        [InlineData("Bolts//BoltPM", "Screw")]
        [InlineData("../Bolts", "Screw")]
        [InlineData("Bolts/./BoltPM", "Screw")]
        [InlineData("Bolts\n", "Screw")]
        [InlineData("Bolts", "Screw:Data")]
        [InlineData("Bolts", "Screw\t")]
        [InlineData("Bolts", "")]
        [InlineData(null, "Screw")]
        public void AmbiguousChildIdentitiesAreRejected(string? path, string fsm)
        {
            Assert.False(PartIdentity.TryFsmId(ItemId("VIN1331"), path!, fsm, out _));
        }

        [Fact]
        public void NativeIdentityBindingsAreRequiredAndCannotUseTheSameSaveProofTwice()
        {
            string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json"));
            var config = SyncCatalogJson.Parse(text).PartIdentity!;
            Assert.Equal("Data", config["fsm"]); Assert.Equal("UTAssemblyID", config["assemblyKeyVariable"]);
            foreach (string key in PartIdentityData.RequiredBindings)
            {
                var json = JsonNode.Parse(text)!; json["partIdentity"]!.AsObject().Remove(key);
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            }
            foreach (var keys in new[] { new[] { "assemblyKeyVariable", "positionKeyVariable" }, new[] { "assemblyKeySuffix", "positionKeySuffix" } })
            {
                var json = JsonNode.Parse(text)!; json["partIdentity"]![keys[0]] = json["partIdentity"]![keys[1]]!.GetValue<string>();
                Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
            }
        }
    }
}
