using System;
using System.Collections;
using System.Collections.Generic;
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
        private static void RunPassengerHeatingChecks(Action<string, Action> check, object original, object vehicles,
            SessionManager session, CaptureTransport capture, Action reset)
        {
            var items = (IDictionary)Get(Get(vehicles, "_items"), "_items");
            var seats = (PassengerSeatLedger)Get(session, "_passengerSeats");
            var world = World.GetProperty("Instance", Static).GetValue(null, null);
            var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null);
            var parent = player.parent; var position = player.position;
            var savedPassenger = PassengerController.Instance; var savedDeath = DeathSyncManager.Instance;
            var controller = new GameObject("passenger heating probe");
            var passenger = controller.AddComponent<PassengerController>(); passenger.enabled = false;
            var death = controller.AddComponent<DeathSyncManager>(); death.enabled = false;
            var deathHook = Get(death, "_deathHook");
            var globalVariables = FsmVariables.GlobalVariables; var oldFloats = globalVariables.FloatVariables;
            var warmth = new FsmFloat { Name = "PlayerTemp", UseVariable = true, Value = 50 };
            var globals = new List<FsmFloat>(oldFloats); globals.RemoveAll(v => v.Name == warmth.Name); globals.Add(warmth);
            var rows = ReadOccupancyRows("condensation-probe.json"); var iceRows = ReadOccupancyRows("window-ice-probe.json");
            try
            {
                globalVariables.FloatVariables = globals.ToArray();
                SetStaticProperty(typeof(PassengerController), "Instance", passenger); SetStaticProperty(typeof(DeathSyncManager), "Instance", death);
                using (var body = new PassengerHeatingFixture(player, warmth))
                foreach (string path in new[] { "CORRIS/Simulation/CarTempCorris", "SORBET(190-200psi)/Simulation/CarTempSorbet",
                    "JOBS/TAXIJOB/MACHTWAGEN/Simulation/CarTempTaxi" })
                using (var f = new CondensationFixture(rows, iceRows, path))
                try
                {
                    var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                    items[VehicleId] = item; Set(item, "Id", VehicleId); Set(item, "Body", f.Ice.Body); Set(item, "IsVehicle", true);
                    Set(item, "Path", path.Substring(0, path.IndexOf("/Simulation/", StringComparison.Ordinal)));
                    CallStatic("EnsureClimateProbe", item); Set(item, "LoggedClimateApply", true); Set(item, "NextClimateDiagAt", float.MaxValue);
                    Action<bool, byte> mode = (host, owner) =>
                    {
                        SetProperty(session, "IsHost", host); SetProperty(session, "State", host ? SessionState.Hosting : SessionState.Connected);
                        SetProperty(session, "LocalPlayerId", host ? (byte)0 : (byte)3);
                        Set(item, "RemoteOwner", owner); Set(item, "LocallyOwned", false);
                    };
                    Action bind = () => { Set(vehicles, "_nextPassengerHeatingProbe", 0f); Call(vehicles, "EnsurePassengerHeating"); };
                    Action clean = () =>
                    {
                        Call(vehicles, "ClearClimateStreams"); seats.Clear(); f.Seed(); mode(true, 255); body.Seed(); warmth.Value = 50;
                        Set(passenger, "_seated", true); Set(passenger, "_seatedVehicleId", VehicleId); Set(passenger, "_seatedIndex", 0);
                        Set(passenger, "_disabled", false); Set(deathHook, "_localDeathActive", false);
                        seats.Record(new PassengerState { PlayerId = 0, VehicleId = VehicleId, SeatIndex = 0 });
                        player.SetParent(f.Ice.Body.transform, false); f.Cabin.Value = 30;
                        Set(item, "NextClimateAt", 0f); capture.Packets.Clear(); bind();
                        Require(Get(vehicles, "_passengerHeating") != null, "Passenger heating did not bind the audited readers.");
                    };
                    Func<VehicleClimate> sample = () => (VehicleClimate)(Call(vehicles, "TryBuildVehicleClimate", item)
                        ?? throw new InvalidOperationException("Missing cabin report."));
                    Func<byte, bool, ushort, VehicleClimate> report = (owner, available, sequence) => new VehicleClimate {
                        VehicleId = VehicleId, OwnerPlayerId = owner, Sequence = sequence, CabinTemp = 223,
                        Flags = available ? VehicleClimate.FlagCabinTemperature : (byte)0, Frost = 102 };
                    Action<PeerId, VehicleClimate> packet = (peer, message) => Call(session, "OnPacketReceived", peer,
                        PacketCodec.Encode(message), Channel.UnreliableSequenced);
                    Action fallback = () => { body.Read("Get temp", -15); body.Read("Source", 5); };
                    Func<float, float> quantized = input => {
                        Require(VehicleClimate.TryQuantizeCabinTemperature(input, out byte b), "Invalid test degrees.");
                        return VehicleClimate.DequantizeCabinTemperature(b); };
                    string prefix = "passenger heating " + f.Ice.Body.gameObject.name + ": ";

                    check(prefix + "host parked passenger uses cabin in both native branches and body calculation", () =>
                    {
                        clean(); float expected = quantized(30);
                        body.Read("Get temp", expected); body.Read("Source", expected);
                        body.Fsm.FsmVariables.FindFsmFloat("AirSpeed").Value = 20;
                        NativeBagPartChecks.Fire(body.Fsm, "Calculate");
                        Require(Mathf.Abs(warmth.Value - (50 + (expected - 10) / 20)) < .00001f, "Native body calculation used a different heat input.");
                        Require(sample().HasCabinTemperature && sample().CabinTemp == 223, "Native finite source was not marked available.");
                    });
                    foreach (float value in new[] { -20f, 0f })
                    {
                        float degrees = value;
                        check(prefix + "cold or zero cabin remains known at " + degrees, () =>
                        {
                            clean(); f.Cabin.Value = degrees; body.Read("Get temp", quantized(degrees)); body.Read("Source", quantized(degrees));
                            Require(sample().HasCabinTemperature, "Known cold cabin lost availability.");
                        });
                    }
                    check(prefix + "all three passenger seats retain native sources and do not grant driver ownership", () =>
                    {
                        clean(); var localIn = f.Ice.Frost.FsmVariables.FindFsmBool("PlayerIn"); localIn.Value = false;
                        for (int i = 0; i < 3; i++) { Set(passenger, "_seatedIndex", i); body.Read("Get temp", quantized(30)); }
                        Require(!(bool)Get(item, "LocallyOwned") && !localIn.Value, "Passenger heat promoted local driver entry.");
                    });
                    check(prefix + "host climate reaches guest body through authenticated live packets", () =>
                    {
                        clean(); mode(false, 0); packet(new PeerId(999), report(0, true, 0));
                        f.Cabin.Value = -30; body.Read("Get temp", quantized(30)); body.Read("Source", quantized(30));
                        Require(f.Cabin.Value == -30, "Body read rewrote cabin presentation source.");
                    });
                    check(prefix + "guest driver climate reaches host passenger and keeps availability in snapshots", () =>
                    {
                        clean(); mode(true, 1); packet(new PeerId(1001), report(1, true, 0)); body.Read("Source", quantized(30));
                        var snapshot = sample(); Require(snapshot.HasCabinTemperature && snapshot.OwnerPlayerId == 0
                            && snapshot.Sequence == VehicleClimate.SnapshotSequence, "Host snapshot dropped accepted cabin availability.");
                        Require(capture.Packets.Count > 0, "Host did not relay accepted climate.");
                        foreach (var sent in capture.Packets) Require(sent.Message is VehicleClimate c && c.HasCabinTemperature, "Relay lost availability.");
                    });
                    check(prefix + "guest passenger accepts the current guest driver only through host relay", () =>
                    {
                        clean(); mode(false, 1); packet(new PeerId(1001), report(1, true, 0)); fallback();
                        packet(new PeerId(999), report(1, true, 0)); body.Read("Get temp", quantized(30));
                    });
                    check(prefix + "join snapshot supplies available cabin until its hold expires", () =>
                    {
                        clean(); mode(false, 255); packet(new PeerId(999), report(0, true, VehicleClimate.SnapshotSequence));
                        body.Read("Source", quantized(30)); Set(item, "RemoteClimateUntil", Time.unscaledTime); fallback();
                    });
                    check(prefix + "unavailable climate skips cabin writes and falls back without discarding frost", () =>
                    {
                        clean(); mode(false, 0); packet(new PeerId(999), report(0, true, 0));
                        f.Cabin.Value = -25; var unavailable = report(0, false, 1); unavailable.CabinTemp = 255;
                        packet(new PeerId(999), unavailable); Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime);
                        Require(f.Cabin.Value == -25 && f.Amount.Value == .4f, "Unavailable temperature wrote cabin or blocked independent frost."); fallback();
                    });
                    check(prefix + "stale unavailable packet cannot erase a fresh cabin source", () =>
                    {
                        clean(); mode(false, 0); packet(new PeerId(999), report(0, true, 2)); packet(new PeerId(999), report(0, false, 1));
                        body.Read("Source", quantized(30));
                    });
                    check(prefix + "owner handoff invalidates the previous cabin until the new owner reports", () =>
                    {
                        clean(); mode(false, 0); packet(new PeerId(999), report(0, true, 0)); Set(item, "RemoteOwner", (byte)1); fallback();
                        packet(new PeerId(999), report(1, true, 0)); body.Read("Source", quantized(30));
                    });
                    check(prefix + "nonfinite or missing native cabin never fabricates passenger heat", () =>
                    {
                        clean(); foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                        {
                            f.Cabin.Value = invalid; Require(!sample().HasCabinTemperature && sample().CabinTemp == 128, "Nonfinite cabin was available."); fallback();
                        }
                        f.Cabin.Value = 30; Set(item, "InteriorTempVar", null);
                        try { Require(!sample().HasCabinTemperature, "Missing source was available."); fallback(); }
                        finally { Set(item, "InteriorTempVar", f.Cabin); }
                    });
                    foreach (string reason in new[] { "standing", "disabled", "seat index", "wrong car", "unparented", "host rejected seat", "disconnected", "dead" })
                    {
                        string why = reason;
                        check(prefix + why + " leaves both native heat reads in control", () =>
                        {
                            clean();
                            switch (why)
                            {
                                case "standing": Set(passenger, "_seated", false); break;
                                case "disabled": Set(passenger, "_disabled", true); break;
                                case "seat index": Set(passenger, "_seatedIndex", 3); break;
                                case "wrong car": Set(passenger, "_seatedVehicleId", VehicleId + 1); break;
                                case "unparented": player.SetParent(parent, false); break;
                                case "host rejected seat": seats.Clear(); break;
                                case "disconnected": SetProperty(session, "State", SessionState.Idle); break;
                                case "dead": Set(deathHook, "_localDeathActive", true); break;
                            }
                            fallback(); Require(warmth.Value == 50, "Read hook wrote body warmth directly.");
                        });
                    }
                    check(prefix + "native nearest-source cache still follows a changed source after exiting", () =>
                    {
                        clean(); body.Read("Source", quantized(30)); Set(passenger, "_seated", false);
                        body.Fsm.FsmVariables.FindFsmGameObject("HeatSource").Value = body.Alternate.gameObject;
                        body.Read("Source", 12); body.Fsm.FsmVariables.FindFsmGameObject("HeatSource").Value = body.Heat.gameObject;
                        body.Read("Source", 5);
                    });
                    check(prefix + "changed native action disables only heating and can bind again", () =>
                    {
                        clean(); var state = NativeBagPartChecks.State(body.Fsm, "Source"); var actions = state.Actions;
                        state.Actions = new FsmStateAction[0];
                        try { Call(vehicles, "EnsurePassengerHeating"); Require(Get(vehicles, "_passengerHeating") == null, "Changed body actions stayed bound."); body.Read("Get temp", -15); }
                        finally { state.Actions = actions; }
                        bind(); body.Read("Source", quantized(30)); Require(sample().HasCabinTemperature, "Heating failure disabled climate capture.");
                    });
                    check(prefix + "reader operand changes detach the hook inside the native callback", () =>
                    {
                        clean(); var action = NativeBagPartChecks.State(body.Fsm, "Get temp").Actions[0];
                        var output = Get(action, "storeValue"); var replacement = new FsmFloat(-99);
                        Set(action, "storeValue", replacement);
                        try
                        {
                            NativeBagPartChecks.Fire(body.Fsm, "Probe idle"); NativeBagPartChecks.Fire(body.Fsm, "Get temp");
                            Require(replacement.Value == -15 && Get(vehicles, "_passengerHeating") == null, "Changed native output stayed intercepted.");
                        }
                        finally { Set(action, "storeValue", output); }
                        bind(); body.Read("Get temp", quantized(30));
                    });
                    check(prefix + "clear removes both readers and a fresh session can rebind", () =>
                    {
                        clean(); Call(vehicles, "ClearClimateStreams"); fallback();
                        Require(((IDictionary)Vehicles.GetField("PassengerHeatingReaders", Static).GetValue(null)).Count == 0, "Clear retained body hooks.");
                        bind(); body.Read("Get temp", quantized(30));
                    });
                }
                finally { Call(vehicles, "ClearClimateStreams"); player.SetParent(parent, false); player.position = position; }
            }
            finally
            {
                globalVariables.FloatVariables = oldFloats; seats.Clear(); items[VehicleId] = original;
                Set(passenger, "_seated", false); UnityEngine.Object.DestroyImmediate(controller);
                SetStaticProperty(typeof(PassengerController), "Instance", savedPassenger); SetStaticProperty(typeof(DeathSyncManager), "Instance", savedDeath);
                reset();
            }
        }

        private sealed class PassengerHeatingFixture : IDisposable
        {
            internal readonly PlayMakerFSM Fsm, Rain, Heat, Alternate;
            private readonly GameObject _body;
            internal PassengerHeatingFixture(Transform player, FsmFloat warmth)
            {
                _body = new GameObject("BodyTemp"); _body.SetActive(false); _body.transform.SetParent(player, false);
                var row = NativeBagPartChecks.Find(ReadOccupancyRows("passenger-heating-probe.json"), "PLAYER/BodyTemp", "Calculations");
                Fsm = NativeBagPartChecks.MakeFsm(_body, row); Fsm.Fsm.Init(Fsm);
                foreach (var state in Fsm.FsmStates) { state.Actions = new FsmStateAction[0]; state.Transitions = new FsmTransition[0]; }
                foreach (Dictionary<string, object> state in (IEnumerable)row["states"])
                {
                    var target = NativeBagPartChecks.State(Fsm, (string)state["name"]); var actions = new List<FsmStateAction>();
                    foreach (Dictionary<string, object> action in (IEnumerable)state["actions"]) actions.Add(ReadCondensationAction(action, Fsm));
                    target.Actions = actions.ToArray(); foreach (var action in target.Actions) action.Init(target);
                }
                Set(NativeBagPartChecks.State(Fsm, "Calculate").Actions[3], "floatVariable", warmth);
                Rain = Source("Rain", "RoofCheck", player, -15); Heat = Source("probe nearby heat", "Data", _body.transform, 5);
                Alternate = Source("probe alternate heat", "Data", _body.transform, 12);
                Seed(); _body.SetActive(true); NativeBagPartChecks.Start(Fsm);
            }
            private static PlayMakerFSM Source(string name, string fsmName, Transform parent, float degrees)
            {
                var go = new GameObject(name); go.SetActive(false); go.transform.SetParent(parent, false);
                var fsm = (PlayMakerFSM)typeof(NativeBagPartChecks).GetMethod("MakeEmptyFsm", Static).Invoke(null, new object[] { go, fsmName });
                fsm.FsmVariables.FloatVariables = new[] { new FsmFloat { Name = "Temperature", UseVariable = true, Value = degrees } };
                fsm.Fsm.Init(fsm); go.SetActive(true); NativeBagPartChecks.Start(fsm); return fsm;
            }
            internal void Seed()
            {
                Fsm.FsmVariables.FindFsmGameObject("Rain").Value = Rain.gameObject;
                Fsm.FsmVariables.FindFsmGameObject("HeatSource").Value = Heat.gameObject;
                Fsm.FsmVariables.FindFsmInt("ClothingStage").Value = -1;
            }
            internal void Read(string state, float expected)
            {
                var action = NativeBagPartChecks.State(Fsm, state).Actions[0]; var owner = (FsmOwnerDefault)Get(action, "gameObject");
                var reference = owner.GameObject; var source = reference.Value;
                NativeBagPartChecks.Fire(Fsm, "Probe idle"); NativeBagPartChecks.Fire(Fsm, state);
                float actual = Fsm.FsmVariables.FindFsmFloat("Temperature").Value;
                Require(Mathf.Abs(actual - expected) < .00001f, state + " read " + actual + " instead of " + expected);
                Require(ReferenceEquals(Get(action, "gameObject"), owner) && ReferenceEquals(owner.GameObject, reference) && reference.Value == source,
                    "Body hook altered a native source operand.");
                Require(Rain.FsmVariables.FindFsmFloat("Temperature").Value == -15 && Heat.FsmVariables.FindFsmFloat("Temperature").Value == 5
                    && Alternate.FsmVariables.FindFsmFloat("Temperature").Value == 12, "Body hook altered a native source temperature.");
            }
            public void Dispose() { UnityEngine.Object.DestroyImmediate(Rain.gameObject); UnityEngine.Object.DestroyImmediate(_body); }
        }
    }
}
