using System;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WinterMP.Core.Sync
{
    internal sealed partial class AtfFillerBinding
    {
        private static T Field<T>(object action, string name)
        {
            var field = action.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
            if (field == null || field.GetValue(action) is not T value)
                throw new InvalidOperationException("Native ATF field changed: " + name);
            return value;
        }

        private static PlayMakerFSM Find(Transform? root, string path, string name)
        {
            Transform? found = root == null ? null : root.Find(path.Substring(ScenePath.Of(root).Length + 1));
            if (found != null) return FsmOn(found.gameObject, name);
            if (root != null) throw new InvalidOperationException("Native ATF object is unavailable: " + path);
            PlayMakerFSM? match = null;
            foreach (var obj in ScenePath.ScanFsms())
                if (obj is PlayMakerFSM fsm && fsm.FsmName == name && ScenePath.Of(fsm.transform) == path)
                { if (match != null) throw new InvalidOperationException("Ambiguous native ATF gauge."); match = fsm; }
            return match ?? throw new InvalidOperationException("Native ATF gauge is unavailable.");
        }

        private static PlayMakerFSM FsmOn(GameObject obj, string name)
        {
            PlayMakerFSM? found = null;
            if (obj != null)
                foreach (var fsm in obj.GetComponents<PlayMakerFSM>())
                    if (fsm.FsmName == name)
                    { if (found != null) throw new InvalidOperationException("Ambiguous native ATF FSM."); found = fsm; }
            return found ?? throw new InvalidOperationException("Missing native ATF FSM " + name);
        }

        private static FsmState State(PlayMakerFSM fsm, string name)
        {
            var state = FsmHook.FindState(fsm, name);
            if (!fsm.Fsm.Initialized || state == null || !state.IsInitialized)
                throw new InvalidOperationException("Native ATF state is not initialized: " + fsm.FsmName + "::" + name);
            return state;
        }

        private static FsmStateAction ActionAt(FsmState state, int index, string type)
        {
            if (index < 0 || index >= state.Actions.Length) throw new InvalidOperationException("Missing native ATF action.");
            var action = state.Actions[index];
            string fullName = type == "MasterAudioPlaySound" ? type : "HutongGames.PlayMaker.Actions." + type;
            if (action == null || !action.Enabled || action.GetType().FullName != fullName
                || action.GetType().Assembly.GetName().Name != "Assembly-CSharp")
                throw new InvalidOperationException("Native ATF action changed: " + state.Name + "[" + index + "]");
            return action;
        }

        private static FsmFloat Float(PlayMakerFSM fsm, string name)
        {
            FsmFloat? found = null;
            foreach (var value in fsm.FsmVariables.FloatVariables)
                if (value.Name == name)
                { if (found != null) throw new InvalidOperationException("Ambiguous native ATF scalar."); found = value; }
            if (found == null || found.IsNone || !found.UseVariable || !Finite(found.Value))
                throw new InvalidOperationException("Invalid native ATF scalar: " + name);
            foreach (var value in FsmVariables.GlobalVariables.FloatVariables)
                if (ReferenceEquals(value, found)) throw new InvalidOperationException("Native ATF scalar aliases a global.");
            return found;
        }

        private static FsmGameObject Object(PlayMakerFSM fsm, string name)
        {
            FsmGameObject? found = null;
            foreach (var value in fsm.FsmVariables.GameObjectVariables)
                if (value.Name == name)
                { if (found != null) throw new InvalidOperationException("Ambiguous native ATF object variable."); found = value; }
            if (found == null || found.IsNone || !found.UseVariable)
                throw new InvalidOperationException("Missing native ATF object variable: " + name);
            return found;
        }

        private static void Variable(FsmStateAction action, string field, FsmFloat value)
        { if (!ReferenceEquals(Field<FsmFloat>(action, field), value)) throw new InvalidOperationException("Native ATF float reference changed."); }
        private static void Literal(FsmStateAction action, string field, float value)
        {
            var actual = Field<FsmFloat>(action, field);
            if (actual.IsNone || actual.UseVariable || actual.Value != value) throw new InvalidOperationException("Native ATF float literal changed.");
        }
        private static void LiteralBool(FsmStateAction action, string field, bool value)
        {
            var actual = Field<FsmBool>(action, field);
            if (actual.IsNone || actual.UseVariable || actual.Value != value) throw new InvalidOperationException("Native ATF bool literal changed.");
        }
        private static void LiteralString(FsmStateAction action, string field, string value)
        {
            var actual = Field<FsmString>(action, field);
            if (actual.IsNone || actual.UseVariable || actual.Value != value) throw new InvalidOperationException("Native ATF string literal changed.");
        }
        private static void Once(FsmStateAction action)
        { if (Field<bool>(action, "everyFrame")) throw new InvalidOperationException("Native ATF action timing changed."); }
        private static void Target(FsmStateAction action, PlayMakerFSM fsm, string variable)
        {
            var target = Field<FsmOwnerDefault>(action, "gameObject");
            if (target.OwnerOption != OwnerDefaultOption.SpecifyGameObject || !ReferenceEquals(target.GameObject, Object(fsm, variable)))
                throw new InvalidOperationException("Native ATF object target changed.");
        }
        private static void External(FsmStateAction action, PlayMakerFSM fsm, string target, string data, string scalar, bool everyFrame)
        {
            Target(action, fsm, target); LiteralString(action, "fsmName", data); LiteralString(action, "variableName", scalar);
            if (Field<bool>(action, "everyFrame") != everyFrame) throw new InvalidOperationException("Native ATF transfer schedule changed.");
        }
        private static void Event(FsmStateAction action, string field, string name)
        { if (Field<FsmEvent>(action, field).Name != name) throw new InvalidOperationException("Native ATF action event changed."); }
        private static void Transition(FsmState state, string name, string target)
        {
            int count = 0;
            foreach (var transition in state.Transitions)
                if (transition.EventName == name)
                { if (transition.ToState != target) throw new InvalidOperationException("Native ATF transition changed."); count++; }
            if (count != 1) throw new InvalidOperationException("Missing native ATF transition.");
        }

        private void ValidateCap()
        {
            if (_up.Actions != _upActions || _down.Actions != _downActions || _upActions.Length != 4 || _downActions.Length != 4
                || _step.Value != 33f || !Finite(_rotation.Value) || _rotation.Value < 1 || _rotation.Value > 359)
                throw new InvalidOperationException("Native ATF cap state changed.");
            foreach (bool down in new[] { false, true })
            {
                var state = down ? _down : _up;
                var step = ActionAt(state, 0, down ? "FloatSubtract" : "FloatAdd");
                Variable(step, "floatVariable", _rotation); Variable(step, down ? "subtract" : "add", _step);
                Once(step); if (Field<bool>(step, "perSecond")) throw new InvalidOperationException("Native ATF cap step changed.");
                var clamp = ActionAt(state, 1, "FloatClamp");
                Variable(clamp, "floatVariable", _rotation); Literal(clamp, "minValue", 1); Literal(clamp, "maxValue", 359); Once(clamp);
                var render = ActionAt(state, 2, "SetRotation"); Target(render, _cap, _rule["mesh"]);
                Variable(render, "zAngle", _rotation); Once(render);
                if (Convert.ToInt32(Field<object>(render, "space")) != 1 || Field<bool>(render, "lateUpdate")
                    || !Field<FsmFloat>(render, "xAngle").IsNone || !Field<FsmFloat>(render, "yAngle").IsNone)
                    throw new InvalidOperationException("Native ATF cap rotation changed.");
                var compare = ActionAt(state, 3, "FloatCompare"); Variable(compare, "float1", _rotation);
                Literal(compare, "float2", down ? 1 : 2); Literal(compare, "tolerance", 0); Once(compare);
                Event(compare, "equal", down ? "UNTIGHTEN" : "TIGHTEN");
                Event(compare, "lessThan", down ? "UNTIGHTEN" : "FINISHED");
                Event(compare, "greaterThan", down ? "FINISHED" : "TIGHTEN");
                Transition(state, "FINISHED", _rule["wait"]);
                Transition(state, down ? "UNTIGHTEN" : "TIGHTEN", _rule[down ? "open" : "closed"]);
            }
            foreach (bool open in new[] { false, true })
            {
                var state = State(_cap, _rule[open ? "open" : "closed"]);
                if (state.Actions.Length != 3) throw new InvalidOperationException("Native ATF cap presentation changed.");
                ActionAt(state, 0, "MasterAudioPlaySound");
                Activate(ActionAt(state, 1, "ActivateGameObject"), _cap, _rule["mesh"], !open, false, false);
                Activate(ActionAt(state, 2, "ActivateGameObject"), _cap, _rule["capTrigger"], open, false, false);
                Transition(state, "FINISHED", _rule["wait"]);
            }
        }

        private static void Activate(FsmStateAction action, PlayMakerFSM fsm, string variable, bool activate, bool recursive, bool reset)
        {
            Target(action, fsm, variable); LiteralBool(action, "activate", activate); LiteralBool(action, "recursive", recursive); Once(action);
            if (Field<bool>(action, "resetOnExit") != reset) throw new InvalidOperationException("Native ATF activation cleanup changed.");
        }

        private GameObject ValidateFill()
        {
            var pour = State(_fill, _rule["pour"]); var idle = State(_fill, _rule["fillIdle"]);
            var check = State(_fill, _rule["fillCheck"]); var stop = State(_fill, _rule["stop"]);
            if (pour.Actions.Length != 9 || idle.Actions.Length != 2 || check.Actions.Length != 2 || stop.Actions.Length != 1
                || Float(_fill, _rule["fillCapacity"]).Value != 6.3f)
                throw new InvalidOperationException("Native ATF filler graph changed.");
            Activate(ActionAt(pour, 0, "ActivateGameObject"), _fill, _rule["fillGui"], true, true, true);
            var sound = ActionAt(pour, 1, "ActivateGameObject"); var soundTarget = Field<FsmOwnerDefault>(sound, "gameObject");
            var soundObject = soundTarget.GameObject.Value;
            if (soundTarget.OwnerOption != OwnerDefaultOption.SpecifyGameObject || soundTarget.GameObject.UseVariable
                || soundObject == null || soundObject.transform.parent != _fill.transform || soundObject.GetComponent<AudioSource>() == null)
                throw new InvalidOperationException("Native ATF sound target changed.");
            LiteralBool(sound, "activate", true); LiteralBool(sound, "recursive", false); Once(sound);
            if (!Field<bool>(sound, "resetOnExit")) throw new InvalidOperationException("Native ATF sound cleanup changed.");
            var subtract = ActionAt(pour, 2, "SubtractFsmFloat");
            External(subtract, _fill, _rule["capTrigger"], _rule["dataFsm"], _rule["bottleFluid"], true);
            Literal(subtract, "subtractValue", .1f);
            var add = ActionAt(pour, 3, "AddFsmFloat");
            External(add, _fill, _rule["fillGearbox"], _rule["dataFsm"], _rule["oil"], true); Literal(add, "addValue", .1f);
            if (!Field<bool>(subtract, "perSecond") || !Field<bool>(add, "perSecond")) throw new InvalidOperationException("Native ATF transfer rate changed.");
            var oil = ActionAt(pour, 4, "GetFsmFloat");
            External(oil, _fill, _rule["fillGearbox"], _rule["dataFsm"], _rule["oil"], true); Variable(oil, "storeValue", _fillOil);
            var pouring = ActionAt(pour, 5, "GetFsmBool");
            External(pouring, _fill, _rule["capTrigger"], _rule["dataFsm"], _rule["bottlePouring"], true);
            if (!ReferenceEquals(Field<FsmBool>(pouring, "storeValue"), _fill.FsmVariables.FindFsmBool(_rule["fillPouring"])))
                throw new InvalidOperationException("Native ATF pouring output changed.");
            var limit = ActionAt(pour, 7, "FloatCompare"); Variable(limit, "float1", _fillOil);
            Variable(limit, "float2", Float(_fill, _rule["fillCapacity"])); Literal(limit, "tolerance", 0);
            Event(limit, "equal", "FINISHED"); Event(limit, "greaterThan", "FINISHED");
            var end = ActionAt(stop, 0, "SetFsmBool");
            External(end, _fill, _rule["capTrigger"], _rule["dataFsm"], _rule["bottlePouring"], false); LiteralBool(end, "setValue", false);
            var name = ActionAt(check, 1, "StringCompare"); LiteralString(name, "compareTo", _rule["bottleTrigger"]);
            Event(name, "equalEvent", "POUR"); Event(name, "notEqualEvent", "FINISHED"); Once(name);
            Transition(check, "POUR", _rule["pour"]); Transition(pour, "FINISHED", _rule["stop"]); Transition(stop, "FINISHED", _rule["fillIdle"]);
            return soundObject;
        }

        private void ValidateGauge()
        {
            var state = State(_gauge, _rule["gaugeState"]);
            if (state.Actions.Length != 4 || ReferenceEquals(_gaugeOil, _gaugeMax) || ReferenceEquals(_gaugeOil, _gaugeScale)
                || ReferenceEquals(_gaugeMax, _gaugeScale)) throw new InvalidOperationException("Native ATF gauge changed.");
            var max = ActionAt(state, 0, "GetFsmFloat");
            External(max, _gauge, _rule["gaugeTarget"], _rule["dataFsm"], _rule["oilMax"], false); Variable(max, "storeValue", _gaugeMax);
            var oil = ActionAt(state, 1, "GetFsmFloat");
            External(oil, _gauge, _rule["gaugeTarget"], _rule["dataFsm"], _rule["oil"], true); Variable(oil, "storeValue", _gaugeOil);
            var divide = ActionAt(state, 2, "FloatOperator");
            Variable(divide, "float1", _gaugeOil); Variable(divide, "float2", _gaugeMax); Variable(divide, "storeResult", _gaugeScale);
            if (Convert.ToInt32(Field<object>(divide, "operation")) != 3 || !Field<bool>(divide, "everyFrame"))
                throw new InvalidOperationException("Native ATF gauge arithmetic changed.");
            var render = ActionAt(state, 3, "SetScale"); Variable(render, "x", _gaugeScale);
            if (Field<FsmOwnerDefault>(render, "gameObject").OwnerOption != OwnerDefaultOption.UseOwner
                || !Field<FsmFloat>(render, "y").IsNone || !Field<FsmFloat>(render, "z").IsNone
                || !Field<bool>(render, "everyFrame") || Field<bool>(render, "lateUpdate"))
                throw new InvalidOperationException("Native ATF gauge rendering changed.");
            foreach (var value in _mount.FsmVariables.FloatVariables)
                if (ReferenceEquals(value, _gaugeOil) || ReferenceEquals(value, _gaugeMax) || ReferenceEquals(value, _gaugeScale) || ReferenceEquals(value, _fillOil))
                    throw new InvalidOperationException("Native ATF projection aliases saved oil.");
        }
    }
}
