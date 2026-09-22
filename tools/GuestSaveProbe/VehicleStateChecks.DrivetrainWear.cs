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
        private static void RunHostDrivetrainWearChecks(Action<string, Action> check, object item, object vehicles,
            SessionManager session, Action reset, Action<bool, byte> mode)
        {
            string[] fields = { "Body", "Path", "EngineRevsVar", "NativeEngineRpm", "NativeEngineRpmOutput", "RequiresNativeEngineRpm" };
            var saved = new Dictionary<string, object>(); foreach (string name in fields) saved[name] = Get(item, name);
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protection = policy.GetType().GetProperty("ProtectWorld", Members); bool before = (bool)protection.GetValue(policy, null);
            Action<bool> protect = value => protection.GetSetMethod(true).Invoke(policy, new object[] { value });
            using (GuestEngineInputChecks.SuspendProjectionForFixture())
            try
            {
                protect(false);
                using (var f = new DifferentialFixture(true))
                {
                    reset(); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); f.Item = item;
                    ushort sequence = 0;
                    Action rebind = () => { Set(item, "NextDrivetrainWearProbeAt", 0f); CallStatic("EnsureDrivetrainWear", item); };
                    Action<float> receive = speed =>
                    {
                        mode(true, 1); var state = State(1, sequence++, 3000); state.DifferentialSpeedAvailable = true; state.DifferentialSpeed = speed;
                        Require((bool)Call(vehicles, "OnRemoteVehicleState", state)!, "Host rejected driver telemetry."); rebind();
                    };
                    Action cycle = f.CycleWear;
                    Action original = () =>
                    {
                        var sample = NativeBagPartChecks.State(f.Fsm, "State 1"); var wear = NativeBagPartChecks.State(f.Fsm, "Wear");
                        Require(ReferenceEquals(Get(sample.Actions[2], "float1"), f.Output), "Compare retained a scoped operand.");
                        for (int i = 0; i < 3; i++) Require(ReferenceEquals(Get(wear.Actions[i * 2], "float1"), f.Output), "Wear division retained a scoped operand.");
                    };
                    check("host drivetrain wear: native graph and all three saved mounts bind on driver acceptance", () =>
                    {
                        receive(3150); var binding = Get(item, "DrivetrainWear");
                        if (binding != null && !(bool)Get(binding, "Ready")) CallStatic("ValidateDrivetrainWear", binding);
                        Require(binding != null && (bool)Get(binding, "Ready") && f.Fsm.enabled, "Audited host wear graph did not bind."); original();
                    });
                    foreach (float speed in new[] { 0f, 1f, -1f, 1.01f, -1.01f, 3150f, -3150f, 22800f, -22800f })
                        check("host drivetrain wear: native and delegated cycles agree at differential speed " + speed, () =>
                        {
                            mode(true, 255); rebind(); f.SetSpeed(speed); f.ResetSaved(); cycle(); var expected = f.WearValues();
                            Require(Math.Abs(speed) <= 1 || expected[2] < 90, "Native local baseline did not execute wear.");
                            receive(speed); f.SetSpeed(0); f.ResetSaved(); cycle(); var actual = f.WearValues();
                            for (int i = 0; i < 3; i++) Require(actual[i] == expected[i], "Delegated wear differs at target " + i + ": " + actual[i] + " vs " + expected[i]);
                            Require(f.Fsm.ActiveStateName == "State 2" && f.Output.Value == 0 && f.Speed == 0, "Native wait or host scratch changed.");
                            var after = f.WearValues(); f.Fsm.Fsm.Update(); var next = f.WearValues();
                            for (int i = 0; i < 3; i++) Require(after[i] == next[i], "Wear repeated before the native wait elapsed."); original();
                        });
                    foreach (float speed in new[] { 22801f, -22801f, float.MaxValue, float.MinValue })
                        check("host drivetrain wear: excessive driver input wears no host mount " + speed, () =>
                        { receive(speed); f.SetSpeed(3150); f.ResetSaved(); cycle(); f.Unchanged(); Require(f.Fsm.ActiveStateName == "State 2", "Rejected input broke native cadence."); original(); });
                    check("host drivetrain wear: unavailable expired missing and mismatched samples contribute no wear", () =>
                    {
                        receive(3150); f.SetSpeed(3150); var accepted = (VehicleState)Get(item, "AcceptedVehicleState"); float until = (float)Get(item, "RemoteEngineUntil");
                        Action noWear = () => { f.ResetSaved(); cycle(); f.Unchanged(); original(); };
                        Set(item, "RemoteEngineUntil", -999f); noWear(); Set(item, "RemoteEngineUntil", until);
                        Set(item, "AcceptedVehicleState", null); noWear(); Set(item, "AcceptedVehicleState", accepted);
                        Set(item, "RemoteOwner", (byte)2); noWear(); Set(item, "RemoteOwner", (byte)1);
                        accepted.DifferentialSpeedAvailable = false; accepted.DifferentialSpeed = 0; noWear(); receive(3150);
                        f.ResetSaved(); cycle(); Require(f.WearValues()[2] < 90, "Fresh available sample failed to recover.");
                    });
                    check("host drivetrain wear: new driver sequence zero and rejected old owner preserve authority", () =>
                    {
                        mode(true, 2); var state = State(2, 0, 3000); state.DifferentialSpeedAvailable = true; state.DifferentialSpeed = 2280;
                        Require((bool)Call(vehicles, "OnRemoteVehicleState", state)!, "New driver's sequence zero rejected.");
                        var old = State(1, sequence++, 3000); old.DifferentialSpeedAvailable = true; old.DifferentialSpeed = 22800;
                        Require(!(bool)Call(vehicles, "OnRemoteVehicleState", old)!, "Former driver resumed wear.");
                        f.ResetSaved(); f.SetSpeed(0); cycle(); Require(Math.Abs(f.WearValues()[2] - 89.9f) < .00001f, "Wrong driver's wear applied.");
                    });
                    check("host drivetrain wear: local ownership seating and disconnected hosts keep native inputs", () =>
                    {
                        receive(3150); f.SetSpeed(0); Action native = () => { f.ResetSaved(); cycle(); f.Unchanged(); original(); };
                        Set(item, "LocallyOwned", true); native(); Set(item, "LocallyOwned", false);
                        var world = World.GetProperty("Instance", Static).GetValue(null, null); var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null); var parent = player.parent;
                        player.SetParent(f.Body.transform, false); try { native(); } finally { player.SetParent(parent, false); }
                        mode(false, 1); native(); mode(true, 1); SetProperty(session, "State", SessionState.Idle); native(); mode(true, 1);
                        cycle(); Require(f.WearValues()[2] < 90, "Delegation did not resume.");
                    });
                    check("host drivetrain wear: nonfinite authoritative wear blocks the entire cycle", () =>
                    {
                        receive(3150); var data = f.Fsm.FsmVariables.FindFsmGameObject("db_Gearbox").Value.GetComponent<PlayMakerFSM>();
                        var wear = data.FsmVariables.FindFsmFloat("Wear"); f.ResetSaved(); wear.Value = float.NaN; f.SetSpeed(0);
                        try { cycle(); var values = f.WearValues(); Require(values[0] == 90 && values[2] == 90 && float.IsNaN(wear.Value), "Invalid host condition partially wore other targets."); }
                        finally { f.ResetSaved(); }
                    });
                    RunDrivetrainMutationChecks(check, item, f, receive, rebind, original);
                    check("host drivetrain wear: replaced canonical saved scalar receives wear without touching retired storage", () =>
                    {
                        receive(3150); f.ResetSaved(); f.SetSpeed(0);
                        var data = f.Fsm.FsmVariables.FindFsmGameObject("db_Gearbox").Value.GetComponent<PlayMakerFSM>();
                        var prior = data.FsmVariables.FloatVariables;
                        var replacement = new FsmFloat { Name = "Wear", UseVariable = true, Value = 70 };
                        try
                        {
                            data.FsmVariables.FloatVariables = new[] { replacement }; cycle();
                            Require(Math.Abs(replacement.Value - 69.9f) < .00001f && prior[0].Value == 90, "Saved scalar replacement reused old storage.");
                        }
                        finally { data.FsmVariables.FloatVariables = prior; }
                        cycle(); Require(f.WearValues()[1] < 90, "Restored scalar did not resume native wear."); original();
                    });
                    check("host drivetrain wear: body replacement retires old hooks and binds the new host mounts", () =>
                    {
                        receive(3150); var prior = Get(item, "DrivetrainWear");
                        using (var next = new DifferentialFixture(true))
                        try
                        {
                            Set(item, "Body", next.Body); receive(3150); var binding = Get(item, "DrivetrainWear");
                            Require(binding != null && !ReferenceEquals(binding, prior) && ReferenceEquals(Get(binding, "Body"), next.Body), "Body replacement retained old binding.");
                            next.SetSpeed(0); next.ResetSaved(); next.CycleWear(); Require(next.WearValues()[2] < 90, "New body did not receive host wear.");
                            f.SetSpeed(0); f.ResetSaved(); f.CycleWear(); f.Unchanged();
                        }
                        finally { Set(item, "Body", f.Body); rebind(); }
                        cycle(); Require(f.WearValues()[2] < 90, "Original body did not rebind."); original();
                    });
                    check("host drivetrain wear: native helper finalizer restores input even on failure", () =>
                    {
                        receive(3150); var read = NativeBagPartChecks.State(f.Fsm, "Wear").Actions[0]; var hooks = Vehicles.GetNestedType("DrivetrainHooks", Static);
                        object?[] args = { read, null }; hooks.GetMethod("BeforeRead", Static).Invoke(null, args);
                        Require(args[1] != null && !ReferenceEquals(Get(read, "float1"), f.Output), "Scoped native input not installed.");
                        var error = new InvalidOperationException("fixture helper failure"); var returned = hooks.GetMethod("AfterRead", Static).Invoke(null, new[] { error, args[1] });
                        Require(ReferenceEquals(returned, error), "Native exception was replaced."); original();
                    });
                    check("host drivetrain wear: stream teardown removes bindings and native local wear remains", () =>
                    {
                        Call(vehicles, "ClearVehicleStateStreams"); Require(Get(item, "DrivetrainWear") == null && (float)Get(item, "NextDrivetrainWearProbeAt") == 0, "Wear binding survived teardown.");
                        f.SetSpeed(3150); f.ResetSaved(); cycle(); Require(f.WearValues()[2] < 90, "Teardown left native wear disabled."); original();
                    });
                }
            }
            finally { CallStatic("ClearDrivetrainWear", item); foreach (string name in fields) Set(item, name, saved[name]); protect(before); reset(); }
        }

        private static void RunDrivetrainMutationChecks(Action<string, Action> check, object item, DifferentialFixture f,
            Action<float> receive, Action rebind, Action original)
        {
            var sample = NativeBagPartChecks.State(f.Fsm, "State 1"); var wear = NativeBagPartChecks.State(f.Fsm, "Wear");
            Action<Action, Action> mutation = (change, restore) =>
            {
                receive(3150); f.ResetSaved(); f.SetSpeed(0); change();
                try { f.CycleWear(); Require(!f.Fsm.enabled && !f.Fsm.Fsm.RestartOnEnable, "Changed graph was not paused before entry."); f.Unchanged(); }
                finally { restore(); }
                rebind(); Require(f.Fsm.enabled && (bool)Get(Get(item, "DrivetrainWear"), "Ready"), "Repaired graph did not resume."); original();
            };
            foreach (string field in new[] { "float1", "storeResult", "operation", "everyFrame" })
                check("host drivetrain wear: changed division " + field + " pauses and recovers", () =>
                {
                    var action = wear.Actions[0]; var prior = Get(action, field);
                    mutation(() => Set(action, field, field == "operation" ? Enum.ToObject(prior.GetType(), 0) : field == "everyFrame" ? (object)true : new FsmFloat(1)), () => Set(action, field, prior));
                });
            for (int i = 0; i < 3; i++)
            {
                int index = i; var writer = wear.Actions[i * 2 + 1];
                check("host drivetrain wear: changed target " + i + " cannot partially write earlier mounts", () =>
                {
                    var prior = Get(writer, "variableName"); mutation(() => Set(writer, "variableName", new FsmString { Value = "OtherSavedScalar" }), () => Set(writer, "variableName", prior));
                });
                check("host drivetrain wear: stale target cache " + i + " cannot write another mount", () =>
                {
                    receive(3150); f.CycleWear(); var prior = Get(writer, "fsm");
                    var other = f.Fsm.FsmVariables.FindFsmGameObject(index == 0 ? "db_Gearbox" : "db_Driveshaft").Value.GetComponent<PlayMakerFSM>();
                    mutation(() => Set(writer, "fsm", other), () => Set(writer, "fsm", prior));
                });
            }
            check("host drivetrain wear: changed native wait cannot accelerate saved wear", () =>
            {
                var wait = NativeBagPartChecks.State(f.Fsm, "State 2").Actions[0]; var prior = Get(wait, "time");
                mutation(() => Set(wait, "time", new FsmFloat(.1f)), () => Set(wait, "time", prior));
            });
            check("host drivetrain wear: global output alias is rejected before native arithmetic", () =>
            {
                var globals = FsmVariables.GlobalVariables.FloatVariables; var next = new List<FsmFloat>(globals) { f.Output };
                mutation(() => FsmVariables.GlobalVariables.FloatVariables = next.ToArray(), () => FsmVariables.GlobalVariables.FloatVariables = globals);
            });
            check("host drivetrain wear: moved canonical mount pauses and restoration recovers", () =>
            {
                var part = f.Fsm.FsmVariables.FindFsmGameObject("db_Gearbox").Value; string name = part.name;
                mutation(() => part.name = "moved mount", () => part.name = name);
            });
            check("host drivetrain wear: missing target pauses while unrelated graphs remain enabled", () =>
            {
                var target = f.Fsm.FsmVariables.FindFsmGameObject("db_RearAxle"); var prior = target.Value;
                mutation(() => target.Value = null, () => target.Value = prior);
                Require(f.Read.Enabled && sample.Actions[1].Enabled, "Quarantine disabled unrelated actions permanently.");
            });
        }
    }
}
