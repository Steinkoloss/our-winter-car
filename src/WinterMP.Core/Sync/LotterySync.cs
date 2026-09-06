using System;
using System.Collections;
using System.Reflection;
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
    /// <summary>Mirrors completed native Lotto draws. Ticket purchases/claims are separate authority work.</summary>
    internal sealed class LotterySync
    {
        private readonly LottoDrawReplica _replica = new LottoDrawReplica();
        private readonly FsmSuppressor _suppressor = new FsmSuppressor();
        private LottoDrawData? _config;
        private PlayMakerFSM? _numbers;
        private GameObject? _results;
        private FsmBool? _drawDone;
        private FsmInt[] _scalars = new FsmInt[0], _winners = new FsmInt[0];
        private IList[] _lists = new IList[0];
        private object[][]? _originalLists;
        private int[]? _originalScalars, _originalWinners;
        private bool _originalDrawDone, _originalVisible, _disabled;
        private LottoDrawState? _lastBroadcast, _applied;
        private uint _sequence;
        private float _nextProbeAt, _nextHostTickAt, _nextKeepAliveAt;

        private bool Ready => _numbers != null && _results != null && _drawDone != null
            && _scalars.Length == 5 && _winners.Length == 5 && _lists.Length == 4;

        public void Update(SessionManager session)
        {
            if (_disabled || session.PlayerCount == 0) return;
            try
            {
                if (!Ready && Time.unscaledTime >= _nextProbeAt)
                {
                    _nextProbeAt = Time.unscaledTime + 5f;
                    Locate();
                }
                if (!Ready) return;
                if (!session.IsHost) { ApplyPending(); return; }
                if (Time.unscaledTime < _nextHostTickAt) return;
                _nextHostTickAt = Time.unscaledTime + 2f;
                var state = ReadHost();
                if (state == null) return;
                bool changed = _lastBroadcast == null || !LottoDrawReplica.SameDraw(_lastBroadcast, state);
                if (!changed && Time.unscaledTime < _nextKeepAliveAt) return;
                state.Sequence = unchecked(++_sequence);
                session.SendWorldMessage(state, Channel.ReliableOrdered);
                _lastBroadcast = state;
                _nextKeepAliveAt = Time.unscaledTime + 30f;
                if (changed) SyncEventLog.Record("lotto", "host round " + state.Round + " seq " + state.Sequence);
            }
            catch (Exception e) { Disable(e); }
        }

        public void ForceBroadcast() { _nextHostTickAt = _nextKeepAliveAt = 0f; }

        internal bool TryPurchaseRound(out int round)
        {
            round = 0;
            if (_disabled || !Ready || !Stable()) return false;
            round = _scalars[0].Value;
            return round >= 0 && round != 8888;
        }

        public LottoDrawState? BuildSnapshot()
        {
            var session = SessionManager.Instance;
            if (_disabled || session == null || !session.IsHost) return null;
            try
            {
                Locate();
                var state = ReadHost();
                if (state == null) { ForceBroadcast(); return null; }
                // A join snapshot must not consume the broadcast change edge for existing peers.
                state.Sequence = unchecked(++_sequence);
                return state;
            }
            catch (Exception e) { Disable(e); return null; }
        }

        public void Apply(LottoDrawState message)
        {
            var session = SessionManager.Instance;
            if (_disabled || session == null || session.IsHost) return;
            if (!_replica.Receive(message)) return;
            try { ApplyPending(); }
            catch (Exception e) { Disable(e); }
        }

        private void ApplyPending()
        {
            if (!Ready) return;
            if (!_suppressor.Active)
            {
                // Let native save loading and any in-flight calculation finish before preserving it.
                // DrawDone is set at the START of a draw, so it cannot serve as this barrier.
                if (!Stable()) return;
                CaptureOriginal();
                if (!_suppressor.Suppress(_numbers)) throw new InvalidOperationException("Cannot pause guest Lotto generator.");
                SyncEventLog.Record("lotto", "guest generator paused; local draw preserved");
            }
            var state = _replica.Current;
            if (state == null || ReferenceEquals(_applied, state)) return;
            bool changed = _applied == null || !LottoDrawReplica.SameDraw(_applied, state);
            // Keep each proxy's live ArrayList instance: native readers retain references to it.
            SetNumbers(_lists[0], state.Numbers); SetNumbers(_lists[1], state.Bonus);
            SetList(_lists[2], state.Prizes); SetList(_lists[3], state.Winners);
            int[] values = { state.Round, state.TicketRound, state.NationalPot, state.NationalPotMin, state.NationalPotFull };
            for (int i = 0; i < values.Length; i++) _scalars[i].Value = values[i];
            for (int i = 0; i < state.Winners.Length; i++) _winners[i].Value = state.Winners[i];
            if (_drawDone != null) _drawDone.Value = (state.Flags & LottoDrawState.FlagDrawDone) != 0;
            RefreshResults((state.Flags & LottoDrawState.FlagResultsVisible) != 0, changed);
            _applied = state;
            if (changed) SyncEventLog.Record("lotto", "guest applied round " + state.Round + " seq " + state.Sequence);
        }

        private LottoDrawState? ReadHost()
        {
            if (!Ready || !Stable()) return null;
            var state = new LottoDrawState
            {
                Round = _scalars[0].Value, TicketRound = _scalars[1].Value,
                NationalPot = _scalars[2].Value, NationalPotMin = _scalars[3].Value, NationalPotFull = _scalars[4].Value,
                Flags = (byte)((_drawDone != null && _drawDone.Value ? LottoDrawState.FlagDrawDone : 0)
                    | (_results != null && _results.activeSelf ? LottoDrawState.FlagResultsVisible : 0)),
            };
            if (!LottoDrawReplica.ReadLists(state, _lists[0], _lists[1], _lists[2], _lists[3])) return null;
            for (int i = 0; i < state.Winners.Length; i++) if (_winners[i].Value != state.Winners[i]) return null;
            return state;
        }

        private bool Stable()
        {
            if (_numbers == null || _config == null) return false;
            foreach (string state in _config.StableStates) if (_numbers.ActiveStateName == state) return true;
            return false;
        }

        private void Locate()
        {
            if (Ready) return;
            var config = SyncCatalog.LottoDraw;
            if (config == null) throw new InvalidOperationException("Missing Lotto catalog bindings.");
            PlayMakerFSM? numbers = null;
            GameObject? results = null;
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null) continue;
                string path = ScenePath.Of(fsm.transform);
                if (path == config.Path && fsm.FsmName == config.Fsm)
                {
                    if (numbers != null) throw new InvalidOperationException("Ambiguous Lotto FSM.");
                    numbers = fsm;
                }
                // The Texts parent has no FSM, but its children exist even while the TV is off.
                if (path.StartsWith(config.ResultsPath + "/", StringComparison.Ordinal))
                {
                    var ancestor = fsm.transform;
                    while (ancestor != null && ScenePath.Of(ancestor) != config.ResultsPath) ancestor = ancestor.parent;
                    if (ancestor != null)
                    {
                        if (results != null && results != ancestor.gameObject) throw new InvalidOperationException("Ambiguous Lotto display.");
                        results = ancestor.gameObject;
                    }
                }
            }
            if (numbers == null || results == null || string.IsNullOrEmpty(numbers.ActiveStateName)) return;
            foreach (string state in config.StableStates)
                if (!FsmHook.HasState(numbers, state)) throw new InvalidOperationException("Missing Lotto stable state: " + state);
            var scalars = BindInts(numbers, config.Scalars);
            var winners = BindInts(numbers, config.WinnerVariables);
            FsmBool? drawDone = null;
            foreach (var value in numbers.FsmVariables.BoolVariables)
                if (value.Name == config.DrawDone) drawDone = value;
            if (drawDone == null) throw new InvalidOperationException("Missing typed Lotto draw flag.");
            var lists = new IList[4];
            for (int i = 0; i < lists.Length; i++)
            {
                var list = FindList(numbers, config.Lists[i]);
                if (list == null) return;
                lists[i] = list;
                for (int j = 0; j < i; j++)
                    if (ReferenceEquals(lists[j], list)) throw new InvalidOperationException("Aliased Lotto lists.");
            }
            _config = config; _numbers = numbers; _results = results; _drawDone = drawDone;
            _scalars = scalars; _winners = winners; _lists = lists;
            WinterMPPlugin.Log.LogInfo("LotterySync: bound complete native Lotto draw and teletext display.");
        }

        private static FsmInt[] BindInts(PlayMakerFSM fsm, string[] names)
        {
            var result = new FsmInt[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                FsmInt? match = null;
                foreach (var variable in fsm.FsmVariables.IntVariables)
                    if (variable.Name == names[i])
                    {
                        if (match != null) throw new InvalidOperationException("Duplicate Lotto variable: " + names[i]);
                        match = variable;
                    }
                if (match == null) throw new InvalidOperationException("Missing typed Lotto integer: " + names[i]);
                result[i] = match;
            }
            return result;
        }

        private static IList? FindList(PlayMakerFSM fsm, string reference)
        {
            IList? result = null;
            bool found = false;
            foreach (var component in fsm.GetComponents<MonoBehaviour>())
            {
                if (component == null || component.GetType().Name != "PlayMakerArrayListProxy") continue;
                var type = component.GetType();
                if (type.GetField("referenceName", BindingFlags.Public | BindingFlags.Instance)?.GetValue(component) as string != reference) continue;
                if (found) throw new InvalidOperationException("Duplicate Lotto ArrayList: " + reference);
                found = true;
                // preFill lists are serialized defaults, never the runtime data used by ticket/TV FSMs.
                result = type.GetProperty("arrayList", BindingFlags.Public | BindingFlags.Instance)?.GetValue(component, null) as IList;
                if (result != null && (result.IsReadOnly || result.IsFixedSize))
                    throw new InvalidOperationException("Lotto ArrayList is not writable: " + reference);
            }
            if (!found) throw new InvalidOperationException("Missing Lotto ArrayList: " + reference);
            return result;
        }

        private void CaptureOriginal()
        {
            var lists = new object[_lists.Length][];
            for (int i = 0; i < lists.Length; i++)
            {
                lists[i] = new object[_lists[i].Count];
                _lists[i].CopyTo(lists[i], 0);
            }
            var scalars = new int[_scalars.Length]; var winners = new int[_winners.Length];
            for (int i = 0; i < scalars.Length; i++) scalars[i] = _scalars[i].Value;
            for (int i = 0; i < winners.Length; i++) winners[i] = _winners[i].Value;
            _originalLists = lists; _originalScalars = scalars; _originalWinners = winners;
            _originalDrawDone = _drawDone != null && _drawDone.Value;
            _originalVisible = _results != null && _results.activeSelf;
        }

        private static void SetNumbers(IList target, byte[] values)
        {
            target.Clear();
            foreach (byte value in values) target.Add((int)value);
        }
        private static void SetList(IList target, IEnumerable values)
        {
            target.Clear();
            foreach (object value in values) target.Add(value);
        }

        private void RefreshResults(bool visible, bool changed)
        {
            if (_results == null) return;
            // Native text FSMs read once on enable. Also unfreeze Reset points' temporary
            // hide when the host leaves that state; the guest's paused FSM cannot run OnExit.
            if (changed && _results.activeSelf) _results.SetActive(false);
            if (_results.activeSelf != visible) _results.SetActive(visible);
        }

        public void Clear()
        {
            try
            {
                if (_numbers != null && _originalLists != null)
                {
                    for (int i = 0; i < _lists.Length; i++)
                    {
                        int index = i;
                        RestorePart(() => SetList(_lists[index], _originalLists[index]));
                    }
                    if (_originalScalars != null)
                        for (int i = 0; i < _scalars.Length; i++) { int index = i; RestorePart(() => _scalars[index].Value = _originalScalars[index]); }
                    if (_originalWinners != null)
                        for (int i = 0; i < _winners.Length; i++) { int index = i; RestorePart(() => _winners[index].Value = _originalWinners[index]); }
                    RestorePart(() => { if (_drawDone != null) _drawDone.Value = _originalDrawDone; });
                    RestorePart(() => RefreshResults(_originalVisible, true));
                }
            }
            finally
            {
                _suppressor.Restore();
                _numbers = null; _results = null; _drawDone = null; _config = null;
                _scalars = _winners = new FsmInt[0]; _lists = new IList[0];
                _originalLists = null; _originalScalars = _originalWinners = null;
                _lastBroadcast = _applied = null; _replica.Clear(); _sequence = 0; _disabled = false;
                _nextProbeAt = _nextHostTickAt = _nextKeepAliveAt = 0f;
            }
        }

        private static void RestorePart(Action restore)
        {
            try { restore(); }
            catch (Exception e) { WinterMPPlugin.Log.LogError("LotterySync: local draw restore failed: " + e); }
        }
        private void Disable(Exception error)
        {
            // Retain suppression and originals until Clear; resuming a half-applied guest draw could run payouts.
            _disabled = true;
            WinterMPPlugin.Log.LogError("LotterySync: draw sync disabled: " + error);
            SyncEventLog.Record("lotto", "disabled: " + error.Message);
        }
    }
}
