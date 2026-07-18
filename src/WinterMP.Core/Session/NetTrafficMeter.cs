using System.Globalization;
using WinterMP.Net;

namespace WinterMP.Core.Session
{
    /// <summary>
    /// M7 bandwidth-budget meter (PLAN §5: &lt; 64 kB/s/client steady). Counts egress
    /// bytes at the single send chokepoint, broken out per channel, plus ingress, and
    /// reports a rolling per-second rate. Pure measurement — it never changes what is
    /// sent; it just feeds the TAB overlay, the periodic log, and the bug-report zip so
    /// the budget can be held honestly instead of guessed at.
    /// </summary>
    public sealed class NetTrafficMeter
    {
        /// <summary>Per-client steady-state target from the M7 exit criteria.</summary>
        public const int BudgetBytesPerSecond = 64 * 1024;

        private const float ReportIntervalSeconds = 10f;

        public static NetTrafficMeter Instance { get; } = new NetTrafficMeter();

        // Channel count is small and fixed (ReliableOrdered/UnreliableSequenced/ReliableBulk).
        private readonly long[] _sentByChannel = new long[3];
        private readonly int[] _rateByChannel = new int[3];
        private long _sentTotal;
        private long _recvTotal;

        private float _windowStart = -999f;
        private long _windowSentBase;
        private long _windowRecvBase;
        private readonly long[] _windowChannelBase = new long[3];

        /// <summary>Total egress across all peers, bytes/sec (last completed window).</summary>
        public int SentBytesPerSec { get; private set; }

        /// <summary>Ingress bytes/sec (last completed window).</summary>
        public int RecvBytesPerSec { get; private set; }

        /// <summary>Egress divided by peer count — the figure the per-client budget gates on.</summary>
        public int PerClientBytesPerSec { get; private set; }

        public void Reset()
        {
            for (int i = 0; i < _sentByChannel.Length; i++)
            {
                _sentByChannel[i] = 0;
                _rateByChannel[i] = 0;
                _windowChannelBase[i] = 0;
            }

            _sentTotal = 0;
            _recvTotal = 0;
            _windowStart = -999f;
            _windowSentBase = 0;
            _windowRecvBase = 0;
            SentBytesPerSec = 0;
            RecvBytesPerSec = 0;
            PerClientBytesPerSec = 0;
        }

        public void RecordSent(Channel channel, int bytes)
        {
            _sentTotal += bytes;
            int idx = (int)channel;
            if (idx >= 0 && idx < _sentByChannel.Length)
                _sentByChannel[idx] += bytes;
        }

        public void RecordReceived(int bytes) => _recvTotal += bytes;

        /// <summary>Channel rate (bytes/sec) for the TAB overlay; 0 = reliable, 1 = unreliable, 2 = bulk.</summary>
        public int RateForChannel(Channel channel)
        {
            int idx = (int)channel;
            return idx >= 0 && idx < _rateByChannel.Length ? _rateByChannel[idx] : 0;
        }

        /// <summary>Recompute rates every <see cref="ReportIntervalSeconds"/>; logs a line and warns when over budget.</summary>
        public void Tick(float now, int peerCount)
        {
            if (_windowStart < 0f)
            {
                StartWindow(now);
                return;
            }

            float dt = now - _windowStart;
            if (dt < ReportIntervalSeconds) return;

            SentBytesPerSec = (int)((_sentTotal - _windowSentBase) / dt);
            RecvBytesPerSec = (int)((_recvTotal - _windowRecvBase) / dt);
            for (int i = 0; i < _rateByChannel.Length; i++)
                _rateByChannel[i] = (int)((_sentByChannel[i] - _windowChannelBase[i]) / dt);

            int clients = peerCount > 0 ? peerCount : 1;
            PerClientBytesPerSec = SentBytesPerSec / clients;

            if (peerCount > 0)
            {
                string line = string.Format(
                    CultureInfo.InvariantCulture,
                    "NetTraffic: out {0:0.0} kB/s ({1:0.0}/client), in {2:0.0} kB/s — reliable {3:0.0}, unreliable {4:0.0}, bulk {5:0.0} kB/s ({6} peer(s)).",
                    SentBytesPerSec / 1024f, PerClientBytesPerSec / 1024f, RecvBytesPerSec / 1024f,
                    _rateByChannel[0] / 1024f, _rateByChannel[1] / 1024f, _rateByChannel[2] / 1024f, peerCount);

                if (PerClientBytesPerSec > BudgetBytesPerSecond)
                    WinterMPPlugin.Log.LogWarning(line + $" OVER BUDGET ({BudgetBytesPerSecond / 1024} kB/s).");
                else
                    WinterMPPlugin.Log.LogInfo(line);
            }

            StartWindow(now);
        }

        /// <summary>One-line summary for the TAB overlay / bug report.</summary>
        public string Summary() => string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.0} kB/s out ({1:0.0}/client) · {2:0.0} kB/s in",
            SentBytesPerSec / 1024f, PerClientBytesPerSec / 1024f, RecvBytesPerSec / 1024f);

        private void StartWindow(float now)
        {
            _windowStart = now;
            _windowSentBase = _sentTotal;
            _windowRecvBase = _recvTotal;
            for (int i = 0; i < _sentByChannel.Length; i++)
                _windowChannelBase[i] = _sentByChannel[i];
        }
    }
}
