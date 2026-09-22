using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        internal VehicleConditionReleaseAck? BuildConditionReleaseAck(SyncedItem item, byte previousOwner, ItemTransform release)
        {
            if (!item.IsVehicle || item.Body == null || !ReferenceEquals(item.ParkedConditionBody, item.Body)) return null;
            return VehicleConditionReleasePolicy.Create(item.ParkedVehicleCondition, previousOwner, release);
        }

        internal static void BeginConditionRelease(SessionManager session, SyncedItem item, ItemTransform release)
        {
            var sent = item.SentFinalCondition;
            ClearConditionRelease(item);
            if (session.IsHost || session.State != SessionState.Connected || !item.IsVehicle || item.Body == null || !item.LocallyOwned) return;
            item.PendingConditionRelease = VehicleConditionReleasePolicy.Create(sent, session.LocalPlayerId, release);
            if (item.PendingConditionRelease != null) item.ConditionReleaseBody = item.Body;
        }

        internal void CompleteLocalConditionRelease(SyncedItem item)
        {
            ClearClaimedCondition(item);
            ApplyApprovedConditionRelease(item);
        }

        internal void ReceiveConditionReleaseAck(VehicleConditionReleaseAck ack)
        {
            var session = _bridge.Session;
            if (session == null || session.IsHost || session.State != SessionState.Connected || !ack.Valid
                || !_items.Items.TryGetValue(ack.Condition.VehicleId, out var item) || !item.IsVehicle || item.Body == null) return;
            if (!ReferenceEquals(item.ConditionReleaseBody, item.Body)) { ClearConditionRelease(item); return; }
            if (!VehicleConditionReleasePolicy.Matches(item.PendingConditionRelease, ack, session.LocalPlayerId)) return;
            // Loopback may confirm before SendItem returns; wait for actual release.
            item.ApprovedConditionRelease = ack.Copy();
            ApplyApprovedConditionRelease(item);
        }

        private void ApplyApprovedConditionRelease(SyncedItem item)
        {
            var session = _bridge.Session;
            if (session == null || session.IsHost || session.State != SessionState.Connected || item.Body == null) return;
            if (!ReferenceEquals(item.ConditionReleaseBody, item.Body)) { ClearConditionRelease(item); return; }
            if (item.LocallyOwned || item.RemoteOwner != WorldSyncIds.NoOwner || _items.IsLocalPlayerDriving(item)
                || !VehicleConditionReleasePolicy.Matches(item.PendingConditionRelease, item.ApprovedConditionRelease, session.LocalPlayerId)) return;
            item.ParkedVehicleCondition = VehicleConditionStreamPolicy.Copy(item.ApprovedConditionRelease!.Condition);
            item.ParkedConditionBody = item.Body;
            item.AcceptedVehicleCondition = null;
            item.ConditionNeedsApply = true;
            ClearConditionRelease(item);
            SyncEventLog.Record("vehicle-condition-release-approved", item.Id.ToString("X8"));
            try { UpdateConditionBindings(item); }
            catch (System.Exception error)
            { SyncEventLog.Record("vehicle-condition-release-retry", item.Id.ToString("X8") + ": " + error.Message); }
        }

        private VehicleCondition? ReadHostParkedCondition(SyncedItem item)
        {
            if (item.ParkedVehicleCondition == null || item.Body == null || !ReferenceEquals(item.ParkedConditionBody, item.Body)) return null;
            var session = _bridge.Session;
            return session != null && session.IsHost && session.State == SessionState.Hosting
                && VehicleConditionStreamPolicy.CanPresentParked(item.ParkedVehicleCondition, item.Id,
                    item.LocallyOwned || _items.IsLocalPlayerDriving(item), item.RemoteOwner)
                ? VehicleConditionStreamPolicy.Copy(item.ParkedVehicleCondition!) : null;
        }

        internal static void ClearConditionRelease(SyncedItem item)
        {
            item.SentFinalCondition = null; item.PendingConditionRelease = null;
            item.ApprovedConditionRelease = null; item.ConditionReleaseBody = null;
        }
    }
}
