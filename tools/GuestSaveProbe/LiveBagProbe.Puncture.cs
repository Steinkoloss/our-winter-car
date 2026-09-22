using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool PunctureProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_PUNCTURE_TEST") == "1";
        private static GameObject PunctureCar => GameObject.Find("CORRIS") ?? throw new InvalidOperationException("Corris missing.");
        private static PlayMakerFSM PunctureDrive => Find("CORRIS/Functions/PlayerTrigger/DriveTriggerX", "PlayerTrigger");
        private static object PunctureItem
        {
            get
            {
                foreach (DictionaryEntry item in (IDictionary)Get(Items, "_items"))
                    if ((string)Get(item.Value, "Path") == "CORRIS") return item.Value;
                throw new InvalidOperationException("Corris not registered.");
            }
        }
        private static PlayMakerFSM PunctureWheel(string suffix)
        {
            foreach (var fsm in PunctureCar.GetComponentsInChildren<PlayMakerFSM>(true))
                if (fsm.FsmName == "Condition" && fsm.name == "WHEELc_" + suffix) return fsm;
            throw new InvalidOperationException("Wheel missing: " + suffix);
        }
        private static PlayMakerFSM PunctureData(GameObject obj)
        {
            foreach (var fsm in obj.GetComponents<PlayMakerFSM>()) if (fsm.FsmName == "Data") return fsm;
            throw new InvalidOperationException("Native wheel Data missing.");
        }
        private static bool PunctureCommand(string[] args)
        {
            if (!PunctureProbe || !args[1].StartsWith("puncture-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox();
            switch (args[1])
            {
                case "puncture-view": return true;
                case "puncture-prepare": PreparePunctureFixture(); return true;
                case "puncture-near":
                    if (Player == null || Player.parent != null) throw new InvalidOperationException("Exit before approaching.");
                    Player.position = PunctureDrive.transform.position - Vector3.up * .6f; return true;
                case "puncture-clear":
                    if (Player == null || Player.parent != null) throw new InvalidOperationException("Exit before moving clear.");
                    Player.position = PunctureCar.transform.position + Vector3.right * 8; return true;
                case "puncture-drive":
                    if (Player == null || Vector3.Distance(Player.position, PunctureDrive.transform.position) > 2)
                        throw new InvalidOperationException("Approach before native driver entry.");
                    // Direct placement does not reliably generate Unity trigger
                    // callbacks. Supply that event; retain native seat/entry logic.
                    if (PunctureDrive.ActiveStateName == "Wait for player" && PunctureDrive.GetComponent<Collider>().enabled)
                        PunctureDrive.SendEvent("FINISHED");
                    if (PunctureDrive.ActiveStateName != "Press return" || !PunctureDrive.GetComponent<Collider>().enabled)
                        throw new InvalidOperationException("Driver entry unavailable: " + PunctureDrive.ActiveStateName);
                    PunctureDrive.SendEvent("Key DOWN"); return true;
                case "puncture-exit":
                    if (PunctureDrive.ActiveStateName != "Player in car") throw new InvalidOperationException("Not driving.");
                    PunctureDrive.SendEvent("Key DOWN"); return true;
                case "puncture-roll":
                    if (!(bool)Get(PunctureItem, "LocallyOwned") || PunctureDrive.ActiveStateName != "Player in car")
                        throw new InvalidOperationException("Local driver required.");
                    float speed = float.Parse(args[2], CultureInfo.InvariantCulture);
                    if (float.IsNaN(speed) || speed < 0 || speed > 8) throw new InvalidOperationException("Invalid rolling speed.");
                    var body = PunctureCar.GetComponent<Rigidbody>(); body.velocity = body.transform.forward * speed; return true;
                case "puncture-fire":
                    foreach (string suffix in args[2].Split(',')) PunctureWheel(suffix).SendEvent("PUNCTURE"); return true;
                case "puncture-request":
                    var session = SessionManager.Instance!;
                    if (session.IsHost) throw new InvalidOperationException("Guest request only.");
                    session.SendWorldMessage(new WheelPunctureRequest { VehicleId = (uint)Get(PunctureItem, "Id"),
                        PlayerId = args.Length > 5 ? byte.Parse(args[5]) : session.LocalPlayerId,
                        Wheel = byte.Parse(args[2]), Epoch = uint.Parse(args[3]), Sequence = ushort.Parse(args[4]) }, WinterMP.Net.Channel.ReliableOrdered);
                    return true;
                case "puncture-health":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host fixture only.");
                    float health = float.Parse(args[3], CultureInfo.InvariantCulture);
                    if (float.IsNaN(health) || health < 0 || health > 100) throw new InvalidOperationException("Invalid fixture health.");
                    var wheel = PunctureWheel(args[2]);
                    var target = wheel.FsmVariables.FindFsmGameObject("ThisTire").Value;
                    Find(PathOf(target.transform), "Data").FsmVariables.FindFsmFloat("TireHealth").Value = health; return true;
                default: throw new InvalidOperationException("Unknown puncture command.");
            }
        }
        private static void PreparePunctureFixture()
        {
            var prefab = Find("RIM14STEELa0", "Use").gameObject;
            var parts = new List<PlayMakerFSM>();
            for (int i = 0; i < 4; i++)
            {
                var part = (GameObject)UnityEngine.Object.Instantiate(prefab);
                part.name = "WinterMP puncture tyre " + i;
                part.SetActive(true);
                foreach (var fsm in part.GetComponents<PlayMakerFSM>())
                {
                    if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
                    NativeBagPartChecks.Start(fsm);
                    if (fsm.FsmName == "Use") parts.Add(fsm);
                }
                part.name = "WinterMP puncture tyre " + i;
            }
            string[] suffixes = { "FL", "FR", "RL", "RR" };
            for (int i = 0; i < 4; i++)
            {
                var wheel = PunctureWheel(suffixes[i]);
                var mount = Find(PathOf(wheel.FsmVariables.FindFsmGameObject("ThisTire").Value.transform), "Data");
                if (mount.FsmVariables.FindFsmGameObject("ActivePart").Value != null) throw new InvalidOperationException("Fixture requires an empty wheel mount.");
                var part = parts[i].gameObject; var data = PunctureData(part);
                data.FsmVariables.FindFsmFloat("TireHealth").Value = SessionManager.Instance!.IsHost ? 80 : 65;
                data.FsmVariables.FindFsmInt("TireType").Value = 1;
                data.FsmVariables.FindFsmInt("TireSize").Value = 0;
                parts[i].SendEvent("LOADTIRE");
                if (parts[i].FsmVariables.FindFsmFloat("SettingTireRadius").Value <= .2f)
                    throw new InvalidOperationException("Native tyre mesh did not initialize.");
                var rb = part.GetComponent<Rigidbody>(); if (rb != null) { rb.isKinematic = true; rb.detectCollisions = false; }
                foreach (var collider in part.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                part.transform.parent = mount.transform; part.transform.localPosition = Vector3.zero; part.transform.localRotation = Quaternion.identity;
                mount.FsmVariables.FindFsmGameObject("ActivePart").Value = part;
                // These disposable mount inputs bypass assembly. The native Set data
                // path still sets actual Wheel physics and starts Condition.
                Enter(mount, "Set data");
            }
            var seat = Find("CORRIS/Assemblies/VINP_SeatDriver", "Data");
            seat.FsmVariables.FindFsmBool("Installed").Value = true;
            seat.FsmVariables.FindFsmBool("RaceSeat").Value = false;
            seat.FsmVariables.FindFsmGameObject("ActivePart").Value = seat.gameObject;
        }
        private static void PunctureSnapshot(List<string> rows)
        {
            RequirePersistenceSandbox();
            if (Application.loadedLevelName != "GAME") return;
            var item = PunctureItem; var body = PunctureCar.GetComponent<Rigidbody>();
            var vehicles = Get(WorldSyncManager.Instance!, "_vehicles");
            var state = SessionManager.Instance!.IsHost ? Call(vehicles, "BuildVehicleWheelHealthState", item) as VehicleWheelHealthState
                : Call(vehicles, "ReadWheelHealthState", Get(item, "Id")) as VehicleWheelHealthState;
            if (state != null) rows.Add("puncture-epochs|" + state.Revision + "|" + state.Availability + "|"
                + state.EpochFL + "|" + state.EpochFR + "|" + state.EpochRL + "|" + state.EpochRR);
            rows.Add("puncture-car|" + Get(item, "Id") + "|" + Vector(body.position) + "|" + Vector(body.velocity)
                + "|" + body.isKinematic + "|" + Get(item, "LocallyOwned") + "|" + Get(item, "RemoteOwner"));
            rows.Add("puncture-drive|" + PunctureDrive.ActiveStateName + "|" + PunctureDrive.enabled + "|" + PunctureDrive.GetComponent<Collider>().enabled);
            rows.Add("puncture-player|" + (Player == null ? "none" : Vector(Player.position)) + "|" + (Player?.parent == null ? "none" : PathOf(Player.parent)));
            foreach (string field in new[] { "AcceptedVehicleCondition", "ParkedVehicleCondition", "SentFinalCondition" })
            {
                var c = Get(item, field) as VehicleCondition;
                rows.Add("puncture-state|" + field + "|" + (c == null ? "none" : c.OwnerPlayerId + "|" + c.Sequence + "|" + c.Flags + "|" + c.Availability));
            }
            foreach (string suffix in new[] { "FL", "FR", "RL", "RR" })
            {
                var fsm = PunctureWheel(suffix); var tire = fsm.FsmVariables.FindFsmGameObject("ThisTire")?.Value;
                var data = tire != null ? Find(PathOf(tire.transform), "Data") : null;
                rows.Add("puncture-wheel|" + suffix + "|" + fsm.ActiveStateName + "|" + fsm.enabled + "|" + fsm.gameObject.activeInHierarchy
                    + "|" + fsm.FsmVariables.FindFsmFloat("Health")?.Value.ToString("R", CultureInfo.InvariantCulture)
                    + "|" + data?.FsmVariables.FindFsmFloat("TireHealth")?.Value.ToString("R", CultureInfo.InvariantCulture) + "|" + Identity(tire));
                var part = data?.FsmVariables.FindFsmGameObject("ActivePart")?.Value;
                var saved = part == null ? null : PunctureData(part).FsmVariables.FindFsmFloat("TireHealth");
                rows.Add("puncture-saved|" + suffix + "|" + saved?.Value.ToString("R", CultureInfo.InvariantCulture) + "|" + Identity(part));
                foreach (var scalar in fsm.FsmVariables.FloatVariables)
                    rows.Add("puncture-value|" + suffix + "|" + scalar.Name + "|" + scalar.Value.ToString("R", CultureInfo.InvariantCulture));
                foreach (var scalar in fsm.FsmVariables.IntVariables) rows.Add("puncture-int|" + suffix + "|" + scalar.Name + "|" + scalar.Value);
                var component = fsm.GetComponent("Wheel");
                foreach (var field in component.GetType().GetFields(Members))
                    if ((field.FieldType == typeof(float) || field.FieldType == typeof(bool)) && (field.Name.IndexOf("riction", StringComparison.Ordinal) >= 0
                        || field.Name.IndexOf("lip", StringComparison.Ordinal) >= 0 || field.Name.IndexOf("ressure", StringComparison.Ordinal) >= 0
                        || field.Name == "radius" || field.Name == "onGroundDown" || field.Name == "angularVelocity"))
                        rows.Add("puncture-physics|" + suffix + "|" + field.Name + "|" + Convert.ToString(field.GetValue(component), CultureInfo.InvariantCulture));
            }
        }
    }
}
