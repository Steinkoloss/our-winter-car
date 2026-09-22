using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;

namespace WinterMP.Core.Sync
{
    internal sealed class AdvertPhoneBinding
    {
        internal const string Waiting = "WinterMP advert approval";
        internal readonly byte Id;
        internal readonly PlayMakerFSM Calling;
        internal readonly PlayMakerFSM Handle;
        internal readonly PlayMakerFSM? Cord, Bill;
        internal readonly GameObject Ringing;
        internal bool Selected;
        private readonly List<Action> _restore = new List<Action>();
        private readonly FsmState _call;
        private readonly FsmStateAction[] _callActions;
        internal string Number => Calling.FsmVariables.FindFsmString("Number").Value;
        internal bool Running => Calling.enabled && Calling.gameObject.activeInHierarchy && Calling.Fsm.Started;
        internal bool Connected => Id == 2 || Cord!.FsmVariables.FindFsmBool("CordPhone").Value && Bill!.FsmVariables.FindFsmBool("PhonePaid").Value;
        internal bool HostIdle => !Ringing.activeSelf && (!Running || Calling.ActiveStateName == "State 1" || Calling.ActiveStateName == "Beep beep");
        internal Vector3 Position => Handle.transform.position;

        internal AdvertPhoneBinding(byte id, PlayMakerFSM calling, PlayMakerFSM handle, PlayMakerFSM? cord,
            PlayMakerFSM? bill, GameObject ringing, AdvertPhoneSync owner)
        {
            Id = id; Calling = calling; Handle = handle; Cord = cord; Bill = bill; Ringing = ringing;
            try
            {
                foreach (var f in new[] { calling, handle, cord, bill }) if (f != null && !f.Fsm.Initialized) f.Fsm.Init(f);
                foreach (string variable in new[] { "Number", "CallerSubtitle", "CallerAudioVariation" })
                    if (calling.FsmVariables.FindFsmString(variable) == null) throw new InvalidOperationException("Phone string missing.");
                if (calling.FsmVariables.FindFsmFloat("CallerCallLenght") == null
                    || id != 2 && (cord?.FsmVariables.FindFsmBool("CordPhone") == null || bill?.FsmVariables.FindFsmBool("PhonePaid") == null
                        || bill.FsmVariables.FindFsmFloat("Connects") == null || bill.FsmVariables.FindFsmFloat("Minutes") == null
                        || calling.FsmVariables.FindFsmGameObject("PhoneBills")?.Value != bill.gameObject))
                    throw new InvalidOperationException("Phone line/billing binding changed.");
                var find = State("Find number"); var check = State("Check type"); var hangup = State("Hangup 2");
                _call = State("Call"); _callActions = _call.Actions;
                Shape(hangup, "MasterAudioStopAllOfSound", "SendEventByName", "MasterAudioPlaySound", "Wait");
                if (id == 2) Shape(_call, "MasterAudioPlaySound", "SetStringValue", "Wait");
                else
                {
                    Shape(_call, "MasterAudioPlaySound", "AddFsmFloat", "AddFsmFloat", "SetStringValue", "Wait");
                    for (int i = 1; i <= 2; i++)
                    {
                        var a = _callActions[i];
                        if (calling.Fsm.GetOwnerDefaultTarget(Field<FsmOwnerDefault>(a, "gameObject")) != bill!.gameObject
                            || Field<FsmString>(a, "fsmName").Value != "Data"
                            || Field<FsmString>(a, "variableName").Value != (i == 1 ? "Connects" : "Minutes")
                            || Field<FsmFloat>(a, "addValue").UseVariable || Field<FsmFloat>(a, "addValue").Value != (i == 1 ? 1f : .2f)
                            || Field<bool>(a, "everyFrame") != (i == 2) || Field<bool>(a, "perSecond") != (i == 2))
                            throw new InvalidOperationException("Native phone charges changed.");
                    }
                }
                var wait = _callActions[_callActions.Length - 1];
                var target = Field<FsmEventTarget>(hangup.Actions[1], "eventTarget");
                if (Field<FsmFloat>(wait, "time").Name != "CallerCallLenght" || !Field<bool>(wait, "realTime")
                    || Field<FsmEvent>(wait, "finishEvent").Name != "FINISHED"
                    || Field<FsmString>(hangup.Actions[1], "sendEvent").Value != "CALLED" || (int)target.target != 2
                    || target.gameObject.GameObject.Name != "FoundListing" || target.fsmName.Value != "Data"
                    || Field<FsmFloat>(hangup.Actions[1], "delay").Value != 0 || Field<bool>(hangup.Actions[1], "everyFrame"))
                    throw new InvalidOperationException("Native phone completion changed.");
                RequireTransition(_call, "FINISHED", "Hangup 2");
                var states = calling.Fsm.States; var globals = calling.Fsm.GlobalTransitions; var events = calling.Fsm.Events;
                var waiting = new FsmState(calling.Fsm) { Name = Waiting, Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] };
                var extended = new List<FsmState>(states) { waiting }; calling.Fsm.States = extended.ToArray();
                _restore.Add(() => { calling.Fsm.States = states; calling.Fsm.GlobalTransitions = globals; calling.Fsm.Events = events; });
                foreach (string state in new[] { Waiting, "Delay", "Call", "Beep beep" })
                    if (!FsmHook.EnsureRemoteEntry(calling, state)) throw new InvalidOperationException("Phone entry unavailable.");
                Hook(find, () =>
                {
                    Selected = Number == SyncCatalog.AdvertPhone!["number"];
                    _call.Actions = _callActions;
                    if (Selected) FsmHook.FireRemoteEntry(calling, "Delay");
                });
                Hook(check, () =>
                {
                    if (!Selected) return;
                    FsmHook.FireRemoteEntry(calling, Waiting);
                    owner.BeginLocal(this);
                });
                Hook(hangup, () =>
                {
                    if (!Selected) return;
                    FsmHook.FireRemoteEntry(calling, "Beep beep");
                    owner.CompleteLocal(this);
                });
                _restore.Add(() => _call.Actions = _callActions);
            }
            catch { Restore(); throw; }
        }
        internal void Present()
        {
            var c = SyncCatalog.AdvertPhone!;
            Calling.FsmVariables.FindFsmString("CallerAudioVariation").Value = c["audio"];
            Calling.FsmVariables.FindFsmString("CallerSubtitle").Value = c["subtitle"];
            Calling.FsmVariables.FindFsmFloat("CallerCallLenght").Value = WinterMP.Net.Sync.AdvertCallLedger.Duration;
            _call.Actions = Id == 2 ? _callActions : new[] { _callActions[0], _callActions[3], _callActions[4] };
            FsmHook.FireRemoteEntry(Calling, "Call");
        }
        internal void Stop()
        {
            if (Running && Selected) FsmHook.FireRemoteEntry(Calling, "Beep beep");
        }
        internal void Charge(bool connection, float delta)
        {
            if (Bill == null) return;
            var variable = Bill.FsmVariables.FindFsmFloat(connection ? "Connects" : "Minutes");
            float value = variable.Value + (connection ? 1 : .2f * delta);
            if (!(value >= 0 && value < 10000000)) throw new InvalidOperationException("Invalid phone usage.");
            variable.Value = value;
        }
        internal void Restore()
        { for (int i = _restore.Count - 1; i >= 0; i--) _restore[i](); _restore.Clear(); }
        private FsmState State(string name) => FsmHook.FindState(Calling, name) ?? throw new InvalidOperationException("Phone state missing: " + name);
        private void Hook(FsmState state, Action callback)
        {
            var old = state.Actions;
            var action = new FsmHookAction(callback); action.Init(state);
            var actions = new List<FsmStateAction>(old); actions.Insert(0, action); state.Actions = actions.ToArray();
            _restore.Add(() => state.Actions = old);
        }
        internal static T Field<T>(FsmStateAction a, string name) => (T)(a.GetType().GetField(name)?.GetValue(a)
            ?? throw new InvalidOperationException("Phone action field missing: " + name));
        internal static void Shape(FsmState s, params string[] names)
        {
            if (s.Actions.Length != names.Length) throw new InvalidOperationException("Phone action count changed: " + s.Name);
            for (int i = 0; i < names.Length; i++)
                if (!s.Actions[i].Enabled || s.Actions[i].GetType().Name != names[i]) throw new InvalidOperationException("Phone action changed: " + s.Name);
        }
        internal static void RequireTransition(FsmState state, string ev, string to)
        {
            foreach (var t in state.Transitions) if (t.EventName == ev && t.ToState == to) return;
            throw new InvalidOperationException("Phone transition changed: " + state.Name);
        }
    }
}
