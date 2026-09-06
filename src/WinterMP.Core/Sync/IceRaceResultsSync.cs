using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>Mirrors host-generated ice-race ranking rows without re-running RNG/order logic.</summary>
    internal sealed class IceRaceResultsSync
    {
        private const string DataPrefix = "RACES/ICERACE/Stats/ResultsRace/Data/";
        private const float ScanIntervalSeconds = 5f;
        private const float SendIntervalSeconds = 1f;
        private readonly FsmString?[] _names = new FsmString?[IceRaceResultsState.MaxRows];
        private readonly FsmString?[] _numbers = new FsmString?[IceRaceResultsState.MaxRows];
        private readonly FsmString?[] _models = new FsmString?[IceRaceResultsState.MaxRows];
        private readonly FsmString?[] _uas = new FsmString?[IceRaceResultsState.MaxRows];
        private IceRaceResultsState? _pending;
        private IceRaceResultsState? _last;
        private float _nextScanAt;
        private float _nextSendAt;
        private ushort _outSequence;
        private ushort _lastRemoteSequence;

        public void Clear()
        {
            for (int i = 0; i < IceRaceResultsState.MaxRows; i++)
            {
                _names[i] = null;
                _numbers[i] = null;
                _models[i] = null;
                _uas[i] = null;
            }
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

        public IceRaceResultsState? BuildSnapshot()
        {
            Scan(force: true);
            // Targeted join send — must not advance the periodic change baseline.
            return BuildState(changedOnly: false, advanceBaseline: false);
        }

        public void Apply(IceRaceResultsState message)
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

            int count = Math.Min(IceRaceResultsState.MaxRows, message.Names.Length);
            for (int i = 0; i < count; i++)
            {
                Set(_names[i], message.Names[i]);
                Set(_numbers[i], message.Numbers[i]);
                Set(_models[i], message.Models[i]);
                Set(_uas[i], message.Uas[i]);
            }
        }

        private void Scan(bool force = false)
        {
            if (!force && Time.unscaledTime < _nextScanAt) return;
            _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;
            try
            {
                foreach (var obj in ScenePath.ScanFsms())
                {
                    var fsm = obj as PlayMakerFSM;
                    if (fsm == null || fsm.FsmName != "Data") continue;
                    string path = ScenePath.Of(fsm.transform);
                    if (!TryParseRow(path, out int row, out string field)) continue;
                    switch (field)
                    {
                        case "Name": _names[row] = fsm.FsmVariables.FindFsmString("String"); break;
                        case "Number": _numbers[row] = fsm.FsmVariables.FindFsmString("String"); break;
                        case "Model": _models[row] = fsm.FsmVariables.FindFsmString("String"); break;
                        case "UA": _uas[row] = fsm.FsmVariables.FindFsmString("String"); break;
                    }
                }
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug("IceRaceResultsSync: scan failed: " + e.Message);
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
            for (int i = 0; i < IceRaceResultsState.MaxRows; i++)
            {
                if (_names[i] == null || _numbers[i] == null || _models[i] == null || _uas[i] == null)
                    return false;
            }
            return true;
        }

        private IceRaceResultsState? BuildState(bool changedOnly, bool advanceBaseline = true)
        {
            if (!Ready()) return null;
            var state = new IceRaceResultsState
            {
                Sequence = ++_outSequence,
                Names = Read(_names),
                Numbers = Read(_numbers),
                Models = Read(_models),
                Uas = Read(_uas),
            };
            if (changedOnly && Same(state, _last))
            {
                _outSequence--;
                return null;
            }
            if (advanceBaseline)
                _last = state;
            return state;
        }

        private static bool TryParseRow(string path, out int row, out string field)
        {
            row = -1;
            field = string.Empty;
            if (!path.StartsWith(DataPrefix, StringComparison.Ordinal)) return false;
            string remaining = path.Substring(DataPrefix.Length);
            if (remaining.Length < 3 || remaining[1] != '/') return false;
            char digit = remaining[0];
            if (digit < '0' || digit > '5') return false;
            row = digit - '0';
            field = remaining.Substring(2);
            return field == "Name" || field == "Number" || field == "Model" || field == "UA";
        }

        private static string[] Read(FsmString?[] values)
        {
            var result = new string[values.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = values[i] != null ? values[i]!.Value ?? string.Empty : string.Empty;
            return result;
        }

        private static bool Same(IceRaceResultsState state, IceRaceResultsState? previous)
        {
            if (previous == null) return false;
            for (int i = 0; i < state.Names.Length; i++)
            {
                if (state.Names[i] != previous.Names[i] || state.Numbers[i] != previous.Numbers[i]
                    || state.Models[i] != previous.Models[i] || state.Uas[i] != previous.Uas[i])
                    return false;
            }
            return true;
        }

        private static void Set(FsmString? value, string text)
        {
            if (value != null) value.Value = text;
        }
    }
}
