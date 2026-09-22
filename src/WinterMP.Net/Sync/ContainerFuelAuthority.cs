using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class ContainerFuelFacts
    {
        public float SourceLevel, SourceCapacity, DestinationLevel, DestinationCapacity;
        public bool OwnsSource, Compatible, Parked, Near;
    }

    public interface IContainerFuelWorld
    {
        bool TryRead(uint source, uint vehicle, byte actor, out ContainerFuelFacts facts);
        // Must compare the captured levels and commit BOTH or neither; no callbacks
        // or sends inside the transaction. Returning false must leave them untouched.
        bool TryCommit(ContainerFuelFacts before, ContainerFuelResult result);
    }

    public sealed class ContainerFuelAuthority
    {
        private readonly IContainerFuelWorld _world;
        private readonly Dictionary<byte, uint> _sequences = new Dictionary<byte, uint>();
        private readonly object _gate = new object();
        private uint _revision;
        public ContainerFuelAuthority(IContainerFuelWorld world) { _world = world; }
        public void Clear() { lock (_gate) { _sequences.Clear(); _revision = 0; } }
        public static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        public static bool Level(float level, float capacity) => Finite(level) && Finite(capacity)
            && capacity > 0 && level >= 0 && level <= capacity;
        public bool TryAccept(ContainerFuelIntent request, byte authenticatedActor, out ContainerFuelResult? result)
        {
            result = null;
            lock (_gate)
            {
                if (request == null || authenticatedActor == 255 || request.PlayerId != authenticatedActor
                    || request.SourceId == 0 || request.VehicleId == 0 || request.SourceId == request.VehicleId
                    || request.Sequence == 0 || !Finite(request.Amount) || request.Amount <= 0
                    || _revision == uint.MaxValue
                    || (_sequences.TryGetValue(authenticatedActor, out var sequence) && request.Sequence <= sequence)) return false;
                if (!_world.TryRead(request.SourceId, request.VehicleId, authenticatedActor, out var facts)
                    || !facts.OwnsSource || !facts.Compatible || !facts.Parked || !facts.Near
                    || !Level(facts.SourceLevel, facts.SourceCapacity) || !Level(facts.DestinationLevel, facts.DestinationCapacity)
                    || request.Amount > facts.SourceLevel
                    || (double)facts.DestinationLevel + request.Amount > facts.DestinationCapacity) return false;
                float source = facts.SourceLevel - request.Amount, destination = facts.DestinationLevel + request.Amount;
                // Native levels are IEEE singles. Never debit a rounded amount that
                // differs from the credited amount, including tiny/no-op transfers.
                double removed = (double)facts.SourceLevel - source, added = (double)destination - facts.DestinationLevel;
                if (removed <= 0 || removed != added || removed != request.Amount) return false;
                var accepted = new ContainerFuelResult { SourceId=request.SourceId, VehicleId=request.VehicleId,
                    PlayerId=authenticatedActor, Sequence=request.Sequence, Revision=_revision+1,
                    AcceptedAmount=request.Amount, SourceLevel=source, DestinationLevel=destination };
                if (!_world.TryCommit(facts, accepted)) return false;
                _sequences[authenticatedActor] = request.Sequence;
                _revision = accepted.Revision;
                result = accepted;
                return true;
            }
        }
    }
}
