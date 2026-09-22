using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class ReplacementPartTests
    {
        private static string CatalogText() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json"));
        private static ReplacementPartsData Catalog() => SyncCatalogJson.Parse(CatalogText()).ReplacementParts!;
        private static ReplacementPartState State(string prefix = "VIN133", uint revision = 1)
        {
            var rule = Catalog().Factories.Single(f => f.Prefix == prefix);
            return new ReplacementPartState { FactoryId = rule.Identity.FactoryId, NativeId = prefix + "1", Revision = revision,
                CamProfile = rule.CamProfileVariable == null ? "" : "55003500",
                Scalars = rule.Scalars.Select(s => s == "Tightness" ? 0f : s == "Wear" ? 98.75f : 7.125f).ToArray(),
                Position = new NetVector3(12, 3, -4), Rotation = new NetQuaternion(0, 0, 0, 1) };
        }
        private static ReplacementPartReplica Replica(ItemSpawnLifecycle? life = null) => new ReplacementPartReplica(Catalog().IdentityRules(), life ?? new ItemSpawnLifecycle());

        [Fact]
        public void LayoutPreservesNativeConditionAndBoundsVariablePayloadBeforeAllocation()
        {
            var state = State(); state.AssemblyId = 12345; state.Installed = true;
            byte[] bytes = PacketCodec.Encode(state);
            using var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
            Assert.Equal(185, r.ReadUInt16()); Assert.Equal(1u, r.ReadUInt32()); Assert.Equal(state.FactoryId, r.ReadUInt32());
            Assert.Equal(state.NativeId, Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16())));
            Assert.Equal(12345, r.ReadInt32());
            int flagOffset = (int)r.BaseStream.Position;
            Assert.Equal(1, r.ReadByte()); Assert.Equal(state.Scalars.Length, r.ReadByte());
            foreach (float value in state.Scalars) Assert.Equal(value, r.ReadSingle());
            Assert.Equal(12, r.ReadSingle()); Assert.Equal(3, r.ReadSingle()); Assert.Equal(-4, r.ReadSingle());
            Assert.Equal(0, r.ReadSingle()); Assert.Equal(0, r.ReadSingle()); Assert.Equal(0, r.ReadSingle()); Assert.Equal(1, r.ReadSingle());
            Assert.Equal(0, r.ReadByte()); Assert.Equal(0u, r.ReadUInt32()); Assert.Equal(0, r.ReadUInt16());
            foreach (float value in new[] { 0f, 0, 0, 0, 0, 0, 1, 1, 1, 1 }) Assert.Equal(value, r.ReadSingle());
            Assert.False(r.ReadBoolean());
            Assert.Equal(0u, r.ReadUInt32());
            Assert.Equal(0, r.ReadByte());
            Assert.Equal(0, r.ReadUInt16());
            Assert.Equal(0, r.ReadByte());
            Assert.Equal(bytes.Length, r.BaseStream.Position);
            Assert.Equal(bytes, PacketCodec.Encode(Assert.IsType<ReplacementPartState>(PacketCodec.Decode(bytes))));
            var invalid = (byte[])bytes.Clone(); invalid[flagOffset] = 2;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(invalid));
            invalid = (byte[])bytes.Clone(); invalid[flagOffset + 1] = 255;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(invalid));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(bytes.Length - 1).ToArray()));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
            state.Scalars = new float[9]; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
        }

        [Fact]
        public void EveryBoxMapsToItsExactPartFactoryAndStableBodyIdentity()
        {
            var catalog = SyncCatalogJson.Parse(CatalogText()); var rules = catalog.ReplacementParts!;
            var boxed = rules.Factories.Where(f => catalog.PartsPackages!.Factories.Any(p => p.ContentsPath == f.Path && p.ContentsFsm == f.Fsm)).ToArray();
            Assert.Equal(31, boxed.Length);
            Assert.Equal(catalog.PartsPackages!.Factories.Where(f => f.SupplyContents == null && f.BulbContents == null).Select(f => f.ContentsPath).OrderBy(p => p), boxed.Select(f => f.Path).OrderBy(p => p));
            foreach (var rule in boxed)
            {
                var box = Assert.Single(catalog.PartsPackages.Factories, f => f.ContentsPath == rule.Path && f.ContentsFsm == rule.Fsm);
                Assert.Equal(box.ContentsFsm, rule.Fsm);
                Assert.Equal(FactoryItemIdentity.FactoryId(rule.Path, rule.Fsm), rule.Identity.FactoryId);
            }
        }

        [Fact]
        public void AllReplacementFactoriesUseTheNativePartIdentityForEveryCounter()
        {
            var rules = Catalog();
            Assert.Equal(39, rules.Factories.Count);
            var replica = Replica(); var ids = new System.Collections.Generic.HashSet<uint>();
            foreach (var rule in rules.Factories)
            {
                var state = State(rule.Prefix);
                Assert.True(rule.Identity.CanCreate(state));
                Assert.Equal(FactoryItemIdentity.FactoryId(rule.Path, rule.Fsm), state.FactoryId);
                for (int count = 0; count < 200; count++)
                {
                    state.NativeId = rule.Prefix + count;
                    Assert.True(replica.Receive(state, out uint id)); Assert.True(ids.Add(id));
                    Assert.True(PartIdentity.TryItemId(state.NativeId, out uint expected)); Assert.Equal(expected, id);
                    Assert.Equal(state.Scalars, replica.Get(id)!.Scalars);
                }
            }
            Assert.Contains("SettingRotation", rules.Factories.Single(f => f.Prefix == "VIN133").Scalars);
            Assert.Contains("SparkAngle", rules.Factories.Single(f => f.Prefix == "VIN131").Scalars);
            Assert.Contains("SettingMixture", rules.Factories.Single(f => f.Prefix == "CARB2BRLa0").Scalars);
            Assert.Contains("SettingRPM", rules.Factories.Single(f => f.Prefix == "REVLIMITER0").Scalars);
        }

        [Theory]
        [InlineData("Fanbelt", "FANBELT0", "Wear", "GetChild", 9)]
        [InlineData("Oilfilter", "OILFILTR0", "Dirt", "GetOwner", 8)]
        public void BagPartFactoriesShareAnObjectButKeepDistinctNativeIdentities(string fsm, string prefix,
            string condition, string firstInitAction, int initCount)
        {
            var rules = Catalog();
            var bags = rules.Factories.Where(f => f.BagOutput).ToArray();
            Assert.Equal(2, bags.Length);
            var rule = bags.Single(f => f.Fsm == fsm);
            Assert.Equal("Spawner/CreateItems", rule.Path); Assert.Equal(prefix, rule.Prefix);
            Assert.Equal(new[] { condition, "Tightness" }, rule.Scalars);
            Assert.Equal(firstInitAction, rule.InitActions[0]); Assert.Equal(initCount, rule.InitActions.Length);
            Assert.Equal(new[] { "BuildStringFast", "SetName", "SetScale", "SetIsKinematic", "IntCompare" }, rule.StatusActions);
            var reference = Assert.Single(rule.References);
            Assert.Equal("InstallPoint", reference.Target); Assert.Equal("VINP", reference.Source);
            Assert.Equal(0, rule.SlotCount); Assert.Empty(rule.SlotReference);
            Assert.NotEqual(FactoryItemIdentity.FactoryId(rule.Path, rules["factoryFsm"]), rule.Identity.FactoryId);
            Assert.Equal(2, bags.Select(f => f.Identity.FactoryId).Distinct().Count());
            var state = State(prefix);
            Assert.Equal(prefix + "1", state.NativeId);
            Assert.True(Replica().Receive(state, out uint id));
            Assert.True(PartIdentity.TryItemId(state.NativeId, out uint expected)); Assert.Equal(expected, id);
            state.FactoryId = bags.Single(f => f.Fsm != fsm).Identity.FactoryId;
            Assert.False(Replica().Receive(state, out _));
        }

        [Fact]
        public void ExplicitDefaultFactoryOptionsPreserveExistingBoxIdentity()
        {
            var json = JsonNode.Parse(CatalogText())!;
            var rule = json["replacementParts"]!["factories"]![0]!;
            var before = Catalog().Factories[0];
            rule["fsm"] = "Spawn"; rule["bagOutput"] = false;
            var after = SyncCatalogJson.Parse(json.ToJsonString()).ReplacementParts!.Factories[0];
            Assert.Equal(before.Identity.FactoryId, after.Identity.FactoryId);
            Assert.Equal(before.Fsm, after.Fsm); Assert.False(after.BagOutput);
        }

        [Fact]
        public void FanbeltFittingRetainsTheNativeAlternatorPrerequisite()
        {
            var rules = Catalog();
            var belt = Assert.Single(rules.Factories, f => f.FitPrerequisite != null);
            Assert.Equal("Fanbelt", belt.Fsm);
            var prerequisite = belt.FitPrerequisite!;
            Assert.Equal("Alternator", prerequisite.State); Assert.Equal("db_Installed2", prerequisite.Reference);
            Assert.Equal("SettingRotation", prerequisite.Scalar); Assert.Equal("Setting", prerequisite.Result);
            Assert.Equal(6f, prerequisite.MinimumExclusive);
        }

        [Theory]
        [InlineData("missing-state")]
        [InlineData("empty-reference")]
        [InlineData("missing-scalar")]
        [InlineData("empty-result")]
        [InlineData("missing-threshold")]
        [InlineData("text-threshold")]
        [InlineData("infinite-threshold")]
        [InlineData("null-prerequisite")]
        [InlineData("slotted-prerequisite")]
        public void MalformedFittingPrerequisitesFailBeforeNativeHooks(string fault)
        {
            var json = JsonNode.Parse(CatalogText())!;
            var rules = json["replacementParts"]!["factories"]!.AsArray();
            var belt = rules.Single(f => (string?)f!["fsm"] == "Fanbelt")!;
            var prerequisite = belt["fitPrerequisite"]!.AsObject();
            switch (fault)
            {
                case "missing-state": prerequisite.Remove("state"); break;
                case "empty-reference": prerequisite["reference"] = ""; break;
                case "missing-scalar": prerequisite.Remove("scalar"); break;
                case "empty-result": prerequisite["result"] = ""; break;
                case "missing-threshold": prerequisite.Remove("minimumExclusive"); break;
                case "text-threshold": prerequisite["minimumExclusive"] = "6"; break;
                case "infinite-threshold": prerequisite["minimumExclusive"] = 1e100; break;
                case "null-prerequisite": belt["fitPrerequisite"] = null; break;
                case "slotted-prerequisite": rules.Single(f => (string?)f!["prefix"] == "VIN103")!["fitPrerequisite"] = prerequisite.DeepClone(); break;
            }
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Theory]
        [InlineData("factory")]
        [InlineData("prefix")]
        [InlineData("padded-counter")]
        [InlineData("overflow-counter")]
        [InlineData("negative-assembly")]
        [InlineData("loose-installed-flag")]
        [InlineData("fitted-uninstalled-flag")]
        [InlineData("missing-scalar")]
        [InlineData("extra-scalar")]
        [InlineData("nan-scalar")]
        [InlineData("infinite-scalar")]
        [InlineData("nan-position")]
        [InlineData("infinite-position")]
        [InlineData("nan-rotation")]
        [InlineData("zero-rotation")]
        public void InvalidObservationCannotReplaceAValidPart(string fault)
        {
            var replica = Replica(); Assert.True(replica.Receive(State(), out uint id));
            var invalid = State(revision: 2);
            switch (fault)
            {
                case "factory": invalid.FactoryId++; break;
                case "prefix": invalid.NativeId = "VIN1301"; break;
                case "padded-counter": invalid.NativeId = "VIN13301"; break;
                case "overflow-counter": invalid.NativeId = "VIN1332147483648"; break;
                case "negative-assembly": invalid.AssemblyId = -1; break;
                case "loose-installed-flag": invalid.Installed = true; break;
                case "fitted-uninstalled-flag": invalid.AssemblyId = 1; break;
                case "missing-scalar": invalid.Scalars = invalid.Scalars.Take(1).ToArray(); break;
                case "extra-scalar": invalid.Scalars = invalid.Scalars.Concat(new[] { 0f }).ToArray(); break;
                case "nan-scalar": invalid.Scalars[0] = float.NaN; break;
                case "infinite-scalar": invalid.Scalars[0] = float.NegativeInfinity; break;
                case "nan-position": invalid.Position = new NetVector3(float.NaN, 0, 0); break;
                case "infinite-position": invalid.Position = new NetVector3(0, float.PositiveInfinity, 0); break;
                case "nan-rotation": invalid.Rotation = new NetQuaternion(0, 0, 0, float.NaN); break;
                case "zero-rotation": invalid.Rotation = default; break;
            }
            Assert.False(replica.Receive(invalid, out _)); Assert.Equal(1u, replica.Get(id)!.Revision);
        }

        [Fact]
        public void InstalledOrPartlySettledPartsAreRetainedWithoutFabricatingALooseCopy()
        {
            var rule = Catalog().Factories.Single(f => f.Prefix == "VIN133").Identity;
            var replica = Replica(); var state = State();
            state.Installed = true; state.AssemblyId = 1;
            Assert.True(replica.Receive(state, out uint id)); Assert.False(rule.CanCreate(replica.Get(id)!));
            state.AssemblyId = 12; state.Revision++;
            Assert.True(replica.Receive(state, out _)); Assert.False(rule.CanCreate(replica.Get(id)!));
            state.Installed = false; state.AssemblyId = 0; state.Scalars[rule.TightnessIndex] = .01f; state.Revision++;
            Assert.True(replica.Receive(state, out _)); Assert.False(rule.CanCreate(replica.Get(id)!));
            state.Scalars[rule.TightnessIndex] = 0; state.Revision++;
            Assert.True(replica.Receive(state, out _)); Assert.True(rule.CanCreate(replica.Get(id)!));
        }

        [Fact]
        public void ReplaysRefreshCreationPoseButCannotRollBackConditionOrAssembly()
        {
            var replica = Replica(); var state = State(revision: uint.MaxValue);
            Assert.True(replica.Receive(state, out uint id));
            state.Position = new NetVector3(500, 0, 0); Assert.True(replica.Receive(state, out _));
            state.Installed = true; state.AssemblyId = 1; Assert.False(replica.Receive(state, out _));
            state.Revision = 0; Assert.True(replica.Receive(state, out _));
            state.Revision = uint.MaxValue; Assert.False(replica.Receive(state, out _));
            Assert.True(replica.Get(id)!.Installed); Assert.Equal(500, replica.Get(id)!.Position.X);
        }

        [Fact]
        public void RetiredPartsNeverResurrectFromAQueuedSnapshotAndReceiptArraysAreOwned()
        {
            var life = new ItemSpawnLifecycle(); var replica = Replica(life); var state = State();
            Assert.True(replica.Receive(state, out uint id)); state.Scalars[0] = 0;
            replica.Get(id)!.Scalars[0] = 1; Assert.Equal(98.75f, replica.Get(id)!.Scalars[0]);
            life.Retire(id); Assert.Null(replica.Get(id)); Assert.False(replica.Receive(State(), out _));
            replica.Clear(); Assert.False(replica.Receive(State(), out _));
            life.Clear(); Assert.True(replica.Receive(State(), out _));
        }

        [Theory]
        [InlineData(PartParentKind.Vehicle, "")]
        [InlineData(PartParentKind.NativePart, "Engine/Mount")]
        public void AttachedLookupReturnsAnOwnedSnapshotForTheExactMount(PartParentKind kind, string path)
        {
            var replica = Replica(); var state = State("VIN131");
            state.Installed = true; state.AssemblyId = 1;
            state.ParentKind = kind; state.ParentId = 902148; state.ParentPath = path;
            Assert.True(replica.Receive(state, out uint id));
            var found = replica.GetAttached(id, kind, state.ParentId, path);
            Assert.NotNull(found); Assert.True(ReplacementPartReplica.SameValues(state, found!));
            Assert.Equal(state.Revision, found.Revision);
            found.Scalars[0] = -1; found.ParentPath = "Changed";
            state.Scalars[0] = -2;
            Assert.Equal(98.75f, replica.GetAttached(id, kind, state.ParentId, path)!.Scalars[0]);
            Assert.Equal(path, replica.Get(id)!.ParentPath);
        }

        [Theory]
        [InlineData("kind")]
        [InlineData("parent")]
        [InlineData("path")]
        [InlineData("unknown")]
        [InlineData("loose")]
        [InlineData("no-attachment")]
        public void AttachedLookupRejectsOtherMountsAndUnattachedParts(string difference)
        {
            var replica = Replica(); var state = State("VIN131");
            state.Installed = true; state.AssemblyId = 1;
            state.ParentKind = PartParentKind.Vehicle; state.ParentId = 902148; state.ParentPath = "Engine/Mount";
            if (difference == "loose" || difference == "no-attachment")
            {
                state.ParentKind = PartParentKind.None; state.ParentId = 0; state.ParentPath = "";
                if (difference == "loose") { state.Installed = false; state.AssemblyId = 0; }
            }
            Assert.True(replica.Receive(state, out uint id));
            var kind = difference == "kind" ? PartParentKind.NativePart : state.ParentKind;
            uint parent = difference == "parent" ? 902149u : state.ParentId;
            string path = difference == "path" ? "Engine/Other" : state.ParentPath;
            Assert.Null(replica.GetAttached(difference == "unknown" ? 0 : id, kind, parent, path));
        }

        [Fact]
        public void AttachedLookupFollowsAcceptedMovesRetirementAndClear()
        {
            var life = new ItemSpawnLifecycle(); var replica = Replica(life); var state = State("VIN131");
            state.Installed = true; state.AssemblyId = 1;
            state.ParentKind = PartParentKind.Vehicle; state.ParentId = 902148; state.ParentPath = "Engine/Mount";
            Assert.True(replica.Receive(state, out uint id));
            var original = replica.GetAttached(id, state.ParentKind, state.ParentId, state.ParentPath)!;
            state.ParentPath = "Engine/Other"; state.Revision++;
            Assert.True(replica.Receive(state, out _)); Assert.False(replica.Receive(original, out _));
            Assert.Null(replica.GetAttached(id, original.ParentKind, original.ParentId, original.ParentPath));
            Assert.Equal(2u, replica.GetAttached(id, state.ParentKind, state.ParentId, state.ParentPath)!.Revision);
            life.Retire(id); Assert.Null(replica.GetAttached(id, state.ParentKind, state.ParentId, state.ParentPath));
            replica.Clear(); life.Clear(); Assert.Null(replica.GetAttached(id, state.ParentKind, state.ParentId, state.ParentPath));
            Assert.True(replica.Receive(original, out _));
            Assert.NotNull(replica.GetAttached(id, original.ParentKind, original.ParentId, original.ParentPath));
        }

        [Fact]
        public void JoinSnapshotDoesNotConsumePendingConditionOrInstallationBroadcast()
        {
            var publication = new ReplacementPartPublication(); var state = State();
            uint first = publication.Observe(state); Assert.True(publication.NeedsBroadcast);
            uint firstPresentation = state.PresentationRevision;
            publication.MarkBroadcast(first, firstPresentation); Assert.False(publication.NeedsBroadcast);
            state.Installed = true; state.AssemblyId = 1; uint snapshot = publication.Observe(state);
            Assert.True(publication.NeedsBroadcast); Assert.Equal(snapshot, publication.Observe(state));
            publication.MarkBroadcast(first, firstPresentation); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(snapshot, state.PresentationRevision); Assert.False(publication.NeedsBroadcast);
            state.Position = new NetVector3(123, 0, 0); Assert.Equal(snapshot, publication.Observe(state));
            Assert.False(publication.NeedsBroadcast);
        }

        [Theory]
        [InlineData("duplicate-factory")]
        [InlineData("duplicate-resolved-fsm")]
        [InlineData("empty-fsm")]
        [InlineData("invalid-fsm")]
        [InlineData("null-fsm")]
        [InlineData("numeric-fsm")]
        [InlineData("text-bag-output")]
        [InlineData("numeric-bag-output")]
        [InlineData("null-bag-output")]
        [InlineData("bad-prefix")]
        [InlineData("missing-tightness")]
        [InlineData("duplicate-scalar")]
        [InlineData("missing-reference")]
        [InlineData("duplicate-reference")]
        [InlineData("missing-tool-mode")]
        public void MalformedCatalogFailsBeforeNativeHooksAreInstalled(string fault)
        {
            var json = JsonNode.Parse(CatalogText())!; var rules = json["replacementParts"]!["factories"]!.AsArray();
            var rule = rules[0]!;
            switch (fault)
            {
                case "duplicate-factory": rules.Add(rule.DeepClone()); break;
                case "duplicate-resolved-fsm": rules[1]!["path"] = rule["path"]!.GetValue<string>();
                    rules[1]!["fsm"] = json["replacementParts"]!["factoryFsm"]!.GetValue<string>(); break;
                case "empty-fsm": rule["fsm"] = ""; break;
                case "invalid-fsm": rule["fsm"] = "Spawn/Other"; break;
                case "null-fsm": rule["fsm"] = null; break;
                case "numeric-fsm": rule["fsm"] = 1; break;
                case "text-bag-output": rule["bagOutput"] = "true"; break;
                case "numeric-bag-output": rule["bagOutput"] = 1; break;
                case "null-bag-output": rule["bagOutput"] = null; break;
                case "bad-prefix": rule["prefix"] = "../part"; break;
                case "missing-tightness": rule["scalars"] = new JsonArray("Wear"); break;
                case "duplicate-scalar": rule["scalars"] = new JsonArray("Tightness", "Tightness"); break;
                case "missing-reference": rule["references"] = new JsonArray(); break;
                case "duplicate-reference": rule["references"]!.AsArray().Add(rule["references"]![0]!.DeepClone()); break;
                case "missing-tool-mode": json["replacementParts"]!.AsObject().Remove("replicaRepairVariable"); break;
            }
            Assert.Throws<FormatException>(() => SyncCatalogJson.Parse(json.ToJsonString()));
        }

        [Fact]
        public void NativeFitDestroyBodyAndRemoveRecreateBodyPreserveTheSamePart()
        {
            var life = new ItemSpawnLifecycle(); var replica = Replica(life);
            var publication = new ReplacementPartPublication(); var state = State();
            Assert.True(PartIdentity.TryItemId(state.NativeId, out uint id));
            void Observe(int assemblyId, bool hasBody, uint expectedRevision, bool loose)
            {
                var phase = PartStatePolicy.NativePhase(true, assemblyId, false, hasBody);
                Assert.NotEqual(NativePartPhase.Retired, phase);
                Assert.NotEqual(NativePartPhase.Unavailable, phase);
                state.AssemblyId = assemblyId; state.Installed = phase == NativePartPhase.Fitted;
                state.Revision = publication.Observe(state);
                Assert.Equal(expectedRevision, state.Revision);
                Assert.True(replica.Receive(Assert.IsType<ReplacementPartState>(PacketCodec.Decode(PacketCodec.Encode(state))), out uint received));
                Assert.Equal(id, received); Assert.Equal(loose, replica.AllowsLooseMotion(id));
                Assert.False(life.IsRetired(id)); publication.MarkBroadcast(state.Revision, state.PresentationRevision);
            }
            Observe(0, true, 1, true);
            Observe(1, true, 2, false); // The native mount has not yet destroyed Rigidbody.
            Observe(1, false, 2, false);
            Assert.Equal(NativePartPhase.Unavailable, PartStatePolicy.NativePhase(true, 0, false, false));
            Assert.False(replica.AllowsLooseMotion(id)); // UNINSTALL awaits the replacement body.
            Observe(0, true, 3, true);
            Observe(1, false, 4, false);
            Assert.Equal(NativePartPhase.Retired, PartStatePolicy.NativePhase(true, 1, true, false));
            life.Retire(id);
            Assert.False(replica.AllowsLooseMotion(id)); Assert.Null(replica.Get(id));
            Assert.False(replica.Receive(State(revision: 5), out _));
        }

        [Fact]
        public void LateJoinFittedStateBlocksLooseMotionUntilNewerRemovalAndRejectsStaleReplay()
        {
            var replica = Replica(); var state = State(revision: uint.MaxValue);
            state.AssemblyId = 1; state.Installed = true;
            Assert.True(replica.Receive(state, out uint id)); Assert.False(replica.AllowsLooseMotion(id));
            var staleLoose = State(revision: uint.MaxValue - 1);
            Assert.False(replica.Receive(staleLoose, out _)); Assert.False(replica.AllowsLooseMotion(id));
            Assert.True(replica.Receive(State(revision: 0), out _)); Assert.True(replica.AllowsLooseMotion(id));
            Assert.False(replica.Receive(state, out _)); Assert.True(replica.AllowsLooseMotion(id));
        }

        [Fact]
        public void ABodyRecreatedBetweenPollsStillPublishesEvenWhenTheFinalScalarsMatch()
        {
            var publication = new ReplacementPartPublication(); var state = State();
            uint first = publication.Observe(state); publication.MarkBroadcast(first, state.PresentationRevision);
            state.Position = new NetVector3(50, 1, 2);
            uint recreated = publication.Observe(state, bodyChanged: true);
            Assert.Equal(first + 1, recreated); Assert.True(publication.NeedsBroadcast);
            Assert.Equal(recreated, publication.Observe(state)); // A join read cannot hide that update.
            Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(recreated, state.PresentationRevision); Assert.False(publication.NeedsBroadcast);
        }
    }
}
