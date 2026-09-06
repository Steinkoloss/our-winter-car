using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>Host-owned occupancy and request sequencing, independent of scene timing.</summary>
    public sealed class PassengerSeatLedger
    {
        private readonly Dictionary<byte, PassengerState> _occupants = new Dictionary<byte, PassengerState>();
        private readonly Dictionary<byte, ushort> _sequences = new Dictionary<byte, ushort>();

        public IEnumerable<PassengerState> Occupants => _occupants.Values;

        public void Clear()
        {
            _occupants.Clear();
            _sequences.Clear();
        }

        public void ForgetPlayer(byte playerId)
        {
            _occupants.Remove(playerId);
            _sequences.Remove(playerId);
        }

        public void Record(PassengerState state)
        {
            if (state.IsSeated) _occupants[state.PlayerId] = state;
            else _occupants.Remove(state.PlayerId);
        }

        /// <summary>
        /// Called only after authenticating the sender. The scene validator must verify
        /// that the seat still exists, but only new claims need entry proximity proof.
        /// Null means an old/duplicate request: it must not eject a current occupant.
        /// </summary>
        public PassengerSeatDecision? Apply(PassengerState request, Func<PassengerState, bool, bool> validateSeat)
        {
            if (_sequences.TryGetValue(request.PlayerId, out ushort previous))
            {
                ushort difference = unchecked((ushort)(request.Sequence - previous));
                if (difference == 0 || difference > short.MaxValue) return null;
            }
            // Rejected requests consume their sequence too; replay cannot undo a correction.
            _sequences[request.PlayerId] = request.Sequence;

            bool continuing = _occupants.TryGetValue(request.PlayerId, out var current)
                && current.VehicleId == request.VehicleId && current.SeatIndex == request.SeatIndex;
            bool accepted = request.IsSeated
                ? request.VehicleId != 0 && request.SeatIndex < 3 && validateSeat(request, continuing)
                : request.VehicleId == 0;

            PassengerState? evicted = null;
            if (accepted && request.IsSeated)
            {
                foreach (var occupant in _occupants.Values)
                {
                    if (occupant.PlayerId == request.PlayerId || occupant.VehicleId != request.VehicleId
                        || occupant.SeatIndex != request.SeatIndex) continue;
                    if (occupant.PlayerId < request.PlayerId) accepted = false;
                    else evicted = OnFoot(occupant);
                    break;
                }
            }

            if (evicted != null) Record(evicted);
            var result = accepted ? request : OnFoot(request);
            Record(result);
            return new PassengerSeatDecision(result, accepted, evicted);
        }

        private static PassengerState OnFoot(PassengerState request) => new PassengerState
        {
            PlayerId = request.PlayerId,
            VehicleId = 0,
            SeatIndex = PassengerState.SeatNone,
            Sequence = request.Sequence,
        };
    }

    public sealed class PassengerSeatDecision
    {
        public PassengerState State { get; }
        public bool Accepted { get; }
        public PassengerState? Evicted { get; }

        internal PassengerSeatDecision(PassengerState state, bool accepted, PassengerState? evicted)
        {
            State = state;
            Accepted = accepted;
            Evicted = evicted;
        }
    }
}
