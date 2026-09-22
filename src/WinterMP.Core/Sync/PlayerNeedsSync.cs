using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Reads local PLAYER need globals and reports them to the host (PLAN.md §4.4).
    /// Guests simulate needs locally; the host keeps the sidecar for reconnect.
    /// </summary>
    internal sealed class PlayerNeedsSync
    {
        private const float ReportIntervalSeconds = 12f;

        private HutongGames.PlayMaker.FsmFloat? _hunger;
        private HutongGames.PlayMaker.FsmFloat? _fatigue;
        private HutongGames.PlayMaker.FsmFloat? _thirst;
        private HutongGames.PlayMaker.FsmFloat? _urine;
        private HutongGames.PlayMaker.FsmFloat? _bodyTemp;
        private HutongGames.PlayMaker.FsmFloat? _stress;
        private HutongGames.PlayMaker.FsmFloat? _drunk;
        private HutongGames.PlayMaker.FsmFloat? _dirtiness;
        private HutongGames.PlayMaker.FsmFloat? _alco;
        private float _nextProbeAt;
        private float _nextReportAt;
        private ushort _sequence;
        private bool _loggedReady;
        private float _pendingDirtiness;
        private bool _hasPendingDirtiness;
        private float _pendingAlco;
        private bool _hasPendingAlco;
        private float _pendingBodyTemp;
        private bool _hasPendingBodyTemp;

        public bool Ready => _hunger != null || _fatigue != null;

        public void Reset()
        {
            _hunger = null;
            _fatigue = null;
            _thirst = null;
            _urine = null;
            _bodyTemp = null;
            _stress = null;
            _drunk = null;
            _dirtiness = null;
            _alco = null;
            _nextProbeAt = 0f;
            _nextReportAt = 0f;
            _sequence = 0;
            _loggedReady = false;
            _pendingDirtiness = 0f;
            _hasPendingDirtiness = false;
            _pendingAlco = 0f;
            _hasPendingAlco = false;
            _pendingBodyTemp = 0f;
            _hasPendingBodyTemp = false;
        }

        public void Locate(bool force = false)
        {
            if (!force && Time.unscaledTime < _nextProbeAt) return;
            _nextProbeAt = Time.unscaledTime + 5f;

            // Calculations.Temperature is the current air/heat-source input.
            // PlayerTemp is the native body's accumulated warmth, including zero.
            _bodyTemp = FindGlobalFloat("PlayerTemp");
            if (_drunk == null)
                _drunk = FindLocalFloat("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera", "Drunk Mode", "DrunkCurrent");
            if (_stress == null)
                _stress = FindGlobalFloat("PlayerStress", "Stress");
            if (_dirtiness == null)
                _dirtiness = FindGlobalFloat("PlayerDirtiness");
            if (_alco == null)
                _alco = FindGlobalFloat("PlayerAlco", "Alco");

            if (_hasPendingDirtiness && _dirtiness != null)
            {
                Write(_dirtiness, _pendingDirtiness);
                _hasPendingDirtiness = false;
                WinterMPPlugin.Log.LogInfo("PlayerNeedsSync: restored pending guest dirtiness from host profile.");
            }
            if (_hasPendingAlco && _alco != null)
            {
                Write(_alco, _pendingAlco);
                _hasPendingAlco = false;
                WinterMPPlugin.Log.LogInfo("PlayerNeedsSync: restored pending guest BAC from host profile.");
            }
            if (_hasPendingBodyTemp && _bodyTemp != null)
            {
                Write(_bodyTemp, _pendingBodyTemp);
                _hasPendingBodyTemp = false;
                WinterMPPlugin.Log.LogInfo("PlayerNeedsSync: restored pending guest body warmth from host profile.");
            }

            if (_hunger != null && _fatigue != null) return;

            _hunger = FindGlobalFloat("PlayerHunger", "Hunger");
            _fatigue = FindGlobalFloat("PlayerFatigue", "Fatigue", "Tiredness");
            _thirst = FindGlobalFloat("PlayerThirst", "Thirst");
            _urine = FindGlobalFloat("PlayerUrine", "Urine");

            if (Ready && !_loggedReady)
            {
                _loggedReady = true;
                WinterMPPlugin.Log.LogInfo("PlayerNeedsSync: found need globals.");
            }
        }

        public void UpdateGuest(SessionManager session)
        {
            if (session.IsHost || session.PlayerCount == 0) return;
            if (Time.unscaledTime < _nextReportAt) return;

            Locate();
            // Report as soon as the core need globals resolve. Dirtiness is best-effort:
            // its report carries HasDirtiness so a late/unresolved PlayerDirtiness never
            // blocks the other seven needs (regression guard) and never lets the host
            // persist a bogus clean value. Do NOT re-add `|| _dirtiness == null` here.
            if (!Ready) return;

            _nextReportAt = Time.unscaledTime + ReportIntervalSeconds;

            var report = BuildReport(session.LocalPlayerId);
            session.SendPlayerProfileMessage(report);
        }

        public PlayerNeedsReport BuildReport(byte playerId)
        {
            Locate();
            return new PlayerNeedsReport
            {
                PlayerId = playerId,
                Hunger = Read(_hunger),
                Fatigue = Read(_fatigue),
                Thirst = Read(_thirst),
                Urine = Read(_urine),
                BodyTemp = HasBodyTemp ? ReadBodyTemp() : 0f,
                HasBodyTemp = HasBodyTemp,
                Stress = Read(_stress),
                Drunk = Read(_drunk),
                Sequence = ++_sequence,
                Dirtiness = Read(_dirtiness),
                HasDirtiness = _dirtiness != null,
                PlayerAlco = Read(_alco),
                HasAlco = _alco != null,
            };
        }

        public WinterMP.Net.Sync.GuestProfile.NeedsSnapshot ReadSnapshot()
        {
            Locate();
            if (!Ready)
                return default(WinterMP.Net.Sync.GuestProfile.NeedsSnapshot);

            return new WinterMP.Net.Sync.GuestProfile.NeedsSnapshot
            {
                Hunger = Read(_hunger),
                Fatigue = Read(_fatigue),
                Thirst = Read(_thirst),
                Urine = Read(_urine),
                BodyTemp = HasBodyTemp ? ReadBodyTemp() : 0f,
                HasBodyTemp = HasBodyTemp,
                Stress = Read(_stress),
                Drunk = Read(_drunk),
                Dirtiness = Read(_dirtiness),
                HasDirtiness = _dirtiness != null,
                PlayerAlco = Read(_alco),
                HasAlco = _alco != null,
                Valid = true,
            };
        }

        public void ApplySnapshot(WinterMP.Net.Sync.GuestProfile.NeedsSnapshot needs)
        {
            if (!needs.Valid) return;

            // A newer host snapshot supersedes any deferred warmth from an older
            // offer, including a legacy profile with no known body warmth.
            _hasPendingBodyTemp = false;
            Locate(force: true);
            Write(_hunger, needs.Hunger);
            Write(_fatigue, needs.Fatigue);
            Write(_thirst, needs.Thirst);
            Write(_urine, needs.Urine);
            if (needs.HasBodyTemp && PlayerWarmthPolicy.Valid(true, needs.BodyTemp))
            {
                if (_bodyTemp != null) Write(_bodyTemp, needs.BodyTemp);
                else { _pendingBodyTemp = needs.BodyTemp; _hasPendingBodyTemp = true; }
            }
            Write(_stress, needs.Stress);
            Write(_drunk, needs.Drunk);
            if (needs.HasDirtiness)
            {
                if (_dirtiness != null)
                    Write(_dirtiness, needs.Dirtiness);
                else
                {
                    _pendingDirtiness = needs.Dirtiness;
                    _hasPendingDirtiness = true;
                }
            }
            if (needs.HasAlco)
            {
                if (_alco != null)
                    Write(_alco, needs.PlayerAlco);
                else
                {
                    _pendingAlco = needs.PlayerAlco;
                    _hasPendingAlco = true;
                }
            }

            WinterMPPlugin.Log.LogInfo(
                "PlayerNeedsSync: restored guest needs from host profile.");
        }

        /// <summary>Guest accepted host sleep — mirror the rested fatigue reset.</summary>
        public void ApplyRestedFromSleep()
        {
            Locate();
            Write(_fatigue, 0f);
            WinterMPPlugin.Log.LogInfo("PlayerNeedsSync: guest fatigue reset after sleep consent.");
        }

        private static HutongGames.PlayMaker.FsmFloat? FindGlobalFloat(params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                var variable = FsmVariables.GlobalVariables.FindFsmFloat(names[i]);
                if (variable != null) return variable;
            }

            return null;
        }

        // Match by FSM name so another component added in a game update cannot
        // silently replace the local DrunkCurrent source.
        private static HutongGames.PlayMaker.FsmFloat? FindLocalFloat(string scenePath, string fsmName, string varName)
        {
            try
            {
                var go = GameObject.Find(scenePath);
                if (go == null) return null;

                var fsms = go.GetComponents<PlayMakerFSM>();
                if (fsms == null) return null;

                foreach (var fsm in fsms)
                {
                    if (fsm == null || fsm.FsmName != fsmName) continue;
                    var variable = fsm.FsmVariables.FindFsmFloat(varName);
                    if (variable != null) return variable;
                }

                return null;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug(
                    "PlayerNeedsSync: local float '" + varName + "' on '" + fsmName + "' at '" + scenePath + "' failed: " + e.Message);
                return null;
            }
        }

        private static float Read(HutongGames.PlayMaker.FsmFloat? variable) =>
            variable != null ? variable.Value : 0f;

        // Until the native global appears, retain the host's pending value in
        // reports so a reconnect during initialization cannot erase known warmth.
        private bool HasBodyTemp => _hasPendingBodyTemp
            || _bodyTemp != null && PlayerWarmthPolicy.Valid(true, _bodyTemp.Value);
        private float ReadBodyTemp() => _hasPendingBodyTemp ? _pendingBodyTemp : Read(_bodyTemp);

        private static void Write(HutongGames.PlayMaker.FsmFloat? variable, float value)
        {
            if (variable != null)
                variable.Value = value;
        }
    }
}
