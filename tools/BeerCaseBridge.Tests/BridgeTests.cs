using System;
using System.Collections.Generic;
using System.Linq;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace WinterMP.Core.Session
{
    // Transport/session double only. Production admission policy and codec are real.
    internal sealed class SessionManager
    {
        internal static SessionManager? Instance;
        internal bool IsHost;
        internal byte LocalPlayerId;
        internal readonly Queue<byte[]> Sent = new Queue<byte[]>();
        internal void SendWorldMessage(IMessage message, Channel channel)
        {
            Assert.Equal(Channel.ReliableOrdered, channel);
            Assert.True(SessionMessagePolicy.IsChannelAllowed(message.Id, channel));
            Sent.Enqueue(PacketCodec.Encode(message));
        }
    }
}
namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        internal void TeardownDouble() { ClearBeerCase(); }
    }
}
namespace BeerCaseBridge.Tests
{
    public sealed class BridgeTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        public BridgeTests(ITestOutputHelper output) { _output = output; }
        public void Dispose() { SessionManager.Instance = null; }
        private sealed class NativeDouble : IBeerCaseHost
        {
            internal readonly BeerCaseUpdate State = new BeerCaseUpdate { CaseId = BeerCasePolicy.CaseId("portable-case"), NativeId = "portable-case",
                Epoch = 21, Revision = 1, Capacity = 3, Remaining = 3, Available = true };
            internal int Extracts, ItemMutations = 0, CargoMutations = 0, RelativeEvents = 0, SaveWrites = 0;
            internal bool Contact = true;
            public BeerCaseUpdate Read() => State.Copy();
            public BeerCaseContact ReadContact(byte actor) => new BeerCaseContact { ActorPresent = true, ActorAlive = true,
                Unobstructed = Contact, CaseId = State.CaseId, NativeId = State.NativeId, Distance = .4f, PoseAgeSeconds = .1f, ContactAgeSeconds = .1f };
            public bool TryExtractOne(BeerCaseUpdate expected, byte actor)
            {
                if (!State.Available || State.Remaining != expected.Remaining || State.Remaining == 0) return false;
                State.Remaining--; Extracts++; return true;
            }
        }
        private static readonly SessionManager HostSession = new SessionManager { IsHost = true, LocalPlayerId = 1 };
        private static readonly SessionManager GuestSession = new SessionManager { IsHost = false, LocalPlayerId = 2 };
        private static ItemWorldSync Host(out NativeDouble native)
        {
            HostSession.Sent.Clear(); GuestSession.Sent.Clear(); SessionManager.Instance = HostSession;
            var h = new ItemWorldSync(); native = new NativeDouble();
            Assert.True(h.BindBeerCaseHost(native.State, native)); Assert.True(h.AdmitBeerCasePlayer(1, 11)); Assert.True(h.AdmitBeerCasePlayer(2, 12)); return h;
        }
        private static ItemWorldSync Guest(NativeDouble native, Action<BeerCaseUpdate> view)
        {
            SessionManager.Instance = GuestSession; var guest = new ItemWorldSync();
            Assert.True(guest.BindBeerCaseGuest(native.State.CaseId, native.State.NativeId, native.State.Epoch, 12, view));
            Assert.True(guest.OnBeerCaseUpdate(native.State)); return guest;
        }
        [Fact]
        public void GuestInputUsesProductionRequestAuthorityAbsoluteResultAndViewWithoutOwnershipOrOptimisticMutation()
        {
            var host = Host(out var native); BeerCaseUpdate? view = null; int views = 0;
            var guest = Guest(native, s => { view = s; views++; });
            Assert.True(guest.RequestBeerCaseExtraction()); Assert.Equal(3, native.State.Remaining); Assert.Equal(3, view!.Remaining); Assert.Equal(0, native.Extracts);
            var intent = Assert.IsType<BeerCaseExtractIntent>(PacketCodec.Decode(GuestSession.Sent.Dequeue()));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(intent.Id, true, true, false, true));
            SessionManager.Instance = HostSession;
            Assert.True(host.OnBeerCaseExtract(intent, 2)); Assert.Equal(2, native.State.Remaining); Assert.Equal(1, native.Extracts);
            Assert.False(host.OnBeerCaseExtract(intent, 2)); Assert.Single(HostSession.Sent);
            var result = Assert.IsType<BeerCaseUpdate>(PacketCodec.Decode(HostSession.Sent.Dequeue()));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(result.Id, false, false, true, true));
            SessionManager.Instance = GuestSession; Assert.True(guest.OnBeerCaseUpdate(result)); Assert.False(guest.OnBeerCaseUpdate(result));
            Assert.Equal(2, view.Remaining); Assert.Equal(native.State.NativeId, view.NativeId); Assert.Equal(native.State.CaseId, view.CaseId); Assert.Equal(2, views);
            SessionManager.Instance = HostSession; Assert.Equal(view.Remaining, host.BuildBeerCaseStates().Single().Remaining);
            Assert.Equal(0, native.ItemMutations + native.CargoMutations + native.RelativeEvents + native.SaveWrites);
            _output.WriteLine("PORTABLE production ItemWorldSync partial: guest actor=2 (non-owner), host actor=1; count 3->2; host extracts=1; guest absolute views=2 (initial+accepted); duplicate input/result rejected; stable case ID preserved. Native/session are doubles, not gameplay evidence.");
        }
        [Fact]
        public void HostLocalInputUsesTheSameAuthority()
        {
            var host = Host(out var native); Assert.True(host.RequestBeerCaseExtraction());
            Assert.Equal(1, native.Extracts); Assert.Equal(2, native.State.Remaining);
            var result = Assert.IsType<BeerCaseUpdate>(PacketCodec.Decode(HostSession.Sent.Dequeue())); Assert.Equal(1, result.PlayerId);
        }
        [Fact]
        public void WrongAuthenticatedActorCannotMutateOrBroadcast()
        {
            var host = Host(out var native); var guest = Guest(native, _ => { }); Assert.True(guest.RequestBeerCaseExtraction());
            var intent = (BeerCaseExtractIntent)PacketCodec.Decode(GuestSession.Sent.Dequeue()); SessionManager.Instance = HostSession;
            Assert.False(host.OnBeerCaseExtract(intent, 1)); Assert.Empty(HostSession.Sent); Assert.Equal(3, native.State.Remaining); Assert.Equal(0, native.Extracts);
        }
        [Fact]
        public void UnboundBridgeAndTeardownDoNotSilentlyExecuteLocalExtraction()
        {
            SessionManager.Instance = GuestSession; var cold = new ItemWorldSync(); Assert.False(cold.RequestBeerCaseExtraction());
            var host = Host(out var native); host.TeardownDouble(); Assert.False(host.RequestBeerCaseExtraction()); Assert.Empty(host.BuildBeerCaseStates());
            var guest = Guest(native, _ => { }); guest.TeardownDouble(); Assert.False(guest.RequestBeerCaseExtraction()); Assert.False(guest.OnBeerCaseUpdate(native.State));
            Assert.Equal(0, native.Extracts);
        }
        [Fact]
        public void ViewFailureDisablesOnlyBeerCaseBridgeWithoutReplayingExtraction()
        {
            var host = Host(out var native); SessionManager.Instance = GuestSession; var guest = new ItemWorldSync();
            Assert.True(guest.BindBeerCaseGuest(native.State.CaseId, native.State.NativeId, native.State.Epoch, 12, _ => throw new InvalidOperationException()));
            Assert.False(guest.OnBeerCaseUpdate(native.State)); Assert.False(guest.RequestBeerCaseExtraction()); Assert.Equal(0, native.Extracts);
        }
        [Fact]
        public void SnapshotAfterAcceptanceHasNoActorActionCorrelation()
        {
            var host = Host(out var native); Assert.True(host.RequestBeerCaseExtraction());
            var snapshot = host.BuildBeerCaseStates().Single();
            Assert.Equal(255, snapshot.PlayerId); Assert.Equal(0u, snapshot.Connection); Assert.Equal(0u, snapshot.Sequence);
            Assert.Equal(2, snapshot.Remaining); Assert.Equal(1, native.Extracts);
        }
    }
}
