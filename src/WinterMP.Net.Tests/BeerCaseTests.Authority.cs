using System;
using System.Threading.Tasks;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed partial class BeerCaseTests
    {
        internal static BeerCaseUpdate Initial(int remaining = 3) => new BeerCaseUpdate {
            CaseId = BeerCasePolicy.CaseId("fixture-native-id"), NativeId = "fixture-native-id", Epoch = 7,
            Revision = 1, Capacity = 3, Remaining = remaining, Available = true };
        internal static BeerCaseExtractIntent Request(BeerCaseUpdate s, byte actor = 2, uint sequence = 1, uint connection = 12) => new BeerCaseExtractIntent {
            CaseId = s.CaseId, NativeId = s.NativeId, Epoch = s.Epoch, ExpectedRevision = s.Revision,
            ExpectedRemaining = s.Remaining, PlayerId = actor, Connection = connection, Sequence = sequence };
        internal sealed class HostDouble : IBeerCaseHost
        {
            internal BeerCaseUpdate State = Initial();
            internal BeerCaseContact Contact;
            internal int Commits, Reads, ItemMutations = 0, CargoMutations = 0, OtherWorld = 17;
            internal bool Reject, Throw, NoEffect;
            internal Action? DuringRead, DuringContact, DuringCommit;
            internal HostDouble()
            {
                Contact = new BeerCaseContact { ActorPresent = true, ActorAlive = true, Unobstructed = true,
                    CaseId = State.CaseId, NativeId = State.NativeId, Distance = .5f, PoseAgeSeconds = .1f, ContactAgeSeconds = .1f };
            }
            public BeerCaseUpdate Read() { Reads++; DuringRead?.Invoke(); return State.Copy(); }
            public BeerCaseContact ReadContact(byte actor) { DuringContact?.Invoke(); return Contact; }
            public bool TryExtractOne(BeerCaseUpdate expected, byte actor)
            {
                DuringCommit?.Invoke();
                if (Throw) throw new InvalidOperationException("portable uncertain native failure");
                if (Reject || State.Remaining != expected.Remaining) return false;
                Commits++; if (!NoEffect) State.Remaining--; return true;
            }
        }
        internal static BeerCaseAuthority Authority(out HostDouble native)
        {
            native = new HostDouble(); var a = new BeerCaseAuthority(native.State, native);
            Assert.True(a.Connect(1, 11)); Assert.True(a.Connect(2, 12)); Assert.True(a.Connect(3, 13)); return a;
        }
        [Theory]
        [InlineData((byte)1, 11u)] [InlineData((byte)2, 12u)]
        public void EitherActorConsumesOnceWithoutCaseOwnershipAndBothPeersAgree(byte actor, uint connection)
        {
            var host = Authority(out var native); var start = host.Snapshot();
            var guest = new BeerCaseReplica(start.CaseId, start.NativeId, start.Epoch);
            Assert.True(guest.Apply(start));
            var request = Assert.IsType<BeerCaseExtractIntent>(PacketCodec.Decode(PacketCodec.Encode(Request(start, actor, connection: connection))));
            Assert.True(host.TryAccept(request, actor, out var accepted));
            var result = Assert.IsType<BeerCaseUpdate>(PacketCodec.Decode(PacketCodec.Encode(accepted!)));
            Assert.True(guest.Apply(result));
            Assert.Equal(2, native.State.Remaining); Assert.Equal(1, native.Commits);
            Assert.Equal(start.CaseId, result.CaseId); Assert.Equal(start.NativeId, result.NativeId);
            Assert.Equal(PacketCodec.Encode(host.Snapshot()), PacketCodec.Encode(guest.Snapshot()!));
            Assert.False(host.TryAccept(request, actor, out _)); Assert.False(guest.Apply(result));
            Assert.Equal(1, native.Commits); Assert.Equal(0, native.ItemMutations); Assert.Equal(0, native.CargoMutations); Assert.Equal(17, native.OtherWorld);
        }
        [Theory]
        [InlineData("actor")] [InlineData("case")] [InlineData("native-id")] [InlineData("epoch")]
        [InlineData("connection")] [InlineData("revision")] [InlineData("count")] [InlineData("negative")]
        [InlineData("overflow")] [InlineData("zero-sequence")] [InlineData("unavailable")] [InlineData("exhausted")]
        [InlineData("dead")] [InlineData("absent")] [InlineData("blocked")] [InlineData("wrong-contact")]
        [InlineData("contact-id")] [InlineData("far")] [InlineData("nan")] [InlineData("old-contact")]
        [InlineData("old-pose")] [InlineData("negative-age")] [InlineData("malformed-native")]
        [InlineData("changed-native-id")] [InlineData("changed-capacity")]
        public void InvalidRequestsNeverMutateCanonicalCountItemsCargoOrWorld(string bad)
        {
            var host = Authority(out var native); var r = Request(host.Snapshot());
            switch (bad)
            {
                case "actor": r.PlayerId = 3; break;
                case "case": r.CaseId++; break;
                case "native-id": r.NativeId = "other"; r.CaseId = BeerCasePolicy.CaseId(r.NativeId); break;
                case "epoch": r.Epoch++; break;
                case "connection": r.Connection++; break;
                case "revision": r.ExpectedRevision++; break;
                case "count": r.ExpectedRemaining = 2; break;
                case "negative": r.ExpectedRemaining = -1; break;
                case "overflow": r.ExpectedRemaining = int.MaxValue; break;
                case "zero-sequence": r.Sequence = 0; break;
                case "unavailable": native.State.Available = false; break;
                case "exhausted": native.State.Remaining = 0; break;
                case "dead": native.Contact.ActorAlive = false; break;
                case "absent": native.Contact.ActorPresent = false; break;
                case "blocked": native.Contact.Unobstructed = false; break;
                case "wrong-contact": native.Contact.CaseId++; break;
                case "contact-id": native.Contact.NativeId = "other"; break;
                case "far": native.Contact.Distance = 2; break;
                case "nan": native.Contact.Distance = float.NaN; break;
                case "old-contact": native.Contact.ContactAgeSeconds = 1; break;
                case "old-pose": native.Contact.PoseAgeSeconds = 1; break;
                case "negative-age": native.Contact.PoseAgeSeconds = -.1f; break;
                case "malformed-native": native.State.Remaining = -1; break;
                case "changed-native-id": native.State.NativeId = "other"; break;
                case "changed-capacity": native.State.Capacity++; break;
            }
            var before = PacketCodec.Encode(host.Snapshot()); int count = native.State.Remaining;
            Assert.False(host.TryAccept(r, 2, out var result)); Assert.Null(result);
            Assert.Equal(before, PacketCodec.Encode(host.Snapshot())); Assert.Equal(count, native.State.Remaining);
            Assert.Equal(0, native.Commits); Assert.Equal(0, native.ItemMutations); Assert.Equal(0, native.CargoMutations); Assert.Equal(17, native.OtherWorld);
        }
        [Fact]
        public void DeniedAndOutOfOrderSequencesStaySpentAfterConditionsImprove()
        {
            var host = Authority(out var native); var r = Request(host.Snapshot(), sequence: 4);
            native.Contact.ActorAlive = false; Assert.False(host.TryAccept(r, 2, out _));
            native.Contact.ActorAlive = true; Assert.False(host.TryAccept(r, 2, out _));
            r.Sequence = 3; Assert.False(host.TryAccept(r, 2, out _));
            r.Sequence = 5; Assert.True(host.TryAccept(r, 2, out _)); Assert.Equal(1, native.Commits);
        }
        [Fact]
        public void CompetingRequestsForLastBottleHaveExactlyOneWinner()
        {
            var native = new HostDouble { State = Initial(1) }; var host = new BeerCaseAuthority(native.State, native);
            host.Connect(2, 12); host.Connect(3, 13);
            var a = Request(host.Snapshot()); var b = Request(host.Snapshot(), 3, connection: 13);
            bool x = false, y = false;
            Parallel.Invoke(() => x = host.TryAccept(a, 2, out _), () => y = host.TryAccept(b, 3, out _));
            Assert.NotEqual(x, y); Assert.Equal(0, native.State.Remaining); Assert.Equal(1, native.Commits);
            Assert.False(host.TryAccept(Request(host.Snapshot(), 3, 2, 13), 3, out _));
        }
        [Fact]
        public void ReentrantAttemptIsSpentAndCannotCapturePartialCount()
        {
            var host = Authority(out var native); var reentrant = Request(host.Snapshot(), 3, 9, 13);
            native.DuringCommit = () => { Assert.Null(host.Capture()); Assert.False(host.TryAccept(reentrant, 3, out _)); };
            Assert.True(host.TryAccept(Request(host.Snapshot()), 2, out _)); native.DuringCommit = null;
            reentrant.ExpectedRevision = host.Snapshot().Revision; reentrant.ExpectedRemaining = 2;
            Assert.False(host.TryAccept(reentrant, 3, out _)); Assert.Equal(1, native.Commits);
        }
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void RevocationDuringReadCannotCommit(bool stop)
        {
            var host = Authority(out var native);
            native.DuringContact = () => { if (stop) host.Stop(); else host.Forget(2); };
            Assert.False(host.TryAccept(Request(host.Snapshot()), 2, out _)); Assert.Equal(0, native.Commits);
        }
        [Fact]
        public void NewConnectionRevokesOldPacketsAndNeverReusesToken()
        {
            var host = Authority(out var native); var old = Request(host.Snapshot());
            host.Forget(2); Assert.False(host.TryAccept(old, 2, out _)); Assert.False(host.Connect(2, 12)); Assert.True(host.Connect(2, 14));
            Assert.False(host.TryAccept(old, 2, out _)); old.Connection = 14; Assert.True(host.TryAccept(old, 2, out _));
        }
        [Theory]
        [InlineData("reject")] [InlineData("throw")] [InlineData("no-effect")]
        public void FailedNativeBoundaryNeverPublishesPredictedSuccess(string mode)
        {
            var host = Authority(out var native); var r = Request(host.Snapshot());
            native.Reject = mode == "reject"; native.Throw = mode == "throw"; native.NoEffect = mode == "no-effect";
            var before = PacketCodec.Encode(host.Snapshot());
            Assert.False(host.TryAccept(r, 2, out var result)); Assert.Null(result); Assert.Equal(before, PacketCodec.Encode(host.Snapshot()));
            Assert.Equal(mode != "reject", host.Faulted); Assert.False(host.TryAccept(r, 2, out _));
        }
        [Fact]
        public void CaptureObservesNativeChangesAndLateReplicaDoesNotReplayExtraction()
        {
            var host = Authority(out var native); Assert.True(host.TryAccept(Request(host.Snapshot()), 2, out _));
            native.State.Remaining = 0; native.State.Available = false;
            var snapshot = host.Capture()!; var late = new BeerCaseReplica(snapshot.CaseId, snapshot.NativeId, snapshot.Epoch);
            Assert.True(late.Apply(snapshot)); Assert.Equal(0, late.Snapshot()!.Remaining); Assert.Equal(255, snapshot.PlayerId);
            Assert.Equal(1, native.Commits); Assert.False(late.Apply(Initial()));
            snapshot.Remaining = 3; Assert.False(late.Apply(snapshot)); Assert.Equal(0, late.Snapshot()!.Remaining);
        }
        [Theory]
        [InlineData("negative")] [InlineData("overflow")] [InlineData("capacity")] [InlineData("case")]
        [InlineData("native")] [InlineData("epoch")] [InlineData("actor")] [InlineData("receipt")]
        public void MalformedUpdatesAreRejectedByWriterReaderAndReplica(string bad)
        {
            var s = Initial(); var replica = new BeerCaseReplica(s.CaseId, s.NativeId, s.Epoch); Assert.True(replica.Apply(s));
            var malformed = s.Copy(); malformed.Revision++;
            switch (bad)
            {
                case "negative": malformed.Remaining = -1; break;
                case "overflow": malformed.Remaining = 4; break;
                case "capacity": malformed.Capacity = 0; break;
                case "case": malformed.CaseId++; break;
                case "native": malformed.NativeId = ""; break;
                case "epoch": malformed.Epoch = 0; break;
                case "actor": malformed.PlayerId = 0; break;
                case "receipt": malformed.Sequence = 1; break;
            }
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(malformed)); Assert.False(replica.Apply(malformed));
            var w = new NetWriter(); w.WriteUInt16(266); w.WriteUInt32(malformed.CaseId); w.WriteString(malformed.NativeId);
            w.WriteUInt32(malformed.Epoch); w.WriteUInt32(malformed.Revision); w.WriteInt32(malformed.Capacity); w.WriteInt32(malformed.Remaining);
            w.WriteBool(malformed.Available); w.WriteByte(malformed.PlayerId); w.WriteUInt32(malformed.Connection); w.WriteUInt32(malformed.Sequence);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(w.ToArray()));
            Assert.Equal(PacketCodec.Encode(s), PacketCodec.Encode(replica.Snapshot()!));
        }
        [Theory]
        [InlineData(MessageId.BeerCaseExtractIntent, true)] [InlineData(MessageId.BeerCaseUpdate, false)]
        public void OnlyAuthenticatedDirectionOnOrderedChannelIsAllowed(MessageId id, bool intent)
        {
            Assert.True(SessionMessagePolicy.IsSenderAllowed(id, intent, true, !intent, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, !intent, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, intent, false, false, false));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableBulk));
        }

        [Fact]
        public void RequestAndSnapshotsCannotBeMutatedByReadCallbacksOrConsumers()
        {
            var host = Authority(out var native); var r = Request(host.Snapshot());
            native.DuringRead = () => { r.PlayerId = 3; r.Connection = 13; r.Sequence = 99; r.NativeId = "other"; };
            Assert.True(host.TryAccept(r, 2, out var accepted));
            Assert.Equal(2, accepted!.PlayerId); Assert.Equal(12u, accepted.Connection); Assert.Equal(1u, accepted.Sequence);
            accepted.Remaining = 0; var snap = host.Snapshot(); snap.Remaining = 0;
            Assert.Equal(2, host.Snapshot().Remaining); Assert.Equal(1, native.Commits);
        }

        [Fact]
        public void PacketTruncationTrailingDataAndNonBooleanAvailabilityAreRejected()
        {
            foreach (var message in new IMessage[] { Initial(), Request(Initial()) })
            {
                var bytes = PacketCodec.Encode(message);
                for (int length = 0; length < bytes.Length; length++)
                {
                    var shortPacket = new byte[length]; Array.Copy(bytes, shortPacket, length);
                    Assert.Throws<ProtocolException>(() => PacketCodec.Decode(shortPacket));
                }
                var extra = new byte[bytes.Length + 1]; Array.Copy(bytes, extra, bytes.Length);
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(extra));
            }
            var invalidBool = PacketCodec.Encode(Initial());
            invalidBool[invalidBool.Length - 10] = 2;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(invalidBool));
        }

        [Fact]
        public void CounterExhaustionAndMalformedInitialCapacityFailClosed()
        {
            var host = Authority(out var native); var r = Request(host.Snapshot(), sequence: uint.MaxValue);
            Assert.True(host.TryAccept(r, 2, out _)); r = Request(host.Snapshot()); Assert.False(host.TryAccept(r, 2, out _));
            var initial = Initial(); initial.Revision = uint.MaxValue;
            host = new BeerCaseAuthority(initial, native); host.Connect(2, 12);
            Assert.False(host.TryAccept(Request(initial), 2, out _)); Assert.Equal(1, native.Commits);
            initial.Capacity = 0; Assert.Throws<ArgumentException>(() => new BeerCaseAuthority(initial, native));
        }
    }
}
