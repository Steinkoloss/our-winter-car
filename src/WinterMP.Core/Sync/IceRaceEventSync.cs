using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>Mirrors the host-owned ice-race grid and heat configuration.</summary>
    internal sealed class IceRaceEventSync
    {
        private const string Path = "RACES/ICERACE/TrackFunctions";
        private const string LineupsPath = Path + "/LINEUPS";
        private const float ScanIntervalSeconds = 5f;
        private const float SendIntervalSeconds = 1f;

        private PlayMakerFSM? _fsm;
        private PlayMakerFSM? _lineups;
        private FsmInt? _carLimit;
        private FsmInt? _carNumber;
        private FsmInt? _carsOnTrack;
        private FsmInt? _gridReady;
        private FsmInt? _heatStage;
        private FsmInt? _lane;
        private FsmInt? _raceDistanceFinals;
        private FsmInt? _raceDistanceQuals;
        private FsmInt? _starterCars;
        private FsmFloat? _time;
        private FsmBool? _onTrack;
        private FsmString? _carId;
        private FsmString? _reference;
        private FsmInt? _lineupHeatStage;
        private FsmInt? _raceStage;
        private FsmBool? _playerRegistered;
        private IceRaceEventState? _pending;
        private IceRaceEventState? _last;
        private float _nextScanAt;
        private float _nextSendAt;
        private ushort _outSequence;
        private ushort _lastRemoteSequence;

        public void Clear()
        {
            _fsm = null;
            _lineups = null;
            _carLimit = null;
            _carNumber = null;
            _carsOnTrack = null;
            _gridReady = null;
            _heatStage = null;
            _lane = null;
            _raceDistanceFinals = null;
            _raceDistanceQuals = null;
            _starterCars = null;
            _time = null;
            _onTrack = null;
            _carId = null;
            _reference = null;
            _lineupHeatStage = null;
            _raceStage = null;
            _playerRegistered = null;
            _pending = null;
            _last = null;
            _nextScanAt = 0f;
            _nextSendAt = 0f;
            _outSequence = 0;
            _lastRemoteSequence = 0;
        }

        public void Update(SessionManager session)
        {
            Scan();
            ApplyPending();
            if (!session.IsHost || session.PlayerCount == 0 || Time.unscaledTime < _nextSendAt) return;
            _nextSendAt = Time.unscaledTime + SendIntervalSeconds;
            var state = BuildState(changedOnly: true);
            if (state != null)
                session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        public IceRaceEventState? BuildSnapshot()
        {
            Scan(force: true);
            return BuildState(changedOnly: false);
        }

        public void Apply(IceRaceEventState message)
        {
            Scan();
            if (!Ready())
            {
                _pending = message;
                return;
            }
            ushort diff = (ushort)(message.Sequence - _lastRemoteSequence);
            if (_lastRemoteSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            _lastRemoteSequence = message.Sequence;

            Set(_carLimit, message.CarLimit);
            Set(_carNumber, message.CarNumber);
            Set(_carsOnTrack, message.CarsOnTrack);
            Set(_gridReady, (message.Flags & IceRaceEventState.FlagGridReady) != 0 ? 1 : 0);
            Set(_heatStage, message.HeatStage);
            Set(_lineupHeatStage, message.HeatStage);
            Set(_lane, message.Lane);
            Set(_raceDistanceFinals, message.RaceDistanceFinals);
            Set(_raceDistanceQuals, message.RaceDistanceQuals);
            Set(_starterCars, message.StarterCars);
            Set(_time, message.Time);
            Set(_onTrack, (message.Flags & IceRaceEventState.FlagOnTrack) != 0);
            Set(_carId, message.CarId);
            Set(_reference, message.Reference);
            Set(_raceStage, message.RaceStage);
            Set(_playerRegistered, (message.Flags & IceRaceEventState.FlagPlayerRegistered) != 0);
        }

        private void Scan(bool force = false)
        {
            if (!force && Time.unscaledTime < _nextScanAt) return;
            _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;
            if (_fsm != null && _lineups != null) return;
            try
            {
                foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
                {
                    var fsm = obj as PlayMakerFSM;
                    if (fsm == null) continue;
                    string path = ScenePath.Of(fsm.transform);
                    if (_fsm == null && fsm.FsmName == "Data" && path == Path)
                    {
                        _fsm = fsm;
                        _carLimit = fsm.FsmVariables.FindFsmInt("CarLimit");
                        _carNumber = fsm.FsmVariables.FindFsmInt("CarNumber");
                        _carsOnTrack = fsm.FsmVariables.FindFsmInt("CarsOnTrack");
                        _gridReady = fsm.FsmVariables.FindFsmInt("GridReady");
                        _heatStage = fsm.FsmVariables.FindFsmInt("HeatStage");
                        _lane = fsm.FsmVariables.FindFsmInt("Lane");
                        _raceDistanceFinals = fsm.FsmVariables.FindFsmInt("RaceDistanceFinals");
                        _raceDistanceQuals = fsm.FsmVariables.FindFsmInt("RaceDistanceQuals");
                        _starterCars = fsm.FsmVariables.FindFsmInt("StarterCars");
                        _time = fsm.FsmVariables.FindFsmFloat("Time");
                        _onTrack = fsm.FsmVariables.FindFsmBool("OnTrack");
                        _carId = fsm.FsmVariables.FindFsmString("CarID");
                        _reference = fsm.FsmVariables.FindFsmString("Reference");
                        WinterMPPlugin.Log.LogInfo("IceRaceEventSync: registered event controller.");
                    }
                    else if (_lineups == null && fsm.FsmName == "Logic" && path == LineupsPath)
                    {
                        _lineups = fsm;
                        _lineupHeatStage = fsm.FsmVariables.FindFsmInt("HeatStage");
                        _raceStage = fsm.FsmVariables.FindFsmInt("RaceStage");
                        _playerRegistered = fsm.FsmVariables.FindFsmBool("PlayerRegistered");
                        WinterMPPlugin.Log.LogInfo("IceRaceEventSync: registered lineup controller.");
                    }

                    if (_fsm != null && _lineups != null) break;
                }
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug("IceRaceEventSync: scan failed: " + e.Message);
            }
        }

        private void ApplyPending()
        {
            if (_pending == null || !Ready()) return;
            var pending = _pending;
            _pending = null;
            Apply(pending);
        }

        private bool Ready()
        {
            return _fsm != null && _lineups != null && _carLimit != null && _carNumber != null && _carsOnTrack != null
                && _gridReady != null && _heatStage != null && _lane != null && _raceDistanceFinals != null
                && _raceDistanceQuals != null && _starterCars != null && _time != null && _onTrack != null
                && _carId != null && _reference != null && _lineupHeatStage != null && _raceStage != null
                && _playerRegistered != null;
        }

        private IceRaceEventState? BuildState(bool changedOnly)
        {
            if (!Ready()) return null;
            byte flags = 0;
            if (Read(_gridReady) != 0) flags |= IceRaceEventState.FlagGridReady;
            if (Read(_onTrack)) flags |= IceRaceEventState.FlagOnTrack;
            if (Read(_playerRegistered)) flags |= IceRaceEventState.FlagPlayerRegistered;
            var state = new IceRaceEventState
            {
                Flags = flags,
                Sequence = ++_outSequence,
                CarLimit = Read(_carLimit),
                CarNumber = Read(_carNumber),
                CarsOnTrack = Read(_carsOnTrack),
                HeatStage = Read(_heatStage),
                Lane = Read(_lane),
                RaceDistanceFinals = Read(_raceDistanceFinals),
                RaceDistanceQuals = Read(_raceDistanceQuals),
                StarterCars = Read(_starterCars),
                Time = Read(_time),
                CarId = Read(_carId),
                Reference = Read(_reference),
                RaceStage = Read(_raceStage),
            };
            if (changedOnly && Same(state, _last))
            {
                _outSequence--;
                return null;
            }
            _last = state;
            return state;
        }

        private static bool Same(IceRaceEventState state, IceRaceEventState? previous)
        {
            return previous != null && state.Flags == previous.Flags
                && state.CarLimit == previous.CarLimit && state.CarNumber == previous.CarNumber
                && state.CarsOnTrack == previous.CarsOnTrack && state.HeatStage == previous.HeatStage
                && state.Lane == previous.Lane && state.RaceDistanceFinals == previous.RaceDistanceFinals
                && state.RaceDistanceQuals == previous.RaceDistanceQuals && state.StarterCars == previous.StarterCars
                && Mathf.Abs(state.Time - previous.Time) < 0.001f
                && state.CarId == previous.CarId && state.Reference == previous.Reference
                && state.RaceStage == previous.RaceStage;
        }

        private static int Read(FsmInt? value) => value != null ? value.Value : 0;
        private static float Read(FsmFloat? value) => value != null ? value.Value : 0f;
        private static bool Read(FsmBool? value) => value != null && value.Value;
        private static string Read(FsmString? value) => value != null ? value.Value ?? string.Empty : string.Empty;
        private static void Set(FsmInt? value, int result) { if (value != null) value.Value = result; }
        private static void Set(FsmFloat? value, float result) { if (value != null) value.Value = result; }
        private static void Set(FsmBool? value, bool result) { if (value != null) value.Value = result; }
        private static void Set(FsmString? value, string result) { if (value != null) value.Value = result; }
    }
}
