using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class GamblingSync
    {
        private void Locate()
        {
            if (_config == null) return;
            var banking = SyncCatalog.Banking;
            if (_cash == null && banking != null)
                _cash = FsmVariables.GlobalVariables.FindFsmFloat(banking.CashGlobal);
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null) continue;
                try
                {
                    string path = ScenePath.Of(fsm.transform);
                    if (path == _config.StatsPath && fsm.FsmName == _config.StatsFsm)
                    {
                        _statsIn = fsm.FsmVariables.FindFsmFloat(_config.StatsMoneyIn);
                        _statsOut = fsm.FsmVariables.FindFsmFloat(_config.StatsMoneyOut);
                    }
                    if (fsm.FsmName != _config.FsmName) continue;
                    foreach (var machine in _machines.Values)
                    {
                        for (int i = 0; i < 7; i++)
                        {
                            if (path != machine.Path + "/" + _config.ButtonPaths[i]) continue;
                            machine.Buttons[i] = fsm;
                            var root = fsm.transform;
                            while (root != null && ScenePath.Of(root) != machine.Path) root = root.parent;
                            machine.Root = root;
                        }
                    }
                }
                catch (MissingReferenceException) { }
            }
        }

        private void BindVariables(Machine machine)
        {
            if (_config == null || machine.Start == null) return;
            var vars = machine.Start.FsmVariables;
            if (machine.Credit == null) machine.Credit = vars.FindFsmFloat(_config.CreditVariable);
            if (machine.Winnings == null) machine.Winnings = vars.FindFsmFloat(_config.WinningsVariable);
            if (machine.LastWin == null) machine.LastWin = vars.FindFsmFloat(_config.LastWinVariable);
            if (machine.Bet == null) machine.Bet = vars.FindFsmFloat(_config.BetVariable);
            if (machine.CanHold == null) machine.CanHold = vars.FindFsmBool(_config.CanHoldVariable);
            if (machine.Roll == null) machine.Roll = vars.FindFsmInt(_config.RollVariable);
            for (int i = 0; i < 3; i++)
            {
                if (machine.Reels[i] == null) machine.Reels[i] = vars.FindFsmString(_config.ReelVariables[i]);
                if (machine.Holds[i] == null) machine.Holds[i] = vars.FindFsmBool(_config.HoldVariables[i]);
                if (machine.ReelObjects[i] == null) machine.ReelObjects[i] = vars.FindFsmGameObject(_config.ReelObjectVariables[i]);
            }
            if (machine.Root == null) return;
            for (int i = 0; i < 4; i++)
            {
                if (machine.Texts[i] != null) continue;
                var text = machine.Root.Find(_config.TextPaths[i]);
                if (text != null) machine.Texts[i] = text.GetComponent<TextMesh>();
            }
        }

        private void InstallHooks(Machine machine)
        {
            if (_config == null || machine.HooksReady || machine.Start == null) return;
            bool all = machine.Credit != null && machine.Winnings != null && machine.Bet != null
                && machine.LastWin != null && machine.CanHold != null && machine.Roll != null;
            for (int i = 0; i < 7; i++)
            {
                var fsm = machine.Buttons[i];
                if (fsm == null) { all = false; continue; }
                byte action = (byte)i;
                all &= FsmHook.EnsureRemoteEntry(fsm, _config.IdleState);
                all &= Hook(machine, fsm, _config.ButtonStates[i], () =>
                {
                    if (!SessionActive()) return;
                    // Queue outside the PlayMaker call stack. Redirecting first prevents
                    // vanilla credit/cash mutations, including on the host's own controls.
                    FsmHook.FireRemoteEntry(fsm, _config.IdleState);
                    Queue(machine, action);
                });
            }
            for (int i = 0; i < 3; i++)
            {
                int reel = i;
                all &= machine.Reels[i] != null && machine.Holds[i] != null && machine.ReelObjects[i] != null;
                var state = FsmHook.FindState(machine.Start, _config.RollStates[i]);
                if (state == null) { all = false; continue; }
                FsmStateAction? random = null;
                try
                {
                    foreach (var action in state.Actions)
                        if (action.GetType().Name == _config.RandomAction) { random = action; break; }
                }
                catch { all = false; continue; }
                if (random == null) throw new InvalidOperationException("Missing configured slot RNG action.");
                RandomAction? saved = null;
                foreach (var entry in machine.RandomActions)
                    if (ReferenceEquals(entry.Action, random)) { saved = entry; break; }
                if (saved == null)
                {
                    saved = new RandomAction { Action = random, Enabled = random.Enabled };
                    machine.RandomActions.Add(saved);
                }
                var original = saved;
                all &= Hook(machine, machine.Start, _config.RollStates[i], () =>
                {
                    bool seeded = machine.Animating && machine.Animation != null;
                    original.Action.Enabled = original.Enabled && !seeded;
                    if (seeded && machine.Roll != null)
                        machine.Roll.Value = Symbol(machine.Animation!, reel);
                });
            }
            all &= FsmHook.EnsureRemoteEntry(machine.Start, _config.SpinState);
            all &= FsmHook.EnsureRemoteEntry(machine.Start, _config.FinishState);
            all &= Hook(machine, machine.Start, _config.FinishState, () =>
            {
                if (!machine.Animating || machine.Animation == null) return;
                machine.Animating = false;
                var session = SessionManager.Instance;
                if (session != null && machine.Animation.PlayerId == session.LocalPlayerId)
                    Queue(machine, SlotMachineIntent.Finish, machine.Animation.Round);
            });
            machine.HooksReady = all;
        }

        private static bool SessionActive()
        {
            var session = SessionManager.Instance;
            return session != null && (session.State == SessionState.Hosting || session.State == SessionState.Connected);
        }

        private bool Hook(Machine machine, PlayMakerFSM fsm, string stateName, Action callback)
        {
            var state = FsmHook.FindState(fsm, stateName);
            if (state == null) return false;
            foreach (var existing in machine.Hooks)
                if (ReferenceEquals(existing.State, state)) return true;
            try
            {
                var actions = state.Actions ?? new FsmStateAction[0];
                var hook = new FsmHookAction(() =>
                {
                    try { callback(); }
                    catch (Exception e) { Disable(machine, e); }
                });
                var expanded = new FsmStateAction[actions.Length + 1];
                expanded[0] = hook;
                Array.Copy(actions, 0, expanded, 1, actions.Length);
                state.Actions = expanded;
                machine.Hooks.Add(new InstalledHook { State = state, Action = hook });
                return true;
            }
            catch { return false; } // uninitialized inactive FSM; retry after Awake
        }

        private void ApplyPresentation(Machine machine)
        {
            var state = machine.View;
            if (state == null || machine.Start == null || _config == null) return;
            if (machine.Animating)
            {
                if (NativeSpinning(machine)) return;
                machine.Animating = false;
                if (!machine.Start.Fsm.Active) StopAnimation(machine);
            }
            if (state.Spinning && machine.HasPresentedRound && machine.PresentedRound == state.Round)
                return; // the visual finished; wait for the host's terminal balance
            if (NativeSpinning(machine)) StopAnimation(machine, true);
            WriteValues(machine);
            if (!state.Spinning) machine.Animation = null;
            if (!state.Spinning || !machine.Start.Fsm.Active || !machine.HooksReady) return;
            machine.Animation = state;
            machine.PresentedRound = state.Round;
            machine.HasPresentedRound = true;
            machine.Animating = true;
            // The ledger already debited one credit source. Start at the animation
            // chain, which never writes the shared cash balance or charges another bet.
            FsmHook.FireRemoteEntry(machine.Start, _config.SpinState);
        }

        private void WriteValues(Machine machine)
        {
            var state = machine.View;
            if (state == null || _config == null) return;
            if (machine.Credit != null) machine.Credit.Value = state.Credit;
            if (machine.Winnings != null) machine.Winnings.Value = state.Winnings;
            if (machine.LastWin != null) machine.LastWin.Value = state.Spinning ? 0 : state.LastWin;
            if (machine.Bet != null) machine.Bet.Value = state.Bet;
            if (machine.CanHold != null) machine.CanHold.Value = state.CanHold;
            var betButton = machine.Buttons[SlotMachineIntent.Bet];
            var bet = betButton != null ? betButton.FsmVariables.FindFsmFloat(_config.BetButtonVariable) : null;
            if (bet != null) bet.Value = state.Bet;
            for (int i = 0; i < 3; i++)
            {
                bool held = (state.HoldMask & (1 << i)) != 0;
                if (machine.Holds[i] != null) machine.Holds[i]!.Value = held;
                if (machine.Reels[i] != null) machine.Reels[i]!.Value = Symbol(state, i).ToString();
                // Raw reel stops differ from the strings rewritten for wildcard payout
                // evaluation. Display the original stops, including for late joiners.
                var reel = machine.ReelObjects[i] != null ? machine.ReelObjects[i]!.Value : null;
                if (reel != null && (!state.Spinning || held))
                    reel.transform.localEulerAngles = new Vector3(-36f * Symbol(state, i), 0f, 0f);
                var button = machine.Buttons[SlotMachineIntent.Hold1 + i];
                if (button != null && button.Fsm.Active && !state.Spinning)
                {
                    var toggle = button.FsmVariables.FindFsmBool(_config.HoldSwitchVariable);
                    if (toggle != null && toggle.Value != held)
                    {
                        string target = _config.HoldStates[held ? 1 : 0];
                        if (FsmHook.EnsureRemoteEntry(button, target)) FsmHook.FireRemoteEntry(button, target);
                    }
                }
            }
            SetText(machine.Texts[0], state.Credit);
            SetText(machine.Texts[1], state.Winnings);
            if (!state.Spinning) SetText(machine.Texts[2], state.LastWin);
            SetText(machine.Texts[3], state.Bet);
        }

        private void StopAnimation(Machine machine, bool force = false)
        {
            if (_config == null || machine.Start == null || (!force && machine.Animation == null)) return;
            machine.Animating = false;
            machine.Animation = null;
            if (machine.Start.Fsm.Active)
            {
                if (FsmHook.EnsureRemoteEntry(machine.Start, _config.FinishState))
                    FsmHook.FireRemoteEntry(machine.Start, _config.FinishState);
            }
            else
            {
                // PlayMaker 1.7.7 Stop resets its active state only with this flag.
                // An inactive presentation must not resume and add its win a second time
                // after disconnect. Restore the original restart policy immediately.
                bool restart = machine.Start.Fsm.RestartOnEnable;
                machine.Start.Fsm.RestartOnEnable = true;
                try { machine.Start.Fsm.Stop(); }
                finally { machine.Start.Fsm.RestartOnEnable = restart; }
                for (int i = 1; i <= 5; i++)
                    if (i != SlotMachineIntent.Spin && machine.Buttons[i] != null) machine.Buttons[i]!.enabled = true;
            }
            if (machine.Root != null)
            {
                var sound = machine.Root.Find(_config.SoundPath);
                if (sound != null) sound.gameObject.SetActive(false);
            }
        }

        private bool NativeSpinning(Machine machine)
        {
            if (machine.Start == null || !machine.Start.Fsm.Active || _config == null) return false;
            string state = machine.Start.ActiveStateName;
            // Both input waits precede the catalogued commit. The mouse-over wait's
            // name is read from the idle state's FINISHED transition, not guessed.
            if (string.IsNullOrEmpty(state) || state == _config.IdleState) return false;
            var idle = FsmHook.FindState(machine.Start, _config.IdleState);
            if (idle != null)
                foreach (var transition in idle.Transitions)
                    if (transition.EventName == "FINISHED" && state == transition.ToState) return false;
            return true;
        }

        private static byte Symbol(SlotMachineState state, int i) => i == 0 ? state.Reel1 : i == 1 ? state.Reel2 : state.Reel3;
        private static void SetText(TextMesh? text, int value) { if (text != null) text.text = value.ToString(); }

        private void ApplyCashoutAchievement(int amount)
        {
            if (_config == null || amount < _config.AchievementThreshold) return;
            try
            {
                var steam = FsmVariables.GlobalVariables.FindFsmGameObject(_config.AchievementObjectGlobal)?.Value;
                if (steam == null) return;
                foreach (var fsm in steam.GetComponents<PlayMakerFSM>())
                    if (fsm.FsmName == _config.AchievementFsm) fsm.SendEvent(_config.AchievementEvent);
            }
            catch (Exception e) { WinterMPPlugin.Log.LogDebug("Slot achievement: " + e.Message); }
        }
    }
}
