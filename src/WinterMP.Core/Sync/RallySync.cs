using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

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
        private const string RallyPrefix = "RACES/RALLY/SS";
        private const float ScanIntervalSeconds = 5f;
        private const float HostBroadcastSeconds = 1f;
        private const float PlayerPoseMaxAgeSeconds = 2f;
        private const float PlayerMarkerMaxDistance = 28f;
        private const float PlayerVehicleMaxDistance = 14f;

        private sealed class Stage
        {
            public byte Number;
            public PlayMakerFSM? Timing;
            public FsmBool? Started;
            public Transform? StartLine;
            public readonly Dictionary<byte, Checkpoint> Checkpoints = new Dictionary<byte, Checkpoint>();
        }

        private sealed class Checkpoint
        {
            public Transform Transform = null!;
            public FsmBool? Reached;
        }

        private sealed class Record
        {
            public byte PlayerId;
            public byte Stage;
            public byte Phase;
            public byte Checkpoint;
            public float StartedAt;
            // Set when the record reaches PhaseFinished so the elapsed clock freezes
            // (otherwise every later broadcast/snapshot of a finished record would
            // report an ever-growing "final" time).
            public float FinishedAt;
            // A finished record must be broadcast to already-connected guests exactly
            // once; racing records are re-sent at HostBroadcastSeconds but the finish is
            // a one-shot terminal edge (host-driven finishes have no intent to relay).
            public bool FinishedBroadcast;
        }

        private readonly ItemWorldSync _items;
        private readonly Dictionary<byte, Stage> _stages = new Dictionary<byte, Stage>();
        private readonly Dictionary<byte, Record> _records = new Dictionary<byte, Record>();
        private readonly Dictionary<byte, ushort> _lastIntentSequence = new Dictionary<byte, ushort>();
        private readonly Dictionary<byte, bool> _guestStartObserved = new Dictionary<byte, bool>();
        private readonly Dictionary<uint, bool> _guestCheckpointObserved = new Dictionary<uint, bool>();
        private readonly Dictionary<byte, bool> _hostStartObserved = new Dictionary<byte, bool>();
        private readonly Dictionary<uint, bool> _hostCheckpointObserved = new Dictionary<uint, bool>();
        private float _nextScanAt;
        private float _nextBroadcastAt;
        private ushort _outIntentSequence;
        private ushort _outStateSequence;
        private ushort _lastRemoteSequence;

        public RallySync(ItemWorldSync items)
        {
            _items = items;
        }

        public void Clear()
        {
            _stages.Clear();
            _records.Clear();
            _lastIntentSequence.Clear();
            _guestStartObserved.Clear();
            _guestCheckpointObserved.Clear();
            _hostStartObserved.Clear();
            _hostCheckpointObserved.Clear();
            _nextScanAt = 0f;
            _nextBroadcastAt = 0f;
            _outIntentSequence = 0;
            _outStateSequence = 0;
            _lastRemoteSequence = 0;
        }

        public void Update(SessionManager session)
        {
            Scan();
            if (session.IsHost)
            {
                ObserveHostStage(session);
                BroadcastActiveRecords(session);
            }
            else
            {
                ObserveGuestStage(session);
            }
        }

        public IEnumerable<RallyState> BuildSnapshots()
        {
            foreach (var record in _records.Values)
                yield return ToMessage(record);
        }

        public bool TryAcceptIntent(RallyIntent message, out RallyState state)
        {
            state = new RallyState();
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || !_stages.TryGetValue(message.Stage, out var stage))
                return false;
            if (!TryGetMarker(stage, message.Checkpoint, out var marker)
                || !TryGetFreshPlayerPose(session, message.PlayerId, out Vector3 playerPosition)
                || (playerPosition - marker.position).sqrMagnitude > PlayerMarkerMaxDistance * PlayerMarkerMaxDistance
                || !HasNearbyDelegatedVehicle(message.PlayerId, playerPosition))
                return false;

            if (_lastIntentSequence.TryGetValue(message.PlayerId, out ushort last))
            {
                ushort diff = (ushort)(message.Sequence - last);
                if (diff == 0 || diff > short.MaxValue) return false;
            }
            _lastIntentSequence[message.PlayerId] = message.Sequence;

            if (!TryAdvance(message.PlayerId, stage, message.Checkpoint, out var record)) return false;
            state = ToMessage(record);
            // The caller broadcasts this accepted state, so the periodic one-shot must not
            // re-send a guest finish; host-driven finishes leave the flag clear for it.
            if (record.Phase == RallyState.PhaseFinished)
                record.FinishedBroadcast = true;
            WinterMPPlugin.Log.LogInfo(
                $"RallySync: accepted player {message.PlayerId} SS{message.Stage} checkpoint {message.Checkpoint}.");
            return true;
        }

        public void Apply(RallyState message)
        {
            ushort diff = (ushort)(message.Sequence - _lastRemoteSequence);
            if (_lastRemoteSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            _lastRemoteSequence = message.Sequence;
            _records[message.PlayerId] = new Record
            {
                PlayerId = message.PlayerId,
                Stage = message.Stage,
                Phase = message.Phase,
                Checkpoint = message.Checkpoint,
                StartedAt = Time.unscaledTime - message.ElapsedCentiseconds / 100f,
            };
        }

        private void Scan()
        {
            if (Time.unscaledTime < _nextScanAt) return;
            _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;
            try
            {
                var fsms = Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM));
                foreach (var obj in fsms)
                {
                    var fsm = obj as PlayMakerFSM;
                    if (fsm == null) continue;
                    string path = ScenePath.Of(fsm.transform);
                    if (!TryParseStage(path, out byte stageNumber)) continue;
                    if (!_stages.TryGetValue(stageNumber, out var stage))
                    {
                        stage = new Stage { Number = stageNumber };
                        _stages[stageNumber] = stage;
                    }

                    string timingPath = RallyPrefix + stageNumber + "/TimingSS" + stageNumber;
                    if (path == timingPath && fsm.FsmName == "Timing" && stage.Timing == null)
                    {
                        stage.Timing = fsm;
                        stage.Started = fsm.FsmVariables.FindFsmBool("Start");
                    }
                    else if (path == timingPath + "/JumpStartLineSS" + stageNumber && fsm.FsmName == "Checkpoint")
                    {
                        stage.StartLine = fsm.transform;
                    }
                    else if (TryParseCheckpoint(path, timingPath, out byte checkpoint) && fsm.FsmName == "Checkpoint"
                        && !stage.Checkpoints.ContainsKey(checkpoint))
                    {
                        stage.Checkpoints[checkpoint] = new Checkpoint
                        {
                            Transform = fsm.transform,
                            Reached = fsm.FsmVariables.FindFsmBool("Checkpoint"),
                        };
                    }
                }
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug("RallySync: scan failed: " + e.Message);
            }
        }

        private void ObserveGuestStage(SessionManager session)
        {
            foreach (var stage in _stages.Values)
            {
                bool started = stage.Started != null && stage.Started.Value;
                bool wasStarted = _guestStartObserved.TryGetValue(stage.Number, out bool seen) && seen;
                if (started && !wasStarted) SendIntent(session, stage.Number, 0);
                _guestStartObserved[stage.Number] = started;

                foreach (var pair in stage.Checkpoints)
                {
                    bool reached = pair.Value.Reached != null && pair.Value.Reached.Value;
                    uint id = CheckpointKey(stage.Number, pair.Key);
                    bool wasReached = _guestCheckpointObserved.TryGetValue(id, out bool previous) && previous;
                    if (reached && !wasReached) SendIntent(session, stage.Number, pair.Key);
                    _guestCheckpointObserved[id] = reached;
                }
            }
        }

        private void ObserveHostStage(SessionManager session)
        {
            foreach (var stage in _stages.Values)
            {
                bool started = stage.Started != null && stage.Started.Value;
                bool wasStarted = _hostStartObserved.TryGetValue(stage.Number, out bool seen) && seen;
                if (started && !wasStarted) TryAdvance(session.LocalPlayerId, stage, 0, out _);
                _hostStartObserved[stage.Number] = started;

                foreach (var pair in stage.Checkpoints)
                {
                    bool reached = pair.Value.Reached != null && pair.Value.Reached.Value;
                    uint id = CheckpointKey(stage.Number, pair.Key);
                    bool wasReached = _hostCheckpointObserved.TryGetValue(id, out bool previous) && previous;
                    if (reached && !wasReached) TryAdvance(session.LocalPlayerId, stage, pair.Key, out _);
                    _hostCheckpointObserved[id] = reached;
                }
            }
        }

        private void SendIntent(SessionManager session, byte stage, byte checkpoint)
        {
            session.SendWorldMessage(new RallyIntent
            {
                PlayerId = session.LocalPlayerId,
                Stage = stage,
                Checkpoint = checkpoint,
                Sequence = ++_outIntentSequence,
            }, Channel.ReliableOrdered);
        }

        private void BroadcastActiveRecords(SessionManager session)
        {
            if (session.PlayerCount == 0 || Time.unscaledTime < _nextBroadcastAt) return;
            _nextBroadcastAt = Time.unscaledTime + HostBroadcastSeconds;
            foreach (var record in _records.Values)
            {
                if (record.Phase == RallyState.PhaseRacing)
                {
                    session.SendWorldMessage(ToMessage(record), Channel.ReliableOrdered);
                }
                else if (record.Phase == RallyState.PhaseFinished && !record.FinishedBroadcast)
                {
                    // Host-driven finishes have no guest intent to relay, so the periodic
                    // loop is the only path that reaches already-connected guests. One-shot:
                    // the terminal time is frozen (FinishedAt), so re-sending adds nothing.
                    record.FinishedBroadcast = true;
                    session.SendWorldMessage(ToMessage(record), Channel.ReliableOrdered);
                }
            }
        }

        private bool TryAdvance(byte playerId, Stage stage, byte checkpoint, out Record record)
        {
            if (checkpoint == 0)
            {
                record = new Record
                {
                    PlayerId = playerId,
                    Stage = stage.Number,
                    Phase = RallyState.PhaseRacing,
                    Checkpoint = 0,
                    StartedAt = Time.unscaledTime,
                };
                _records[playerId] = record;
                return true;
            }

            if (!_records.TryGetValue(playerId, out record!) || record.Stage != stage.Number
                || record.Phase != RallyState.PhaseRacing || checkpoint != record.Checkpoint + 1
                || !stage.Checkpoints.ContainsKey(checkpoint))
                return false;

            record.Checkpoint = checkpoint;
            if (checkpoint == stage.Checkpoints.Count)
            {
                record.Phase = RallyState.PhaseFinished;
                record.FinishedAt = Time.unscaledTime;
            }
            return true;
        }

        private RallyState ToMessage(Record record)
        {
            float endTime = record.Phase == RallyState.PhaseFinished && record.FinishedAt > 0f
                ? record.FinishedAt
                : Time.unscaledTime;
            return new RallyState
            {
                PlayerId = record.PlayerId,
                Stage = record.Stage,
                Phase = record.Phase,
                Checkpoint = record.Checkpoint,
                Sequence = ++_outStateSequence,
                ElapsedCentiseconds = (uint)Mathf.Max(0, Mathf.RoundToInt((endTime - record.StartedAt) * 100f)),
            };
        }

        private static bool TryParseStage(string path, out byte stage)
        {
            stage = 0;
            if (!path.StartsWith(RallyPrefix, StringComparison.Ordinal) || path.Length <= RallyPrefix.Length)
                return false;
            char digit = path[RallyPrefix.Length];
            if (digit < '1' || digit > '3' || path.Length <= RallyPrefix.Length + 1 || path[RallyPrefix.Length + 1] != '/')
                return false;
            stage = (byte)(digit - '0');
            return true;
        }

        private static bool TryParseCheckpoint(string path, string timingPath, out byte checkpoint)
        {
            checkpoint = 0;
            const string prefix = "/Checkpoint0";
            if (!path.StartsWith(timingPath + prefix, StringComparison.Ordinal)) return false;
            int index = (timingPath + prefix).Length;
            if (index >= path.Length || path.Length != index + 1) return false;
            char digit = path[index];
            if (digit < '1' || digit > '6') return false;
            checkpoint = (byte)(digit - '0');
            return true;
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

        private static bool TryGetFreshPlayerPose(SessionManager session, byte playerId, out Vector3 position)
        {
            float now = Time.unscaledTime;
            foreach (var player in session.Players)
            {
                if (player.PlayerId != playerId) continue;
                if (player.LastTransformTime <= 0f || now - player.LastTransformTime > PlayerPoseMaxAgeSeconds)
                    break;
                position = player.Position;
                return true;
            }
            position = Vector3.zero;
            return false;
        }

        private static uint CheckpointKey(byte stage, byte checkpoint)
        {
            return (uint)(stage << 8 | checkpoint);
        }
    }
}
