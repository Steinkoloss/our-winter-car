using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WoodstoveGameplay.Tests
{
    public sealed class CabinTests
    {
        private const uint Log = 51;
        private sealed class Rig : IDisposable
        {
            internal readonly SessionManager Session;
            internal readonly HeatSourceSync Sync = new HeatSourceSync();
            internal readonly WorldSyncManager World = new WorldSyncManager();
            internal readonly PlayerSyncManager Player = new PlayerSyncManager();
            internal readonly Dictionary<string, GameObject> Scene;
            internal readonly PlayMakerFSM Trigger, Fire;
            internal readonly FsmInt Woods = new FsmInt { Name = "Woods", Value = 1 };
            internal readonly FsmInt Cache = new FsmInt { Name = "Woods" };
            internal readonly FsmFloat Heat = new FsmFloat { Name = "HeatingEfficiency", Value = .18f };
            internal readonly FsmBool Lit = new FsmBool { Name = "Hiillos", Value = true };
            internal readonly FsmGameObject Collider = new FsmGameObject { Name = "Collider" };
            internal readonly GameObject Piece;
            internal readonly SyncedItem Item;
            internal readonly DestroyObject Destroy;
            internal readonly IntAdd Add;
            internal readonly RemotePlayer Guest = new RemotePlayer { PlayerId = 2 };
            internal Rig(bool host)
            {
                GameObject.Scene.Clear();
                Session = new SessionManager { IsHost = host, LocalPlayerId = host ? (byte)0 : (byte)2 };
                Session.Players.Add(host ? Guest : new RemotePlayer { PlayerId = 0 });
                Player.Feet = new Vector3(host ? 50 : 0, 0, 0); // host is away, never an ownership prerequisite
                var root = new GameObject(WoodstoveFuelAuthority.CabinPath);
                Trigger = Child(root, "WoodTrigger").Add(new PlayMakerFSM { FsmName = "Trigger" });
                Trigger.gameObject.AddComponent<BoxCollider>();
                Fire = Child(root, "SetFire").Add(new PlayMakerFSM { FsmName = "Use" });
                Trigger.FsmVariables.Vars["Woods"] = Woods;
                Trigger.FsmVariables.Vars["Collider"] = Collider;
                Trigger.FsmVariables.Vars["Parent"] = new FsmGameObject { Name = "Parent" };
                Fire.FsmVariables.Vars["Woods"] = Cache;
                Fire.FsmVariables.Vars["HeatingEfficiency"] = Heat;
                Fire.FsmVariables.Vars["Hiillos"] = Lit;
                Destroy = new DestroyObject { gameObject = Collider };
                Add = new IntAdd { intVariable = Woods };
                Trigger.Fsm.States = new[] {
                    State("Wait wood"), State("State 1", new IntCompare { integer1 = Woods }),
                    State("Destroy firewood", new GetRandomChild(), new ActivateGameObject(), new ActivateGameObject(), Destroy, Add,
                        new IntCompare(), new IntCompare(), new IntCompare(), new IntCompare()) };
                Fire.Fsm.States = new[] { State("Remove wood", new IntAdd { intVariable = Cache, add = new FsmInt { Value = -1 } },
                    new SetFsmInt { Write = () => Woods.Value = Cache.Value }), State("Start fire 2") };
                for (int i = 1; i <= 4; i++) Child(root, "Woods/log" + i);
                Piece = new GameObject("firewood(Clone)");
                Item = new SyncedItem { Body = Piece.AddComponent<Rigidbody>(), RemoteOwner = 255,
                    LastRemoteSequenceOwner = 2, LastRemoteAt = Time.unscaledTime, LastRemoteReleaseAt = Time.unscaledTime };
                Piece.AddComponent<BoxCollider>();
                Scene = new Dictionary<string, GameObject>(GameObject.Scene);
                Activate(); Sync.Update(Session);
                Assert.False(Sync.Test.Faulted);
                Assert.False(Destroy.Enabled); // actual BindCabin signature and suppression ran
                Sync.AdmitTestPiece(Log, Piece, Item);
                Sync.ForceBroadcast(); Sync.Update(Session);
            }
            private static FsmState State(string name, params FsmStateAction[] actions) => new FsmState { Name = name, Actions = actions };
            private static GameObject Child(GameObject root, string name)
            {
                var child = new GameObject(name); root.transform.Children[name] = child.transform; return child;
            }
            internal void Activate()
            {
                SessionManager.Instance = Session; WorldSyncManager.Instance = World; PlayerSyncManager.Instance = Player;
                DeathSyncManager.Instance = null;
                GameObject.Scene.Clear(); foreach (var pair in Scene) GameObject.Scene[pair.Key] = pair.Value;
            }
            internal void Contact()
            {
                Activate();
                var sensor = Trigger.GetComponent<CabinWoodContact>();
                typeof(CabinWoodContact).GetMethod("OnTriggerStay", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(sensor, new object[] { Piece.GetComponent<Collider>() });
                Collider.Value = Piece;
                FsmHook.FindState(Trigger, "State 1")!.Enter();
            }
            internal void Tick() { Activate(); Sync.Update(Session); }
            internal WoodstoveFuelUpdate Request(WoodstoveFeedIntent intent, byte actor = 2)
            {
                Activate(); Sync.OnCabinIntent(Assert.IsType<WoodstoveFeedIntent>(PacketCodec.Decode(PacketCodec.Encode(intent))), actor);
                return Session.Sent.OfType<WoodstoveFuelUpdate>().Last(x => x.IsDecision);
            }
            internal void Receive(IEnumerable<IMessage> messages)
            {
                Activate(); foreach (var m in messages.OfType<WoodstoveFuelUpdate>()) Sync.OnCabinUpdate(m);
            }
            public void Dispose() { Activate(); Sync.Clear(); }
        }
        private static void Reset()
        { Time.unscaledTime = 10; Time.frameCount = 10; UnityEngine.Object.Reset(); }
        private static WoodstoveFeedIntent Intent(Rig host, uint sequence = 1, uint resource = Log) => new WoodstoveFeedIntent {
            Actor = 2, SourceId = WoodstoveFuelAuthority.CabinSourceId, Epoch = host.Sync.Test.Epoch, Sequence = sequence, ResourceId = resource };
        private static WoodstoveFuelUpdate Finish(Rig host)
        {
            UnityEngine.Object.CompleteDestruction(); Time.frameCount++; host.Tick();
            return host.Session.Sent.OfType<WoodstoveFuelUpdate>().Last(x => x.IsDecision);
        }
        private static void Agree(Rig host, Rig guest)
        {
            Assert.Equal(host.Woods.Value, guest.Woods.Value); Assert.Equal(host.Woods.Value, guest.Cache.Value);
            Assert.Equal(host.Heat.Value, guest.Heat.Value); Assert.Equal(host.Lit.Value, guest.Lit.Value);
            Assert.Equal(new uint[] { Log }, guest.Sync.Test.Client!.Current!.ConsumedResources);
            Assert.Null(guest.Sync.Test.Client.Create(Log));
            for (int i = 1; i <= 4; i++)
                Assert.Equal(i <= host.Woods.Value, guest.Scene[WoodstoveFuelAuthority.CabinPath].transform.Find("Woods/log" + i)!.gameObject.activeSelf);
        }

        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void ContactUsesOneHostAdapterAndAbsoluteGuestResultEvenWhenPendingRequestIsDuplicated(bool duplicate)
        {
            Reset(); using var host = new Rig(true); using var guest = new Rig(false);
            guest.Receive(host.Session.Sent); Assert.Equal(1, guest.Woods.Value);
            guest.Contact(); guest.Tick();
            var intent = Assert.Single(guest.Session.Sent.OfType<WoodstoveFeedIntent>());
            Assert.Equal((byte)2, intent.Actor); Assert.Equal(Log, intent.ResourceId);
            Assert.Equal(0, guest.Destroy.Calls); Assert.Equal(1, guest.Woods.Value); Assert.False(guest.Piece.Destroyed);
            host.Contact(); // host's local contact is not permission to impersonate nearby guest
            var pending = host.Request(intent);
            Assert.Equal(WoodstoveFeedStatus.Pending, pending.Status); Assert.Null(pending.Snapshot);
            guest.Receive(new[] { pending });
            if (duplicate)
            {
                var replay = host.Request(intent); Assert.NotEqual(WoodstoveFeedStatus.Accepted, replay.Status);
                Assert.Null(replay.Snapshot); guest.Receive(new[] { replay });
                Assert.Null(guest.Sync.Test.Client!.Create(Log)); // must retain the in-flight native reservation
            }
            Assert.Equal(1, host.Destroy.Calls); Assert.Single(host.Destroy.Retired);
            Assert.Same(host.Piece, host.Destroy.Retired[0]); Assert.Equal(2, host.Woods.Value); Assert.Equal(0, host.Cache.Value);
            Assert.False(host.Destroy.Enabled); Assert.Equal(0, guest.Destroy.Calls);
            var final = Finish(host);
            Assert.Equal(WoodstoveFeedStatus.Accepted, final.Status); Assert.Equal(intent.Sequence, final.Sequence);
            Assert.Single(host.Session.Sent.OfType<WoodstoveFuelUpdate>(), x => x.IsDecision && x.Status == WoodstoveFeedStatus.Accepted);
            guest.Receive(new[] { final }); Agree(host, guest);
            Assert.Equal(WoodstoveFeedStatus.Accepted, guest.Sync.Test.Client!.LastDecision!.Status);
            var again = host.Request(intent); Assert.Equal(WoodstoveFeedStatus.ReplayedSequence, again.Status);
            Assert.Equal(1, host.Destroy.Calls); guest.Receive(new[] { again }); Agree(host, guest);
            UnityEngine.Object.CompleteDestruction(); Assert.True(guest.Piece.Destroyed);
        }
        [Fact]
        public void LocalHostContactUsesTheSameAuthorityWithoutVehicleOwnership()
        {
            Reset(); using var host = new Rig(true); host.Player.Feet = Vector3.zero;
            host.Item.LastRemoteSequenceOwner = 255; host.Contact(); host.Tick();
            var pending = host.Session.Sent.OfType<WoodstoveFuelUpdate>().Last(x => x.IsDecision);
            Assert.Equal((byte)0, pending.Actor); Assert.Equal(WoodstoveFeedStatus.Pending, pending.Status);
            Assert.Equal(1, host.Destroy.Calls); Assert.Equal(WoodstoveFeedStatus.Accepted, Finish(host).Status);
            Assert.Equal(1, host.Destroy.Calls);
        }
        [Theory]
        [InlineData("actor", WoodstoveFeedStatus.InvalidActor)]
        [InlineData("epoch", WoodstoveFeedStatus.StaleEpoch)]
        [InlineData("sequence", WoodstoveFeedStatus.ReplayedSequence)]
        [InlineData("source", WoodstoveFeedStatus.WrongSource)]
        [InlineData("resource", WoodstoveFeedStatus.InvalidResource)]
        [InlineData("missingActor", WoodstoveFeedStatus.ActorUnavailable)]
        [InlineData("dead", WoodstoveFeedStatus.ActorUnavailable)]
        [InlineData("oldPose", WoodstoveFeedStatus.ActorUnavailable)]
        [InlineData("range", WoodstoveFeedStatus.OutOfRange)]
        [InlineData("inactive", WoodstoveFeedStatus.SourceUnavailable)]
        [InlineData("full", WoodstoveFeedStatus.SourceUnavailable)]
        [InlineData("contact", WoodstoveFeedStatus.InvalidContact)]
        [InlineData("bounds", WoodstoveFeedStatus.InvalidContact)]
        [InlineData("parent", WoodstoveFeedStatus.InvalidResource)]
        [InlineData("tag", WoodstoveFeedStatus.InvalidResource)]
        [InlineData("name", WoodstoveFeedStatus.InvalidResource)]
        [InlineData("heldByOther", WoodstoveFeedStatus.InvalidResource)]
        [InlineData("oldRelease", WoodstoveFeedStatus.InvalidResource)]
        public void HostObservedInvalidRequestsNeverReachNativeMutation(string kind, WoodstoveFeedStatus expected)
        {
            Reset(); using var host = new Rig(true); var intent = Intent(host); byte authenticated = 2;
            host.Contact();
            switch (kind)
            {
                case "actor": authenticated = 3; break;
                case "epoch": intent.Epoch++; break;
                case "sequence": intent.Sequence = 0; break;
                case "source": intent.SourceId++; break;
                case "resource": intent.ResourceId++; break;
                case "missingActor": host.Session.Players.Clear(); break;
                case "dead": host.Guest.IsDead = true; break;
                case "oldPose": host.Guest.LastTransformTime = 7; break;
                case "range": host.Guest.Position = new Vector3(4, 0, 0); break;
                case "inactive": host.Trigger.enabled = false; break;
                case "full": host.Woods.Value = 4; break;
                case "contact": Time.unscaledTime += .3f; break;
                case "bounds": host.Piece.GetComponent<Collider>().bounds.Overlaps = false; break;
                case "parent": host.Piece.transform.parent = host.Trigger.transform; break;
                case "tag": host.Piece.tag = "OTHER"; break;
                case "name": host.Piece.name = "not firewood"; break;
                case "heldByOther": host.Item.RemoteOwner = 3; break;
                case "oldRelease": host.Item.LastRemoteReleaseAt = 7; break;
            }
            int fuel = host.Woods.Value;
            var result = host.Request(intent, authenticated); Assert.Equal(expected, result.Status);
            Assert.Equal(0, host.Destroy.Calls); Assert.Equal(fuel, host.Woods.Value);
            Assert.False(host.Piece.Destroyed); Assert.Empty(result.Snapshot!.ConsumedResources);
            Assert.Equal(expected == WoodstoveFeedStatus.InvalidActor || expected == WoodstoveFeedStatus.StaleEpoch ? 0u : intent.Sequence, result.HighWater);
        }
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void PartialNativeFailureOrDeferredTimeoutNeverPublishesSuccessOrRetries(bool timeout)
        {
            Reset(); using var host = new Rig(true); host.Contact();
            host.Destroy.Throw = !timeout; host.Destroy.OmitDestruction = timeout;
            var result = host.Request(Intent(host));
            if (timeout) { Assert.Equal(WoodstoveFeedStatus.Pending, result.Status); Time.unscaledTime += 2.1f; result = Finish(host); }
            Assert.Equal(WoodstoveFeedStatus.NativeFailure, result.Status); Assert.Null(result.Snapshot);
            Assert.True(host.Sync.Test.Faulted); Assert.False(host.Destroy.Enabled);
            host.Request(Intent(host, 2)); host.Tick(); Assert.Equal(1, host.Destroy.Calls);
            Assert.DoesNotContain(host.Session.Sent.OfType<WoodstoveFuelUpdate>(), x => x.IsDecision && x.Status == WoodstoveFeedStatus.Accepted);
        }
        [Fact]
        public void ReentrantNativeAttemptCannotMutateAndCannotBecomeValidOnReplay()
        {
            Reset(); using var host = new Rig(true); host.Contact(); var nested = Intent(host, 2, 52);
            host.Destroy.During = () => {
                host.Destroy.During = null;
                var denied = host.Request(nested); Assert.Equal(WoodstoveFeedStatus.Busy, denied.Status); Assert.Null(denied.Snapshot);
            };
            Assert.Equal(WoodstoveFeedStatus.Pending, host.Request(Intent(host)).Status);
            Assert.Equal(1, host.Destroy.Calls); Assert.Equal(WoodstoveFeedStatus.Accepted, Finish(host).Status);
            var next = new GameObject("firewood(Clone)");
            var nextItem = new SyncedItem { Body = next.AddComponent<Rigidbody>(), RemoteOwner = 2 };
            next.AddComponent<BoxCollider>(); host.Sync.AdmitTestPiece(52, next, nextItem);
            typeof(CabinWoodContact).GetMethod("OnTriggerStay", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(host.Trigger.GetComponent<CabinWoodContact>(), new object[] { next.GetComponent<Collider>() });
            var replay = host.Request(nested);
            Assert.Equal(WoodstoveFeedStatus.ReplayedSequence, replay.Status);
            Assert.Equal(1, host.Destroy.Calls);
        }
        [Fact]
        public void LegacyCabinFeedAndGuestBurnAreSuppressedButOtherSourcesStillUseLegacyRouting()
        {
            Reset(); using var guest = new Rig(false);
            FsmHook.FindState(guest.Fire, "Remove wood")!.Enter(); Assert.Equal(1, guest.Woods.Value);
            FsmHook.FindState(guest.Trigger, "Destroy firewood")!.Enter(); Assert.Equal(0, guest.Destroy.Calls);
            guest.Sync.OnRemoteState(new HeatSourceState { SourceId = WoodstoveFuelAuthority.CabinSourceId, Fuel = 4 });
            Assert.Equal(1, guest.Woods.Value);
            using var host = new Rig(true);
            Assert.False(host.Sync.TryAcceptIntent(new HeatSourceIntent { SourceId = WoodstoveFuelAuthority.CabinSourceId,
                Action = HeatSourceIntent.ActionFeedWood, PlayerId = 2, Sequence = 1 }));
            Assert.DoesNotContain("WOOD", host.Trigger.Events);
            Assert.True(host.Sync.TryAcceptIntent(new HeatSourceIntent { SourceId = WoodstoveFuelAuthority.CabinSourceId,
                Action = HeatSourceIntent.ActionLight, PlayerId = 2, Sequence = 1 }));
            Assert.Contains("USE", host.Fire.Events);
            // Discovery uses each original independent source hash, not the cabin seam.
            var fireplace = new GameObject("COTTAGE/Stuff/Fireplace");
            var trigger = new GameObject("OtherWoodTrigger").Add(new PlayMakerFSM { FsmName = "Trigger" });
            fireplace.transform.Children["WoodTrigger"] = trigger.transform;
            Assert.True(host.Sync.TryAcceptIntent(new HeatSourceIntent { SourceId = StableHash.Fnv1a32("COTTAGE/Stuff/Fireplace"),
                Action = HeatSourceIntent.ActionFeedWood, PlayerId = 2, Sequence = 1 }));
            Assert.Contains("WOOD", trigger.Events); Assert.Equal(0, host.Destroy.Calls);
        }

        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void ResultPublicationCannotReenterFeedOrPublishANestedOutcome(bool finalResult)
        {
            Reset(); using var host = new Rig(true); host.Contact();
            var next = new GameObject("firewood(Clone)");
            var item = new SyncedItem { Body = next.AddComponent<Rigidbody>(), RemoteOwner = 2 };
            next.AddComponent<BoxCollider>(); host.Sync.AdmitTestPiece(52, next, item);
            typeof(CabinWoodContact).GetMethod("OnTriggerStay", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(host.Trigger.GetComponent<CabinWoodContact>(), new object[] { next.GetComponent<Collider>() });
            var nested = Intent(host, 2, 52); int callbacks = 0;
            host.Session.Sending = message => {
                if (!(message is WoodstoveFuelUpdate update) || !update.IsDecision
                    || (update.Status == WoodstoveFeedStatus.Accepted) != finalResult) return;
                callbacks++; host.Session.Sending = null;
                host.Sync.OnCabinIntent(nested, 2);
            };
            Assert.Equal(WoodstoveFeedStatus.Pending, host.Request(Intent(host)).Status);
            Assert.Equal(WoodstoveFeedStatus.Accepted, Finish(host).Status);
            Assert.Equal(1, callbacks); Assert.Equal(1, host.Destroy.Calls);
            Assert.Equal(2, host.Session.Sent.OfType<WoodstoveFuelUpdate>().Count(x => x.IsDecision));
            Assert.Equal(WoodstoveFeedStatus.ReplayedSequence, host.Request(nested).Status);
            Assert.Equal(1, host.Destroy.Calls); Assert.False(next.Destroyed);
        }

        [Fact]
        public void DepletionAndPortableRejoinKeepAbsoluteStateTombstonesAndHighWater()
        {
            Reset(); using var host = new Rig(true); host.Contact();
            Assert.Equal(WoodstoveFeedStatus.Pending, host.Request(Intent(host)).Status);
            host.Woods.Value = 1; host.Heat.Value = .12f; host.Lit.Value = false;
            var final = Finish(host);
            Assert.Equal(1, final.Snapshot!.Values.Fuel); // modeled host depletion is not restored to cached +1
            host.Sync.ForgetPlayer(2); // actual Core reconnect hook must not erase the cabin ledger
            host.Sync.ForceBroadcast(); host.Tick();
            using var rejoin = new Rig(false); rejoin.Receive(host.Session.Sent);
            Agree(host, rejoin); Assert.Equal(1u, rejoin.Sync.Test.Client!.HighWater);
            Assert.Equal(WoodstoveFeedStatus.ReplayedSequence, host.Request(Intent(host)).Status);
            Assert.Equal(1, host.Destroy.Calls);
        }

        [Fact]
        public void SendExceptionReleasesPublicationGuardWithoutRepeatingNativeEffect()
        {
            Reset(); using var host = new Rig(true); host.Contact();
            host.Session.Sending = _ => { host.Session.Sending = null; throw new InvalidOperationException("portable send failure"); };
            Assert.Throws<InvalidOperationException>(() => host.Request(Intent(host)));
            Assert.Equal(1, host.Destroy.Calls);
            Assert.Equal(WoodstoveFeedStatus.Accepted, Finish(host).Status);
            Assert.False(host.Sync.Test.Faulted);
            Assert.Equal(WoodstoveFeedStatus.ReplayedSequence, host.Request(Intent(host)).Status);
            Assert.Equal(1, host.Destroy.Calls);
        }

        [Fact]
        public void AdmissionPublicationCannotFeedFromItsOwnCallback()
        {
            Reset(); using var host = new Rig(true); host.Contact();
            int callbacks = 0; var request = Intent(host);
            host.Session.Sending = _ => { callbacks++; host.Session.Sending = null; host.Sync.OnCabinIntent(request, 2); host.Tick(); };
            host.Sync.ForceBroadcast(); host.Tick();
            Assert.Equal(1, callbacks); Assert.Equal(0, host.Destroy.Calls);
            Assert.DoesNotContain(host.Session.Sent.OfType<WoodstoveFuelUpdate>(), x => x.IsDecision);
            Assert.Equal(WoodstoveFeedStatus.ReplayedSequence, host.Request(request).Status);
            Assert.Equal(WoodstoveFeedStatus.Pending, host.Request(Intent(host, 2)).Status);
            Assert.Equal(WoodstoveFeedStatus.Accepted, Finish(host).Status); Assert.Equal(1, host.Destroy.Calls);
        }
    }
}
