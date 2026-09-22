using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        internal static void OnLocalConditionClaim(SessionManager session, SyncedItem item)
        {
            if (!item.IsVehicle) return;
            ClearConditionRelease(item);
            ClearClaimedCondition(item);
            if (GuestSaveGuard.ProtectWorld && !session.IsHost && session.State == SessionState.Connected
                && session.LocalPlayerId != 0 && session.LocalPlayerId != WorldSyncIds.NoOwner && item.Body != null)
            {
                item.ClaimedVehicleCondition = VehicleConditionClaimPolicy.Capture(item.AcceptedVehicleCondition,
                    ReferenceEquals(item.ParkedConditionBody, item.Body) ? item.ParkedVehicleCondition : null, item.Id, item.RemoteOwner);
                if (item.ClaimedVehicleCondition != null)
                {
                    item.ClaimedConditionBody = item.Body;
                    item.ConditionClaimant = session.LocalPlayerId;
                    SyncEventLog.Record("vehicle-condition-claim", item.Id.ToString("X8") + " inputs " + item.ClaimedVehicleCondition.Availability);
                }
            }
            ClearParkedCondition(item);
            item.AcceptedVehicleCondition = null;
            // Preserve sequence history while forcing the first report of this lease.
            item.HasSentCondition = false;
        }

        private static VehicleCondition? ReadClaimedCondition(SyncedItem item)
        {
            var session = SessionManager.Instance;
            if (!GuestSaveGuard.ProtectWorld || session == null || session.IsHost || session.State != SessionState.Connected) return null;
            if (item.Body == null || !ReferenceEquals(item.ClaimedConditionBody, item.Body))
            {
                ClearClaimedCondition(item);
                return null;
            }
            return VehicleConditionClaimPolicy.CanRead(item.ClaimedVehicleCondition, item.Id, item.ConditionClaimant,
                session.LocalPlayerId, item.LocallyOwned, item.RemoteOwner) ? item.ClaimedVehicleCondition : null;
        }

        internal static void ClearClaimedCondition(SyncedItem item)
        {
            item.ClaimedVehicleCondition = null;
            item.ClaimedConditionBody = null;
            item.ConditionClaimant = WorldSyncIds.NoOwner;
        }
    }
}
