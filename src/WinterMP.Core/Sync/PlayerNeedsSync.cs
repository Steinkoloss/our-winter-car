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
        private float _nextProbeAt;
        private float _nextReportAt;
        private ushort _sequence;
        private bool _loggedReady;

        public bool Ready => _hunger != null || _fatigue != null;

        public void Reset()
        {
            _hunger = null;
            _fatigue = null;
            _thirst = null;
            _urine = null;
            _bodyTemp = null;
            _nextProbeAt = 0f;
            _nextReportAt = 0f;
            _sequence = 0;
            _loggedReady = false;
        }

        public void Locate()
        {
            if (Time.unscaledTime < _nextProbeAt) return;
            _nextProbeAt = Time.unscaledTime + 5f;

            // BodyTemp lives on the local PLAYER/BodyTemp FSM, which can initialize after
            // the need globals. Keep probing for it even once hunger/fatigue are resolved.
            if (_bodyTemp == null)
                _bodyTemp = FindLocalFloat("PLAYER/BodyTemp", "Temperature");

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
                Sequence = ++_sequence,
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
                Valid = true,
            };
        }

        public void ApplySnapshot(GuestProfileStore.NeedsSnapshot needs)
        {
            if (!needs.Valid) return;

            Locate();
            Write(_hunger, needs.Hunger);
            Write(_fatigue, needs.Fatigue);
            Write(_thirst, needs.Thirst);
            Write(_urine, needs.Urine);
            Write(_bodyTemp, needs.BodyTemp);

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

        // BodyTemp is a LOCAL fsm var on the PLAYER/BodyTemp FSM, so it must be path-located
        // rather than read from GlobalVariables. GameObject.Find + FindFsmFloat both null-return
        // safely; the try/catch guards against the object/component being mid-teardown.
        private static HutongGames.PlayMaker.FsmFloat? FindLocalFloat(string scenePath, string varName)
        {
            try
            {
                var go = GameObject.Find(scenePath);
                if (go == null) return null;

                var fsm = go.GetComponent<PlayMakerFSM>();
                if (fsm == null) return null;

                return fsm.FsmVariables.FindFsmFloat(varName);
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug(
                    "PlayerNeedsSync: local float '" + varName + "' at '" + scenePath + "' failed: " + e.Message);
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
