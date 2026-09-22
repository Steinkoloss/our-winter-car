using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class FirewoodBuyerPolicy
    {
        public static bool Valid(FirewoodBuyerState state) => state != null && state.NetId != 0
            && (state.Flags & ~3) == 0 && ((state.Flags & FirewoodBuyerState.OfferReady) != 0
                ? state.Flags == 3 && HostPaymentPolicy.ValidAmount(state.Amount) : state.Amount == 0)
            && ValidPose(state);
        public static bool Same(FirewoodBuyerState a, FirewoodBuyerState b) => a.NetId == b.NetId && a.Flags == b.Flags && a.Amount == b.Amount
            && a.Position.X == b.Position.X && a.Position.Y == b.Position.Y && a.Position.Z == b.Position.Z
            && a.Rotation.X == b.Rotation.X && a.Rotation.Y == b.Rotation.Y && a.Rotation.Z == b.Rotation.Z && a.Rotation.W == b.Rotation.W;
        public static bool CanReceive(FirewoodBuyerState? previous, FirewoodBuyerState next)
        {
            if (!Valid(next)) return false;
            if (previous == null) return true;
            if (previous.NetId != next.NetId) return false;
            uint difference = unchecked(next.Revision - previous.Revision);
            return difference == 0 ? Same(previous, next) : difference < 0x80000000u;
        }
        public static FirewoodBuyerState Copy(FirewoodBuyerState state) => new FirewoodBuyerState {
            NetId = state.NetId, Revision = state.Revision, Flags = state.Flags, Amount = state.Amount, Position = state.Position, Rotation = state.Rotation };
        private static bool ValidPose(FirewoodBuyerState state)
        {
            float norm = state.Rotation.X * state.Rotation.X + state.Rotation.Y * state.Rotation.Y
                + state.Rotation.Z * state.Rotation.Z + state.Rotation.W * state.Rotation.W;
            return System.Math.Abs(state.Position.X) <= 100000 && System.Math.Abs(state.Position.Y) <= 100000
                && System.Math.Abs(state.Position.Z) <= 100000 && norm >= .9f && norm <= 1.1f;
        }
        public static float IncludeGuestDistance(float nativeDistance, float guestDistance, bool dead, float age) =>
            !dead && age >= 0 && age <= 2 && guestDistance >= 0 && guestDistance < nativeDistance ? guestDistance : nativeDistance;
    }
}
