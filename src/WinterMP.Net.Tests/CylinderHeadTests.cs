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
    public class CylinderHeadTests
    {
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;

        [Fact]
        public void HeadInstallationUsesBlockRevisionAndCannotBeRestoredByOldState()
        {
            var publication = new EngineBlockPublication(); var initial = publication.Observe(3, 90);
            publication.MarkBroadcast(initial.Revision); var fitted = publication.Observe(11, 90);
            Assert.Equal(initial.Revision + 1, fitted.Revision); Assert.True(publication.NeedsBroadcast);
            var snapshot = publication.Observe(11, 90); Assert.Equal(fitted.Revision, snapshot.Revision); Assert.True(publication.NeedsBroadcast);
            var replica = new EngineBlockReplica(); Assert.True(replica.Receive(snapshot)); snapshot.Flags = 3;
            Assert.Equal((byte)11, replica.Get()!.Flags); Assert.False(replica.Receive(snapshot)); Assert.False(replica.Receive(initial));
            var removed = publication.Observe(3, 90); Assert.True(replica.Receive(removed)); Assert.Equal((byte)3, replica.Get()!.Flags);
            Assert.False(replica.Receive(fitted)); var damaged = publication.Observe(15, 90); Assert.True(replica.Receive(damaged));
            Assert.Equal((byte)15, replica.Get()!.Flags); Assert.True(publication.NeedsBroadcast);
            Assert.Equal(209, PayloadLength(damaged));
        }
        private static int PayloadLength(EngineBlockState state) { var writer = new NetWriter(); state.Write(writer); return writer.ToArray().Length; }

        [Fact]
        public void HeadReadUsesMovingNativeBlockProtectionAndExistingBlockState()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            var entry = Assert.Single(parsed.GuestEngineInputs!.Entries, e => e.HeadSource != null);
            Assert.Empty(entry.Families); Assert.Empty(entry.MountVariable); Assert.False(entry.DirectTarget); Assert.Equal("db_Cylinderhead", entry.TargetVariable);
            var head = entry.HeadSource!; Assert.Same(parsed.GuestEngineInputs.Block!.Head, head);
            Assert.Equal("VIN101", head.RootPrefix); Assert.Equal("VIN111", head.PartPrefix); Assert.Equal("VINP_Cylinderhead", head.RelativePath);
            var read = Assert.Single(entry.Readers); Assert.Equal("Powertrain", read.State); Assert.Equal(0, read.ActionIndex);
            Assert.Equal("GetFsmBool", read.ActionType); Assert.Equal("Installed", read.Variable); Assert.Equal("Installed1", read.Output); Assert.False(read.EveryFrame);
            var paused = Assert.Single(parsed.GuestEngineProtection!.PausedFsms, p => p.RelativePath == "VINP_Cylinderhead");
            Assert.Equal(head.RootPrefix, paused.RootPrefix); Assert.Equal(head.RelativePath, paused.RelativePath); Assert.Equal(10, paused.RequiredStates.Length);
            Assert.Equal(39, parsed.ReplacementParts!.Factories.Count); Assert.Equal(34, parsed.PartsPackages!.Factories.Count);
        }

        [Theory]
        [InlineData("profile")] [InlineData("mount")] [InlineData("root")] [InlineData("relative")] [InlineData("part")]
        [InlineData("fsm")] [InlineData("ready")] [InlineData("pause missing")] [InlineData("pause root missing")]
        [InlineData("pause relative missing")] [InlineData("pause root")] [InlineData("pause relative")] [InlineData("pause state")]
        [InlineData("entry")] [InlineData("source")] [InlineData("entry mount")] [InlineData("reader")] [InlineData("consumer")]
        [InlineData("target")] [InlineData("direct")] [InlineData("family")] [InlineData("gearbox")] [InlineData("slot")]
        [InlineData("index")] [InlineData("state")] [InlineData("output")] [InlineData("cadence")] [InlineData("field")] [InlineData("type")]
        public void IncompleteHeadOrMovableProtectionFailsWithinItsScope(string fault)
        {
            var json = Catalog(); var inputs = json["guestEngineInputs"]!; var block = inputs["block"]!; var head = block["head"]!;
            var entries = inputs["entries"]!.AsArray(); var entry = entries.Single(e => e!["head"] != null)!; var read = entry["readers"]![0]!;
            var pauses = json["guestEngineProtection"]!["pausedFsms"]!.AsArray(); var pause = pauses.Single(p => p!["relativePath"]?.GetValue<string>() == "VINP_Cylinderhead")!;
            bool invalidProtection = false;
            switch (fault)
            {
                case "profile": block.AsObject().Remove("head"); break;
                case "mount": head["mountPath"] = "CORRIS/Other"; break;
                case "root": head["rootPrefix"] = "VIN111"; break;
                case "relative": head["relativePath"] = "VINP_Headgasket"; break;
                case "part": head["partPrefix"] = "VIN112"; break;
                case "fsm": head["fsm"] = "Status"; break;
                case "ready": head["readyState"] = "Installed"; break;
                case "pause missing": pauses.Remove(pause); break;
                case "pause root missing": pause.AsObject().Remove("rootPrefix"); invalidProtection = true; break;
                case "pause relative missing": pause.AsObject().Remove("relativePath"); invalidProtection = true; break;
                case "pause root": pause["rootPrefix"] = "VIN111"; invalidProtection = true; break;
                case "pause relative": pause["relativePath"] = "VINP_Headgasket"; invalidProtection = true; break;
                case "pause state": pause["requiredStates"]!.AsArray().RemoveAt(0); break;
                case "entry": entries.Remove(entry); break;
                case "source": entry["head"] = "Other"; break;
                case "entry mount": entry["mountPath"] = "CORRIS/Other"; break;
                case "reader": entry["readerPath"] = "CORRIS/Other"; break;
                case "consumer": entry["fsm"] = "Oil"; break;
                case "target": entry["targetVariable"] = "db_Headgasket"; break;
                case "direct": entry["directTarget"] = true; break;
                case "family": entry["familyPrefix"] = "VIN111"; break;
                case "gearbox": entry["gearbox"] = "Gearbox"; break;
                case "slot": entry["slotIndex"] = 1; break;
                case "index": read["actionIndex"] = 1; break;
                case "state": read["state"] = "Crank"; break;
                case "output": read["output"] = "Installed2"; break;
                case "cadence": read["everyFrame"] = true; break;
                case "field": read["variable"] = "Wear"; break;
                case "type": read["actionType"] = "GetFsmFloat"; break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.GuestEngineInputsError!);
            if (invalidProtection) { Assert.Null(parsed.GuestEngineProtection); Assert.NotEmpty(parsed.GuestEngineProtectionError!); }
            else Assert.NotNull(parsed.GuestEngineProtection);
            Assert.NotNull(parsed.ReplacementParts); Assert.NotEmpty(parsed.Doors);
        }
    }
}
