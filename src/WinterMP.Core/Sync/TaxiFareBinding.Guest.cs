using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class TaxiFareBinding
    {
        private bool _guestInstalled;
        private void InstallGuest(Action<TaxiFareAction> send)
        {
            RememberVariables(_terminal); RememberVariables(_cash);
            float cost = _offered.Value; bool paid = _paid.Value, visible = _cash.gameObject.activeSelf, enabled = _cash.enabled;
            string text = _display.text;
            _restore.Add(() => { _offered.Value = cost; _paid.Value = paid; if (_display != null) _display.text = text;
                if (_cash != null) { _cash.enabled = enabled; _cash.gameObject.SetActive(visible); } });
            foreach (var state in _terminal.FsmStates)
            {
                if (state.Name == "Wait button" || state.Name == "Wait button 2" || state.Name == "Wait button 3"
                    || state.Name == "Wait player 2" || state.Name == "Wait player 3") continue;
                if (state.Name == "Wait player")
                {
                    var original = state.Actions;
                    // Keep only native GUI clearing and ray picking; host state
                    // grants readiness, rather than the guest's independent meter.
                    if (original.Length != 5) throw new InvalidOperationException("Changed taxi terminal input.");
                    ActionAt(_terminal, state.Name, 2, "MousePickEvent");
                    Replace(_terminal, state, new[] { original[0], original[1], original[2] }, "Wait button");
                }
                else
                {
                    string name = state.Name;
                    Callback(_terminal, state, () => {
                        if (name == _c["chargeState"]) send(TaxiFareAction.Charge);
                        else if (name == _c["printState"]) send(TaxiFareAction.PrintReceipt);
                        else if (name == _c["takeState"]) send(TaxiFareAction.TakeReceipt);
                    });
                }
            }
            foreach (var state in _cash.FsmStates)
            {
                if (state.Name == "Wait player" || state.Name == "Wait button") continue;
                string name = state.Name;
                Callback(_cash, state, () => { if (name == _c["collectState"]) send(TaxiFareAction.Collect); });
            }
            foreach (string name in new[] { "Wait player", "Wait player 2", "Wait player 3" }) PrepareEntry(_terminal, name);
            InstallGuestReceipt(send);
            _guestInstalled = true; _cash.enabled = false; _cash.gameObject.SetActive(false);
        }
        private void Callback(PlayMakerFSM fsm, FsmState state, Action callback)
        {
            var action = new FsmHookAction(callback); action.Init(state); Replace(fsm, state, new FsmStateAction[] { action }, "Wait player");
        }
        private void Replace(PlayMakerFSM fsm, FsmState state, FsmStateAction[] actions, string destination)
        {
            State(fsm, destination); var old = state.Actions; var oldTransitions = state.Transitions;
            var routes = new[] { new FsmTransition { FsmEvent = FsmEvent.Finished, ToState = destination } };
            state.Actions = actions; state.Transitions = routes;
            _restore.Add(() => { if (ReferenceEquals(state.Actions, actions)) state.Actions = old;
                if (ReferenceEquals(state.Transitions, routes)) state.Transitions = oldTransitions; });
        }
        internal void Present(TaxiFareState state)
        {
            if (!_guest || _restored) return;
            _quoted.Value = state.QuotedCost; _offered.Value = state.OfferedCost;
            _paid.Value = (state.Flags & TaxiFareState.Paid) != 0;
            _label.Value = state.OfferLabel; Need(_cash.FsmVariables.FindFsmFloat("Money")).Value = state.OfferedCost;
            _display.text = state.TerminalDisplay;
            bool charge = (state.Flags & TaxiFareState.CanCharge) != 0, collect = (state.Flags & TaxiFareState.CanCollect) != 0;
            if (!charge && _terminal.enabled) ClearPrompt(_terminal, "ACCEPT PAYMENT");
            if (!collect && _cash.enabled) ClearPrompt(_cash, _label.Value);
            bool print = (state.ReceiptFlags & TaxiFareState.CanPrint) != 0, take = (state.ReceiptFlags & TaxiFareState.CanTake) != 0;
            if (!print && _terminal.enabled) ClearPrompt(_terminal, "PRINT RECEIPT");
            if (!take && _terminal.enabled) ClearPrompt(_terminal, "TAKE RECEIPT");
            bool input = charge || print || take;
            string suffix = print ? " 2" : take ? " 3" : "";
            _meterGuard.SetFareInput(_terminal, input);
            if (input && _terminal.Fsm.Started && _terminal.ActiveStateName != "Wait player" + suffix && _terminal.ActiveStateName != "Wait button" + suffix)
                FsmHook.FireRemoteEntry(_terminal, "Wait player" + suffix);
            _cash.enabled = collect;
            PresentReceipt(state);
            _cash.gameObject.SetActive((state.Flags & TaxiFareState.CashVisible) != 0);
        }
        internal void DisableGuestInput()
        {
            if (!_guest || !_guestInstalled || _restored) return;
            try
            {
                ClearPrompt(_terminal, "ACCEPT PAYMENT"); ClearPrompt(_terminal, "PRINT RECEIPT"); ClearPrompt(_terminal, "TAKE RECEIPT"); ClearPrompt(_cash, _label.Value);
                ClearReceiptPrompt();
                if (_receiptUse != null) _receiptUse.enabled = false;
                _meterGuard.SetFareInput(_terminal, false); _cash.enabled = false;
            }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("Taxi fare input cleanup: " + e.Message); }
        }
        private static void ClearPrompt(PlayMakerFSM fsm, string label)
        {
            if (fsm == null || !fsm.ActiveStateName.StartsWith("Wait button", StringComparison.Ordinal)) return;
            var text = FsmVariables.GlobalVariables.FindFsmString("GUIinteraction");
            if (text != null && text.Value == label)
            { text.Value = ""; var use = FsmVariables.GlobalVariables.FindFsmBool("GUIuse"); if (use != null) use.Value = false; }
        }
        private void RememberVariables(PlayMakerFSM fsm)
        {
            foreach (var v in fsm.FsmVariables.FloatVariables) { float value = v.Value; _restore.Add(() => v.Value = value); }
            foreach (var v in fsm.FsmVariables.IntVariables) { int value = v.Value; _restore.Add(() => v.Value = value); }
            foreach (var v in fsm.FsmVariables.BoolVariables) { bool value = v.Value; _restore.Add(() => v.Value = value); }
            foreach (var v in fsm.FsmVariables.StringVariables) { string value = v.Value; _restore.Add(() => v.Value = value); }
        }
    }
}
