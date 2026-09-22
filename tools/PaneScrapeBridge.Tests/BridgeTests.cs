using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace PaneScrapeBridge.Tests
{
    // These actions are counted adapter doubles, not synthesized native evidence.
    public sealed class FloatAdd : FsmStateAction
    {
        public FsmFloat floatVariable = null!, add = null!;
        public bool everyFrame, perSecond;
        public int Calls;
        public Action? Callback;
        public override void OnEnter() { Calls++; Callback?.Invoke(); floatVariable.Value += add.Value; }
    }
    public sealed class SetMaterialFloat : FsmStateAction
    {
        public bool everyFrame;
        public FsmFloat floatValue = null!;
        public FsmMaterial material = null!;
        public FsmString namedFloat = new FsmString { Value = "_Cutoff" };
        public override void OnEnter() => material.Value!.SetFloat(namedFloat.Value, floatValue.Value);
    }
    public sealed class SendEventByName : FsmStateAction
    { public FsmString sendEvent = new FsmString { Value = "WINDSHIELD" }; public int Calls; public override void OnEnter() { Calls++; } }
    public sealed class Counter : FsmStateAction { public int Calls; public override void OnEnter() { Calls++; } }
    public sealed class RandomFloat : FsmStateAction { }
    public sealed class ArrayListGetRandom : FsmStateAction { }
    public sealed class MasterAudioPlaySound : FsmStateAction { }

    public sealed partial class BridgeTests
    {
        private sealed class Rig : IDisposable
        {
            public readonly PaneScrapeSync Bridge = new PaneScrapeSync();
            public readonly SessionManager Session = new SessionManager();
            public readonly FsmFloat Cutoff = new FsmFloat { Name = "CutoffWindshield", Value = .25f };
            public readonly FsmFloat Heat = new FsmFloat { Name = "PlayerTemp" };
            public readonly FsmGameObject Picked = new FsmGameObject();
            public readonly Material Material = new Material();
            public readonly FloatAdd Glass, Effect;
            public readonly Collider Pane, ToolContact;
            public readonly PlayMakerFSM Hand, Scrape;
            public readonly Dictionary<FsmState, FsmStateAction[]> Originals = new Dictionary<FsmState, FsmStateAction[]>();
            public readonly FsmTransition[] Globals;
            public readonly FsmEvent[] Events;
            public readonly SyncedItem Car, Tool;
            public uint Epoch => Get<uint>(Bridge, "_epoch");
            public Rig(bool host = true)
            {
                GameObject.Scene.Clear(); Time.unscaledTime = 10; Physics.Contact = null; Physics.Distance = .6f;
                SessionManager.Instance = Session;
                Session.IsHost = host; Session.State = host ? SessionState.Hosting : SessionState.Connected;
                Session.LocalPlayerId = host ? (byte)0 : (byte)2;
                Session.Players.Add(new RemotePlayer { PlayerId = 2 });
                var c = new PaneScrapeData();
                string[] values = { "CORRIS", "CORRIS/BODY/Windshield/collider", "Scrape", "CORRIS/Simulation/CarTempCorris", "Freezing",
                    "CutoffWindshield", "State 7", "Scrape 2", "WINDSHIELD", "ice scraper(itemx)", "hand", "PickUp", "PickedObject",
                    "Set pivot 2", "Ice Scraper", "Off", "Drop part", "Drop part 2", "eye", "inside" };
                for (int i = 0; i < values.Length; i++) c.Bindings[PaneScrapeData.Keys[i]] = values[i];
                SyncCatalog.PaneScrape = c;
                Car = new SyncedItem { Id = 9, Path = "CORRIS", IsVehicle = true, Body = new GameObject("CORRIS").Add(new Rigidbody()) };
                Tool = new SyncedItem { Id = 77, Body = new GameObject(c["toolName"]).Add(new Rigidbody()) };
                ToolContact = Tool.Body.gameObject.Add(new Collider { attachedRigidbody = Tool.Body });
                WorldSyncManager.Instance = new WorldSyncManager();
                WorldSyncManager.Instance.ItemSync.Items.Add(9, Car); WorldSyncManager.Instance.ItemSync.Items.Add(77, Tool);
                Scrape = FsmAt(c["panePath"], "Scrape", State("Scrape 2", new SendEventByName()));
                Pane = Scrape.gameObject.Add(new Collider());
                Scrape.FsmVariables.Vars["Distance"] = new FsmFloat { Value = .8f };
                Glass = new FloatAdd { floatVariable = Cutoff, add = new FsmFloat { Name = "ScrapeEfficiency", Value = .005f } };
                Effect = new FloatAdd { floatVariable = Heat, add = new FsmFloat { Name = "BodyTempAdd", Value = .47f } };
                var material = new FsmMaterial { Value = Material };
                var freezing = FsmAt(c["freezingPath"], "Freezing", State("State 7", Glass, new SetMaterialFloat { floatValue = Cutoff, material = material }),
                    State("Sound", new RandomFloat(), new ArrayListGetRandom(), new MasterAudioPlaySound(), Effect));
                freezing.FsmVariables.Vars["CutoffWindshield"] = Cutoff; freezing.FsmVariables.Vars["6"] = material;
                freezing.FsmVariables.Vars["GlassPos"] = new FsmGameObject();
                Hand = FsmAt("hand", "PickUp", State("Set pivot 2", new Counter(), new Counter { Enabled = false }),
                    State("Ice Scraper", new Counter()), State("Off", new Counter()), State("Drop part", new Counter()),
                    State("Drop part 2", new Counter()), State("Look for object", new Counter()));
                Hand.FsmVariables.Vars["PickedObject"] = Picked;
                Globals = Hand.Fsm.GlobalTransitions; Events = Hand.Fsm.Events;
                var inside = FsmAt("inside", "PlayerTrigger"); inside.gameObject.Add(new Collider());
                inside.FsmVariables.Vars["PlayerInside"] = new FsmBool();
                var eye = new GameObject("eye"); eye.transform.position = new Vector3(0, 1, 0);
                foreach (var f in new[] { Hand, Scrape }) foreach (var s in f.Fsm.States) Originals.Add(s, s.Actions);
                Call(Bridge, "Awake"); Call(Bridge, "Update");
                Assert.False(Get<bool>(Bridge, "_failed"));
            }
            private static FsmState State(string name, params FsmStateAction[] a)
            { var s = new FsmState { Name = name, Actions = a }; foreach (var x in a) x.Init(s); return s; }
            private static PlayMakerFSM FsmAt(string path, string name, params FsmState[] states)
                => new GameObject(path).Add(new PlayMakerFSM { FsmName = name, Fsm = new Fsm { States = states } });
            public void Enter(string name) { Hand.ActiveStateName = name; Hand.Fsm.States.Single(s => s.Name == name).Enter(); }
            public ScraperAction Request(uint n, ScraperOperation op, byte actor = 2)
                => new ScraperAction { Epoch = Epoch, Actor = actor, Sequence = n, VehicleId = 9, Pane = 1, ToolId = 77,
                    Operation = op, Eye = new NetVector3(0, 1, 0), Direction = new NetVector3(0, 0, 1) };
            public PaneScrapeUpdate Act(ScraperAction a, byte actor = 2)
            {
                SessionManager.Instance = Session;
                Bridge.OnAction(Assert.IsType<ScraperAction>(PacketCodec.Decode(PacketCodec.Encode(a))), actor);
                return Assert.IsType<PaneScrapeUpdate>(PacketCodec.Decode(PacketCodec.Encode(Session.Sent.OfType<PaneScrapeUpdate>().Last())));
            }
            public void Equip()
            {
                Physics.Contact = ToolContact;
                Assert.Equal(PaneScrapeStatus.Accepted, Act(Request(1, ScraperOperation.Pickup)).Status);
                Assert.Equal(PaneScrapeStatus.Accepted, Act(Request(2, ScraperOperation.Equip)).Status);
                Physics.Contact = Pane;
            }
            public void Dispose() { Call(Bridge, "OnDestroy"); }
        }
        private static T Get<T>(object o, string n) => (T)o.GetType().GetField(n, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;
        private static void Call(object o, string n) => o.GetType().GetMethod(n, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, null);

        [Theory]
        [InlineData(false)][InlineData(true)]
        public void NonOwnerGuestUsesActualStrokeHookThenHostDeltaAndActorOnlyAbsoluteResult(bool interruptedHostPickup)
        {
            using var host = new Rig();
            if (interruptedHostPickup)
            {
                host.Picked.Value = host.Tool.Body!.gameObject;
                Physics.Contact = host.ToolContact;
                host.Enter("Set pivot 2");
                Assert.True(Get<bool>(host.Bridge, "_resumePickup"));
                Assert.Equal(0, ((Counter)host.Originals.Single(x => x.Key.Name == "Set pivot 2").Value[0]).Calls);
                host.Bridge.ResetSession();
                host.Session.Sent.Clear();
                Call(host.Bridge, "Update");
            }
            using var guest = new Rig(false);
            guest.Bridge.OnUpdate(host.Session.Sent.OfType<PaneScrapeUpdate>().Single(x => x.Actor == 2));
            guest.Picked.Value = guest.Tool.Body!.gameObject;
            guest.Enter("Set pivot 2");
            var pickup = Assert.IsType<ScraperAction>(guest.Session.Sent.Last());
            Assert.Equal(ScraperOperation.Pickup, pickup.Operation);
            Assert.False(guest.Originals.Single(x => x.Key.Name == "Set pivot 2").Value[0].Enabled);
            Physics.Contact = host.ToolContact;
            var approval = host.Act(pickup);
            Assert.Equal(PaneScrapeStatus.Accepted, approval.Status);
            SessionManager.Instance = guest.Session; guest.Bridge.OnUpdate(approval);
            Call(guest.Bridge, "Update"); guest.Enter("Ice Scraper");
            foreach (var action in guest.Session.Sent.OfType<ScraperAction>().Skip(1).ToArray())
                Assert.Equal(PaneScrapeStatus.Accepted, host.Act(action).Status);
            SessionManager.Instance = guest.Session;
            guest.Scrape.Fsm.States[0].Enter();
            var outgoing = Assert.IsType<ScraperAction>(guest.Session.Sent.Last());
            Assert.Equal(ScraperOperation.Stroke, outgoing.Operation);
            Assert.Equal(0, guest.Glass.Calls); Assert.Equal(0, guest.Effect.Calls);
            Physics.Contact = host.Pane;
            var result = host.Act(outgoing);
            Assert.Equal(PaneScrapeStatus.Accepted, result.Status);
            Assert.Equal(1, host.Glass.Calls); Assert.Equal(0, host.Effect.Calls);
            Assert.Equal(.255f, host.Cutoff.Value, 6); Assert.Equal(host.Cutoff.Value, result.Cutoff);
            Assert.Equal(host.Cutoff.Value, host.Material.Cutoff);
            Assert.False(host.Car.LocallyOwned); Assert.Equal((byte)255, host.Car.RemoteOwner);
            Assert.False(guest.Car.LocallyOwned); Assert.Equal((byte)255, guest.Car.RemoteOwner);
            host.Act(outgoing); Assert.Equal(1, host.Glass.Calls);
            SessionManager.Instance = guest.Session;
            guest.Bridge.OnUpdate(result); guest.Bridge.OnUpdate(result);
            Assert.Equal(result.Cutoff, guest.Cutoff.Value); Assert.Equal(result.Cutoff, guest.Material.Cutoff);
            Assert.Equal(0, guest.Glass.Calls); Assert.Equal(1, guest.Effect.Calls); Assert.Equal(.47f, guest.Heat.Value);
        }

        [Theory]
        [InlineData("actor")][InlineData("epoch")][InlineData("vehicle")][InlineData("pane")]
        [InlineData("tool")][InlineData("contact")][InlineData("range")][InlineData("pose")]
        [InlineData("equipment")][InlineData("moving")][InlineData("inside")][InlineData("eye")]
        [InlineData("duplicate")][InlineData("out-of-order")]
        public void InvalidBridgeStrokeMutatesNeitherPaneNorEffects(string bad)
        {
            using var r = new Rig(); r.Equip();
            var a = r.Request(4, ScraperOperation.Stroke);
            switch (bad)
            {
                case "actor": a.Actor = 3; break;
                case "epoch": a.Epoch++; break;
                case "vehicle": a.VehicleId++; break;
                case "pane": a.Pane++; break;
                case "tool": a.ToolId++; break;
                case "contact": Physics.Contact = r.ToolContact; break;
                case "range": Physics.Distance = .801f; break;
                case "pose": r.Session.Players[0].LastTransformTime = 9; break;
                case "equipment": Time.unscaledTime = 11; r.Session.Players[0].LastTransformTime = 11; break;
                case "moving": r.Car.Body!.velocity = new Vector3(1, 0, 0); break;
                case "inside": r.Session.Players[0].MoveState = PlayerMoveState.Driving; break;
                case "eye": a.Eye = new NetVector3(9, 1, 0); break;
                case "duplicate": r.Act(r.Request(4, ScraperOperation.KeepAlive)); break;
                case "out-of-order": r.Act(r.Request(5, ScraperOperation.KeepAlive)); break;
            }
            var result = r.Act(a);
            Assert.NotEqual(PaneScrapeStatus.Accepted, result.Status);
            var replica = new PaneScrapeReplica(9, r.Epoch, 2);
            Assert.True(replica.ReceiveDecision(true, result.Decision(), out bool effect)); Assert.False(effect);
            Assert.Equal(.25f, replica.Current!.Cutoff); Assert.Equal(.25f, r.Cutoff.Value);
            Assert.Equal(0, r.Glass.Calls); Assert.Equal(0, r.Effect.Calls);
        }

        [Theory]
        [InlineData(ScraperOperation.Pickup)][InlineData(ScraperOperation.Equip)][InlineData(ScraperOperation.KeepAlive)]
        public void EquipmentRequestsMustNameThisExactPane(ScraperOperation op)
        {
            using var r = new Rig();
            if (op != ScraperOperation.Pickup) r.Equip();
            Physics.Contact = r.ToolContact;
            var request = r.Request(4, op); request.Pane = 2;
            Assert.NotEqual(PaneScrapeStatus.Accepted, r.Act(request).Status);
            Assert.Equal(0, r.Glass.Calls); Assert.Equal(0, r.Effect.Calls);
        }

        [Fact]
        public void TeardownRestoresEveryNativeActionEnabledBitAndHandRoutingTable()
        {
            using var r = new Rig();
            r.Picked.Value = r.Tool.Body!.gameObject; Physics.Contact = r.ToolContact;
            r.Enter("Set pivot 2");
            r.Bridge.ResetSession();
            foreach (var p in r.Originals) Assert.Same(p.Value, p.Key.Actions);
            Assert.True(r.Originals.Keys.Single(s => s.Name == "Set pivot 2").Actions[0].Enabled);
            Assert.False(r.Originals.Keys.Single(s => s.Name == "Set pivot 2").Actions[1].Enabled);
            Assert.Same(r.Globals, r.Hand.Fsm.GlobalTransitions); Assert.Same(r.Events, r.Hand.Fsm.Events);
        }

        [Fact]
        public void ConcurrentStrokeCannotMutateAndCannotBeRetriedAfterOuterStroke()
        {
            using var r = new Rig(); r.Equip();
            var nested = r.Request(4, ScraperOperation.Stroke);
            r.Glass.Callback = () => r.Bridge.OnAction(nested, 2);
            var result = r.Act(r.Request(3, ScraperOperation.Stroke));
            Assert.Equal(PaneScrapeStatus.Accepted, result.Status);
            Assert.Equal(1, r.Glass.Calls); Assert.Equal(0, r.Effect.Calls);
            r.Glass.Callback = null;
            Assert.NotEqual(PaneScrapeStatus.Accepted, r.Act(nested).Status);
            Assert.Equal(1, r.Glass.Calls);
        }

        [Fact]
        public void LatePickupApprovalCannotReenterHandAfterPlayerChangedObject()
        {
            using var r = new Rig(false);
            r.Bridge.OnUpdate(new PaneScrapeUpdate { Epoch = 42, VehicleId = 9, ToolId = 77, Revision = 1, Cutoff = .25f, Actor = 2 });
            r.Picked.Value = r.Tool.Body!.gameObject; r.Enter("Set pivot 2");
            var pickup = Assert.IsType<ScraperAction>(r.Session.Sent.Last());
            var other = new GameObject("other-held-item"); r.Picked.Value = other; r.Enter("Look for object");
            var counter = (Counter)r.Originals.Keys.Single(s => s.Name == "Set pivot 2").Actions[1];
            r.Bridge.OnUpdate(new PaneScrapeUpdate { Epoch = 42, VehicleId = 9, ToolId = 77, Revision = 1,
                Cutoff = .25f, Actor = 2, Holder = 2, Sequence = pickup.Sequence, HighWater = pickup.Sequence });
            Call(r.Bridge, "Update");
            Assert.Equal("Look for object", r.Hand.ActiveStateName); Assert.Same(other, r.Picked.Value);
            Assert.Equal(0, counter.Calls);
            Assert.Contains(r.Session.Sent.OfType<ScraperAction>(), a => a.Operation == ScraperOperation.Drop);
            Assert.Equal(0, r.Glass.Calls); Assert.Equal(0, r.Effect.Calls);
        }

        [Fact]
        public void TeardownPreservesForeignHandHooksAndRemoteEntriesAddedAfterBinding()
        {
            using var r = new Rig();
            var state = r.Hand.Fsm.States.Single(s => s.Name == "Ice Scraper");
            var otherHook = new Counter(); otherHook.Init(state);
            state.Actions = state.Actions.Concat(new[] { otherHook }).ToArray();
            var otherEvent = FsmEvent.GetFsmEvent("OTHER_ENTRY");
            var otherTransition = new FsmTransition { FsmEvent = otherEvent, ToState = "Off" };
            r.Hand.Fsm.Events = r.Hand.Fsm.Events.Concat(new[] { otherEvent }).ToArray();
            r.Hand.Fsm.GlobalTransitions = r.Hand.Fsm.GlobalTransitions.Concat(new[] { otherTransition }).ToArray();
            r.Bridge.ResetSession();
            Assert.Equal(r.Originals[state].Concat(new[] { otherHook }), state.Actions);
            Assert.Equal(new[] { otherEvent }, r.Hand.Fsm.Events);
            Assert.Equal(new[] { otherTransition }, r.Hand.Fsm.GlobalTransitions);
        }

        [Fact]
        public void ConcurrentDropCannotRevokeEquipmentDuringAcceptedStroke()
        {
            using var r = new Rig(); r.Equip();
            var drop = r.Request(4, ScraperOperation.Drop);
            r.Glass.Callback = () => r.Bridge.OnAction(drop, 2);
            var result = r.Act(r.Request(3, ScraperOperation.Stroke));
            Assert.Equal(PaneScrapeStatus.Accepted, result.Status);
            Assert.Equal((byte)2, result.Holder); Assert.True(result.Equipped);
            r.Glass.Callback = null;
            Assert.NotEqual(PaneScrapeStatus.Accepted, r.Act(drop).Status);
            Assert.Equal(1, r.Glass.Calls); Assert.Equal(0, r.Effect.Calls);
        }

        [Theory]
        [InlineData(ScraperOperation.Pickup)][InlineData(ScraperOperation.Equip)][InlineData(ScraperOperation.KeepAlive)]
        public void EquipmentCannotBeGrantedWhileCarMoves(ScraperOperation operation)
        {
            using var r = new Rig(); if (operation != ScraperOperation.Pickup) r.Equip();
            r.Car.Body!.velocity = new Vector3(1, 0, 0); Physics.Contact = r.ToolContact;
            Assert.NotEqual(PaneScrapeStatus.Accepted, r.Act(r.Request(4, operation)).Status);
            Assert.Equal(0, r.Glass.Calls); Assert.Equal(0, r.Effect.Calls);
        }

        [Fact]
        public void ResultBeforeAdmissionCannotReplayOldActorEffects()
        {
            using var r = new Rig(false);
            var old = new PaneScrapeUpdate { Epoch = 42, VehicleId = 9, ToolId = 77, Revision = 2, Cutoff = .255f,
                Actor = 2, Sequence = 5, HighWater = 5, IsDecision = true, Status = PaneScrapeStatus.Accepted };
            r.Bridge.OnUpdate(old);
            Assert.Equal(.255f, r.Cutoff.Value); Assert.Equal(0, r.Effect.Calls);
            old.IsDecision = false; old.Sequence = 0; r.Bridge.OnUpdate(old);
            old.IsDecision = true; old.Sequence = 5; r.Bridge.OnUpdate(old);
            Assert.Equal(0, r.Effect.Calls);
        }

        [Fact]
        public void HostUsesSameNativeHooksWithExactlyOneLocalEffect()
        {
            using var r = new Rig();
            r.Picked.Value = r.Tool.Body!.gameObject; Physics.Contact = r.ToolContact;
            r.Enter("Set pivot 2"); Call(r.Bridge, "Update"); r.Enter("Ice Scraper");
            Physics.Contact = r.Pane; r.Scrape.Fsm.States[0].Enter();
            var result = r.Session.Sent.OfType<PaneScrapeUpdate>().Last();
            Assert.Equal(PaneScrapeStatus.Accepted, result.Status); Assert.Equal((byte)0, result.Actor);
            Assert.Equal(1, r.Glass.Calls); Assert.Equal(1, r.Effect.Calls);
            Assert.Equal(.255f, r.Cutoff.Value, 6); Assert.Equal(.47f, r.Heat.Value);
            r.Bridge.OnUpdate(result); Assert.Equal(1, r.Effect.Calls);
        }

        [Fact]
        public void MotionPermissionRequiresNonnegativeFreshLeaseAndKnownHolder()
        {
            using var r = new Rig(); r.Equip();
            Assert.True(r.Bridge.AllowsToolMotion(77, 2));
            Assert.False(r.Bridge.AllowsToolMotion(77, 3)); Assert.False(r.Bridge.AllowsToolMotion(78, 2));
            Time.unscaledTime = 9;
            Assert.False(r.Bridge.AllowsToolMotion(77, 2));
            Time.unscaledTime = 11;
            Assert.False(r.Bridge.AllowsToolMotion(77, 2));
        }

        [Fact]
        public void NonHolderDropIsDeniedAndDisconnectRevokesHeldTool()
        {
            using var r = new Rig();
            Assert.NotEqual(PaneScrapeStatus.Accepted, r.Act(r.Request(1, ScraperOperation.Drop)).Status);
            Physics.Contact = r.ToolContact;
            Assert.Equal(PaneScrapeStatus.Accepted, r.Act(r.Request(2, ScraperOperation.Pickup)).Status);
            Assert.Equal(PaneScrapeStatus.Accepted, r.Act(r.Request(3, ScraperOperation.Equip)).Status);
            r.Session.Leave(r.Session.Players[0]);
            Assert.False(r.Bridge.AllowsToolMotion(77, 2));
            Physics.Contact = r.Pane;
            Assert.NotEqual(PaneScrapeStatus.Accepted, r.Act(r.Request(4, ScraperOperation.Stroke)).Status);
            Assert.Equal(0, r.Glass.Calls); Assert.Equal(0, r.Effect.Calls);
        }

        [Theory]
        [InlineData("sound")][InlineData("heat-repeat")][InlineData("material-repeat")][InlineData("inside-variable")]
        public void ChangedNativeSignatureFailsBeforeInstallingAnyHook(string change)
        {
            using var r = new Rig(); r.Bridge.ResetSession();
            var freezing = GameObject.Find("CORRIS/Simulation/CarTempCorris")!.GetComponent<PlayMakerFSM>()!;
            if (change == "sound") freezing.Fsm.States.Single(s => s.Name == "Sound").Actions[0] = new Counter();
            if (change == "heat-repeat") r.Effect.everyFrame = true;
            if (change == "material-repeat") ((SetMaterialFloat)freezing.Fsm.States.Single(s => s.Name == "State 7").Actions[1]).everyFrame = true;
            if (change == "inside-variable") GameObject.Find("inside")!.GetComponent<PlayMakerFSM>()!.FsmVariables.Vars.Clear();
            Call(r.Bridge, "Update");
            Assert.True(Get<bool>(r.Bridge, "_failed"));
            foreach (var p in r.Originals) Assert.Same(p.Value, p.Key.Actions);
            Assert.Same(r.Globals, r.Hand.Fsm.GlobalTransitions); Assert.Same(r.Events, r.Hand.Fsm.Events);
            Assert.Equal(0, r.Glass.Calls); Assert.Equal(0, r.Effect.Calls);
        }
    }
}
