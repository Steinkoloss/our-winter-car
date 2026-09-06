using System;
using HutongGames.PlayMaker;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VenttiSync
    {
        private readonly VenttiTableReplica _tableReplica = new VenttiTableReplica();
        private FsmString? _resultStatus;
        private VenttiTableState? _lastSentTable;
        private uint _outTableSequence;
        private bool _tableDisabled, _tableSaved, _tableApplied, _invalidTableLogged;
        private uint _appliedTableSequence;
        private float _originalStake;
        private int _originalPlayerTotal, _originalHouseTotal;
        private string _originalResult = string.Empty;

        private bool TableReady => _built && _manager != null && _betValue != null
            && _playerHand != null && _houseHand != null && _resultStatus != null;

        private void HostBroadcastIfChanged(SessionManager session, bool keepAlive)
        {
            if (_tableDisabled) return;
            try
            {
                var state = ReadTable();
                if (state == null) return;
                bool changed = _lastSentTable == null || !VenttiTableReplica.SameTable(_lastSentTable, state);
                if (!changed && !keepAlive) return;
                state.Sequence = ++_outTableSequence;
                session.SendWorldMessage(state, Channel.ReliableOrdered);
                _lastSentTable = state;
                if (changed) SyncEventLog.Record("ventti-table",
                    $"stake {state.Stake} player {state.PlayerTotal} house {state.HouseTotal} outcome {state.Outcome}");
            }
            catch (Exception e) { DisableTable(e); }
        }

        internal VenttiTableState? BuildTableSnapshot()
        {
            var session = SessionManager.Instance;
            if (_tableDisabled || _ledger != null || session == null || !session.IsHost) return null;
            try
            {
                EnsureBuilt();
                Locate();
                if (_tableDisabled) return null;
                var state = ReadTable();
                // Snapshot enumeration must not consume a change owed to existing guests.
                if (state != null) state.Sequence = ++_outTableSequence;
                return state;
            }
            catch (Exception e) { DisableTable(e); return null; }
        }

        public void OnRemoteState(VenttiTableState state)
        {
            var session = SessionManager.Instance;
            if (_tableDisabled || _gameReplica.Current != null || session == null || session.IsHost) return;
            try
            {
                EnsureBuilt();
                if (!_built || !_tableReplica.Receive(_tableId, state)) return;
                // Keep the observation even if the guest has never opened the table.
                // Update retries application after the regular discovery probe.
                ApplyTable();
            }
            catch (Exception e) { DisableTable(e); }
        }

        private VenttiTableState? ReadTable()
        {
            var bindings = SyncCatalog.VenttiTable;
            if (!TableReady || bindings == null || _betValue == null || _playerHand == null
                || _houseHand == null || _resultStatus == null) return null;
            string text = _resultStatus.Value;
            byte outcome = VenttiTableState.None;
            if (!string.IsNullOrEmpty(text))
            {
                for (int i = 0; i < VenttiTableData.OutcomeBindings.Length; i++)
                    if (text == bindings[VenttiTableData.OutcomeBindings[i]]) { outcome = (byte)(i + 1); break; }
                // An updated game's unknown status must not masquerade as a reset.
                if (outcome == VenttiTableState.None) return RejectTable("Unmapped native result: " + text);
            }
            var state = new VenttiTableState
            {
                TableId = _tableId, Stake = _betValue.Value, PlayerTotal = _playerHand.Value,
                HouseTotal = _houseHand.Value, Outcome = outcome,
            };
            if (!VenttiTableReplica.IsValid(state)) return RejectTable("Invalid native stake or hand total.");
            _invalidTableLogged = false;
            return state;
        }

        private VenttiTableState? RejectTable(string reason)
        {
            if (!_invalidTableLogged)
            {
                WinterMPPlugin.Log.LogWarning("VenttiSync: table observation skipped: " + reason);
                SyncEventLog.Record("ventti-table-invalid", reason);
                _invalidTableLogged = true;
            }
            return null;
        }

        private void ApplyTable()
        {
            if (_tableDisabled) return;
            try
            {
                var state = _tableReplica.Current;
                var bindings = SyncCatalog.VenttiTable;
                if (state == null || bindings == null || !TableReady || _betValue == null
                    || _playerHand == null || _houseHand == null || _resultStatus == null) return;
                if (_tableApplied && _appliedTableSequence == state.Sequence) return;
                GuestLockdown();
                if (!ManagerLocked) return;
                CaptureTable();
                _betValue.Value = state.Stake;
                _playerHand.Value = state.PlayerTotal;
                _houseHand.Value = state.HouseTotal;
                _resultStatus.Value = state.Outcome == VenttiTableState.None ? string.Empty
                    : bindings[VenttiTableData.OutcomeBindings[state.Outcome - 1]];
                _appliedTableSequence = state.Sequence;
                _tableApplied = true;
                // No resolver events or LoseText activation: its global interaction
                // text must not open on a guest who is elsewhere in the world.
            }
            catch (Exception e) { DisableTable(e); }
        }

        private void RestoreTable()
        {
            if (!_tableSaved) return;
            try
            {
                if (_betValue != null) _betValue.Value = _originalStake;
                if (_playerHand != null) _playerHand.Value = _originalPlayerTotal;
                if (_houseHand != null) _houseHand.Value = _originalHouseTotal;
                if (_resultStatus != null) _resultStatus.Value = _originalResult;
            }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("VenttiSync: table restore failed: " + e.Message); }
            _tableSaved = false;
        }

        private void CaptureTable()
        {
            if (_tableSaved || _betValue == null || _playerHand == null || _houseHand == null || _resultStatus == null) return;
            _originalStake = _betValue.Value;
            _originalPlayerTotal = _playerHand.Value;
            _originalHouseTotal = _houseHand.Value;
            _originalResult = _resultStatus.Value;
            _tableSaved = true;
        }

        private void DisableTable(Exception e)
        {
            _tableDisabled = true;
            RestoreTable();
            DisableGame(e);
            WinterMPPlugin.Log.LogError("VenttiSync: table synchronization disabled: " + e);
            SyncEventLog.Record("ventti-table-disabled", e.Message);
        }

        private void ClearTable()
        {
            RestoreTable();
            _tableReplica.Clear();
            _lastSentTable = null;
            _outTableSequence = 0;
            _appliedTableSequence = 0;
            _tableApplied = false;
            _tableDisabled = false;
            _invalidTableLogged = false;
            _resultStatus = null;
            _originalResult = string.Empty;
        }
    }
}
