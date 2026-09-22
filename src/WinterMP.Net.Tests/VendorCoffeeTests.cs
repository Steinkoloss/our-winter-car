using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;
using Xunit.Abstractions;

namespace WinterMP.Net.Tests
{
    // All quantities and lifecycle choices below are synthetic PORTABLE fixtures,
    // not extracted CoffeeAutomatic prices, capacities, identities or save behavior.
    public sealed class VendorCoffeeTests
    {
        private readonly ITestOutputHelper _output;
        public VendorCoffeeTests(ITestOutputHelper output) { _output = output; }
        internal static VendorCoffeeState Opening() => new VendorCoffeeState {
            MachineId = 41, CupId = 42, Epoch = 7, Generation = 1, Revision = 1,
            Lifecycle = VendorCoffeeLifecycle.Available };
        internal static VendorCoffeeIntent Request(VendorCoffeeState s, byte actor = 2, uint sequence = 1,
            VendorCoffeeAction action = VendorCoffeeAction.Acquire, uint connection = 12) => new VendorCoffeeIntent {
            MachineId = s.MachineId, CupId = s.CupId, Epoch = s.Epoch, Generation = s.Generation,
            ExpectedRevision = s.Revision, PlayerId = actor, Connection = connection, Sequence = sequence, Action = action };
        private static VendorCoffeeActor Actor(byte id = 2, uint connection = 12) => new VendorCoffeeActor(id, connection, true, true, true, true);
        private static VendorCoffeeRuntime Host(out Fixture adapter)
        {
            adapter = new Fixture();
            var authority = new VendorCoffeeAuthority(Opening(), 3.25f, adapter);
            authority.Connect(1, 11); authority.Connect(2, 12); authority.Connect(3, 13);
            return new VendorCoffeeRuntime(authority);
        }
        private static VendorCoffeeResult Accept(VendorCoffeeRuntime host, byte actor, uint connection, uint sequence, VendorCoffeeAction action)
        {
            var request = Request(host.Snapshot()!, actor, sequence, action, connection);
            Assert.True(host.OnIntent(request, Actor(actor, connection), out var result));
            return Assert.IsType<VendorCoffeeResult>(result);
        }

        [Fact]
        public void AuthenticatedHostAndGuestUseIdenticalOnceOnlyTransactionAndReplicaPath()
        {
            byte[][] finalStates = new byte[2][];
            for (byte actor = 1; actor <= 2; actor++)
            {
                uint connection = actor == 1 ? 11u : 12u;
                var host = Host(out var native);
                var replica = new VendorCoffeeReplica(41, 42, 7, actor, connection);
                var guest = new VendorCoffeeRuntime(replica);
                Assert.True(guest.OnState(host.Snapshot()!));
                uint sequence = 0;
                foreach (var action in new[] { VendorCoffeeAction.Acquire, VendorCoffeeAction.Purchase, VendorCoffeeAction.Fill, VendorCoffeeAction.Drink })
                {
                    var request = Request(host.Snapshot()!, actor, ++sequence, action, connection);
                    Assert.True(replica.Expect(request));
                    Assert.True(host.OnIntent(request, Actor(actor, connection), out var receipt));
                    var state = Assert.IsType<VendorCoffeeState>(PacketCodec.Decode(PacketCodec.Encode(host.Snapshot()!)));
                    Assert.True(guest.OnState(state));
                    Assert.Equal(PacketCodec.Encode(state), PacketCodec.Encode(guest.Snapshot()!));
                    var result = Assert.IsType<VendorCoffeeResult>(PacketCodec.Decode(PacketCodec.Encode(receipt!)));
                    Assert.Equal(action == VendorCoffeeAction.Drink, guest.OnResult(result));
                    Assert.False(guest.OnResult(result));
                    var before = PacketCodec.Encode(host.Snapshot()!);
                    Assert.False(host.OnIntent(request, Actor(actor, connection), out _));
                    Assert.Equal(before, PacketCodec.Encode(host.Snapshot()!));
                }
                Assert.Equal(16.75f, native.Wallet); Assert.Equal(1, native.Debits);
                Assert.Equal(1, native.Outputs); Assert.Equal(1, native.Drinks); Assert.Equal(4, native.Commits);
                _output.WriteLine("PORTABLE synthetic actor={0} connection={1}: wallet={2}, debits={3}, outputs={4}, drinks={5}, commits={6}; peers byte-identical; duplicate requests/results rejected.",
                    actor, connection, native.Wallet, native.Debits, native.Outputs, native.Drinks, native.Commits);
                var final = host.Snapshot()!; final.Holder = 0;
                finalStates[actor - 1] = PacketCodec.Encode(final);
            }
            Assert.Equal(finalStates[0], finalStates[1]);
        }

        [Theory]
        [InlineData("spoof")] [InlineData("connection")] [InlineData("unauthenticated")]
        [InlineData("dead")] [InlineData("pose")] [InlineData("range")]
        [InlineData("epoch")] [InlineData("cup")] [InlineData("machine")]
        [InlineData("generation")] [InlineData("revision")] [InlineData("action")]
        public void InvalidRequestsCannotPrepareOrMutate(string bad)
        {
            var host = Host(out var native); var request = Request(host.Snapshot()!); var actor = Actor();
            switch (bad)
            {
                case "spoof": request.PlayerId = 3; break;
                case "connection": request.Connection++; break;
                case "unauthenticated": actor = new VendorCoffeeActor(2, 12, false, true, true, true); break;
                case "dead": actor = new VendorCoffeeActor(2, 12, true, false, true, true); break;
                case "pose": actor = new VendorCoffeeActor(2, 12, true, true, false, true); break;
                case "range": actor = new VendorCoffeeActor(2, 12, true, true, true, false); break;
                case "epoch": request.Epoch++; break;
                case "cup": request.CupId++; break;
                case "machine": request.MachineId++; break;
                case "generation": request.Generation++; break;
                case "revision": request.ExpectedRevision++; break;
                case "action": request.Action = (VendorCoffeeAction)99; break;
            }
            var before = PacketCodec.Encode(host.Snapshot()!);
            Assert.False(host.OnIntent(request, actor, out var receipt)); Assert.Null(receipt);
            Assert.Equal(before, PacketCodec.Encode(host.Snapshot()!)); Assert.Equal(0, native.Prepares); Assert.Equal(0, native.Commits);
        }

        [Theory]
        [InlineData("noeffect")] [InlineData("reject")] [InlineData("commit")] [InlineData("price")] [InlineData("doubleoutput")]
        public void FailedOrInvalidNativePlansDoNotChangeWalletCupOrRevisionAndCannotBeRetried(string mode)
        {
            var host = Host(out var native);
            if (mode == "price") Accept(host, 2, 12, 1, VendorCoffeeAction.Acquire);
            native.Mode = mode;
            var before = PacketCodec.Encode(host.Snapshot()!); int commits = native.Commits;
            var r = Request(host.Snapshot()!, sequence: 2, action: mode == "price" ? VendorCoffeeAction.Purchase : VendorCoffeeAction.Acquire);
            Assert.False(host.OnIntent(r, Actor(), out _));
            Assert.Equal(before, PacketCodec.Encode(host.Snapshot()!)); Assert.Equal(commits, native.Commits); Assert.Equal(20, native.Wallet);
            native.Mode = "normal";
            Assert.False(host.OnIntent(r, Actor(), out _));
        }

        [Fact]
        public void FreshSequencesCannotRepeatAnAcceptedActionAndLowFundsCannotBecomeDelayedPurchase()
        {
            var host = Host(out var native);
            Accept(host, 2, 12, 1, VendorCoffeeAction.Acquire);
            native.Wallet = 1;
            var poor = Request(host.Snapshot()!, sequence: 3, action: VendorCoffeeAction.Purchase);
            var before = PacketCodec.Encode(host.Snapshot()!);
            Assert.False(host.OnIntent(poor, Actor(), out _));
            Assert.Equal(1, native.Wallet); Assert.Equal(before, PacketCodec.Encode(host.Snapshot()!));
            native.Wallet = 20;
            Assert.False(host.OnIntent(poor, Actor(), out _));
            var older = poor.Copy(); older.Sequence = 2;
            Assert.False(host.OnIntent(older, Actor(), out _));
            Accept(host, 2, 12, 4, VendorCoffeeAction.Purchase);
            before = PacketCodec.Encode(host.Snapshot()!);
            var repeat = Request(host.Snapshot()!, sequence: 5, action: VendorCoffeeAction.Purchase);
            Assert.False(host.OnIntent(repeat, Actor(), out _));
            Assert.Equal(before, PacketCodec.Encode(host.Snapshot()!)); Assert.Equal(1, native.Debits); Assert.Equal(1, native.Outputs);
        }

        [Fact]
        public void DisconnectDuringReadOnlyPreparationRevokesTheCommit()
        {
            var host = Host(out var native);
            native.DuringPrepare = () => host.Forget(2);
            var before = PacketCodec.Encode(host.Snapshot()!);
            Assert.False(host.OnIntent(Request(host.Snapshot()!), Actor(), out _));
            Assert.Equal(before, PacketCodec.Encode(host.Snapshot()!)); Assert.Equal(0, native.Commits); Assert.Equal(0, native.Outputs);
        }

        [Fact]
        public void TeardownDuringReadOnlyPreparationCannotCommitAnOrphanedAuthority()
        {
            var host = Host(out var native);
            native.DuringPrepare = host.Clear;
            Assert.False(host.OnIntent(Request(host.Snapshot()!), Actor(), out _));
            Assert.Null(host.Snapshot()); Assert.Equal(0, native.Commits); Assert.Equal(0, native.Outputs);
        }

        [Fact]
        public void SameCupConflictStaleRequestAndReentrantCallbackAreSerialized()
        {
            var host = Host(out var native); var stale = Request(host.Snapshot()!, 3, 1, connection: 13);
            native.DuringPrepare = () => Assert.False(host.OnIntent(stale, Actor(3, 13), out _));
            Accept(host, 2, 12, 1, VendorCoffeeAction.Acquire); native.DuringPrepare = null;
            Assert.False(host.OnIntent(stale, Actor(3, 13), out _));
            var steal = Request(host.Snapshot()!, 3, 2, VendorCoffeeAction.Purchase, 13);
            Assert.False(host.OnIntent(steal, Actor(3, 13), out _));
            Assert.Equal(1, native.Outputs); Assert.Equal(0, native.Debits); Assert.Equal(1, native.Commits);
        }

        [Fact]
        public void DisconnectRejoinRetirementAndTimeoutCannotResurrectOrReplayEffects()
        {
            var host = Host(out var native);
            Accept(host, 2, 12, 1, VendorCoffeeAction.Acquire);
            Accept(host, 2, 12, 2, VendorCoffeeAction.Purchase);
            Accept(host, 2, 12, 3, VendorCoffeeAction.Fill);
            var active = host.Snapshot()!;
            var drink = Request(active, sequence: 4, action: VendorCoffeeAction.Drink);
            var replica = new VendorCoffeeReplica(41, 42, 7, 2, 12);
            Assert.True(replica.Apply(active)); Assert.True(replica.Expect(drink));
            Assert.True(host.OnIntent(drink, Actor(), out var result));
            var retired = host.Snapshot()!;
            Assert.True(replica.Apply(retired)); replica.ExpirePending(); Assert.False(replica.TakeDrink(result!));
            Assert.False(replica.Apply(active)); active.Revision = retired.Revision + 1; Assert.False(replica.Apply(active));
            Assert.Equal(VendorCoffeeLifecycle.Retired, replica.Snapshot()!.Lifecycle);
            host.Forget(2); Assert.False(host.OnIntent(drink, Actor(), out _));
            Assert.True(host.Connect(2, 22)); Assert.False(host.Connect(2, 12));
            Assert.False(host.OnIntent(drink, Actor(2, 22), out _));
            var rejoin = new VendorCoffeeReplica(41, 42, 7, 2, 22);
            Assert.True(rejoin.Apply(retired)); Assert.False(rejoin.TakeDrink(result!));
            var oldEpoch = host.Snapshot()!; oldEpoch.Epoch = 6; Assert.False(rejoin.Apply(oldEpoch));
            var next = host.Snapshot()!; next.Generation++; next.Revision++; next.Lifecycle = VendorCoffeeLifecycle.Available;
            Assert.True(rejoin.Apply(next)); retired.Revision = next.Revision + 1; Assert.False(rejoin.Apply(retired));
            Assert.Equal(1, native.Drinks);
        }

        [Fact]
        public void DisabledRuntimeNeverAcceptsIntentReplicaOrSnapshot()
        {
            var runtime = new VendorCoffeeRuntime();
            Assert.False(runtime.Enabled); Assert.Null(runtime.Snapshot());
            Assert.False(runtime.OnIntent(Request(Opening()), Actor(), out _));
            Assert.False(runtime.OnState(Opening())); Assert.False(runtime.OnResult(new VendorCoffeeResult()));
            Assert.False(runtime.Connect(2, 12)); runtime.Forget(2); runtime.Clear();
        }

        [Fact]
        public void WireRegistryRolesChannelsAndMalformedPayloadsAreDistinctFromHousehold()
        {
            var host = Host(out _); var result = Accept(host, 2, 12, 1, VendorCoffeeAction.Acquire);
            IMessage[] messages = { Request(Opening()), host.Snapshot()!, result };
            foreach (var message in messages)
            {
                Assert.True((ushort)message.Id > 261);
                var bytes = PacketCodec.Encode(message); Assert.Equal(bytes, PacketCodec.Encode(PacketCodec.Decode(bytes)));
                for (int n = 0; n < bytes.Length; n++) Assert.Throws<ProtocolException>(() => PacketCodec.Decode(new ArraySegment<byte>(bytes, 0, n).ToArray()));
                Assert.True(SessionMessagePolicy.IsChannelAllowed(message.Id, Channel.ReliableOrdered));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(message.Id, Channel.ReliableBulk));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(message.Id, Channel.UnreliableSequenced));
                bool intent = message is VendorCoffeeIntent;
                Assert.Equal(intent, SessionMessagePolicy.IsSenderAllowed(message.Id, true, true, false, true));
                Assert.Equal(!intent, SessionMessagePolicy.IsSenderAllowed(message.Id, false, true, true, true));
                Assert.False(SessionMessagePolicy.IsSenderAllowed(message.Id, true, false, false, true));
                Assert.False(SessionMessagePolicy.IsSenderAllowed(message.Id, false, true, false, true));
                Assert.False(SessionMessagePolicy.IsSenderAllowed(message.Id, false, true, true, false));
            }
            Assert.Equal(245, (ushort)MessageId.CoffeeIntent); Assert.Equal(246, (ushort)MessageId.CoffeeState); Assert.Equal(247, (ushort)MessageId.CoffeeDrinkResult);
            var invalid = Opening(); invalid.Contents = float.NaN; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(invalid));
            invalid = Opening(); invalid.Lifecycle = VendorCoffeeLifecycle.Retired; invalid.Contents = 1; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(invalid));
        }

        [Fact]
        public void CatalogIsExplicitlyUnresolvedAndCannotEnableWithInventedNativeValues()
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.VendorCoffeeError); Assert.NotNull(parsed.VendorCoffee);
            Assert.False(parsed.VendorCoffee!.RuntimeEnabled); Assert.Equal(8, parsed.VendorCoffee.MissingNativeFields.Length);
            _output.WriteLine("ACTUAL CATALOG schema 1 runtime DISABLED; missing serialized native fields: " + string.Join(", ", parsed.VendorCoffee.MissingNativeFields));
            Assert.NotNull(parsed.Coffee); Assert.Contains(parsed.Buys, b => b.Template == "shopBuy");
            foreach (string field in parsed.VendorCoffee.MissingNativeFields)
            {
                var changed = json.DeepClone(); changed["vendorCoffee"]!["machines"]![0]!["native"]![field] = "guessed";
                var bad = SyncCatalogJson.Parse(changed.ToJsonString()); Assert.Null(bad.VendorCoffee); Assert.NotNull(bad.VendorCoffeeError); Assert.NotNull(bad.Coffee);
            }
            foreach (string key in new[] { "root", "roles", "native" })
            {
                var changed = json.DeepClone(); changed["vendorCoffee"]!["machines"]![0]!.AsObject().Remove(key);
                Assert.NotNull(SyncCatalogJson.Parse(changed.ToJsonString()).VendorCoffeeError);
            }
            json["vendorCoffee"]!["schemaVersion"] = 2;
            Assert.NotNull(SyncCatalogJson.Parse(json.ToJsonString()).VendorCoffeeError);
            Assert.True(VendorCoffeePolicy.QuarantineBuy("INSPECTION/LOD/CoffeeAutomatic/Functions/CoffeeButton", "Buy"));
            Assert.False(VendorCoffeePolicy.QuarantineBuy("JOBS/FACTORY/OpeningTimes/LOD1/Kitchen/CoffeeAutomatic/Functions/CoffeeButton", "Buy"));
            Assert.False(VendorCoffeePolicy.QuarantineBuy("INSPECTION/LOD/CoffeeAutomatic2/Functions/CoffeeButton", "Buy"));
            Assert.False(VendorCoffeePolicy.QuarantineBuy("INSPECTION/LOD/CoffeeAutomatic/Functions/CoffeeButton", "Use"));
        }

        private sealed class Fixture : IVendorCoffeeAdapter
        {
            public float Wallet = 20;
            public int Debits, Outputs, Drinks, Commits, Prepares;
            public string Mode = "normal";
            public Action? DuringPrepare;
            public bool TryPrepare(VendorCoffeeState before, VendorCoffeeIntent request, out VendorCoffeeMutation? mutation)
            {
                Prepares++; DuringPrepare?.Invoke(); mutation = null;
                if (Mode == "reject") return false;
                var after = before.Copy(); float debit = 0; byte outputs = 0, drinks = 0;
                if (Mode != "noeffect")
                    switch (request.Action)
                    {
                        case VendorCoffeeAction.Acquire: after.Lifecycle = VendorCoffeeLifecycle.Active; after.Holder = request.PlayerId; after.HolderConnection = request.Connection; outputs = 1; break;
                        case VendorCoffeeAction.Purchase: debit = Mode == "price" ? 999 : 3.25f; break;
                        case VendorCoffeeAction.Fill: after.Contents = 6; break;
                        case VendorCoffeeAction.Drink: after.Contents = 0; after.Lifecycle = VendorCoffeeLifecycle.Retired; after.Holder = 0; after.HolderConnection = 0; drinks = 1; break;
                    }
                if (Mode == "doubleoutput") outputs = 2;
                mutation = new VendorCoffeeMutation(after, debit, outputs, drinks, () => {
                    if (Mode == "commit" || Wallet < debit) return false;
                    Wallet -= debit; if (debit > 0) Debits++; Outputs += outputs; Drinks += drinks; Commits++; return true;
                });
                return true;
            }
        }
    }
}
