using System;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed class AtfBottleBinding
    {
        internal readonly Rigidbody Body;
        internal readonly PlayMakerFSM Use;
        internal readonly string NativeId;
        internal readonly uint ItemId;
        internal readonly bool Replica;
        internal readonly PlayMakerFSM? Data;
        internal readonly CapsuleCollider? PourCollider;
        internal readonly GameObject? Particle;
        internal AtfBottleState? Observed, Received;
        internal uint SentRevision;
        internal float NextSend;
        internal bool Sent;

        private readonly AtfRefillData _catalog;
        private readonly FsmFloat _rootFluid;
        private readonly FsmFloat? _sourceFluid, _mass;
        private readonly FsmBool? _pouring;
        private readonly FsmSuppressor _pause = new FsmSuppressor();
        private bool _empty, _isPouring, _pendingEmpty;
        private readonly bool _originalParticle, _originalPouring;

        internal float Fluid => Data != null && _sourceFluid != null ? _sourceFluid.Value : _rootFluid.Value;
        internal bool Empty => _empty || (Body != null && Body.name == _catalog["emptyName"]);
        internal bool IsPouring => _isPouring;

        internal AtfBottleBinding(Rigidbody body, PlayMakerFSM use, string nativeId, uint itemId, bool replica, AtfRefillData catalog)
        {
            Body = body; Use = use; NativeId = nativeId; ItemId = itemId; Replica = replica; _catalog = catalog;
            if (body == null || use == null || use.gameObject != body.gameObject || use.FsmName != catalog["use"]
                || !use.Fsm.Initialized || (!replica && !use.Fsm.Started))
                throw new InvalidOperationException("ATF bottle is not initialized.");
            _rootFluid = use.FsmVariables.FindFsmFloat(catalog["fluid"]) ?? throw new InvalidOperationException("ATF root fluid missing.");
            var consumed = use.FsmVariables.FindFsmBool(catalog["consumed"]);
            if (consumed == null || consumed.Value) throw new InvalidOperationException("ATF bottle was consumed.");
            var empty = State(use, catalog["empty"], "SetName", "DestroyObject", "DestroyObject");
            if (Field<FsmString>(empty.Actions[0], "name")?.Value != catalog["emptyName"])
                throw new InvalidOperationException("ATF empty presentation changed.");
            Particle = Field<FsmGameObject>(empty.Actions[1], "gameObject")?.Value;
            _originalParticle = Particle != null && Particle.activeSelf;
            var trigger = Field<FsmGameObject>(empty.Actions[2], "gameObject")?.Value;
            _empty = body.name == catalog["emptyName"];
            if (trigger == null)
            {
                if (replica || !_empty || !Finite(_rootFluid.Value) || _rootFluid.Value < 0 || _rootFluid.Value >= .1f)
                    throw new InvalidOperationException("ATF source trigger missing from a nonempty bottle.");
                return;
            }
            if (trigger.name != catalog["trigger"] || trigger.transform.parent != body.transform
                || Particle == null || Particle.name != catalog["particle"] || Particle.transform.parent != body.transform)
                throw new InvalidOperationException("ATF native child references changed.");
            PourCollider = trigger.GetComponent<CapsuleCollider>();
            if (PourCollider == null || !PourCollider.enabled || !PourCollider.isTrigger || PourCollider.direction != 2
                || Mathf.Abs(PourCollider.radius - .03f) > .0001f || Mathf.Abs(PourCollider.height - .3f) > .0001f
                || (PourCollider.center - new Vector3(0, 0, .06f)).sqrMagnitude > .00000001f
                || (trigger.transform.localPosition - new Vector3(0, -.061f, .193f)).sqrMagnitude > .00000001f
                || trigger.transform.localScale != Vector3.one || trigger.transform.localRotation != Quaternion.identity)
                throw new InvalidOperationException("ATF pour collider changed.");
            foreach (var fsm in trigger.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == catalog["data"])
                {
                    if (Data != null) throw new InvalidOperationException("Ambiguous ATF Data FSM.");
                    Data = fsm;
                }
            if (Data == null) throw new InvalidOperationException("ATF source Data missing.");
            if (!Data.Fsm.Initialized)
            {
                if (!replica) throw new InvalidOperationException("ATF source Data not initialized.");
                Data.Fsm.Init(Data);
            }
            if (!replica && !Data.Fsm.Started) throw new InvalidOperationException("ATF source Data not started.");
            _sourceFluid = Data.FsmVariables.FindFsmFloat(catalog["fluid"]) ?? throw new InvalidOperationException("ATF source fluid missing.");
            _mass = Data.FsmVariables.FindFsmFloat(catalog["mass"]) ?? throw new InvalidOperationException("ATF source mass missing.");
            _pouring = Data.FsmVariables.FindFsmBool(catalog["pouring"]) ?? throw new InvalidOperationException("ATF source pouring missing.");
            _originalPouring = _pouring.Value;
            var clamp = State(Data, catalog["copyToRoot"], "FloatClamp", "SetFsmFloat", "SendEvent", "Wait");
            if (Field<FsmFloat>(clamp.Actions[0], "floatVariable")?.Name != catalog["fluid"]
                || Field<FsmFloat>(clamp.Actions[0], "minValue")?.Value != 0
                || Field<FsmFloat>(clamp.Actions[0], "maxValue")?.Value != 1)
                throw new InvalidOperationException("ATF capacity changed.");
            var copy = State(use, catalog["copyToChild"], "SetFsmFloat").Actions[0];
            var target = Field<FsmOwnerDefault>(copy, "gameObject");
            if (target == null || use.Fsm.GetOwnerDefaultTarget(target) != trigger
                || Field<FsmString>(copy, "fsmName")?.Value != catalog["data"]
                || Field<FsmString>(copy, "variableName")?.Value != catalog["fluid"]
                || Field<FsmFloat>(copy, "setValue")?.Name != catalog["fluid"])
                throw new InvalidOperationException("ATF native load source changed.");
            if (!replica && !_pause.Suppress(Data)) throw new InvalidOperationException("Could not pause ATF source simulation.");
        }

        internal void SetHostFluid(float fluid)
        {
            if (Replica || Body == null || !Finite(fluid) || fluid < 0 || fluid > 1 || (Empty && fluid != Fluid))
                throw new InvalidOperationException("Invalid host ATF source update.");
            _rootFluid.Value = fluid;
            if (Data != null && _sourceFluid != null) _sourceFluid.Value = fluid;
            if (_mass != null) _mass.Value = fluid + .5f;
            Body.mass = fluid + .5f;
            _pendingEmpty = !Empty && fluid < .02f;
        }

        internal void Apply(AtfBottleState state)
        {
            if (!Replica) throw new InvalidOperationException("ATF replica state targeted a native save object.");
            _rootFluid.Value = state.Fluid;
            if (_sourceFluid != null) _sourceFluid.Value = state.Fluid;
            _empty = state.Empty;
            Body.name = _empty ? _catalog["emptyName"] : _catalog["itemName"];
            if (PourCollider != null) PourCollider.enabled = !_empty;
            TickPresentation();
        }

        internal void TickPresentation()
        {
            if (Body == null || Use == null) return;
            if (!Replica && _pendingEmpty)
            {
                _pendingEmpty = false;
                // Keep native destruction outside the coordinator's atomic scalar
                // transfer. Use retains the remainder and host save semantics.
                Use.SendEvent(_catalog["emptyEvent"]);
                _empty = Body.name == _catalog["emptyName"];
            }
            float fluid = Fluid;
            if (!Finite(fluid) || fluid < 0 || fluid > 1) throw new InvalidOperationException("ATF source amount out of range.");
            _rootFluid.Value = fluid;
            float x = Body.rotation.eulerAngles.x;
            // Native Pour2 exits above80; its brief >340 entry exits immediately.
            // At exactly80 neither comparison transitions, so retain prior phase.
            _isPouring = !Empty && fluid > 0 && (x < 80f || (x == 80f && _isPouring));
            if (_pouring != null) _pouring.Value = _isPouring;
            if (_mass != null) _mass.Value = fluid + .5f;
            Body.mass = fluid + .5f;
            if (Particle != null && Particle.activeSelf != _isPouring) Particle.SetActive(_isPouring);
        }

        internal void Restore()
        {
            if (!Replica)
            {
                if (Data != null && _pouring != null) _pouring.Value = _originalPouring;
                if (Particle != null) Particle.SetActive(_originalParticle);
            }
            _pause.Restore();
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static T? Field<T>(FsmStateAction action, string field) where T : class =>
            action.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(action) as T;
        private static FsmState State(PlayMakerFSM fsm, string name, params string[] actions)
        {
            var state = FsmHook.FindState(fsm, name) ?? throw new InvalidOperationException("ATF state missing: " + name);
            if (state.Actions.Length != actions.Length) throw new InvalidOperationException("ATF actions changed: " + name);
            for (int i = 0; i < actions.Length; i++)
                if (!state.Actions[i].Enabled || state.Actions[i].GetType().Name != actions[i])
                    throw new InvalidOperationException("ATF action changed: " + name + "[" + i + "]");
            return state;
        }
    }
}
