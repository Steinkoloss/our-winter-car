using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net
{
    /// <summary>Condition follows established vehicle ownership, retaining each sender's history through handoffs.</summary>
    public sealed class VehicleConditionStreamPolicy
    {
        private readonly Dictionary<uint, Dictionary<byte, ushort>> _sequences = new Dictionary<uint, Dictionary<byte, ushort>>();

        public static bool IsValid(VehicleCondition? state) => state != null && state.VehicleId != 0
            && state.OwnerPlayerId != VehicleStateStreamPolicy.NoOwner && ((state.Flags & 15) & (state.Flags >> 4)) == 0
            && (state.Availability & ~VehicleCondition.AvailableAll) == 0;

        public bool Receive(VehicleCondition? state, bool receiverIsHost, bool localDriver, byte remoteOwner)
        {
            if (!IsValid(state) || localDriver) return false;
            var message = state!;
            if (message.Sequence == VehicleCondition.SnapshotSequence)
                return !receiverIsHost && message.OwnerPlayerId == 0
                    && (remoteOwner == VehicleStateStreamPolicy.NoOwner || remoteOwner == 0);
            if (receiverIsHost)
            {
                if (message.OwnerPlayerId == 0 || message.OwnerPlayerId != remoteOwner) return false;
            }
            else if (message.OwnerPlayerId != (remoteOwner == VehicleStateStreamPolicy.NoOwner ? (byte)0 : remoteOwner)) return false;

            if (!_sequences.TryGetValue(message.VehicleId, out var senders))
            {
                senders = new Dictionary<byte, ushort>(); _sequences.Add(message.VehicleId, senders);
            }
            if (senders.TryGetValue(message.OwnerPlayerId, out ushort previous))
            {
                ushort difference = unchecked((ushort)(message.Sequence - previous));
                if (difference == 0 || difference > short.MaxValue) return false;
            }
            senders[message.OwnerPlayerId] = message.Sequence;
            return true;
        }

        public void ForgetPlayer(byte playerId) { foreach (var senders in _sequences.Values) senders.Remove(playerId); }
        public void Clear() => _sequences.Clear();

        public static bool TryGetObserverHealth(VehicleCondition? state, uint vehicleId, bool localDriver,
            byte remoteOwner, int wheel, out byte health)
        {
            health = 0;
            if (wheel < 0 || wheel > 3 || !CanPresent(state, vehicleId, localDriver, remoteOwner) || !state!.HasWheel(wheel)) return false;
            health = WheelHealth(state!, wheel);
            return true;
        }

        public static bool CanPresent(VehicleCondition? state, uint vehicleId, bool localDriver, byte remoteOwner) =>
            IsValid(state) && state!.VehicleId == vehicleId && !localDriver
            && state.OwnerPlayerId == (remoteOwner == VehicleStateStreamPolicy.NoOwner ? (byte)0 : remoteOwner);

        public static bool CanPresentParked(VehicleCondition? state, uint vehicleId, bool localDriver, byte remoteOwner) =>
            remoteOwner == VehicleStateStreamPolicy.NoOwner && !localDriver && IsValid(state) && state!.VehicleId == vehicleId;

        public static bool TryGetObserverPressure(VehicleCondition? state, uint vehicleId, bool localDriver, byte remoteOwner, out byte pressure)
        {
            pressure = 0;
            if (!CanPresent(state, vehicleId, localDriver, remoteOwner) || !state!.HasPressure) return false;
            pressure = state!.TirePressure;
            return true;
        }

        public static bool TryGetParkedObserverPressure(VehicleCondition? state, uint vehicleId, bool localDriver, byte remoteOwner, out byte pressure)
        {
            pressure = 0;
            if (!CanPresentParked(state, vehicleId, localDriver, remoteOwner) || !state!.HasPressure) return false;
            pressure = state!.TirePressure;
            return true;
        }

        public static bool TryGetObserverDrivetrainDamage(VehicleCondition? state, uint vehicleId, bool localDriver,
            byte remoteOwner, out byte damage)
        {
            damage = 0;
            if (!CanPresent(state, vehicleId, localDriver, remoteOwner) || !state!.HasDrivetrain) return false;
            damage = state!.DrivetrainDamage;
            return true;
        }

        public static bool TryGetParkedObserverDrivetrainDamage(VehicleCondition? state, uint vehicleId, bool localDriver,
            byte remoteOwner, out byte damage)
        {
            damage = 0;
            if (!CanPresentParked(state, vehicleId, localDriver, remoteOwner) || !state!.HasDrivetrain) return false;
            damage = state!.DrivetrainDamage;
            return true;
        }

        public static VehicleCondition? CaptureReleasedCondition(VehicleCondition? accepted, byte previousOwner, ItemTransform? release)
        {
            // Final poses omit FlagVehicle; the registered vehicle and accepted
            // condition identify the car, not the live-stream flag.
            if (release == null || !release.IsFinal || release.IsDriver
                || previousOwner == VehicleStateStreamPolicy.NoOwner || previousOwner != release.OwnerPlayerId
                || !IsValid(accepted) || accepted!.VehicleId != release.ItemId || accepted.OwnerPlayerId != previousOwner
                || accepted.Sequence == VehicleCondition.SnapshotSequence && previousOwner != 0) return null;
            return Copy(accepted);
        }

        public static bool TryGetParkedObserverHealth(VehicleCondition? parked, uint vehicleId, bool localDriver,
            byte remoteOwner, int wheel, out byte health)
        {
            health = 0;
            if (wheel < 0 || wheel > 3 || !CanPresentParked(parked, vehicleId, localDriver, remoteOwner) || !parked!.HasWheel(wheel)) return false;
            health = WheelHealth(parked!, wheel);
            return true;
        }

        private static byte WheelHealth(VehicleCondition state, int wheel)
        {
            switch (wheel)
            {
                case 0: return state.HealthFL;
                case 1: return state.HealthFR;
                case 2: return state.HealthRL;
                default: return state.HealthRR;
            }
        }

        public static VehicleCondition Copy(VehicleCondition state) => new VehicleCondition {
            VehicleId = state.VehicleId, OwnerPlayerId = state.OwnerPlayerId, Sequence = state.Sequence,
            TirePressure = state.TirePressure, DrivetrainDamage = state.DrivetrainDamage, Flags = state.Flags, Availability = state.Availability,
            HealthFL = state.HealthFL, HealthFR = state.HealthFR, HealthRL = state.HealthRL, HealthRR = state.HealthRR };
    }
}
