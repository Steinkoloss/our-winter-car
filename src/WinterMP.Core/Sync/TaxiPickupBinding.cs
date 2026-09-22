using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    /// <summary>Projects the authenticated taxi driver's context into the host's native pickup checks.</summary>
    internal sealed class TaxiPickupBinding
    {
        private readonly TaxiPickupData _c;
        private readonly PlayMakerFSM _walker;
        private readonly GameObject _car, _proxy;
        private readonly FsmGameObject _camera, _player;
        private readonly FsmString _vehicle;
        private readonly FsmGameObject _target = new FsmGameObject();
        private readonly FsmGameObject _look = new FsmGameObject();
        private readonly FsmString _driving = new FsmString();
        private readonly List<Replacement> _replaced = new List<Replacement>();
        private byte _driver;
        private bool _restored;

        internal static TaxiPickupBinding? TryBind()
        {
            SyncCatalog.EnsureLoaded();
            var c = SyncCatalog.TaxiPickup ?? throw new InvalidOperationException("Missing taxi pickup catalog.");
            PlayMakerFSM? job = null;
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null || fsm.FsmName != c["jobFsm"] || ScenePath.Of(fsm.transform) != c["jobPath"]) continue;
                if (job != null) throw new InvalidOperationException("Ambiguous taxi job.");
                job = fsm;
            }
            if (job == null) return null;
            if (!job.Fsm.Initialized) job.Fsm.Init(job);
            var parent = job.FsmVariables.FindFsmGameObject(c["customer"])?.Value;
            var car = job.FsmVariables.FindFsmGameObject(c["car"])?.Value;
            if (parent == null || car == null) throw new InvalidOperationException("Missing native taxi objects.");
            // The walker leaves Customer1 when it boards; discover both native
            // parents without requiring either object to be active.
            PlayMakerFSM? walker = null;
            foreach (var fsm in parent.GetComponentsInChildren<PlayMakerFSM>(true))
                if (fsm.gameObject.name == c["walker"] && fsm.FsmName == c["walkerFsm"])
                {
                    if (walker != null) throw new InvalidOperationException("Ambiguous taxi customer.");
                    walker = fsm;
                }
            if (walker == null)
                foreach (var fsm in car.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (fsm.gameObject.name == c["walker"] && fsm.FsmName == c["walkerFsm"])
                    {
                        if (walker != null) throw new InvalidOperationException("Ambiguous boarded taxi customer.");
                        walker = fsm;
                    }
            if (walker == null) return null;
            if (!walker.Fsm.Initialized) walker.Fsm.Init(walker);
            foreach (var state in walker.Fsm.States) if (!state.IsInitialized) return null;
            return new TaxiPickupBinding(c, walker, parent, car);
        }

        private TaxiPickupBinding(TaxiPickupData c, PlayMakerFSM walker, GameObject parent, GameObject car)
        {
            _c = c; _walker = walker; _car = car;
            _camera = FsmVariables.GlobalVariables.FindFsmGameObject(c["cameraGlobal"])
                ?? throw new InvalidOperationException("Missing native taxi camera.");
            _player = FsmVariables.GlobalVariables.FindFsmGameObject(c["playerGlobal"])
                ?? throw new InvalidOperationException("Missing native taxi player.");
            _vehicle = FsmVariables.GlobalVariables.FindFsmString(c["vehicleGlobal"])
                ?? throw new InvalidOperationException("Missing native taxi vehicle name.");
            if (walker.FsmVariables.FindFsmGameObject("Parent")?.Value != parent
                || walker.FsmVariables.FindFsmGameObject("CarGetInPivot")?.Value?.transform.IsChildOf(car.transform) != true
                || walker.FsmVariables.FindFsmGameObject("CarMassPassenger")?.Value?.transform.IsChildOf(car.transform) != true)
                throw new InvalidOperationException("Changed native taxi boarding targets.");
            Distance(c["farState"], 0);
            Distance(c["nearState"], 3);
            var look = Action(c["nearState"], 0, "SmoothLookAt");
            Replace(look, "targetObject", c["playerGlobal"], _look);
            var compare = Action(c["carState"], 0, "StringCompare");
            if (Field<FsmString>(compare, "compareTo").Value != c["vehicleName"]
                || Field<bool>(compare, "everyFrame")) throw new InvalidOperationException("Changed native taxi vehicle check.");
            Replace(compare, "stringVariable", c["vehicleGlobal"], _driving);
            _proxy = new GameObject("WinterMP taxi driver context") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                UseHostContext();
                foreach (var replacement in _replaced) replacement.Field.SetValue(replacement.Action, replacement.Value);
            }
            catch { Restore(); throw; }
        }

        internal void Update(SessionManager session)
        {
            if (_restored) return;
            if (!session.IsHost || _walker == null || _car == null) throw new InvalidOperationException("Taxi pickup context lost.");
            UseHostContext();
            byte driver = 0;
            var world = WorldSyncManager.Instance;
            if (world != null && _car.activeInHierarchy)
                foreach (var peer in session.Players)
                {
                    if (peer.PlayerId == session.LocalPlayerId
                        || !world.TryGetDriverAnchor(peer.PlayerId, out var seat, out var vehicle)
                        || vehicle != _car.transform || seat == null
                        || !TaxiPickupPolicy.CanUseDriver(peer.IsDead, Time.unscaledTime, peer.LastTransformTime,
                            (peer.Position - seat.position).sqrMagnitude)) continue;
                    _proxy.transform.position = seat.position;
                    _target.Value = _look.Value = _proxy;
                    _driving.Value = _c["vehicleName"];
                    driver = peer.PlayerId;
                    break;
                }
            if (driver != _driver)
            {
                _driver = driver;
                SyncEventLog.Record("taxi-pickup-driver", driver == 0 ? "native host context" : "guest " + driver);
            }
        }

        private void UseHostContext()
        {
            _target.Value = _camera.Value; _look.Value = _player.Value; _driving.Value = _vehicle.Value;
        }

        internal void Restore()
        {
            if (_restored) return;
            _restored = true;
            foreach (var replacement in _replaced) replacement.Field.SetValue(replacement.Action, replacement.Original);
            if (_proxy != null) UnityEngine.Object.Destroy(_proxy);
        }

        private void Distance(string state, int index)
        {
            var action = Action(state, index, "GetDistance");
            var owner = Field<FsmOwnerDefault>(action, "gameObject");
            if ((int)owner.OwnerOption != 0 || !Field<bool>(action, "everyFrame")
                || !ReferenceEquals(Field<FsmFloat>(action, "storeResult"), _walker.FsmVariables.FindFsmFloat("Distance")))
                throw new InvalidOperationException("Changed native taxi distance check.");
            Replace(action, "target", _c["cameraGlobal"], _target);
        }

        private FsmStateAction Action(string state, int index, string type)
        {
            var s = FsmHook.FindState(_walker, state) ?? throw new InvalidOperationException("Missing taxi pickup state " + state);
            var action = FsmHook.NativeAction(s, index);
            if (action == null || action.GetType().Name != type) throw new InvalidOperationException("Changed taxi action " + state + "/" + index);
            return action;
        }

        private void Replace(FsmStateAction action, string field, string global, NamedVariable value)
        {
            var info = action.GetType().GetField(field) ?? throw new InvalidOperationException("Missing taxi field " + field);
            var original = info.GetValue(action) as NamedVariable;
            if (original == null || !original.UseVariable || original.Name != global)
                throw new InvalidOperationException("Changed native taxi reference " + global);
            _replaced.Add(new Replacement { Action = action, Field = info, Original = original, Value = value });
        }

        private static T Field<T>(FsmStateAction action, string name) => (T)action.GetType().GetField(name).GetValue(action);
        private sealed class Replacement
        {
            internal FsmStateAction Action = null!;
            internal FieldInfo Field = null!;
            internal NamedVariable Original = null!, Value = null!;
        }
    }
}
