using System;

namespace WinterMP.Net.Sync
{
    /// <summary>
    /// Host-result acceptance seam. Authentication is supplied by the session,
    /// never by a payload flag. Keep effect high-water state across same-peer
    /// reconnects; snapshots alone cannot grant personal effects. No native
    /// mutation/prediction happens here while an intent is pending.
    /// </summary>
    public sealed class PaneScrapeReplica
    {
        private readonly uint _vehicle, _epoch;
        private readonly byte _localActor;
        private uint _lastPersonalSequence;
        public PaneScrapeSnapshot? Current { get; private set; }

        public PaneScrapeReplica(uint vehicleId, uint epoch, byte localActor)
        {
            if (vehicleId == 0 || epoch == 0 || localActor == 255) throw new ArgumentOutOfRangeException(nameof(epoch));
            _vehicle = vehicleId; _epoch = epoch; _localActor = localActor;
        }

        public bool ReceiveSnapshot(bool authenticatedHost, PaneScrapeSnapshot snapshot)
        {
            if (!ValidSnapshot(authenticatedHost, snapshot)) return false;
            if (Current != null && (snapshot.Revision < Current.Revision
                || (snapshot.Revision == Current.Revision && snapshot.Cutoff != Current.Cutoff))) return false;
            Current = snapshot;
            return true;
        }

        /// <summary>Admission-only baseline before re-enabling input on rejoin.
        /// Never use for live snapshots, which may overtake accepted effects.</summary>
        public void ResumePersonalEffectsAfter(uint sequence)
        {
            if (sequence > _lastPersonalSequence) _lastPersonalSequence = sequence;
        }

        public bool ReceiveDecision(bool authenticatedHost, PaneScrapeDecision decision, out bool applyPersonalEffects)
        {
            applyPersonalEffects = false;
            if (decision == null || decision.Snapshot == null || !ValidSnapshot(authenticatedHost, decision.Snapshot)) return false;
            var snapshot = decision.Snapshot;
            if (Current != null && snapshot.Revision == Current.Revision && snapshot.Cutoff != Current.Cutoff) return false;
            // Bulk snapshots can overtake ordered action results. Do not restore
            // the old pane, but still deliver the accepted actor's effect once.
            if (Current == null || snapshot.Revision >= Current.Revision) Current = snapshot;
            if (decision.Status == PaneScrapeStatus.Accepted && decision.Actor == _localActor
                && decision.Sequence > _lastPersonalSequence)
            {
                _lastPersonalSequence = decision.Sequence;
                applyPersonalEffects = true;
            }
            return true;
        }

        private bool ValidSnapshot(bool authenticatedHost, PaneScrapeSnapshot snapshot)
            => authenticatedHost && snapshot != null && snapshot.VehicleId == _vehicle && snapshot.Epoch == _epoch
                && snapshot.Revision != 0 && PaneScrapePolicy.Finite(snapshot.Cutoff);
    }
}
