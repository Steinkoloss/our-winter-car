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
        private static bool CoffeeProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_COFFEE_TEST") == "1";
        private const string CoffeePotPath = "EQUIPMENTS/coffee pan(itemx)", CoffeeCupPath = "EQUIPMENTS/coffee cup(itemx)";
        private static object CoffeeBinding(string target)
        {
            var bindings = (IDictionary)Get(Items, "_coffee");
            foreach (DictionaryEntry e in bindings)
                if (target == e.Key.ToString() || target == "pot" && (byte)Get(e.Value, "Kind") == 0 || target == "cup" && (byte)Get(e.Value, "Kind") == 1) return e.Value;
            throw new InvalidOperationException("Coffee binding missing: " + target);
        }
        private static Rigidbody CoffeeBody(string target) => (Rigidbody)Get(CoffeeBinding(target), "Body");
        private static PlayMakerFSM CoffeeNative(string path, string name)
        {
            string target = path.StartsWith(CoffeePotPath, StringComparison.Ordinal) ? "pot" : "cup";
            string root = target == "pot" ? CoffeePotPath : CoffeeCupPath;
            var body = CoffeeBody(target); var transform = path == root ? body.transform : body.transform.Find(path.Substring(root.Length + 1));
            foreach (var f in transform.GetComponents<PlayMakerFSM>()) if (f.FsmName == name) return f;
            throw new InvalidOperationException("Missing coffee native FSM.");
        }
        private static float CoffeeNumber(string text) => float.Parse(text, CultureInfo.InvariantCulture);
        private static bool CoffeeCommand(string[] a, List<string> rows)
        {
            if (!CoffeeProbe || !a[1].StartsWith("coffee-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox(); var session = SessionManager.Instance!;
            switch (a[1])
            {
                case "coffee-view": return true;
                case "coffee-scan": Call(Items, "ScanItems"); return true;
                case "coffee-near": Player!.position = CoffeeBody(a[2]).position + new Vector3(0, 0, a.Length > 3 ? CoffeeNumber(a[3]) : 1); return true;
                case "coffee-place":
                    var rb = CoffeeBody(a[2]); rb.transform.position = new Vector3(CoffeeNumber(a[3]), CoffeeNumber(a[4]), CoffeeNumber(a[5]));
                    rb.transform.rotation = Quaternion.Euler(a.Length > 6 ? CoffeeNumber(a[6]) : 0, 0, 0);
                    rb.useGravity = false; rb.constraints = RigidbodyConstraints.FreezeAll;
                    Call(Items, "ClaimItem", session, ((IDictionary)Get(Items, "_items"))[(uint)Get(CoffeeBinding(a[2]), "Id")], rb, Time.unscaledTime); return true;
                case "coffee-pick":
                    var pickup = Find(Hand, "PickUp"); pickup.FsmVariables.FindFsmGameObject("PickedObject").Value = CoffeeBody(a[2]).gameObject;
                    Enter(pickup, "Set pivot 2"); return true;
                case "coffee-drop": Find(Hand, "PickUp").SendEvent("DROP_PART"); return true;
                case "coffee-lid": Enter(CoffeeNative(CoffeePotPath + "/Cap", "Use"), a[2] == "open" ? "ON" : "OFF"); return true;
                case "coffee-fill": Enter(CoffeeNative(CoffeeCupPath, "Use"), "State 5"); return true;
                case "coffee-drink": Enter(CoffeeNative(CoffeeCupPath, "Use"), "Check drink"); return true;
                case "coffee-align":
                    // Move the host-owned pot to the already held cup's native pour target.
                    if (!session.IsHost) throw new InvalidOperationException("Host alignment fixture only.");
                    var pot = CoffeeBody("pot"); pot.transform.position += CoffeeBody("cup").transform.Find("PourTarget").position - pot.transform.Find("PanTarget").position;
                    pot.useGravity = false; pot.constraints = RigidbodyConstraints.FreezeAll; return true;
                case "coffee-native":
                    if (!session.IsHost) throw new InvalidOperationException("Host native input only.");
                    if (a[2] == "heat") CoffeeNative(CoffeePotPath, "Data").SendEvent(a[3] == "on" ? "ONFIRE" : "OFF");
                    else if (a[2] == "water") Enter(CoffeeNative(CoffeePotPath + "/Functions/TriggerWater", "Water"), a[3]);
                    else if (a[2] == "grounds")
                    { var f = CoffeeNative(CoffeePotPath + "/Functions/TriggerCoffee", "Coffee"); f.FsmVariables.FindFsmGameObject("Package").Value = CoffeeBody(a[3]).gameObject; Enter(f, "Check rotation"); }
                    else throw new InvalidOperationException("Unknown native coffee fixture.");
                    return true;
                case "coffee-seed":
                    if (!session.IsHost) throw new InvalidOperationException("Host fixture only.");
                    var use = (PlayMakerFSM)Get(CoffeeBinding(a[2]), "Fsm");
                    foreach (var assignment in a[3].Split(',')) { var fields = assignment.Split('='); use.FsmVariables.FindFsmFloat(fields[0]).Value = CoffeeNumber(fields[1]); } return true;
                case "coffee-new":
                    if (!session.IsHost) throw new InvalidOperationException("Host creation fixture only.");
                    var factory = Find("Spawner/CreateItems", "Coffee");
                    FsmVariables.GlobalVariables.FindFsmGameObject("ShoppingBagSpawn").Value = Player!.gameObject;
                    Enter(factory, "Create product"); var product = factory.FsmVariables.FindFsmGameObject("New").Value;
                    var body = product.GetComponent<Rigidbody>(); body.transform.position = CoffeeBody("pot").position + new Vector3(0, 1, 0); body.useGravity = false; body.constraints = RigidbodyConstraints.FreezeAll;
                    rows.Add("coffee-created|" + product.name); return true;
                case "coffee-receipt":
                    if (!session.IsHost) throw new InvalidOperationException("Host receipt fixture.");
                    session.SendWorldMessage(new CoffeeDrinkResult { ItemId = (uint)Get(CoffeeBinding("cup"), "Id"), Sequence = uint.Parse(a[2]),
                        PlayerId = byte.Parse(a[3]), Amount = .3f, Caffeine = 1.5f }, Channel.ReliableOrdered); return true;
                case "coffee-request":
                    if (session.IsHost) throw new InvalidOperationException("Guest request fixture.");
                    session.SendWorldMessage(new CoffeeIntent { ItemId = (uint)Get(CoffeeBinding(a[2]), "Id"), Action = (CoffeeAction)byte.Parse(a[3]), PlayerId = byte.Parse(a[4]), Sequence = uint.Parse(a[5]) }, Channel.ReliableOrdered); return true;
                case "coffee-needs":
                    foreach (string name in new[] { "PlayerFatigue", "PlayerThirst", "PlayerUrine", "PlayerHunger", "PlayerStress" }) FsmVariables.GlobalVariables.FindFsmFloat(name).Value = 40;
                    return true;
                default: throw new InvalidOperationException("Unknown coffee command.");
            }
        }
        private static void CoffeeSnapshot(List<string> rows)
        {
            if (Application.loadedLevelName != "GAME") return;
            rows.Add("coffee-sync|" + Get(Items, "_coffeeFailed") + "|" + ((IDictionary)Get(Items, "_localCoffeePackets")).Count + "|" + Get(Items, "_coffeePendingDrink"));
            rows.Add("player|" + (Player == null ? "none" : Vector(Player.position)));
            rows.Add("coffee-sequence|" + Get(Items, "_coffeeOutSequence"));
            if (((IDictionary)Get(Items, "_coffee")).Count == 0)
            {
                var native = Find(CoffeePotPath, "Data");
                rows.Add("coffee-restored|" + native.enabled + "|" + native.FsmVariables.FindFsmFloat("Water").Value + "|"
                    + Find(CoffeePotPath + "/PanTarget", "Pour").enabled);
                return;
            }
            foreach (DictionaryEntry e in (IDictionary)Get(Items, "_coffee"))
            {
                var b = e.Value; var body = Get(b, "Body") as Rigidbody; if (body == null) continue;
                var f = (PlayMakerFSM)Get(b, "Fsm"); var item = ((IDictionary)Get(Items, "_items"))[e.Key];
                rows.Add("coffee-item|" + e.Key + "|" + Get(b, "Kind") + "|" + body.name + "|" + f.ActiveStateName + "|" + f.enabled
                    + "|" + f.FsmVariables.FindFsmString("ID")?.Value + "|" + Get(b, "Replica") + "|" + (item == null ? "missing" : Get(item, "RemoteOwner"))
                    + "|" + Call(Items, "IsHeldByLocalPlayer", body) + "|" + Vector(body.position));
                foreach (string name in new[] { "Water", "Ground", "Coffee", "Caffeine", "BoilVolume", "Pos" })
                { var v = f.FsmVariables.FindFsmFloat(name); if (v != null) rows.Add("coffee-value|" + e.Key + "|" + name + "|" + v.Value.ToString("R", CultureInfo.InvariantCulture)); }
            }
            foreach (var peer in SessionManager.Instance!.Players) rows.Add("coffee-peer|" + peer.PlayerId + "|" + Vector(peer.Position) + "|" + (Time.unscaledTime - peer.LastTransformTime));
            var pot = CoffeeNative(CoffeePotPath, "Data"); var cup = CoffeeNative(CoffeeCupPath, "Use");
            rows.Add("coffee-pot|" + pot.FsmVariables.FindFsmBool("CapOpen").Value + "|" + CoffeeNative(CoffeePotPath + "/Cap", "Use").ActiveStateName
                + "|" + CoffeeNative(CoffeePotPath + "/PanTarget", "Pour").enabled + "|" + pot.transform.Find("BoilSound").gameObject.activeSelf
                + "|" + pot.transform.Find("coffee_pan_cap").gameObject.activeSelf + "|" + pot.transform.Find("Functions").gameObject.activeSelf);
            rows.Add("coffee-distance|" + Vector(pot.transform.Find("PanTarget").position) + "|" + Vector(cup.transform.Find("PourTarget").position)
                + "|" + Vector3.Distance(pot.transform.Find("PanTarget").position, cup.transform.Find("PourTarget").position));
            var hand = Find(Hand, "PickUp"); rows.Add("coffee-hand|" + hand.ActiveStateName + "|" + Identity(hand.FsmVariables.FindFsmGameObject("PickedObject").Value));
            foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = obj as PlayMakerFSM; if (f == null) continue;
                if (f.FsmName == "Use" && (f.FsmVariables.FindFsmString("ID")?.Value ?? "").StartsWith("groundcoffee", StringComparison.Ordinal))
                    rows.Add("coffee-native-packet|" + f.FsmVariables.FindFsmString("ID").Value + "|" + f.enabled + "|" + f.gameObject.activeInHierarchy);
                if (f.gameObject.name == "Drink" && f.FsmVariables.FindFsmFloat("CoffeeHomeCoffee") != null)
                    rows.Add("coffee-effect|" + f.ActiveStateName + "|" + f.FsmVariables.FindFsmFloat("CoffeeHomeCoffee").Value + "|" + f.FsmVariables.FindFsmFloat("CoffeeHomeCaffeine").Value);
            }
        }
    }
}
