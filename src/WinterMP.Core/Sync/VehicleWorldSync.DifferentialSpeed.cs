using System;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        internal sealed class NativeDifferentialSpeed
        {
            internal VehicleDifferentialSpeedData Rule = null!;
            internal Rigidbody Body = null!;
            internal PlayMakerFSM Fsm = null!;
            internal FsmState State = null!;
            internal FsmStateAction Action = null!;
            internal FsmObject Target = null!;
            internal FsmFloat Output = null!;
            internal Behaviour Drive = null!;
            internal FieldInfo Member = null!;
        }

        private static void CaptureDifferentialSpeedTelemetry(SyncedItem item, VehicleState state)
        {
            state.DifferentialSpeedAvailable = false; state.DifferentialSpeed = 0;
            if (!item.IsVehicle || item.Body == null) { item.DifferentialSpeedSource = null; return; }
            try
            {
                var rule = SyncCatalog.VehicleDifferentialSpeed;
                var b = item.DifferentialSpeedSource;
                if (b != null && b.Body != item.Body) { item.DifferentialSpeedSource = null; b = null; item.NextDifferentialProbeAt = 0; }
                if (b == null)
                {
                    if (rule == null || item.Path != rule.RootPath || Time.unscaledTime < item.NextDifferentialProbeAt) return;
                    item.NextDifferentialProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
                    b = BindDifferentialSpeed(item, rule);
                }
                ValidateDifferentialSpeed(item, b);
                item.DifferentialSpeedSource = b;
                if (!b.Fsm.enabled || !b.Fsm.gameObject.activeInHierarchy || !b.Fsm.Fsm.Started || !b.Drive.enabled
                    || (b.Fsm.ActiveStateName != b.Rule.State && b.Fsm.ActiveStateName != b.Rule.WearState && b.Fsm.ActiveStateName != b.Rule.WaitState)) return;
                // The native graph samples only every two seconds. Read its validated
                // component field at send time so telemetry never repeats old scratch.
                float speed = (float)b.Member.GetValue(b.Drive);
                if (!FiniteTemperature(speed)) return;
                state.DifferentialSpeedAvailable = true; state.DifferentialSpeed = speed;
            }
            catch (Exception error)
            {
                item.DifferentialSpeedSource = null;
                item.NextDifferentialProbeAt = Time.unscaledTime + SystemsProbeIntervalSeconds;
                if (Time.unscaledTime < item.NextDifferentialErrorAt) return;
                item.NextDifferentialErrorAt = Time.unscaledTime + 10f;
                try { SyncEventLog.Record("vehicle-differential-speed-unavailable", item.Path + ": " + error.Message); } catch { }
            }
        }

        private static NativeDifferentialSpeed BindDifferentialSpeed(SyncedItem item, VehicleDifferentialSpeedData rule)
        {
            if (item.Body == null) throw new InvalidOperationException("Missing differential speed body.");
            var fsm = FindTemperatureFsm(item.Body.GetComponentsInChildren<PlayMakerFSM>(true), rule.Path, rule.Fsm);
            var sample = HeatState(fsm, rule.State);
            if (sample.Actions.Length <= rule.Index) throw new InvalidOperationException("Missing differential speed read.");
            var target = fsm.FsmVariables.FindFsmObject(rule.ObjectVariable);
            var drive = target?.Value as Behaviour ?? throw new InvalidOperationException("Missing native drivetrain.");
            return new NativeDifferentialSpeed { Rule = rule, Body = item.Body, Fsm = fsm, State = sample,
                Action = sample.Actions[rule.Index], Target = target!, Drive = drive,
                Output = fsm.FsmVariables.FindFsmFloat(rule.Output),
                Member = drive.GetType().GetField(rule.Member, BindingFlags.Instance | BindingFlags.Public) };
        }

        private static void ValidateDifferentialSpeed(SyncedItem item, NativeDifferentialSpeed b)
        {
            var rule = b.Rule; var fsm = b.Fsm; var type = b.Drive.GetType();
            if (!ReferenceEquals(rule, SyncCatalog.VehicleDifferentialSpeed) || item.Body == null || b.Body != item.Body
                || item.Path != rule.RootPath || ScenePath.Of(item.Body.transform) != rule.RootPath
                || fsm == null || !fsm.Fsm.Initialized || fsm.FsmName != rule.Fsm || ScenePath.Of(fsm.transform) != rule.Path
                || FindTemperatureFsm(item.Body.GetComponentsInChildren<PlayMakerFSM>(true), rule.Path, rule.Fsm) != fsm
                || b.Drive == null || b.Drive.gameObject != item.Body.gameObject || type.FullName != rule.ComponentType
                || type.Assembly.GetName().Name != "Assembly-CSharp" || item.Body.GetComponents(type).Length != 1
                || b.Target == null || !ReferenceEquals(b.Target, fsm.FsmVariables.FindFsmObject(rule.ObjectVariable))
                || !b.Target.UseVariable || b.Target.Name != rule.ObjectVariable || b.Target.Value != b.Drive
                || b.Target.TypeName != rule.ComponentType || b.Target.ObjectType != type
                || b.Member == null || b.Member.FieldType != typeof(float) || b.Member.Name != rule.Member
                || b.Output == null || b.Output.IsNone || !b.Output.UseVariable || b.Output.Name != rule.Output
                || !ReferenceEquals(b.Output, fsm.FsmVariables.FindFsmFloat(rule.Output)))
                throw new InvalidOperationException("Native differential speed source changed.");
            foreach (var global in FsmVariables.GlobalVariables.FloatVariables)
                if (ReferenceEquals(global, b.Output)) throw new InvalidOperationException("Differential output aliases a global.");
            if (HeatState(fsm, rule.State) != b.State || b.State.Actions.Length <= rule.Index
                || !ReferenceEquals(b.State.Actions[rule.Index], b.Action) || !b.Action.Enabled)
                throw new InvalidOperationException("Differential speed action changed.");
            if (b.Action.GetType().FullName != "HutongGames.PlayMaker.Actions.GetProperty"
                || b.Action.GetType().Assembly.GetName().Name != "Assembly-CSharp")
                throw new InvalidOperationException("Native differential speed action type changed.");
            var property = TemperatureField(b.Action, "targetProperty") as FsmProperty;
            if ((bool)TemperatureField(b.Action, "everyFrame") || property == null || property.setProperty
                || property.PropertyName != rule.Member || property.TargetTypeName != rule.ComponentType || property.TargetType != type
                || !ReferenceEquals(property.TargetObject, b.Target) || !ReferenceEquals(property.FloatParameter, b.Output))
                throw new InvalidOperationException("Native differential speed property changed.");
        }
    }
}
