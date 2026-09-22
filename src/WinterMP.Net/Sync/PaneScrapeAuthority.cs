using System;
using System.Collections.Generic;

namespace WinterMP.Net.Sync
{
    /// <summary>
    /// One discovered pane in one authority epoch, called on the game thread.
    /// Keep this instance (including denied sequence high-water marks) across peer
    /// reconnects. A new world/session uses a new nonzero epoch and a new instance.
    /// Core's PaneScrapeSync supplies authenticated native equipment/contact facts.
    /// </summary>
    public sealed class PaneScrapeAuthority
    {
        private readonly uint _vehicle, _epoch;
        private readonly IPaneScrapeHost _host;
        private readonly Dictionary<byte, uint> _seen = new Dictionary<byte, uint>();
        private PaneScrapeSnapshot _current;
        private bool _executing;
        public bool IsExecuting => _executing;
        public bool Faulted { get; private set; }

        public PaneScrapeAuthority(uint vehicleId, uint epoch, IPaneScrapeHost host)
        {
            if (vehicleId == 0 || epoch == 0) throw new ArgumentOutOfRangeException(nameof(epoch));
            if (host == null) throw new ArgumentNullException(nameof(host));
            _vehicle = vehicleId; _epoch = epoch; _host = host;
            float cutoff = host.ReadWindshieldCutoff();
            if (!PaneScrapePolicy.Finite(cutoff)) throw new ArgumentException("Native cutoff is nonfinite.");
            _current = new PaneScrapeSnapshot(_vehicle, _epoch, 1, cutoff);
        }

        /// <summary>
        /// Reads live vanilla state for late join/soft resync. FREEZE, roof startup
        /// and climate changes are observed, never overwritten by a stored scrape.
        /// The binding must publish Capture() when vanilla changes the pane too.
        /// </summary>
        public PaneScrapeSnapshot Capture()
        {
            try
            {
                float cutoff = _host.ReadWindshieldCutoff();
                if (!PaneScrapePolicy.Finite(cutoff)) throw new InvalidOperationException("Native cutoff is nonfinite.");
                if (cutoff != _current.Cutoff)
                {
                    if (_current.Revision == uint.MaxValue) throw new InvalidOperationException("Pane revision exhausted; renew authority epoch.");
                    _current = new PaneScrapeSnapshot(_vehicle, _epoch, _current.Revision + 1, cutoff);
                }
                return _current;
            }
            catch { Faulted = true; throw; }
        }

        public PaneScrapeDecision Decide(byte authenticatedActor, PaneScrapeIntent intent)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            if (_executing)
            {
                if (authenticatedActor != 255 && intent.Actor == authenticatedActor && intent.Epoch == _epoch
                    && intent.Sequence != 0 && (!_seen.TryGetValue(authenticatedActor, out uint seen) || intent.Sequence > seen))
                    _seen[authenticatedActor] = intent.Sequence;
                return new PaneScrapeDecision(PaneScrapeStatus.Busy, intent.Actor, intent.Sequence, null);
            }
            if (Faulted) return Result(PaneScrapeStatus.NativeFailure, intent);
            if (authenticatedActor == 255 || intent.Actor != authenticatedActor)
                return Result(PaneScrapeStatus.InvalidActor, intent);
            if (intent.Epoch != _epoch) return Result(PaneScrapeStatus.StaleEpoch, intent);
            if (intent.Sequence == 0 || (_seen.TryGetValue(authenticatedActor, out uint previous) && intent.Sequence <= previous))
                return Result(PaneScrapeStatus.ReplayedSequence, intent);

            // Record authenticated attempts before world checks or callbacks. A
            // denied stroke cannot later be made valid by replaying beside the car.
            // uint sequences do not wrap; exhaustion requires a new authority epoch.
            _seen[authenticatedActor] = intent.Sequence;
            if (intent.VehicleId != _vehicle || intent.Pane != PaneScrapeIntent.Windshield)
                return Result(PaneScrapeStatus.WrongPane, intent);
            _executing = true;
            try
            {
                var status = PaneScrapePolicy.Validate(intent, _host.ReadContext(authenticatedActor));
                if (status != PaneScrapeStatus.Accepted) return Result(status, intent);
                // Read current vanilla state before touching it. Never call a native
                // mutator with a stale/nonfinite base, or retry after partial failure.
                Capture();
                _host.ApplyWindshieldDelta();
                return Result(PaneScrapeStatus.Accepted, intent);
            }
            catch
            {
                Faulted = true;
                return Result(PaneScrapeStatus.NativeFailure, intent);
            }
            finally { _executing = false; }
        }

        private PaneScrapeDecision Result(PaneScrapeStatus status, PaneScrapeIntent intent)
        {
            PaneScrapeSnapshot? snapshot = null;
            try { snapshot = Capture(); }
            catch { status = PaneScrapeStatus.NativeFailure; }
            return new PaneScrapeDecision(status, intent.Actor, intent.Sequence, snapshot);
        }
    }
}
