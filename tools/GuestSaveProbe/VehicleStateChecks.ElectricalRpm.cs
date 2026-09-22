using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private static void RunElectricalRpmChecks(Action<string, Action> check, object item, object vehicles,
            SessionManager session, Action reset, Action<bool, byte> mode)
        {
            string[] fields = { "Body", "Path", "EngineRevsVar", "NativeEngineRpm", "NativeEngineRpmOutput", "RequiresNativeEngineRpm" };
            var saved = new Dictionary<string, object>(); foreach (string field in fields) saved[field] = Get(item, field);
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var property = policy.GetType().GetProperty("ProtectWorld", Members); bool wasProtected = (bool)property.GetValue(policy, null);
            Action<bool> protect = value => property.GetSetMethod(true).Invoke(policy, new object[] { value });
            using (GuestEngineInputChecks.SuspendProjectionForFixture())
            using (var f = new ElectricalTemperatureFixture(charging: true))
            try
            {
                reset(); protect(false); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); f.Item = item; f.Start();
                ushort sequence = 0;
                Func<ushort, VehicleState> receive = rpm =>
                {
                    mode(true, 1); var state = State(1, sequence++, rpm);
                    Require((bool)Call(vehicles, "OnRemoteVehicleState", state)!, "Electrical driver state rejected."); f.RebindRpm(); return state;
                };
                check("host electrical RPM: host update binds before the first driver report", () =>
                {
                    f.Rpm.Value = 6500; f.Engine.Value = 90; mode(true, 1); Set(item, "NextElectricalInputProbeAt", 0f);
                    Call(vehicles, "UpdateVehicleStates", session);
                    Require(Get(item, "NativeElectrical") != null && Get(item, "AcceptedVehicleState") == null,
                        "Electrical readers waited for the first driver packet.");
                    Require(f.ChargeCycle().Values[0] == 0 && f.Rpm.Value == 6500, "Unseeded driver used host RPM for charging.");
                });
                check("host electrical RPM: all three native readers bind without global writes", () =>
                {
                    f.Rpm.Value = 6500; f.Engine.Value = 90; receive(2500);
                    Require(Get(item, "NativeElectrical") != null && f.Rpm.Value == 6500 && f.Engine.Value == 90, "Electrical binding failed or changed native globals."); f.OriginalRpmInputs();
                });
                foreach (ushort rpm in new ushort[] { 0, 399, 400, 401, 800, 3000, 6500 })
                    foreach (string condition in new[] { "working", "damaged", "alternator", "fanbelt", "wiring", "regulator" })
                        foreach (float heat in new[] { -25f, 90f })
                        {
                            ushort speed = rpm; string fault = condition; float temperature = heat;
                            check("host electrical RPM: native charging at " + speed + " RPM " + fault + " " + temperature + " C", () =>
                            {
                                mode(true, 255); f.Engine.Value = temperature; f.Rpm.Value = speed; var expected = f.ChargeCycle(fault);
                                f.Rpm.Value = 6500; receive(speed); SameElectrical(expected, f.ChargeCycle(fault));
                                Require(f.Rpm.Value == 6500 && f.Engine.Value == temperature, "Electrical RPM projection rewrote host globals."); f.OriginalRpmInputs();
                            });
                        }
                check("host electrical RPM: native delay writes charge and drain with host alternator condition", () =>
                {
                    Require(Time.deltaTime > 0, "Native charge-writer check needs a positive frame duration.");
                    f.Engine.Value = 90;
                    foreach (float condition in new[] { 0f, 50f, 98f })
                    {
                        mode(true, 255); f.Rpm.Value = 3000; var expected = f.ChargeCycle("working", condition);
                        f.Rpm.Value = 0; receive(3000); var actual = f.ChargeCycle("working", condition); SameElectrical(expected, actual);
                        Require(actual.Branch == "Delay" && actual.Values[0] > 0 && actual.Values[2] > 100 && actual.Values[3] == 147
                            && actual.Values[4] < condition, "Native host charge or alternator wear did not advance.");
                    }
                    mode(true, 255); f.Rpm.Value = 3000; var drain = f.ChargeCycle("fanbelt"); f.Rpm.Value = 0; receive(3000);
                    var battery = f.ChargeCycle("fanbelt"); SameElectrical(drain, battery);
                    Require(battery.Values[0] == 0 && battery.Values[1] > 0 && battery.Values[2] < 100 && battery.Values[3] == 126,
                        "Missing fanbelt did not retain native battery drain.");
                });
                check("host electrical RPM: accepted packet is copied and stale replays do not change charging", () =>
                {
                    f.Engine.Value = 90; var state = receive(401); var expected = f.ChargeCycle(); state.Rpm = 6500;
                    SameElectrical(expected, f.ChargeCycle()); Require(!(bool)Call(vehicles, "OnRemoteVehicleState", state)!, "Duplicate RPM accepted."); SameElectrical(expected, f.ChargeCycle());
                    state.VehicleId++; Require(!(bool)Call(vehicles, "OnRemoteVehicleState", state)!, "Foreign electrical state accepted."); SameElectrical(expected, f.ChargeCycle());
                });
                check("host electrical RPM: missing stale mismatched and snapshot samples use native zero RPM", () =>
                {
                    f.Engine.Value = 90; mode(true, 255); f.Rpm.Value = 0; var expected = f.ChargeCycle(); f.Rpm.Value = 6500;
                    foreach (string fault in new[] { "missing", "expired", "owner", "vehicle", "snapshot", "invalid" })
                    {
                        receive(3000); var state = (VehicleState)Get(item, "AcceptedVehicleState");
                        if (fault == "missing") Set(item, "AcceptedVehicleState", null!);
                        else if (fault == "expired") Set(item, "RemoteEngineUntil", -999f);
                        else if (fault == "owner") state.OwnerPlayerId = 2;
                        else if (fault == "vehicle") state.VehicleId++;
                        else if (fault == "snapshot") state.Sequence = VehicleState.SnapshotSequence;
                        else state.Flags = 255;
                        SameElectrical(expected, f.ChargeCycle());
                    }
                    f.OriginalRpmInputs();
                });
                check("host electrical RPM: optional torque and movement absence does not suppress charging", () =>
                {
                    var state = receive(3000); Require(!state.TorqueAvailable && !state.MovementSpeedAvailable, "Fixture unexpectedly supplies optional inputs.");
                    Require(f.ChargeCycle().Values[0] > 0, "Missing optional telemetry disabled valid RPM charging.");
                });
                check("host electrical RPM: driver handoff and departure cannot reuse the former driver's RPM", () =>
                {
                    receive(6500); mode(true, 2); var unseeded = f.ChargeCycle(); Require(unseeded.Values[0] == 0, "New driver inherited stale charging.");
                    Require((bool)Call(vehicles, "OnRemoteVehicleState", State(2, 0, 401))!, "Driver two sequence zero rejected.");
                    Require(f.ChargeCycle().Values[0] > 0, "New driver's engine failed to charge.");
                    Call(vehicles, "ForgetVehicleStatePlayer", (byte)2); Require(f.ChargeCycle().Values[0] == 0, "Departed driver's RPM remained live.");
                });
                check("host electrical RPM: local driver seating guest mode and disconnect retain native inputs", () =>
                {
                    receive(0); f.Rpm.Value = 3000; mode(true, 255); var expected = f.ChargeCycle(); mode(true, 1);
                    Set(item, "LocallyOwned", true); try { SameElectrical(expected, f.ChargeCycle()); } finally { Set(item, "LocallyOwned", false); }
                    var world = World.GetProperty("Instance", Static).GetValue(null, null); var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null); var parent = player.parent;
                    player.SetParent(f.Body.transform, false); try { SameElectrical(expected, f.ChargeCycle()); } finally { player.SetParent(parent, false); }
                    mode(false, 1); SameElectrical(expected, f.ChargeCycle()); mode(true, 1); SetProperty(session, "State", SessionState.Idle); SameElectrical(expected, f.ChargeCycle()); mode(true, 1);
                    protect(true);
                    try
                    {
                        var before = Vehicles.GetNestedType("NativeElectricalHooks", Members).GetMethod("Before", Static);
                        foreach (object read in (IEnumerable)Get(Get(item, "NativeElectrical"), "Reads"))
                        { var args = new object?[] { Get(read, "Action"), null }; before.Invoke(null, args); Require(args[1] == null, "Protected save acquired an electrical RPM scope."); }
                    }
                    finally { protect(false); }
                    f.OriginalRpmInputs();
                });
                check("host electrical RPM: nested native boundaries restore their operands on exceptions", () =>
                {
                    receive(3000); var hooks = Vehicles.GetNestedType("NativeElectricalHooks", Members); var before = hooks.GetMethod("Before", Static); var after = hooks.GetMethod("After", Static);
                    foreach (object read in (IEnumerable)Get(Get(item, "NativeElectrical"), "Reads"))
                    {
                        var action = Get(read, "Action"); var args = new object?[] { action, null }; before.Invoke(null, args); var outer = args[1];
                        args[1] = null; before.Invoke(null, args); Require(outer != null && args[1] != null, "Nested electrical RPM scope abstained.");
                        var error = new InvalidOperationException("fixture error"); Require(ReferenceEquals(after.Invoke(null, new[] { error, args[1] }), error), "Native electrical exception lost.");
                        Require(!ReferenceEquals(Get(action, "float1"), f.Rpm), "Nested finalizer restored outer RPM too early.");
                        after.Invoke(null, new object?[] { null, outer }); f.OriginalRpmInputs();
                    }
                });
                foreach (string fault in new[] { "operand", "threshold", "event", "cadence", "charging operand", "charging output", "efficiency", "drain divisor", "drain output", "drain minimum", "enabled", "shadow", "duplicate", "array", "transition", "catalog" })
                {
                    string changed = fault; check("host electrical RPM: changed " + changed + " disables the group and repairs", () => f.RpmFault(changed));
                }
                check("host electrical RPM: native callback detects changed operands before the next scan", () =>
                {
                    receive(3000); var action = NativeBagPartChecks.State(f.Main, "Engine running?").Actions[0]; var original = Get(action, "float2");
                    try { Set(action, "float2", new FsmFloat(450)); f.ChargeCycle(); Require(Get(item, "NativeElectrical") == null, "Changed native threshold stayed bound."); }
                    finally { Set(action, "float2", original); }
                    f.RebindRpm(); Require(Get(item, "NativeElectrical") != null, "Native callback retirement did not recover.");
                });
                check("host electrical RPM: cleanup restores native input and the next sample rebinds", () =>
                {
                    mode(true, 255); f.Rpm.Value = 3000; var expected = f.ChargeCycle(); receive(0); Call(vehicles, "ClearVehicleStateStreams");
                    Require(Get(item, "NativeElectrical") == null, "Electrical RPM survived cleanup."); SameElectrical(expected, f.ChargeCycle());
                    receive(0); Require(f.ChargeCycle().Values[0] == 0, "Fresh stop sample did not rebind."); f.OriginalRpmInputs();
                });
            }
            finally { reset(); foreach (string field in fields) Set(item, field, saved[field]); protect(wasProtected); }
        }
    }
}
