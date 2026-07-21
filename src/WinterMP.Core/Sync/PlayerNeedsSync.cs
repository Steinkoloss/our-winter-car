using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
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
        }

        public void Locate(bool force = false)
        {
            if (!force && Time.unscaledTime < _nextProbeAt) return;
            _nextProbeAt = Time.unscaledTime + 5f;

            // BodyTemp and Drunk live on local PLAYER FSMs, which can initialize after
            // the need globals. Keep probing for them even once hunger/fatigue are resolved.
            if (_bodyTemp == null)
                _bodyTemp = FindLocalFloat("PLAYER/BodyTemp", "Calculations", "Temperature");
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
                BodyTemp = Read(_bodyTemp),
                Stress = Read(_stress),
                Drunk = Read(_drunk),
                Sequence = ++_sequence,
                Dirtiness = Read(_dirtiness),
                HasDirtiness = _dirtiness != null,
                PlayerAlco = Read(_alco),
                HasAlco = _alco != null,
            };
        }

        public GuestProfileStore.NeedsSnapshot ReadSnapshot()
        {
            Locate();
            if (!Ready)
                return default(GuestProfileStore.NeedsSnapshot);

            return new GuestProfileStore.NeedsSnapshot
            {
                Hunger = Read(_hunger),
                Fatigue = Read(_fatigue),
                Thirst = Read(_thirst),
                Urine = Read(_urine),
                BodyTemp = Read(_bodyTemp),
                Stress = Read(_stress),
                Drunk = Read(_drunk),
                Dirtiness = Read(_dirtiness),
                HasDirtiness = _dirtiness != null,
                PlayerAlco = Read(_alco),
                HasAlco = _alco != null,
                Valid = true,
            };
        }

        public void ApplySnapshot(GuestProfileStore.NeedsSnapshot needs)
        {
            if (!needs.Valid) return;

            Locate(force: true);
            Write(_hunger, needs.Hunger);
            Write(_fatigue, needs.Fatigue);
            Write(_thirst, needs.Thirst);
            Write(_urine, needs.Urine);
            // 0 means "absent" for BodyTemp (pre-v28 sidecar rows) — writing it would
            // restore the guest at freezing. Stress/Drunk 0 are honest defaults (calm,
            // sober) so those always apply.
            if (needs.BodyTemp != 0f)
                Write(_bodyTemp, needs.BodyTemp);
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

        // BodyTemp is a LOCAL fsm var on the PLAYER/BodyTemp "Calculations" FSM, so it must be
        // path-located rather than read from GlobalVariables. Match by FsmName: today the object
        // carries only "Calculations", but a game patch could add a second FSM and a bare
        // GetComponent would then grab an arbitrary one and silently read 0 (ClothingSync reads
        // ClothingStage off this same object and matches by name for exactly this reason). Null
        // returns and the try/catch keep it safe while the object/component is mid-teardown.
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

        private static void Write(HutongGames.PlayMaker.FsmFloat? variable, float value)
        {
            if (variable != null)
                variable.Value = value;
        }
    }
}
