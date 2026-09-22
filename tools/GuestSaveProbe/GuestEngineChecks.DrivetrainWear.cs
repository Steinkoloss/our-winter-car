using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineChecks
    {
        private static readonly string[] DrivetrainParts = { "Driveshaft", "Gearbox", "RearAxle" };

        private static Writer DrivetrainWriter(List<Writer> writers)
        {
            foreach (var writer in writers)
                if ((string)Get(writer.Rule, "Path") == "CORRIS/Simulation/Systems/Drivetrain" && writer.Fsm.FsmName == "Wear") return writer;
            throw new InvalidOperationException("Missing periodic drivetrain wear protection.");
        }

        private static List<FsmFloat> DrivetrainSaved(PlayMakerFSM fsm)
        {
            var result = new List<FsmFloat>();
            foreach (string part in DrivetrainParts)
                result.Add(Data(fsm.FsmVariables.FindFsmGameObject("db_" + part).Value).FsmVariables.FindFsmFloat("Wear"));
            return result;
        }

        private static void RunDrivetrainWritesSolo(Action<string, Action> check, List<Writer> writers)
        {
            var writer = DrivetrainWriter(writers); var fsm = writer.Fsm; var saved = DrivetrainSaved(fsm);
            // Only the component property read is omitted. Supply its signed result;
            // native abs, DRIVE threshold, three divisions/writes and Wait still run.
            foreach (float speed in new[] { 0f, 1f, 3150f, -3150f })
                check("drivetrain writes: native solo cycle at differential speed " + speed, () =>
                {
                    foreach (var scalar in saved) scalar.Value = 90;
                    fsm.FsmVariables.FindFsmFloat("DiffSpeed").Value = speed;
                    NativeBagPartChecks.Fire(fsm, "State 1");
                    for (int i = 0; i < saved.Count; i++)
                    {
                        float rate = fsm.FsmVariables.FindFsmFloat("Rate" + DrivetrainParts[i]).Value;
                        float expected = 90 - (Math.Abs(speed) > 1 ? Math.Abs(speed) / rate : 0);
                        Require(Math.Abs(saved[i].Value - expected) < .00001f, "Native wear disagrees for " + DrivetrainParts[i]);
                    }
                    Require(fsm.ActiveStateName == "State 2" && writer.Selected.TrueForAll(a => a.Enabled), "Native wear did not reach its wait.");
                    var wait = NativeBagPartChecks.State(fsm, "State 2").Actions[0];
                    Require(wait.GetType().Name == "Wait" && ((FsmFloat)Get(wait, "time")).Value == 2 && (bool)Get(wait, "realTime"), "Native wear cadence changed.");
                });
            foreach (var scalar in saved) scalar.Value = 90;
        }

        private static void RunDrivetrainWritesProtected(Action<string, Action> check, List<Writer> writers, Func<bool> prepare)
        {
            var writer = DrivetrainWriter(writers); var fsm = writer.Fsm; var saved = DrivetrainSaved(fsm);
            var state = NativeBagPartChecks.State(fsm, "Wear");
            foreach (float speed in new[] { 3150f, -3150f })
                check("drivetrain writes: guest cycle preserves all three saved mounts at speed " + speed, () =>
                {
                    var before = Snapshot(saved);
                    for (int i = 0; i < 3; i++)
                    {
                        fsm.FsmVariables.FindFsmFloat("DiffSpeed").Value = speed;
                        NativeBagPartChecks.Fire(fsm, "State 1"); Tick(fsm);
                        Require(Same(saved, before) && fsm.enabled && fsm.ActiveStateName == "State 2", "Guest native wear or wait changed.");
                        Require(Math.Abs(fsm.FsmVariables.FindFsmFloat("Rate").Value - Math.Abs(speed) / 22800f) < .000001f,
                            "Protecting saved writes suppressed native rate calculation.");
                        foreach (var write in writer.Selected)
                            Require(!write.Enabled && !state.ActiveActions.Contains(write), "A saved mount writer remained active.");
                    }
                });
            for (int part = 0; part < 3; part++)
            {
                int index = part * 2 + 1; string name = DrivetrainParts[part]; var write = state.Actions[index];
                check("drivetrain writes: reenabled " + name + " cannot write on entry", () =>
                {
                    var before = Snapshot(saved); NativeBagPartChecks.Fire(fsm, "Probe idle"); write.Enabled = true;
                    NativeBagPartChecks.Fire(fsm, "Wear"); Tick(fsm);
                    Require(Same(saved, before) && !write.Enabled && !state.ActiveActions.Contains(write) && fsm.enabled,
                        "Reenabled native drivetrain write escaped entry protection.");
                });
                foreach (string field in new[] { "variableName", "fsmName", "gameObject" })
                    check("drivetrain writes: changed " + name + " " + field + " pauses before any saved write and recovers", () =>
                    {
                        var before = Snapshot(saved); NativeBagPartChecks.Fire(fsm, "Probe idle");
                        var raw = (Dictionary<string, object>)((List<object>)FindState(writer.Row, "Wear")["actions"])[index];
                        var changed = NativeAction(raw, fsm);
                        if (field == "gameObject") Set(changed, field, new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner });
                        else Set(changed, field, new FsmString { Value = "Changed saved destination" });
                        state.Actions[index] = changed; changed.Init(state);
                        try
                        {
                            NativeBagPartChecks.Fire(fsm, "Wear");
                            Require(!fsm.enabled && !fsm.Fsm.RestartOnEnable && Same(saved, before), "Changed writer was admitted.");
                        }
                        finally { state.Actions[index] = write; }
                        Require(prepare() && fsm.enabled && !write.Enabled && Same(saved, before), "Repair replayed wear or failed to recover.");
                    });
            }
            check("drivetrain writes: disconnect retains protection for the periodic loop", () =>
            {
                var session = SessionManager.Instance; var property = typeof(SessionManager).GetProperty("State", Members);
                object previous = property.GetValue(session, null); var before = Snapshot(saved);
                try
                {
                    property.GetSetMethod(true).Invoke(session, new object[] { SessionState.Idle });
                    Require(prepare(), "Disconnected drivetrain guard not ready.");
                    fsm.FsmVariables.FindFsmFloat("DiffSpeed").Value = 3150;
                    NativeBagPartChecks.Fire(fsm, "State 1"); Tick(fsm);
                    Require(Same(saved, before) && fsm.ActiveStateName == "State 2", "Disconnect resumed native saved wear.");
                }
                finally { property.GetSetMethod(true).Invoke(session, new[] { previous }); }
            });
            RunLateDrivetrainWrites(check, writer, saved, prepare);
        }

        private static void RunLateDrivetrainWrites(Action<string, Action> check, Writer source, List<FsmFloat> saved, Func<bool> prepare)
        {
            var original = source.Fsm.gameObject; string name = original.name;
            var obj = Child(original.transform.parent.gameObject, "unrelated drivetrain fixture"); obj.SetActive(false);
            var fsm = NativeBagPartChecks.MakeFsm(obj, source.Row);
            foreach (var reference in fsm.FsmVariables.GameObjectVariables)
                reference.Value = source.Fsm.FsmVariables.FindFsmGameObject(reference.Name).Value;
            var writer = new Writer { Rule = source.Rule, Row = source.Row, Fsm = fsm };
            try
            {
                obj.SetActive(true); LoadWriterActions(writer); NativeBagPartChecks.Start(fsm);
                check("drivetrain writes: unrelated native graph retains saved wear in a protected session", () =>
                {
                    var before = Snapshot(saved); fsm.FsmVariables.FindFsmFloat("DiffSpeed").Value = 3150;
                    NativeBagPartChecks.Fire(fsm, "State 1");
                    for (int i = 0; i < saved.Count; i++) Require(saved[i].Value < before[i], "Exact path guard suppressed an unrelated writer.");
                });
                check("drivetrain writes: late canonical graph is protected before the next scan", () =>
                {
                    original.name = name + " earlier fixture"; obj.name = name; var before = Snapshot(saved);
                    fsm.FsmVariables.FindFsmFloat("DiffSpeed").Value = 3150; NativeBagPartChecks.Fire(fsm, "State 1"); Tick(fsm);
                    Require(Same(saved, before) && fsm.enabled && fsm.ActiveStateName == "State 2" && writer.Selected.TrueForAll(a => !a.Enabled),
                        "Late drivetrain entered native saved writes before scanning.");
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(obj); original.name = name; Require(prepare(), "Original drivetrain fixture did not recover."); }
        }
    }
}
