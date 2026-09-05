using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Small host-owned world scalars that re-roll or progress per-client
    /// (COVERAGE-ROADMAP R1.5 scrap price, R1.7 player keys, and the rate half of R1.1):
    /// <c>Systems/ScrapMetalPrice :: Logic</c> re-rolls the daily price per-client,
    /// <c>Systems/BankAccount :: InterestRate</c> re-rolls the prime rate, and
    /// <c>Database/Keys :: PlayerKeys</c> carries progression (UncleStage / GIFU key /
    /// Conline number). The <b>host</b> owns all three and broadcasts on change + join;
    /// guests write the values back. Guests' own daily re-rolls get stomped on the next
    /// tick — convergent by construction. A host-scalar-state broadcaster like
    /// <see cref="TaxiJobSync"/>.
    /// </summary>
    internal sealed class WorldScalarsSync
    {
        private const string ScrapPath = "Systems/ScrapMetalPrice";
        private const string BankPath = "Systems/BankAccount";
        private const string KeysPath = "Database/Keys";
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 3f;
        private const float KeepAliveSeconds = 30f;

        private FsmFloat? _scrapPrice;
        private FsmFloat? _scrapChange;
        private FsmFloat? _primeInterest;
        private FsmInt? _uncleStage;
        private FsmInt? _conline;
        private FsmBool? _gifu;
        private bool _loggedFound;

        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;
        private ushort _outSequence;
        private ushort _lastRemoteSequence;
        private bool _hasLast;
        private int _lastPrice;
        private int _lastInterest;
        private int _lastConline;
        private int _lastStage;
        private byte _lastFlags;

        // ALL three sources must bind before broadcasting: they're always-active Systems
        // objects that bind in the same probe, and a partial bind would broadcast
        // UncleStage=0/Gifu=false over a guest's real progression (0/false are valid
        // values, so no per-field guard can tell "unbound" from "reset").
        private bool Ready => _scrapPrice != null && _primeInterest != null && _uncleStage != null;

        public void Clear()
        {
            _scrapPrice = _scrapChange = _primeInterest = null;
            _uncleStage = _conline = null;
            _gifu = null;
            _loggedFound = false;
            _nextProbeAt = _nextHostTickAt = _nextKeepAliveAt = 0f;
            _outSequence = _lastRemoteSequence = 0;
            _hasLast = false;
        }

        public void Update(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            if (Time.unscaledTime >= _nextProbeAt) { _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds; Locate(); }
            if (!session.IsHost || !Ready) return;

            if (Time.unscaledTime < _nextHostTickAt) return;
            _nextHostTickAt = Time.unscaledTime + HostTickSeconds;
            bool keepAlive = Time.unscaledTime >= _nextKeepAliveAt;
            if (keepAlive) _nextKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;
            HostBroadcastIfChanged(session, keepAlive);
        }

        public WorldScalarsState? BuildSnapshot()
        {
            Locate();
            return Ready ? BuildState() : null;
        }

        public void Apply(WorldScalarsState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            Locate();
            if (!Ready) return;

            ushort diff = (ushort)(message.Sequence - _lastRemoteSequence);
            if (_lastRemoteSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            _lastRemoteSequence = message.Sequence;

            try
            {
                if (_scrapPrice != null && message.ScrapPriceMKkg > 0f) _scrapPrice.Value = message.ScrapPriceMKkg;
                if (_scrapChange != null) _scrapChange.Value = message.ScrapChange;
                if (_primeInterest != null && message.PrimeInterest > 0f) _primeInterest.Value = message.PrimeInterest;
                if (_uncleStage != null) _uncleStage.Value = message.UncleStage;
                if (_conline != null && message.ConlineNumber != 0) _conline.Value = message.ConlineNumber;
                if (_gifu != null) _gifu.Value = message.GifuKey;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("WorldScalarsSync: apply failed: " + e.Message);
            }
        }

        private void HostBroadcastIfChanged(SessionManager session, bool keepAlive)
        {
            var state = BuildState();
            if (state == null) return;
            bool changed = !_hasLast
                || _lastPrice != Mathf.RoundToInt(state.ScrapPriceMKkg * 100f)
                || _lastInterest != Mathf.RoundToInt(state.PrimeInterest * 100f)
                || _lastConline != state.ConlineNumber
                || _lastStage != state.UncleStage
                || _lastFlags != state.Flags;
            if (!changed && !keepAlive) return;
            _hasLast = true;
            _lastPrice = Mathf.RoundToInt(state.ScrapPriceMKkg * 100f);
            _lastInterest = Mathf.RoundToInt(state.PrimeInterest * 100f);
            _lastConline = state.ConlineNumber;
            _lastStage = state.UncleStage;
            _lastFlags = state.Flags;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        private WorldScalarsState? BuildState()
        {
            if (!Ready) return null;
            byte flags = 0;
            if (_gifu != null && _gifu.Value) flags |= WorldScalarsState.FlagGifuKey;
            return new WorldScalarsState
            {
                Sequence = ++_outSequence,
                ScrapPriceMKkg = _scrapPrice != null ? _scrapPrice.Value : 0f,
                ScrapChange = _scrapChange != null ? _scrapChange.Value : 0f,
                PrimeInterest = _primeInterest != null ? _primeInterest.Value : 0f,
                UncleStage = (byte)Mathf.Clamp(_uncleStage != null ? _uncleStage.Value : 0, 0, 255),
                ConlineNumber = _conline != null ? _conline.Value : 0,
                Flags = flags,
            };
        }

        private void Locate()
        {
            if (_scrapPrice == null) BindScrap();
            if (_primeInterest == null) BindInterest();
            if (_uncleStage == null) BindKeys();

            if (!_loggedFound && Ready)
            {
                _loggedFound = true;
                WinterMPPlugin.Log.LogInfo("WorldScalarsSync: located "
                    + (_scrapPrice != null ? "scrap " : string.Empty)
                    + (_primeInterest != null ? "interest " : string.Empty)
                    + (_uncleStage != null ? "keys" : string.Empty) + ".");
            }
        }

        private void BindScrap()
        {
            var fsm = FindFsm(ScrapPath, "Logic");
            if (fsm == null) return;
            _scrapPrice = fsm.FsmVariables.FindFsmFloat("ScrapPriceMKkg");
            _scrapChange = fsm.FsmVariables.FindFsmFloat("Change");
        }

        private void BindInterest()
        {
            var fsm = FindFsm(BankPath, "InterestRate");
            if (fsm == null) return;
            _primeInterest = fsm.FsmVariables.FindFsmFloat("PrimeInterest");
        }

        private void BindKeys()
        {
            var fsm = FindFsm(KeysPath, "PlayerKeys");
            if (fsm == null) return;
            _uncleStage = fsm.FsmVariables.FindFsmInt("UncleStage");
            _conline = fsm.FsmVariables.FindFsmInt("ConlineNMBRint");
            _gifu = fsm.FsmVariables.FindFsmBool("Gifu");
        }

        private static PlayMakerFSM? FindFsm(string path, string fsmName)
        {
            GameObject? go;
            try { go = GameObject.Find(path); }
            catch { return null; }
            if (go == null) return null;
            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
                if (fsm != null && fsm.FsmName == fsmName) return fsm;
            return null;
        }
    }
}
