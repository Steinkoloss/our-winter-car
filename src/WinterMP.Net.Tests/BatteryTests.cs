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
    public class BatteryTests
    {
        private static BatteryState State(uint revision, byte flags, float charge = 0) => new BatteryState { Revision = revision, Flags = flags, Charge = charge };
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;

        [Theory]
        [InlineData(0, 0)] [InlineData(1, 0)] [InlineData(3, 0)] [InlineData(3, -0.25f)] [InlineData(3, 109.75f)] [InlineData(3, 127)]
        public void NativeAvailabilityAndFiniteChargeRoundTrip(byte flags, float charge)
        {
            var state = (BatteryState)PacketCodec.Decode(PacketCodec.Encode(State(uint.MaxValue, flags, charge)));
            Assert.Equal(uint.MaxValue, state.Revision); Assert.Equal(flags, state.Flags); Assert.Equal(charge, state.Charge);
            var writer = new NetWriter(); state.Write(writer); Assert.Equal(13, writer.ToArray().Length);
            Assert.True(new BatteryReplica().Receive(state));
        }

        [Theory]
        [InlineData(2, 0)] [InlineData(4, 0)] [InlineData(7, 0)] [InlineData(255, 0)]
        [InlineData(0, 127)] [InlineData(1, 127)] [InlineData(3, float.NaN)]
        [InlineData(3, float.PositiveInfinity)] [InlineData(3, float.NegativeInfinity)]
        public void UnavailableValuesUnknownFlagsAndNonfiniteChargeAreRejected(byte flags, float charge)
        {
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(State(1, flags, charge)));
            var writer = new NetWriter(); writer.WriteUInt32(1); writer.WriteByte(flags); writer.WriteSingle(charge); writer.WriteSingle(0);
            Assert.Throws<ProtocolException>(() => new BatteryState().Read(new NetReader(writer.ToArray())));
            Assert.False(new BatteryReplica().Receive(State(1, flags, charge)));
            Assert.Throws<ArgumentException>(() => new BatteryPublication().Observe(flags, charge));
        }

        [Fact]
        public void CopiedStateRejectsConflictsAndStaleRevisionsAcrossWraparound()
        {
            var replica = new BatteryReplica(); var state = State(uint.MaxValue, 3, 127);
            Assert.True(replica.Receive(state)); state.Charge = 1;
            var copy = replica.Get()!; copy.Flags = 0; Assert.Equal(127, replica.Get()!.Charge); Assert.Equal((byte)3, replica.Get()!.Flags);
            Assert.True(replica.Receive(State(uint.MaxValue, 3, 127)));
            Assert.False(replica.Receive(State(uint.MaxValue, 3, 126)));
            Assert.False(replica.Receive(State(uint.MaxValue, 1)));
            Assert.True(replica.Receive(State(0, 3, 126)));
            Assert.False(replica.Receive(State(uint.MaxValue, 3, 127)));
            Assert.False(replica.Receive(State(0x80000000, 3, 127)));
            Assert.True(replica.Receive(State(1, 0))); Assert.Equal((byte)0, replica.Get()!.Flags);
            replica.Clear(); Assert.Null(replica.Get()); Assert.True(replica.Receive(State(0, 3, 127)));
        }

        [Fact]
        public void JoinSnapshotAndOldAcknowledgmentCannotSwallowPendingChargeChanges()
        {
            var publication = new BatteryPublication(); Assert.False(publication.NeedsBroadcast);
            var first = publication.Observe(3, 127); publication.MarkBroadcast(first.Revision); Assert.False(publication.NeedsBroadcast);
            var snapshot = publication.Observe(3, 126); Assert.Equal(first.Revision + 1, snapshot.Revision); snapshot.Charge = 1;
            var live = publication.Observe(3, 126); Assert.Equal(snapshot.Revision, live.Revision); Assert.Equal(126, live.Charge);
            publication.MarkBroadcast(first.Revision); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(live.Revision); Assert.False(publication.NeedsBroadcast);
            var removed = publication.Observe(1, 0); Assert.Equal(live.Revision + 1, removed.Revision); Assert.True(publication.NeedsBroadcast);
            Assert.Equal(removed.Revision + 1, publication.Observe(0, 0).Revision);
        }

        [Fact]
        public void BatteryIsSelectedHostOnlyAfterHandshakeAndUsesOrderedEvents()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.BatteryState, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.BatteryState, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.BatteryState, false, true, true, false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.BatteryState, false, true, true, true));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.BatteryState, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.BatteryState, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.BatteryState, Channel.ReliableBulk));
        }

        [Fact]
        public void ThreeAuditedReadsRequirePausedBatteryAndAllTenChargeWriters()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            var entry = Assert.Single(parsed.GuestEngineInputs!.Entries, e => e.BatterySource != null);
            Assert.Empty(entry.Families); Assert.Empty(entry.MountVariable); Assert.Equal("Electrics", entry.Fsm);
            Assert.Equal(new[] { "Installed", "Charge", "Charge" }, entry.Readers.Select(r => r.Variable));
            Assert.Equal(new[] { "Battery", "Charge", "Volts" }, entry.Readers.Select(r => r.Output));
            Assert.All(entry.Readers, r => Assert.False(r.EveryFrame));
            Assert.Equal(10, parsed.GuestEngineProtection!.Writers.SelectMany(w => w.Actions).Count(a => a.TargetVariable == "db_Battery"));
            Assert.Single(parsed.GuestEngineProtection.PausedFsms, f => f.Path == entry.MountPath && f.Fsm == "Data");
            Assert.Equal(39, parsed.ReplacementParts!.Factories.Count); Assert.Equal(34, parsed.PartsPackages!.Factories.Count);
        }

        [Theory]
        [InlineData("profile")] [InlineData("path")] [InlineData("fsm")] [InlineData("idle")]
        [InlineData("ready")] [InlineData("paused")] [InlineData("pause state")] [InlineData("writer")]
        [InlineData("entry")] [InlineData("source")] [InlineData("mount")] [InlineData("family")]
        [InlineData("slot")] [InlineData("mount variable")] [InlineData("field")] [InlineData("type")] [InlineData("read")]
        public void IncompleteBatteryProfilesFailInsideInputSubsystem(string fault)
        {
            var json = Catalog(); var inputs = json["guestEngineInputs"]!; var battery = inputs["battery"]!;
            var entries = inputs["entries"]!.AsArray(); var entry = entries.Single(e => e!["battery"] != null)!;
            var protection = json["guestEngineProtection"]!; var pauses = protection["pausedFsms"]!.AsArray();
            var pause = pauses.Single(p => p!["path"]!.GetValue<string>() == "CORRIS/Assemblies/VINP_Battery")!;
            switch (fault)
            {
                case "profile": inputs.AsObject().Remove("battery"); break;
                case "path": battery["path"] = "CORRIS/Elsewhere"; break;
                case "fsm": battery["fsm"] = "Status"; break;
                case "idle": battery["idleState"] = "Install 2"; break;
                case "ready": battery["readyStates"]![0] = "Install 2"; break;
                case "paused": pauses.Remove(pause); break;
                case "pause state": pause["requiredStates"]!.AsArray().RemoveAt(0); break;
                case "writer":
                    var writer = protection["writers"]!.AsArray().Single(w => w!["fsm"]!.GetValue<string>() == "Electrics")!;
                    var actions = writer["actions"]!.AsArray(); actions.Remove(actions.First(a => a!["targetVariable"]!.GetValue<string>() == "db_Battery")); break;
                case "entry": entries.Remove(entry); break;
                case "source": entry["battery"] = "Other"; break;
                case "mount": entry["mountPath"] = "CORRIS/Elsewhere"; break;
                case "family": entry["familyPrefix"] = "VIN133"; break;
                case "slot": entry["slotIndex"] = 1; break;
                case "mount variable": entry["mountVariable"] = "VINP"; break;
                case "field": entry["readers"]![1]!["variable"] = "Wear"; break;
                case "type": entry["readers"]![1]!["actionType"] = "GetFsmBool"; break;
                case "read": entry["readers"]!.AsArray().RemoveAt(0); break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.GuestEngineInputsError!);
            Assert.NotNull(parsed.GuestEngineProtection); Assert.NotNull(parsed.ReplacementParts); Assert.NotEmpty(parsed.Doors);
        }
    }
}
