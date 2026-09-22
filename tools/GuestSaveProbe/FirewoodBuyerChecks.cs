using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static class FirewoodBuyerChecks
    {
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        private static readonly Type Binding = Core.GetType("WinterMP.Core.Sync.FirewoodBuyerBinding", true);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Members).Invoke(target, args);
        private static object Get(object target, string name) => target.GetType().GetField(name, Members).GetValue(target);
        private static void Require(bool value) { if (!value) throw new InvalidOperationException("Firewood buyer assertion failed."); }

        internal static void Run(Action<string, Action> check)
        {
            Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true).GetMethod("EnsureLoaded", Members).Invoke(null, null);
            var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var reader = Activator.CreateInstance(readerType, Members, null, new object[] {
                File.ReadAllText(Path.Combine(Application.dataPath, "../native-firewood.json")) }, null);
            var json = (Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null);
            for (int n = 1; n <= 4; n++) RunBuyer(n, (List<object>)json["fsms"], check);
        }

        private static GameObject Node(GameObject root, string path)
        {
            string[] names = path.Split('/'); Transform at = root.transform;
            for (int i = names[0] == root.name ? 1 : 0; i < names.Length; i++)
            {
                var next = at.Find(names[i]);
                if (next == null) { var child = new GameObject(names[i]); child.transform.SetParent(at, false); next = child.transform; }
                at = next;
            }
            return at.gameObject;
        }

        private static void RunBuyer(int n, List<object> rows, Action<string, Action> check)
        {
            string prefix = "JOBS/HouseWood" + n, label = "buyer " + n + ": ";
            var root = new GameObject("JOBS"); root.SetActive(false);
            var fsms = new Dictionary<string, PlayMakerFSM>(); var sources = new Dictionary<PlayMakerFSM, Dictionary<string, object>>();
            object? binding = null;
            try
            {
                foreach (Dictionary<string, object> row in rows)
                {
                    string path = (string)row["path"], name = (string)row["fsmName"];
                    if (!path.StartsWith(prefix, StringComparison.Ordinal) || (name != "Animations" && name != "Logic" && name != "LOD"
                        && !(name == "Use" && path.EndsWith("/PayMoney", StringComparison.Ordinal)))) continue;
                    var fsm = NativeBagPartChecks.MakeFsm(Node(root, path), row); fsms.Add(name == "LOD" && path != prefix ? "BuyerLod" : name, fsm); sources.Add(fsm, row);
                }
                foreach (var pair in sources)
                {
                    var defaults = (Dictionary<string, object>)pair.Value["variableDefaults"];
                    foreach (Dictionary<string, object> variable in (IEnumerable)defaults["gameObjectVariables"])
                    {
                        var objectValue = (Dictionary<string, object>)variable["value"];
                        if (objectValue.TryGetValue("scenePath", out var path))
                            pair.Key.FsmVariables.FindFsmGameObject((string)variable["name"]).Value = Node(root, (string)path);
                    }
                }
                var payment = fsms["Use"]; var buyer = fsms["Animations"]; var job = fsms["Logic"]; var lod = fsms["LOD"]; var npc = fsms["BuyerLod"];
                var arm = payment.FsmVariables.FindFsmGameObject("CollarRight").Value.AddComponent<Animation>();
                var left = buyer.FsmVariables.FindFsmGameObject("CollarLeft").Value.AddComponent<Animation>();
                root.SetActive(true);
                var leftClip = new AnimationClip(); leftClip.legacy = true;
                leftClip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1, 0));
                left.AddClip(leftClip, "fat_handsleft_ass");
                foreach (string name in new[] { "fat_handsright_carry", "fat_handsright_offer" })
                {
                    var clip = new AnimationClip(); clip.legacy = true;
                    clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1, 0));
                    arm.AddClip(clip, name);
                }
                foreach (var pair in sources)
                {
                    var states = new List<string>();
                    foreach (Dictionary<string, object> state in (List<object>)pair.Value["states"])
                    {
                        string name = (string)state["name"];
                        if (pair.Key == job && name != "Player left?") continue;
                        if (pair.Key == buyer && name != "State 1" && name != "State 2") continue;
                        states.Add(name);
                    }
                    NativeBagPartChecks.LoadActions(pair.Key, pair.Value, states.ToArray());
                    foreach (var state in pair.Key.Fsm.States)
                        foreach (var action in state.Actions)
                            if (action.GetType().Name == "GetDistance")
                            {
                                var target = (FsmGameObject)Get(action, "target");
                                target.Value = Node(root, "PLAYER");
                            }
                    NativeBagPartChecks.Start(pair.Key);
                }
                var guardType = Core.GetType("WinterMP.Core.Sync.FirewoodPaymentGuard", true);
                var guard = Activator.CreateInstance(guardType, Members, null, new object[] { payment }, null);
                Func<bool, object> make = guest => Activator.CreateInstance(Binding, Members, null,
                    new object[] { 42u, payment, guard, buyer, job, lod, npc, guest }, null);
                var original = new Dictionary<FsmState, FsmStateAction[]>();
                foreach (var fsm in fsms.Values) foreach (var state in fsm.Fsm.States) original[state] = state.Actions;
                check(label + "serialized host visibility binds exactly six player-distance reads", () =>
                {
                    binding = make(false); int count = 0;
                    foreach (var fsm in fsms.Values) foreach (var state in fsm.Fsm.States) foreach (var action in state.Actions)
                        if (action.GetType().Name == "PlayerDistance") count++;
                    Require(count == 6 && job.enabled && buyer.enabled && npc.enabled);
                });
                check(label + "native distance replacement retains local player distance", () =>
                {
                    Node(root, "PLAYER").transform.position = new Vector3(123, 0, 0);
                    var state = NativeBagPartChecks.State(lod, "ON");
                    foreach (var action in state.Actions) if (action.GetType().Name == "PlayerDistance") action.OnEnter();
                    Require(Math.Abs(lod.FsmVariables.FindFsmFloat("Distance").Value - 123) < .01f);
                });
                check(label + "cleanup restores native action arrays exactly", () =>
                {
                    Call(binding!, "Restore"); binding = null;
                    foreach (var pair in original) Require(ReferenceEquals(pair.Key.Actions, pair.Value));
                });
                check(label + "changed job reference refuses binding before any edit", () =>
                {
                    var reference = buyer.FsmVariables.FindFsmGameObject("JobData"); var saved = reference.Value; reference.Value = payment.gameObject;
                    try { make(false); throw new InvalidOperationException("Wrong reference accepted."); }
                    catch (TargetInvocationException e) { Require(e.InnerException is InvalidOperationException); }
                    finally { reference.Value = saved; }
                    foreach (var pair in original) Require(ReferenceEquals(pair.Key.Actions, pair.Value));
                });
                check(label + "relocated native buyer retains its canonical payment identity", () =>
                {
                    var config = Binding.GetMethod("ConfigFor", Members).Invoke(null, new object[] { payment });
                    Require(config != null);
                    var oldParent = npc.transform.parent;
                    npc.transform.SetParent(Node(root, prefix + "/LOD/RelocatedBuyer").transform, true);
                    try
                    {
                        Require(ReferenceEquals(config, Binding.GetMethod("ConfigFor", Members).Invoke(null, new object[] { payment })));
                        var relocated = Binding.GetMethod("TryCreate", Members).Invoke(null, new object[] { 42u, payment, guard, config!, false });
                        Require(relocated != null); Call(relocated!, "Restore");
                    }
                    finally { npc.transform.SetParent(oldParent, true); }
                });
                var money = payment.FsmVariables.FindFsmFloat("Money"); var value = payment.FsmVariables.FindFsmString("Value");
                money.Value = 17; value.Value = "original"; bool active = payment.gameObject.activeSelf;
                check(label + "guest waits for host with native job and buyer decisions paused", () =>
                {
                    binding = make(true); Call(binding, "Present");
                    Require(!job.enabled && !buyer.enabled && !npc.enabled && !payment.gameObject.activeInHierarchy);
                });
                // The fixture has no camera. Keep the native input state waiting
                // while checking the production label, collider and revision paths.
                foreach (string name in new[] { "Wait player", "Wait button" })
                    foreach (var action in NativeBagPartChecks.State(payment, name).Actions)
                        if (action.GetType().Name == "MousePickEvent" || action.GetType().Name == "GetMouseButtonDown") action.Enabled = false;
                var offer = new FirewoodBuyerState { NetId = 42, Revision = 1, Flags = 3, Amount = 500 };
                check(label + "host offer enables native collection and correct amount", () =>
                {
                    Call(binding!, "Receive", offer); Call(binding!, "Present");
                    Require(payment.gameObject.activeInHierarchy && buyer.gameObject.activeInHierarchy && money.Value == 500 && value.Value == "TAKE MONEY 500 MK");
                });
                check(label + "host buyer pose follows movement without running guest decisions", () =>
                {
                    var moved = new FirewoodBuyerState { NetId = 42, Revision = 2, Flags = 3, Amount = 500,
                        Position = new WinterMP.Net.NetVector3(20, 3, -15), Rotation = new WinterMP.Net.NetQuaternion(0, 1, 0, 0) };
                    Call(binding!, "Receive", moved); Call(binding!, "Present");
                    Require(Vector3.Distance(npc.transform.position, new Vector3(20, 3, -15)) < .001f
                        && Quaternion.Angle(npc.transform.rotation, new Quaternion(0, 1, 0, 0)) < .001f && !buyer.enabled);
                    offer.Revision = 3; Call(binding!, "Receive", offer); Call(binding!, "Present");
                });
                check(label + "join snapshots do not consume the broadcast baseline", () =>
                {
                    var state = (FirewoodBuyerState)Call(binding!, "Capture");
                    Call(binding!, "Capture"); Require((bool)Call(binding!, "ShouldBroadcast", state, false));
                    Require(!(bool)Call(binding!, "ShouldBroadcast", state, false));
                });
                check(label + "collected state hides payment and refuses a delayed old offer", () =>
                {
                    Call(binding!, "Receive", new FirewoodBuyerState { NetId = 42, Revision = 4, Flags = 1 }); Call(binding!, "Present");
                    Call(binding!, "Receive", offer); Call(binding!, "Present");
                    Require(!payment.gameObject.activeInHierarchy && buyer.gameObject.activeInHierarchy && money.Value == 0);
                });
                check(label + "fresh offer and equal-revision LOD repair preserve the amount", () =>
                {
                    offer.Revision = 5; offer.Amount = 123.5f; Call(binding!, "Receive", offer); Call(binding!, "Present");
                    payment.gameObject.SetActive(false); money.Value = 999;
                    Call(binding!, "Receive", offer); Call(binding!, "Present");
                    Require(payment.gameObject.activeInHierarchy && money.Value == 123.5f && value.Value == "TAKE MONEY 123.5 MK");
                });
                check(label + "guest cleanup restores amount label activation and FSM flags", () =>
                {
                    Call(binding!, "Restore"); binding = null;
                    Require(job.enabled && buyer.enabled && npc.enabled && money.Value == 17 && value.Value == "original" && payment.gameObject.activeSelf == active);
                    foreach (var pair in original) Require(ReferenceEquals(pair.Key.Actions, pair.Value));
                });
            }
            finally { if (binding != null) Call(binding, "Restore"); UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
