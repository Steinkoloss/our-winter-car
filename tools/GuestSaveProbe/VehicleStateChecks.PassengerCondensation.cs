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
        private delegate void SweatPacket(PeerId peer, byte id, ushort sequence, float sweat, bool available);
        private static void RunPassengerCondensationChecks(Action<string, Action> check, object original, object vehicles,
            SessionManager session, CaptureTransport capture, Action reset)
        {
            var items = (IDictionary)Get(Get(vehicles, "_items"), "_items");
            var seats = (PassengerSeatLedger)Get(session, "_passengerSeats");
            var peers = (IDictionary)Get(session, "_playersByPeer"); var first = new PeerId(1001); var second = new PeerId(1002);
            var one = (RemotePlayer)peers[first]; var two = (RemotePlayer)peers[second];
            var globals = FsmVariables.GlobalVariables.FindFsmFloat("PlayerSweat"); float savedSweat = globals.Value;
            var savedPassenger = PassengerController.Instance; var go = new GameObject("passenger condensation probe");
            var passenger = go.AddComponent<PassengerController>(); passenger.enabled = false;
            var world = World.GetProperty("Instance", Static).GetValue(null, null);
            var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null); var parent = player.parent; var position = player.position;
            var rows = ReadOccupancyRows("condensation-probe.json"); var iceRows = ReadOccupancyRows("window-ice-probe.json");
            var sweatRows = ReadOccupancyRows("passenger-condensation-probe.json");
            try
            {
                foreach (string path in new[] { "CORRIS/Simulation/CarTempCorris", "SORBET(190-200psi)/Simulation/CarTempSorbet",
                    "JOBS/TAXIJOB/MACHTWAGEN/Simulation/CarTempTaxi" })
                using (var f = new CondensationFixture(rows, iceRows, path))
                {
                    var native = NativeBagPartChecks.State(f.Ice.Frost, "Player in?");
                    foreach (Dictionary<string, object> s in (IEnumerable)NativeBagPartChecks.Find(sweatRows, path, "GlassFrosting")["states"])
                    {
                        if ((string)s["name"] != native.Name) continue;
                        var actions = new List<FsmStateAction>();
                        foreach (Dictionary<string, object> a in (IEnumerable)s["actions"]) actions.Add(ReadCondensationAction(a, f.Ice.Frost));
                        native.Actions = actions.ToArray(); foreach (var action in native.Actions) action.Init(native);
                    }
                    Set(native.Actions[3], "float1", globals);
                    native.Transitions = new[] { new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("FINISHED"), ToState = "Defrosting" } };
                    var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                    items[VehicleId] = item; Set(item, "Id", VehicleId); Set(item, "Body", f.Ice.Body); Set(item, "IsVehicle", true);
                    Set(item, "Path", path.Substring(0, path.IndexOf("/Simulation/", StringComparison.Ordinal)));
                    CallStatic("EnsureClimateProbe", item); Set(item, "LoggedClimateApply", true); Set(item, "NextClimateDiagAt", float.MaxValue);
                    var localIn = f.Ice.Frost.FsmVariables.FindFsmBool("PlayerIn");
                    var rate = f.Ice.Frost.FsmVariables.FindFsmFloat("FrostingRate");
                    float emptyRate = f.Ice.Frost.FsmVariables.FindFsmFloat("DefaultRate").Value;
                    string prefix = "passenger condensation " + f.Ice.Body.gameObject.name + ": ";
                    Action<bool, byte> mode = (host, owner) =>
                    {
                        SetProperty(session, "IsHost", host); SetProperty(session, "State", host ? SessionState.Hosting : SessionState.Connected);
                        SetProperty(session, "LocalPlayerId", host ? (byte)0 : (byte)3); Set(item, "RemoteOwner", owner); Set(item, "LocallyOwned", false);
                    };
                    Func<byte, uint, byte, ushort, PassengerState> seat = (id, car, index, sequence) =>
                        new PassengerState { PlayerId = id, VehicleId = car, SeatIndex = index, Sequence = sequence };
                    Action<RemotePlayer, float> fresh = (p, sweat) =>
                    { p.HasSweat = true; p.Sweat = sweat; p.LastTransformTime = Time.unscaledTime; p.IsDead = false; };
                    Action clean = () =>
                    {
                        Call(vehicles, "ClearClimateStreams"); seats.Clear(); f.Seed(); globals.Value = 3; localIn.Value = false;
                        ((IDictionary)Get(passenger, "_remoteSeats")).Clear(); ((IDictionary)Get(passenger, "_remoteSeatSequences")).Clear();
                        Set(passenger, "_seated", false); Set(passenger, "_disabled", false);
                        foreach (var p in new[] { one, two }) { p.HasSweat = false; p.Sweat = 0; p.LastTransformTime = 0; p.LastTransformSequence = 0; p.IsDead = false; }
                        peers[first] = one; peers[second] = two; player.SetParent(parent, false); player.position = position;
                        mode(true, 255); capture.Packets.Clear(); Call(vehicles, "EnsurePassengerCondensation", item);
                        Require(Get(item, "PassengerCondensation") != null, "Native passenger condensation failed to bind.");
                    };
                    Action<float> run = expected =>
                    {
                        float before = globals.Value; bool entry = localIn.Value;
                        NativeBagPartChecks.Fire(f.Ice.Frost, "Probe idle"); NativeBagPartChecks.Fire(f.Ice.Frost, "Player in?");
                        Require(Mathf.Abs(rate.Value - expected) < .000001f, "Wrong native condensation rate: " + rate.Value + " expected " + expected);
                        Require(globals.Value == before && localIn.Value == entry && ReferenceEquals(Get(native.Actions[2], "boolVariable"), localIn)
                            && ReferenceEquals(Get(native.Actions[3], "float1"), globals), "Scoped condensation changed a native input or left it borrowed.");
                    };
                    SweatPacket packet = (peer, id, seq, sweat, available) => Call(session, "OnPacketReceived", peer,
                        PacketCodec.Encode(new PlayerTransform { PlayerId = id, Sequence = seq, HasSweat = available, Sweat = sweat }), Channel.UnreliableSequenced);

                    check(prefix + "empty cabin follows native default growth", () => { clean(); run(emptyRate); });
                    check(prefix + "accepted remote passenger supplies sweat without local entry or driving", () =>
                    {
                        clean(); fresh(one, 15); seats.Apply(seat(1, VehicleId, 0, 1), (_, _) => true); run(.05f);
                        Require(!localIn.Value && !(bool)Call(world, "IsLocalPlayerDriving", item)!, "Remote passenger promoted observer into driver.");
                    });
                    check(prefix + "two dry passengers combine native minimum contributions", () =>
                    {
                        clean(); fresh(one, 0); fresh(two, 0); seats.Apply(seat(1, VehicleId, 0, 1), (_, _) => true);
                        seats.Apply(seat(2, VehicleId, 1, 1), (_, _) => true); run(.04f);
                        float before = f.Amount.Value; f.Action("Defrosting", 0).OnUpdate();
                        Require(Mathf.Abs(f.Amount.Value - (before + .04f * Time.deltaTime)) < .000001f, "Native per-second growth changed.");
                    });
                    check(prefix + "wet occupants cannot exceed the native rate ceiling", () =>
                    {
                        clean(); fresh(one, 100); fresh(two, 100); seats.Apply(seat(1, VehicleId, 0, 1), (_, _) => true);
                        seats.Apply(seat(2, VehicleId, 1, 1), (_, _) => true); run(.1f);
                    });
                    check(prefix + "local driver and passenger combine without modifying global sweat", () =>
                    {
                        clean(); localIn.Value = true; fresh(one, 15); seats.Apply(seat(1, VehicleId, 0, 1), (_, _) => true); run(.07f);
                        localIn.Value = false; seats.Clear(); player.SetParent(f.Ice.Body.transform, false); run(.02f);
                        player.SetParent(parent, false); player.position = position;
                    });
                    check(prefix + "local passenger contributes while native entry stays false", () =>
                    {
                        clean(); Set(passenger, "_seated", true); Set(passenger, "_seatedVehicleId", VehicleId); run(.02f);
                        Require(!localIn.Value && !(bool)Call(world, "IsLocalPlayerDriving", item)!, "Local passenger gained driver authority.");
                    });
                    check(prefix + "missing sweat uses only the accepted occupant minimum", () =>
                    {
                        clean(); fresh(one, 0); one.HasSweat = false; seats.Apply(seat(1, VehicleId, 0, 1), (_, _) => true); run(.02f);
                    });
                    check(prefix + "old future dead and departed player reports cannot fog the cabin", () =>
                    {
                        clean(); fresh(one, 15); seats.Apply(seat(1, VehicleId, 0, 1), (_, _) => true);
                        one.LastTransformTime = Time.unscaledTime - 5; run(emptyRate);
                        one.LastTransformTime = Time.unscaledTime + 1; run(emptyRate);
                        fresh(one, 15); one.IsDead = true; run(emptyRate); one.IsDead = false;
                        try { peers.Remove(first); run(emptyRate); } finally { peers[first] = one; }
                    });
                    check(prefix + "seat moves rejections and exits stop the old cabin contribution", () =>
                    {
                        clean(); fresh(one, 15); seats.Apply(seat(1, VehicleId + 1, 0, 1), (_, _) => true); run(emptyRate);
                        seats.Apply(seat(1, VehicleId, 0, 2), (_, _) => false); run(emptyRate);
                        seats.Apply(seat(1, VehicleId, 0, 3), (_, _) => true); run(.05f);
                        seats.Apply(seat(1, 0, 255, 2), (_, _) => true); run(.05f);
                        seats.Apply(seat(1, 0, 255, 4), (_, _) => true); run(emptyRate);
                    });
                    check(prefix + "guest climate owner uses accepted remote seats", () =>
                    {
                        clean(); mode(false, 255); Set(item, "LocallyOwned", true); fresh(one, 15);
                        passenger.OnRemotePassengerState(seat(1, VehicleId, 0, 1)); run(.05f);
                        passenger.OnRemotePassengerState(seat(1, 0, 255, 2)); run(emptyRate);
                    });
                    check(prefix + "guest climate owner includes a host passenger", () =>
                    {
                        clean(); mode(false, 255); Set(item, "LocallyOwned", true); var peer = new PeerId(999);
                        try
                        {
                            var host = new RemotePlayer { PlayerId = 0, Peer = peer }; peers.Add(peer, host); fresh(host, 18);
                            passenger.OnRemotePassengerState(seat(0, VehicleId, 0, 1)); run(.06f);
                        }
                        finally { peers.Remove(peer); }
                    });
                    check(prefix + "observers and delegated hosts leave native inputs unchanged", () =>
                    {
                        clean(); fresh(one, 15); seats.Apply(seat(1, VehicleId, 0, 1), (_, _) => true); mode(true, 1); run(emptyRate);
                        mode(false, 1); passenger.OnRemotePassengerState(seat(1, VehicleId, 0, 1)); run(emptyRate);
                        localIn.Value = true; run(.02f);
                    });
                    check(prefix + "disconnected session does not project remote occupants", () =>
                    {
                        clean(); fresh(one, 15); seats.Apply(seat(1, VehicleId, 0, 1), (_, _) => true);
                        SetProperty(session, "State", SessionState.Idle); run(emptyRate);
                    });
                    check(prefix + "native result enters the existing climate stream", () =>
                    {
                        clean(); fresh(one, 15); seats.Apply(seat(1, VehicleId, 0, 1), (_, _) => true); run(.05f);
                        f.Amount.Value = .4f; f.NativeColor("Defrosting"); Call(vehicles, "UpdateVehicleClimate", session);
                        Require(capture.Packets.Count > 0, "Missing resulting climate.");
                        foreach (var p in capture.Packets) Require(p.Message is VehicleClimate c && c.PlayerIn && c.Frost == 102 && c.Fog == 0,
                            "Passenger result bypassed normal condensation/occupancy capture.");
                    });
                    check(prefix + "changed native arithmetic disables only the input binding and can recover", () =>
                    {
                        clean(); fresh(one, 15); seats.Apply(seat(1, VehicleId, 0, 1), (_, _) => true); var divisor = Get(native.Actions[3], "float2");
                        try
                        {
                            Set(native.Actions[3], "float2", new FsmFloat(400)); Call(vehicles, "EnsurePassengerCondensation", item);
                            Require(Get(item, "PassengerCondensation") == null, "Changed arithmetic retained input binding."); run(emptyRate);
                        }
                        finally { Set(native.Actions[3], "float2", divisor); Set(item, "NextPassengerCondensationProbeAt", 0f); }
                        Call(vehicles, "EnsurePassengerCondensation", item); run(.05f);
                    });
                    check(prefix + "nested and exceptional native reads restore borrowed operands", () =>
                    {
                        clean(); fresh(one, 15); seats.Apply(seat(1, VehicleId, 0, 1), (_, _) => true);
                        var hooks = Vehicles.GetNestedType("NativePassengerCondensationHooks", System.Reflection.BindingFlags.NonPublic);
                        var before = hooks.GetMethod("Before", Static); var after = hooks.GetMethod("After", Static);
                        foreach (int index in new[] { 2, 3 })
                        {
                            var a = new object?[] { native.Actions[index], null }; before.Invoke(null, a); Require(a[1] != null, "Outer native read did not borrow.");
                            var b = new object?[] { native.Actions[index], null }; before.Invoke(null, b); Require(b[1] != null, "Nested native read did not borrow.");
                            var fault = new InvalidOperationException("probe native fault");
                            Require(ReferenceEquals(after.Invoke(null, new object?[] { fault, b[1] }), fault), "Native fault was swallowed.");
                            after.Invoke(null, new object?[] { null, a[1] });
                        }
                        Require(ReferenceEquals(Get(native.Actions[2], "boolVariable"), localIn)
                            && ReferenceEquals(Get(native.Actions[3], "float1"), globals), "Exceptional read retained temporary input."); run(.05f);
                    });
                    check(prefix + "session clear removes bindings even after vehicle metadata disappears", () =>
                    {
                        clean(); fresh(one, 15); seats.Apply(seat(1, VehicleId, 0, 1), (_, _) => true);
                        items.Remove(VehicleId); Call(vehicles, "ClearClimateStreams"); items[VehicleId] = item;
                        Require(Get(item, "PassengerCondensation") == null, "Unindexed vehicle retained a native hook binding."); run(emptyRate);
                        Set(item, "NextPassengerCondensationProbeAt", 0f); Call(vehicles, "EnsurePassengerCondensation", item); run(.05f);
                    });
                    if (path.StartsWith("CORRIS/", StringComparison.Ordinal))
                    {
                        check(prefix + "authenticated first-zero pose carries sweat and relays on the same channel", () =>
                        {
                            clean(); packet(first, 1, 0, 15, true); seats.Apply(seat(1, VehicleId, 0, 1), (_, _) => true); run(.05f);
                            Require(one.HasSweat && one.Sweat == 15 && capture.Packets.Count == 1 && capture.Packets[0].Peer == second
                                && capture.Packets[0].Channel == Channel.UnreliableSequenced && ((PlayerTransform)capture.Packets[0].Message).Sweat == 15,
                                "Authenticated sweat did not follow accepted pose relay.");
                        });
                        check(prefix + "stale duplicate forged and unknown player data cannot refresh or relay sweat", () =>
                        {
                            clean(); packet(first, 1, 8, 15, true); capture.Packets.Clear(); float time = one.LastTransformTime;
                            packet(first, 1, 7, 100, true); packet(first, 1, 8, 100, true); packet(first, 2, 9, 100, true);
                            Call(session, "HandlePlayerTransform", first, new PlayerTransform { PlayerId = 99, HasSweat = true, Sweat = 100 });
                            Require(one.Sweat == 15 && one.LastTransformTime == time && capture.Packets.Count == 0, "Rejected pose changed sweat or relay.");
                        });
                        check(prefix + "unavailable reports clear old sweat and rollover preserves freshness", () =>
                        {
                            clean(); packet(first, 1, 65535, 100, true); capture.Packets.Clear(); packet(first, 1, 0, 0, false);
                            Require(!one.HasSweat && one.Sweat == 0 && one.LastTransformSequence == 0 && capture.Packets.Count == 1, "Sweat availability/wrap was lost.");
                        });
                        check(prefix + "guest accepts sweat only through the selected host relay", () =>
                        {
                            clean(); mode(false, 255); packet(first, 1, 1, 100, true); Require(!one.HasSweat, "Guest accepted direct peer sweat.");
                            packet(new PeerId(999), 1, 1, 15, true); Require(one.HasSweat && one.Sweat == 15, "Host-relayed sweat rejected.");
                        });
                        check(prefix + "native local sweat reader reports absence instead of nonfinite or out-of-range values", () =>
                        {
                            clean(); var method = Vehicles.GetMethod("TryReadLocalSweat", Static);
                            foreach (float value in new[] { 0f, 23.75f, 100f, float.NaN, float.PositiveInfinity, -1f, 101f })
                            {
                                globals.Value = value; var args = new object[] { 0f }; bool valid = (bool)method.Invoke(null, args);
                                Require(valid == PassengerCondensationPolicy.ValidSweat(value) && (float)args[0] == (valid ? value : 0), "Invalid local sweat escaped into pose sender.");
                            }
                        });
                    }
                    clean(); Call(vehicles, "ClearClimateStreams");
                }
            }
            finally
            {
                globals.Value = savedSweat; seats.Clear(); peers[first] = one; peers[second] = two;
                SetStaticProperty(typeof(PassengerController), "Instance", savedPassenger); UnityEngine.Object.DestroyImmediate(go);
                player.SetParent(parent, false); player.position = position; items[VehicleId] = original; reset();
            }
        }
    }
}
