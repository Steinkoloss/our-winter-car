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
    /// Host-validated ice-track progress. The Corris race database observes the
    /// local driver's trigger collisions, while the host validates reports against
    /// the fixed track-marker transforms and its own elapsed clock.
    /// </summary>
    internal sealed class IceRaceSync
    {
        private const string CorrisRacePath = "CORRIS/DB/RaceTrigger";
        private const string TrackPrefix = "RACES/ICERACE/TrackFunctions/";
        private const float ScanIntervalSeconds = 5f;
        private const float BroadcastSeconds = 1f;
        private const float PlayerPoseMaxAgeSeconds = 2f;
        private const float PlayerMarkerMaxDistance = 28f;
        private const float PlayerVehicleMaxDistance = 14f;

        private sealed class Record
        {
            public byte PlayerId;
            public byte Mode;
            public byte Checkpoint;
            public byte Laps;
            public float StartedAt;
        }

        private readonly ItemWorldSync _items;
        private readonly Dictionary<byte, Record> _records = new Dictionary<byte, Record>();
        private readonly Dictionary<byte, ushort> _lastIntentSequence = new Dictionary<byte, ushort>();
        private PlayMakerFSM? _corrisRace;

        /// <summary>Host: a player (re)joined — its intent counter restarted; drop the stale latch.</summary>
        public void ForgetPlayer(byte playerId) => _lastIntentSequence.Remove(playerId);
        private FsmFloat? _time;
        private FsmBool? _checkpoint1;
        private FsmBool? _checkpoint2;
        private FsmInt? _laps;
        private Transform? _timeStart;
        private Transform? _lapStart;
        private Transform? _checkpoint1Marker;
        private Transform? _checkpoint2Marker;
        private float _lastGuestTime;
        private int _lastGuestLaps;
        private bool _lastGuestCheckpoint1;
        private bool _lastGuestCheckpoint2;
        private float _nextScanAt;
        private float _nextBroadcastAt;
        private ushort _outIntentSequence;
        private ushort _outStateSequence;
        private ushort _lastRemoteSequence;

        public IceRaceSync(ItemWorldSync items)
        {
            _items = items;
        }

        public void Clear()
        {
            _records.Clear();
            _lastIntentSequence.Clear();
            _corrisRace = null;
            _time = null;
            _checkpoint1 = null;
            _checkpoint2 = null;
            _laps = null;
            _timeStart = null;
            _lapStart = null;
            _checkpoint1Marker = null;
            _checkpoint2Marker = null;
            _lastGuestTime = 0f;
            _lastGuestLaps = 0;
            _lastGuestCheckpoint1 = false;
            _lastGuestCheckpoint2 = false;
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
                ObserveHost(session);
                Broadcast(session);
                return;
            }
            ObserveGuest(session);
        }

        // Host observes its OWN driving with the same FSM edges the guest path reports
        // (R2.7, mirroring RallySync.ObserveHostStage): the edges feed TryAdvance directly —
        // no wire hop, no freshness check, and the pose is our own local player (via the
        // world-sync bridge), so the time-trial-vs-lap mode pick is if anything more exact
        // than the guest path's network pose. The _lastGuest* edge baselines are shared with
        // ObserveGuest — safe, one role runs per session.
        private void ObserveHost(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            if (_corrisRace == null || _time == null || _checkpoint1 == null || _checkpoint2 == null || _laps == null) return;

            float currentTime = _time.Value;
            bool cp1 = _checkpoint1.Value;
            bool cp2 = _checkpoint2.Value;
            int laps = _laps.Value;

            if (_items.TryGetLocalPlayerPosition(out Vector3 pose))
            {
                if (currentTime > 0.01f && _lastGuestTime <= 0.01f) HostAdvance(session, IceRaceIntent.MarkerStart, pose);
                if (cp1 && !_lastGuestCheckpoint1) HostAdvance(session, IceRaceIntent.MarkerCheckpoint1, pose);
                if (cp2 && !_lastGuestCheckpoint2) HostAdvance(session, IceRaceIntent.MarkerCheckpoint2, pose);
                if (laps > _lastGuestLaps) HostAdvance(session, IceRaceIntent.MarkerFinish, pose);
            }

            _lastGuestTime = currentTime;
            _lastGuestCheckpoint1 = cp1;
            _lastGuestCheckpoint2 = cp2;
            _lastGuestLaps = laps;
        }

        private void HostAdvance(SessionManager session, byte marker, Vector3 pose)
        {
            if (!TryAdvance(session.LocalPlayerId, marker, pose, out var record)) return;
            session.SendWorldMessage(ToMessage(record), Channel.ReliableOrdered);
            WinterMPPlugin.Log.LogInfo($"IceRaceSync: host marker {marker}, lap {record.Laps}.");
        }

        public IEnumerable<IceRaceState> BuildSnapshots()
        {
            foreach (var record in _records.Values)
                yield return ToMessage(record);
        }

        public bool TryAcceptIntent(IceRaceIntent message, out IceRaceState state)
        {
            state = new IceRaceState();
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || !IsMarker(message.Marker)
                || !TryGetFreshPlayerPose(session, message.PlayerId, out Vector3 playerPosition)
                || !HasNearbyDelegatedVehicle(message.PlayerId, playerPosition))
                return false;

            if (_lastIntentSequence.TryGetValue(message.PlayerId, out ushort last))
            {
                ushort diff = (ushort)(message.Sequence - last);
                if (diff == 0 || diff > short.MaxValue) return false;
            }

            if (!TryAdvance(message.PlayerId, message.Marker, playerPosition, out var record)) return false;
            _lastIntentSequence[message.PlayerId] = message.Sequence;
            state = ToMessage(record);
            WinterMPPlugin.Log.LogInfo(
                $"IceRaceSync: accepted player {message.PlayerId} marker {message.Marker}, lap {record.Laps}.");
            return true;
        }

        public void Apply(IceRaceState message)
        {
            ushort diff = (ushort)(message.Sequence - _lastRemoteSequence);
            if (_lastRemoteSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            _lastRemoteSequence = message.Sequence;
            if (message.Mode != IceRaceState.ModeTimeTrial && message.Mode != IceRaceState.ModeLapRace) return;
            _records[message.PlayerId] = new Record
            {
                PlayerId = message.PlayerId,
                Mode = message.Mode,
                Checkpoint = message.Checkpoint,
                Laps = message.Laps,
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
                    if (path == CorrisRacePath && fsm.FsmName == "Data" && _corrisRace == null)
                    {
                        _corrisRace = fsm;
                        _time = fsm.FsmVariables.FindFsmFloat("Time");
                        _checkpoint1 = fsm.FsmVariables.FindFsmBool("Checkpoint1");
                        _checkpoint2 = fsm.FsmVariables.FindFsmBool("Checkpoint2");
                        _laps = fsm.FsmVariables.FindFsmInt("Laps");
                    }
                    else if (path == TrackPrefix + "StartFinishTime" && fsm.FsmName == "Checkpoint")
                        _timeStart = fsm.transform;
                    else if (path == TrackPrefix + "StartFinishLaps" && fsm.FsmName == "Checkpoint")
                        _lapStart = fsm.transform;
                    else if (path == TrackPrefix + "Checkpoint1" && fsm.FsmName == "Checkpoint")
                        _checkpoint1Marker = fsm.transform;
                    else if (path == TrackPrefix + "Checkpoint2" && fsm.FsmName == "Checkpoint")
                        _checkpoint2Marker = fsm.transform;
                }
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug("IceRaceSync: scan failed: " + e.Message);
            }
        }

        private void ObserveGuest(SessionManager session)
        {
            if (_corrisRace == null || _time == null || _checkpoint1 == null || _checkpoint2 == null || _laps == null) return;
            float currentTime = _time.Value;
            bool cp1 = _checkpoint1.Value;
            bool cp2 = _checkpoint2.Value;
            int laps = _laps.Value;

            if (currentTime > 0.01f && _lastGuestTime <= 0.01f)
                SendIntent(session, IceRaceIntent.MarkerStart);
            if (cp1 && !_lastGuestCheckpoint1)
                SendIntent(session, IceRaceIntent.MarkerCheckpoint1);
            if (cp2 && !_lastGuestCheckpoint2)
                SendIntent(session, IceRaceIntent.MarkerCheckpoint2);
            if (laps > _lastGuestLaps)
                SendIntent(session, IceRaceIntent.MarkerFinish);

            _lastGuestTime = currentTime;
            _lastGuestCheckpoint1 = cp1;
            _lastGuestCheckpoint2 = cp2;
            _lastGuestLaps = laps;
        }

        private void SendIntent(SessionManager session, byte marker)
        {
            session.SendWorldMessage(new IceRaceIntent
            {
                PlayerId = session.LocalPlayerId,
                Marker = marker,
                Sequence = ++_outIntentSequence,
            }, Channel.ReliableOrdered);
        }

        private void Broadcast(SessionManager session)
        {
            if (session.PlayerCount == 0 || Time.unscaledTime < _nextBroadcastAt) return;
            _nextBroadcastAt = Time.unscaledTime + BroadcastSeconds;
            foreach (var record in _records.Values)
                session.SendWorldMessage(ToMessage(record), Channel.ReliableOrdered);
        }

        private bool TryAdvance(byte playerId, byte marker, Vector3 playerPosition, out Record record)
        {
            if (!_records.TryGetValue(playerId, out record!))
            {
                if (marker != IceRaceIntent.MarkerStart) return false;
                if (IsNear(playerPosition, _timeStart))
                    record = NewRecord(playerId, IceRaceState.ModeTimeTrial);
                else if (IsNear(playerPosition, _lapStart))
                    record = NewRecord(playerId, IceRaceState.ModeLapRace);
                else
                    return false;
                _records[playerId] = record;
                return true;
            }

            if (record.Checkpoint == 0 && marker == IceRaceIntent.MarkerCheckpoint1 && IsNear(playerPosition, _checkpoint1Marker))
                record.Checkpoint = 1;
            else if (record.Checkpoint == 1 && marker == IceRaceIntent.MarkerCheckpoint2 && IsNear(playerPosition, _checkpoint2Marker))
                record.Checkpoint = 2;
            else if (record.Checkpoint == 2 && marker == IceRaceIntent.MarkerFinish
                && IsNear(playerPosition, record.Mode == IceRaceState.ModeTimeTrial ? _timeStart : _lapStart))
            {
                record.Checkpoint = 0;
                record.Laps++;
            }
            else
                return false;
            return true;
        }

        private Record NewRecord(byte playerId, byte mode)
        {
            return new Record
            {
                PlayerId = playerId,
                Mode = mode,
                Checkpoint = 0,
                Laps = 0,
                StartedAt = Time.unscaledTime,
            };
        }

        private IceRaceState ToMessage(Record record)
        {
            return new IceRaceState
            {
                PlayerId = record.PlayerId,
                Mode = record.Mode,
                Checkpoint = record.Checkpoint,
                Laps = record.Laps,
                Sequence = ++_outStateSequence,
                ElapsedCentiseconds = (uint)Mathf.Max(0, Mathf.RoundToInt((Time.unscaledTime - record.StartedAt) * 100f)),
            };
        }

        private static bool IsMarker(byte marker)
        {
            return marker <= IceRaceIntent.MarkerFinish;
        }

        private static bool IsNear(Vector3 playerPosition, Transform? marker)
        {
            return marker != null && (playerPosition - marker.position).sqrMagnitude
                <= PlayerMarkerMaxDistance * PlayerMarkerMaxDistance;
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
    }
}
