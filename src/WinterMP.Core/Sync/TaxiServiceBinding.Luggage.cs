using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class TaxiServiceBinding
    {
        private readonly Rigidbody[] _luggageBodies = new Rigidbody[5];
        private readonly Transform[] _luggagePivots = new Transform[5];
        private ItemWorldSync _luggageItems = null!;
        private uint _luggageEpoch = 1, _shownLuggageEpoch;
        private byte _shownLuggageMask;

        private void InitializeLuggage()
        {
            _luggageItems = Required(WorldSyncManager.Instance).ItemSync;
            var fsm = Fsm(_walker.gameObject, _c["luggageFsm"]);
            for (int i = 0; i < 5; i++)
            {
                var reset = ActionAt(fsm, _c["luggageResetState"], i, "SetParent");
                var body = Required(ObjectVariable(fsm, _c["luggage" + i]).GetComponent<Rigidbody>());
                var pivot = Required(Field<FsmGameObject>(reset, "parent").Value).transform;
                if (Field<FsmOwnerDefault>(reset, "gameObject").GameObject.Value != body.gameObject
                    || !pivot.IsChildOf(_customer.transform) || !Field<FsmBool>(reset, "resetLocalPosition").Value
                    || !Field<FsmBool>(reset, "resetLocalRotation").Value)
                    throw new InvalidOperationException("Changed native taxi luggage reset.");
                for (int j = 0; j < i; j++) if (_luggageBodies[j] == body) throw new InvalidOperationException("Duplicate native taxi luggage.");
                _luggageBodies[i] = body; _luggagePivots[i] = pivot;
                if (_guest) RememberPose(body.transform);
            }
            var release = ActionAt(fsm, _c["luggageReleaseState"], 2, "SetParent");
            var parent = Field<FsmGameObject>(release, "parent");
            if (Field<FsmOwnerDefault>(release, "gameObject").GameObject.Name != "Luggage" || parent.UseVariable || parent.Value != null)
                throw new InvalidOperationException("Changed native taxi luggage release.");
            _restore.Add(_luggageItems.UnregisterTaxiLuggage);
            _luggageItems.RegisterTaxiLuggage(_luggageBodies, _luggageEpoch);
            if (!_guest)
            {
                BoundNativeLuggageCount(fsm);
                Observe(fsm, _c["luggageResetState"], () => {
                    // Native bodies are reused. New identities prevent delayed packets
                    // from a previous customer reclaiming newly randomized luggage.
                    _luggageEpoch = unchecked(_luggageEpoch + 1); if (_luggageEpoch == 0) _luggageEpoch = 1;
                    _luggageItems.RegisterTaxiLuggage(_luggageBodies, _luggageEpoch);
                    _luggageItems.SetTaxiLuggageMask(0);
                });
            }
        }
        private void BoundNativeLuggageCount(PlayMakerFSM fsm)
        {
            IList? pool = null;
            foreach (var component in fsm.GetComponents<MonoBehaviour>())
                if (component != null && component.GetType().Name == "PlayMakerArrayListProxy"
                    && Field<string>(component, "referenceName") == _c["luggagePool"])
                {
                    if (pool != null) throw new InvalidOperationException("Ambiguous native luggage pool.");
                    pool = component.GetType().GetProperty("arrayList")?.GetValue(component, null) as IList;
                }
            var choices = new HashSet<GameObject>();
            foreach (var value in Required(pool))
            {
                var go = value as GameObject;
                if (go == null || Array.FindIndex(_luggageBodies, body => body.gameObject == go) < 0 || !choices.Add(go))
                    throw new InvalidOperationException("Changed native luggage choices.");
            }
            var state = State(fsm, _c["luggageResetState"]);
            var count = Required(fsm.FsmVariables.FindFsmInt("Luggages"));
            var compare = ActionAt(fsm, state.Name, 7, "IntCompare");
            if (!ReferenceEquals(Field<FsmInt>(compare, "integer1"), count)) throw new InvalidOperationException("Changed native luggage count check.");
            // The native pool has five objects but Amounts includes six. Clamp the
            // draw before its zero check so the random-selection loop can terminate.
            var old = state.Actions; var actions = new List<FsmStateAction>(old);
            var clamp = new FsmHookAction(() => count.Value = TaxiServicePolicy.BoundLuggageCount(count.Value, choices.Count)); clamp.Init(state);
            actions.Insert(actions.IndexOf(compare), clamp); var installed = actions.ToArray(); state.Actions = installed;
            _restore.Add(() => { if (ReferenceEquals(state.Actions, installed)) state.Actions = old; });
        }
        private void CaptureLuggage(TaxiServiceState state)
        {
            state.LuggageEpoch = _luggageEpoch;
            for (int i = 0; i < 5; i++)
            {
                var body = _luggageBodies[i];
                if (body.gameObject.activeInHierarchy && body.transform.parent != _luggagePivots[i]) state.LuggageMask |= (byte)(1 << i);
                state.LuggagePositions[i] = Net(body.transform.position); state.LuggageRotations[i] = Net(body.transform.rotation);
            }
            _luggageItems.SetTaxiLuggageMask(state.LuggageMask);
        }
        private void PresentLuggage(TaxiServiceState state)
        {
            bool reset = state.LuggageEpoch != _shownLuggageEpoch;
            if (reset) _luggageItems.RegisterTaxiLuggage(_luggageBodies, state.LuggageEpoch);
            _luggageItems.SetTaxiLuggageMask(state.LuggageMask);
            for (int i = 0; i < 5; i++)
            {
                var body = _luggageBodies[i]; bool active = (state.LuggageMask & (1 << i)) != 0;
                if (reset || ((state.LuggageMask ^ _shownLuggageMask) & (1 << i)) != 0)
                {
                    body.transform.SetParent(active ? null : _luggagePivots[i], false);
                    if (active)
                    {
                        // Inactive rigidbodies ignore pose writes on this Unity
                        // build. Set the Transform before enabling its physics.
                        body.transform.position = Unity(state.LuggagePositions[i]);
                        body.transform.rotation = Unity(state.LuggageRotations[i]);
                        body.position = body.transform.position; body.rotation = body.transform.rotation;
                    }
                    else { body.transform.localPosition = Vector3.zero; body.transform.localRotation = Quaternion.identity; }
                }
                body.gameObject.SetActive(active);
            }
            _shownLuggageEpoch = state.LuggageEpoch; _shownLuggageMask = state.LuggageMask;
        }
    }
}
