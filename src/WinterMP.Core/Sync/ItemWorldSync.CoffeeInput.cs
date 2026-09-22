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
    internal sealed partial class ItemWorldSync
    {
        private const string CoffeeRequestState = "WinterMP coffee request";
        private void ValidateCoffee(CoffeeData c)
        {
            var drink = PackageStateActions(_coffeeCup!.Fsm, c["drink"], "ActivateGameObject", "GetChild", "SetFsmFloat", "SetFsmFloat", "SetPosition", "SetPosition",
                "SetColorRGBA", "SetMaterialColor", "SendEventByName", "SetFloatValue", "SetFloatValue", "SetFloatValue", "Wait");
            if (PackageField<FsmString>(drink.Actions[8], "sendEvent")?.Value != "DRINKCOFFEEHOME"
                || PackageField<FsmString>(drink.Actions[2], "variableName")?.Value != "CoffeeHomeCaffeine"
                || PackageField<FsmString>(drink.Actions[3], "variableName")?.Value != "CoffeeHomeCoffee")
                throw new InvalidOperationException("Native home coffee drink effect changed.");
            var fill = FsmHook.FindState(_coffeeCup.Fsm, "Pour")!;
            if (FsmHook.NativeAction(fill, 4)?.GetType().Name != "FloatAdd" || PackageField<FsmFloat>(FsmHook.NativeAction(fill, 4)!, "add")?.Value != .16f)
                throw new InvalidOperationException("Native coffee transfer rate changed.");
            foreach (string state in new[] { c["delay"], c["drink"] })
                if (!FsmHook.EnsureRemoteEntry(_coffeeCup.Fsm, state)) throw new InvalidOperationException("Coffee cup state missing.");
            foreach (string state in new[] { c["waitClosed"], c["waitOpen"] })
                if (!FsmHook.EnsureRemoteEntry(_coffeeCap!, state)) throw new InvalidOperationException("Coffee lid state missing.");
        }
        private void ReplaceCoffeeState(PlayMakerFSM fsm, string name, Action callback)
        {
            var state = FsmHook.FindState(fsm, name) ?? throw new InvalidOperationException("Missing coffee state " + name);
            var original = state.Actions; var hook = new FsmHookAction(() => { try { callback(); } catch (Exception e) { FailCoffee(e); } }); hook.Init(state);
            state.Actions = new FsmStateAction[] { hook };
            _coffeeRestore.Add(() => state.Actions = original);
        }
        private void InstallCoffeeInput(CoffeeData c)
        {
            ReplaceCoffeeState(_coffeeCap!, c["open"], () => SendCoffee(_coffeePot!, CoffeeAction.OpenLid));
            ReplaceCoffeeState(_coffeeCap!, c["close"], () => SendCoffee(_coffeePot!, CoffeeAction.CloseLid));
            var fill = FsmHook.FindState(_coffeeCup!.Fsm, c["fill"]) ?? throw new InvalidOperationException("Coffee fill state missing.");
            var fillActions = fill.Actions;
            var pour = new CoffeePourAction(this); pour.Init(fill); fill.Actions = new FsmStateAction[] { pour };
            _coffeeRestore.Add(() => fill.Actions = fillActions);
            var use = _coffeeCup.Fsm; var original = use.Fsm.States;
            var request = new FsmState(use.Fsm) { Name = CoffeeRequestState, Actions = new FsmStateAction[] {
                new FsmHookAction(() => { try { SendCoffee(_coffeeCup!, CoffeeAction.Drink); } catch (Exception e) { FailCoffee(e); } }) }, Transitions = new FsmTransition[0] };
            var states = new List<FsmState>(original); states.Add(request); use.Fsm.States = states.ToArray();
            _coffeeRestore.Add(() => { if (use != null) use.Fsm.States = original; });
            var check = FsmHook.FindState(use, c["checkDrink"])!;
            foreach (var transition in check.Transitions)
                if (transition.ToState == c["drink"])
                { string target = transition.ToState; transition.ToState = CoffeeRequestState; _coffeeRestore.Add(() => transition.ToState = target); }
        }
        private sealed class CoffeePourAction : FsmStateAction
        {
            private readonly ItemWorldSync _owner;
            private float _next;
            internal CoffeePourAction(ItemWorldSync owner) { _owner = owner; }
            public override void OnEnter() { _next = 0; OnUpdate(); }
            public override void OnUpdate()
            {
                try
                {
                    var cup = _owner._coffeeCup; var pot = _owner._coffeePot;
                    if (cup == null || pot == null) return;
                    if (!_owner.IsHeldByLocalPlayer(cup.Body) || cup.Coffee!.Value >= .3f || pot.Water!.Value <= .1f
                        || (_owner._coffeeCupTarget!.position - _owner._coffeePour!.transform.position).sqrMagnitude > .0036f)
                    { FsmHook.FireRemoteEntry(cup.Fsm, SyncCatalog.Coffee!["delay"]); return; }
                    if (Time.unscaledTime < _next) return;
                    _next = Time.unscaledTime + .2f;
                    _owner.SendCoffee(cup, CoffeeAction.FillCup);
                }
                catch (Exception e) { _owner.FailCoffee(e); }
            }
        }
        private void SendCoffee(CoffeeBinding b, CoffeeAction action)
        {
            var session = SessionManager.Instance;
            if (_coffeeFailed || session == null) return;
            if (++_coffeeOutSequence == 0) ++_coffeeOutSequence;
            var request = new CoffeeIntent { ItemId = b.Id, PlayerId = session.LocalPlayerId, Sequence = _coffeeOutSequence, Action = action };
            if (action == CoffeeAction.Drink) { _coffeePendingDrink = request.Sequence; _coffeeDrinkDeadline = Time.unscaledTime + 5; }
            if (session.IsHost) AcceptCoffee(request, request.PlayerId);
            else
            {
                session.SendWorldMessage(request, Channel.ReliableOrdered);
                if (action == CoffeeAction.OpenLid || action == CoffeeAction.CloseLid)
                    FsmHook.FireRemoteEntry(_coffeeCap!, b.Cap!.Value ? SyncCatalog.Coffee!["waitOpen"] : SyncCatalog.Coffee!["waitClosed"]);
            }
        }
        internal void ForgetCoffeePlayer(byte actor)
        {
            _coffeeSequences.Remove(actor);
            if (_coffeePourActor == actor) _coffeePourUntil = 0;
        }
        internal void OnCoffeeIntent(CoffeeIntent request, byte actor)
        {
            if (SessionManager.Instance?.IsHost != true || _coffeeFailed || !CoffeePolicy.Valid(request) || request.PlayerId != actor) return;
            try { AcceptCoffee(request, actor); } catch (Exception e) { FailCoffee(e); }
        }
        private bool CoffeeActor(SessionManager session, byte actor, CoffeeBinding b, bool held)
        {
            if (b.Body == null || !b.Body.gameObject.activeInHierarchy || !GamblingSync.TryPlayerPosition(session, actor, out var position)
                || (position - b.Body.position).sqrMagnitude > 9 || !_items.TryGetValue(b.Id, out var item)) return false;
            if (actor == session.LocalPlayerId) return !held || IsHeldByLocalPlayer(b.Body);
            return held ? item.RemoteOwner == actor && !IsHeldByLocalPlayer(b.Body)
                : !IsHeldByLocalPlayer(b.Body) && (item.RemoteOwner == actor || item.RemoteOwner == 255);
        }
        private void AcceptCoffee(CoffeeIntent request, byte actor)
        {
            var session = SessionManager.Instance!;
            if (_coffeePot == null || _coffeeCup == null || !_coffee.TryGetValue(request.ItemId, out var b)) return;
            if (_coffeeSequences.TryGetValue(actor, out uint previous) && !TractorTrailerPolicy.Newer(request.Sequence, previous)) return;
            _coffeeSequences[actor] = request.Sequence;
            bool lid = request.Action == CoffeeAction.OpenLid || request.Action == CoffeeAction.CloseLid;
            if (!CoffeeActor(session, actor, b, !lid) || b != (lid ? _coffeePot : _coffeeCup)) return;
            if (lid)
            {
                SetCoffeeLid(request.Action == CoffeeAction.OpenLid);
                BroadcastCoffee(session, _coffeePot); return;
            }
            if (request.Action == CoffeeAction.FillCup)
            {
                if (_coffeePendingDrink != 0 || (_coffeeCupTarget!.position - _coffeePour!.transform.position).sqrMagnitude > .01f) return;
                _coffeePourActor = actor; _coffeePourUntil = Time.unscaledTime + .8f; _coffeePourTick = Time.unscaledTime;
                return;
            }
            if (request.Action != CoffeeAction.Drink || b.Coffee!.Value <= .01f || Time.unscaledTime < _coffeeHostDrinkUntil) return;
            var result = new CoffeeDrinkResult { ItemId = b.Id, PlayerId = actor, Sequence = request.Sequence,
                Amount = Mathf.Clamp(b.Coffee.Value, 0, .3f), Caffeine = Mathf.Clamp(b.Caffeine!.Value, 0, 1.5f) };
            _coffeeHostDrinkUntil = Time.unscaledTime + 5.52f; _coffeePourUntil = 0;
            b.Coffee.Value = b.Caffeine.Value = 0; BroadcastCoffee(session, b);
            if (actor == session.LocalPlayerId) OnCoffeeDrink(result);
            else session.SendWorldMessage(result, Channel.ReliableOrdered);
            SyncEventLog.Record("coffee-drink", "actor " + actor + " amount " + result.Amount);
        }
        private float _coffeeHostDrinkUntil;
        private void SetCoffeeLid(bool open)
        {
            var c = SyncCatalog.Coffee!; var pot = _coffeePot!;
            pot.Cap!.Value = open;
            pot.Body.transform.Find(c["capMesh"]).gameObject.SetActive(!open);
            pot.Body.transform.Find(c["functions"]).gameObject.SetActive(open);
            if (SessionManager.Instance?.IsHost == true) CoffeeFsm(pot.Body.transform, c["empty"]).enabled = open;
            string wait = open ? c["waitOpen"] : c["waitClosed"];
            if (_coffeeCap != null && _coffeeCap.ActiveStateName != wait) FsmHook.FireRemoteEntry(_coffeeCap, wait);
        }
        private void TickCoffeePour(SessionManager session)
        {
            float now = Time.unscaledTime;
            if (now > _coffeePourUntil || _coffeeCup == null || _coffeePot == null || !CoffeeActor(session, _coffeePourActor, _coffeeCup, true)
                || (_coffeeCupTarget!.position - _coffeePour!.transform.position).sqrMagnitude > .01f) { _coffeePourUntil = 0; return; }
            float amount = CoffeePolicy.Transfer(_coffeePot.Water!.Value, _coffeeCup.Coffee!.Value, Mathf.Min(.25f, now - _coffeePourTick));
            _coffeePourTick = now;
            if (amount <= 0) return;
            if (_coffeeCup.Coffee.Value <= .0001f) _coffeeCup.Caffeine!.Value = Mathf.Clamp(_coffeePot.Caffeine!.Value, 0, 1.5f);
            _coffeePot.Water.Value -= amount; _coffeeCup.Coffee.Value += amount;
        }
        internal void OnCoffeeDrink(CoffeeDrinkResult result)
        {
            var session = SessionManager.Instance;
            if (_coffeeFailed || session == null || result.PlayerId != session.LocalPlayerId || result.Sequence != _coffeePendingDrink
                || _coffeeCup == null || result.ItemId != _coffeeCup.Id || !CoffeePolicy.Valid(result)) return;
            _coffeePendingDrink = 0;
            try
            {
                if (DeathSyncManager.Instance?.IsLocalDead == true)
                { FsmHook.FireRemoteEntry(_coffeeCup.Fsm, SyncCatalog.Coffee!["delay"]); return; }
                var c = SyncCatalog.Coffee!; var cup = _coffeeCup;
                cup.Coffee!.Value = result.Amount; cup.Caffeine!.Value = result.Caffeine;
                cup.Fsm.FsmVariables.FindFsmFloat("Pos").Value = result.Amount / 3.75f;
                FsmHook.FireRemoteEntry(cup.Fsm, c["drink"]);
            }
            catch (Exception e) { FailCoffee(e); }
        }
    }
}
