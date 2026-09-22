using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net
{
    /// <summary>Only an established simulator advances its own vehicle stream.</summary>
    public sealed class VehicleStateStreamPolicy
    {
        public const byte HostPlayerId = 0, NoOwner = byte.MaxValue;
        private const byte KnownFlags = VehicleState.FlagEngineOn | VehicleState.FlagAccOn
            | VehicleState.FlagBlinkerLeft | VehicleState.FlagBlinkerRight | VehicleState.FlagHazard;
        private readonly Dictionary<uint, Dictionary<byte, ushort>> _sequences = new Dictionary<uint, Dictionary<byte, ushort>>();

        public static bool IsValid(VehicleState? state) => state != null && state.VehicleId != 0
            && state.OwnerPlayerId != NoOwner && (state.Flags & ~KnownFlags) == 0 && state.ValidTorque && state.ValidMovementSpeed && state.ValidDifferentialSpeed && state.ValidHandoffTemperature;

        public static bool CanPublish(bool isHost, bool locallyOwned, byte remoteOwner)
            => locallyOwned || (isHost && remoteOwner == NoOwner);

        public static ushort NextSequence(ushort previous)
        {
            ushort next = unchecked((ushort)(previous + 1));
            return next == VehicleState.SnapshotSequence ? (ushort)0 : next;
        }

        /// <summary>Transport authentication precedes this ownership and sequence gate.</summary>
        public bool Receive(VehicleState? state, bool receiverIsHost, bool locallyOwned, byte remoteOwner)
        {
            if (!IsValid(state) || locallyOwned) return false;
            var message = state!;
            if (message.Sequence == VehicleState.SnapshotSequence)
                return !receiverIsHost && message.OwnerPlayerId == HostPlayerId
                    && (remoteOwner == NoOwner || remoteOwner == HostPlayerId);

            if (receiverIsHost)
            {
                if (message.OwnerPlayerId == HostPlayerId || message.OwnerPlayerId != remoteOwner) return false;
            }
            else if (message.OwnerPlayerId != (remoteOwner == NoOwner ? HostPlayerId : remoteOwner)) return false;

            if (!_sequences.TryGetValue(message.VehicleId, out var senders))
            {
                senders = new Dictionary<byte, ushort>();
                _sequences.Add(message.VehicleId, senders);
            }
            if (senders.TryGetValue(message.OwnerPlayerId, out ushort previous))
            {
                ushort difference = unchecked((ushort)(message.Sequence - previous));
                if (difference == 0 || difference > short.MaxValue) return false;
            }
            senders[message.OwnerPlayerId] = message.Sequence;
            return true;
        }

        public void ForgetPlayer(byte playerId)
        {
            foreach (var senders in _sequences.Values) senders.Remove(playerId);
        }

        public void Clear() => _sequences.Clear();

        public static VehicleState? CaptureEngineClaim(VehicleState? state, uint vehicleId,
            byte previousOwner, bool seated, bool alreadyOwned, double now, double expiresAt)
        {
            if (!seated || alreadyOwned || previousOwner == NoOwner || !IsValid(state)
                || state!.VehicleId != vehicleId || state.OwnerPlayerId != previousOwner
                || double.IsNaN(now) || double.IsInfinity(now) || double.IsNaN(expiresAt)
                || double.IsInfinity(expiresAt) || now >= expiresAt) return null;
            return Copy(state);
        }

        public static VehicleState Copy(VehicleState state) => new VehicleState {
            VehicleId = state.VehicleId, OwnerPlayerId = state.OwnerPlayerId, Sequence = state.Sequence,
            Flags = state.Flags, Rpm = state.Rpm, SpeedTenthsKmh = state.SpeedTenthsKmh,
            FuelLevel = state.FuelLevel, CoolantTemp = state.CoolantTemp, Gear = state.Gear,
            TorqueAvailable = state.TorqueAvailable, EngineTorque = state.EngineTorque,
            MovementSpeedAvailable = state.MovementSpeedAvailable, MovementSpeedTenthsKmh = state.MovementSpeedTenthsKmh,
            DifferentialSpeedAvailable = state.DifferentialSpeedAvailable, DifferentialSpeed = state.DifferentialSpeed,
            HandoffTemperatureAvailable = state.HandoffTemperatureAvailable, HandoffTemperature = state.HandoffTemperature };
    }
}
