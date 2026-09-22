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
        private const BindingFlags HandoffFields = BindingFlags.Instance | BindingFlags.Public;

        private static VehicleEngineHandoffSource? EngineHandoffRule(SyncedItem item)
        {
            var profile = SyncCatalog.VehicleEngineHandoff;
            if (profile == null || item.Body == null) return null;
            foreach (var rule in profile.Sources)
                if (rule.RootPath == item.Path && ScenePath.Of(item.Body.transform) == rule.RootPath) return rule;
            return null;
        }

        internal static bool UsesEngineHandoff(PlayMakerFSM fsm)
        {
            var profile = SyncCatalog.VehicleEngineHandoff;
            if (profile == null) return false;
            string path = ScenePath.Of(fsm.transform);
            foreach (var rule in profile.Sources)
                if ((fsm.FsmName == "Starter" && path == rule.StarterPath)
                    || (fsm.FsmName == "Use" && path == rule.IgnitionPath)) return true;
            return false;
        }

        private static NativeTemperatureBinding? HandoffTemperatureBinding(SyncedItem item)
        {
            EnsureTemperatureProbe(item);
            var binding = item.NativeTemperature;
            return binding != null && !binding.Rule.HostAuthoritative && binding.Source.enabled
                && binding.Source.gameObject.activeInHierarchy && binding.Source.Fsm.Started ? binding : null;
        }

        private static void CaptureHandoffTemperature(SyncedItem item, VehicleState state)
        {
            if (EngineHandoffRule(item) == null) return;
            var binding = HandoffTemperatureBinding(item);
            if (binding == null) return;
            state.HandoffTemperature = binding.Celsius.Value;
            state.HandoffTemperatureAvailable = true;
            if (!state.ValidHandoffTemperature)
            { state.HandoffTemperatureAvailable = false; state.HandoffTemperature = 0; }
        }

        // Only the previous accepted simulator can supply a one-time initial state.
        // Observers keep receiving presentation; their native engine stays stopped.
        internal void RestoreClaimedEngine(SyncedItem item, VehicleState? state)
        {
            var rule = EngineHandoffRule(item);
            if (rule == null || state == null || !item.LocallyOwned || !_items.IsLocalPlayerDriving(item)) return;
            bool applying = _bridge.ApplyingRemote;
            try
            {
                var binding = new EngineHandoffBinding(item, rule);
                var temperature = HandoffTemperatureBinding(item);
                if (state.EngineOn && (!state.HandoffTemperatureAvailable || temperature == null))
                    throw new InvalidOperationException("Native engine temperature is unavailable.");
                if (state.Gear >= binding.GearCount || (state.EngineOn && (state.Rpm <= EngineRunningRevs || state.Rpm > binding.MaxRpm)))
                    throw new InvalidOperationException("Engine handoff exceeds native RPM or gear limits.");
                _bridge.ApplyingRemote = true;
                binding.Stop();
                if (state.HandoffTemperatureAvailable && temperature != null)
                    temperature.Celsius.Value = state.HandoffTemperature;
                if (state.AccOn || state.EngineOn) binding.AccOn();
                else binding.IgnitionOff();
                if (state.EngineOn) binding.Resume(state.Rpm, state.Gear);
                item.NextVehicleStateAt = Time.unscaledTime + .5f;
                item.LastSentIgnitionActive = state.AccOn || state.EngineOn;
                item.AcceptedVehicleState = null;
                SyncEventLog.Record("engine-handoff", item.Path + " from " + state.OwnerPlayerId + " rpm=" + state.Rpm);
            }
            catch (Exception error)
            {
                WinterMPPlugin.Log.LogWarning("Vehicle engine handoff unavailable for " + item.Path + ": " + error.Message);
                SyncEventLog.Record("engine-handoff-failed", item.Path + ": " + error.Message);
            }
            finally { _bridge.ApplyingRemote = applying; }
        }

        internal void StopRelinquishedEngine(SyncedItem item)
        {
            var rule = EngineHandoffRule(item);
            if (rule == null) return;
            bool applying = _bridge.ApplyingRemote;
            try
            {
                var binding = new EngineHandoffBinding(item, rule);
                _bridge.ApplyingRemote = true; binding.Stop();
                SyncEventLog.Record("engine-relinquished", item.Path);
            }
            catch (Exception error) { WinterMPPlugin.Log.LogWarning("Vehicle engine release failed for " + item.Path + ": " + error.Message); }
            finally { _bridge.ApplyingRemote = applying; }
        }

        private static byte? ReadHandoffGear(SyncedItem item)
        {
            if (EngineHandoffRule(item) == null || item.Body == null) return null;
            var drivetrain = item.Body.GetComponent("Drivetrain");
            var field = drivetrain?.GetType().GetField("gear", HandoffFields);
            return field?.FieldType == typeof(int) ? (byte?)Mathf.Clamp((int)field.GetValue(drivetrain), 0, 255) : null;
        }

        private static float? ReadHandoffRpm(SyncedItem item)
        {
            if (EngineHandoffRule(item) == null) return null;
            var drivetrain = item.Body.GetComponent("Drivetrain") as Behaviour;
            if (drivetrain == null || !drivetrain.enabled || !drivetrain.gameObject.activeInHierarchy
                || drivetrain.GetType().FullName != "Drivetrain"
                || drivetrain.GetType().Assembly.GetName().Name != "Assembly-CSharp") return 0;
            var field = drivetrain.GetType().GetField("rpm", HandoffFields);
            if (field?.FieldType != typeof(float)) return 0;
            float rpm = (float)field.GetValue(drivetrain);
            return float.IsNaN(rpm) || float.IsInfinity(rpm) || rpm < 0 ? 0 : rpm;
        }

        private sealed class EngineHandoffBinding
        {
            private readonly PlayMakerFSM _starter, _ignition;
            private readonly FsmState _context;
            private readonly FsmStateAction[] _resume;
            private readonly FsmStateAction _enable, _rpmSetter;
            private readonly FsmObject _drivetrain;
            private readonly FieldInfo _angularVelocity;
            private readonly float _rpmToAngular;
            private readonly FsmBool _acc, _starting, _shutOff;
            internal readonly int GearCount;
            internal readonly float MaxRpm;

            internal EngineHandoffBinding(SyncedItem item, VehicleEngineHandoffSource rule)
            {
                _starter = Find(item, rule.StarterPath, "Starter"); _ignition = Find(item, rule.IgnitionPath, "Use", allowDisabled: true);
                _context = new FsmState(_starter.Fsm) { Name = "WinterMP engine handoff actions" };
                _acc = Bool("ACC"); _starting = Bool("Starting"); _shutOff = Bool("ShutOff");
                _drivetrain = _starter.FsmVariables.FindFsmObject("CarDrivetrain");
                var component = _drivetrain?.Value as Component;
                if (component == null || component.gameObject != item.Body.gameObject || component.GetType().FullName != "Drivetrain"
                    || component.GetType().Assembly.GetName().Name != "Assembly-CSharp") throw new InvalidOperationException("Native drivetrain identity changed.");
                _angularVelocity = component.GetType().GetField("engineAngularVelo", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                _rpmToAngular = Field<float>(component, "RPM2angularVelo");
                if (_angularVelocity == null || _angularVelocity.FieldType != typeof(float) || _angularVelocity.IsInitOnly
                    || float.IsNaN(_rpmToAngular) || Math.Abs(_rpmToAngular - (float)(Math.PI / 30)) > .00001f)
                    throw new InvalidOperationException("Native engine integration state changed.");
                GearCount = Field<float[]>(component, "gearRatios").Length; MaxRpm = Field<float>(component, "maxRPM");
                if (GearCount < 2 || GearCount > 16 || float.IsNaN(MaxRpm) || MaxRpm <= EngineRunningRevs || MaxRpm > 20000)
                    throw new InvalidOperationException("Native drivetrain limits changed.");
                var start = FsmHook.FindState(_starter, "Start engine") ?? throw new InvalidOperationException("Native start state missing.");
                if (FsmHook.NativeAction(start, 11) != null) throw new InvalidOperationException("Native start layout changed.");
                _resume = new[] { Action(start, 0, "SetFsmFloat"), Action(start, 3, "ActivateGameObject"),
                    Action(start, 4, "SetProperty"), Action(start, 5, "EnableBehaviour"), Action(start, 7, "SetFsmFloat"),
                    Action(start, 8, "SetBoolValue"), Action(start, 9, "ActivateGameObject") };
                _enable = _resume[3];
                var target = Field<FsmOwnerDefault>(_enable, "gameObject");
                if (target.OwnerOption != OwnerDefaultOption.SpecifyGameObject || target.GameObject.Value != item.Body.gameObject
                    || Field<FsmString>(_enable, "behaviour").Value != "Drivetrain" || !Field<FsmBool>(_enable, "enable").Value)
                    throw new InvalidOperationException("Native engine enable target changed.");
                var engine = Field<FsmOwnerDefault>(_resume[6], "gameObject").GameObject;
                if (!ReferenceEquals(engine, _starter.FsmVariables.FindFsmGameObject("Engine")) || engine.Value == null
                    || !engine.Value.transform.IsChildOf(item.Body.transform) || !Field<FsmBool>(_resume[6], "activate").Value
                    || !ReferenceEquals(Field<FsmBool>(_resume[5], "boolVariable"), _shutOff)
                    || Field<FsmBool>(_resume[5], "boolValue").Value)
                    throw new InvalidOperationException("Native engine activation references changed.");
                ValidateWrite(_resume[0], item, "SimEngine", "EngineFriction", "FrictionStop", 0);
                ValidateWrite(_resume[4], item, "PowerON", "Power", "Amps", 3);
                var light = Field<FsmOwnerDefault>(_resume[1], "gameObject").GameObject;
                var stall = Field<FsmProperty>(_resume[2], "targetProperty");
                if (!ReferenceEquals(light, _starter.FsmVariables.FindFsmGameObject("DashLightOil")) || light.Value == null
                    || !light.Value.transform.IsChildOf(item.Body.transform) || Field<FsmBool>(_resume[1], "activate").Value
                    || !ReferenceEquals(stall.TargetObject, _drivetrain) || stall.PropertyName != "canStall"
                    || stall.TargetTypeName != "Drivetrain" || !stall.setProperty || stall.BoolParameter.UseVariable || stall.BoolParameter.Value)
                    throw new InvalidOperationException("Native activation outputs changed.");
                var stopped = FsmHook.FindState(_starter, "Wait for start") ?? throw new InvalidOperationException("Native stop state missing.");
                _rpmSetter = Action(stopped, 5, "SetProperty", allowContinuous: true);
                var rpm = Field<FsmProperty>(_rpmSetter, "targetProperty");
                if (!ReferenceEquals(rpm.TargetObject, _drivetrain) || rpm.PropertyName != "rpm" || !rpm.setProperty
                    || rpm.TargetTypeName != "Drivetrain" || rpm.FloatParameter.UseVariable || rpm.FloatParameter.Value != 0)
                    throw new InvalidOperationException("Native RPM setter changed.");
                foreach (var state in new[] { "Wait", "Crank up", "Running" })
                    if (!FsmHook.EnsureRemoteEntry(_starter, state)) throw new InvalidOperationException("Native engine destination missing.");
                foreach (var state in new[] { "ACC on", "Motor OFF" })
                    if (!FsmHook.EnsureRemoteEntry(_ignition, state)) throw new InvalidOperationException("Native ignition destination missing.");
            }

            internal void Stop()
            {
                _acc.Value = false; _starting.Value = false; _shutOff.Value = true;
                FsmHook.FireRemoteEntry(_starter, "Wait");
                var disable = Clone(_enable); Set(disable, "enable", new FsmBool(false)); Run(disable);
                SetDrivetrain("rpm", 0, 0);
                _angularVelocity.SetValue(_drivetrain.Value, 0f);
            }
            internal void AccOn() => FsmHook.FireRemoteEntry(_ignition, "ACC on");
            internal void IgnitionOff() => FsmHook.FireRemoteEntry(_ignition, "Motor OFF");
            internal void Resume(ushort rpm, byte gear)
            {
                // Copy only the native activation actions, excluding starter sound,
                // battery drain and cranking time: an existing engine is taking over.
                foreach (var prototype in _resume) Run(Clone(prototype));
                SetDrivetrain("gear", 0, gear); SetDrivetrain("rpm", rpm, 0);
                // UnityCar overwrites its public rpm from this integrator each physics
                // tick. Seed both once at ownership change, then leave native physics alone.
                _angularVelocity.SetValue(_drivetrain.Value, rpm * _rpmToAngular);
                FsmHook.FireRemoteEntry(_starter, "Crank up");
            }
            private void SetDrivetrain(string member, float rpm, int gear)
            {
                var action = Clone(_rpmSetter);
                Set(action, "targetProperty", new FsmProperty { TargetObject = _drivetrain, TargetType = _drivetrain.Value.GetType(),
                    TargetTypeName = "Drivetrain", PropertyName = member, setProperty = true,
                    FloatParameter = new FsmFloat(rpm), IntParameter = new FsmInt(gear) });
                Set(action, "everyFrame", false); Run(action);
            }
            private void Run(FsmStateAction action) { action.Init(_context); action.OnEnter(); }
            private void ValidateWrite(FsmStateAction action, SyncedItem item, string target, string fsm, string variable, float value)
            {
                var obj = Field<FsmOwnerDefault>(action, "gameObject").GameObject;
                var scalar = Field<FsmFloat>(action, "setValue");
                if (!ReferenceEquals(obj, _starter.FsmVariables.FindFsmGameObject(target)) || obj.Value == null
                    || !obj.Value.transform.IsChildOf(item.Body.transform) || Field<FsmString>(action, "fsmName").Value != fsm
                    || Field<FsmString>(action, "variableName").Value != variable || scalar.UseVariable || scalar.Value != value)
                    throw new InvalidOperationException("Native activation scalar changed.");
            }
            private FsmBool Bool(string name) => _starter.FsmVariables.FindFsmBool(name) ?? throw new InvalidOperationException("Native engine flag missing: " + name);
            private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, HandoffFields).GetValue(obj);
            private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, HandoffFields).SetValue(obj, value);
            private static FsmStateAction Clone(FsmStateAction original)
            {
                var clone = (FsmStateAction)Activator.CreateInstance(original.GetType());
                foreach (var field in original.GetType().GetFields(HandoffFields)) field.SetValue(clone, field.GetValue(original));
                return clone;
            }
            private static FsmStateAction Action(FsmState state, int index, string type, bool allowContinuous = false)
            {
                var action = FsmHook.NativeAction(state, index) ?? throw new InvalidOperationException("Native engine action missing.");
                if (!action.Enabled || action.GetType().FullName != "HutongGames.PlayMaker.Actions." + type
                    || action.GetType().Assembly.GetName().Name != "Assembly-CSharp") throw new InvalidOperationException("Native engine action changed.");
                var everyFrame = action.GetType().GetField("everyFrame", HandoffFields);
                if (!allowContinuous && everyFrame != null && (bool)everyFrame.GetValue(action))
                    throw new InvalidOperationException("Native activation is no longer a one-shot action.");
                return action;
            }
            private static PlayMakerFSM Find(SyncedItem item, string path, string name, bool allowDisabled = false)
            {
                foreach (var fsm in item.Body.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (fsm.FsmName == name && ScenePath.Of(fsm.transform) == path && (allowDisabled || fsm.enabled) && fsm.gameObject.activeInHierarchy
                        && fsm.Fsm.Started) return fsm;
                throw new InvalidOperationException("Native engine graph not ready: " + path);
            }
        }
    }
}
