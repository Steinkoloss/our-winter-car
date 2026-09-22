using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed class MilkConditionBinding
    {
        internal readonly PlayMakerFSM Use;
        internal readonly Rigidbody Body;
        internal readonly FsmFloat Condition;
        internal MilkConditionState? Captured, Received;
        internal MilkConditionState? Sent;
        internal float NextSend;
        internal bool Failed;
        private readonly MilkConditionData _catalog;
        private readonly bool _guest;
        private readonly List<FsmState> _states = new List<FsmState>();
        private readonly List<FsmStateAction[]> _original = new List<FsmStateAction[]>();
        private readonly List<FsmStateAction[]> _installed = new List<FsmStateAction[]>();
        private readonly FsmSuppressor _failure = new FsmSuppressor();
        private DrinkGate? _drink;
        private bool _saved;
        private float _originalCondition;
        private string _originalName = "", _originalState = "";

        private sealed class DrinkGate : FsmStateAction
        {
            private readonly MilkConditionBinding _binding;
            private readonly FsmStateAction[] _actions;
            private readonly bool[] _enabled;
            internal DrinkGate(MilkConditionBinding binding, FsmStateAction[] actions)
            {
                _binding = binding; _actions = actions; _enabled = new bool[actions.Length];
                for (int i = 0; i < actions.Length; i++) _enabled[i] = actions[i].Enabled;
            }
            public override void OnEnter()
            {
                bool allowed = !_binding.Failed && _binding.Received != null && _binding.Received.Spoiled == 0;
                for (int i = 0; i < _actions.Length; i++) _actions[i].Enabled = allowed && _enabled[i];
                if (!allowed) { _binding.ReturnToHostPhase(); return; }
                Finish();
            }
            internal void Restore()
            {
                Enabled = false;
                for (int i = 0; i < _actions.Length; i++) _actions[i].Enabled = _enabled[i];
            }
        }

        internal MilkConditionBinding(PlayMakerFSM use, Rigidbody body, MilkConditionData catalog, bool guest)
        {
            Use = use; Body = body; _catalog = catalog; _guest = guest;
            if (!use.Fsm.Initialized) use.Fsm.Init(use);
            Condition = use.FsmVariables.FindFsmFloat(catalog.Condition) ?? throw new InvalidOperationException("Missing milk condition.");
            var spoil = RequireActions(catalog.Spoil, "FloatCompare", "ArrayListGetClosestGameObject", "GetDistance", "FloatCompare", "FloatSubtract");
            var fridge = RequireActions(catalog.Fridge, "FloatSubtract");
            var bad = RequireActions(catalog.Bad, "SetName");
            var drink = RequireActions(catalog.Drink, "BoolTest", "BoolTest", "BoolTest");
            ValidateDecay(FsmHook.NativeAction(spoil, 4)!, "SpoilingRate");
            ValidateDecay(FsmHook.NativeAction(fridge, 0)!, "SpoilingRateFridge");
            var compare = FsmHook.NativeAction(spoil, 0)!;
            if (!ReferenceEquals(Field<FsmFloat>(compare, "float1"), Condition)
                || Field<FsmFloat>(compare, "float2").UseVariable || Field<FsmFloat>(compare, "float2").Value != 1
                || Field<FsmEvent>(compare, "lessThan").Name != "BAD" || Field<FsmEvent>(compare, "equal").Name != "BAD")
                throw new InvalidOperationException("Changed milk spoil threshold.");
            var rename = FsmHook.NativeAction(bad, 0)!;
            if (Field<FsmOwnerDefault>(rename, "gameObject").OwnerOption != OwnerDefaultOption.UseOwner
                || Field<FsmString>(rename, "name").UseVariable || Field<FsmString>(rename, "name").Value != catalog.SpoiledName
                || FsmHook.FindState(use, catalog.Idle) == null)
                throw new InvalidOperationException("Changed spoiled milk presentation.");
            if (!guest) return;
            if (!FsmHook.EnsureRemoteEntry(use, catalog.Idle) || !FsmHook.EnsureRemoteEntry(use, catalog.Bad))
                throw new InvalidOperationException("Cannot prepare milk presentation.");
            Replace(spoil, new FsmStateAction[] { new FsmHookAction(ReturnToHostPhase) });
            Replace(fridge, new FsmStateAction[] { new FsmHookAction(ReturnToHostPhase) });
            _drink = new DrinkGate(this, drink.Actions);
            var actions = new List<FsmStateAction>(drink.Actions); actions.Insert(0, _drink);
            Replace(drink, actions.ToArray());
        }

        private static T Field<T>(FsmStateAction action, string name) => (T)action.GetType().GetField(name).GetValue(action);
        private FsmState RequireActions(string name, params string[] types)
        {
            var state = FsmHook.FindState(Use, name) ?? throw new InvalidOperationException("Missing milk state " + name);
            for (int i = 0; i < types.Length; i++)
                if (FsmHook.NativeAction(state, i)?.GetType().Name != types[i]) throw new InvalidOperationException("Changed milk actions in " + name);
            if (FsmHook.NativeAction(state, types.Length) != null) throw new InvalidOperationException("Extra milk actions in " + name);
            return state;
        }
        private void ValidateDecay(FsmStateAction action, string rate)
        {
            if (!ReferenceEquals(Field<FsmFloat>(action, "floatVariable"), Condition)
                || !ReferenceEquals(Field<FsmFloat>(action, "subtract"), Use.FsmVariables.FindFsmFloat(rate))
                || Field<bool>(action, "everyFrame") || Field<bool>(action, "perSecond"))
                throw new InvalidOperationException("Changed milk decay binding.");
        }
        private void Replace(FsmState state, FsmStateAction[] actions)
        {
            _states.Add(state); _original.Add(state.Actions); _installed.Add(actions); state.Actions = actions;
            foreach (var action in actions) action.Init(state);
        }
        internal bool Ready => !Failed && Use != null && Body != null && Use.enabled && Use.gameObject.activeInHierarchy
            && Use.Fsm.Started && (Use.ActiveStateName == _catalog.Idle || Use.ActiveStateName == "Wait button 2"
                || Use.ActiveStateName == _catalog.Bad);
        internal void CaptureOriginal()
        {
            if (!_guest || _saved || !Ready) return;
            _originalCondition = Condition.Value; _originalName = Use.gameObject.name; _originalState = Use.ActiveStateName; _saved = true;
        }
        internal MilkConditionState? Capture(uint id)
        {
            if (_guest || !Ready) return null;
            var next = new MilkConditionState { NetId = id, Condition = Condition.Value,
                Spoiled = (byte)(Use.ActiveStateName == _catalog.Bad ? 1 : 0), Revision = Captured == null ? 1 : Captured.Revision };
            if (!MilkConditionPolicy.Valid(next)) return null;
            if (Captured != null && !MilkConditionPolicy.Same(Captured, next)) next.Revision++;
            Captured = next; return MilkConditionPolicy.Copy(next);
        }
        internal bool Apply(MilkConditionState state)
        {
            if (!_guest || !Ready) return false;
            if (!MilkConditionPolicy.CanReceive(Received, state)) return true;
            CaptureOriginal(); Condition.Value = state.Condition; Received = MilkConditionPolicy.Copy(state);
            if (state.Spoiled != 0 && Use.ActiveStateName != _catalog.Bad) FsmHook.FireRemoteEntry(Use, _catalog.Bad);
            else if (state.Spoiled == 0 && (Use.ActiveStateName == _catalog.Bad || Use.gameObject.name == _catalog.SpoiledName))
            {
                Use.gameObject.name = _catalog.ItemName; FsmHook.FireRemoteEntry(Use, _catalog.Idle);
            }
            return true;
        }
        private void ReturnToHostPhase() => FsmHook.FireRemoteEntry(Use, Received?.Spoiled == 1 ? _catalog.Bad : _catalog.Idle);
        internal void Fail(Exception error)
        {
            if (Failed) return;
            Failed = true;
            if (_guest) _failure.Suppress(Use);
            WinterMPPlugin.Log.LogWarning("Milk condition sync paused for one item: " + error.Message);
        }
        internal void Restore()
        {
            if (!_guest || Use == null) return;
            _drink?.Restore();
            for (int i = 0; i < _states.Count; i++)
                if (ReferenceEquals(_states[i].Actions, _installed[i])) _states[i].Actions = _original[i];
            _states.Clear(); _original.Clear(); _installed.Clear();
            _failure.Restore();
            if (_saved && Body != null && MilkConditionPolicy.ValidCondition(Condition.Value))
            {
                Condition.Value = _originalCondition; Use.gameObject.name = _originalName;
                if (FsmHook.EnsureRemoteEntry(Use, _originalState)) FsmHook.FireRemoteEntry(Use, _originalState);
            }
            _saved = false;
        }
    }
}
