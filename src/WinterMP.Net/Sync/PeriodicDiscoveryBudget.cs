namespace WinterMP.Net.Sync
{
    /// <summary>Admits one due routine scan per frame. Initial discovery and
    /// explicit refreshes stay immediate, while reserving that frame's slot.</summary>
    public sealed class PeriodicDiscoveryBudget
    {
        private int _lastFrame;
        private bool _used;

        public bool TryBegin(int frame, float now, ref float nextAt, float interval, bool force = false)
        {
            if (!force && now < nextAt) return false;
            if (!force && nextAt > 0 && _used && _lastFrame == frame) return false;
            _used = true;
            _lastFrame = frame;
            nextAt = now + interval;
            return true;
        }
    }
}
