using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool _meatTracing;
        private static object? _meatMoverHold;
        private static string _meatDespawnTrace = "none";
        private static void MeatDespawnTrace(object __instance, WinterMP.Net.Messages.ItemDespawn message, byte playerId, bool __result)
        {
            var items = (IDictionary)Get(__instance, "_items");
            var item = items[message.ItemId];
            string detail = item == null ? "missing" : "owner=" + Get(item, "RemoteOwner") + ";motion=" + Call(__instance, "CanSyncItemMotion", item);
            foreach (var peer in SessionManager.Instance!.Players)
            {
                if (peer.PlayerId != playerId) continue;
                var body = item != null ? Get(item, "Body") as Rigidbody : null;
                detail += ";age=" + (Time.unscaledTime - peer.LastTransformTime) + ";distance=" + (body != null ? Vector3.Distance(peer.Position, body.position) : -1);
            }
            _meatDespawnTrace = message.ItemId + "|" + __result + "|" + detail;
        }

        private static bool MeatCommand(string[] args, List<string> rows)
        {
            if (!args[1].StartsWith("meat-", StringComparison.Ordinal)) return false;
            if (Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_MEAT_TEST") != "1" || !Persistence)
                throw new InvalidOperationException("Meat probe requires marked disposable persistence profiles.");
            RequirePersistenceSandbox();
            if (!_meatTracing)
            {
                new Harmony("com.ourwintercar.probe.meat").Patch(Items.GetType().GetMethod("TryAcceptGuestDespawn", Members),
                    postfix: new HarmonyMethod(typeof(LiveBagProbe).GetMethod("MeatDespawnTrace", Members)));
                _meatTracing = true;
            }
            var session = SessionManager.Instance!;
            var factory = Find("Spawner/CreateMooseMeat", "MooseMeat");
            var meat = (IDictionary)Get(Items, "_meat");
            switch (args[1])
            {
                case "meat-visit":
                    if (Player == null) throw new InvalidOperationException("Player missing.");
                    var hit = Find("AnimalsMoose/Moose/Offset/Mesh/Collider", "CarHit");
                    Player.position = hit.transform.position + new Vector3(0, 1, 3);
                    for (var t = hit.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
                    hit.enabled = true; break;
                case "meat-death":
                    Enter(Find("AnimalsMoose/Moose/Offset/Mesh/Collider", "CarHit"), "State 2"); break;
                case "meat-hold":
                    var move = Find("AnimalsMoose/Moose", "Move");
                    _meatMoverHold = _meatMoverHold ?? Activator.CreateInstance(typeof(SessionManager).Assembly.GetType("WinterMP.Core.Sync.FsmSuppressor", true), true);
                    if (!(bool)Call(_meatMoverHold, "Suppress", move)) throw new InvalidOperationException("Could not hold test moose.");
                    if (Player != null) Player.position = args.Length == 5
                        ? new Vector3(float.Parse(args[2], CultureInfo.InvariantCulture), float.Parse(args[3], CultureInfo.InvariantCulture), float.Parse(args[4], CultureInfo.InvariantCulture))
                        : move.transform.position + new Vector3(0, 1, 10);
                    break;
                case "meat-request":
                    if (session.IsHost) throw new InvalidOperationException("Guest request fixture only.");
                    var moose = Get(Items, "_moose");
                    var receipt = (WinterMP.Net.Messages.MooseCorpseState)Get(moose, "Received");
                    session.SendWorldMessage(new WinterMP.Net.Messages.MooseChopIntent {
                        PlayerId = args.Length > 5 ? byte.Parse(args[5]) : session.LocalPlayerId,
                        Section = byte.Parse(args[2]), ExpectedPieces = byte.Parse(args[3]),
                        Corpse = args.Length > 4 ? uint.Parse(args[4]) : receipt.Corpse }, WinterMP.Net.Channel.ReliableOrdered);
                    break;
                case "meat-corpse-near":
                case "meat-axe":
                    var target = FindMeatChop(args.Length > 2 && args[2] == "1");
                    if (Player == null) throw new InvalidOperationException("Player missing.");
                    if (args[1] == "meat-corpse-near") Player.position = target.transform.position + new Vector3(0, 1, 2);
                    else
                    {
                        var axe = Find("PLAYER/Pivot/AnimPivot/Camera/Ax", "Use");
                        target.FsmVariables.FindFsmGameObject("Object").Value = args.Length > 3 ? Player.gameObject : axe.transform.Find("Pivot").gameObject;
                        Enter(target, "State 2");
                    }
                    break;
                case "meat-chop":
                    if (!session.IsHost) throw new InvalidOperationException("Host chopping test only.");
                    PlayMakerFSM? chop = null;
                    foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
                    {
                        var fsm = (PlayMakerFSM)obj;
                        if (fsm.FsmName == "Chop" && fsm.gameObject.activeInHierarchy
                            && ((args.Length <= 2 && fsm.gameObject.name.StartsWith("dead moose", StringComparison.Ordinal))
                                || (args.Length > 2 && fsm.gameObject.name == "spine2" && PathOf(fsm.transform).Contains("dead moose")))) { chop = fsm; break; }
                    }
                    if (chop == null || Player == null) throw new InvalidOperationException("Native corpse not available.");
                    Player.position = chop.transform.position + new Vector3(0, 1, 2);
                    rows.Add("chop-before|" + chop.FsmVariables.FindFsmInt("Pieces").Value);
                    Enter(chop, "Pieces"); break;
                case "meat-local":
                    if (session.State != SessionState.Idle || Player == null) throw new InvalidOperationException("Disconnected fixture only.");
                    factory.FsmVariables.FindFsmGameObject("SpawnPoint").Value = Player.gameObject;
                    factory.SendEvent("SPAWNITEM"); break;
                case "meat-food":
                    if (!session.IsHost) throw new InvalidOperationException("Host food fixture only.");
                    var binding = meat[uint.Parse(args[2])];
                    var use = (PlayMakerFSM)Get(binding, "Use");
                    string state = args[3] == "grilled" ? "Grilled" : args[3] == "charred" ? "Done 2" : args[3] == "rotten" ? "Bad" : "Bad 2";
                    use.FsmVariables.FindFsmFloat("Condition").Value = args[3] == "grilled" ? 100 : .5f;
                    Enter(use, state); break;
                case "meat-near":
                    if (Player == null) throw new InvalidOperationException("Player missing.");
                    Player.position = ((Rigidbody)Get(meat[uint.Parse(args[2])], "Body")).position + new Vector3(0, 0, 1); break;
                case "meat-eat": Enter((PlayMakerFSM)Get(meat[uint.Parse(args[2])], "Use"), "Eat"); break;
                case "meat-list": break;
                default: throw new InvalidOperationException("Unknown meat command.");
            }
            rows.Add("meat-factory|" + factory.ActiveStateName + "|" + factory.enabled + "|" + Get(Items, "_meatFailed")
                + "|" + factory.FsmVariables.FindFsmString("SaveID").Value + "|" + factory.FsmVariables.FindFsmInt("ObjectNumberInt").Value);
            rows.Add("meat-despawn-trace|" + _meatDespawnTrace);
            var bound = Get(Items, "_moose");
            rows.Add("chop-binding|" + Get(Items, "_mooseFailed") + "|" + (bound == null ? "none" : Get(bound, "Epoch")));
            if (bound != null)
            {
                var received = Get(bound, "Received") as WinterMP.Net.Messages.MooseCorpseState;
                if (received != null) rows.Add("chop-state|" + received.Corpse + "|" + received.Revision + "|" + received.Dead
                    + "|" + received.FrontPieces + "|" + received.RearPieces);
            }
            string events = (string)typeof(SessionManager).Assembly.GetType("WinterMP.Core.Diagnostics.SyncEventLog", true)
                .GetMethod("GetSnapshot", Members).Invoke(null, null);
            foreach (string line in events.Split('\n'))
                if (line.Contains("moose-death-report")) rows.Add("chop-death-report|" + line.Trim());
            foreach (var peer in session.Players)
                rows.Add("meat-peer|" + peer.PlayerId + "|" + Vector(peer.Position) + "|" + (Time.unscaledTime - peer.LastTransformTime) + "|" + peer.IsDead);
            foreach (DictionaryEntry entry in meat)
            {
                var body = Get(entry.Value, "Body") as Rigidbody;
                if (body == null) continue;
                var use = (PlayMakerFSM)Get(entry.Value, "Use");
                rows.Add("meat|" + entry.Key + "|" + Get(entry.Value, "NativeId") + "|" + Get(entry.Value, "Replica") + "|" + body.gameObject.activeInHierarchy
                    + "|" + use.ActiveStateName + "|" + use.FsmVariables.FindFsmFloat("Condition").Value.ToString("R", CultureInfo.InvariantCulture)
                    + "|" + use.FsmVariables.FindFsmInt("Type").Value + "|" + body.name + "|" + Vector(body.position) + "|" + body.GetInstanceID());
            }
            foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var use = (PlayMakerFSM)obj;
                if (use.FsmName == "Chop" && PathOf(use.transform).Contains("dead moose"))
                    rows.Add("corpse|" + PathOf(use.transform) + "|" + use.gameObject.activeInHierarchy + "|" + use.enabled
                        + "|" + use.ActiveStateName + "|" + use.FsmVariables.FindFsmInt("Pieces").Value + "|" + Vector(use.transform.position));
                if (use.FsmName != "Use") continue;
                string id = use.FsmVariables.FindFsmString("ID")?.Value ?? "";
                if (id.StartsWith("wintermp-meat-", StringComparison.Ordinal))
                    rows.Add("meat-view|" + id + "|" + use.GetInstanceID() + "|" + (use.GetComponent<Rigidbody>() != null));
                if (!id.StartsWith("moosemeat0", StringComparison.Ordinal)) continue;
                rows.Add("native-meat|" + id + "|" + use.gameObject.activeInHierarchy + "|" + use.enabled + "|" + use.GetInstanceID()
                    + "|" + use.FsmVariables.FindFsmFloat("Condition").Value.ToString("R", CultureInfo.InvariantCulture) + "|" + use.ActiveStateName);
            }
            return true;
        }

        private static PlayMakerFSM FindMeatChop(bool spine)
        {
            foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var fsm = (PlayMakerFSM)obj;
                if (fsm.FsmName == "Chop" && PathOf(fsm.transform).Contains("dead moose")
                    && (spine ? fsm.name == "spine2" : fsm.name.StartsWith("dead moose", StringComparison.Ordinal))) return fsm;
            }
            throw new InvalidOperationException("Native corpse missing.");
        }
    }
}
