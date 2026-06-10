using UnityEngine;

namespace WinterMP.Core.Diagnostics
{
    /// <summary>
    /// Runs the delayed environment report (~30s after boot, when the game has loaded
    /// its Steam integration) and logs every level transition — the level names feed
    /// the sync work in later milestones.
    /// </summary>
    internal sealed class DiagnosticsTicker : MonoBehaviour
    {
        private const float DelayedReportAfterSeconds = 30f;

        private float _startedAt;
        private bool _delayedDone;
        private string _lastLevel = string.Empty;

        private void Awake()
        {
            _startedAt = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            if (!_delayedDone && Time.realtimeSinceStartup - _startedAt > DelayedReportAfterSeconds)
            {
                _delayedDone = true;
                EnvironmentReport.Write("delayed", includeSteam: true);
            }

            string level;
            try
            {
                level = Application.loadedLevelName ?? string.Empty;
            }
            catch
            {
                return;
            }

            if (level != _lastLevel)
            {
                WinterMPPlugin.Log.LogInfo($"Level changed: '{_lastLevel}' -> '{level}' (index {Application.loadedLevel})");
                _lastLevel = level;
            }
        }
    }
}
