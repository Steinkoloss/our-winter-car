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
    public class GearboxTests
    {
        private static GearboxState State(uint revision, byte flags, int type = 0) => new GearboxState { Revision = revision, Flags = flags, Type = type };
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;

        [Theory]
        [InlineData(0, 0)] [InlineData(1, 0)] [InlineData(1, 1)] [InlineData(1, 2)]
        [InlineData(1, -1)] [InlineData(1, int.MinValue)] [InlineData(1, int.MaxValue)]
        public void NativeIntegerRoundTripsWithoutInventingAnEnum(byte flags, int type)
        {
            var result = (GearboxState)PacketCodec.Decode(PacketCodec.Encode(State(uint.MaxValue, flags, type)));
            Assert.Equal(uint.MaxValue, result.Revision); Assert.Equal(flags, result.Flags); Assert.Equal(type, result.Type);
            var writer = new NetWriter(); result.Write(writer); Assert.Equal(9, writer.ToArray().Length);
        }

        [Theory]
        [InlineData(2, 0)] [InlineData(3, 2)] [InlineData(255, 0)] [InlineData(0, 1)] [InlineData(0, -1)]
        public void UnknownFlagsAndUnavailableValuesAreRejectedAtEveryBoundary(byte flags, int type)
        {
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(State(1, flags, type)));
            var writer = new NetWriter(); writer.WriteUInt32(1); writer.WriteByte(flags); writer.WriteInt32(type);
            Assert.Throws<ProtocolException>(() => new GearboxState().Read(new NetReader(writer.ToArray())));
            Assert.False(new GearboxReplica().Receive(State(1, flags, type)));
            Assert.Throws<ArgumentException>(() => new GearboxPublication().Observe(flags, type));
        }

        [Fact]
        public void CopiesRejectConflictsAndStaleStateAcrossWraparound()
        {
            var replica = new GearboxReplica(); var state = State(uint.MaxValue, 1, 2);
            Assert.True(replica.Receive(state)); state.Type = 0; replica.Get()!.Type = 0;
            Assert.Equal(2, replica.Get()!.Type); Assert.True(replica.Receive(State(uint.MaxValue, 1, 2)));
            Assert.False(replica.Receive(State(uint.MaxValue, 1, 0))); Assert.False(replica.Receive(State(uint.MaxValue, 0)));
            Assert.True(replica.Receive(State(0, 0))); Assert.False(replica.Receive(State(uint.MaxValue, 1, 2)));
            Assert.False(replica.Receive(State(0x80000000, 1, 2))); Assert.Equal((byte)0, replica.Get()!.Flags);
            replica.Clear(); Assert.Null(replica.Get()); Assert.True(replica.Receive(State(0, 1, 1)));
        }

        [Fact]
        public void JoinSnapshotAndOldAcknowledgmentCannotConsumeLiveTypeChange()
        {
            var publication = new GearboxPublication(); Assert.False(publication.NeedsBroadcast);
            var first = publication.Observe(1, 0); publication.MarkBroadcast(first.Revision); Assert.False(publication.NeedsBroadcast);
            var snapshot = publication.Observe(1, 2); snapshot.Type = 0;
            var live = publication.Observe(1, 2); Assert.Equal(snapshot.Revision, live.Revision); Assert.Equal(2, live.Type);
            publication.MarkBroadcast(first.Revision); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(live.Revision); Assert.False(publication.NeedsBroadcast);
            var loading = publication.Observe(0, 0); Assert.Equal(live.Revision + 1, loading.Revision); Assert.True(publication.NeedsBroadcast);
            Assert.Equal(loading.Revision + 1, publication.Observe(1, 2).Revision);
        }

        [Fact]
        public void OnlySelectedHandshakenHostMaySendOrderedGearboxState()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.GearboxState, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.GearboxState, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.GearboxState, false, true, true, false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.GearboxState, false, true, true, true));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.GearboxState, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.GearboxState, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.GearboxState, Channel.ReliableBulk));
        }

        [Fact]
        public void StarterReadsHostTypeWithNativeIntegerAndPreservesLocalSelector()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            var entry = Assert.Single(parsed.GuestEngineInputs!.Entries, e => e.GearboxSource != null);
            Assert.Empty(entry.Families); Assert.Empty(entry.MountVariable); Assert.False(entry.DirectTarget);
            Assert.Equal("db_Gearbox", entry.TargetVariable); Assert.Equal("Starter", entry.Fsm);
            var read = Assert.Single(entry.Readers); Assert.Equal("Check automatic", read.State);
            Assert.Equal(0, read.ActionIndex); Assert.Equal("GetFsmInt", read.ActionType);
            Assert.Equal("Type", read.Variable); Assert.Equal("Automatic", read.Output); Assert.False(read.EveryFrame);
            Assert.DoesNotContain(parsed.GuestEngineInputs.Entries, e => e.TargetVariable == "SimAutomatic");
            var paused = Assert.Single(parsed.GuestEngineProtection!.PausedFsms, p => p.Path == entry.MountPath);
            Assert.Equal(14, paused.RequiredStates.Length); Assert.Contains("Update 2", paused.RequiredStates); Assert.Contains("Remove part", paused.RequiredStates);
        }

        [Theory]
        [InlineData("profile")] [InlineData("path")] [InlineData("fsm")] [InlineData("idle")] [InlineData("ready")]
        [InlineData("paused")] [InlineData("pause state")] [InlineData("entry")] [InlineData("source")] [InlineData("mount")]
        [InlineData("family")] [InlineData("block")] [InlineData("direct")] [InlineData("target")] [InlineData("reader")]
        [InlineData("field")] [InlineData("type")] [InlineData("index")] [InlineData("state")] [InlineData("output")] [InlineData("cadence")]
        public void IncompleteOrForeignGearboxBindingsFailWithinProtectedInputs(string fault)
        {
            var json = Catalog(); var inputs = json["guestEngineInputs"]!; var source = inputs["gearbox"]!;
            var entries = inputs["entries"]!.AsArray(); var entry = entries.Single(e => e!["gearbox"] != null)!; var read = entry["readers"]![0]!;
            var pauses = json["guestEngineProtection"]!["pausedFsms"]!.AsArray();
            var pause = pauses.Single(p => p!["path"]!.GetValue<string>() == "CORRIS/MotorPivot/MassCenter/Block/VINP_Gearbox")!;
            switch (fault)
            {
                case "profile": inputs.AsObject().Remove("gearbox"); break;
                case "path": source["path"] = "CORRIS/Elsewhere"; break;
                case "fsm": source["fsm"] = "Status"; break;
                case "idle": source["idleState"] = "Install 2"; break;
                case "ready": source["readyState"] = "Install 2"; break;
                case "paused": pauses.Remove(pause); break;
                case "pause state": pause["requiredStates"]!.AsArray().RemoveAt(0); break;
                case "entry": entries.Remove(entry); break;
                case "source": entry["gearbox"] = "Other"; break;
                case "mount": entry["mountPath"] = "CORRIS/Elsewhere"; break;
                case "family": entry["familyPrefix"] = "VIN101"; break;
                case "block": entry["block"] = "EngineBlock"; break;
                case "direct": entry["directTarget"] = true; break;
                case "target": entry["targetVariable"] = "SimAutomatic"; break;
                case "reader": entry["readerPath"] = "CORRIS/Elsewhere"; break;
                case "field": read["variable"] = "Wear"; break;
                case "type": read["actionType"] = "GetFsmFloat"; break;
                case "index": read["actionIndex"] = 1; break;
                case "state": read["state"] = "Wiring"; break;
                case "output": read["output"] = "Other"; break;
                case "cadence": read["everyFrame"] = true; break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.GuestEngineInputsError!);
            if (fault == "paused") { Assert.Null(parsed.GuestEngineProtection); Assert.NotEmpty(parsed.GuestEngineProtectionError!); }
            else Assert.NotNull(parsed.GuestEngineProtection);
            Assert.NotNull(parsed.ReplacementParts); Assert.NotEmpty(parsed.Doors);
        }
    }
}
