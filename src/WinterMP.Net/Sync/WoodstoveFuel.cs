using System;

namespace WinterMP.Net.Sync
{
    // Engine-independent authority models; versioned codecs live in Messages.
    public enum WoodstoveFeedStatus
    {
        Accepted, InvalidActor, StaleEpoch, ReplayedSequence, WrongSource,
        InvalidResource, ResourceConsumed, ActorUnavailable, OutOfRange,
        InvalidEquipment, InvalidContact, SourceUnavailable, NativeFailure, Busy, Pending
    }

    public sealed class WoodstoveFeedRequest
    {
        public readonly uint SourceId, Epoch, Sequence, ResourceId;
        public readonly byte Actor;
        public WoodstoveFeedRequest(uint sourceId, uint epoch, byte actor, uint sequence, uint resourceId)
        { SourceId = sourceId; Epoch = epoch; Actor = actor; Sequence = sequence; ResourceId = resourceId; }
    }

    public sealed class WoodstoveFuelValues
    {
        public readonly int Fuel;
        public readonly float Heat;
        public readonly bool Lit;
        public WoodstoveFuelValues(int fuel, float heat, bool lit) { Fuel = fuel; Heat = heat; Lit = lit; }
        public bool Valid => Fuel >= 0 && Fuel <= 4 && FiniteNonnegative(Heat);
        internal static bool FiniteNonnegative(float value) => value >= 0 && !float.IsInfinity(value) && !float.IsNaN(value);
        internal bool Same(WoodstoveFuelValues other) => Fuel == other.Fuel && Heat == other.Heat && Lit == other.Lit;
    }

    /// <summary>Host observations only. No field here may be filled from guest assertions.</summary>
    public sealed class WoodstoveFeedContext
    {
        public bool ActorPresent, ActorAlive, SourceReady;
        public float PoseAgeSeconds, DistanceSquared;
        public uint ResourceId;
        // AvailableToActor is an observed prerequisite, NOT a grant of item/world ownership.
        // The native audit must decide whether feeding requires release or holding.
        public bool ResourceAvailable, ResourceAtSource, ResourceAvailableToActor, ResourceIsFirewood, EquipmentReady;
        public bool ResourceParented;
        public bool ResourceTagIsPart = true;
    }

    /// <summary>
    /// Game-thread adapter contract for the single cabin source. ReadFuel is pure;
    /// SourceReady includes native capacity. FeedOne must execute the audited
    /// native resource consumption plus fuel insertion, not inject final values.
    /// The authority checks one-unit change and IsConsumed afterward; exceptions
    /// or partial execution poison this authority until world/session reset.
    /// Deferred adapters additionally implement IWoodstoveDeferredFuelHost.
    /// </summary>
    public interface IWoodstoveFuelHost
    {
        WoodstoveFuelValues ReadFuel();
        WoodstoveFeedContext Observe(byte actor, uint resourceId);
        void FeedOne(uint resourceId);
        bool IsConsumed(uint resourceId);
    }

    // Unity Destroy completes later. Expiry is a failure, never evidence of consumption.
    public interface IWoodstoveDeferredFuelHost
    {
        bool CompletionExpired { get; }
    }

    public sealed class WoodstoveFuelSnapshot
    {
        public readonly uint SourceId, Epoch, Revision;
        public readonly WoodstoveFuelValues Values;
        private readonly uint[] _consumed;
        public uint[] ConsumedResources => (uint[])_consumed.Clone();
        public WoodstoveFuelSnapshot(uint sourceId, uint epoch, uint revision, WoodstoveFuelValues values, uint[] consumed)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (consumed == null) throw new ArgumentNullException(nameof(consumed));
            SourceId = sourceId; Epoch = epoch; Revision = revision; Values = values;
            _consumed = (uint[])consumed.Clone();
        }
    }

    public sealed class WoodstoveFeedDecision
    {
        public readonly WoodstoveFeedStatus Status;
        public readonly byte Actor;
        public readonly uint Sequence, HighWater;
        public readonly WoodstoveFuelSnapshot? Snapshot;
        public WoodstoveFeedDecision(WoodstoveFeedStatus status, byte actor, uint sequence,
            uint highWater, WoodstoveFuelSnapshot? snapshot)
        { Status = status; Actor = actor; Sequence = sequence; HighWater = highWater; Snapshot = snapshot; }
    }
}
