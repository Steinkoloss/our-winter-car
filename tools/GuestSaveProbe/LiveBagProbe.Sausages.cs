using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool SausageProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_SAUSAGE_TEST") == "1";
        private static readonly List<SausageState> CachedSausages = new List<SausageState>();
        private static object SausageSource(int index)
        {
            var sources = (IDictionary)Get(Items, "_sausageSources");
            var found = new List<object>(); foreach (DictionaryEntry e in sources) found.Add(e.Value);
            found.Sort((a,b) => string.CompareOrdinal(PathOf(((PlayMakerFSM)Get(a, "Fsm")).transform), PathOf(((PlayMakerFSM)Get(b, "Fsm")).transform)));
            return found[index];
        }
        private static object SausageItem(string id) => ((IDictionary)Get(Items, "_items"))[uint.Parse(id)] ?? throw new InvalidOperationException("Missing sausage fixture item.");
        private static PlayMakerFSM FoodUse(GameObject obj)
        {
            foreach (var fsm in obj.GetComponents<PlayMakerFSM>()) if (fsm.FsmName == "Use") return fsm;
            throw new InvalidOperationException("Food Use missing.");
        }
        private static bool SausageCommand(string[] args, List<string> rows)
        {
            if (!SausageProbe || !args[1].StartsWith("sausage-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox(); var session = SessionManager.Instance!;
            switch (args[1])
            {
                case "sausage-view": return true;
                case "sausage-scan": Call(Items, "ScanItems"); return true;
                case "sausage-new":
                    if (!session.IsHost) throw new InvalidOperationException("Host-only fixture.");
                    var factory = Find("Spawner/CreateItems", "Sausages");
                    FsmVariables.GlobalVariables.FindFsmGameObject("ShoppingBagSpawn").Value = factory.FsmVariables.FindFsmGameObject("Prefab").Value;
                    Enter(factory, "Create product");
                    var product = factory.FsmVariables.FindFsmGameObject("New").Value;
                    var rb = product.GetComponent<Rigidbody>(); rb.transform.position = ((Collider)Get(SausageSource(4), "Trigger")).bounds.center + new Vector3(0, 1.5f, 0);
                    rb.useGravity = false; rb.constraints = RigidbodyConstraints.FreezeAll;
                    FoodUse(product).FsmVariables.FindFsmFloat("Condition").Value = float.Parse(args[2], CultureInfo.InvariantCulture);
                    rows.Add("sausage-created|" + product.name); return true;
                case "sausage-near":
                case "sausage-far":
                    Player!.position = ((Collider)Get(SausageSource(int.Parse(args[2])), "Trigger")).bounds.center + new Vector3(0, 0, args[1] == "sausage-near" ? 1.5f : 30); return true;
                case "sausage-place":
                    var item = SausageItem(args[2]); var body = (Rigidbody)Get(item, "Body");
                    body.transform.position = ((Collider)Get(SausageSource(int.Parse(args[3])), "Trigger")).bounds.center + new Vector3(0, float.Parse(args[4], CultureInfo.InvariantCulture), 0);
                    body.useGravity = false; body.constraints = RigidbodyConstraints.FreezeAll;
                    Call(Items, "ClaimItem", session, item, body, Time.unscaledTime); return true;
                case "sausage-open":
                    var source = SausageSource(int.Parse(args[3])); var fsm = (PlayMakerFSM)Get(source, "Fsm");
                    fsm.FsmVariables.FindFsmGameObject("Package").Value = ((Rigidbody)Get(SausageItem(args[2]), "Body")).gameObject;
                    Enter(fsm, "State 4"); return true;
                case "sausage-park":
                    var sfsm = (PlayMakerFSM)Get(SausageSource(int.Parse(args[2])), "Fsm");
                    foreach (var state in sfsm.Fsm.States) if (state.Name == "State 3")
                        foreach (var action in state.Actions) if (action.GetType().Name == "Wait") ((FsmFloat)action.GetType().GetField("time").GetValue(action)).Value = float.Parse(args[3], CultureInfo.InvariantCulture);
                    Enter(sfsm, "State 3"); return true;
                case "sausage-request":
                    if (session.IsHost) throw new InvalidOperationException("Guest request fixture.");
                    session.SendWorldMessage(new SausageOpenIntent { SourceId = (uint)Get(SausageSource(int.Parse(args[3])), "Id"), PackageId = uint.Parse(args[2]), PlayerId = byte.Parse(args[4]), Sequence = uint.Parse(args[5]) }, Channel.ReliableOrdered); return true;
                case "sausage-eat": Enter(FoodUse(((Rigidbody)Get(SausageItem(args[2]), "Body")).gameObject), "State 2"); return true;
                case "sausage-food":
                    if (!session.IsHost) throw new InvalidOperationException("Host food fixture.");
                    var use = FoodUse(((Rigidbody)Get(SausageItem(args[2]), "Body")).gameObject);
                    if (args[3] == "condition") use.FsmVariables.FindFsmFloat("Condition").Value = float.Parse(args[4], CultureInfo.InvariantCulture);
                    else use.SendEvent(args[3]); return true;
                case "sausage-cache":
                    CachedSausages.Clear(); foreach (var state in (IEnumerable)Call(Items, "BuildSausageStates")) CachedSausages.Add((SausageState)state); return true;
                case "sausage-replay": foreach (var state in CachedSausages) session.SendWorldMessage(state, Channel.ReliableOrdered); return true;
                default: throw new InvalidOperationException("Unknown sausage fixture.");
            }
        }
        private static void SausageSnapshot(List<string> rows)
        {
            if (Application.loadedLevelName != "GAME") return;
            rows.Add("sausage-sync|" + Get(Items, "_sausageFailed") + "|" + ((IDictionary)Get(Items, "_sausageSources")).Count);
            int n = 0;
            foreach (DictionaryEntry e in (IDictionary)Get(Items, "_sausageSources"))
            {
                var fsm = (PlayMakerFSM)Get(e.Value, "Fsm"); var trigger = (Collider)Get(e.Value, "Trigger");
                rows.Add("sausage-trigger|" + n++ + "|" + e.Key + "|" + PathOf(fsm.transform) + "|" + fsm.enabled + "|" + fsm.gameObject.activeInHierarchy + "|" + fsm.ActiveStateName + "|" + Vector(trigger.bounds.center));
            }
            foreach (DictionaryEntry e in (IDictionary)Get(Items, "_items"))
            {
                var body = Get(e.Value, "Body") as Rigidbody;
                if (body == null) continue;
                var use = FoodUseOrNull(body.gameObject); if (use == null) continue;
                string nativeId = use.FsmVariables.FindFsmString("ID")?.Value ?? "";
                if (!nativeId.StartsWith("sausages", StringComparison.Ordinal) && body.name != "sausages(itemx)") continue;
                rows.Add("sausage-package|" + e.Key + "|" + nativeId + "|" + body.name + "|" + use.FsmVariables.FindFsmFloat("Condition").Value + "|" + Vector(body.position) + "|" + Get(e.Value, "RemoteOwner"));
            }
            foreach (DictionaryEntry e in (IDictionary)Get(Items, "_sausages"))
            {
                var body = Get(e.Value, "Body") as Rigidbody; if (body == null) continue;
                var use = (PlayMakerFSM)Get(e.Value, "Use"); PlayMakerFSM? fire = null;
                foreach (var f in body.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Fire") fire = f;
                rows.Add("sausage-item|" + e.Key + "|" + body.name + "|" + use.FsmVariables.FindFsmFloat("Condition").Value.ToString("R", CultureInfo.InvariantCulture)
                    + "|" + use.FsmVariables.FindFsmBool("Grilled").Value + "|" + Vector(body.position) + "|" + Get(e.Value, "Replica") + "|" + use.enabled + "|" + (fire != null && fire.enabled));
            }
            foreach (var peer in SessionManager.Instance!.Players) rows.Add("sausage-peer|" + peer.PlayerId + "|" + Vector(peer.Position) + "|" + (Time.unscaledTime - peer.LastTransformTime));
        }
        private static PlayMakerFSM? FoodUseOrNull(GameObject obj)
        { foreach (var f in obj.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Use") return f; return null; }
    }
}
