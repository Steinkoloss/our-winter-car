using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class GearboxOilUsePolicy
    {
        // A mod abuse budget, not a claimed native shift-frequency limit.
        public const float MaximumBurst = 4f, StepsPerSecond = 4f;
        private readonly VehicleCallbackPolicy _callbacks = new VehicleCallbackPolicy(MaximumBurst, StepsPerSecond);
        public bool Receive(GearboxOilUseRequest request, byte authenticatedPlayer, byte owner, bool localDriver, float now)
            => request != null && request.Valid && _callbacks.Receive(request.VehicleId, request.PlayerId, request.Sequence,
                authenticatedPlayer, owner, localDriver, now);
        public void ForgetPlayer(byte playerId) => _callbacks.ForgetPlayer(playerId);
        public void Clear() => _callbacks.Clear();
    }
}
