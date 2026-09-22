using System;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    public sealed partial class WorldSyncManager
    {
        private const int MaxSyncErrors = 8;
        private const float SyncErrorBackoffSeconds = 1f;
        private bool _worldSyncDisabled;
        private int _syncErrorCount;
        private float _syncErrorBackoffUntil;
        private readonly CallbackFailure _vehicleLateFailure = new CallbackFailure();
        private readonly CallbackFailure _trailerLateFailure = new CallbackFailure();
        private readonly CallbackFailure _venttiLateFailure = new CallbackFailure();
        private readonly CallbackFailure _trainFixedFailure = new CallbackFailure();
        private readonly CallbackFailure _trainReceiveFailure = new CallbackFailure();
        private readonly CallbackFailure _trainSnapshotFailure = new CallbackFailure();

        private sealed class CallbackFailure
        {
            internal const int MaxFailures = 3;
            internal int Count;
            internal float RetryAt;
            internal bool Quarantined => Count >= MaxFailures;
            internal bool CanRun(float now) => !Quarantined && now >= RetryAt;
            internal void Record(float now)
            {
                Count++;
                RetryAt = Quarantined ? 0f : now + Count;
            }
            internal void Clear() { Count = 0; RetryAt = 0f; }
        }

        private void Update()
        {
            if (Time.unscaledTime < _syncErrorBackoffUntil) return;

            try
            {
                if (_worldSyncDisabled)
                {
                    var session = SessionManager.Instance;
                    if (_wasSessionActive && (session == null ||
                        session.State != SessionState.Hosting && session.State != SessionState.Connected))
                    {
                        // Global fallback stops gameplay, not restoration. A failed
                        // cleanup is also rate-limited instead of retried every frame.
                        _syncErrorBackoffUntil = Time.unscaledTime + SyncErrorBackoffSeconds;
                        ReleaseEverything();
                        _wasSessionActive = false;
                    }
                    return;
                }
                UpdateWorldSync();
            }
            catch (Exception e)
            {
                HandleSyncError("Update", e);
            }
        }

        private void FixedUpdate()
        {
            if (!_syncReady || _worldSyncDisabled || !_wasSessionActive || Time.unscaledTime < _syncErrorBackoffUntil)
                return;
            if (!IsGameLevel() || !_trainFixedFailure.CanRun(Time.unscaledTime)) return;

            // Only contain escapes from TrainSync's own failure handling. Its
            // authority, replica protection and native disable/restore stay intact.
            try { _train.FixedUpdate(); }
            catch (Exception e) { HandleSyncError("FixedUpdate.TrainSync", e, _trainFixedFailure); }
        }

        private void LateUpdate()
        {
            if (!_syncReady || _worldSyncDisabled || !_wasSessionActive || Time.unscaledTime < _syncErrorBackoffUntil)
                return;

            if (!IsGameLevel()) return;

            try
            {
                float now = Time.unscaledTime;
                if (_vehicleLateFailure.CanRun(now))
                {
                    try { _vehicles.LateUpdateRemoteVehicles(now); }
                    catch (Exception e) { HandleSyncError("LateUpdate.VehicleWorldSync", e, _vehicleLateFailure); }
                }
                if (_trailerLateFailure.CanRun(now))
                {
                    try { _trailer.LateUpdate(); }
                    catch (Exception e) { HandleSyncError("LateUpdate.TractorTrailerSync", e, _trailerLateFailure); }
                }
                var session = SessionManager.Instance;
                if (session != null && _venttiLateFailure.CanRun(now))
                {
                    try { _ventti.LateUpdate(session); }
                    catch (Exception e) { HandleSyncError("LateUpdate.VenttiSync", e, _venttiLateFailure); }
                }
            }
            catch (Exception e)
            {
                HandleSyncError("LateUpdate", e);
            }
        }

        /// <summary>
        /// One transient exception must not silently kill world sync for the rest
        /// of the session (a despawn race did exactly that and broke all vehicle
        /// sync). Back off briefly and only disable after repeated failures.
        /// </summary>
        private void HandleSyncError(string phase, Exception e, CallbackFailure? isolated = null)
        {
            if (isolated != null) isolated.Record(Time.unscaledTime);
            _syncErrorCount++;
            SyncEventLog.Record("error", $"{phase} #{_syncErrorCount}: {e}");

            if (_syncErrorCount >= MaxSyncErrors)
            {
                _worldSyncDisabled = true;
                WinterMPPlugin.Log.LogError($"WorldSync disabled after {_syncErrorCount} errors; last ({phase}): {e}");
                SyncEventLog.DumpToFile();
                return;
            }

            if (isolated != null)
            {
                // The cumulative local budget also bounds intermittent failures;
                // successful frames do not give a flapping callback infinite retries.
                string disposition = isolated.Quarantined ? "quarantined until world/session cleanup" :
                    $"retrying only this callback in {isolated.Count}s";
                WinterMPPlugin.Log.LogWarning(
                    $"WorldSync {phase} error {_syncErrorCount}/{MaxSyncErrors} (subsystem {isolated.Count}/{CallbackFailure.MaxFailures}) — {disposition}; other world sync continues: {e}");
                return;
            }

            _syncErrorBackoffUntil = Time.unscaledTime + SyncErrorBackoffSeconds;
            WinterMPPlugin.Log.LogWarning(
                $"WorldSync {phase} error {_syncErrorCount}/{MaxSyncErrors} — retrying in {SyncErrorBackoffSeconds:0.#}s: {e}");
        }

        private void ResetSyncErrors()
        {
            ResetLateUpdateErrors();
            ResetVehicleUpdateErrors();
            ResetFixedUpdateErrors();
            ResetTrainMessageErrors();
            _worldSyncDisabled = false;
            _syncErrorCount = 0;
            _syncErrorBackoffUntil = 0f;
            SyncEventLog.Clear();
        }

        private void ResetTrainMessageErrors()
        {
            _trainReceiveFailure.Clear();
            _trainSnapshotFailure.Clear();
        }

        private void ResetFixedUpdateErrors()
        {
            _trainFixedFailure.Clear();
        }

        private void ResetLateUpdateErrors()
        {
            _vehicleLateFailure.Clear();
            _trailerLateFailure.Clear();
            _venttiLateFailure.Clear();
        }
    }
}
