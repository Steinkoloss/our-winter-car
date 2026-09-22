using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        internal static bool PrepareContainerTank(SyncedItem item)
        {
            if (!item.IsVehicle || item.Path != FluidContainerSync.SorbetPath || item.Body == null) return false;
            EnsureVehicleSystemsProbe(item);
            foreach (var fsm in item.Body.GetComponentsInChildren<PlayMakerFSM>(true))
                if (fsm.FsmName == "Data" && fsm.Fsm.Started
                    && ScenePath.Of(fsm.transform) == item.Path + "/Simulation/FuelTankSorbett") {
                    item.FuelTankLevelVar=fsm.FsmVariables.FindFsmFloat("FuelLevel");
                    item.FuelTankCapacityVar=fsm.FsmVariables.FindFsmFloat("MaxCapacity");
                    return item.FuelTankLevelVar != null && item.FuelTankCapacityVar != null;
                }
            return false;
        }

        internal static bool IsParkedForContainer(SyncedItem item) => item.Body != null
            && !item.LocalDriveActive && !item.RemoteIsDriver && !item.LocallyOwned
            && item.RemoteOwner == WorldSyncIds.NoOwner && item.Body.velocity.sqrMagnitude <= .25f
            && !item.RemoteEngineOn && !item.RemoteAccOn && !HasLocalIgnitionActivity(item);

        internal static void CaptureContainerTank(SyncedItem item, VehicleState state)
        {
            state.FuelRevision=item.FuelRevision;
            state.FuelLiters=item.FuelRevision != 0 && item.FuelTankLevelVar != null ? item.FuelTankLevelVar.Value : 0;
        }

        internal static void RefreshContainerTank(SyncedItem item)
        {
            if (item.FuelTankLevelVar == null || item.FuelTankCapacityVar == null) return;
            float capacity=item.FuelTankCapacityVar.Value;
            if (!ContainerFuelAuthority.Level(item.FuelTankLevelVar.Value,capacity)) return;
            item.RemoteFuelLevel=NormalizeFuelLevel(item.FuelTankLevelVar.Value,capacity);
            if (item.GaugeFuelLevelVar != null) item.GaugeFuelLevelVar.Value=item.FuelTankLevelVar.Value/capacity;
            if (item.AcceptedVehicleState != null) {
                item.AcceptedVehicleState.FuelLevel=item.RemoteFuelLevel;
                CaptureContainerTank(item,item.AcceptedVehicleState);
            }
        }

        // Returns true when this is a revisioned tank (including a stale packet),
        // so callers must not fall back to the legacy quantized byte writer.
        internal static bool ReceiveContainerTank(SyncedItem item, VehicleState state, bool isHost)
        {
            if (state.FuelRevision == 0 && item.FuelRevision == 0) return false;
            if (state.FuelRevision < item.FuelRevision || (isHost && state.FuelRevision != item.FuelRevision)
                || item.FuelTankLevelVar == null || item.FuelTankCapacityVar == null
                || !ContainerFuelAuthority.Level(state.FuelLiters,item.FuelTankCapacityVar.Value)) {
                RefreshContainerTank(item);
                return true;
            }
            item.FuelTankLevelVar.Value=state.FuelLiters;
            item.FuelRevision=state.FuelRevision;
            RefreshContainerTank(item);
            return true;
        }
    }
}
