using System;
using System.Collections.Generic;
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
    internal sealed partial class TaxiFareBinding
    {
        private readonly TaxiFareData _c;
        private readonly bool _guest;
        private readonly TaxiMeterBinding _meterGuard;
        private readonly PlayMakerFSM _job, _meter, _walker, _terminal, _cash;
        private readonly GameObject _car, _pivot, _receipt;
        private readonly TextMesh _display;
        private readonly FsmFloat _quoted, _offered, _price;
        private readonly FsmBool _paid;
        private readonly FsmString _label;
        private readonly List<Action> _restore = new List<Action>();
        private readonly TaxiMeterIntentOrder _order = new TaxiMeterIntentOrder();
        private uint _revision, _controlRevision = 1, _fareId;
        private bool _charged, _collected, _restored;
        private byte[]? _sent;

        internal static TaxiFareBinding? TryBind(SessionManager session, TaxiMeterBinding meterGuard, Action<TaxiFareAction> send)
        {
            SyncCatalog.EnsureLoaded(); var c = SyncCatalog.TaxiFare ?? throw new InvalidOperationException("Missing taxi fare catalog.");
            PlayMakerFSM? job = null, meter = null;
            foreach (var obj in ScenePath.ScanFsms())
            {
                var f = obj as PlayMakerFSM; if (f == null) continue;
                string path = ScenePath.Of(f.transform);
                if (path == c["jobPath"] && f.FsmName == c["jobFsm"]) Assign(ref job, f);
                if (path == c["meterPath"] && f.FsmName == c["meterFsm"]) Assign(ref meter, f);
            }
            if (job == null || meter == null) return null;
            Init(job); Init(meter); return new TaxiFareBinding(c, job, meter, session.IsHost, meterGuard, send);
        }
        private TaxiFareBinding(TaxiFareData c, PlayMakerFSM job, PlayMakerFSM meter, bool host, TaxiMeterBinding guard, Action<TaxiFareAction> send)
        {
            _c = c; _job = job; _meter = meter; _guest = !host; _meterGuard = guard;
            _walker = Fsm(Obj(meter, c["customerVariable"]), c["walkerFsm"]);
            _terminal = Fsm(Obj(_walker, c["terminalVariable"]), c["terminalFsm"]);
            _cash = Fsm(Obj(_walker, c["cashVariable"]), c["cashFsm"]);
            _car = Obj(job, "Car"); _pivot = Obj(_walker, "CarGetInPivot"); _receipt = Obj(_walker, c["receiptVariable"]);
            _quoted = Need(_terminal.FsmVariables.FindFsmFloat("Cost")); _offered = Need(_walker.FsmVariables.FindFsmFloat("Cost"));
            _price = Need(meter.FsmVariables.FindFsmFloat("Price")); _paid = Need(_walker.FsmVariables.FindFsmBool("Paid"));
            _label = Need(_cash.FsmVariables.FindFsmString("Value"));
            _display = Need(Field<FsmProperty>(ActionAt(_terminal, c["chargeState"], 4, "SetProperty"), "targetProperty").TargetObject.Value as TextMesh);
            Validate();
            try
            {
                InitializeReceipt();
                if (_guest) InstallGuest(send);
                else
                {
                    Observe(_walker, c["boardState"], BeginTrip);
                    Observe(_terminal, c["chargeState"], () => { _charged = true; Advance(); });
                    Observe(_cash, c["collectState"], () => { _collected = true; Advance(); });
                    foreach (string state in new[] { "Out", "Pay", "Add money", "No pay", "Leave" }) Observe(_walker, state, Advance);
                    PrepareEntry(_terminal, c["chargeState"]);
                    InstallHostReceipt();
                    if (_walker.transform.parent == _pivot.transform || _printed)
                    {
                        _fareId = 1; _collected = _paid.Value;
                        _charged = _printed || _paid.Value || _walker.ActiveStateName == "Pay" || _walker.ActiveStateName == "Offer money"
                            || _walker.ActiveStateName == "Add money" || _receipt.activeInHierarchy;
                    }
                }
                WinterMPPlugin.Log.LogInfo("Taxi fare bound: " + (_guest ? "shared terminal and cash input" : "native host payment"));
            }
            catch { Restore(); throw; }
        }
        private void Validate()
        {
            if (Obj(_terminal, "Walker") != _walker.gameObject || Obj(_cash, "Walker") != _walker.gameObject
                || Obj(_terminal, "Tripmeter") != _meter.gameObject || !_terminal.transform.IsChildOf(_car.transform)
                || !_cash.transform.IsChildOf(_walker.transform) || !_pivot.transform.IsChildOf(_car.transform))
                throw new InvalidOperationException("Changed taxi fare object links.");
            var quote = ActionAt(_terminal, _c["chargeState"], 2, "GetFsmFloat");
            if (Field<FsmOwnerDefault>(quote, "gameObject").GameObject.Value != _meter.gameObject
                || Field<FsmString>(quote, "variableName").Value != "Price" || Field<FsmString>(quote, "fsmName").Value != _meter.FsmName
                || !ReferenceEquals(Field<FsmFloat>(quote, "storeValue"), _quoted)) throw new InvalidOperationException("Changed taxi quote source.");
            var timer = ActionAt(_terminal, _c["chargeState"], 1, "SubtractFsmFloat");
            if (Field<FsmString>(timer, "variableName").Value != "Timer" || Field<FsmOwnerDefault>(timer, "gameObject").GameObject.Value != _job.gameObject
                || Field<FsmFloat>(timer, "subtractValue").Value != 4500) throw new InvalidOperationException("Changed taxi job adjustment.");
            var notify = ActionAt(_terminal, _c["chargeState"], 5, "SendEventByName");
            var target = Field<FsmEventTarget>(notify, "eventTarget");
            if (Field<FsmString>(notify, "sendEvent").Value != "CASHIER" || Field<FsmFloat>(notify, "delay").Value != 2
                || target.gameObject.GameObject.Value != _walker.gameObject || target.fsmName.Value != _walker.FsmName)
                throw new InvalidOperationException("Changed taxi quote completion.");
            var paid = ActionAt(_cash, _c["collectState"], 4, "SetFsmBool");
            if (Field<FsmOwnerDefault>(paid, "gameObject").GameObject.Value != _walker.gameObject || Field<FsmString>(paid, "variableName").Value != "Paid"
                || !Field<FsmBool>(paid, "setValue").Value) throw new InvalidOperationException("Changed taxi cash output.");
            var income = ActionAt(_walker, "Add money", 0, "AddFsmFloat");
            if (Field<FsmOwnerDefault>(income, "gameObject").GameObject.Value != _meter.gameObject
                || Field<FsmString>(income, "variableName").Value != "IncomeTotal" || !ReferenceEquals(Field<FsmFloat>(income, "addValue"), _offered))
                throw new InvalidOperationException("Changed native taxi income.");
            State(_walker, _c["arrivedState"]);
        }
        private void BeginTrip()
        {
            if (Obj(_walker, "GetInPivot") != _pivot) return;
            _fareId = unchecked(_fareId + 1); if (_fareId == 0) _fareId = 1;
            _charged = _collected = _printed = _taken = _handed = false; Advance();
        }
        private void Advance() { _controlRevision = unchecked(_controlRevision + 1); }
        internal TaxiFareState Capture()
        {
            var s = new TaxiFareState { Revision = ++_revision, ControlRevision = _controlRevision, FareId = _fareId,
                QuotedCost = _quoted.Value, OfferedCost = _offered.Value, TerminalDisplay = _display.text, OfferLabel = _label.Value };
            int stage = Need(_job.FsmVariables.FindFsmInt("JobStage")).Value;
            bool active = _car.activeInHierarchy && (stage == 2 || stage == 3) && Ready(_walker) && Ready(_terminal);
            if (active) s.Flags |= TaxiFareState.Active;
            if (_fareId == 0) { CaptureReceipt(s); return s; }
            bool arrived = _walker.ActiveStateName == _c["arrivedState"];
            if (arrived) s.Flags |= TaxiFareState.Arrived;
            if (_charged) s.Flags |= TaxiFareState.Charged;
            if (_paid.Value) s.Flags |= TaxiFareState.Paid;
            if (_receipt.activeInHierarchy) s.Flags |= TaxiFareState.ReceiptRequested;
            if (_cash.gameObject.activeInHierarchy) s.Flags |= TaxiFareState.CashVisible;
            if (active && arrived && !_charged && _price.Value > 0 && Need(_meter.FsmVariables.FindFsmBool("On")).Value
                && (_terminal.ActiveStateName == "Wait player" || _terminal.ActiveStateName == "Wait button")) s.Flags |= TaxiFareState.CanCharge;
            if (active && _charged && !_collected && !_paid.Value && Ready(_cash)
                && (_walker.ActiveStateName == "Pay" || _walker.ActiveStateName == "Offer money")) s.Flags |= TaxiFareState.CanCollect;
            CaptureReceipt(s); return s;
        }
        internal void Act(SessionManager session, TaxiFareIntent intent, byte actor)
        {
            var target = intent.Action == TaxiFareAction.Collect ? _cash : intent.Action == TaxiFareAction.GiveReceipt ? _receiptUse : _terminal;
            bool near = GamblingSync.TryPlayerPosition(session, actor, out var position) && (position - target.transform.position).sqrMagnitude <= 9;
            if (intent.FareId != _fareId || intent.ExpectedControlRevision != _controlRevision || !_order.Accept(actor, intent.Sequence)
                || !TaxiFarePolicy.CanAct(Capture(), intent, actor, near)
                || (intent.Action == TaxiFareAction.GiveReceipt && (!_items.TaxiReceiptHeldBy(actor)
                    || (_ticket.position - target.transform.position).sqrMagnitude > 9)))
            { SyncEventLog.Record("taxi-fare-rejected", actor + "/" + intent.FareId + "/" + intent.Sequence + "/" + intent.Action); return; }
            Advance();
            if (intent.Action == TaxiFareAction.Charge) { _charged = true; FsmHook.FireRemoteEntry(_terminal, _c["chargeState"]); }
            else if (intent.Action == TaxiFareAction.Collect)
            {
                _collected = true;
                // The native hand also awards the local player's achievement. A
                // remote collection applies only its audited Paid/hide outputs;
                // native Offer money -> Add money still owns the income credit.
                _paid.Value = true; _cash.gameObject.SetActive(false);
            }
            else ActReceipt(intent.Action);
            SyncEventLog.Record("taxi-fare", actor + "/" + intent.FareId + "/" + intent.Sequence + "/" + intent.Action);
        }
        internal void Forget(byte actor) { _order.Forget(actor); if (!_guest) Advance(); }
        internal bool ShouldSend(TaxiFareState s, bool keep)
        {
            uint revision = s.Revision; s.Revision = 0; byte[] bytes;
            try { bytes = PacketCodec.Encode(s); } finally { s.Revision = revision; }
            bool same = _sent != null && _sent.Length == bytes.Length;
            if (same) for (int i = 0; i < bytes.Length; i++) if (_sent![i] != bytes[i]) { same = false; break; }
            if (same && !keep) return false; _sent = bytes; return true;
        }
        private void Observe(PlayMakerFSM fsm, string name, Action callback)
        {
            var state = State(fsm, name); var old = state.Actions;
            if (!FsmHook.OnStateEnter(fsm, name, callback)) throw new InvalidOperationException("Cannot observe taxi payment.");
            var installed = state.Actions; _restore.Add(() => { if (ReferenceEquals(state.Actions, installed)) state.Actions = old; });
        }
        private void PrepareEntry(PlayMakerFSM fsm, string name)
        {
            var oldEvents = fsm.Fsm.Events; var oldTransitions = fsm.Fsm.GlobalTransitions;
            if (!FsmHook.EnsureRemoteEntry(fsm, name)) throw new InvalidOperationException("Cannot enter taxi payment.");
            var events = fsm.Fsm.Events; var transitions = fsm.Fsm.GlobalTransitions;
            _restore.Add(() => { if (ReferenceEquals(fsm.Fsm.Events, events)) fsm.Fsm.Events = oldEvents;
                if (ReferenceEquals(fsm.Fsm.GlobalTransitions, transitions)) fsm.Fsm.GlobalTransitions = oldTransitions; });
        }
        internal void Restore()
        {
            if (_restored) return;
            DisableGuestInput(); _restored = true;
            for (int i = _restore.Count - 1; i >= 0; i--) try { _restore[i](); } catch (Exception e) { WinterMPPlugin.Log.LogWarning("Taxi fare restore: " + e.Message); }
            _restore.Clear();
        }
        private static bool Ready(PlayMakerFSM fsm) => fsm.enabled && fsm.gameObject.activeInHierarchy && fsm.Fsm.Started;
        private static void Assign(ref PlayMakerFSM? target, PlayMakerFSM value) { if (target != null) throw new InvalidOperationException("Ambiguous taxi fare."); target = value; }
        private static void Init(PlayMakerFSM fsm) { if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm); }
        private static T Need<T>(T? value) where T : class => value ?? throw new InvalidOperationException("Missing taxi fare binding.");
        private static GameObject Obj(PlayMakerFSM fsm, string name) => Need(fsm.FsmVariables.FindFsmGameObject(name)?.Value);
        private static PlayMakerFSM Fsm(GameObject go, string name)
        {
            PlayMakerFSM? found = null; foreach (var f in go.GetComponents<PlayMakerFSM>()) if (f.FsmName == name) Assign(ref found, f);
            Init(Need(found)); return found!;
        }
        private static FsmState State(PlayMakerFSM fsm, string name) => Need(FsmHook.FindState(fsm, name));
        private static FsmStateAction ActionAt(PlayMakerFSM fsm, string state, int index, string type)
        {
            var a = Need(FsmHook.NativeAction(State(fsm, state), index));
            if (a.GetType().Name != type) throw new InvalidOperationException("Changed taxi fare action " + state + "/" + index); return a;
        }
        private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name).GetValue(target);
    }
}
