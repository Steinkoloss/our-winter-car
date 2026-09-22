using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class TaxiServicePolicy
    {
        public static bool CanDial(TaxiServiceState state) => Valid(state) && state.CallPhase == TaxiCallPhase.Silent
            && state.CallOwner == TaxiServiceState.Nobody
            && (state.Flags & (TaxiServiceState.Car | TaxiServiceState.Phone)) == (TaxiServiceState.Car | TaxiServiceState.Phone);
        public static bool Valid(TaxiServiceState s) => s != null && (s.Flags & ~255u) == 0 && (s.ColliderFlags & ~7) == 0
            && (byte)s.CallPhase <= 3 && (s.CallPhase == TaxiCallPhase.Silent || s.CallId != 0)
            && (s.CallOwner == TaxiServiceState.Nobody || s.CallId != 0)
            && Pose(s.CustomerPosition, s.CustomerRotation) && Pose(s.WalkerPosition, s.WalkerRotation)
            && Text(s.Pickup, 128) && Text(s.Destination, 128) && Text(s.IndicatorText, 512) && Text(s.Subtitle, 512)
            && Text(s.Voice, 80) && Text(s.RootClip, 80) && Text(s.SkeletonClip, 80)
            && s.RootTime >= 0 && s.RootTime <= 1000000 && s.SkeletonTime >= 0 && s.SkeletonTime <= 1000000
            && ValidLuggage(s) && ValidPayday(s);
        private static bool ValidPayday(TaxiServiceState s)
        {
            if (s.PaydayId == 0 || (s.PaydayFlags & ~3) != 0 || s.PaydayRundown == null || s.PaydayRundown.Length != 8) return false;
            foreach (float value in s.PaydayRundown) if (float.IsNaN(value) || float.IsInfinity(value)) return false;
            return true;
        }
        public static bool CanReadPayday(TaxiServiceState state, TaxiPaydayReadIntent intent, byte actor, bool nearLetter)
            => Valid(state) && intent != null && actor != TaxiServiceState.Nobody && actor == intent.PlayerId
                && intent.PaydayId == state.PaydayId && nearLetter
                && (state.PaydayFlags & 3) == 3;
        public static uint LuggageItemId(uint epoch, int slot)
        {
            if (epoch == 0 || slot < 0 || slot >= 5) throw new ArgumentOutOfRangeException();
            return StableHash.Fnv1a32("taxi:luggage:" + epoch.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ":" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        public static int BoundLuggageCount(int requested, int available)
        {
            if (available < 0 || available > 5) throw new ArgumentOutOfRangeException(nameof(available));
            return Math.Max(0, Math.Min(requested, available));
        }
        private static bool ValidLuggage(TaxiServiceState s)
        {
            if (s.LuggageEpoch == 0 || (s.LuggageMask & ~31) != 0 || s.LuggagePositions == null || s.LuggagePositions.Length != 5
                || s.LuggageRotations == null || s.LuggageRotations.Length != 5) return false;
            for (int i = 0; i < 5; i++) if (!Pose(s.LuggagePositions[i], s.LuggageRotations[i])) return false;
            return true;
        }
        public static bool Newer(uint previous, uint next) => unchecked(next - previous) != 0 && unchecked(next - previous) < 0x80000000u;
        public static bool CanAct(TaxiServiceState state, TaxiCallIntent intent, byte actor, bool nearPhone)
        {
            if (!Valid(state) || intent == null || actor == TaxiServiceState.Nobody || actor != intent.PlayerId
                || intent.CallId == 0 || intent.CallId != state.CallId) return false;
            if (intent.Action == TaxiCallAction.HangUp) return state.CallOwner == actor;
            return intent.Action == TaxiCallAction.Answer && nearPhone && state.CallOwner == TaxiServiceState.Nobody
                && state.CallPhase == TaxiCallPhase.Ringing && (state.Flags & (TaxiServiceState.Car | TaxiServiceState.Phone))
                    == (TaxiServiceState.Car | TaxiServiceState.Phone);
        }
        private static bool Text(string s, int max) => s != null && s.Length <= max && s.IndexOf('\0') < 0;
        private static bool Pose(NetVector3 p, NetQuaternion q)
        {
            float norm = q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
            return Math.Abs(p.X) <= 100000 && Math.Abs(p.Y) <= 100000 && Math.Abs(p.Z) <= 100000 && norm >= .9f && norm <= 1.1f;
        }
    }
}
