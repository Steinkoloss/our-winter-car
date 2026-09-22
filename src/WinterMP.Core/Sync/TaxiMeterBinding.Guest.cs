using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class TaxiMeterBinding
    {
        private PlayMakerFSM? _terminalInput;
        private bool _fareInput;
        internal void SetFareInput(PlayMakerFSM terminal, bool enabled)
        {
            if (!_guest || _restored || terminal != _terminalInput) throw new InvalidOperationException("Taxi terminal input guard mismatch.");
            _fareInput = enabled; terminal.enabled = enabled;
        }
        private void InstallGuest(Action<TaxiMeterAction> send)
        {
            foreach (var fsm in new[] { _meter, _knob, _button, _display }) RememberVariables(fsm);
            bool driverBreak = Bool(_ring, "DriverBreak").Value;
            _restore.Add(() => Bool(_ring, "DriverBreak").Value = driverBreak);
            foreach (var go in new[] { _indicators, _lightIndicator })
            { bool active = go.activeSelf; _restore.Add(() => { if (go != null) go.SetActive(active); }); }
            var rotation = _modeKnob.localRotation; var material = _dome.sharedMaterial;
            string modeText = _modeText.text, costText = _costText.text;
            _restore.Add(() => { if (_modeKnob != null) _modeKnob.localRotation = rotation;
                if (_dome != null) _dome.sharedMaterial = material;
                if (_modeText != null) _modeText.text = modeText; if (_costText != null) _costText.text = costText; });
            Pause(_meter); Pause(_display);
            // Keep native payment paused unless the validated fare adapter owns
            // its input states. A failed fare binding must not clear guest Price.
            _terminalInput = Fsm(Child(_meter.transform.parent, "PaymentTerminal/Payment").gameObject, "Use");
            Pause(_terminalInput);
            foreach (var state in _knob.FsmStates)
            {
                if (state.Name == _c["knobIdle"] || state.Name == "Get scroll") continue;
                string name = state.Name;
                ReplaceControl(_knob, state, _c["knobIdle"], () =>
                {
                    if (name == _c["increase"]) send(TaxiMeterAction.IncreaseMode);
                    else if (name == _c["decrease"]) send(TaxiMeterAction.DecreaseMode);
                });
            }
            foreach (var state in _button.FsmStates)
            {
                if (state.Name == _c["buttonIdle"] || state.Name == "Wait button" || state.Name == "State 1") continue;
                string name = state.Name;
                ReplaceControl(_button, state, _c["buttonIdle"], () =>
                {
                    if (name == _c["toggle"]) send(TaxiMeterAction.ToggleLight);
                    else if (name == _c["reset"]) send(TaxiMeterAction.ResetTotals);
                });
            }
        }

        private void ReplaceControl(PlayMakerFSM fsm, FsmState state, string idle, Action callback)
        {
            var old = state.Actions; var transitions = state.Transitions;
            var action = new FsmHookAction(callback); action.Init(state);
            var installed = new FsmStateAction[] { action };
            var routes = new[] { new FsmTransition { FsmEvent = FsmEvent.Finished, ToState = idle } };
            state.Actions = installed; state.Transitions = routes;
            _restore.Add(() => { if (ReferenceEquals(state.Actions, installed)) state.Actions = old;
                if (ReferenceEquals(state.Transitions, routes)) state.Transitions = transitions; });
        }

        internal void Present(TaxiMeterState s)
        {
            if (!_guest || _restored) return;
            foreach (var fsm in _paused) if (fsm != null && fsm.enabled && !(fsm == _terminalInput && _fareInput)) fsm.enabled = false;
            for (int i = 0; i < _values.Length; i++) _values[i].Value = s.Values[i];
            _rotation.Value = s.Mode * 35; Need(_knob.FsmVariables.FindFsmFloat("Rot")).Value = _rotation.Value;
            // Legacy native SetRotation can produce a pose different from its
            // scalar mode. Mirror the observed host pose instead of rebuilding it.
            var q = s.KnobRotation; _modeKnob.localRotation = new Quaternion(q.X, q.Y, q.Z, q.W);
            Bool(_meter, "On").Value = (s.Flags & TaxiMeterState.MeterOn) != 0;
            Bool(_meter, "Off").Value = (s.Flags & TaxiMeterState.MeterOff) != 0;
            Bool(_knob, "On").Value = (s.Flags & TaxiMeterState.KnobOn) != 0;
            Bool(_knob, "ActivateCustomer").Value = (s.Flags & TaxiMeterState.CustomerEnabled) != 0;
            Bool(_ring, "DriverBreak").Value = (s.Flags & TaxiMeterState.DriverBreak) != 0;
            bool light = (s.Flags & TaxiMeterState.TaxiLight) != 0;
            Bool(_button, "TaxiLightOn").Value = light;
            Bool(_button, "ModeLotto").Value = s.Mode == 6;
            _indicators.SetActive((s.Flags & TaxiMeterState.Indicators) != 0);
            _lightIndicator.SetActive((s.Flags & TaxiMeterState.LightIndicator) != 0);
            _dome.sharedMaterial = light ? _lightOn : _lightOff;
            _modeText.text = s.ModeDisplay; _costText.text = s.Display;
        }

        private void Pause(PlayMakerFSM fsm)
        {
            var pause = new FsmSuppressor();
            if (!pause.Suppress(fsm)) throw new InvalidOperationException("Cannot pause guest meter calculation.");
            _paused.Add(fsm); _resume.Add(pause.Restore);
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
