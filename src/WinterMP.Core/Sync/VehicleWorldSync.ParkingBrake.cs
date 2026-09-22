using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        internal static bool IsParkingBrakeControl(PlayMakerFSM fsm)
        {
            var config = SyncCatalog.ParkingBrake;
            if (fsm == null || config == null) return false;
            foreach (var source in config.Sources)
                if (fsm.FsmName == source.Fsm && ScenePath.Of(fsm.transform) == source.ControlPath) return true;
            return false;
        }

        internal bool OnLocalParkingBrake(PlayMakerFSM fsm)
        {
            if (!IsParkingBrakeControl(fsm)) return false;
            var session = _bridge.Session;
            if (session == null) return true;
            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || item.Body == null || !fsm.transform.IsChildOf(item.Body.transform)) continue;
                var binding = EnsureParkingBrake(item);
                if (binding == null) return true;
                // The existing motion lease authenticates the simulator. A parked
                // nearby user can claim it; a passenger cannot override a driver.
                if (!_items.TryClaimForInteraction(session, item))
                {
                    binding.Apply(item.AcceptedVehicleClimate?.ParkingBrakeAvailable == true
                        ? item.AcceptedVehicleClimate.ParkingBrake : binding.LastAccepted);
                }
                return true;
            }
            return true;
        }

        private static ParkingBrakeBinding? EnsureParkingBrake(SyncedItem item)
        {
            if (item.ParkingBrake != null && item.ParkingBrake.Control != null) return item.ParkingBrake;
            if (item.Body == null || Time.unscaledTime < item.NextParkingBrakeProbeAt) return null;
            item.NextParkingBrakeProbeAt = Time.unscaledTime + 5f;
            var config = SyncCatalog.ParkingBrake;
            if (config == null) return null;
            foreach (var source in config.Sources)
            {
                if (source.RootPath != item.Path) continue;
                try
                {
                    var node = item.Body.transform.Find(source.ControlPath.Substring(source.RootPath.Length + 1));
                    if (node == null) return null;
                    PlayMakerFSM? found = null;
                    foreach (var fsm in node.GetComponents<PlayMakerFSM>())
                        if (fsm.FsmName == source.Fsm)
                        {
                            if (found != null) throw new InvalidOperationException("Ambiguous native parking brake.");
                            found = fsm;
                        }
                    if (found == null || !found.Fsm.Initialized || !found.Fsm.Started) return null;
                    item.ParkingBrake = new ParkingBrakeBinding(found, source);
                    return item.ParkingBrake;
                }
                catch (Exception error)
                {
                    // A changed native signature disables only this binding.
                    item.NextParkingBrakeProbeAt = float.PositiveInfinity;
                    WinterMPPlugin.Log.LogError("Parking brake sync unavailable for " + item.Path + ": " + error.Message);
                }
            }
            return null;
        }

        private static void CaptureParkingBrake(SyncedItem item, VehicleClimate state)
        {
            var binding = EnsureParkingBrake(item);
            if (binding == null || !binding.Read(out float value)) return;
            state.ParkingBrakeAvailable = true; state.ParkingBrake = value;
        }

        private static void ApplyRemoteParkingBrake(SyncedItem item, VehicleClimate state)
        {
            if (!state.ParkingBrakeAvailable) return;
            var binding = EnsureParkingBrake(item);
            if (binding == null) return;
            try { binding.Apply(state.ParkingBrake); }
            catch (Exception error)
            { WinterMPPlugin.Log.LogError("Parking brake apply failed for " + item.Path + ": " + error.Message); }
        }

        private static void ClearParkingBrake(SyncedItem item)
        {
            if (item.ParkingBrake != null && GuestSaveGuard.ProtectWorld) item.ParkingBrake.Restore();
            item.ParkingBrake = null; item.NextParkingBrakeProbeAt = 0;
        }
    }

    internal sealed class ParkingBrakeBinding
    {
        internal readonly PlayMakerFSM Control;
        private readonly FsmFloat _value;
        private readonly string _idle, _increase, _decrease;
        private readonly float _maximum, _original;
        internal float LastAccepted;

        internal ParkingBrakeBinding(PlayMakerFSM control, ParkingBrakeSource source)
        {
            Control = control; _idle = source.IdleState; _increase = source.IncreaseState; _decrease = source.DecreaseState;
            _value = control.FsmVariables.FindFsmFloat(source.Variable) ?? throw new InvalidOperationException("Missing parking brake scalar.");
            if (!FsmHook.EnsureRemoteEntry(control, _idle)) throw new InvalidOperationException("Missing parking idle state.");
            float maximum = 0; int matches = 0;
            foreach (var state in control.Fsm.States)
            {
                if (state.Name != _increase && state.Name != _decrease) continue;
                foreach (var action in state.Actions)
                {
                    if (action.GetType().Name != "FloatClamp" || !action.Enabled) continue;
                    var type = action.GetType();
                    var variable = type.GetField("floatVariable")?.GetValue(action) as FsmFloat;
                    var min = type.GetField("minValue")?.GetValue(action) as FsmFloat;
                    var max = type.GetField("maxValue")?.GetValue(action) as FsmFloat;
                    if (!ReferenceEquals(variable, _value) || min == null || max == null || min.UseVariable || max.UseVariable
                        || min.Value != 0 || float.IsNaN(max.Value) || max.Value <= 0 || max.Value > 10000
                        || (matches != 0 && max.Value != maximum)) throw new InvalidOperationException("Native parking clamp changed.");
                    maximum = max.Value; matches++;
                }
            }
            if (matches != 2) throw new InvalidOperationException("Parking brake needs both native clamps.");
            _maximum = maximum; _original = _value.Value;
            if (!Read(out LastAccepted)) throw new InvalidOperationException("Invalid native parking brake value.");
        }

        internal bool Read(out float value)
        {
            value = _value.Value / _maximum;
            return Control != null && !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value <= 1;
        }

        internal void Apply(float value)
        {
            if (Control == null) return;
            // Replaying a timed FloatAdd yields a different result at each frame
            // rate. Only the simulator runs that action; observers use its scalar.
            if (Control.ActiveStateName == _increase || Control.ActiveStateName == _decrease)
                FsmHook.FireRemoteEntry(Control, _idle);
            _value.Value = value * _maximum; LastAccepted = value;
        }

        internal void Restore() { if (Control != null) _value.Value = _original; }
    }
}
