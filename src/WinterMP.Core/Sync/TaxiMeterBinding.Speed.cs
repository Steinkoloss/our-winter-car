using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class TaxiMeterBinding
    {
        private void InstallSpeedReads()
        {
            var odometer = Obj(_meter, "Odometer");
            var native = Need(Fsm(odometer, "Data").FsmVariables.FindFsmFloat("MpS"));
            foreach (string name in new[] { "State 1", "State 2" })
            {
                var state = State(_meter, name); var original = state.Actions;
                var read = ActionAt(_meter, name, 0, "GetFsmFloat");
                if (Field<FsmOwnerDefault>(read, "gameObject").GameObject.Value != odometer
                    || Field<FsmString>(read, "fsmName").Value != "Data" || Field<FsmString>(read, "variableName").Value != "MpS"
                    || !ReferenceEquals(Field<FsmFloat>(read, "storeValue"), _values[8]) || !Field<bool>(read, "everyFrame"))
                    throw new InvalidOperationException("Changed taxi speed input.");
                var installed = (FsmStateAction[])original.Clone();
                var replacement = new MeterSpeedRead(_car.transform, native, _values[8]); replacement.Init(state);
                installed[Array.IndexOf(installed, read)] = replacement; state.Actions = installed;
                _restore.Add(() => { if (ReferenceEquals(state.Actions, installed)) state.Actions = original; });
            }
        }
        private sealed class MeterSpeedRead : FsmStateAction
        {
            private readonly Transform _car;
            private readonly FsmFloat _native, _output;
            private bool _failed;
            internal MeterSpeedRead(Transform car, FsmFloat native, FsmFloat output) { _car = car; _native = native; _output = output; }
            public override void OnEnter() { Read(); }
            public override void OnUpdate() { Read(); }
            private void Read()
            {
                if (!Enabled) return;
                if (_failed) { _output.Value = 0; return; }
                try
                {
                    var world = WorldSyncManager.Instance;
                    _output.Value = world != null && world.TryGetTaxiMeterSpeed(_car, out var speed) ? speed : _native.Value;
                }
                catch (Exception e) { _failed = true; _output.Value = 0; WinterMPPlugin.Log.LogError("Taxi meter speed unavailable: " + e); }
            }
        }
    }

    public sealed partial class WorldSyncManager
    {
        internal bool TryGetTaxiMeterSpeed(Transform car, out float speed)
        {
            speed = 0;
            var session = SessionManager.Instance;
            bool host = session != null && session.IsHost && (session.State == SessionState.Hosting || session.State == SessionState.Connected);
            foreach (var pair in _items.Items)
            {
                var item = pair.Value;
                if (item.Body == null || item.Body.transform != car) continue;
                if (!VehicleWearSimulationPolicy.Delegated(host, GuestSaveGuard.ProtectWorld,
                    item.LocallyOwned || IsLocalPlayerDriving(item), item.RemoteOwner)) return false;
                // A delegated car with expired telemetry contributes zero distance;
                // the host's inactive odometer must not resurrect a stale speed.
                speed = TaxiMeterPolicy.DelegatedSpeed(item.AcceptedVehicleState, item.Id, item.RemoteOwner, Time.unscaledTime < item.RemoteEngineUntil);
                return true;
            }
            return false;
        }
    }
}
