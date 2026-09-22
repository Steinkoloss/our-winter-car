using System;
using System.Collections.Generic;

namespace WinterMP.Net.Sync
{
    /// <summary>
    /// One fixed cabin stove, one host world/session epoch. Retain this instance
    /// and Seen(actor) through reconnects. No sidecar or cross-save persistence;
    /// Capture observes vanilla burn/initialization rather than replaying old fuel.
    /// </summary>
    public sealed class WoodstoveFuelAuthority
    {
        public const string CabinPath = "CABIN/Cabin/woodstove/Fireplace";
        public static readonly uint CabinSourceId = StableHash.Fnv1a32(CabinPath);
        // Conservative seam limits, not claims about native trigger geometry.
        public const float MaxActorDistanceSquared = 9f, MaxPoseAgeSeconds = 2f;
        private readonly uint _epoch;
        private readonly IWoodstoveFuelHost _host;
        private readonly Dictionary<byte, uint> _seen = new Dictionary<byte, uint>();
        private readonly HashSet<uint> _consumed = new HashSet<uint>();
        private WoodstoveFuelValues _values;
        private uint _revision = 1;
        private bool _executing;
        private WoodstoveFeedRequest? _pending;
        public bool Pending => _pending != null;
        public bool Faulted { get; private set; }

        public WoodstoveFuelAuthority(uint epoch, IWoodstoveFuelHost host)
        {
            if (epoch == 0) throw new ArgumentOutOfRangeException(nameof(epoch));
            if (host == null) throw new ArgumentNullException(nameof(host));
            _epoch = epoch; _host = host; _values = host.ReadFuel();
            if (_values == null || !_values.Valid) throw new ArgumentException("Invalid native woodstove fuel/heat.");
        }

        public uint Seen(byte actor) => _seen.TryGetValue(actor, out var sequence) ? sequence : 0;

        public WoodstoveFuelSnapshot Capture()
        {
            if (Faulted || _executing || Pending) throw new InvalidOperationException("Woodstove capture unavailable.");
            try
            {
                var values = _host.ReadFuel();
                if (values == null || !values.Valid) throw new InvalidOperationException("Invalid native woodstove fuel/heat.");
                if (!_values.Same(values)) { AdvanceRevision(); _values = values; }
                return Snapshot();
            }
            catch { Faulted = true; throw; }
        }

        public WoodstoveFeedDecision Decide(byte authenticatedActor, WoodstoveFeedRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (_executing || Pending) return RejectBusy(authenticatedActor, request);
            if (Faulted) return Result(WoodstoveFeedStatus.NativeFailure, request, false);
            if (authenticatedActor == 255 || authenticatedActor != request.Actor)
                return Result(WoodstoveFeedStatus.InvalidActor, request);
            if (request.Epoch != _epoch) return Result(WoodstoveFeedStatus.StaleEpoch, request);
            if (request.Sequence == 0 || request.Sequence <= Seen(authenticatedActor))
                return Result(WoodstoveFeedStatus.ReplayedSequence, request);
            // Authenticate first. Remember even denied attempts so replaying after
            // moving closer/equipping a resource cannot turn a denial into a feed.
            _seen[authenticatedActor] = request.Sequence;
            if (request.SourceId != CabinSourceId) return Result(WoodstoveFeedStatus.WrongSource, request);
            if (request.ResourceId == 0) return Result(WoodstoveFeedStatus.InvalidResource, request);
            if (_consumed.Contains(request.ResourceId)) return Result(WoodstoveFeedStatus.ResourceConsumed, request);

            _executing = true;
            try
            {
                var context = _host.Observe(authenticatedActor, request.ResourceId);
                var status = Validate(request, context);
                if (status != WoodstoveFeedStatus.Accepted) return Result(status, request);
                var before = _host.ReadFuel();
                if (before == null || !before.Valid) throw new InvalidOperationException("Invalid native fuel before feed.");
                if (before.Fuel >= 4) return Result(WoodstoveFeedStatus.SourceUnavailable, request);
                if (_revision == uint.MaxValue) throw new InvalidOperationException("Woodstove revision exhausted.");
                // The in-flight guard is the reservation: callbacks cannot re-enter
                // this source. Never repeat the call after partial native failure.
                _host.FeedOne(request.ResourceId);
                var after = _host.ReadFuel();
                if (after == null || !after.Valid || after.Fuel != before.Fuel + 1)
                    throw new InvalidOperationException("Native feed did not consume exactly one resource and add one wood.");
                if (!_host.IsConsumed(request.ResourceId))
                {
                    if (!(_host is IWoodstoveDeferredFuelHost))
                        throw new InvalidOperationException("Synchronous feed did not retire its resource.");
                    _pending = request;
                    return Result(WoodstoveFeedStatus.Pending, request, false);
                }
                _consumed.Add(request.ResourceId);
                AdvanceRevision(); _values = after;
                return Result(WoodstoveFeedStatus.Accepted, request);
            }
            catch
            {
                Faulted = true;
                // No stale 'success' snapshot following a possibly partial native write.
                return Result(WoodstoveFeedStatus.NativeFailure, request, false);
            }
            finally { _executing = false; }
        }

        /// <summary>Also used while the Core adapter publishes an outcome. Never invokes native code.</summary>
        public WoodstoveFeedDecision RejectBusy(byte authenticatedActor, WoodstoveFeedRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            // Busy is still a denied attempt. A callback/replay must not gain
            // permission to spend another piece after this reservation finishes.
            if (authenticatedActor != 255 && authenticatedActor == request.Actor && request.Epoch == _epoch
                && request.Sequence > Seen(authenticatedActor)) _seen[authenticatedActor] = request.Sequence;
            return Result(WoodstoveFeedStatus.Busy, request, false);
        }

        private static WoodstoveFeedStatus Validate(WoodstoveFeedRequest request, WoodstoveFeedContext context)
        {
            if (context == null || !context.ActorPresent || !context.ActorAlive
                || !WoodstoveFuelValues.FiniteNonnegative(context.PoseAgeSeconds) || context.PoseAgeSeconds > MaxPoseAgeSeconds)
                return WoodstoveFeedStatus.ActorUnavailable;
            if (!context.SourceReady) return WoodstoveFeedStatus.SourceUnavailable;
            if (!WoodstoveFuelValues.FiniteNonnegative(context.DistanceSquared) || context.DistanceSquared > MaxActorDistanceSquared)
                return WoodstoveFeedStatus.OutOfRange;
            if (!context.ResourceAvailable || !context.ResourceIsFirewood || !context.ResourceAvailableToActor
                || context.ResourceParented || !context.ResourceTagIsPart
                || context.ResourceId != request.ResourceId)
                return WoodstoveFeedStatus.InvalidResource;
            if (!context.EquipmentReady) return WoodstoveFeedStatus.InvalidEquipment;
            if (!context.ResourceAtSource) return WoodstoveFeedStatus.InvalidContact;
            return WoodstoveFeedStatus.Accepted;
        }
        private void AdvanceRevision()
        {
            if (_revision == uint.MaxValue) throw new InvalidOperationException("Woodstove revision exhausted.");
            _revision++;
        }

        public WoodstoveFeedDecision? Poll()
        {
            var request = _pending;
            if (request == null || _executing || Faulted) return null;
            _executing = true;
            try
            {
                if (((IWoodstoveDeferredFuelHost)_host).CompletionExpired)
                    throw new InvalidOperationException("Deferred native destruction expired.");
                if (!_host.IsConsumed(request.ResourceId)) return null;
                var after = _host.ReadFuel();
                if (after == null || !after.Valid || after.Fuel > 4)
                    throw new InvalidOperationException("Invalid native state after destruction.");
                // The +1 was checked immediately. Vanilla depletion since then wins.
                _consumed.Add(request.ResourceId); AdvanceRevision(); _values = after;
                _pending = null;
                return Result(WoodstoveFeedStatus.Accepted, request);
            }
            catch
            {
                Faulted = true; _pending = null;
                return Result(WoodstoveFeedStatus.NativeFailure, request, false);
            }
            finally { _executing = false; }
        }
        private WoodstoveFuelSnapshot Snapshot()
        {
            var consumed = new uint[_consumed.Count]; _consumed.CopyTo(consumed); Array.Sort(consumed);
            return new WoodstoveFuelSnapshot(CabinSourceId, _epoch, _revision, _values, consumed);
        }
        private WoodstoveFeedDecision Result(WoodstoveFeedStatus status, WoodstoveFeedRequest request, bool snapshot = true)
            => new WoodstoveFeedDecision(status, request.Actor, request.Sequence, Seen(request.Actor), snapshot ? Snapshot() : null);
    }
}
