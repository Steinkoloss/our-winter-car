using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FirewoodBuyerBinding
    {
        internal static FirewoodBuyerBinding? TryCreate(uint id, PlayMakerFSM payment, FirewoodPaymentGuard guard, FirewoodBuyerData config, bool guest)
        {
            var root = payment.transform.root;
            var job = Find(root, config.JobPath, "Logic");
            if (job == null) return null;
            if (!job.Fsm.Initialized) job.Fsm.Init(job);
            var buyer = On(ObjectVariable(job, "Buyer"), "Animations");
            var lod = Find(root, config.LodPath, "LOD");
            var buyerLod = On(ObjectVariable(job, "ThisMan"), "LOD");
            if (buyer == null || job == null || lod == null || buyerLod == null) return null;
            // Deserialize exact catalogued dormant graphs without activating or starting them.
            foreach (var fsm in new[] { payment, buyer, job, lod, buyerLod })
                if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
            foreach (var fsm in new[] { payment, buyer, job, lod, buyerLod })
                foreach (var state in fsm.Fsm.States) if (!state.IsInitialized) return null;
            if (!ScenePath.Of(buyer.transform).StartsWith(config.LodPath + "/LOD/", StringComparison.Ordinal)
                || ScenePath.FindRelative(buyer.transform, config.PaymentPath.Substring(config.BuyerPath.Length + 1)) != payment.transform)
                throw new InvalidOperationException("Wrong firewood payment reference.");
            return new FirewoodBuyerBinding(id, payment, guard, buyer, job, lod, buyerLod, guest);
        }

        internal static FirewoodBuyerData? ConfigFor(PlayMakerFSM payment)
        {
            if (SyncCatalog.FirewoodBuyers == null) return null;
            foreach (var config in SyncCatalog.FirewoodBuyers)
                if (PaymentAt(payment.transform.root, config) == payment) return config;
            return null;
        }

        internal static PlayMakerFSM? PaymentAt(Transform root, FirewoodBuyerData config)
        {
            var job = Find(root, config.JobPath, "Logic");
            if (job == null) return null;
            if (!job.Fsm.Initialized) job.Fsm.Init(job);
            var buyer = On(ObjectVariable(job, "Buyer"), "Animations");
            if (buyer == null) return null;
            if (!buyer.Fsm.Initialized) buyer.Fsm.Init(buyer);
            return On(ObjectVariable(buyer, "PayMoney"), "Use");
        }

        private static PlayMakerFSM? On(GameObject owner, string name)
        {
            PlayMakerFSM? result = null;
            foreach (var fsm in owner.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == name)
                {
                    if (result != null) throw new InvalidOperationException("Ambiguous firewood FSM.");
                    result = fsm;
                }
            return result;
        }

        internal static PlayMakerFSM? Find(Transform root, string path, string name)
        {
            if (!path.StartsWith(root.name + "/", StringComparison.Ordinal)) return null;
            var node = ScenePath.FindRelative(root, path.Substring(root.name.Length + 1));
            if (node == null) return null;
            PlayMakerFSM? result = null;
            foreach (var fsm in node.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == name)
                {
                    if (result != null) throw new InvalidOperationException("Ambiguous firewood FSM.");
                    result = fsm;
                }
            return result;
        }

        private static T Required<T>(T? value) where T : class => value ?? throw new InvalidOperationException("Missing firewood binding.");
        private static T Field<T>(FsmStateAction action, string name) => (T)action.GetType().GetField(name).GetValue(action);
        private static GameObject ObjectVariable(PlayMakerFSM fsm, string name) => Required(Required(fsm.FsmVariables.FindFsmGameObject(name)).Value);
        private static FsmStateAction Action(PlayMakerFSM fsm, string state, int index, string type)
        {
            var action = Required(FsmHook.NativeAction(Required(FsmHook.FindState(fsm, state)), index));
            if (action.GetType().Name != type) throw new InvalidOperationException("Changed firewood action " + state + "/" + index);
            return action;
        }
        private string Clip(PlayMakerFSM fsm, string state, int index, Animation? arm = null)
        {
            var action = Action(fsm, state, index, "PlayAnimation");
            var target = Field<FsmOwnerDefault>(action, "gameObject");
            if ((int)target.OwnerOption != 1 || target.GameObject.Value != (arm ?? _arm).gameObject)
                throw new InvalidOperationException("Changed firewood arm binding.");
            return Field<FsmString>(action, "animName").Value;
        }

        private void Validate()
        {
            if (ObjectVariable(Buyer, "PayMoney") != Payment.gameObject || ObjectVariable(Buyer, "JobData") != Job.gameObject
                || ObjectVariable(Job, "Buyer") != Buyer.gameObject || ObjectVariable(Job, "ThisMan") != BuyerLod.gameObject
                || ObjectVariable(BuyerLod, "Char") != Buyer.gameObject || !BuyerLod.transform.IsChildOf(ObjectVariable(Lod, "Object").transform)
                || !_bill.transform.IsChildOf(Payment.transform)) throw new InvalidOperationException("Changed firewood object references.");
            var amount = Action(Buyer, "State 2", 3, "SetFsmFloat");
            if (Field<FsmOwnerDefault>(amount, "gameObject").GameObject.Value != Payment.gameObject
                || Field<FsmString>(amount, "fsmName").Value != "Use" || Field<FsmString>(amount, "variableName").Value != "Money"
                || !ReferenceEquals(Field<FsmFloat>(amount, "setValue"), Buyer.FsmVariables.FindFsmFloat("Money")))
                throw new InvalidOperationException("Changed firewood offer amount binding.");
            Action(Payment, "Wait player", 2, "MousePickEvent");
            Action(Payment, "Wait button", 3, "GetMouseButtonDown");
            foreach (string state in new[] { "Wait player", "Wait button", "State 1", "State 2", "State 3", "State 4", "State 5" })
                Required(FsmHook.FindState(Payment, state));
            // Resolve every candidate before changing the first state, so graph
            // drift cannot leave half of the host's visibility checks patched.
            DistanceBindings();
        }

        private List<KeyValuePair<FsmState, FsmStateAction>> DistanceBindings()
        {
            var result = new List<KeyValuePair<FsmState, FsmStateAction>>();
            foreach (var fsm in new[] { Lod, BuyerLod, Job })
                foreach (var state in fsm.Fsm.States)
                {
                    if (fsm == Job && state.Name != "Player left?") continue;
                    foreach (var action in state.Actions)
                    {
                        if (action.GetType().Name != "GetDistance") continue;
                        var owner = Field<FsmOwnerDefault>(action, "gameObject");
                        var target = Field<FsmGameObject>(action, "target");
                        if ((int)owner.OwnerOption != 0 || !ReferenceEquals(Field<FsmFloat>(action, "storeResult"), fsm.FsmVariables.FindFsmFloat("Distance"))
                            || (target.Name != "SavePlayerCam" && (target.Value == null || target.Value.name != "PLAYER")))
                            throw new InvalidOperationException("Changed firewood player-distance binding.");
                        result.Add(new KeyValuePair<FsmState, FsmStateAction>(state, action));
                    }
                }
            if (result.Count != 6) throw new InvalidOperationException("Changed firewood visibility graph.");
            return result;
        }

        private void InstallDistances()
        {
            foreach (var pair in DistanceBindings())
            {
                var actions = (FsmStateAction[])pair.Key.Actions.Clone();
                int index = Array.IndexOf(actions, pair.Value);
                actions[index] = new PlayerDistance(pair.Key.Fsm.Owner.transform, pair.Value);
                Replace(pair.Key, actions);
            }
        }

        private sealed class PlayerDistance : FsmStateAction
        {
            private readonly Transform _origin;
            private readonly FsmGameObject _target;
            private readonly FsmFloat _result;
            private readonly bool _everyFrame;
            internal PlayerDistance(Transform origin, FsmStateAction native)
            {
                _origin = origin; _target = Field<FsmGameObject>(native, "target");
                _result = Field<FsmFloat>(native, "storeResult"); _everyFrame = Field<bool>(native, "everyFrame"); Enabled = native.Enabled;
            }
            public override void OnEnter() { Read(); if (!_everyFrame) Finish(); }
            public override void OnUpdate() { Read(); }
            private void Read()
            {
                float distance = _target.Value != null ? Vector3.Distance(_origin.position, _target.Value.transform.position) : float.MaxValue;
                var session = SessionManager.Instance;
                if (session != null && session.IsHost)
                    foreach (var player in session.Players)
                        if (player.LastTransformTime > 0)
                            distance = FirewoodBuyerPolicy.IncludeGuestDistance(distance, Vector3.Distance(_origin.position, player.Position),
                                player.IsDead, Time.unscaledTime - player.LastTransformTime);
                _result.Value = distance;
            }
        }
    }
}
