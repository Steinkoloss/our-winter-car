using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private static void RunCoolingRpmChecks(Action<string, Action> check, SpeedFixture f, object item, object vehicles,
            SessionManager session, Action reset, Action<bool, byte> mode, Action<bool> protect)
        {
            ushort sequence = 2000;
            Func<ushort, VehicleState> receive = rpm => {
                mode(true, 1); var sample = State(1, sequence++, rpm);
                Require((bool)Call(vehicles, "OnRemoteVehicleState", sample)!, "Cooling RPM sample rejected."); f.Rebind(); return sample; };
            try
            {
                reset();
                check("host cooling rpm: all five movement and RPM reads bind together", () =>
                {
                    receive(2500); var binding = Get(item, "NativeCooling");
                    Require(binding != null && ((Array)Get(binding, "Reads")).Length == 5, "Cooling group is incomplete."); f.OriginalInputs();
                });
                foreach (ushort rpm in new ushort[] { 0, 99, 100, 199, 200, 201, 800, 2500, 6500, ushort.MaxValue })
                    foreach (string parts in new[] { "healthy", "missing belt", "missing fan", "missing pump", "worn pump", "wear threshold" })
                    {
                        ushort revs = rpm; string setup = parts;
                        check("host cooling rpm: native local and delegated pump fan leak agree at " + revs + " RPM with " + setup, () =>
                        {
                            f.ConfigureCoolingParts(setup); mode(true, 255); f.Rpm.Value = revs; var expected = f.CoolingCycle();
                            receive(revs); f.Rpm.Value = 0; var actual = f.CoolingCycle();
                            Require(actual[0] == expected[0] && actual[1] == expected[1] && actual[2] == expected[2], "Delegated cooling differs from native local calculations.");
                            Require(f.Cooling.ActiveStateName == (revs >= 200 ? "Housing tightness" : "Water leak"), "Native leak threshold changed.");
                            bool open = revs >= 100 && setup != "missing belt" && setup != "missing pump" && setup != "worn pump";
                            Require(actual[0] == (open ? .75f : .0001f), "Native pump installation/wear/RPM gate changed.");
                            Require((actual[1] > 0) == (revs > 0 && setup != "missing belt" && setup != "missing fan"), "Native fan installation/belt gate changed.");
                            Require(f.Rpm.Value == 0 && f.PumpWear.Value == (setup == "worn pump" ? 6 : setup == "wear threshold" ? 7 : 70),
                                "Scoped RPM changed host inputs."); f.OriginalInputs();
                        });
                    }
                check("host cooling rpm: host pump efficiency and fan modifier remain effective", () =>
                {
                    f.ConfigureCoolingParts("healthy"); receive(2500); f.Rpm.Value = 0;
                    f.PumpEfficiency.Value = .25f; var low = f.CoolingCycle(); f.PumpEfficiency.Value = .9f;
                    f.Local("CoolingFanModifier").Value *= 2; var high = f.CoolingCycle();
                    Require(low[0] == .25f && high[0] == .9f && high[1] == low[1] / 2, "Driver RPM bypassed native host part inputs.");
                });
                check("host cooling rpm: arrival writes no cooling rate circulation or temperature", () =>
                {
                    f.ConfigureCoolingParts("healthy"); f.Local("CoolingFanRate").Value = .37f; f.Local("Circulation").Value = .42f;
                    f.Local("CoolantTemp").Value = 87; f.Temperature.Value = 96; receive(2500);
                    Require(f.Local("CoolingFanRate").Value == .37f && f.Local("Circulation").Value == .42f
                        && f.Local("CoolantTemp").Value == 87 && f.Temperature.Value == 96, "Packet advanced cooling.");
                });
                check("host cooling rpm: missing expired wrong-owner and snapshot inputs use stopped engine", () =>
                {
                    f.ConfigureCoolingParts("healthy");
                    Action stopped = () => { f.Rpm.Value = 6500; var value = f.CoolingCycle();
                        Require(value[0] == .0001f && value[1] == 0 && f.Cooling.ActiveStateName == "Water leak", "Missing RPM retained a running engine."); f.OriginalInputs(); };
                    receive(2500); Set(item, "RemoteEngineUntil", -1f); stopped();
                    receive(2500); Set(item, "AcceptedVehicleState", null); stopped();
                    receive(2500); Set(item, "RemoteOwner", (byte)2); stopped();
                    var snapshot = receive(2500); snapshot.Sequence = VehicleState.SnapshotSequence; Set(item, "AcceptedVehicleState", snapshot); stopped();
                });
                check("host cooling rpm: absent movement and torque leave valid RPM usable", () =>
                {
                    f.ConfigureCoolingParts("healthy"); var sample = receive(2500); Require(!sample.MovementSpeedAvailable && !sample.TorqueAvailable, "Test sample unexpectedly has optional data.");
                    f.Rpm.Value = 0; var actual = f.CoolingCycle(); Require(actual[0] == .75f && actual[1] > 0, "Optional speed/load gated native cooling RPM.");
                });
                check("host cooling rpm: stale forged mutated and handed-off samples keep one RPM source", () =>
                {
                    f.ConfigureCoolingParts("healthy"); var sample = receive(100); sample.Rpm = 6500;
                    Require(!(bool)Call(vehicles, "OnRemoteVehicleState", sample)!, "Stale RPM accepted.");
                    sample.Sequence++; sample.OwnerPlayerId = 2; Require(!(bool)Call(vehicles, "OnRemoteVehicleState", sample)!, "Foreign RPM accepted.");
                    f.Rpm.Value = 0; f.CoolingCycle(); Require(f.Cooling.ActiveStateName == "Water leak", "Caller/rejected packet changed accepted RPM.");
                    mode(true, 2); sample.Sequence = 0; sample.Rpm = 200;
                    Require((bool)Call(vehicles, "OnRemoteVehicleState", sample)!, "New driver's sequence zero rejected.");
                    f.CoolingCycle(); Require(f.Cooling.ActiveStateName == "Housing tightness", "Handoff retained previous driver's RPM.");
                });
                check("host cooling rpm: local ownership seating guest mode disconnect and protection yield", () =>
                {
                    f.ConfigureCoolingParts("healthy"); receive(0); f.Rpm.Value = 2500;
                    Action native = () => { var value = f.CoolingCycle(); Require(value[0] == .75f && value[1] > 0 && f.Cooling.ActiveStateName == "Housing tightness", "Remote RPM overrode native authority."); };
                    Set(item, "LocallyOwned", true); try { native(); } finally { Set(item, "LocallyOwned", false); }
                    var world = World.GetProperty("Instance", Static).GetValue(null, null); var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null); var parent = player.parent;
                    player.SetParent(f.Body.transform, false); try { native(); } finally { player.SetParent(parent, false); }
                    mode(false, 1); native(); mode(true, 1); SetProperty(session, "State", SessionState.Idle); native(); mode(true, 1);
                    protect(true);
                    try
                    {
                        var prefix = CoolingHooks().GetMethod("BeforeCoolingRead", Static);
                        foreach (var action in f.RpmActions())
                        { object?[] args = { action, null }; prefix.Invoke(null, args); Require(args[1] == null, "Protected save acquired RPM scope."); }
                        f.OriginalInputs();
                    }
                    finally { protect(false); }
                });
                check("host cooling rpm: nested scopes and native exceptions restore every RPM operand", () =>
                {
                    receive(2500); var hooks = CoolingHooks(); var prefix = hooks.GetMethod("BeforeCoolingRead", Static); var finalizer = hooks.GetMethod("AfterCoolingRead", Static);
                    foreach (var action in f.RpmActions())
                    {
                        object?[] outer = { action, null }, inner = { action, null }; prefix.Invoke(null, outer); prefix.Invoke(null, inner);
                        Require(outer[1] != null && inner[1] != null, "Nested RPM scope was lost.");
                        finalizer.Invoke(null, new object?[] { null, inner[1] }); Require(!ReferenceEquals(Get(action, "float1"), f.Rpm), "Inner completion removed outer RPM scope.");
                        var error = new InvalidOperationException("native cooling RPM failure");
                        Require(ReferenceEquals(error, finalizer.Invoke(null, new object?[] { error, outer[1] })), "Native exception was swallowed."); f.OriginalInputs();
                    }
                });
                foreach (string fault in new[] { "pump threshold", "pump event", "fan divisor", "fan output", "fan timing", "leak operand", "leak threshold" })
                {
                    string changed = fault;
                    check("host cooling rpm: changed " + changed + " clears all five readers and repairs", () =>
                    {
                        receive(2500); FsmStateAction action; string field; object replacement;
                        if (changed == "pump threshold") { action = f.RpmActions()[0]; field = "float2"; replacement = new FsmFloat(101); }
                        else if (changed == "pump event") { action = f.RpmActions()[0]; field = "lessThan"; replacement = new FsmEvent("FINISHED"); }
                        else if (changed == "fan divisor") { action = f.RpmActions()[1]; field = "float2"; replacement = new FsmFloat(100); }
                        else if (changed == "fan output") { action = f.RpmActions()[1]; field = "storeResult"; replacement = f.Rpm; }
                        else if (changed == "fan timing") { action = f.RpmActions()[1]; field = "everyFrame"; replacement = true; }
                        else if (changed == "leak operand") { action = f.RpmActions()[2]; field = "float1"; replacement = new FsmFloat(2500); }
                        else { action = f.RpmActions()[2]; field = "float2"; replacement = new FsmFloat(201); }
                        object original = Get(action, field); Set(action, field, replacement);
                        try { f.Rebind(); Require(Get(item, "NativeCooling") == null, "Malformed RPM left a partially bound group."); }
                        finally { Set(action, field, original); }
                        f.Rebind(); Require(Get(item, "NativeCooling") != null, "Repaired cooling did not bind."); f.OriginalInputs();
                    });
                }
                check("host cooling rpm: local RPM shadow rejects and repairs the group", () =>
                {
                    receive(2500); var original = f.Cooling.FsmVariables.FloatVariables;
                    var changed = new List<FsmFloat>(original) { new FsmFloat { Name = "RPM", UseVariable = true } };
                    f.Cooling.FsmVariables.FloatVariables = changed.ToArray();
                    try { f.Rebind(); Require(Get(item, "NativeCooling") == null, "Local shadow accepted as global RPM."); }
                    finally { f.Cooling.FsmVariables.FloatVariables = original; }
                    f.Rebind(); Require(Get(item, "NativeCooling") != null, "Global RPM repair did not bind.");
                });
                check("host cooling rpm: clearing stream removes RPM scopes alongside movement", () =>
                {
                    f.ConfigureCoolingParts("healthy"); receive(2500); Call(vehicles, "ClearVehicleStateStreams"); f.Rpm.Value = 0;
                    var value = f.CoolingCycle(); Require(Get(item, "NativeCooling") == null && value[0] == .0001f && value[1] == 0,
                        "Cleared driver left cooling RPM behind."); f.OriginalInputs();
                });
            }
            finally { reset(); f.ConfigureCoolingParts("healthy"); }
        }

        private static Type CoolingHooks() => Vehicles.GetNestedType("NativeCoolingHooks", System.Reflection.BindingFlags.NonPublic);

        private sealed partial class SpeedFixture
        {
            internal FsmBool BeltInstalled = null!, PumpInstalled = null!, FanInstalled = null!;
            internal FsmFloat PumpEfficiency = null!, PumpWear = null!;

            private void CreateCoolingParts()
            {
                PlayMakerFSM Data(string name)
                {
                    var obj = Child(_root, name); var data = obj.AddComponent<PlayMakerFSM>(); data.enabled = false;
                    Set(data, "fsm", new Fsm()); data.FsmName = "Data"; data.Fsm.StartState = "Idle";
                    data.Fsm.States = new[] { new FsmState(data.Fsm) { Name = "Idle", Actions = new FsmStateAction[0] } };
                    data.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true } };
                    data.FsmVariables.FloatVariables = new[] { new FsmFloat { Name = "Efficiency", UseVariable = true, Value = .75f }, new FsmFloat { Name = "Wear", UseVariable = true, Value = 70 } };
                    data.Fsm.Init(data); NativeBagPartChecks.Start(data); data.enabled = false;
                    Cooling.FsmVariables.FindFsmGameObject(name).Value = obj; return data;
                }
                var belt = Data("db_FanBelt"); var pump = Data("db_Waterpump"); var fan = Data("db_RadiatorFan");
                BeltInstalled = belt.FsmVariables.FindFsmBool("Installed"); PumpInstalled = pump.FsmVariables.FindFsmBool("Installed"); FanInstalled = fan.FsmVariables.FindFsmBool("Installed");
                PumpEfficiency = pump.FsmVariables.FindFsmFloat("Efficiency"); PumpWear = pump.FsmVariables.FindFsmFloat("Wear");
            }

            internal void ConfigureCoolingParts(string setup)
            {
                BeltInstalled.Value = setup != "missing belt"; FanInstalled.Value = setup != "missing fan"; PumpInstalled.Value = setup != "missing pump";
                PumpWear.Value = setup == "worn pump" ? 6 : setup == "wear threshold" ? 7 : 70; PumpEfficiency.Value = .75f;
                Local("CoolingFanModifier").Value = 16000; Local("WaterLevel").Value = 15;
            }

            internal float[] CoolingCycle()
            {
                NativeBagPartChecks.Fire(Cooling, "Water Pump 2"); float circulation = Local("Circulation").Value;
                float open = Cooling.FsmVariables.FindFsmBool("ThermostatOpen").Value ? 1 : 0;
                Local("CoolingFanRate").Value = 0; NativeBagPartChecks.Fire(Cooling, "Fan"); float fan = Local("CoolingFanRate").Value;
                NativeBagPartChecks.Fire(Cooling, "Motor on?"); return new[] { circulation, fan, open };
            }

            internal FsmStateAction[] RpmActions() => new[] { NativeBagPartChecks.State(Cooling, "Water Pump 2").Actions[2],
                NativeBagPartChecks.State(Cooling, "Fan").Actions[5], NativeBagPartChecks.State(Cooling, "Motor on?").Actions[0] };

            internal void RpmInputsRestored()
            { foreach (var action in RpmActions()) Require(ReferenceEquals(Get(action, "float1"), Rpm), "RPM operand was not restored."); }
        }
    }
}
