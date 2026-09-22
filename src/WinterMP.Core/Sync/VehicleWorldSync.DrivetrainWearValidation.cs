using System;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        private static void ValidateDrivetrainWear(NativeDrivetrainWear b)
        {
            var rule = b.Rule; var source = b.Source; var fsm = b.Fsm;
            if (!ReferenceEquals(rule, SyncCatalog.VehicleDrivetrainWear) || source == null || b.Body != b.Item.Body
                || source.Fsm != fsm || rule.RootPath != source.Rule.RootPath || rule.Path != source.Rule.Path || rule.Fsm != source.Rule.Fsm
                || source.Rule.Index != 0 || !fsm.Fsm.Initialized || !fsm.Fsm.Started || rule.Targets.Count != 3)
                throw new InvalidOperationException("Native drivetrain wear identity changed.");
            ValidateDifferentialSpeed(b.Item, source);
            if (fsm.Fsm.GlobalTransitions.Length != 0 || HeatState(fsm, source.Rule.State) != b.Sample
                || HeatState(fsm, source.Rule.WearState) != b.Wear || HeatState(fsm, source.Rule.WaitState) != b.Wait)
                throw new InvalidOperationException("Native drivetrain wear states changed.");
            DrivetrainState(b.Sample, 3, "FINISHED", b.Wait.Name, "DRIVE", b.Wear.Name);
            DrivetrainState(b.Wear, 6, "FINISHED", b.Wait.Name);
            DrivetrainState(b.Wait, 1, "FINISHED", b.Sample.Name);
            DrivetrainAction(b.Sample.Actions[1], "FloatAbs"); DrivetrainLocal(b, b.Sample.Actions[1], "floatVariable", source.Output);
            var compare = b.Sample.Actions[2]; DrivetrainAction(compare, "FloatCompare"); DrivetrainLocal(b, compare, "float1", source.Output);
            if (TemperatureConstant(TemperatureField(compare, "float2")) != 1 || TemperatureConstant(TemperatureField(compare, "tolerance")) != 0
                || DrivetrainEvent(compare, "equal") != "" || DrivetrainEvent(compare, "lessThan") != "" || DrivetrainEvent(compare, "greaterThan") != "DRIVE")
                throw new InvalidOperationException("Native drivetrain threshold changed.");
            var wait = b.Wait.Actions[0];
            if (wait.GetType().FullName != "HutongGames.PlayMaker.Actions.Wait" || wait.GetType().Assembly.GetName().Name != "Assembly-CSharp"
                || TemperatureConstant(TemperatureField(wait, "time")) != 2 || !(bool)TemperatureField(wait, "realTime") || DrivetrainEvent(wait, "finishEvent") != "FINISHED")
                throw new InvalidOperationException("Native drivetrain cadence changed.");
            if (b.Reads.Count != 0 && (b.Reads.Count != 4 || b.Reads[0].Action != compare)) throw new InvalidOperationException("Drivetrain read replaced.");
            for (int i = 0; i < 3; i++)
            {
                var target = rule.Targets[i]; var math = b.Wear.Actions[i * 2]; var write = b.Wear.Actions[i * 2 + 1];
                if (b.Reads.Count != 0 && b.Reads[i + 1].Action != math) throw new InvalidOperationException("Drivetrain calculation replaced.");
                var rate = fsm.FsmVariables.FindFsmFloat(target.Rate);
                DrivetrainAction(math, "FloatOperator"); DrivetrainLocal(b, math, "float1", source.Output);
                DrivetrainLocal(b, math, "float2", rate); DrivetrainLocal(b, math, "storeResult", b.Rate);
                if (TemperatureField(math, "operation").ToString() != "Divide" || rate == null || rate.Value != target.Divisor)
                    throw new InvalidOperationException("Native drivetrain wear rate changed.");
                DrivetrainAction(write, "SubtractFsmFloat"); DrivetrainLocal(b, write, "subtractValue", b.Rate);
                var owner = TemperatureField(write, "gameObject") as FsmOwnerDefault;
                var reference = fsm.FsmVariables.FindFsmGameObject(target.Variable);
                if (owner == null || owner.OwnerOption == OwnerDefaultOption.UseOwner || reference == null || reference.IsNone || !reference.UseVariable
                    || reference.Name != target.Variable || !ReferenceEquals(owner.GameObject, reference) || reference.Value == null
                    || ScenePath.Of(reference.Value.transform) != target.Path || !reference.Value.transform.IsChildOf(b.Body.transform)
                    || !TemperatureLiteral(TemperatureField(write, "fsmName"), "Data") || !TemperatureLiteral(TemperatureField(write, "variableName"), "Wear")
                    || (bool)TemperatureField(write, "perSecond")) throw new InvalidOperationException("Native drivetrain saved target changed.");
                PlayMakerFSM? data = null;
                foreach (var candidate in reference.Value.GetComponents<PlayMakerFSM>())
                    if (candidate.FsmName == "Data") { if (data != null) throw new InvalidOperationException("Ambiguous saved drivetrain data."); data = candidate; }
                var wear = data?.FsmVariables.FindFsmFloat("Wear");
                if (data == null || !data.Fsm.Initialized || !data.Fsm.Started || !data.gameObject.activeInHierarchy || wear == null || wear.IsNone || !wear.UseVariable)
                    throw new InvalidOperationException("Saved drivetrain data not ready.");
                foreach (var global in FsmVariables.GlobalVariables.FloatVariables)
                    if (ReferenceEquals(global, wear)) throw new InvalidOperationException("Saved drivetrain wear aliases a global.");
                foreach (var value in data.FsmVariables.FloatVariables)
                    if (ReferenceEquals(value, source.Output) || ReferenceEquals(value, b.Rate) || ReferenceEquals(value, rate))
                        throw new InvalidOperationException("Drivetrain calculation aliases saved data.");
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var goCache = write.GetType().GetField("goLastFrame", flags); var fsmCache = write.GetType().GetField("fsm", flags);
                if (goCache == null || fsmCache == null || (goCache.GetValue(write) as GameObject == reference.Value && !ReferenceEquals(fsmCache.GetValue(write), data)))
                    throw new InvalidOperationException("Native drivetrain writer cache changed.");
                b.Divisors[i] = rate.Value; b.SavedWear[i] = wear;
            }
            if (ReferenceEquals(b.SavedWear[0], b.SavedWear[1]) || ReferenceEquals(b.SavedWear[0], b.SavedWear[2]) || ReferenceEquals(b.SavedWear[1], b.SavedWear[2]))
                throw new InvalidOperationException("Drivetrain targets alias each other.");
        }

        private static void DrivetrainState(FsmState state, int actions, params string[] transitions)
        {
            if (state.Actions.Length != actions || state.Transitions.Length * 2 != transitions.Length) throw new InvalidOperationException("Native drivetrain state shape changed.");
            foreach (var action in state.Actions) if (!action.Enabled) throw new InvalidOperationException("Native drivetrain action disabled.");
            for (int i = 0; i < transitions.Length; i += 2)
            {
                int matches = 0;
                foreach (var edge in state.Transitions) if (edge.EventName == transitions[i] && edge.ToState == transitions[i + 1]) matches++;
                if (matches != 1) throw new InvalidOperationException("Native drivetrain transition changed.");
            }
        }
        private static string DrivetrainEvent(FsmStateAction action, string name)
        {
            var field = action.GetType().GetField(name) ?? throw new InvalidOperationException("Native drivetrain event field missing.");
            return field.GetValue(action) is FsmEvent value ? value.Name ?? "" : "";
        }
        private static void DrivetrainAction(FsmStateAction action, string name)
        {
            if (action.GetType().FullName != "HutongGames.PlayMaker.Actions." + name || action.GetType().Assembly.GetName().Name != "Assembly-CSharp"
                || (bool)TemperatureField(action, "everyFrame")) throw new InvalidOperationException("Native drivetrain action signature changed.");
        }
        private static void DrivetrainLocal(NativeDrivetrainWear b, FsmStateAction action, string field, FsmFloat value)
        {
            object current = TemperatureField(action, field);
            foreach (var read in b.Reads) if (read.Action == action && read.Field.Name == field && read.Depth > 0 && ReferenceEquals(current, read.Input)) current = read.Original;
            if (value == null || value.IsNone || !value.UseVariable || !ReferenceEquals(value, b.Fsm.FsmVariables.FindFsmFloat(value.Name)) || !ReferenceEquals(current, value))
                throw new InvalidOperationException("Native drivetrain operand changed.");
            foreach (var global in FsmVariables.GlobalVariables.FloatVariables) if (ReferenceEquals(global, value)) throw new InvalidOperationException("Drivetrain scratch aliases a global.");
        }
    }
}
