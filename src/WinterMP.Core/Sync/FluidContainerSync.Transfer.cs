using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FluidContainerSync : IContainerFuelWorld
    {
        internal const string GasolinePath = "EQUIPMENTS/gasoline(itemx)";
        internal const string SorbetPath = "SORBET(190-200psi)";
        private ContainerFuelAuthority? _fuelAuthority;
        private uint _fuelSequence;
        private ContainerFuelResult? _pendingFuelResult;

        internal static bool IsFiniteSource(SyncedItem item) => !item.IsVehicle && item.Path == GasolinePath;

        private static PlayMakerFSM? FindFsm(SyncedItem item, string suffix, string name)
        {
            if (item.Body == null) return null;
            var target = item.Body.transform.Find(suffix.TrimStart('/'));
            foreach (var fsm in item.Body.GetComponentsInChildren<PlayMakerFSM>(true))
                if (fsm.FsmName == name && fsm.transform == target) return fsm;
            return null;
        }

        private static bool LocateGasoline(SyncedItem item)
        {
            var data = FindFsm(item, "/FluidTrigger", "Data");
            var cap = FindFsm(item, "/Triggers/CapTrigger_FuelJerryGasoline", "Trigger");
            if (data == null || cap == null || !data.Fsm.Started) return false;
            var level = data.FsmVariables.FindFsmFloat("Fluid");
            var capacity = cap.FsmVariables.FindFsmFloat("MaxCapacity");
            if (level == null || capacity == null) return false;
            item.FluidLevelVar = level; item.FluidCapacityVar = capacity;
            item.FluidPouringVar = data.FsmVariables.FindFsmBool("Pouring");
            return true;
        }

        internal void RequestFuelTransfer(uint sourceId, uint vehicleId, float amount)
        {
            var session = SessionManager.Instance;
            if (session == null || _fuelSequence == uint.MaxValue) return;
            var request = new ContainerFuelIntent { SourceId=sourceId, VehicleId=vehicleId,
                PlayerId=session.LocalPlayerId, Sequence=++_fuelSequence, Amount=amount };
            if (session.IsHost) OnFuelIntent(request, session.LocalPlayerId);
            else session.SendWorldMessage(request, Channel.ReliableOrdered);
        }

        internal void OnFuelIntent(ContainerFuelIntent request, byte actor)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return;
            try
            {
                if (_fuelAuthority == null) _fuelAuthority = new ContainerFuelAuthority(this);
                if (_fuelAuthority.TryAccept(request, actor, out var result))
                    session.SendWorldMessage(result!, Channel.ReliableOrdered);
            }
            catch (Exception e) { WinterMPPlugin.Log.LogError("ContainerFuel: " + e); }
        }

        public bool TryRead(uint source, uint vehicle, byte actor, out ContainerFuelFacts facts)
        {
            facts = new ContainerFuelFacts();
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || !_items.Items.TryGetValue(source, out var can)
                || !_items.Items.TryGetValue(vehicle, out var car) || !IsFiniteSource(can)
                || !car.IsVehicle || car.Path != SorbetPath || can.Body == null || car.Body == null
                || can.DespawnSent || car.DespawnSent || !can.Body.gameObject.activeInHierarchy
                || !car.Body.gameObject.activeInHierarchy) return false;
            Locate(can, Time.unscaledTime);
            if (can.FluidLevelVar == null || can.FluidCapacityVar == null
                || !VehicleWorldSync.PrepareContainerTank(car)) return false;
            bool local = actor == session.LocalPlayerId;
            float now = Time.unscaledTime;
            facts.OwnsSource = local ? can.LocallyOwned && _items.IsHeldForFuel(can)
                : !can.LocallyOwned && can.RemoteOwner == actor && can.LastRemoteAt > 0
                    && now >= can.LastRemoteAt && now-can.LastRemoteAt <= 2f;
            facts.Compatible = true;
            facts.Parked = VehicleWorldSync.IsParkedForContainer(car) && !_items.IsLocalPlayerDriving(car);
            Vector3 sourcePosition = local ? can.Body.position : can.TargetPosition;
            facts.Near = PlayerPosition(session, actor, out var player)
                && (player-sourcePosition).sqrMagnitude <= 9f
                && (player-car.Body.position).sqrMagnitude <= 64f
                && (sourcePosition-car.Body.position).sqrMagnitude <= 64f;
            facts.SourceLevel=can.FluidLevelVar.Value; facts.SourceCapacity=can.FluidCapacityVar.Value;
            facts.DestinationLevel=car.FuelTankLevelVar!.Value; facts.DestinationCapacity=car.FuelTankCapacityVar!.Value;
            return true;
        }

        public bool TryCommit(ContainerFuelFacts before, ContainerFuelResult result)
        {
            if (!_items.Items.TryGetValue(result.SourceId, out var can)
                || !_items.Items.TryGetValue(result.VehicleId, out var car)
                || can.FluidLevelVar == null || car.FuelTankLevelVar == null
                || can.FluidLevelVar.Value != before.SourceLevel || car.FuelTankLevelVar.Value != before.DestinationLevel) return false;
            // FsmFloat.Value is a scalar assignment; no FSM event, native transfer,
            // or network callback is fired between these writes.
            try { can.FluidLevelVar.Value=result.SourceLevel; car.FuelTankLevelVar.Value=result.DestinationLevel; }
            catch { can.FluidLevelVar.Value=before.SourceLevel; car.FuelTankLevelVar.Value=before.DestinationLevel; throw; }
            can.FuelRevision=car.FuelRevision=result.Revision;
            can.AcceptedFluidLevel=result.SourceLevel;
            try { VehicleWorldSync.RefreshContainerTank(car); }
            catch (Exception e) { WinterMPPlugin.Log.LogError("ContainerFuel presentation: " + e); }
            can.NextFluidSendAt=0;
            return true;
        }

        internal void OnFuelResult(ContainerFuelResult result)
        {
            var session=SessionManager.Instance;
            if (session == null || session.IsHost || result.Revision == 0 || result.Sequence == 0
                || result.PlayerId == 255 || !ContainerFuelAuthority.Finite(result.AcceptedAmount) || result.AcceptedAmount <= 0
                || !ContainerFuelAuthority.Finite(result.SourceLevel) || result.SourceLevel < 0
                || !ContainerFuelAuthority.Finite(result.DestinationLevel) || result.DestinationLevel < 0) return;
            if (_pendingFuelResult == null || result.Revision > _pendingFuelResult.Revision) _pendingFuelResult=result;
            ApplyFuelResult();
        }

        private void ApplyFuelResult()
        {
            var r=_pendingFuelResult;
            if (r == null || !_items.Items.TryGetValue(r.SourceId,out var can) || !IsFiniteSource(can)
                || !_items.Items.TryGetValue(r.VehicleId,out var car) || car.Path != SorbetPath || !car.IsVehicle) return;
            Locate(can,Time.unscaledTime);
            if (can.FluidLevelVar == null || can.FluidCapacityVar == null || !VehicleWorldSync.PrepareContainerTank(car)) return;
            if (!ContainerFuelAuthority.Level(r.SourceLevel,can.FluidCapacityVar.Value)
                || !ContainerFuelAuthority.Level(r.DestinationLevel,car.FuelTankCapacityVar!.Value)) return;
            if (r.Revision > can.FuelRevision) {
                can.FluidLevelVar.Value=can.AcceptedFluidLevel=r.SourceLevel; can.FuelRevision=r.Revision;
            }
            if (r.Revision > car.FuelRevision) {
                car.FuelTankLevelVar!.Value=r.DestinationLevel; car.FuelRevision=r.Revision;
                VehicleWorldSync.RefreshContainerTank(car);
            }
            _pendingFuelResult=null;
        }

        private static bool PlayerPosition(SessionManager session, byte actor, out Vector3 position)
        {
            if (actor == session.LocalPlayerId) {
                var local=GameObject.Find(WorldSyncBridge.PlayerObjectName);
                position=local != null ? local.transform.position : Vector3.zero; return local != null;
            }
            float now=Time.unscaledTime;
            foreach (var player in session.Players)
                if (player.PlayerId == actor && player.LastTransformTime > 0 && now >= player.LastTransformTime
                    && now-player.LastTransformTime <= 2f) { position=player.Position; return true; }
            position=Vector3.zero; return false;
        }

        private void UpdateFuelInput(SessionManager session)
        {
            ApplyFuelResult();
            // Explicit finite action; never infer a transfer from already-mutated
            // native levels. Native pour/station mechanics remain a separate path.
            if (!Input.GetKey(KeyCode.LeftAlt) || !Input.GetKeyDown(KeyCode.R)) return;
            foreach (var can in _items.Items.Values)
                if (IsFiniteSource(can) && can.LocallyOwned && _items.IsHeldForFuel(can))
                    foreach (var car in _items.Items.Values)
                        if (car.IsVehicle && car.Path == SorbetPath && car.Body != null
                            && (can.Body.position-car.Body.position).sqrMagnitude <= 64f) {
                            RequestFuelTransfer(can.Id,car.Id,.25f); return;
                        }
        }
    }
}
