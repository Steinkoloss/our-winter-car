using System;
using HutongGames.PlayMaker;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    /// <summary>One collection per native offer; only the host runs its cash and income additions.</summary>
    internal sealed class FirewoodPaymentGuard
    {
        private readonly PlayMakerFSM _fsm;
        private readonly FsmFloat _money;
        private readonly FsmStateAction[] _credits;
        private readonly bool[] _enabled;
        private bool _claimed, _reserved;

        internal FirewoodPaymentGuard(PlayMakerFSM fsm)
        {
            _fsm = fsm;
            _money = fsm.FsmVariables.FindFsmFloat("Money") ?? throw new InvalidOperationException("Missing firewood payment amount.");
            var state = FsmHook.FindState(fsm, "State 1") ?? throw new InvalidOperationException("Missing firewood payment entry.");
            string[] types = { "MasterAudioPlaySound", "ActivateGameObject", "SetStringValue", "SetBoolValue", "FloatAdd", "FloatAdd", "PlayAnimation" };
            for (int i = 0; i < types.Length; i++)
                if (FsmHook.NativeAction(state, i)?.GetType().Name != types[i]) throw new InvalidOperationException("Changed firewood payment actions.");
            if (FsmHook.NativeAction(state, types.Length) != null) throw new InvalidOperationException("Extra firewood payment action.");
            _credits = new[] { FsmHook.NativeAction(state, 4)!, FsmHook.NativeAction(state, 5)! };
            _enabled = new[] { _credits[0].Enabled, _credits[1].Enabled };
            string[] globals = { "PlayerMoney", "PlayerNetIncome" };
            for (int i = 0; i < _credits.Length; i++)
            {
                var action = _credits[i]; var type = action.GetType();
                var target = type.GetField("floatVariable").GetValue(action) as FsmFloat;
                var amount = type.GetField("add").GetValue(action) as FsmFloat;
                if (target == null || !target.UseVariable || target.Name != globals[i]
                    || amount == null || !amount.UseVariable || amount.Name != _money.Name
                    || (bool)type.GetField("everyFrame").GetValue(action) || (bool)type.GetField("perSecond").GetValue(action))
                    throw new InvalidOperationException("Changed firewood payment binding.");
            }
        }

        internal void Ready() { _claimed = _reserved = false; }
        internal bool Available => HostPaymentPolicy.CanCollect(_fsm.gameObject.activeInHierarchy, _fsm.enabled,
            _claimed || _reserved, _fsm.ActiveStateName, _money.Value);
        internal bool Reserve()
        {
            if (!HostPaymentPolicy.CanCollect(_fsm.gameObject.activeInHierarchy, _fsm.enabled,
                _claimed || _reserved, _fsm.ActiveStateName, _money.Value)) return false;
            _reserved = _claimed = true;
            return true;
        }
        internal void Enter(bool guest)
        {
            bool pay = !guest && (_reserved || !_claimed) && HostPaymentPolicy.ValidAmount(_money.Value);
            _reserved = false; _claimed = true;
            for (int i = 0; i < _credits.Length; i++) _credits[i].Enabled = pay && _enabled[i];
        }
        internal void Restore()
        {
            for (int i = 0; i < _credits.Length; i++) _credits[i].Enabled = _enabled[i];
        }
    }
}
