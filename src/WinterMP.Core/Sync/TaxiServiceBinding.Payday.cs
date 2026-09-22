using System;
using System.Collections;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class TaxiServiceBinding
    {
        private static uint _nextPaydayId;
        private uint _paydayId, _shownPaydayId, _openedPaydayId;
        private PlayMakerFSM _payday = null!, _paydaySheet = null!;
        private FsmBool _paydayUnread = null!;
        private GameObject _paydayEnvelope = null!;
        private IList _paydayRundown = null!;

        private void InitializePayday(Action<uint> sendRead)
        {
            _payday = Fsm(_phone.transform.parent.parent.parent.gameObject, _c["paymentsFsm"]);
            _paydayUnread = Required(_payday.FsmVariables.FindFsmBool(_c["rundownVariable"]));
            _paydayEnvelope = ObjectVariable(_payday, _c["rundownLetter"]);
            var envelope = Fsm(_paydayEnvelope, _c["envelopeFsm"]);
            _paydaySheet = Fsm(ObjectVariable(envelope, _c["sheetVariable"]), _c["sheetFsm"]);
            foreach (var component in _payday.GetComponents<MonoBehaviour>())
                if (component != null && component.GetType().Name == "PlayMakerArrayListProxy"
                    && Field<string>(component, "referenceName") == _c["rundownList"])
                {
                    if (_paydayRundown != null) throw new InvalidOperationException("Ambiguous native taxi payslip.");
                    _paydayRundown = Required(component.GetType().GetProperty("arrayList")?.GetValue(component, null) as IList);
                }
            if (_paydayRundown == null || _paydayRundown.Count != 8)
                throw new InvalidOperationException("Changed native taxi payslip size.");
            foreach (var value in _paydayRundown) if (!(value is float)) throw new InvalidOperationException("Changed taxi payslip value type.");
            var netCell = ActionAt(_payday, "Payment", 2, "ArrayListSet");
            var netIndex = Field<FsmInt>(netCell, "atIndex");
            var netValue = Field<FsmVar>(netCell, "variable");
            var reference = Field<FsmString>(netCell, "reference");
            if (Field<FsmOwnerDefault>(netCell, "gameObject").OwnerOption != OwnerDefaultOption.UseOwner
                || reference.UseVariable || reference.Value != _c["rundownList"]
                || netIndex.UseVariable || netIndex.Value != 7 || !netValue.useVariable
                || netValue.Type != VariableType.Float || netValue.variableName != "Money")
                throw new InvalidOperationException("Changed native taxi net payslip cell.");
            var close = ActionAt(_paydaySheet, _c["sheetCloseState"], 0, "SetFsmBool");
            if (Field<FsmOwnerDefault>(close, "gameObject").GameObject.Value != _payday.gameObject
                || Field<FsmString>(close, "fsmName").Value != _payday.FsmName
                || Field<FsmString>(close, "variableName").Value != _paydayUnread.Name
                || Field<FsmBool>(close, "setValue").Value)
                throw new InvalidOperationException("Changed native taxi payslip acknowledgement.");
            State(_payday, _c["settledState"]);
            if (!_guest)
            {
                var money = Required(_payday.FsmVariables.FindFsmFloat("Money"));
                var clamp = ActionAt(_payday, "Phone use", 2, "FloatClamp");
                if (!ReferenceEquals(Field<FsmFloat>(clamp, "floatVariable"), money)
                    || Field<FsmFloat>(clamp, "minValue").Value != 0)
                    throw new InvalidOperationException("Changed taxi net wage calculation.");
                NewPayday(); Observe(_payday, _c["settledState"], () => {
                    // Native Payment writes this cell only for positive wages. Its
                    // zero-pay branch otherwise displays the previous week's net.
                    _paydayRundown[7] = money.Value;
                    NewPayday();
                });
                return;
            }
            var original = new object[8]; _paydayRundown.CopyTo(original, 0);
            _restore.Add(() => { for (int i = 0; i < 8; i++) _paydayRundown[i] = original[i]; });
            RememberActive(_paydayEnvelope);
            Observe(envelope, _c["envelopeOpenState"], () => _openedPaydayId = _shownPaydayId);
            // Keep the local camera/menu flow; only its shared read flag becomes an intent.
            var state = State(_paydaySheet, _c["sheetCloseState"]); var actions = state.Actions;
            var installed = (FsmStateAction[])actions.Clone();
            var callback = new FsmHookAction(() => {
                uint id = _openedPaydayId; _openedPaydayId = 0;
                if (!_restored && id != 0) sendRead(id);
            });
            callback.Init(state); installed[Array.IndexOf(installed, close)] = callback; state.Actions = installed;
            _restore.Add(() => { if (ReferenceEquals(state.Actions, installed)) state.Actions = actions; });
            _restore.Add(() => {
                if (_openedPaydayId != 0 && _paydaySheet.gameObject.activeSelf)
                    _paydaySheet.SendEvent("FINISHED");
                _openedPaydayId = 0;
            });
        }

        private void NewPayday()
        {
            _nextPaydayId = unchecked(_nextPaydayId + 1); if (_nextPaydayId == 0) _nextPaydayId = 1;
            _paydayId = _nextPaydayId;
        }
        private void CapturePayday(TaxiServiceState state)
        {
            state.PaydayId = _paydayId;
            if (_paydayUnread.Value) state.PaydayFlags |= TaxiServiceState.UnreadPayday;
            if (_paydayEnvelope.activeSelf) state.PaydayFlags |= TaxiServiceState.PaydayEnvelope;
            for (int i = 0; i < 8; i++) state.PaydayRundown[i] = (float)_paydayRundown[i];
        }
        private void PresentPayday(TaxiServiceState state)
        {
            for (int i = 0; i < 8; i++) _paydayRundown[i] = state.PaydayRundown[i];
            _paydayUnread.Value = (state.PaydayFlags & TaxiServiceState.UnreadPayday) != 0;
            _paydayEnvelope.SetActive((state.PaydayFlags & TaxiServiceState.PaydayEnvelope) != 0);
            _shownPaydayId = state.PaydayId;
        }
        internal void ReadPayday(SessionManager session, TaxiPaydayReadIntent intent, byte actor)
        {
            bool near = GamblingSync.TryPlayerPosition(session, actor, out var position)
                && (position - _paydayEnvelope.transform.position).sqrMagnitude <= 16;
            if (!TaxiServicePolicy.CanReadPayday(Capture(), intent, actor, near))
            { SyncEventLog.Record("taxi-payday-read-rejected", actor + "/" + intent.PaydayId); return; }
            // Native sheet close writes only this flag; settlement and bank writes stay native host work.
            _paydayUnread.Value = false;
            SyncEventLog.Record("taxi-payday-read", actor + "/" + intent.PaydayId);
        }
    }
}
