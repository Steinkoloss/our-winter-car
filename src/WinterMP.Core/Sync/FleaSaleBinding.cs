using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FleaSaleBinding
    {
        private readonly FleaSaleData _c;
        private readonly bool _guest;
        private readonly PlayMakerFSM _logic, _sell, _day, _cash, _envelope, _rent;
        private readonly FsmFloat _money, _price, _offer, _wallet;
        private readonly FsmInt _days;
        private readonly FsmState _request, _collect;
        private readonly FsmStateAction[] _requestActions, _collectActions;
        private readonly FsmSuppressor _logicPause = new FsmSuppressor(), _sellPause = new FsmSuppressor(), _dayPause = new FsmSuppressor();
        private readonly float _oldMoney, _oldOffer, _oldPrice;
        private readonly int _oldDays;
        private readonly bool _oldEnvelope;
        private readonly Dictionary<FsmFloat, float> _cartFloats = new Dictionary<FsmFloat, float>();
        private readonly Dictionary<FsmInt, int> _cartInts = new Dictionary<FsmInt, int>();
        private readonly string _oldLine;
        private readonly IList _bought;
        private readonly object[] _oldBought;
        private readonly FsmTransition[] _cashGlobals, _envelopeGlobals;
        private readonly FsmEvent[] _cashEvents, _envelopeEvents;
        private bool _installed;
        internal Vector3 CashPosition => _cash.transform.position;
        internal Vector3 EnvelopePosition => _envelope.transform.position;
        internal float Wallet { get => _wallet.Value; set => _wallet.Value = value; }
        internal int Weeks => Int(_cash, "Weeks").Value;
        internal bool RentOnly
        {
            get
            {
                if (Float(_cash, "Total").Value != 0) return false;
                foreach (var value in _bought)
                    if (value is not bool selected || selected) return false;
                return true;
            }
        }
        internal static bool OwnsCheckout(string path, string fsm) =>
            (path == "FleaMarket/LOD/FleaCashRegister/CashRegisterLogic" && fsm == "Data")
            || (SyncCatalog.FleaSale != null && path == SyncCatalog.FleaSale["cashPath"] && fsm == SyncCatalog.FleaSale["cashFsm"]);
        internal static bool OwnsEnvelope(string path, string fsm) =>
            (path == "FleaMarket/LOD/OpenHours/MoneyFlea" && fsm == "Use")
            || (SyncCatalog.FleaSale != null && path == SyncCatalog.FleaSale["envelopePath"] && fsm == SyncCatalog.FleaSale["envelopeFsm"]);
        private bool HoursOpen => _rent.transform.parent != null && _rent.transform.parent.gameObject.activeSelf;
        internal bool RentAvailable => HoursOpen && _rent.gameObject.activeSelf && _cash.gameObject.activeSelf
            && _logic.enabled && _logic.ActiveStateName != "State 5";
        internal bool CollectAvailable => HoursOpen && _envelope.gameObject.activeSelf && _envelope.enabled && _days.Value < 0
            && _money.Value > 0 && _offer.Value == _money.Value;

        internal static FleaSaleBinding? Bind(bool guest, Action<byte> queue)
        {
            SyncCatalog.EnsureLoaded();
            var c = SyncCatalog.FleaSale ?? throw new InvalidOperationException("Flea catalog unavailable.");
            var fsms = new Dictionary<string, PlayMakerFSM>();
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm != null) fsms[ScenePath.Of(fsm.transform) + "::" + fsm.FsmName] = fsm;
            }
            var found = new List<PlayMakerFSM>();
            foreach (var pair in new[] { new[] { "tablePath", "logic" }, new[] { "tablePath", "sell" },
                new[] { "tablePath", "day" }, new[] { "cashPath", "cashFsm" }, new[] { "envelopePath", "envelopeFsm" },
                new[] { "rentPath", "rentFsm" } })
            {
                if (!fsms.TryGetValue(c[pair[0]] + "::" + c[pair[1]], out var fsm)) return null;
                if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
                found.Add(fsm);
            }
            var b = new FleaSaleBinding(c, guest, found.ToArray());
            try { b.Install(queue); return b; }
            catch { b.Restore(); throw; }
        }

        private FleaSaleBinding(FleaSaleData c, bool guest, PlayMakerFSM[] f)
        {
            _c = c; _guest = guest; _logic = f[0]; _sell = f[1]; _day = f[2]; _cash = f[3]; _envelope = f[4]; _rent = f[5];
            _cashGlobals = _cash.Fsm.GlobalTransitions; _envelopeGlobals = _envelope.Fsm.GlobalTransitions;
            _cashEvents = _cash.Fsm.Events; _envelopeEvents = _envelope.Fsm.Events;
            _money = Float(_logic, c["money"]); _days = Int(_logic, c["days"]); _price = Float(_rent, c["weekPrice"]);
            _offer = Float(_envelope, "Money"); _wallet = FsmVariables.GlobalVariables.FindFsmFloat(c["cashGlobal"])
                ?? throw new InvalidOperationException("Missing native flea wallet.");
            _request = State(_cash, c["request"]); _collect = State(_envelope, c["collect"]);
            _requestActions = Actions(_request, "SetStringValue", "SetBoolValue", "FloatCompare");
            _collectActions = Actions(_collect, "SetStringValue", "MasterAudioPlaySound", "SetBoolValue", "SetFsmFloat", "FloatAdd", "ActivateGameObject");
            var inventory = _cash.FsmVariables.FindFsmGameObject("Inventory")?.Value;
            if (inventory == null || ScenePath.Of(inventory.transform) != c["inventoryPath"])
                throw new InvalidOperationException("Native flea inventory changed.");
            IList? bought = null;
            foreach (var component in inventory.GetComponents<MonoBehaviour>())
            {
                if (component == null || component.GetType().Name != "PlayMakerArrayListProxy"
                    || component.GetType().GetField("referenceName")?.GetValue(component) as string != c["bought"]) continue;
                if (bought != null) throw new InvalidOperationException("Ambiguous flea basket.");
                bought = component.GetType().GetProperty("arrayList")?.GetValue(component, null) as IList;
            }
            if (bought == null || bought.Count != Int(_cash, "MaxIndex").Value + 1 || bought.Count < 1 || bought.Count > 256)
                throw new InvalidOperationException("Native flea basket shape changed.");
            _bought = bought; _oldBought = new object[bought.Count];
            for (int i = 0; i < bought.Count; i++)
            {
                if (bought[i] is not bool) throw new InvalidOperationException("Non-boolean flea basket entry.");
                _oldBought[i] = bought[i];
            }
            Validate();
            _oldPrice = _price.Value;
            _oldMoney = _money.Value; _oldDays = _days.Value; _oldOffer = _offer.Value; _oldEnvelope = _envelope.gameObject.activeSelf;
            _oldLine = _envelope.FsmVariables.FindFsmString("Line").Value;
            foreach (var key in new[] { "Total", "TotalRent", "TotalFinal" }) { var v = Float(_cash, key); _cartFloats[v] = v.Value; }
            foreach (var key in new[] { "Weeks", "RentWeeks", "IndexProducts" }) { var v = Int(_cash, key); _cartInts[v] = v.Value; }
        }

        private void Install(Action<byte> queue)
        {
            _installed = true;
            _request.Actions = new FsmStateAction[] { new RequestAction(() =>
            {
                if (!_guest && !RentOnly)
                {
                    // Native mixed merchandise checkout retains its existing product/cart flow.
                    foreach (var action in _requestActions) action.OnEnter();
                }
                else queue(FleaSaleIntent.PayRent);
            }) };
            _collect.Actions = new FsmStateAction[] { new RequestAction(() => queue(FleaSaleIntent.CollectProceeds)) };
            if (_guest) { _logicPause.Suppress(_logic); _sellPause.Suppress(_sell); _dayPause.Suppress(_day); }
        }

        internal FleaSaleState Capture() => new FleaSaleState { MoneyTotal = _money.Value,
            RentDays = (ushort)Mathf.Clamp(_days.Value, 0, 1006), WeekPrice = _price.Value,
            Flags = (byte)((_days.Value >= 0 ? FleaSaleState.FlagRented : 0) | (CollectAvailable ? FleaSaleState.FlagCollectable : 0)) };

        internal void Commit(FleaSaleIntent request, float cash, FleaSaleState expected)
        {
            if (_guest) throw new InvalidOperationException("Guest cannot commit flea transactions.");
            if (request.Action == FleaSaleIntent.PayRent)
            {
                _wallet.Value = cash;
                for (int i = 0; i < request.Weeks; i++) _logic.SendEvent(_c["rentEvent"]);
                _envelope.gameObject.SetActive(false);
                if (_days.Value != expected.RentDays) throw new InvalidOperationException("Native rent result changed.");
            }
            else
            {
                // This is the native envelope's two-value transfer, before hiding it.
                _money.Value = 0; _wallet.Value = cash; _offer.Value = 0;
                _envelope.gameObject.SetActive(false);
            }
        }

        internal void Apply(FleaSaleState state)
        {
            if (!_guest) return;
            if (_price.Value != state.WeekPrice)
            {
                _price.Value = state.WeekPrice;
                Float(_cash, "TotalRent").Value = Math.Max(0, Weeks) * state.WeekPrice;
                Float(_cash, "TotalFinal").Value = Float(_cash, "Total").Value + Float(_cash, "TotalRent").Value;
                if (_cash.ActiveStateName == _c["idle"] || _cash.ActiveStateName == "Wait button") Enter(_cash, _c["idle"]);
            }
            _money.Value = state.MoneyTotal; _days.Value = (state.Flags & 1) != 0 ? state.RentDays : -1;
            _offer.Value = state.MoneyTotal;
            _envelope.FsmVariables.FindFsmString("Line").Value = "SALES " + state.MoneyTotal.ToString("0") + " MK";
            bool visible = (state.Flags & FleaSaleState.FlagCollectable) != 0;
            if (_envelope.gameObject.activeSelf != visible) _envelope.gameObject.SetActive(visible);
        }

        internal void Finish(byte action, bool accepted, bool funds = false)
        {
            if (action == FleaSaleIntent.PayRent)
            {
                if (accepted) Enter(_cash, _c["resetCart"]);
                else Enter(_cash, funds ? _c["funds"] : _c["idle"]);
            }
            else if (_envelope.gameObject.activeInHierarchy) Enter(_envelope, _c["idle"]);
        }

        internal void Restore()
        {
            if (!_installed) return;
            RestoreListings();
            _installed = false;
            if (_cash != null) { if (_cash.ActiveStateName == _request.Name) Enter(_cash, _c["idle"]); _request.Actions = _requestActions; }
            if (_envelope != null) { if (_envelope.ActiveStateName == _collect.Name) Enter(_envelope, _c["idle"]); _collect.Actions = _collectActions; }
            if (_guest && _logic != null && _envelope != null)
            {
                _price.Value = _oldPrice;
                _money.Value = _oldMoney; _days.Value = _oldDays; _offer.Value = _oldOffer;
                foreach (var pair in _cartFloats) pair.Key.Value = pair.Value;
                foreach (var pair in _cartInts) pair.Key.Value = pair.Value;
                _bought.Clear(); foreach (var selected in _oldBought) _bought.Add(selected);
                _envelope.FsmVariables.FindFsmString("Line").Value = _oldLine;
                _envelope.gameObject.SetActive(_oldEnvelope);
            }
            if (_cash != null) { _cash.Fsm.GlobalTransitions = _cashGlobals; _cash.Fsm.Events = _cashEvents; }
            if (_envelope != null) { _envelope.Fsm.GlobalTransitions = _envelopeGlobals; _envelope.Fsm.Events = _envelopeEvents; }
            _logicPause.Restore(); _sellPause.Restore(); _dayPause.Restore();
        }

        private static void Enter(PlayMakerFSM fsm, string state)
        {
            if (!FsmHook.EnsureRemoteEntry(fsm, state)) throw new InvalidOperationException("Flea presentation state unavailable.");
            FsmHook.FireRemoteEntry(fsm, state);
        }

        private sealed class RequestAction : FsmStateAction
        {
            private readonly Action _request;
            internal RequestAction(Action request) { _request = request; }
            public override void OnEnter() { _request(); }
        }
        private static FsmFloat Float(PlayMakerFSM f, string key) => f.FsmVariables.FindFsmFloat(key) ?? throw new InvalidOperationException("Missing flea float: " + key);
        private static FsmInt Int(PlayMakerFSM f, string key) => f.FsmVariables.FindFsmInt(key) ?? throw new InvalidOperationException("Missing flea int: " + key);
        private static FsmState State(PlayMakerFSM f, string key) => FsmHook.FindState(f, key) ?? throw new InvalidOperationException("Missing flea state: " + key);
        private static T Field<T>(FsmStateAction a, string key) => (T)(a.GetType().GetField(key)?.GetValue(a) ?? throw new InvalidOperationException("Missing flea argument: " + key));
        private static FsmStateAction[] Actions(FsmState state, params string[] names)
        {
            var a = state.Actions;
            if (a.Length != names.Length) throw new InvalidOperationException("Flea action count changed: " + state.Name);
            for (int i = 0; i < a.Length; i++) if (!a[i].Enabled || a[i].GetType().Name != names[i])
                throw new InvalidOperationException("Flea action changed: " + state.Name + "/" + i);
            return a;
        }
        private void Validate()
        {
            var debit = Actions(State(_cash, "Purchase"), "MasterAudioPlaySound", "FloatSubtract")[1];
            var rent = Actions(State(_logic, _c["rentCommit"]), "IntClamp", "IntAdd");
            var rental = Actions(State(_cash, "Add rent week"), "IntAdd", "SendEventByName", "Wait");
            bool transition = false;
            foreach (var t in _logic.Fsm.GlobalTransitions) if (t.EventName == _c["rentEvent"] && t.ToState == _c["rentCommit"]) transition = true;
            var target = Field<FsmEventTarget>(rental[1], "eventTarget");
            if (!transition || Field<FsmFloat>(_requestActions[2], "float1").Name != "TotalFinal"
                || Field<FsmFloat>(_requestActions[2], "float2").Name != _wallet.Name
                || Field<FsmFloat>(debit, "floatVariable").Name != _wallet.Name || Field<FsmFloat>(debit, "subtract").Name != "TotalFinal"
                || Field<FsmInt>(rent[0], "intVariable").Name != _days.Name || Field<FsmInt>(rent[0], "minValue").Value != 0
                || Field<FsmInt>(rent[0], "maxValue").Value != 999 || Field<FsmInt>(rent[1], "intVariable").Name != _days.Name
                || Field<FsmInt>(rent[1], "add").Value != 7 || Field<FsmInt>(rent[1], "add").UseVariable
                || _cash.Fsm.GetOwnerDefaultTarget(target.gameObject) != _logic.gameObject || target.fsmName.Value != _logic.FsmName
                || Field<FsmString>(rental[1], "sendEvent").Value != _c["rentEvent"]
                || _envelope.Fsm.GetOwnerDefaultTarget(Field<FsmOwnerDefault>(_collectActions[3], "gameObject")) != _logic.gameObject
                || Field<FsmString>(_collectActions[3], "fsmName").Value != _logic.FsmName
                || Field<FsmString>(_collectActions[3], "variableName").Value != _money.Name
                || Field<FsmFloat>(_collectActions[3], "setValue").Value != 0 || Field<FsmFloat>(_collectActions[3], "setValue").UseVariable
                || Field<FsmFloat>(_collectActions[4], "floatVariable").Name != _wallet.Name
                || Field<FsmFloat>(_collectActions[4], "add").Name != _offer.Name)
                throw new InvalidOperationException("Native flea payment binding changed.");
            foreach (var a in new[] { debit, rent[0], rent[1], _collectActions[3], _collectActions[4] })
                if (Field<bool>(a, "everyFrame")) throw new InvalidOperationException("Repeated native flea mutation.");
            if (Field<bool>(debit, "perSecond") || Field<bool>(_collectActions[4], "perSecond")) throw new InvalidOperationException("Continuous flea cash mutation.");
            foreach (var key in new[] { "idle", "resetCart", "funds" }) State(_cash, _c[key]);
            State(_envelope, _c["idle"]);
        }
    }
}
