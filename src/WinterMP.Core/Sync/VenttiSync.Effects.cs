using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VenttiSync
    {
        private void ValidateOutcomeActions()
        {
            if (_manager == null || _gameConfig == null) return;
            // Validate every state before cutting any action. A renamed/reordered
            // game action must fail closed instead of leaving a second wallet writer.
            foreach (var outcome in _gameConfig.Outcomes)
            {
                var state = FsmHook.FindState(_manager, outcome.State);
                if (state == null || state.Actions.Length != outcome.ActionTypes.Length)
                    throw new InvalidOperationException("Ventti outcome layout changed: " + outcome.State);
                for (int i = 0; i < state.Actions.Length; i++)
                    if (state.Actions[i].GetType().Name != outcome.ActionTypes[i])
                        throw new InvalidOperationException("Ventti outcome action changed: " + outcome.State + " #" + i);
            }
            foreach (var outcome in _gameConfig.Outcomes)
            {
                var state = FsmHook.FindState(_manager, outcome.State);
                if (state == null) throw new InvalidOperationException("Ventti outcome disappeared.");
                foreach (int index in outcome.SuppressActions) CutAction(state.Actions[index]);
                if (!FsmHook.EnsureRemoteEntry(_manager, outcome.State))
                    throw new InvalidOperationException("Cannot bind Ventti outcome: " + outcome.State);
            }
        }

        private void WriteProgress(VenttiLedgerState state)
        {
            if (_gameConfig == null) return;
            if (_betMaximum != null) _betMaximum.Value = state.BetMaximum;
            if (_opponentLoss != null) _opponentLoss.Value = state.OpponentLoss;
            if (_propertyStage != null) _propertyStage.Value = state.PropertyStage;
            if (_gameHost && _manager != null)
            {
                var max = _manager.FsmVariables.FindFsmFloat(_gameConfig["betMaximum"]);
                var bet = _manager.FsmVariables.FindFsmFloat(_gameConfig["stake"]);
                if (max != null) max.Value = state.BetMaximum;
                if (bet != null) bet.Value = state.Stake;
            }
        }

        private void ApplyHostOutcome(VenttiLedgerState state)
        {
            WriteProgress(state);
            if (!_gameHost || _manager == null || _gameConfig == null || state.Outcome == VenttiTableState.None
                || state.PendingCash != 0
                || (_hasEffectRound && _effectRound == state.Round)) return;
            if (!_manager.enabled || !_manager.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Ventti property resolver became inactive before settlement.");
            // Cash, progression and actor stress have a single owner. Only the native
            // door/key/access, dialogue and animation actions remain in these states.
            _hasEffectRound = true; _effectRound = state.Round;
            _effectsUntil = Time.unscaledTime + 3f;
            FsmHook.FireRemoteEntry(_manager, _gameConfig.Outcomes[state.Outcome - 1].State);
            _nextPropertyTickAt = _nextPropertyKeepAliveAt = 0;
            SyncEventLog.Record("ventti-settle", "round " + state.Round + " outcome " + state.Outcome);
        }
    }
}
