using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FleaSaleBinding
    {
        private readonly Dictionary<FsmState, FsmStateAction[]> _listingActions = new Dictionary<FsmState, FsmStateAction[]>();
        private readonly Dictionary<PlayMakerFSM, FsmTransition[]> _listingGlobals = new Dictionary<PlayMakerFSM, FsmTransition[]>();
        private readonly Dictionary<PlayMakerFSM, FsmEvent[]> _listingEvents = new Dictionary<PlayMakerFSM, FsmEvent[]>();
        private PlayMakerFSM? _pricing;
        private GameObject? _sheet;
        private BoxCollider? _tableTrigger;
        private IList _listingIds = null!;
        private IDictionary _listingPrices = null!;
        private bool _sharedPriceOpen;
        private Action<Exception>? _listingFailure;
        internal Transform Table => _logic.transform;
        internal bool IsGuest => _guest;
        internal string ListingName => _c["listingName"];
        internal string ListingIdPrefix => _c["listingIdPrefix"];
        internal bool ListingsAvailable => _days.Value >= 0 && _logic.ActiveStateName != "State 5";
        internal bool PricingOpen => _sheet != null && _sheet.activeSelf;
        internal IEnumerable<string> ListingKeys { get { foreach (var key in _listingIds) if (key is string s) yield return s; } }
        internal bool TryPrice(string key, out float price)
        {
            price = 0;
            if (!_listingIds.Contains(key) || !_listingPrices.Contains(key) || _listingPrices[key] is not float value) return false;
            price = value; return true;
        }
        internal bool OnTable(Vector3 position) => _tableTrigger != null && _tableTrigger.bounds.Contains(position);

        internal void InstallListings(Action<GameObject> offer, Action<float> submit, Action<string> sell, Action<Exception> failure)
        {
            _listingFailure = failure;
            foreach (var obj in ScenePath.ScanFsms())
            {
                var f = obj as PlayMakerFSM;
                if (f != null && ScenePath.Of(f.transform) == _c["pricingPath"] && f.FsmName == _c["pricingFsm"]) _pricing = f;
            }
            if (_pricing == null) throw new InvalidOperationException("Native flea price sheet missing.");
            if (!_pricing.Fsm.Initialized) _pricing.Fsm.Init(_pricing);
            _sheet = _pricing.transform.parent.gameObject;
            _tableTrigger = _logic.GetComponent<BoxCollider>();
            if (_tableTrigger == null || !_tableTrigger.isTrigger) throw new InvalidOperationException("Flea table trigger changed.");
            _listingIds = Proxy<IList>(_logic.gameObject, "PlayMakerArrayListProxy", _c["listingIds"], "arrayList");
            _listingPrices = Proxy<IDictionary>(_logic.gameObject, "PlayMakerHashTableProxy", _c["listingPrices"], "hashTable");
            var set = State(_pricing, "Set price"); Actions(set, "SetFsmFloat", "SendEventByName");
            var cancel = State(_pricing, "Cancel"); Actions(cancel, "SendEventByName");
            State(_pricing, "Close");
            Remember(_pricing); Remember(_logic);
            Replace(set, () =>
            {
                if (!_sharedPriceOpen) { NativeActions(set); return; }
                float price = Float(_pricing, "Price").Value;
                ClosePrice(); submit(price);
            });
            Replace(cancel, () => { if (_sharedPriceOpen) { ClosePrice(); submit(-1); } else NativeActions(cancel); });
            if (_guest) return;
            var offerState = State(_logic, "Set price 2"); Actions(offerState, "ActivateGameObject");
            Replace(offerState, () =>
            {
                var item = _logic.FsmVariables.FindFsmGameObject("Item").Value;
                if (item != null && item.name == ListingName) { Enter(_logic, "Wait 2"); offer(item); }
                else NativeActions(offerState);
            });
            var sale = State(_logic, "Find item");
            Actions(sale, "ArrayListRemove", "FloatAdd", "HashTableRemove", "GetStringLength", "IntAdd", "GetStringLeft", "FindChild");
            var random = State(_logic, "Find item 2");
            Actions(random, "ArrayListGetRandom", "ArrayListRemove", "HashTableGet", "FloatAdd", "HashTableRemove", "GetStringLength", "IntAdd", "GetStringLeft", "FindChild");
            Replace(sale, () => DispatchSale(sale, sell, false));
            Replace(random, () => DispatchSale(random, sell, true));
            var reset = State(_logic, "State 1");
            Actions(reset, "ArrayListRevertToSnapShot", "HashTableRevertSnapShot", "SetIntValue", "FloatCompare");
            Replace(reset, () => { NativeActions(reset); ClearExpiredListings(); });
            // A direct/stale PRICESET must never bypass host item validation.
            var freeze = State(_logic, "Freeze object");
            Actions(freeze, "SetParent", "SetLayer", "SetIsKinematic", "BoolTest");
            Replace(freeze, () =>
            {
                var item = _logic.FsmVariables.FindFsmGameObject("Item").Value;
                if (item != null && item.name == ListingName && !_logic.FsmVariables.FindFsmBool("Initialization").Value)
                    Enter(_logic, "Wait");
                else NativeActions(freeze);
            });
        }
        private void DispatchSale(FsmState state, Action<string> sell, bool random)
        {
            if (random)
            {
                if (_listingIds.Count == 0) { Enter(_logic, "Player LOD"); return; }
                _listingActions[state][0].OnEnter();
            }
            string key = _logic.FsmVariables.FindFsmString("ItemName").Value;
            if (key.StartsWith(ListingName + "OW", StringComparison.Ordinal))
            {
                sell(key);
                if (_logic.ActiveStateName == state.Name) Enter(_logic, "Player LOD");
            }
            else NativeActions(state, random ? 1 : 0);
        }
        internal void OpenPrice()
        {
            if (PricingOpen || _sheet == null) return;
            _sharedPriceOpen = true; _sheet.SetActive(true);
        }
        private void ClosePrice()
        {
            _sharedPriceOpen = false;
            if (_pricing != null && PricingOpen) Enter(_pricing, "Close");
        }
        internal void CancelSharedPrice() { if (_sharedPriceOpen) ClosePrice(); }
        internal void AddListing(string key, ushort price)
        {
            if (_guest || _listingIds.Contains(key) || _listingPrices.Contains(key)) throw new InvalidOperationException("Flea identity collision.");
            _listingPrices.Add(key, (float)price);
            try { _listingIds.Add(key); } catch { _listingPrices.Remove(key); throw; }
        }
        internal void ClearExpiredListings()
        {
            if (_guest || _days.Value >= 0 || !_logic.Fsm.Started
                || (_logic.ActiveStateName != "State 1" && _logic.ActiveStateName != "State 2")) return;
            // Native RESET restores proxy snapshots which may contain listings
            // loaded from the previous save, including subsequently sold objects.
            var keys = new List<string>();
            foreach (var key in _listingIds) if (key is string s && s.StartsWith(ListingName + "OW", StringComparison.Ordinal)) keys.Add(s);
            foreach (var key in _listingPrices.Keys) if (key is string s && s.StartsWith(ListingName + "OW", StringComparison.Ordinal) && !keys.Contains(s)) keys.Add(s);
            foreach (var key in keys)
            {
                while (_listingIds.Contains(key)) _listingIds.Remove(key);
                _listingPrices.Remove(key);
            }
        }
        internal void SellListing(string key, GameObject item)
        {
            if (_guest || !TryPrice(key, out float price) || !BankTransferPolicy.IsFinite(_money.Value + price))
                throw new InvalidOperationException("Flea sale no longer available.");
            _listingIds.Remove(key); _listingPrices.Remove(key); _money.Value += price;
            _logic.FsmVariables.FindFsmGameObject("ItemSold").Value = item;
            Float(_logic, "Price").Value = price;
            Enter(_logic, "Remove object");
        }
        private void Remember(PlayMakerFSM f)
        {
            _listingGlobals[f] = f.Fsm.GlobalTransitions; _listingEvents[f] = f.Fsm.Events;
        }
        private void Replace(FsmState s, Action callback)
        {
            _listingActions.Add(s, s.Actions); s.Actions = new FsmStateAction[] { new RequestAction(() =>
            {
                try { callback(); }
                catch (Exception e)
                {
                    _listingFailure?.Invoke(e);
                }
            }) };
        }
        private void NativeActions(FsmState s, int start = 0)
        {
            var actions = _listingActions[s];
            for (int i = start; i < actions.Length; i++) actions[i].OnEnter();
            var owner = ReferenceEquals(FsmHook.FindState(_logic, s.Name), s) ? _logic : _pricing;
            if (owner == null || owner.ActiveStateName != s.Name) return;
            // The replacement action owns completion; original immediate actions
            // no longer appear in the state's completion array.
            foreach (var transition in s.Transitions)
                if (transition.EventName == "FINISHED") { Enter(owner, transition.ToState); break; }
        }
        private void RestoreListings()
        {
            if (_sharedPriceOpen) ClosePrice();
            foreach (var pair in _listingActions) pair.Key.Actions = pair.Value;
            foreach (var pair in _listingGlobals) if (pair.Key != null) pair.Key.Fsm.GlobalTransitions = pair.Value;
            foreach (var pair in _listingEvents) if (pair.Key != null) pair.Key.Fsm.Events = pair.Value;
            _listingActions.Clear(); _listingGlobals.Clear(); _listingEvents.Clear();
        }
        private static T Proxy<T>(GameObject go, string type, string name, string property) where T : class
        {
            T? found = null;
            foreach (var c in go.GetComponents<MonoBehaviour>())
                if (c != null && c.GetType().Name == type && c.GetType().GetField("referenceName")?.GetValue(c) as string == name)
                {
                    if (found != null) throw new InvalidOperationException("Ambiguous flea collection: " + name);
                    found = c.GetType().GetProperty(property)?.GetValue(c, null) as T;
                }
            return found ?? throw new InvalidOperationException("Missing flea collection: " + name);
        }
    }
}
