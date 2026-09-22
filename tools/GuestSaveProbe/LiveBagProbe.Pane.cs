using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private const string PanePath = "CORRIS/BODY/Windshield/collider";
        private const string PaneClimatePath = "CORRIS/Simulation/CarTempCorris";

        private static string _paneAim = string.Empty;
        private static object PaneBridge => typeof(SessionManager).Assembly.GetType("WinterMP.Core.Sync.PaneScrapeSync", true)
            .GetProperty("Instance", Members).GetValue(null, null);
        private static Rigidbody ScraperBody()
        {
            foreach (DictionaryEntry entry in (IDictionary)Get(Items, "_items"))
            {
                var body = (Rigidbody)Get(entry.Value, "Body");
                if (body != null && body.name == "ice scraper(itemx)") return body;
            }
            throw new InvalidOperationException("Shared scraper not discovered.");
        }
        private static void TickPaneAim()
        {
            if (!PaneProbe || _paneAim.Length == 0 || Player == null || Application.loadedLevelName != "GAME") return;
            var eye = GameObject.Find("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Camera/Camera").transform;
            var car = (Rigidbody)Get(PaneItem(), "Body");
            var target = _paneAim == "tool" ? ScraperBody().GetComponent<Collider>() : Find(PanePath, "Scrape").GetComponent<Collider>();
            var origin = target.bounds.center + car.transform.forward * (_paneAim == "far" ? 1.4f : .6f);
            Player.position += origin - eye.position;
            eye.rotation = Quaternion.LookRotation(-car.transform.forward);
        }
        private static object PaneItem()
        {
            foreach (DictionaryEntry entry in (IDictionary)Get(Items, "_items"))
                if ((string)Get(entry.Value, "Path") == "CORRIS") return entry.Value;
            throw new InvalidOperationException("Corris not registered.");
        }
        private static bool PaneCommand(string[] args, List<string> rows)
        {
            try { return ExecutePaneCommand(args, rows); }
            catch { ClearPaneMutation(); throw; }
        }
        private static bool ExecutePaneCommand(string[] args, List<string> rows)
        {
            if (!args[1].StartsWith("pane-", StringComparison.Ordinal)) return false;
            if (!PaneProbe || !File.Exists(Path.Combine(BepInEx.Paths.GameRootPath, "wintermp-v11-pane-sandbox.txt")))
                throw new InvalidOperationException("V11 pane audit requires opt-in and disposable marker.");
            switch (args[1])
            {
                case "pane-snapshot": return true;
                case "pane-tool-describe":
                    Describe(Find("Spawner/CreateItems", "IceScraper"), rows);
                    foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(Rigidbody)))
                    {
                        var found = obj as Rigidbody;
                        if (found != null && found.name.ToLowerInvariant().Contains("scrap"))
                            rows.Add("scraper-body|" + PathOf(found.transform) + "|" + found.gameObject.activeInHierarchy);
                    }
                    return true;
                case "pane-fixture-tool":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Only host seeds initial shared tool through native factory.");
                    var factory = Find("Spawner/CreateItems", "IceScraper");
                    if (factory.FsmVariables.FindFsmInt("ObjectNumberInt").Value != 0)
                        throw new InvalidOperationException("Fixture requires an initially empty scraper factory; never add duplicates.");
                    var spawn = new GameObject("V11FixtureToolSpawn");
                    spawn.transform.position = ((Rigidbody)Get(PaneItem(), "Body")).transform.TransformPoint(new Vector3(2, 1, 1));
                    var point = FsmVariables.GlobalVariables.FindFsmGameObject("ShoppingBagSpawn");
                    var previous = point.Value;
                    try { point.Value = spawn; factory.SendEvent("SPAWNITEM"); }
                    finally { point.Value = previous; UnityEngine.Object.Destroy(spawn); }
                    rows.Add("fixture|initial host tool created by native IceScraper.SPAWNITEM; no equipment lease, pane result or guest value injected");
                    return true;
                case "pane-aim-tool": _paneAim = "tool"; TickPaneAim(); rows.Add("fixture|teleport and hold real camera aimed at shared scraper; no lease injection"); return true;
                case "pane-aim-glass": _paneAim = "glass"; TickPaneAim(); rows.Add("fixture|teleport and hold real camera aimed at windshield; host still raycasts"); return true;
                case "pane-aim-far": _paneAim = "far"; TickPaneAim(); rows.Add("fixture|camera beyond native .8m reach; no contact observation injected"); return true;
                case "pane-replay-stroke":
                    if (_paneLastStroke == null) throw new InvalidOperationException("No real outgoing stroke to replay.");
                    SessionManager.Instance!.SendWorldMessage(_paneLastStroke, WinterMP.Net.Channel.ReliableOrdered);
                    rows.Add("fixture|replayed exact previously emitted stroke over real transport"); return true;
                case "pane-arm-wrong-pane":
                    ArmPaneWrongPane(); rows.Add("fixture|armed next outgoing guest stroke: Pane only; no action emitted"); return true;
                case "pane-cancel-wrong-pane": ClearPaneMutation(); return true;
                case "pane-replay-original-stroke":
                    if (_paneOriginalStroke == null) throw new InvalidOperationException("No original outgoing stroke to replay.");
                    SessionManager.Instance!.SendWorldMessage(_paneOriginalStroke, WinterMP.Net.Channel.ReliableOrdered);
                    rows.Add("fixture|replayed original production packet with unchanged consumed sequence"); return true;
                case "pane-release-aim": _paneAim = string.Empty; return true;
                case "pane-pickup-tool":
                    var hand = Find(Hand, "PickUp");
                    hand.FsmVariables.FindFsmGameObject("PickedObject").Value = ScraperBody().gameObject;
                    Enter(hand, "Set pivot 2");
                    rows.Add("fixture|native picked-object selection and pickup entry; production gate must authorize first-hit contact"); return true;
                case "pane-equip-tool": Enter(Find(Hand, "PickUp"), "Ice Scraper"); return true;
                case "pane-stroke-bridge":
                    Enter(Find(PanePath, "Scrape"), "Get this glass");
                    Enter(Find(PanePath, "Scrape"), "Scrape 2");
                    rows.Add("fixture|native stroke entry only; production SendEvent replacement emits intent"); return true;
                case "pane-describe":
                    Describe(Find(PanePath, "Scrape"), rows);
                    Describe(Find(PaneClimatePath, "Freezing"), rows);
                    return true;
                case "pane-seed":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Only host seeds initial fixture ice.");
                    Find(PaneClimatePath, "Freezing").FsmVariables.FindFsmFloat("CutoffWindshield").Value = .25f;
                    rows.Add("fixture|initial windshield cutoff=.25; no result injected; other panes unchanged");
                    return true;
                case "pane-scrape":
                    var scrape = Find(PanePath, "Scrape");
                    var freeze = Find(PaneClimatePath, "Freezing");
                    float before = freeze.FsmVariables.FindFsmFloat("CutoffWindshield").Value;
                    // Audited native tool-enable and stroke states, not a forged climate result.
                    // Physical pickup, mouse button and camera reversal are explicitly bypassed.
                    Enter(Find(Hand, "PickUp"), "Ice Scraper");
                    Enter(scrape, "Get this glass");
                    Enter(scrape, "Scrape 2");
                    rows.Add("stroke|" + before.ToString("R", CultureInfo.InvariantCulture) + "|"
                        + freeze.FsmVariables.FindFsmFloat("CutoffWindshield").Value.ToString("R", CultureInfo.InvariantCulture));
                    rows.Add("fixture|native Hand.Ice Scraper -> Scrape.Get this glass -> Scrape 2 -> WINDSHIELD; input entry bypass only");
                    return true;
                case "pane-contact":
                    var coll = Find(PanePath, "Scrape").GetComponent<Collider>();
                    var center = coll.bounds.center;
                    var normal = ((Rigidbody)Get(PaneItem(), "Body")).transform.forward;
                    RaycastHit hit;
                    bool near = coll.Raycast(new Ray(center + normal * .6f, -normal), out hit, .8f);
                    rows.Add("contact|near|" + near + "|" + hit.distance.ToString("R", CultureInfo.InvariantCulture));
                    bool far = coll.Raycast(new Ray(center + normal * 2, -normal), out hit, .8f);
                    rows.Add("contact|far|" + far + "|" + hit.distance.ToString("R", CultureInfo.InvariantCulture));
                    rows.Add("contact-limit|native pane collider raycast only; not native camera MousePickEvent or host validation");
                    return true;
                case "pane-park":
                    var body = (Rigidbody)Get(PaneItem(), "Body");
                    if (SessionManager.Instance!.IsHost) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.isKinematic = true; }
                    var player = Player ?? throw new InvalidOperationException("Player missing.");
                    player.position = body.transform.TransformPoint(new Vector3(2, 1, 1));
                    rows.Add("fixture|stationary Corris; player placed outside; no pane value or owner injected");
                    return true;
                default: throw new InvalidOperationException("Unknown V11 pane command.");
            }
        }
        private static void PaneSnapshot(List<string> rows)
        {
            if (Application.loadedLevelName != "GAME") return;
            var session = SessionManager.Instance!;
            if (session.State != SessionState.Hosting && session.State != SessionState.Connected) return;
            Call(WorldSyncManager.Instance!, "FindLocalPlayer");
            rows.Add("player-ready|" + (Player != null));
            var playerSync = PlayerSyncManager.Instance;
            if (playerSync != null)
            {
                rows.Add("pose-local|" + playerSync.GetType().GetProperty("IsLocalSpawnReady", Members).GetValue(playerSync, null)
                    + "|" + playerSync.GetType().GetProperty("IsLocalDead", Members).GetValue(playerSync, null)
                    + "|" + Get(playerSync, "_playerSyncDisabled") + "|" + Get(playerSync, "_sequence")
                    + "|" + Get(Get(playerSync, "_guestResume"), "_phase")
                    + "|" + Get(Get(playerSync, "_guestRelocator"), "_pending"));
                Vector3 feet; Quaternion look;
                rows.Add("pose-tracked|" + playerSync.TryReadLocalPose(out feet, out look) + "|" + feet + "|" + look);
            }
            foreach (var remote in session.Players)
                rows.Add("pose-remote|" + remote.PlayerId + "|" + remote.LastTransformSequence
                    + "|" + remote.LastTransformTime.ToString("R", CultureInfo.InvariantCulture)
                    + "|" + (Time.unscaledTime - remote.LastTransformTime).ToString("R", CultureInfo.InvariantCulture)
                    + "|" + remote.IsDead + "|" + remote.MoveState + "|" + remote.Position);
            var bridge = PaneBridge;
            PaneTraceSnapshot(rows);
            rows.Add("bridge|" + (Get(bridge, "_pane") != null) + "|" + Get(bridge, "_failed") + "|" + Get(bridge, "_epoch"));
            rows.Add("bridge-wait|" + Get(bridge, "_bindingWait"));
            rows.Add("bridge-last-action|" + Get(bridge, "_lastAction"));
            rows.Add("bridge-local-sequence|" + Get(bridge, "_sequence") + "|" + Get(bridge, "_pickupSequence"));
            var lease = Get(bridge, "_lease") as WinterMP.Net.Sync.ScraperLease;
            var received = Get(bridge, "_received") as WinterMP.Net.Messages.PaneScrapeUpdate;
            if (lease != null) rows.Add("lease|" + lease.Holder + "|" + lease.Equipped);
            if (received != null) rows.Add("scraper-result|" + received.Actor + "|" + received.Sequence + "|" + received.Status + "|" + received.Holder + "|" + received.Equipped);
            var item = PaneItem(); var body = (Rigidbody)Get(item, "Body");
            var fsm = Find(PaneClimatePath, "Freezing"); var scrape = Find(PanePath, "Scrape");
            rows.Add("pane|" + PanePath + "|CutoffWindshield|" + fsm.FsmVariables.FindFsmFloat("CutoffWindshield").Value.ToString("R", CultureInfo.InvariantCulture));
            rows.Add("ownership|" + Get(item, "Id") + "|" + Get(item, "LocallyOwned") + "|" + Get(item, "RemoteOwner"));
            var vehicles = Get(WorldSyncManager.Instance!, "_vehicles");
            rows.Add("publish|" + Call(vehicles, "ShouldStreamVehicleClimate", item));
            rows.Add("motion|" + body.velocity.magnitude.ToString("R", CultureInfo.InvariantCulture) + "|" + body.isKinematic);
            rows.Add("scrape-state|" + scrape.ActiveStateName);
            rows.Add("freezing-state|" + fsm.ActiveStateName);
            rows.Add("efficiency|" + fsm.FsmVariables.FindFsmFloat("ScrapeEfficiency").Value.ToString("R", CultureInfo.InvariantCulture));
            rows.Add("hold|" + Get(item, "RemoteClimateUntil") + "|" + Time.unscaledTime);
            foreach (var material in fsm.FsmVariables.MaterialVariables)
                if (material.Name == "6" && material.Value != null && material.Value.HasProperty("_Cutoff"))
                    rows.Add("material|" + material.Name + "|" + material.Value.name + "|" + material.Value.GetFloat("_Cutoff").ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
