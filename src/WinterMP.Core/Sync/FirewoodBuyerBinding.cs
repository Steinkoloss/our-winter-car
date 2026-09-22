using System;
using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FirewoodBuyerBinding
    {
        internal readonly uint Id;
        internal readonly PlayMakerFSM Payment, Buyer, Job, Lod, BuyerLod;
        private readonly FirewoodPaymentGuard _guard;
        private readonly bool _guest;
        private readonly FsmFloat _money;
        private readonly FsmString _label;
        private readonly GameObject _bill;
        private readonly Animation _arm, _leftArm;
        private readonly string _offerClip, _idleClip, _leftClip, _labelPrefix, _labelSuffix;
        private readonly FsmSuppressor[] _paused = { new FsmSuppressor(), new FsmSuppressor(), new FsmSuppressor() };
        private readonly List<FsmState> _states = new List<FsmState>();
        private readonly List<FsmStateAction[]> _originalActions = new List<FsmStateAction[]>();
        private readonly List<FsmStateAction[]> _installedActions = new List<FsmStateAction[]>();
        private readonly bool _originalRoot, _originalBuyer, _originalPayment, _originalBill, _originalRestart;
        private readonly Vector3 _originalPosition;
        private readonly Quaternion _originalRotation;
        private readonly float _originalMoney;
        private readonly string _originalLabel;
        private readonly WrapMode _originalWrap;
        private FirewoodBuyerState? _current, _sent, _remote;
        private bool _shownOffer, _shownBuyer, _failed;
        private float _retryAt;

        private FirewoodBuyerBinding(uint id, PlayMakerFSM payment, FirewoodPaymentGuard guard,
            PlayMakerFSM buyer, PlayMakerFSM job, PlayMakerFSM lod, PlayMakerFSM buyerLod, bool guest)
        {
            Id = id; Payment = payment; _guard = guard; Buyer = buyer; Job = job; Lod = lod; BuyerLod = buyerLod; _guest = guest;
            _money = Required(payment.FsmVariables.FindFsmFloat("Money"));
            _label = Required(payment.FsmVariables.FindFsmString("Value"));
            _bill = ObjectVariable(payment, "Bill");
            _arm = Required(ObjectVariable(payment, "CollarRight").GetComponent<Animation>());
            _offerClip = Clip(buyer, "State 2", 4);
            _idleClip = Clip(buyer, "State 1", FsmHook.NativeAction(Required(FsmHook.FindState(buyer, "State 1")), 0)?.GetType().Name == "ActivateGameObject" ? 1 : 0);
            if (_arm[_offerClip] == null || _arm[_idleClip] == null) throw new InvalidOperationException("Missing buyer arm animations: " + _offerClip + "/" + _idleClip);
            _leftArm = Required(ObjectVariable(Buyer, "CollarLeft").GetComponent<Animation>());
            _leftClip = Clip(Buyer, "State 1", FsmHook.NativeAction(Required(FsmHook.FindState(Buyer, "State 1")), 0)?.GetType().Name == "ActivateGameObject" ? 2 : 1, _leftArm);
            if (_leftArm[_leftClip] == null) throw new InvalidOperationException("Missing buyer idle animation.");
            var initial = Required(FsmHook.FindState(Payment, "State 2"));
            int formatIndex = FsmHook.NativeAction(initial, 1)?.GetType().Name == "BoolTest" ? 3 : 2;
            var parts = Field<FsmString[]>(Action(Payment, "State 2", formatIndex, "BuildString"), "stringParts");
            if (parts.Length != 3 || parts[0].UseVariable || parts[2].UseVariable || !ReferenceEquals(parts[1], _label))
                throw new InvalidOperationException("Changed payment label template.");
            _labelPrefix = parts[0].Value; _labelSuffix = parts[2].Value;
            Validate();
            _originalPosition = BuyerLod.transform.position; _originalRotation = BuyerLod.transform.rotation;
            _originalRoot = BuyerLod.gameObject.activeSelf; _originalBuyer = Buyer.gameObject.activeSelf;
            _originalPayment = Payment.gameObject.activeSelf; _originalBill = _bill.activeSelf;
            _originalMoney = _money.Value; _originalLabel = _label.Value; _originalRestart = Payment.Fsm.RestartOnEnable;
            _originalWrap = _arm[_offerClip].wrapMode;
            try
            {
                if (guest)
                {
                    if (!_paused[0].Suppress(Job) || !_paused[1].Suppress(Buyer) || !_paused[2].Suppress(BuyerLod))
                        throw new InvalidOperationException("Cannot pause local firewood decisions.");
                    Payment.Fsm.RestartOnEnable = false;
                    FsmHook.EnsureRemoteEntry(Payment, "Wait player");
                    // Customer 1 also sells a car through State 2. A replicated wood
                    // offer must never select that native purchase branch on a guest.
                    Replace(Required(FsmHook.FindState(Payment, "State 2")), new FsmStateAction[] { new FsmHookAction(PrepareOffer) });
                    Payment.gameObject.SetActive(false);
                    Buyer.gameObject.SetActive(false);
                }
                else InstallDistances();
            }
            catch { Restore(); throw; }
        }

        internal FirewoodBuyerState Capture()
        {
            byte flags = Buyer.gameObject.activeInHierarchy ? FirewoodBuyerState.BuyerPresent : (byte)0;
            float amount = 0;
            if (flags != 0 && _guard.Available) { flags |= FirewoodBuyerState.OfferReady; amount = _money.Value; }
            var position = BuyerLod.transform.position; var rotation = BuyerLod.transform.rotation;
            var next = new FirewoodBuyerState { NetId = Id, Flags = flags, Amount = amount, Revision = _current?.Revision ?? 0,
                Position = new WinterMP.Net.NetVector3(position.x, position.y, position.z),
                Rotation = new WinterMP.Net.NetQuaternion(rotation.x, rotation.y, rotation.z, rotation.w) };
            if (_current == null || !FirewoodBuyerPolicy.Same(_current, next)) next.Revision = unchecked(next.Revision + 1);
            _current = next;
            return FirewoodBuyerPolicy.Copy(next);
        }

        internal bool ShouldBroadcast(FirewoodBuyerState state, bool keepAlive)
        {
            if (!keepAlive && _sent != null && FirewoodBuyerPolicy.Same(_sent, state)) return false;
            _sent = FirewoodBuyerPolicy.Copy(state); return true;
        }

        internal void Receive(FirewoodBuyerState state)
        {
            if (_guest && state.NetId == Id && FirewoodBuyerPolicy.CanReceive(_remote, state)) _remote = FirewoodBuyerPolicy.Copy(state);
        }

        internal void CollectedLocally() { if (_guest) _retryAt = Time.unscaledTime + 1; }

        private void PrepareOffer()
        {
            _label.Value = _labelPrefix + _money.Value.ToString("0.##", CultureInfo.InvariantCulture) + _labelSuffix;
            FsmHook.FireRemoteEntry(Payment, "Wait player");
        }

        internal void Present()
        {
            if (!_guest || _failed) return;
            bool present = _remote != null && (_remote.Flags & FirewoodBuyerState.BuyerPresent) != 0;
            bool offer = present && (_remote!.Flags & FirewoodBuyerState.OfferReady) != 0;
            // The outer house LOD remains local. The native host checks all players,
            // so its buyer can stay available while only a guest visits the site.
            if (_remote != null)
            {
                BuyerLod.transform.position = new Vector3(_remote.Position.X, _remote.Position.Y, _remote.Position.Z);
                BuyerLod.transform.rotation = new Quaternion(_remote.Rotation.X, _remote.Rotation.Y, _remote.Rotation.Z, _remote.Rotation.W);
            }
            bool appeared = present && !_shownBuyer;
            BuyerLod.gameObject.SetActive(present);
            Buyer.gameObject.SetActive(present);
            _shownBuyer = present;
            if (appeared) _leftArm.Play(_leftClip);
            if (!offer)
            {
                if (Payment.ActiveStateName == "Wait button")
                {
                    var interaction = FsmVariables.GlobalVariables.FindFsmString("GUIinteraction");
                    var use = FsmVariables.GlobalVariables.FindFsmBool("GUIuse");
                    if (interaction != null && interaction.Value == _label.Value)
                    { interaction.Value = string.Empty; if (use != null) use.Value = false; }
                }
                Payment.gameObject.SetActive(false);
                _money.Value = 0; _label.Value = string.Empty;
                if (_shownOffer || appeared) { _arm.Play(_idleClip); _shownOffer = false; }
                return;
            }
            if (Time.unscaledTime < _retryAt) return;
            _money.Value = _remote!.Amount;
            _label.Value = _labelPrefix + _money.Value.ToString("0.##", CultureInfo.InvariantCulture) + _labelSuffix;
            if (!_shownOffer)
            {
                _arm[_offerClip].wrapMode = WrapMode.ClampForever;
                _arm.Play(_offerClip); _shownOffer = true;
            }
            bool repair = !Payment.gameObject.activeSelf || !Payment.Fsm.Started
                || (Payment.ActiveStateName != "Wait player" && Payment.ActiveStateName != "Wait button");
            _bill.SetActive(true);
            Payment.gameObject.SetActive(true);
            if (repair && Payment.gameObject.activeInHierarchy && Payment.Fsm.Started) PrepareOffer();
        }

        internal void Fail()
        {
            _failed = true;
            if (_guest && Payment != null) Payment.gameObject.SetActive(false);
        }

        internal void Restore()
        {
            for (int i = _states.Count - 1; i >= 0; i--)
                if (ReferenceEquals(_states[i].Actions, _installedActions[i])) _states[i].Actions = _originalActions[i];
            _states.Clear(); _originalActions.Clear(); _installedActions.Clear();
            if (!_guest) return;
            if (Payment != null)
            {
                Payment.gameObject.SetActive(false);
                BuyerLod.transform.position = _originalPosition; BuyerLod.transform.rotation = _originalRotation;
                _money.Value = _originalMoney; _label.Value = _originalLabel;
                _bill.SetActive(_originalBill); Buyer.gameObject.SetActive(_originalBuyer); BuyerLod.gameObject.SetActive(_originalRoot);
                Payment.gameObject.SetActive(_originalPayment); Payment.Fsm.RestartOnEnable = _originalRestart;
                _arm[_offerClip].wrapMode = _originalWrap;
                if (_shownOffer) _arm.Play(_idleClip);
            }
            foreach (var paused in _paused) paused.Restore();
        }

        private void Replace(FsmState state, FsmStateAction[] actions)
        {
            _states.Add(state); _originalActions.Add(state.Actions); _installedActions.Add(actions);
            state.Actions = actions;
            foreach (var action in actions) action.Init(state);
        }
    }
}
