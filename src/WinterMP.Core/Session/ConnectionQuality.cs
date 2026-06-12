namespace WinterMP.Core.Session
{
    /// <summary>M7 link stats for the TAB overlay — ping, loss, relay hint.</summary>
    public sealed class ConnectionQuality
    {
        private const int LossPauseThresholdPercent = 25;
        private const int SendFailurePauseThreshold = 5;

        public static ConnectionQuality Instance { get; } = new ConnectionQuality();

        public string TransportName { get; set; } = "?";

        /// <summary>True when Steam reports relay use; false when direct; unset when unknown.</summary>
        public bool? UsingRelay { get; private set; }

        private int _unreliableReceived;
        private int _unreliableDropped;
        private int _sendFailures;
        private float _windowStart = -999f;

        public void Reset()
        {
            TransportName = "?";
            UsingRelay = null;
            _unreliableReceived = 0;
            _unreliableDropped = 0;
            _sendFailures = 0;
            _windowStart = -999f;
        }

        public void NoteUnreliableReceived() => _unreliableReceived++;

        public void NoteUnreliableDropped() => _unreliableDropped++;

        public void NoteSendFailure() => _sendFailures++;

        public void SetRelay(bool? usingRelay) => UsingRelay = usingRelay;

        public int LossPercent
        {
            get
            {
                int total = _unreliableReceived + _unreliableDropped;
                if (total == 0) return 0;
                return _unreliableDropped * 100 / total;
            }
        }

        public int SendFailures => _sendFailures;

        /// <summary>Pause item/vehicle ownership hand-offs while the link is flaky (M7).</summary>
        public bool ShouldPauseOwnershipTransfers =>
            LossPercent >= LossPauseThresholdPercent || SendFailures >= SendFailurePauseThreshold;

        public void TickWindow(float now)
        {
            if (_windowStart < 0f)
            {
                _windowStart = now;
                return;
            }

            if (now - _windowStart < 30f) return;

            _unreliableReceived = 0;
            _unreliableDropped = 0;
            _sendFailures = 0;
            _windowStart = now;
        }
    }
}
