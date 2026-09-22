using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net
{
    /// <summary>Climate follows the host's established vehicle ownership, with independent sender histories.</summary>
    public sealed class VehicleClimateStreamPolicy
    {
        private readonly Dictionary<uint, Dictionary<byte, ushort>> _sequences = new Dictionary<uint, Dictionary<byte, ushort>>();
        private const byte KnownFlags = VehicleClimate.FlagWindowHeater | VehicleClimate.FlagGlassDefrosting
            | VehicleClimate.FlagPlayerIn | VehicleClimate.FlagCabinTemperature;

        public static bool IsValid(VehicleClimate? message) => message != null && message.VehicleId != 0
            && message.OwnerPlayerId != VehicleStateStreamPolicy.NoOwner && (message.Flags & ~KnownFlags) == 0 && message.ValidIce && message.ValidParkingBrake;

        public static bool CanPresent(byte source, bool receiverIsHost, bool localDriver, byte remoteOwner)
        {
            if (localDriver || source == VehicleStateStreamPolicy.NoOwner) return false;
            return receiverIsHost ? source != 0 && source == remoteOwner
                : source == (remoteOwner == VehicleStateStreamPolicy.NoOwner ? (byte)0 : remoteOwner);
        }

        public bool Receive(VehicleClimate? message, bool receiverIsHost, bool localDriver, byte remoteOwner)
        {
            if (!IsValid(message) || localDriver) return false;
            var state = message!;
            // Only the authenticated host sends snapshots; an active guest source keeps its live stream.
            if (state.Sequence == VehicleClimate.SnapshotSequence)
                return !receiverIsHost && state.OwnerPlayerId == 0 && (remoteOwner == VehicleStateStreamPolicy.NoOwner || remoteOwner == 0);
            if (!CanPresent(state.OwnerPlayerId, receiverIsHost, localDriver, remoteOwner)) return false;
            if (!_sequences.TryGetValue(state.VehicleId, out var senders))
            {
                senders = new Dictionary<byte, ushort>(); _sequences.Add(state.VehicleId, senders);
            }
            if (senders.TryGetValue(state.OwnerPlayerId, out ushort previous))
            {
                ushort diff = unchecked((ushort)(state.Sequence - previous));
                if (diff == 0 || diff > short.MaxValue) return false;
            }
            senders[state.OwnerPlayerId] = state.Sequence;
            return true;
        }

        public void ForgetPlayer(byte playerId) { foreach (var senders in _sequences.Values) senders.Remove(playerId); }
        public void Clear() => _sequences.Clear();

        public static VehicleClimate Copy(VehicleClimate state) => new VehicleClimate {
            VehicleId = state.VehicleId, OwnerPlayerId = state.OwnerPlayerId, Sequence = state.Sequence,
            Frost = state.Frost, Flags = state.Flags, HeaterTemp = state.HeaterTemp, HeaterBlower = state.HeaterBlower,
            HeaterDirection = state.HeaterDirection, Fog = state.Fog, CabinTemp = state.CabinTemp,
            Ice = state.Ice, IceSideLeft = state.IceSideLeft, IceSideRight = state.IceSideRight,
            IceDoorLeft = state.IceDoorLeft, IceDoorRight = state.IceDoorRight, IceRear = state.IceRear, IceMask = state.IceMask,
            ParkingBrakeAvailable = state.ParkingBrakeAvailable, ParkingBrake = state.ParkingBrake };
    }
}
