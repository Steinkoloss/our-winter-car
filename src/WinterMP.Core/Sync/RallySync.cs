using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Host-validated progress for the three single-stage rallies. Local trigger
    /// collisions only exist on the driver machine, so guests report crossings;
    /// the host verifies the exact static marker, ordered progression, delegated
    /// vehicle and a fresh player pose before publishing its own elapsed clock.
    /// </summary>
    internal sealed class RallySync
    {
        private const float ScanIntervalSeconds = 5f;
        private const float HostBroadcastSeconds = 1f;
        private const float PlayerPoseMaxAgeSeconds = 2f;
        private const float PlayerMarkerMaxDistance = 28f;
        private const float PlayerVehicleMaxDistance = 14f;

        private sealed class Stage
        {
            public byte Number, CheckpointCount;
            public RallyStageData Config = null!;
            public string CompletedState = "";
            public bool Observed, WasStarted;
            public byte ObservedMask;
            public PlayMakerFSM? Timing;
            public FsmBool? Started;
            public Transform? StartLine;
            public readonly Dictionary<byte, Checkpoint> Checkpoints = new Dictionary<byte, Checkpoint>();
        }

        private sealed class Checkpoint
        {
            public Transform Transform = null!;
            public PlayMakerFSM Marker = null!;
        }

        private readonly ItemWorldSync _items;
        private readonly Dictionary<byte, Stage> _stages = new Dictionary<byte, Stage>();
        private readonly RallyProgressLedger _ledger = new RallyProgressLedger();
        private readonly RallyProgressReplica _replica = new RallyProgressReplica(NewReportToken());
        private readonly RallyCrossingEvidence _crossings = new RallyCrossingEvidence();
        private readonly Dictionary<byte, float> _requestAt = new Dictionary<byte, float>();
        private float _nextScanAt, _nextBroadcastAt;
        private bool _failed, _reportFailureLogged;

        public void ForgetPlayer(byte playerId)
        {
            _ledger.ForgetPlayer(playerId);
            _crossings.ForgetPlayer(playerId);
            _requestAt.Remove(playerId);
        }

        private static ulong NewReportToken()
        {
            ulong token = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
            return token == 0 ? 1 : token;
        }

        public RallySync(ItemWorldSync items)
        {
            _items = items;
        }

        public void Clear()
        {
            _stages.Clear(); _ledger.Clear(); _crossings.Clear(); _requestAt.Clear();
            if (SessionManager.Instance?.State == SessionState.Connected) _replica.ResetView();
            else _replica.Clear(NewReportToken());
            _nextScanAt = _nextBroadcastAt = 0;
            _failed = _reportFailureLogged = false;
        }

        public void Update(SessionManager session)
        {
            if (_failed) return;
            try
            {
                Scan();
                if (session.IsHost)
                {
                    ObserveCrossings(session);
                    ObserveHostStage(session);
                    if (Time.unscaledTime >= _nextBroadcastAt)
                    {
                        _nextBroadcastAt = Time.unscaledTime + HostBroadcastSeconds;
                        foreach (var state in _ledger.Snapshots(Time.unscaledTime, true))
                            session.SendWorldMessage(state, Channel.ReliableOrdered);
                    }
                }
                else ObserveGuestStage(session);
            }
            catch (Exception e) { Disable(e); }
        }

        public IEnumerable<RallyState> BuildSnapshots() => _failed
            ? new RallyState[0] : _ledger.Snapshots(Time.unscaledTime, false);

        public bool TryAcceptIntent(RallyIntent message, out RallyState state)
        {
            state = new RallyState();
            var session = SessionManager.Instance;
            if (_failed || session == null || !session.IsHost || !_stages.TryGetValue(message.Stage, out var stage)
                || !Ready(stage) || message.Checkpoint > stage.CheckpointCount) return false;
            try
            {
                float now = Time.unscaledTime;
                if (_requestAt.TryGetValue(message.PlayerId, out float next) && now < next) return false;
                _requestAt[message.PlayerId] = now + .1f;
                ObserveCrossings(session);
                bool verified = _crossings.Contains(message.PlayerId, message.Stage, message.Checkpoint, now);
                bool accepted = _ledger.TryAccept(message, stage.CheckpointCount, now, verified, out state);
                if (accepted) SyncEventLog.Record("rally-crossing", "player " + message.PlayerId + " SS" + message.Stage
                    + " checkpoint " + message.Checkpoint + " report " + message.Sequence);
                return accepted;
            }
            catch (Exception e) { Disable(e); return false; }
        }

        public void Apply(RallyState message)
        {
            if (!_failed) _replica.Receive(message);
        }

        private void Disable(Exception e)
        {
            _failed = true;
            WinterMPPlugin.Log.LogError("RallySync: progress sync disabled: " + e);
            SyncEventLog.Record("rally-disabled", e.Message);
        }

        private void Scan()
        {
            if (Time.unscaledTime < _nextScanAt) return;
            _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;
            try
            {
                var config = SyncCatalog.RallyProgress;
                if (config == null) return;
                for (int i = 0; i < config.Stages.Count; i++)
                    if (!_stages.ContainsKey((byte)(i + 1))) _stages.Add((byte)(i + 1), new Stage
                    {
                        Number = (byte)(i + 1), Config = config.Stages[i],
                        CheckpointCount = (byte)config.Stages[i].Checkpoints.Length, CompletedState = config.CompletedState,
                    });
                foreach (var obj in ScenePath.ScanFsms())
                {
                    var fsm = obj as PlayMakerFSM;
                    if (fsm == null) continue;
                    string path = ScenePath.Of(fsm.transform);
                    foreach (var stage in _stages.Values)
                    {
                        if (path == stage.Config.TimingPath && fsm.FsmName == config.TimingFsm)
                        {
                            stage.Timing = fsm;
                            stage.Started = fsm.FsmVariables.FindFsmBool(config.StartedVariable);
                        }
                        else if (path == stage.Config.StartPath && fsm.FsmName == config.MarkerFsm) stage.StartLine = fsm.transform;
                        else if (fsm.FsmName == config.MarkerFsm)
                            for (int i = 0; i < stage.Config.Checkpoints.Length; i++)
                                if (path == stage.Config.TimingPath + "/" + stage.Config.Checkpoints[i])
                                {
                                    ValidateMarker(fsm, config);
                                    stage.Checkpoints[(byte)(i + 1)] = new Checkpoint { Transform = fsm.transform, Marker = fsm };
                                }
                    }
                }
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug("RallySync: scan failed: " + e.Message);
            }
        }

        private static void ValidateMarker(PlayMakerFSM fsm, RallyProgressData config)
        {
            if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
            var commit = FsmHook.FindState(fsm, config.CommitState);
            var completed = FsmHook.FindState(fsm, config.CompletedState);
            // SS2 checkpoint 4 activates checkpoints 5/6 on entry to Idle, then
            // remains there. Observing completion must leave those native actions alone.
            if (commit == null || completed == null || completed.Transitions.Length != 0
                || commit.Transitions.Length != 1 || commit.Transitions[0].ToState != config.CompletedState)
                throw new InvalidOperationException("Rally checkpoint completion layout changed: " + fsm.name);
        }

        private static bool Ready(Stage stage)
        {
            if (stage.Timing == null || stage.Started == null || stage.StartLine == null || stage.CheckpointCount == 0
                || stage.Checkpoints.Count != stage.CheckpointCount) return false;
            for (byte i = 1; i <= stage.CheckpointCount; i++)
                if (!stage.Checkpoints.TryGetValue(i, out var point) || point.Transform == null || point.Marker == null
                    || stage.Timing.FsmVariables.FindFsmBool(stage.Config.Checkpoints[i - 1]) == null) return false;
            return true;
        }

        private static byte ReadMask(Stage stage)
        {
            // The marker's unused Checkpoint bool never changes. Its terminal state
            // survives the Timing FSM clearing every flag synchronously at the finish.
            byte mask = 0;
            foreach (var pair in stage.Checkpoints)
                if (pair.Value.Marker != null && pair.Value.Marker.ActiveStateName == stage.CompletedState) mask |= (byte)(1 << (pair.Key - 1));
            return mask;
        }

        private void ObserveGuestStage(SessionManager session)
        {
            foreach (var stage in _stages.Values)
                if (Ready(stage)) _replica.Observe(session.LocalPlayerId, stage.Number, stage.Started!.Value, ReadMask(stage), Time.unscaledTime);
            var report = _replica.Take(Time.unscaledTime);
            if (report != null) session.SendWorldMessage(report, Channel.ReliableOrdered);
            if (_replica.Failed && !_reportFailureLogged)
            {
                _reportFailureLogged = true;
                session.AddSystemChat("* Rally crossing could not be confirmed. Restart the stage to record a new time.");
                SyncEventLog.Record("rally-report-timeout", "unconfirmed local crossing");
            }
            if (!_replica.Failed) _reportFailureLogged = false;
        }

        private void ObserveHostStage(SessionManager session)
        {
            foreach (var stage in _stages.Values)
            {
                if (!Ready(stage)) continue;
                bool started = stage.Started!.Value;
                byte mask = ReadMask(stage);
                if (stage.Observed)
                {
                    if (started && !stage.WasStarted) _ledger.AdvanceLocal(session.LocalPlayerId, stage.Number, 0, stage.CheckpointCount, Time.unscaledTime);
                    // Dictionary scan order must never decide which checkpoint reaches the host first.
                    for (byte i = 1; i <= stage.CheckpointCount; i++)
                        if ((mask & (1 << (i - 1))) != 0 && (stage.ObservedMask & (1 << (i - 1))) == 0)
                            _ledger.AdvanceLocal(session.LocalPlayerId, stage.Number, i, stage.CheckpointCount, Time.unscaledTime);
                }
                stage.Observed = true; stage.WasStarted = started; stage.ObservedMask = mask;
            }
        }

        private void ObserveCrossings(SessionManager session)
        {
            float now = Time.unscaledTime;
            foreach (var player in session.Players)
            {
                if (player.PlayerId == session.LocalPlayerId || player.LastTransformTime <= 0
                    || now < player.LastTransformTime || now - player.LastTransformTime > PlayerPoseMaxAgeSeconds
                    || !HasNearbyDelegatedVehicle(player.PlayerId, player.Position)) continue;
                foreach (var stage in _stages.Values)
                {
                    if (!Ready(stage)) continue;
                    for (byte checkpoint = 0; checkpoint <= stage.CheckpointCount; checkpoint++)
                        if (TryGetMarker(stage, checkpoint, out var marker)
                            && (player.Position - marker.position).sqrMagnitude <= PlayerMarkerMaxDistance * PlayerMarkerMaxDistance)
                            _crossings.Observe(player.PlayerId, stage.Number, checkpoint, player.LastTransformTime, now);
                }
            }
        }

        private static bool TryGetMarker(Stage stage, byte checkpoint, out Transform marker)
        {
            if (checkpoint == 0 && stage.StartLine != null)
            {
                marker = stage.StartLine;
                return true;
            }
            if (stage.Checkpoints.TryGetValue(checkpoint, out var point))
            {
                marker = point.Transform;
                return true;
            }
            marker = null!;
            return false;
        }

        private bool HasNearbyDelegatedVehicle(byte playerId, Vector3 playerPosition)
        {
            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || item.Body == null || item.RemoteOwner != playerId) continue;
                if ((item.Body.transform.position - playerPosition).sqrMagnitude
                    <= PlayerVehicleMaxDistance * PlayerVehicleMaxDistance)
                    return true;
            }
            return false;
        }

    }
}
