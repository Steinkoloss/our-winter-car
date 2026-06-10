using UnityEngine;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Host-authoritative game clock and weather (M3).
    ///
    /// The game's master clock is the SUN Color FSM (MAP/Sun/PivotSun/SUN :: Color):
    /// one state per hour ('1'..'24'), an int variable 'Time' holding the current
    /// hour and a float 'Minutes'. The weather forecast lives on
    /// MAP/WEATHER/Forecast :: Logic (OldTemp/NewTemp/Snowing/Index) and the cloud
    /// cycle on MAP/WEATHER/Clouds :: Weather (hour states '00','1'..'23').
    ///
    /// The host broadcasts <see cref="TimeSync"/> periodically; guests jump their
    /// hour states (via injected MP_* global transitions, same mechanism as doors)
    /// when drift exceeds <see cref="DriftThresholdMinutes"/> and overwrite the
    /// forecast variables. Day-of-week is not synced yet (no verified variable).
    /// </summary>
    internal sealed class TimeWeatherSync
    {
        private const float DriftThresholdMinutes = 10f;

        private PlayMakerFSM? _sunColor;
        private PlayMakerFSM? _cloudsWeather;
        private PlayMakerFSM? _forecast;
        private float _nextSearchAt;

        public bool Ready => _sunColor != null;

        public void Reset()
        {
            _sunColor = null;
            _cloudsWeather = null;
            _forecast = null;
            _nextSearchAt = 0f;
        }

        public void Locate()
        {
            if (_sunColor != null || Time.unscaledTime < _nextSearchAt) return;
            _nextSearchAt = Time.unscaledTime + 5f;

            var fsms = Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM));
            foreach (var obj in fsms)
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null) continue;

                try
                {
                    string name = fsm.gameObject.name;
                    string parent = fsm.transform.parent != null ? fsm.transform.parent.name : string.Empty;

                    if (_sunColor == null && name == "SUN" && fsm.FsmName == "Color" && parent == "PivotSun")
                        _sunColor = fsm;
                    else if (_cloudsWeather == null && name == "Clouds" && fsm.FsmName == "Weather" && parent == "WEATHER")
                        _cloudsWeather = fsm;
                    else if (_forecast == null && name == "Forecast" && fsm.FsmName == "Logic" && parent == "WEATHER")
                        _forecast = fsm;
                }
                catch
                {
                    // Destroyed-but-listed objects throw on access; skip them.
                }
            }

            if (_sunColor != null)
                WinterMPPlugin.Log.LogInfo("TimeSync: found game clock"
                    + (_forecast != null ? " and weather FSMs." : " (weather FSMs missing)."));
        }

        /// <summary>Host side: snapshot the clock + forecast. Null until the world is loaded.</summary>
        public TimeSync? BuildMessage()
        {
            Locate();
            if (_sunColor == null) return null;

            var hourVar = _sunColor.FsmVariables.FindFsmInt("Time");
            if (hourVar == null) return null;
            var minutesVar = _sunColor.FsmVariables.FindFsmFloat("Minutes");

            var message = new TimeSync
            {
                Hour = (byte)Mathf.Clamp(hourVar.Value, 0, 255),
                Minutes = minutesVar != null ? minutesVar.Value : 0f,
            };

            if (_forecast != null)
            {
                var oldTemp = _forecast.FsmVariables.FindFsmFloat("OldTemp");
                var newTemp = _forecast.FsmVariables.FindFsmFloat("NewTemp");
                var snowing = _forecast.FsmVariables.FindFsmBool("Snowing");
                var index = _forecast.FsmVariables.FindFsmInt("Index");
                if (oldTemp != null) message.TempOld = oldTemp.Value;
                if (newTemp != null) message.TempNew = newTemp.Value;
                if (snowing != null) message.Snowing = snowing.Value;
                if (index != null) message.ForecastIndex = index.Value;
            }

            return message;
        }

        /// <summary>Guest side: align the local clock/weather with the host's.</summary>
        public void Apply(TimeSync message)
        {
            Locate();
            if (_sunColor == null) return;

            ApplyClock(message);
            ApplyForecast(message);
        }

        private void ApplyClock(TimeSync message)
        {
            if (message.Hour < 1 || message.Hour > 24) return;

            var hourVar = _sunColor!.FsmVariables.FindFsmInt("Time");
            var minutesVar = _sunColor.FsmVariables.FindFsmFloat("Minutes");
            if (hourVar == null) return;

            float localTotal = hourVar.Value * 60f + (minutesVar != null ? minutesVar.Value : 0f);
            float hostTotal = message.Hour * 60f + message.Minutes;
            float drift = Mathf.Abs(hostTotal - localTotal);
            // The day wraps at 24:00 — 23:59 vs 0:01 is 2 minutes, not ~24h.
            drift = Mathf.Min(drift, 1440f - drift);
            if (drift <= DriftThresholdMinutes) return;

            WinterMPPlugin.Log.LogInfo(
                $"TimeSync: clock drift {drift:0} min — jumping {hourVar.Value}:{(minutesVar != null ? minutesVar.Value : 0f):00} " +
                $"-> {message.Hour}:{message.Minutes:00}.");

            // Jump the sun to the host's hour state (same injected-transition trick
            // as doors); the state's own actions set rotation/ambience/exposure.
            string sunState = message.Hour.ToString();
            if (FsmHook.EnsureRemoteEntry(_sunColor, sunState))
                FsmHook.FireRemoteEntry(_sunColor, sunState);

            if (minutesVar != null) minutesVar.Value = message.Minutes;

            // Keep the cloud cycle on the same hour as well ('00' == midnight).
            if (_cloudsWeather != null)
            {
                string cloudState = message.Hour == 24 ? "00" : message.Hour.ToString();
                if (FsmHook.EnsureRemoteEntry(_cloudsWeather, cloudState))
                    FsmHook.FireRemoteEntry(_cloudsWeather, cloudState);
            }
        }

        private void ApplyForecast(TimeSync message)
        {
            if (_forecast == null) return;

            var oldTemp = _forecast.FsmVariables.FindFsmFloat("OldTemp");
            var newTemp = _forecast.FsmVariables.FindFsmFloat("NewTemp");
            var snowing = _forecast.FsmVariables.FindFsmBool("Snowing");
            var index = _forecast.FsmVariables.FindFsmInt("Index");
            if (oldTemp != null) oldTemp.Value = message.TempOld;
            if (newTemp != null) newTemp.Value = message.TempNew;
            if (snowing != null) snowing.Value = message.Snowing;
            if (index != null) index.Value = message.ForecastIndex;
        }
    }
}
