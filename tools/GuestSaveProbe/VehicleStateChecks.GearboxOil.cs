using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private static void RunGearboxOilPublicationChecks(Action<string, Action> check, DifferentialFixture f, object vehicles,
            object item, SessionManager session, CaptureTransport transport, Action<bool, byte> mode,
            Func<VehicleDrivetrainWearState> build, Action poll, Func<VehicleDrivetrainWearState> packet)
        {
            var data = f.Fsm.FsmVariables.FindFsmGameObject("db_Gearbox").Value.GetComponent<PlayMakerFSM>();
            var original = data.FsmVariables.FloatVariables;
            var oil = new FsmFloat { Name = "OilLevel", UseVariable = true, Value = 6.3f };
            data.FsmVariables.FloatVariables = new List<FsmFloat>(original) { oil }.ToArray();
            const string prefix = "gearbox oil publication: "; mode(true, 255);
            try
            {
                foreach (float value in new[] { -1f, 0f, .01999f, .02f, 3.15f, 6.3f, 100f })
                    check(prefix + "exact host oil reaches every guest " + value, () =>
                    {
                        oil.Value = value; transport.Packets.Clear(); poll(); var result = packet();
                        Require(result.GearboxOilAvailable && result.GearboxOilLevel == value && result.Flags == 1, "Host oil was lost or clamped.");
                        Require(PacketCodec.Encode(result).Length == 28 && oil.Value == value, "Wrong wire layout or native saved write."); f.Unchanged();
                    });
                foreach (string fault in new[] { "missing", "duplicate", "nonfinite", "not variable", "global alias", "producer alias" })
                    check(prefix + fault + " withdraws only oil and repair recovers", () =>
                    {
                        var saved = data.FsmVariables.FloatVariables; var globals = FsmVariables.GlobalVariables.FloatVariables;
                        var scratch = f.Fsm.FsmVariables.FloatVariables; oil.Value = 6.3f;
                        if (fault == "missing") data.FsmVariables.FloatVariables = original;
                        if (fault == "duplicate") data.FsmVariables.FloatVariables = new List<FsmFloat>(saved) { new FsmFloat { Name = "OilLevel", UseVariable = true, Value = 2 } }.ToArray();
                        if (fault == "nonfinite") oil.Value = float.NaN;
                        if (fault == "not variable") oil.UseVariable = false;
                        if (fault == "global alias") FsmVariables.GlobalVariables.FloatVariables = new List<FsmFloat>(globals) { oil }.ToArray();
                        if (fault == "producer alias") f.Fsm.FsmVariables.FloatVariables = new List<FsmFloat>(scratch) { oil }.ToArray();
                        try { var state = build(); Require(state.Flags == 1 && !state.GearboxOilAvailable && state.GearboxOilLevel == 0, "Unsafe oil did not withdraw independently."); f.Unchanged(); }
                        finally { data.FsmVariables.FloatVariables = saved; FsmVariables.GlobalVariables.FloatVariables = globals; f.Fsm.FsmVariables.FloatVariables = scratch; oil.UseVariable = true; oil.Value = 6.3f; }
                        Require(build().GearboxOilAvailable && build().GearboxOilLevel == 6.3f, "Repaired oil remained unavailable.");
                    });
                foreach (string method in new[] { "BuildJoinVehicleSnapshots", "BuildVehicleResyncMessages", "BuildVehicleStateMessages" })
                    check(prefix + method + " carries oil without acknowledging live refill", () =>
                    {
                        oil.Value -= .1f;
                        object[] args = method == "BuildVehicleStateMessages" ? new object[] { item, (byte)0 } : new object[0];
                        VehicleDrivetrainWearState? result = null;
                        foreach (var message in (IEnumerable)Call(vehicles, method, args)!) if (message is VehicleDrivetrainWearState state) result = state;
                        Require(result != null && result.GearboxOilAvailable && result.GearboxOilLevel == oil.Value, "Snapshot omitted current oil.");
                        transport.Packets.Clear(); poll(); Require(packet().GearboxOilLevel == oil.Value, "Snapshot swallowed pending live refill.");
                    });
                check(prefix + "parked refill survives driver handoff and copied replica storage", () =>
                {
                    oil.Value = 6.3f; var result = build(); mode(false, 1);
                    Require((bool)Call(vehicles, "OnVehicleDrivetrainWearState", result)!, "Host oil receipt failed.");
                    foreach (byte owner in new byte[] { 1, 2, 255 })
                        foreach (bool local in new[] { false, true })
                        {
                            mode(false, owner); Set(item, "LocallyOwned", local);
                            var retained = (VehicleDrivetrainWearState)Call(vehicles, "ReadDrivetrainWearState", Get(item, "Id"))!;
                            Require(retained.GearboxOilLevel == 6.3f && retained.GearboxOilAvailable, "Owner change lost host oil."); retained.GearboxOilLevel = 0;
                        }
                    Set(item, "LocallyOwned", false); mode(true, 255); f.Unchanged(); Require(oil.Value == 6.3f, "Receipt changed saved oil.");
                });
            }
            finally { data.FsmVariables.FloatVariables = original; mode(true, 255); }
        }

        private static void RunGearboxOilConsumerChecks(Action<string, Action> check, DifferentialFixture f,
            PlayMakerFSM transmission, PlayMakerFSM automatic, Func<bool> prepare, Action<PlayMakerFSM, string> fire,
            Action<float, bool> receiveOil, Action<bool, byte> mode, object item)
        {
            const string prefix = "gearbox oil consumers: ";
            var state = NativeBagPartChecks.State(automatic, "Set stall speed"); var priorActions = state.Actions;
            var row = NativeBagPartChecks.Find(f.Rows, "CORRIS/Simulation/Systems/Drivetrain/GearboxAutomatic", "3 speed");
            var stallRow = NativeBagPartChecks.Find(f.Rows, "CORRIS/Simulation/Systems/Drivetrain/GearboxAutomatic", "Stallspeed");
            var stall = NativeBagPartChecks.MakeFsm(automatic.gameObject, stallRow); NativeBagPartChecks.Start(stall);
            foreach (Dictionary<string, object> candidate in (IEnumerable)row["states"])
                if ((string)candidate["name"] == state.Name)
                {
                    var raw = (IList)candidate["actions"]; var actions = new FsmStateAction[raw.Count];
                    for (int i = 0; i < actions.Length; i++)
                    {
                        actions[i] = (FsmStateAction)typeof(NativeBagPartChecks).GetMethod("ReadAction", Static).Invoke(null, new object[] { raw[i], automatic });
                        actions[i].Init(state);
                    }
                    state.Actions = actions;
                }
            var reader = state.Actions[0]; var output = automatic.FsmVariables.FindFsmFloat("Oil");
            var data = automatic.FsmVariables.FindFsmGameObject("db_Gearbox").Value.GetComponent<PlayMakerFSM>();
            var savedOil = data.FsmVariables.FindFsmFloat("OilLevel"); float beforeOil = savedOil.Value;
            try
            {
                foreach (float value in new[] { -1f, 0f, .01999f, .02f, .02001f, 3.15f, 6.29999f, 6.3f, 6.30001f, 100f })
                    check(prefix + "native shift and stall calculations use exact host oil " + value, () =>
                    {
                        receiveOil(value, true); prepare(); Require(automatic.enabled, "Available host oil did not recover automatic gearbox.");
                        var source = Get(reader, "gameObject"); var cachedGo = Get(reader, "goLastFrame"); var cachedFsm = Get(reader, "fsm");
                        reader.OnEnter(); Require(output.Value == value, "Native oil reader lost exact host value.");
                        fire(automatic, "Set stall speed"); float clamp = Mathf.Clamp(value, .02f, 6.3f);
                        // Native actions store each result in a float32 variable;
                        // legacy Mono may retain extra precision in local arithmetic.
                        var shift = new FsmFloat(15120f / clamp); var expectedStall = new FsmFloat(shift.Value / 800f);
                        Require(output.Value == clamp && automatic.FsmVariables.FindFsmFloat("UpShiftRPM").Value == shift.Value
                            && automatic.FsmVariables.FindFsmFloat("StallSpeed").Value == expectedStall.Value,
                            "Native oil arithmetic differs: shift=" + automatic.FsmVariables.FindFsmFloat("UpShiftRPM").Value.ToString("R")
                            + " expected=" + shift.Value.ToString("R") + " stall=" + automatic.FsmVariables.FindFsmFloat("StallSpeed").Value.ToString("R")
                            + " expected=" + expectedStall.Value.ToString("R"));
                        Require(stall.FsmVariables.FindFsmFloat("Stall").Value == expectedStall.Value && stall.FsmVariables.FindFsmFloat("MaxStall").Value == 1,
                            "Native stall output did not reach its local consumer.");
                        Require(Mathf.Abs(automatic.FsmVariables.FindFsmFloat("OilLeakRate").Value - .001f) < .0000001f, "Native host-wear leak calculation did not execute.");
                        Require(ReferenceEquals(source, Get(reader, "gameObject")) && ReferenceEquals(cachedGo, Get(reader, "goLastFrame"))
                            && ReferenceEquals(cachedFsm, Get(reader, "fsm")), "Projection changed native oil source or cache.");
                        fire(automatic, "State 1"); fire(automatic, "State 3"); Require(savedOil.Value == beforeOil, "Native guest oil drain escaped protection."); f.Unchanged();
                    });
                check(prefix + "oil withdrawal pauses only automatic until a valid refill", () =>
                {
                    receiveOil(0, false); Require(!automatic.enabled && transmission.enabled, "Oil withdrawal disabled unrelated wear or left automatic running.");
                    float previous = output.Value; reader.OnEnter(); Require(output.Value == previous && savedOil.Value == beforeOil, "Missing host oil borrowed saved oil.");
                    receiveOil(6.3f, true); prepare(); fire(automatic, "Set stall speed");
                    Require(automatic.enabled && output.Value == 6.3f && savedOil.Value == beforeOil, "Refill did not safely recover automatic gearbox.");
                });
                check(prefix + "driving ownership and parking do not replace host oil", () =>
                {
                    receiveOil(3.15f, true);
                    foreach (byte owner in new byte[] { 1, 2, 255 })
                        foreach (bool local in new[] { false, true })
                        { mode(false, owner); Set(item, "LocallyOwned", local); fire(automatic, "Set stall speed"); Require(output.Value == 3.15f, "Lease replaced host oil."); }
                    Set(item, "LocallyOwned", false);
                });
                foreach (string field in new[] { "variableName", "fsmName", "storeValue", "everyFrame" })
                    check(prefix + "changed oil reader " + field + " pauses and repairs", () =>
                    {
                        object original = Get(reader, field); float before = output.Value;
                        Set(reader, field, field == "everyFrame" ? (object)true : field == "storeValue" ? new FsmFloat(0) : new FsmString { Value = "Other" });
                        try { fire(automatic, "Set stall speed"); Require(!automatic.enabled && savedOil.Value == beforeOil && output.Value == before, "Changed reader escaped protection."); }
                        finally { Set(reader, field, original); prepare(); }
                        Require(automatic.enabled, "Repaired oil reader stayed paused.");
                    });
                foreach (bool global in new[] { false, true })
                    check(prefix + (global ? "global" : "saved") + " output alias is never written", () =>
                    {
                        var target = global ? FsmVariables.GlobalVariables : data.FsmVariables; var original = target.FloatVariables;
                        float before = output.Value; target.FloatVariables = new List<FsmFloat>(original) { output }.ToArray();
                        try { fire(automatic, "Set stall speed"); Require(!automatic.enabled && output.Value == before && savedOil.Value == beforeOil, "Aliased oil output was written."); }
                        finally { target.FloatVariables = original; prepare(); }
                    });
                RunGearboxOilUseChecks(check, f, transmission, automatic, prepare, fire, mode, item);
                RunGearboxWearChecks(check, f, prepare, mode, item);
            }
            finally { state.Actions = priorActions; UnityEngine.Object.DestroyImmediate(stall); receiveOil(6.3f, true); prepare(); }
        }
    }
}
