using System;
using System.Collections;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    /// <summary>Publishes completed native rounds; guests preserve and pause their season and odds generators.</summary>
    internal sealed partial class HockeyBettingSync
    {
        private readonly HockeyBettingReplica _replica = new HockeyBettingReplica();
        private readonly FsmSuppressor _bettingPause = new FsmSuppressor(), _seasonPause = new FsmSuppressor();
        private HockeyBettingData? _c;
        private PlayMakerFSM? _betting, _season;
        private FsmInt[] _ints = new FsmInt[0];
        private FsmFloat[] _floats = new FsmFloat[0];
        private FsmInt? _gamesPlayed;
        private FsmBool? _kurpaWins;
        private FsmString? _result;
        private Proxy[] _lists = new Proxy[0], _tables = new Proxy[0];
        private GameObject[] _displays = new GameObject[0];
        private HockeyBettingState? _lastComplete, _broadcast, _applied;
        private object[][]? _originalLists;
        private IDictionary[]? _originalTables;
        private int[]? _originalInts;
        private bool _originalWin, _disabled;
        private ushort _sequence;
        private float _probeAt, _tickAt, _keepaliveAt;
        private bool Ready => _betting != null && _season != null && _lists.Length == 9 && _tables.Length == 6;

        public void Update(SessionManager session)
        {
            if (_disabled || session.PlayerCount == 0) return;
            try
            {
                if (!Ready && Time.unscaledTime >= _probeAt) { _probeAt = Time.unscaledTime + 2f; Locate(); }
                if (!Ready) return;
                if (!session.IsHost) { ApplyPending(); return; }
                if (Time.unscaledTime < _tickAt) return;
                _tickAt = Time.unscaledTime + 1f;
                var state = ReadHost();
                if (state == null) return;
                bool changed = _broadcast == null || !HockeyBettingReplica.SameBoard(_broadcast, state);
                if (!changed && Time.unscaledTime < _keepaliveAt) return;
                state.Sequence = unchecked(++_sequence);
                session.SendWorldMessage(state, Channel.ReliableOrdered);
                _broadcast = state; _keepaliveAt = Time.unscaledTime + 30f;
                if (changed) SyncEventLog.Record("hockey", "host completed games " + state.GamesPlayed + " seq " + state.Sequence);
            }
            catch (Exception e) { Disable(e); }
        }

        public HockeyBettingState? BuildSnapshot()
        {
            var session = SessionManager.Instance;
            if (_disabled || session == null || !session.IsHost) return null;
            try
            {
                if (!Ready) Locate();
                var state = ReadHost();
                if (state == null && _lastComplete != null) state = HockeyBettingReplica.Copy(_lastComplete);
                if (state == null) { _tickAt = _keepaliveAt = 0; return null; }
                // A join during native calculation sees the last complete round. It must
                // not consume the next broadcast for peers already in the world.
                state.Sequence = unchecked(++_sequence);
                return state;
            }
            catch (Exception e) { Disable(e); return null; }
        }

        public void Apply(HockeyBettingState message)
        {
            var session = SessionManager.Instance;
            if (_disabled || session == null || session.IsHost || !_replica.Receive(message)) return;
            try { ApplyPending(); }
            catch (Exception e) { Disable(e); }
        }

        private HockeyBettingState? ReadHost()
        {
            if (!Ready || !Stable()) return null;
            var state = new HockeyBettingState
            {
                LatestRound = _ints[0].Value, GameIndex = _ints[1].Value, Team1Id = _ints[2].Value, Team2Id = _ints[3].Value,
                Team1Odds = _floats[0].Value, Team2Odds = _floats[1].Value, TieOdds = _floats[2].Value,
                Result = _result!.Value, GamesPlayed = _gamesPlayed!.Value,
                Flags = (byte)(_kurpaWins!.Value ? HockeyBettingState.FlagKurPaWins : 0),
            };
            if (!HockeyBettingReplica.ReadCollections(state, LiveLists(), LiveTables(), _c!.Keys)) return null;
            _lastComplete = HockeyBettingReplica.Copy(state);
            return state;
        }

        private void ApplyPending()
        {
            if (!Ready) return;
            if (!_seasonPause.Active)
            {
                // Both FSMs must finish load/generation before a guest can pause them.
                // LatestRound changes on each match; it is not a completion barrier.
                if (!Stable()) return;
                CaptureOriginal();
                if (!_seasonPause.Suppress(_season) || !_bettingPause.Suppress(_betting))
                    throw new InvalidOperationException("Cannot pause guest hockey generators.");
            }
            var state = _replica.Current;
            if (state == null || ReferenceEquals(_applied, state)) return;
            var lists = LiveLists(); var tables = LiveTables();
            SetBytes(lists[0], state.Pairs, false); SetBytes(lists[1], state.PreviousPairs, false);
            SetBytes(lists[2], state.Results, true); Set(lists[3], state.ResultOdds); Set(lists[4], state.Scores);
            for (int list = 0; list < 4; list++)
            {
                lists[5 + list].Clear();
                for (int row = 0; row < 12; row++) lists[5 + list].Add(state.Standings[list * 12 + row]);
            }
            for (int match = 0; match < 6; match++)
                for (int key = 0; key < 3; key++) tables[match][_c!.Keys[key]] = state.Odds[match * 3 + key];
            _ints[0].Value = state.LatestRound;
            _gamesPlayed!.Value = state.GamesPlayed; _kurpaWins!.Value = state.KurPaWins;
            // GameIndex, team IDs and scalar odds belong to the paused FSM's cursor.
            // Native readers consume the collections above, never those scratch values.
            bool changed = _applied == null || !HockeyBettingReplica.SameBoard(_applied, state);
            _applied = state;
            if (changed) { RefreshDisplays(); SyncEventLog.Record("hockey", "guest board seq " + state.Sequence); }
        }

        private void Disable(Exception e)
        {
            if (_disabled) return;
            _disabled = true;
            WinterMPPlugin.Log.LogError("Hockey betting sync disabled; other world sync continues: " + e);
        }

        public void Clear()
        {
            try
            {
                if (_originalLists != null)
                {
                    for (int i = 0; i < _lists.Length; i++) Set(_lists[i].List(), _originalLists[i]);
                    var tables = LiveTables();
                    for (int i = 0; i < tables.Length; i++)
                    {
                        tables[i].Clear();
                        foreach (DictionaryEntry entry in _originalTables![i]) tables[i].Add(entry.Key, entry.Value);
                    }
                    _ints[0].Value = _originalInts![0]; _gamesPlayed!.Value = _originalInts[1]; _kurpaWins!.Value = _originalWin;
                    RefreshDisplays();
                }
            }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("Hockey state restore failed: " + e.Message); }
            finally
            {
                _bettingPause.Restore(); _seasonPause.Restore();
                _replica.Clear(); _lastComplete = _broadcast = _applied = null;
                _originalLists = null; _originalTables = null; _originalInts = null;
                _betting = _season = null; _gamesPlayed = null; _kurpaWins = null; _result = null; _c = null;
                _ints = new FsmInt[0]; _floats = new FsmFloat[0]; _lists = _tables = new Proxy[0]; _displays = new GameObject[0];
                _probeAt = _tickAt = _keepaliveAt = 0; _disabled = false;
            }
        }
    }
}
