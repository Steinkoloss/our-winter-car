using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private static void RunClimateOccupancyChecks(Action<string, Action> check, object original, object vehicles,
            SessionManager session, Action reset)
        {
            var items = (IDictionary)Get(Get(vehicles, "_items"), "_items");
            var iceRows = ReadOccupancyRows("window-ice-probe.json");
            var entryRows = ReadOccupancyRows("climate-entry-probe.json");
            var ledger = (PassengerSeatLedger)Get(session, "_passengerSeats");
            var savedPassenger = PassengerController.Instance;
            var go = new GameObject("climate seat probe");
            var passenger = go.AddComponent<PassengerController>(); passenger.enabled = false;
            var world = World.GetProperty("Instance", Static).GetValue(null, null);
            var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null);
            var parent = player.parent; var position = player.position;
            try
            {
                foreach (string path in new[] { "CORRIS/Simulation/CarTempCorris", "SORBET(190-200psi)/Simulation/CarTempSorbet",
                    "JOBS/TAXIJOB/MACHTWAGEN/Simulation/CarTempTaxi" })
                using (var f = new WindowIceFixture(iceRows, path))
                {
                    var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                    items[VehicleId] = item; Set(item, "Id", VehicleId); Set(item, "Body", f.Body); Set(item, "IsVehicle", true);
                    string root = path.Substring(0, path.IndexOf("/Simulation/", StringComparison.Ordinal)); Set(item, "Path", root);
                    CallStatic("EnsureClimateProbe", item); Set(item, "LoggedClimateApply", true); Set(item, "NextClimateDiagAt", float.MaxValue);
                    var entry = MakeClimateEntry(entryRows, root + "/Functions/PlayerTrigger", f);
                    var localIn = f.Frost.FsmVariables.FindFsmBool("PlayerIn");
                    string prefix = "climate occupancy " + f.Body.gameObject.name + ": ";
                    Action<bool, byte> mode = (host, owner) =>
                    {
                        SetProperty(session, "IsHost", host); SetProperty(session, "State", host ? SessionState.Hosting : SessionState.Connected);
                        SetProperty(session, "LocalPlayerId", host ? (byte)0 : (byte)3); Set(item, "RemoteOwner", owner);
                    };
                    Action clean = () =>
                    {
                        Call(vehicles, "ClearClimateStreams"); ledger.Clear(); f.Seed(); localIn.Value = false;
                        ((IDictionary)Get(passenger, "_remoteSeats")).Clear(); ((IDictionary)Get(passenger, "_remoteSeatSequences")).Clear();
                        Set(passenger, "_disabled", false); Set(passenger, "_seated", false); Set(item, "LocallyOwned", false);
                        player.SetParent(parent, false); player.position = position; mode(true, 255);
                    };
                    Func<VehicleClimate> sample = () => (VehicleClimate)Call(vehicles, "TryBuildVehicleClimate", item)!;
                    Func<bool> driving = () => (bool)Call(world, "IsLocalPlayerDriving", item)!;
                    Action<byte, bool> receive = (owner, occupied) =>
                    {
                        var report = new VehicleClimate { VehicleId = VehicleId, OwnerPlayerId = owner, Sequence = 1,
                            Flags = occupied ? VehicleClimate.FlagPlayerIn : (byte)0, Frost = 40, Fog = 50, CabinTemp = 60 };
                        Require((bool)Call(vehicles, "OnRemoteVehicleClimate", report)!, "Climate fixture report rejected.");
                    };
                    Action<bool> occupancy = expected =>
                    {
                        Require(sample().PlayerIn == expected, "Wrong captured cabin occupancy.");
                        Require(!localIn.Value && !driving(), "Shared passenger occupancy changed local entry or driver authority.");
                    };
                    Func<byte, uint, byte, ushort, PassengerState> seat = (id, car, index, sequence) =>
                        new PassengerState { PlayerId = id, VehicleId = car, SeatIndex = index, Sequence = sequence };

                    check(prefix + "received occupied climate leaves native entry false", () =>
                    {
                        clean(); mode(false, 0); receive(0, true);
                        Require((bool)Get(item, "RemotePlayerIn") && !localIn.Value, "Received occupancy overwrote native entry.");
                        Require(Mathf.Abs(f.Frost.FsmVariables.FindFsmFloat("Frost").Value - 40 / 255f) < .000001f,
                            "Occupancy isolation broke frost presentation.");
                        Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime);
                        Require(!localIn.Value && !driving(), "Late climate presentation promoted an observer.");
                    });
                    check(prefix + "expired remote occupancy cannot promote an observer into a driver", () =>
                    {
                        mode(false, 255); Set(item, "RemoteClimateUntil", -999f); occupancy(false);
                        mode(true, 255); occupancy(false);
                    });
                    check(prefix + "native cabin proximity never claims a driver seat", () =>
                    {
                        clean(); NativeBagPartChecks.Fire(entry, "Press return");
                        Require(localIn.Value && !driving() && sample().PlayerIn, "Cabin proximity claimed an empty driver seat or lost cabin heating.");
                        player.SetParent(f.Body.transform, false);
                        Require(driving(), "Native seating did not establish driver authority.");
                        player.SetParent(parent, false); player.position = position;
                        Require(localIn.Value && !driving(), "Driver exit retained authority while cabin proximity remained true.");
                        mode(false, 0); receive(0, false); Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime);
                        Require(localIn.Value && sample().PlayerIn, "Remote empty cabin cleared local entry.");
                        NativeBagPartChecks.Fire(entry, "Player reset"); occupancy(false);
                    });
                    check(prefix + "host repair retains accepted guest occupancy without a local entry write", () =>
                    {
                        clean(); mode(true, 1); receive(1, true); var snapshot = sample();
                        Require(snapshot.PlayerIn && snapshot.OwnerPlayerId == 0 && snapshot.Sequence == VehicleClimate.SnapshotSequence
                            && !localIn.Value && !driving(), "Host repair lost guest occupancy or borrowed local entry.");
                        mode(true, 255); occupancy(false);
                    });
                    check(prefix + "host accepted passenger occupies only the matching cabin", () =>
                    {
                        clean(); Require(ledger.Apply(seat(1, VehicleId, 0, 1), (s, continuing) => true)!.Accepted, "Seat fixture rejected.");
                        occupancy(true); ledger.Apply(seat(1, VehicleId + 1, 1, 2), (s, continuing) => true); occupancy(false);
                    });
                    check(prefix + "host rejected claim cannot add cabin occupancy", () =>
                    {
                        clean(); Require(!ledger.Apply(seat(1, VehicleId, 0, 1), (s, continuing) => false)!.Accepted, "Rejected seat fixture accepted.");
                        occupancy(false);
                    });
                    check(prefix + "host exits preserve other passengers and clear the last seat", () =>
                    {
                        clean(); ledger.Apply(seat(1, VehicleId, 1, 1), (s, continuing) => true);
                        ledger.Apply(seat(2, VehicleId, 2, 1), (s, continuing) => true); occupancy(true);
                        ledger.Apply(seat(1, 0, 255, 2), (s, continuing) => true); occupancy(true);
                        ledger.Apply(seat(2, 0, 255, 2), (s, continuing) => true); occupancy(false);
                    });
                    check(prefix + "host disconnect and session clear retire passenger occupancy", () =>
                    {
                        clean(); ledger.Apply(seat(1, VehicleId, 0, 1), (s, continuing) => true); occupancy(true);
                        ledger.ForgetPlayer(1); occupancy(false);
                        session.RecordPassengerState(seat(0, VehicleId, 2, 1)); occupancy(true); ledger.Clear(); occupancy(false);
                    });
                    check(prefix + "guest uses accepted remote seats only for the matching car", () =>
                    {
                        clean(); mode(false, 255); passenger.OnRemotePassengerState(seat(1, VehicleId, 0, 1)); occupancy(true);
                        passenger.OnRemotePassengerState(seat(1, VehicleId + 1, 0, 2)); occupancy(false);
                    });
                    check(prefix + "stale and duplicate guest seat exits cannot empty a cabin", () =>
                    {
                        clean(); mode(false, 255); passenger.OnRemotePassengerState(seat(1, VehicleId, 0, 8));
                        passenger.OnRemotePassengerState(seat(1, 0, 255, 7)); occupancy(true);
                        passenger.OnRemotePassengerState(seat(1, 0, 255, 8)); occupancy(true);
                        passenger.OnRemotePassengerState(seat(1, 0, 255, 9)); occupancy(false);
                    });
                    check(prefix + "roster departure stops guest occupancy before anchor cleanup", () =>
                    {
                        clean(); mode(false, 255); passenger.OnRemotePassengerState(seat(1, VehicleId, 0, 1)); occupancy(true);
                        var peers = (IDictionary)Get(session, "_playersByPeer"); var peer = new PeerId(1001); var saved = peers[peer];
                        try { peers.Remove(peer); occupancy(false); } finally { peers.Add(peer, saved); }
                    });
                    check(prefix + "guest includes the host passenger when present in its roster", () =>
                    {
                        clean(); mode(false, 255); var peers = (IDictionary)Get(session, "_playersByPeer"); var peer = new PeerId(999);
                        try
                        {
                            peers.Add(peer, new RemotePlayer { Peer = peer, PlayerId = 0 });
                            passenger.OnRemotePassengerState(seat(0, VehicleId, 1, 1)); occupancy(true);
                            passenger.OnRemotePassengerState(seat(0, 0, 255, 2)); occupancy(false);
                        }
                        finally { peers.Remove(peer); }
                    });
                    check(prefix + "readmitted player slot cannot inherit its previous cabin", () =>
                    {
                        clean(); mode(false, 255); passenger.OnRemotePassengerState(seat(1, VehicleId, 0, 800)); occupancy(true);
                        passenger.ForgetPlayer(1); occupancy(false);
                        passenger.OnRemotePassengerState(seat(1, VehicleId + 1, 1, 0)); occupancy(false);
                        passenger.OnRemotePassengerState(seat(1, VehicleId, 1, 1)); occupancy(true);
                    });
                    check(prefix + "local passenger is occupied without gaining driver authority", () =>
                    {
                        clean(); Set(passenger, "_seated", true); Set(passenger, "_seatedVehicleId", VehicleId); occupancy(true);
                        Set(passenger, "_seatedVehicleId", VehicleId + 1); occupancy(false);
                        Set(passenger, "_seatedVehicleId", VehicleId); mode(false, 255); occupancy(true);
                        Set(passenger, "_seated", false); occupancy(false);
                    });
                    check(prefix + "disabled or disconnected remote passenger view is not a climate source", () =>
                    {
                        clean(); mode(false, 255); passenger.OnRemotePassengerState(seat(1, VehicleId, 0, 1)); occupancy(true);
                        Set(passenger, "_disabled", true); occupancy(false); Set(passenger, "_disabled", false);
                        SetProperty(session, "State", SessionState.Idle); occupancy(false);
                    });
                    check(prefix + "host uses its ledger rather than the guest presentation map", () =>
                    {
                        clean(); passenger.OnRemotePassengerState(seat(1, VehicleId, 0, 1)); occupancy(false);
                        ledger.Record(seat(1, VehicleId, 0, 1)); occupancy(true);
                        SetProperty(session, "State", SessionState.Idle); occupancy(false);
                    });
                    check(prefix + "native driver parenting still contributes when the entry flag is absent", () =>
                    {
                        clean(); player.SetParent(f.Body.transform, false);
                        Require(!localIn.Value && driving() && sample().PlayerIn, "Local driver parenting no longer contributes occupancy.");
                        player.SetParent(parent, false); player.position = position; occupancy(false);
                    });
                    clean();
                }
            }
            finally
            {
                ledger.Clear(); SetStaticProperty(typeof(PassengerController), "Instance", savedPassenger);
                player.SetParent(parent, false); player.position = position;
                UnityEngine.Object.DestroyImmediate(go); items[VehicleId] = original; reset();
            }
        }

        private static List<object> ReadOccupancyRows(string file)
        {
            var reader = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var parser = Activator.CreateInstance(reader, Members, null, new object[] {
                File.ReadAllText(Path.Combine(Application.dataPath, "../" + file)) }, null);
            return (List<object>)((Dictionary<string, object>)reader.GetMethod("ReadObject", Members).Invoke(parser, null))["fsms"];
        }

        private static PlayMakerFSM MakeClimateEntry(List<object> rows, string path, WindowIceFixture fixture)
        {
            var go = new GameObject("native climate entry"); go.SetActive(false); go.transform.SetParent(fixture.Body.transform, false);
            var row = NativeBagPartChecks.Find(rows, path, "PlayerTrigger"); var fsm = NativeBagPartChecks.MakeFsm(go, row);
            fsm.Fsm.Init(fsm);
            foreach (var state in fsm.FsmStates) { state.Transitions = new FsmTransition[0]; state.Actions = new FsmStateAction[0]; }
            int imported = 0;
            foreach (Dictionary<string, object> state in (IEnumerable)row["states"])
            {
                string name = (string)state["name"]; if (name != "Press return" && name != "Player reset") continue;
                var raw = (Dictionary<string, object>)((List<object>)state["actions"])[name == "Press return" ? 8 : 2];
                Require((string)raw["type"] == "HutongGames.PlayMaker.Actions.SetFsmBool", "Native entry action changed.");
                var action = ReadIceAction(raw, fsm);
                Require(((FsmString)Get(action, "fsmName")).Value == "GlassFrosting"
                    && ((FsmString)Get(action, "variableName")).Value == "PlayerIn"
                    && ((FsmBool)Get(action, "setValue")).Value == (name == "Press return"), "Native entry target changed.");
                // Import only the climate bool writer; mirror, player parenting and save callbacks remain absent.
                ((FsmOwnerDefault)Get(action, "gameObject")).GameObject.Value = fixture.Frost.gameObject;
                var native = NativeBagPartChecks.State(fsm, name); native.Actions = new[] { action }; action.Init(native); imported++;
            }
            Require(imported == 2, "Native entry/reset pair missing."); go.SetActive(true); NativeBagPartChecks.Start(fsm); return fsm;
        }
    }
}
