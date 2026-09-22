namespace WinterMP.Net
{
    public enum JoinAttemptStage { None, Connecting, AwaitingHandshake }

    /// <summary>Local join deadlines; transport keepalives are not mod admission.</summary>
    public sealed class JoinAttempt
    {
        public const double ConnectionTimeoutSeconds = 60;
        public const double HandshakeTimeoutSeconds = 60;

        public JoinAttemptStage Stage { get; private set; }
        private double _deadline;

        public void Begin(double now)
        {
            Stage = JoinAttemptStage.Connecting;
            _deadline = now + ConnectionTimeoutSeconds;
        }

        public void TransportConnected(double now)
        {
            // A duplicate transport callback must not extend an unanswered handshake.
            if (Stage != JoinAttemptStage.Connecting) return;
            Stage = JoinAttemptStage.AwaitingHandshake;
            _deadline = now + HandshakeTimeoutSeconds;
        }

        public bool HasTimedOut(double now) => Stage != JoinAttemptStage.None && now >= _deadline;

        public void Clear()
        {
            Stage = JoinAttemptStage.None;
            _deadline = 0;
        }
    }
}
