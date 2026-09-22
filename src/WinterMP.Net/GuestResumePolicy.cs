namespace WinterMP.Net
{
    /// <summary>Protects a returning profile until its one join offer has been applied.</summary>
    public sealed class GuestResumePolicy
    {
        private enum Phase { AwaitingOffer, Choosing, Relocating, Ready, Leaving }
        private Phase _phase;

        public void Reset() => _phase = Phase.AwaitingOffer;

        public void LeaveWorld() => _phase = Phase.Leaving;

        public bool ReceiveOffer(bool returning)
        {
            if (_phase != Phase.AwaitingOffer) return false;
            _phase = returning ? Phase.Choosing : Phase.Relocating;
            return true;
        }

        public void Choose()
        {
            if (_phase == Phase.Choosing) _phase = Phase.Relocating;
        }

        public void CompleteRelocation()
        {
            if (_phase == Phase.Relocating) _phase = Phase.Ready;
        }

        public bool CanPublish(bool inGame) => inGame && _phase == Phase.Ready;
    }
}
