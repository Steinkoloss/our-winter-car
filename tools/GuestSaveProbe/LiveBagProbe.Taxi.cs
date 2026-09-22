using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool TaxiProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_TAXI_TEST") == "1";
        private const string TaxiRoot = "JOBS/TAXIJOB/MACHTWAGEN";
        private static PlayMakerFSM TaxiJob => Find("JOBS/TAXIJOB", "Logic");
        private static PlayMakerFSM TaxiDrive => Find(TaxiRoot + "/Functions/PlayerTrigger/DriveTriggerX", "PlayerTrigger");
        private static GameObject TaxiCar => TaxiJob.FsmVariables.FindFsmGameObject("Car").Value;
        private static PlayMakerFSM TaxiWalker
        {
            get
            {
                var go = Find(TaxiRoot + "/TaxiFunctions/Tripmeter", "Function").FsmVariables.FindFsmGameObject("Customer").Value;
                foreach (var f in go.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Logic") return f;
                throw new InvalidOperationException("Missing native taxi customer reference.");
            }
        }
        private static float _taxiNeedsAt;
        private static Vector3? _taxiParkingPosition;
        private static void TickTaxiFixture()
        {
            if (!TaxiProbe || Application.loadedLevelName != "GAME" || Time.unscaledTime < _taxiNeedsAt) return;
            RequirePersistenceSandbox(); _taxiNeedsAt = Time.unscaledTime + 5;
            foreach (string name in new[] { "PlayerHunger", "PlayerThirst", "PlayerFatigue", "PlayerStress", "PlayerUrine", "PlayerTemp" })
            { var v = FsmVariables.GlobalVariables.FindFsmFloat(name); if (v != null) v.Value = name == "PlayerTemp" ? 50 : 0; }
        }
        private static bool TaxiCommand(string[] args, List<string> rows)
        {
            if (!TaxiProbe || !args[1].StartsWith("taxi-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox(); if (TaxiPassengerCommand(args) || TaxiFareCommand(args) || TaxiMeterCommand(args) || TaxiLuggageCommand(args) || TaxiPaydayCommand(args) || TaxiJourneyCommand(args)) return true; var session = SessionManager.Instance!;
            switch (args[1])
            {
                case "taxi-view": return true;
                case "taxi-service-prepare":
                    if (!session.IsHost) throw new InvalidOperationException("Host job fixture only.");
                    Enter(TaxiJob, "Enable stuff"); return true;
                case "taxi-call":
                    if (!session.IsHost) throw new InvalidOperationException("Host native call fixture only.");
                    var ring = Find(TaxiRoot + "/TaxiFunctions/Carphone/CarPhoneFunctions/RingingTaxi", "Ring");
                    ring.gameObject.SetActive(false);
                    ring.FsmVariables.FindFsmBool("Answer").Value = false;
                    ring.FsmVariables.FindFsmBool("Occupied").Value = false;
                    ring.FsmVariables.FindFsmBool("DriverBreak").Value = false;
                    Enter(TaxiWalker, "Randomize loca"); return true;
                case "taxi-phone-near":
                    if (Player == null) throw new InvalidOperationException("Player not ready.");
                    Player.position = Find(TaxiRoot + "/TaxiFunctions/Carphone/CarPhoneFunctions/UseHandle", "Use").transform.position + Vector3.up;
                    return true;
                case "taxi-answer":
                case "taxi-hangup":
                    Enter(Find(TaxiRoot + "/TaxiFunctions/Carphone/CarPhoneFunctions/UseHandle", "Use"), args[1] == "taxi-answer" ? "Pick phone" : "Close phone"); return true;
                case "taxi-intent":
                    session.SendWorldMessage(new WinterMP.Net.Messages.TaxiCallIntent { PlayerId = session.LocalPlayerId,
                        CallId = uint.Parse(args[2]), Action = (WinterMP.Net.Messages.TaxiCallAction)int.Parse(args[3]) }, WinterMP.Net.Channel.ReliableOrdered);
                    return true;
                case "taxi-service":
                    var serviceSync = Get(WorldSyncManager.Instance!, "_taxiJob"); Call(serviceSync, "Clear");
                    serviceSync.GetType().GetField("_serviceFailed", Members).SetValue(serviceSync, args[2] == "off"); return true;
                case "taxi-prepare":
                    // Legacy pickup fixture; integrated journeys activate only the host.
                    TaxiJob.FsmVariables.FindFsmInt("JobStage").Value = 2;
                    Enter(TaxiJob, "Check stage"); return true;
                case "taxi-near":
                    if (Player == null || Player.IsChildOf(TaxiCar.transform)) throw new InvalidOperationException("Unseated player required.");
                    Player.position = TaxiDrive.transform.position - Vector3.up * .6f; return true;
                case "taxi-anchor":
                    if (Player == null || Player.IsChildOf(TaxiCar.transform)) throw new InvalidOperationException("Unseated player required.");
                    _taxiParkingPosition = Player.position; return true;
                case "taxi-far":
                    if (Player == null || Player.IsChildOf(TaxiCar.transform)) throw new InvalidOperationException("Exit first.");
                    // An arbitrary offset can land underwater and trigger native permadeath.
                    if (!_taxiParkingPosition.HasValue) throw new InvalidOperationException("Record a safe taxi-anchor before moving the player.");
                    Player.position = _taxiParkingPosition.Value; return true;
                case "taxi-drive":
                    if (Player == null || Vector3.Distance(Player.position, TaxiDrive.transform.position) > 2)
                        throw new InvalidOperationException("Approach the native driver trigger first.");
                    if (TaxiDrive.ActiveStateName == "Wait for player" && TaxiDrive.GetComponent<Collider>().enabled)
                        TaxiDrive.SendEvent("FINISHED");
                    if (TaxiDrive.ActiveStateName != "Press return" || !TaxiDrive.GetComponent<Collider>().enabled)
                        throw new InvalidOperationException("Native taxi driver entry unavailable: " + TaxiDrive.ActiveStateName);
                    TaxiDrive.SendEvent("Key DOWN"); return true;
                case "taxi-exit":
                    if (TaxiDrive.ActiveStateName != "Player in car") throw new InvalidOperationException("Not driving taxi.");
                    TaxiDrive.SendEvent("Key DOWN"); return true;
                case "taxi-customer":
                    if (!session.IsHost) throw new InvalidOperationException("Host customer fixture only.");
                    var walker = TaxiWalker; var parent = walker.FsmVariables.FindFsmGameObject("Parent").Value;
                    var pivot = walker.FsmVariables.FindFsmGameObject("CarGetInPivot").Value.transform;
                    walker.FsmVariables.FindFsmGameObject("CarMassPassenger").Value.SetActive(false);
                    walker.transform.parent = parent.transform; walker.transform.localPosition = Vector3.zero; walker.transform.localRotation = Quaternion.identity;
                    parent.transform.position = pivot.position + TaxiCar.transform.forward * 1.5f;
                    Transform? destination = null; float distance = 0;
                    foreach (Transform address in walker.FsmVariables.FindFsmGameObject("Locations").Value.transform)
                    {
                        float d = Vector3.Distance(address.position, pivot.position);
                        if (d > distance) { distance = d; destination = address; }
                    }
                    walker.FsmVariables.FindFsmGameObject("DropOffPoint").Value = destination!.gameObject;
                    walker.FsmVariables.FindFsmString("SubtitleDestination").Value = destination.name;
                    Enter(walker, "Activate"); return true;
                case "taxi-context":
                    if (!session.IsHost) throw new InvalidOperationException("Host context fixture only.");
                    var sync = Get(WorldSyncManager.Instance!, "_taxiJob"); Call(sync, "Clear");
                    sync.GetType().GetField("_pickupFailed", Members).SetValue(sync, args[2] == "off"); return true;
                case "taxi-signature":
                    if (!session.IsHost) throw new InvalidOperationException("Host signature fixture only.");
                    Call(Get(WorldSyncManager.Instance!, "_taxiJob"), "Clear");
                    var compare = TaxiWalker.Fsm.GetState("Which car").Actions[0];
                    ((FsmString)compare.GetType().GetField("compareTo").GetValue(compare)).Value = args[2] == "valid" ? "Taxi" : "Changed native taxi";
                    return true;
                default: throw new InvalidOperationException("Unknown taxi command.");
            }
        }
        private static void TaxiSnapshot(List<string> rows)
        {
            if (Application.loadedLevelName != "GAME") return;
            TaxiPassengerSnapshot(rows);
            TaxiLuggageSnapshot(rows);
            TaxiPaydaySnapshot(rows);
            TaxiFareSnapshot(rows);
            TaxiMeterSnapshot(rows);
            var w = TaxiWalker; var session = SessionManager.Instance!;
            rows.Add("taxi-job|" + TaxiJob.FsmVariables.FindFsmInt("JobStage").Value + "|" + TaxiCar.activeInHierarchy);
            rows.Add("taxi-driver|" + TaxiDrive.ActiveStateName + "|" + TaxiDrive.GetComponent<Collider>().enabled
                + "|" + FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle").Value + "|" + (Player == null ? "none" : Vector(Player.position)));
            rows.Add("taxi-customer|" + w.enabled + "|" + w.ActiveStateName + "|" + PathOf(w.transform.parent)
                + "|" + Vector(w.transform.position) + "|" + w.FsmVariables.FindFsmFloat("Distance").Value
                + "|" + w.FsmVariables.FindFsmGameObject("CarMassPassenger").Value.activeSelf);
            foreach (DictionaryEntry pair in (IDictionary)Get(Items, "_items"))
                if (Get(pair.Value, "Body") is Rigidbody body && body.gameObject == TaxiCar)
                    rows.Add("taxi-body|" + pair.Key + "|" + Get(pair.Value, "LocallyOwned") + "|" + Get(pair.Value, "RemoteOwner")
                        + "|" + Get(pair.Value, "RemoteIsDriver") + "|" + Vector(body.position));
            var sync = Get(WorldSyncManager.Instance!, "_taxiJob"); var binding = Get(sync, "_pickup");
            var service = Get(sync, "_service");
            rows.Add("taxi-service|" + (service != null) + "|" + Get(sync, "_serviceFailed")
                + "|" + (service == null ? "none" : Get(service, "_callId") + "|" + Get(service, "_owner")));
            if (service != null) rows.Add("taxi-presentation|" + Get(service, "_shownRootClip") + "|" + Get(service, "_shownSkeletonClip")
                + "|" + Get(service, "_ownedSubtitle"));
            var ring = Find(TaxiRoot + "/TaxiFunctions/Carphone/CarPhoneFunctions/RingingTaxi", "Ring");
            rows.Add("taxi-call|" + ring.enabled + "|" + ring.gameObject.activeInHierarchy + "|" + ring.ActiveStateName
                + "|" + ring.FsmVariables.FindFsmBool("Answer").Value + "|" + ring.FsmVariables.FindFsmBool("Occupied").Value);
            rows.Add("taxi-route|" + w.FsmVariables.FindFsmString("SubtitlePickupPoint").Value
                + "|" + w.FsmVariables.FindFsmString("SubtitleDestination").Value + "|" + w.FsmVariables.FindFsmGameObject("Char").Value.activeInHierarchy);
            var gui = Find("GUI/Indicators/TaxiGUI", "SetText");
            rows.Add("taxi-gui|" + gui.gameObject.activeSelf + "|" + gui.FsmVariables.FindFsmString("Text").Value);
            var remoteService = Get(sync, "_remoteService") as WinterMP.Net.Messages.TaxiServiceState;
            if (remoteService != null) rows.Add("taxi-remote|" + remoteService.Revision + "|" + remoteService.CallId
                + "|" + remoteService.CallOwner + "|" + remoteService.CallPhase + "|" + remoteService.Flags);
            rows.Add("taxi-context|" + (binding == null ? "none" : Get(binding, "_driver").ToString()) + "|" + Get(sync, "_pickupFailed"));
            int proxies = 0;
            foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
                if (obj.name == "WinterMP taxi driver context") proxies++;
            rows.Add("taxi-proxies|" + proxies);
            foreach (var name in new[] { "Distance 1", "Distance 2", "Which car" })
            {
                var s = w.Fsm.GetState(name);
                var a = s.Actions[name == "Distance 2" ? 3 : 0];
                var v = a.GetType().GetField(name == "Which car" ? "stringVariable" : "target").GetValue(a) as NamedVariable;
                rows.Add("taxi-input|" + name + "|" + v!.UseVariable + "|" + v.Name + "|" + v.ToString());
            }
            var camera = FsmVariables.GlobalVariables.FindFsmGameObject("SavePlayerCam")?.Value;
            rows.Add("taxi-globals|" + (camera == null ? "none" : PathOf(camera.transform))
                + "|" + FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle").Value);
        }
    }
}
