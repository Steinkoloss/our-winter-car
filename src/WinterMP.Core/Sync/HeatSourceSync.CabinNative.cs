using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class HeatSourceSync
    {
        private CabinWoodContact? _woodContact;
        private static T CabinField<T>(FsmStateAction action, string field) where T : class
            => action.GetType().GetField(field)?.GetValue(action) as T
                ?? throw new InvalidOperationException("Cabin action field changed: " + field);
        private static FsmState RequireCabinState(PlayMakerFSM fsm, string name, params string[] types)
        {
            var state = FsmHook.FindState(fsm, name);
            if (state == null || !state.IsInitialized || state.Actions.Length != types.Length)
                throw new InvalidOperationException("Cabin native state changed: " + name);
            for (int i = 0; i < types.Length; i++)
                if (state.Actions[i].GetType().Name != types[i] || !state.Actions[i].Enabled)
                    throw new InvalidOperationException("Cabin native action changed: " + name + "/" + i);
            return state;
        }
        private static void ValidateCabinActions(PlayMakerFSM trigger, FsmState entry, FsmState destroy)
        {
            if (CabinField<FsmInt>(entry.Actions[0], "integer1").Name != "Woods"
                || CabinField<FsmInt>(entry.Actions[0], "integer2").Value != 4
                || CabinField<FsmGameObject>(destroy.Actions[3], "gameObject").Name != "Collider"
                || CabinField<FsmFloat>(destroy.Actions[3], "delay").Value != 0
                || CabinField<FsmBool>(destroy.Actions[3], "detachChildren").Value
                || CabinField<FsmInt>(destroy.Actions[4], "intVariable").Name != "Woods"
                || CabinField<FsmInt>(destroy.Actions[4], "add").Value != 1
                || !Equals(destroy.Actions[4].GetType().GetField("everyFrame")?.GetValue(destroy.Actions[4]), false)
                || trigger.FsmVariables.FindFsmGameObject("Collider") == null
                || trigger.FsmVariables.FindFsmGameObject("Parent") == null)
                throw new InvalidOperationException("Cabin native feed signature changed.");
            var collider = trigger.GetComponent<BoxCollider>();
            if (collider == null || !collider.enabled || !collider.isTrigger)
                throw new InvalidOperationException("Cabin native wood trigger missing.");
        }
        private sealed class CabinFuelHost : IWoodstoveFuelHost, IWoodstoveDeferredFuelHost
        {
            private readonly HeatSourceSync _owner;
            private readonly Source _source;
            private uint _pendingId;
            private int _mutatedFrame;
            private float _deadline;
            public CabinFuelHost(HeatSourceSync owner, Source source) { _owner = owner; _source = source; }
            public WoodstoveFuelValues ReadFuel() => new WoodstoveFuelValues(_source.Woods!.Value,
                _source.HeatingEfficiency!.Value, _source.Hiillos!.Value);
            public WoodstoveFeedContext Observe(byte actor, uint resourceId)
            {
                var session = SessionManager.Instance!;
                var context = new WoodstoveFeedContext { ResourceId = resourceId, EquipmentReady = true,
                    PoseAgeSeconds = float.PositiveInfinity, DistanceSquared = float.PositiveInfinity };
                Vector3 feet = Vector3.zero;
                if (actor == session.LocalPlayerId)
                {
                    context.ActorPresent = PlayerSyncManager.Instance != null
                        && PlayerSyncManager.Instance.TryReadLocalPose(out feet, out var rotation);
                    context.ActorAlive = DeathSyncManager.Instance?.IsLocalDead != true;
                    context.PoseAgeSeconds = 0;
                }
                else foreach (var player in session.Players)
                {
                    if (player.PlayerId != actor) continue;
                    context.ActorPresent = player.LastTransformTime > 0;
                    context.ActorAlive = !player.IsDead; context.PoseAgeSeconds = Time.unscaledTime - player.LastTransformTime;
                    feet = player.Position; break;
                }
                var trigger = _source.WoodTrigger!;
                context.DistanceSquared = (feet - trigger.transform.position).sqrMagnitude;
                context.SourceReady = trigger.enabled && trigger.gameObject.activeInHierarchy
                    && _source.Woods!.Value >= 0 && _source.Woods.Value < 4 && !_owner._cabinFailed;
                if (!_owner._cabinWood.TryGetValue(resourceId, out var wood) || wood.Piece == null) return context;
                var piece = wood.Piece; var collider = piece.GetComponent<Collider>();
                context.ResourceAvailable = piece.activeInHierarchy && collider != null && collider.enabled;
                context.ResourceParented = piece.transform.parent != null;
                context.ResourceTagIsPart = piece.tag == "PART";
                context.ResourceIsFirewood = piece.name == "firewood(Clone)" && piece.GetComponent<Rigidbody>() == wood.Item.Body
                    && piece.GetComponent<FixedJoint>() == null;
                context.ResourceAtSource = _owner._woodContact != null && _owner._woodContact.Contains(piece)
                    && collider != null && trigger.GetComponent<BoxCollider>().bounds.Intersects(collider.bounds);
                var item = wood.Item;
                if (actor == session.LocalPlayerId)
                    context.ResourceAvailableToActor = item.RemoteOwner == WorldSyncIds.NoOwner
                        && (item.LastRemoteSequenceOwner == WorldSyncIds.NoOwner || item.LastRemoteSequenceOwner == actor
                            || Time.unscaledTime - item.LastRemoteAt > 2f);
                else context.ResourceAvailableToActor = (item.RemoteOwner == actor || (item.RemoteOwner == WorldSyncIds.NoOwner
                        && item.LastRemoteSequenceOwner == actor && Time.unscaledTime - item.LastRemoteReleaseAt <= 2f));
                return context;
            }
            public void FeedOne(uint resourceId)
            {
                var wood = _owner._cabinWood[resourceId]; var trigger = _source.WoodTrigger!;
                _pendingId = resourceId; _mutatedFrame = Time.frameCount; _deadline = Time.unscaledTime + 2f;
                // Remove the alternate generic motion/despawn route before native mutation.
                WorldSyncManager.Instance!.ItemSync.UnbindCabinWood(resourceId);
                trigger.FsmVariables.FindFsmGameObject("Collider").Value = wood.Piece;
                trigger.FsmVariables.FindFsmGameObject("Parent").Value = null;
                var actions = FsmHook.FindState(trigger, "Destroy firewood")!.Actions;
                try
                {
                    foreach (var action in actions) action.Enabled = _owner._cabinEnabled[action];
                    FsmHook.FireRemoteEntry(trigger, "Destroy firewood");
                }
                finally { foreach (var action in actions) action.Enabled = false; }
            }
            public bool IsConsumed(uint resourceId) => resourceId == _pendingId && Time.frameCount > _mutatedFrame
                && _owner._cabinWood[resourceId].Piece == null;
            public bool CompletionExpired => Time.unscaledTime > _deadline;
        }
    }

    // Real host physics contact on the same Collider object stored by TriggerEvent.
    // Bounds alone are not sufficient evidence of a native OnTriggerStay.
    internal sealed class CabinWoodContact : MonoBehaviour
    {
        private readonly Dictionary<GameObject, float> _contacts = new Dictionary<GameObject, float>();
        private void OnTriggerEnter(Collider other) { _contacts[other.gameObject] = Time.unscaledTime; }
        private void OnTriggerStay(Collider other) { _contacts[other.gameObject] = Time.unscaledTime; }
        private void OnTriggerExit(Collider other) { _contacts.Remove(other.gameObject); }
        public bool Contains(GameObject piece) => piece != null && _contacts.TryGetValue(piece, out float at)
            && Time.unscaledTime - at <= .25f;
    }
}
