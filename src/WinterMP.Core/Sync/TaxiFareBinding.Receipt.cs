using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class TaxiFareBinding
    {
        private ItemWorldSync _items = null!;
        private PlayMakerFSM _receiptUse = null!;
        private Rigidbody _ticket = null!;
        private Transform _printer = null!, _fingers = null!;
        private GameObject _print = null!;
        private bool _printed, _taken, _handed;
        private TaxiReceiptStage _shownReceipt = (TaxiReceiptStage)255;

        private void InitializeReceipt()
        {
            _items = Need(WorldSyncManager.Instance).ItemSync;
            _receiptUse = Fsm(_receipt, "Use");
            _ticket = Need(Obj(_terminal, _c["ticketVariable"]).GetComponent<Rigidbody>());
            _print = Obj(_terminal, "TicketPrint");
            var printParent = ActionAt(_terminal, _c["printState"], 4, "SetParent");
            _printer = Need(Field<FsmGameObject>(printParent, "parent").Value).transform;
            var giveParent = ActionAt(_receiptUse, _c["giveState"], 1, "SetParent");
            _fingers = Need(Field<FsmGameObject>(giveParent, "parent").Value).transform;
            ValidateReceipt();
            if (_guest)
            {
                var parent = _ticket.transform.parent; var position = _ticket.transform.localPosition; var rotation = _ticket.transform.localRotation;
                bool kinematic = _ticket.isKinematic, print = _print.activeSelf, trigger = _receipt.activeSelf, enabled = _receiptUse.enabled;
                _restore.Add(() => { if (_ticket != null) { _ticket.transform.parent = parent; _ticket.transform.localPosition = position;
                    _ticket.transform.localRotation = rotation; _ticket.isKinematic = kinematic; }
                    if (_print != null) _print.SetActive(print); if (_receipt != null) _receipt.SetActive(trigger);
                    if (_receiptUse != null) _receiptUse.enabled = enabled; });
            }
            _items.RegisterTaxiReceipt(_ticket); _restore.Add(_items.UnregisterTaxiReceipt);
        }
        private void ValidateReceipt()
        {
            if (!_printer.IsChildOf(_car.transform) || !_fingers.IsChildOf(_walker.transform)
                || Obj(_receiptUse, "Walker") != _walker.gameObject)
                throw new InvalidOperationException("Changed taxi receipt references.");
            foreach (var pair in new[] { new[] { "1", "OdoTotal", "Trip" }, new[] { "2", "IncomeReceipts", "Cost" } })
            {
                var add = ActionAt(_terminal, _c["printState"], int.Parse(pair[0]), "AddFsmFloat");
                if (Field<FsmOwnerDefault>(add, "gameObject").GameObject.Value != _meter.gameObject
                    || Field<FsmString>(add, "variableName").Value != pair[1]
                    || !ReferenceEquals(Field<FsmFloat>(add, "addValue"), _terminal.FsmVariables.FindFsmFloat(pair[2]))
                    || Field<bool>(add, "everyFrame") || Field<bool>(add, "perSecond"))
                    throw new InvalidOperationException("Changed taxi receipt accounting.");
            }
            var take = ActionAt(_terminal, _c["takeState"], 2, "SetParent");
            if (Field<FsmOwnerDefault>(take, "gameObject").GameObject.Value != _ticket.gameObject || (Field<FsmGameObject>(take, "parent").Value != null || Field<FsmGameObject>(take, "parent").UseVariable))
                throw new InvalidOperationException("Changed taxi receipt release.");
            var item = Need(_receiptUse.FsmVariables.FindFsmGameObject("Item"));
            var lookup = ActionAt(_receiptUse, "State 2", 0, "GetChild");
            if (Field<FsmString>(lookup, "childName").Value != _ticket.name || Field<FsmString>(lookup, "withTag").Value != "PART"
                || Field<FsmOwnerDefault>(lookup, "gameObject").GameObject.Name != "ItemPivot"
                || !ReferenceEquals(Field<FsmGameObject>(lookup, "storeResult"), item))
                throw new InvalidOperationException("Changed taxi receipt hand lookup.");
            var give = ActionAt(_receiptUse, _c["giveState"], 3, "SendEventByName");
            var target = Field<FsmEventTarget>(give, "eventTarget");
            if (Field<FsmString>(give, "sendEvent").Value != "CASHIER" || target.gameObject.GameObject.Value != _walker.gameObject
                || target.fsmName.Value != _walker.FsmName || Field<FsmFloat>(give, "delay").Value != 0)
                throw new InvalidOperationException("Changed taxi receipt completion.");
            var back = ActionAt(_receiptUse, "State 3", 0, "SetParent");
            if (Field<FsmGameObject>(back, "parent").Value != _printer.gameObject)
                throw new InvalidOperationException("Changed taxi receipt return.");
        }
        private void InstallHostReceipt()
        {
            _handed = _ticket.transform.parent == _fingers || _receiptUse.ActiveStateName == "State 3";
            _taken = _handed || _ticket.transform.parent != _printer;
            string state = _terminal.ActiveStateName;
            _printed = _taken || state == "State 1" || state == "State 3" || state == "State 4" || state == "Reset"
                || state == "Wait player 3" || state == "Wait button 3";
            InstallDepartureDistance();
            Observe(_terminal, _c["printState"], ReceiptPrinted);
            Observe(_terminal, _c["takeState"], () => { _taken = true; Advance(); });
            Observe(_receiptUse, _c["giveState"], ReceiptGiven);
            Observe(_receiptUse, "State 3", Advance);
            Observe(_walker, "State 3", Advance);
            PrepareEntry(_terminal, _c["printState"]); PrepareEntry(_terminal, _c["takeState"]); PrepareEntry(_receiptUse, _c["giveState"]);
        }
        private sealed class DepartureDistance : FsmStateAction
        {
            private readonly PlayMakerFSM _walker;
            private readonly FsmGameObject _camera;
            private readonly FsmFloat _distance;
            internal DepartureDistance(PlayMakerFSM walker, FsmGameObject camera, FsmFloat distance)
            { _walker = walker; _camera = camera; _distance = distance; }
            public override void OnEnter() { Read(); }
            public override void OnUpdate() { Read(); }
            private void Read()
            {
                try
                {
                    if (_walker == null || _camera.Value == null) return;
                    float distance = Vector3.Distance(_walker.transform.position, _camera.Value.transform.position);
                    var session = SessionManager.Instance;
                    if (session != null && session.IsHost)
                        foreach (var player in session.Players)
                            if (GamblingSync.TryPlayerPosition(session, player.PlayerId, out var position))
                                distance = Mathf.Min(distance, Vector3.Distance(_walker.transform.position, position));
                    _distance.Value = distance;
                }
                catch (Exception e) { Enabled = false; WinterMPPlugin.Log.LogWarning("Taxi departure distance disabled: " + e.Message); }
            }
        }
        private void InstallDepartureDistance()
        {
            var state = State(_walker, _c["departureState"]);
            var read = ActionAt(_walker, state.Name, 10, "GetDistance");
            var compare = ActionAt(_walker, state.Name, 11, "FloatCompare");
            var distance = Need(_walker.FsmVariables.FindFsmFloat("Distance"));
            var camera = Field<FsmGameObject>(read, "target");
            if (camera.Name != "SavePlayerCam" || !ReferenceEquals(Field<FsmFloat>(read, "storeResult"), distance)
                || !Field<bool>(read, "everyFrame") || Field<FsmFloat>(compare, "float2").Value != 99
                || Field<FsmEvent>(compare, "greaterThan").Name != "PROCEED")
                throw new InvalidOperationException("Changed taxi customer departure check.");
            // Host-only distance can hide Char and suspend its ten-second receipt
            // return while a guest is still beside the departing customer.
            var old = state.Actions; var installed = (FsmStateAction[])old.Clone();
            var action = new DepartureDistance(_walker, camera, distance); action.Init(state);
            for (int i = 0; i < installed.Length; i++) if (ReferenceEquals(installed[i], read)) installed[i] = action;
            state.Actions = installed;
            _restore.Add(() => { if (ReferenceEquals(state.Actions, installed)) state.Actions = old; });
        }
        private void ReceiptPrinted() { _printed = true; _taken = _handed = false; _items.SetTaxiReceiptLoose(false); Advance(); }
        private void ReceiptGiven() { _handed = true; _items.SetTaxiReceiptLoose(false); Advance(); }
        private void ActReceipt(TaxiFareAction action)
        {
            if (action == TaxiFareAction.PrintReceipt) { ReceiptPrinted(); FsmHook.FireRemoteEntry(_terminal, _c["printState"]); }
            else if (action == TaxiFareAction.TakeReceipt) { _taken = true; FsmHook.FireRemoteEntry(_terminal, _c["takeState"]); }
            else
            {
                Need(_receiptUse.FsmVariables.FindFsmGameObject("Item")).Value = _ticket.gameObject;
                ReceiptGiven(); FsmHook.FireRemoteEntry(_receiptUse, _c["giveState"]);
            }
        }
        private void CaptureReceipt(TaxiFareState s)
        {
            string state = _terminal.ActiveStateName;
            s.ReceiptStage = !_printed ? TaxiReceiptStage.Hidden : _handed
                ? (_ticket.transform.parent == _fingers ? TaxiReceiptStage.Customer : TaxiReceiptStage.Returned)
                : _taken ? TaxiReceiptStage.Loose : state == "Wait player 3" || state == "Wait button 3" ? TaxiReceiptStage.Ready : TaxiReceiptStage.Printing;
            bool active = (s.Flags & TaxiFareState.Active) != 0;
            if (_fareId != 0 && active && _charged && !_printed && (state == "Wait player 2" || state == "Wait button 2")) s.ReceiptFlags |= TaxiFareState.CanPrint;
            if (active && s.ReceiptStage == TaxiReceiptStage.Ready) s.ReceiptFlags |= TaxiFareState.CanTake;
            if (_receipt.activeInHierarchy) s.ReceiptFlags |= TaxiFareState.ReceiptTriggerVisible;
            if (_print.activeInHierarchy) s.ReceiptFlags |= TaxiFareState.PrintVisible;
            if (active && _paid.Value && Ready(_receiptUse) && _walker.ActiveStateName == "State 3" && s.ReceiptStage == TaxiReceiptStage.Loose)
                s.ReceiptFlags |= TaxiFareState.CanGive;
            bool hand = s.ReceiptStage == TaxiReceiptStage.Customer;
            s.ReceiptPosition = (hand ? _ticket.transform.localPosition : _ticket.position).ToNet();
            s.ReceiptRotation = (hand ? _ticket.transform.localRotation : _ticket.rotation).ToNet();
            _items.SetTaxiReceiptLoose(s.ReceiptStage == TaxiReceiptStage.Loose);
        }
        private void InstallGuestReceipt(Action<TaxiFareAction> send)
        {
            RememberVariables(_receiptUse);
            Callback(_receiptUse, State(_receiptUse, _c["giveState"]), () => {
                if (Obj(_receiptUse, "Item") == _ticket.gameObject && _items.TaxiReceiptHeldLocally) send(TaxiFareAction.GiveReceipt);
            });
            Callback(_receiptUse, State(_receiptUse, "State 3"), () => { });
            _receiptUse.enabled = false; _receipt.SetActive(false); _print.SetActive(false);
        }
        private void ClearReceiptPrompt()
        {
            if (_receiptUse == null || !_receiptUse.enabled || _receiptUse.ActiveStateName != "Wait button") return;
            var use = FsmVariables.GlobalVariables.FindFsmBool("GUIuse"); if (use != null) use.Value = false;
        }
        private void PresentReceipt(TaxiFareState s)
        {
            _items.SetTaxiReceiptLoose(s.ReceiptStage == TaxiReceiptStage.Loose);
            if (s.ReceiptStage != _shownReceipt)
            {
                bool loose = s.ReceiptStage == TaxiReceiptStage.Loose, hand = s.ReceiptStage == TaxiReceiptStage.Customer;
                _ticket.transform.parent = loose ? null : hand ? _fingers : _printer;
                if (loose) { _ticket.position = s.ReceiptPosition.ToUnity(); _ticket.rotation = s.ReceiptRotation.ToUnity(); _ticket.isKinematic = false; }
                else { _ticket.transform.localPosition = hand ? s.ReceiptPosition.ToUnity() : Vector3.zero;
                    _ticket.transform.localRotation = hand ? s.ReceiptRotation.ToUnity() : Quaternion.identity; _ticket.isKinematic = true; }
                _shownReceipt = s.ReceiptStage;
            }
            _print.SetActive((s.ReceiptFlags & TaxiFareState.PrintVisible) != 0);
            _receipt.SetActive((s.ReceiptFlags & TaxiFareState.ReceiptTriggerVisible) != 0);
            bool give = (s.ReceiptFlags & TaxiFareState.CanGive) != 0;
            if (!give) ClearReceiptPrompt();
            _receiptUse.enabled = give;
        }
    }
}
