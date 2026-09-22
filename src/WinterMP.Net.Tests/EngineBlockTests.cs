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
    public class EngineBlockTests
    {
        private static EngineBlockState State(uint revision, byte flags, float wear = 0) => new EngineBlockState { Revision = revision, Flags = flags, Wear = wear };
        private static JsonNode Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;

        [Theory]
        [InlineData(0, 0)] [InlineData(1, 0)] [InlineData(3, 0)] [InlineData(3, -0.25f)] [InlineData(3, 109.75f)] [InlineData(3, 127)] [InlineData(7, 19.75f)] [InlineData(11, 65)] [InlineData(15, 0)]
        public void NativeAvailabilityAndFiniteWearRoundTrip(byte flags, float wear)
        {
            var state = (EngineBlockState)PacketCodec.Decode(PacketCodec.Encode(State(uint.MaxValue, flags, wear)));
            Assert.Equal(uint.MaxValue, state.Revision); Assert.Equal(flags, state.Flags); Assert.Equal(wear, state.Wear);
            var writer = new NetWriter(); state.Write(writer); Assert.Equal(209, writer.ToArray().Length);
            Assert.True(new EngineBlockReplica().Receive(state));
        }

        [Theory]
        [InlineData(8, 0)] [InlineData(9, 0)] [InlineData(10, 0)] [InlineData(12, 0)] [InlineData(13, 0)] [InlineData(14, 0)] [InlineData(16, 0)] [InlineData(2, 0)] [InlineData(4, 0)] [InlineData(5, 0)] [InlineData(255, 0)]
        [InlineData(0, 127)] [InlineData(1, 127)] [InlineData(3, float.NaN)]
        [InlineData(3, float.PositiveInfinity)] [InlineData(3, float.NegativeInfinity)]
        public void UnavailableValuesUnknownFlagsAndNonfiniteWearAreRejected(byte flags, float wear)
        {
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(State(1, flags, wear)));
            var writer = new NetWriter(); writer.WriteUInt32(1); writer.WriteByte(flags); writer.WriteSingle(wear); writer.WriteSingle(0); writer.WriteSingle(0); writer.WriteSingle(0);
            for (int i = 0; i < 6; i++) writer.WriteSingle(0);
            writer.WriteByte(0); for (int i = 0; i < 12; i++) writer.WriteSingle(0); writer.WriteBool(false); for (int i = 0; i < 8; i++) writer.WriteSingle(0); writer.WriteBool(false); for (int i = 0; i < 5; i++) writer.WriteSingle(0); writer.WriteBool(false); writer.WriteSingle(0); writer.WriteBool(false); for (int i = 0; i < 4; i++) writer.WriteSingle(0); writer.WriteByte(0); for (int i = 0; i < 5; i++) writer.WriteSingle(0); writer.WriteByte(0); for (int i = 0; i < 3; i++) writer.WriteSingle(0); writer.WriteBool(false); writer.WriteSingle(0);
            Assert.Throws<ProtocolException>(() => new EngineBlockState().Read(new NetReader(writer.ToArray())));
            Assert.False(new EngineBlockReplica().Receive(State(1, flags, wear)));
            Assert.Throws<ArgumentException>(() => new EngineBlockPublication().Observe(flags, wear));
        }

        [Fact]
        public void CopiedStateRejectsConflictsAndStaleRevisionsAcrossWraparound()
        {
            var replica = new EngineBlockReplica(); var state = State(uint.MaxValue, 3, 127);
            Assert.True(replica.Receive(state)); state.Wear = 1;
            var copy = replica.Get()!; copy.Flags = 0; Assert.Equal(127, replica.Get()!.Wear); Assert.Equal((byte)3, replica.Get()!.Flags);
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
        public void CopyPreservesEveryPopulatedWireInput()
        {
            var state = PopulatedState();
            var copy = state.Copy();
            Assert.NotSame(state, copy); Assert.True(copy.Valid);
            Assert.Equal(PacketCodec.Encode(state), PacketCodec.Encode(copy));
        }

        private static EngineBlockState PopulatedState()
        {
            var state = new EngineBlockState { Revision = uint.MaxValue, Flags = 63,
                ExhaustFlags = 15, CoolingAirflowFlags = 15, CoolantHoseFlags = 15 };
            float value = 1;
            foreach (var field in typeof(EngineBlockState).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (field.FieldType == typeof(float)) field.SetValue(state, value++);
                if (field.FieldType == typeof(bool)) field.SetValue(state, true);
                if (field.FieldType == typeof(float[]))
                {
                    var array = (float[])field.GetValue(state)!;
                    for (int i = 0; i < array.Length; i++) array[i] = value++;
                }
            }
            Assert.True(state.Valid);
            return state;
        }

        [Fact]
        public void ReadOnlySnapshotExposesEveryInputWithoutMutableReferences()
        {
            var source = PopulatedState(); var replica = new EngineBlockReplica();
            Assert.True(replica.Receive(source)); var snapshot = replica.Inputs!;
            var type = snapshot.GetType();
            Assert.Empty(type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance));
            foreach (var field in typeof(EngineBlockState).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (field.FieldType == typeof(float[]))
                {
                    string method = field.Name == nameof(EngineBlockState.ValveSettings) ? "ValveSettingAt" : field.Name + "At";
                    var read = type.GetMethod(method)!; Assert.Equal(typeof(float), read.ReturnType);
                    var values = (float[])field.GetValue(source)!;
                    for (int i = 0; i < values.Length; i++) Assert.Equal(values[i], read.Invoke(snapshot, new object[] { i }));
                }
                else Assert.Equal(field.GetValue(source), type.GetProperty(field.Name)!.GetValue(snapshot));
            }
            foreach (var property in type.GetProperties())
            { Assert.Null(property.SetMethod); Assert.True(property.PropertyType.IsValueType); }
            foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                Assert.True(method.ReturnType.IsValueType);
        }

        [Fact]
        public void ReadOnlySnapshotSurvivesReplacementRejectionAndClear()
        {
            var replica = new EngineBlockReplica(); Assert.Null(replica.Inputs);
            var source = PopulatedState(); Assert.True(replica.Receive(source)); var first = replica.Inputs!;
            var bytes = PacketCodec.Encode(replica.Get()!);
            Assert.True(replica.Receive(source.Copy())); Assert.Same(first, replica.Inputs);
            source.Wear++; Assert.False(replica.Receive(source)); Assert.Same(first, replica.Inputs);
            source.Revision = 0; Assert.True(replica.Receive(source)); var next = replica.Inputs!;
            Assert.NotSame(first, next); Assert.Equal(0u, next.Revision); Assert.Equal(first.Wear + 1, next.Wear);
            Assert.False(replica.Receive(PopulatedState())); Assert.Same(next, replica.Inputs);
            var invalid = source.Copy(); invalid.Wear = float.NaN;
            Assert.False(replica.Receive(invalid)); Assert.Same(next, replica.Inputs);
            replica.Clear(); Assert.Null(replica.Inputs); Assert.Null(replica.Get());
            Assert.Equal(uint.MaxValue, first.Revision); Assert.Equal(PopulatedState().Wear, first.Wear);
            Assert.True(replica.Receive(PopulatedState())); Assert.NotSame(first, replica.Inputs);
            Assert.Equal(bytes, PacketCodec.Encode(replica.Get()!));
            Assert.Equal(0u, next.Revision); Assert.Equal(source.Wear, next.Wear);
        }

        [Theory]
        [InlineData(nameof(EngineBlockState.CoolantHoseTightness), "CoolantHoseTightnessAt")]
        [InlineData(nameof(EngineBlockState.ValveSettings), "ValveSettingAt")]
        [InlineData(nameof(EngineBlockState.ExhaustPerformance), "ExhaustPerformanceAt")]
        public void ReadOnlySnapshotIsIsolatedFromIncomingAndReturnedArrays(string name, string accessor)
        {
            var source = PopulatedState(); var replica = new EngineBlockReplica(); Assert.True(replica.Receive(source));
            var snapshot = replica.Inputs!;
            var field = typeof(EngineBlockState).GetField(name)!;
            var incoming = (float[])field.GetValue(source)!;
            var copy = replica.Get()!; var outgoing = (float[])field.GetValue(copy)!;
            var read = (Func<int, float>)Delegate.CreateDelegate(typeof(Func<int, float>), snapshot, accessor);
            float before = incoming[0]; incoming[0]++; outgoing[0]--;
            source.Flags = 0; copy.Flags = 0;
            Assert.Equal(before, read(0)); Assert.Equal((byte)63, snapshot.Flags);
            Assert.Equal(before, ((float[])field.GetValue(replica.Get())!)[0]);
            Assert.Throws<IndexOutOfRangeException>(() => read(-1));
            Assert.Throws<IndexOutOfRangeException>(() => read(incoming.Length));
            replica.Clear(); Assert.Equal(before, read(0));
        }

        [Fact]
        public void RepeatedReadOnlySnapshotsDoNotAllocate()
        {
            var replica = new EngineBlockReplica(); Assert.True(replica.Receive(PopulatedState()));
            var snapshot = replica.Inputs!; float observed = 0;
            for (int i = 0; i < 1000; i++) observed += replica.Inputs!.CoolantHoseTightnessAt(i % 4);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) observed += replica.Inputs!.CoolantHoseTightnessAt(i % 4);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated); Assert.True(observed > 0); Assert.Same(snapshot, replica.Inputs);
        }

        [Theory]
        [InlineData(nameof(EngineBlockState.CoolantHoseTightness))]
        [InlineData(nameof(EngineBlockState.ValveSettings))]
        [InlineData(nameof(EngineBlockState.ExhaustPerformance))]
        public void CopiedInputArraysRemainIndependentInBothDirections(string name)
        {
            var state = new EngineBlockState(); var field = typeof(EngineBlockState).GetField(name)!;
            var source = (float[])field.GetValue(state)!; source[0] = 12;
            var copy = state.Copy(); var copied = (float[])field.GetValue(copy)!;
            Assert.NotSame(source, copied); Assert.Equal(source, copied);
            source[0] = 13; Assert.Equal(12, copied[0]);
            copied[1] = 14; Assert.Equal(0, source[1]);
        }

        [Theory]
        [InlineData(nameof(EngineBlockState.CoolantHoseTightness))]
        [InlineData(nameof(EngineBlockState.ValveSettings))]
        [InlineData(nameof(EngineBlockState.ExhaustPerformance))]
        public void CopyRetainsMalformedArrayNormalizationWithoutRepairingAvailability(string name)
        {
            var state = new EngineBlockState(); var field = typeof(EngineBlockState).GetField(name)!;
            field.SetValue(state, null);
            var copy = state.Copy();
            Assert.Empty((float[])field.GetValue(copy)!); Assert.Null(field.GetValue(state));
            Assert.False(copy.Valid);
        }

        [Fact]
        public void JoinSnapshotAndOldAcknowledgmentCannotSwallowPendingWearChanges()
        {
            var publication = new EngineBlockPublication(); Assert.False(publication.NeedsBroadcast);
            var first = publication.Observe(3, 127); publication.MarkBroadcast(first.Revision); Assert.False(publication.NeedsBroadcast);
            var snapshot = publication.Observe(3, 126); Assert.Equal(first.Revision + 1, snapshot.Revision); snapshot.Wear = 1;
            var live = publication.Observe(3, 126); Assert.Equal(snapshot.Revision, live.Revision); Assert.Equal(126, live.Wear);
            publication.MarkBroadcast(first.Revision); Assert.True(publication.NeedsBroadcast);
            publication.MarkBroadcast(live.Revision); Assert.False(publication.NeedsBroadcast);
            var removed = publication.Observe(1, 0); Assert.Equal(live.Revision + 1, removed.Revision); Assert.True(publication.NeedsBroadcast);
            Assert.Equal(removed.Revision + 1, publication.Observe(0, 0).Revision);
        }

        [Fact]
        public void EngineBlockIsSelectedHostOnlyAfterHandshakeAndUsesOrderedEvents()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.EngineBlockState, true, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.EngineBlockState, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.EngineBlockState, false, true, true, false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.EngineBlockState, false, true, true, true));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.EngineBlockState, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.EngineBlockState, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.EngineBlockState, Channel.ReliableBulk));
        }

        [Fact]
        public void BlockProfilePreservesDirectAndNamedReadersWithTheirNativeCadences()
        {
            var parsed = SyncCatalogJson.Parse(Catalog().ToJsonString()); Assert.Null(parsed.GuestEngineInputsError);
            var entries = parsed.GuestEngineInputs!.Entries.Where(e => e.BlockSource != null).ToArray();
            Assert.Equal(3, entries.Length); Assert.Equal(4, entries.Sum(e => e.Readers.Count));
            Assert.All(entries, e => { Assert.Empty(e.Families); Assert.Empty(e.MountVariable); });
            var starter = entries.Single(e => e.Fsm == "Starter"); Assert.True(starter.DirectTarget); Assert.Empty(starter.TargetVariable);
            Assert.Equal(new[] { "Installed", "Installed" }, starter.Readers.Select(r => r.Variable));
            Assert.Equal(new[] { false, true }, starter.Readers.Select(r => r.EveryFrame));
            Assert.Equal(new[] { "Motor installed", "Running" }, starter.Readers.Select(r => r.State));
            Assert.All(entries.Where(e => e.Fsm != "Starter"), e => { Assert.False(e.DirectTarget); Assert.Equal("db_Block", e.TargetVariable); });
            Assert.Equal("Wear", Assert.Single(entries.Single(e => e.Fsm == "Oil").Readers).Variable);
            Assert.Equal("Damaged", Assert.Single(entries.Single(e => e.Fsm == "Cooling").Readers).Variable);
            var paused = Assert.Single(parsed.GuestEngineProtection!.PausedFsms, p => p.Path == starter.MountPath);
            Assert.Contains("Update", paused.RequiredStates); Assert.Contains("Remove part", paused.RequiredStates);
            Assert.Equal(39, parsed.ReplacementParts!.Factories.Count); Assert.Equal(34, parsed.PartsPackages!.Factories.Count);
        }

        [Theory]
        [InlineData("profile")] [InlineData("path")] [InlineData("fsm")] [InlineData("idle")]
        [InlineData("ready")] [InlineData("paused")] [InlineData("pause state")] [InlineData("entry")]
        [InlineData("source")] [InlineData("mount")] [InlineData("family")] [InlineData("slot")]
        [InlineData("mount variable")] [InlineData("field")] [InlineData("type")] [InlineData("read")]
        [InlineData("direct false")] [InlineData("direct type")] [InlineData("direct named")]
        [InlineData("named direct")] [InlineData("other direct")]
        public void PartialOrForeignBlockBindingsFailInsideProtectedInputs(string fault)
        {
            var json = Catalog(); var inputs = json["guestEngineInputs"]!; var block = inputs["block"]!;
            var entries = inputs["entries"]!.AsArray(); var entry = entries.Single(e => e!["block"] != null && e["fsm"]!.GetValue<string>() == "Starter")!;
            var pauses = json["guestEngineProtection"]!["pausedFsms"]!.AsArray();
            var pause = pauses.Single(p => p!["path"]!.GetValue<string>() == "CORRIS/MotorPivot/MassCenter/Block/VINP_Block")!;
            switch (fault)
            {
                case "profile": inputs.AsObject().Remove("block"); break;
                case "path": block["path"] = "CORRIS/Elsewhere"; break;
                case "fsm": block["fsm"] = "Status"; break;
                case "idle": block["idleState"] = "Install 2"; break;
                case "ready": block["readyState"] = "Install 2"; break;
                case "paused": pauses.Remove(pause); break;
                case "pause state": pause["requiredStates"]!.AsArray().RemoveAt(0); break;
                case "entry": entries.Remove(entry); break;
                case "source": entry["block"] = "Other"; break;
                case "mount": entry["mountPath"] = "CORRIS/Elsewhere"; break;
                case "family": entry["familyPrefix"] = "VIN101"; break;
                case "slot": entry["slotIndex"] = 1; break;
                case "mount variable": entry["mountVariable"] = "VINP"; break;
                case "field": entry["readers"]![1]!["variable"] = "Wear"; break;
                case "type": entry["readers"]![1]!["actionType"] = "GetFsmFloat"; break;
                case "read": entry["readers"]!.AsArray().RemoveAt(0); break;
                case "direct false": entry["directTarget"] = false; break;
                case "direct type": entry["directTarget"] = "true"; break;
                case "direct named": entry["targetVariable"] = "db_Block"; break;
                case "named direct": entries.Single(e => e!["block"] != null && e["fsm"]!.GetValue<string>() == "Oil")!["directTarget"] = true; break;
                case "other direct": entries.First(e => e!["familyPrefix"] != null)!["directTarget"] = true; break;
            }
            var parsed = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(parsed.GuestEngineInputs); Assert.NotEmpty(parsed.GuestEngineInputsError!);
            Assert.NotNull(parsed.GuestEngineProtection); Assert.NotNull(parsed.ReplacementParts); Assert.NotEmpty(parsed.Doors);
        }
    }
}
