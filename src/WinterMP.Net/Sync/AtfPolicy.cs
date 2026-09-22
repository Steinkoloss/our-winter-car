using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class AtfIntentLedger
    {
        private readonly Dictionary<byte, ushort> _sequences = new Dictionary<byte, ushort>();
        public bool Accept(AtfRefillIntent request, bool geometryValid)
        {
            if (!AtfPolicy.Valid(request)) return false;
            if (_sequences.TryGetValue(request.PlayerId, out ushort old))
            {
                ushort delta = unchecked((ushort)(request.Sequence - old));
                if (delta == 0 || delta >= 0x8000) return false;
            }
            // A rejected remote attempt cannot become valid later by replaying it
            // beside the car, or refresh the host's pour lease without new input.
            _sequences[request.PlayerId] = request.Sequence;
            return geometryValid;
        }
        public void Forget(byte player) { _sequences.Remove(player); }
        public void Clear() { _sequences.Clear(); }
    }

    public static class AtfPolicy
    {
        public const int MaximumNativeIdLength = 64;
        public const string NativePrefix = "atfoil0";
        public const float Capacity = 6.3f, MaximumBottleFluid = 1f, EmptyThreshold = .1f;
        public const float LeaseSeconds = .6f, MaximumTickSeconds = .25f, TransferPerSecond = .1f;
        public const float CapMinimum = 1f, CapMaximum = 359f, CapStep = 33f;
        public const float CapLocalPositionLimit = 5f, CapPositionTolerance = .001f;
        // cos(0.1 degrees / 2)^2; squared normalized dot treats q and -q alike.
        private const double CapRotationDotSquaredMinimum = .999999238456644;

        public static bool Valid(AtfBottleState s)
        {
            if (s == null || s.ItemId == 0 || s.Revision == 0 || s.NativeId == null
                || s.NativeId.Length > MaximumNativeIdLength || !FactoryItemIdentity.IsNativeId(s.NativeId, NativePrefix)
                || !ValidFluid(s.Fluid) || (s.Empty && s.Fluid > EmptyThreshold)) return false;
            var p = s.Position;
            return Finite(p.X) && Finite(p.Y) && Finite(p.Z) && ValidRotation(s.Rotation);
        }
        public static bool Valid(AtfFillerState s) => s != null && s.VehicleId != 0 && s.Revision != 0
            && ValidCap(s.Rotation) && ValidOilLevel(s.OilLevel) && (s.Flags & ~AtfFillerState.FlagAvailable) == 0
            && LocalCoordinate(s.CapLocalPosition.X) && LocalCoordinate(s.CapLocalPosition.Y) && LocalCoordinate(s.CapLocalPosition.Z)
            && ValidRotation(s.CapLocalRotation);
        public static bool Valid(AtfRefillIntent s) => s != null && s.VehicleId != 0 && s.PlayerId > 0 && s.PlayerId < 255
            && s.Action <= AtfRefillIntent.Screw && (s.Action <= AtfRefillIntent.Pour ? s.BottleId != 0 : s.BottleId == 0);
        public static bool ValidFluid(float value) => Finite(value) && value >= 0 && value <= MaximumBottleFluid;
        public static bool ValidOilLevel(float value) => Finite(value) && value >= -1 && value <= Capacity;
        public static bool ValidCap(float value) => Finite(value) && value >= CapMinimum && value <= CapMaximum;
        public static bool CapOpen(float rotation) => ValidCap(rotation) && rotation <= CapMinimum;
        public static float TurnCap(float rotation, bool unscrew)
        {
            if (!ValidCap(rotation)) throw new ArgumentOutOfRangeException(nameof(rotation));
            return Math.Max(CapMinimum, Math.Min(CapMaximum, rotation + (unscrew ? -CapStep : CapStep)));
        }
        public static bool LeaseFresh(float now, float lastAcceptedKeepalive) => Finite(now) && Finite(lastAcceptedKeepalive)
            && lastAcceptedKeepalive >= 0 && now >= lastAcceptedKeepalive && now - lastAcceptedKeepalive <= LeaseSeconds;
        public static float TransferAmount(float source, float target, float dt)
        {
            if (!ValidFluid(source) || !ValidOilLevel(target) || !Finite(dt) || dt <= 0) return 0;
            return Math.Min(source, Math.Min(Math.Max(0, Capacity - target), TransferPerSecond * Math.Min(dt, MaximumTickSeconds)));
        }

        // Movement already has its own stream; an equal revision may refresh the
        // creation pose in a late-join snapshot without changing bottle contents.
        public static bool Same(AtfBottleState a, AtfBottleState b) => a.ItemId == b.ItemId && a.NativeId == b.NativeId
            && a.Fluid == b.Fluid && a.Empty == b.Empty;
        public static bool Same(AtfFillerState a, AtfFillerState b) => a.VehicleId == b.VehicleId && a.Rotation == b.Rotation
            && a.OilLevel == b.OilLevel && a.Flags == b.Flags && SameCapPose(a, b);
        public static bool CanReceive(AtfBottleState? old, AtfBottleState next)
        {
            if (!Valid(next)) return false;
            if (old == null) return true;
            uint delta = unchecked(next.Revision - old.Revision);
            return old.ItemId == next.ItemId && old.NativeId == next.NativeId && (delta == 0 ? Same(old, next) : delta < 0x80000000u);
        }
        public static bool CanReceive(AtfFillerState? old, AtfFillerState next)
        {
            if (!Valid(next)) return false;
            if (old == null) return true;
            uint delta = unchecked(next.Revision - old.Revision);
            return old.VehicleId == next.VehicleId && (delta == 0 ? Same(old, next) : delta < 0x80000000u);
        }
        public static AtfBottleState Copy(AtfBottleState s) => new AtfBottleState { ItemId = s.ItemId, Revision = s.Revision,
            NativeId = s.NativeId, Fluid = s.Fluid, Empty = s.Empty, Position = s.Position, Rotation = s.Rotation };
        public static AtfFillerState Copy(AtfFillerState s) => new AtfFillerState { VehicleId = s.VehicleId, Revision = s.Revision,
            Rotation = s.Rotation, OilLevel = s.OilLevel, Flags = s.Flags,
            CapLocalPosition = s.CapLocalPosition, CapLocalRotation = s.CapLocalRotation };
        private static bool SameCapPose(AtfFillerState a, AtfFillerState b)
        {
            double x = (double)a.CapLocalPosition.X - b.CapLocalPosition.X;
            double y = (double)a.CapLocalPosition.Y - b.CapLocalPosition.Y;
            double z = (double)a.CapLocalPosition.Z - b.CapLocalPosition.Z;
            if (x * x + y * y + z * z > (double)CapPositionTolerance * CapPositionTolerance) return false;
            var p = a.CapLocalRotation; var q = b.CapLocalRotation;
            double norm = RotationNormSquared(p) * RotationNormSquared(q);
            double dot = (double)p.X * q.X + (double)p.Y * q.Y + (double)p.Z * q.Z + (double)p.W * q.W;
            return norm > 0 && dot * dot >= CapRotationDotSquaredMinimum * norm;
        }
        private static bool LocalCoordinate(float value) => Finite(value) && value >= -CapLocalPositionLimit && value <= CapLocalPositionLimit;
        private static bool ValidRotation(NetQuaternion q)
        {
            double norm = RotationNormSquared(q);
            return Finite(q.X) && Finite(q.Y) && Finite(q.Z) && Finite(q.W) && norm >= .9 && norm <= 1.1;
        }
        private static double RotationNormSquared(NetQuaternion q)
            => (double)q.X * q.X + (double)q.Y * q.Y + (double)q.Z * q.Z + (double)q.W * q.W;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
