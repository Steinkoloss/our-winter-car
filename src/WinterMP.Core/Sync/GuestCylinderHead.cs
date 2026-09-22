using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>Reversible pose of the scene head; native assembly and tuning data remain local.</summary>
    internal sealed class GuestCylinderHead
    {
        internal readonly PlayMakerFSM Data;
        private readonly Transform? _parent;
        private readonly bool _hadParent;
        private readonly Vector3 _position, _scale;
        private readonly Quaternion _rotation;
        private readonly Rigidbody? _body;
        private readonly bool _kinematic, _collisions;
        private readonly Vector3 _velocity, _angularVelocity;
        private readonly float _mass;
        private readonly Collider[] _colliders;
        private readonly bool[] _enabled, _triggers;
        private readonly string _tag;
        private readonly int _layer;
        private readonly FsmSuppressor _data = new FsmSuppressor();
        private readonly List<FsmSuppressor> _boltFsms = new List<FsmSuppressor>();
        private readonly List<KeyValuePair<Collider, bool>> _boltPicks = new List<KeyValuePair<Collider, bool>>();
        private Rigidbody? _addedBody;
        private bool _restored;
        internal bool Ready, Failed;
        internal uint AppliedRevision;
        internal bool Paused => _data.Active;
        private CylinderHeadFasteners? _fasteners;
        private uint _presentedParent;
        private float _presentedMass;
        private Func<byte, WinterMP.Net.Messages.PartFitOperation, bool>? _turn;

        internal GuestCylinderHead(PlayMakerFSM data)
        {
            Data = data; var t = data.transform; _parent = t.parent; _hadParent = _parent != null;
            _position = t.localPosition; _rotation = t.localRotation; _scale = t.localScale;
            _tag = data.gameObject.tag; _layer = data.gameObject.layer;
            _body = data.GetComponent<Rigidbody>();
            if (_body != null)
            {
                _kinematic = _body.isKinematic; _collisions = _body.detectCollisions; _mass = _body.mass;
                _velocity = _body.velocity; _angularVelocity = _body.angularVelocity;
            }
            _colliders = data.GetComponents<Collider>();
            _enabled = new bool[_colliders.Length]; _triggers = new bool[_colliders.Length];
            for (int i = 0; i < _colliders.Length; i++)
            { _enabled[i] = _colliders[i].enabled; _triggers[i] = _colliders[i].isTrigger; }
        }

        internal void Pause()
        {
            if (_colliders.Length != 2 || !(_colliders[0] is BoxCollider) || !(_colliders[1] is BoxCollider)
                || Data.FsmVariables.FindFsmGameObject("Owner")?.Value != Data.gameObject)
                throw new InvalidOperationException("Native head geometry or owner changed.");
            if (!_data.Suppress(Data)) throw new InvalidOperationException("Cannot pause native cylinder head.");
            var bolts = Data.FsmVariables.FindFsmGameObject("Bolts")?.Value;
            if (bolts == null || ScenePath.RelativeTo(bolts.transform, Data.transform) != "Bolts")
                throw new InvalidOperationException("Native head bolt group changed.");
            try { _fasteners = new CylinderHeadFasteners(Data); }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("WorldSync: guest head fasteners unavailable: " + e.Message); }
            foreach (var fsm in bolts.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                var saved = new FsmSuppressor();
                if (!saved.Suppress(fsm)) throw new InvalidOperationException("Cannot pause saved head fasteners.");
                _boltFsms.Add(saved);
            }
            foreach (var pick in bolts.GetComponentsInChildren<Collider>(true))
            { _boltPicks.Add(new KeyValuePair<Collider, bool>(pick, pick.enabled)); pick.enabled = false; }
            if (_fasteners != null)
                _fasteners.PrepareGuest((slot, operation) => _turn != null && _turn(slot, operation));
        }

        internal void BindTurns(Func<byte, PartFitOperation, bool> turn) => _turn = turn;
        internal void RefreshFasteners(CylinderHeadState state)
        { _fasteners?.Apply(state); AppliedRevision = state.Revision; }
        internal void TickFasteners() => _fasteners?.Tick();
        internal bool SameAttachment(CylinderHeadState state) => _presentedParent == state.ParentId && _presentedMass == state.Mass;

        internal void Hold()
        {
            Ready = false;
            _fasteners?.Hold();
            var body = Data == null ? null : Data.GetComponent<Rigidbody>();
            if (body != null) { body.isKinematic = true; body.detectCollisions = false; }
        }

        internal void Apply(CylinderHeadState state, Transform? parent)
        {
            if (!Paused || Data == null || (parent != null) != (state.ParentId != 0))
                throw new InvalidOperationException("Head attachment is not prepared.");
            var obj = Data.gameObject;
            var body = obj.GetComponent<Rigidbody>();
            if (body == null) { _addedBody = obj.AddComponent<Rigidbody>(); body = _addedBody; }
            body.isKinematic = true;
            body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero;
            obj.transform.SetParent(parent, false); obj.transform.localScale = Vector3.one;
            obj.tag = parent == null ? "PART" : "Untagged"; obj.layer = 19;
            // Keep trigger picking for the valve controls on the retained body.
            // Only the head's own collision boxes change; child mounts keep their identities.
            body.detectCollisions = true;
            for (int i = 0; i < _colliders.Length; i++)
            { _colliders[i].enabled = parent == null; _colliders[i].isTrigger = false; }
            if (parent != null)
            { obj.transform.localPosition = Vector3.zero; obj.transform.localRotation = Quaternion.identity; }
            else
            {
                obj.transform.position = state.Position.ToUnity(); obj.transform.rotation = state.Rotation.ToUnity();
                body.mass = state.Mass; body.isKinematic = false;
            }
            AppliedRevision = state.Revision; Ready = true;
            _presentedParent = state.ParentId; _presentedMass = state.Mass;
            RefreshFasteners(state);
        }

        internal void Restore()
        {
            if (_restored || Data == null) return;
            _restored = true;
            Hold();
            _turn = null; _fasteners?.Restore();
            if (_addedBody != null) UnityEngine.Object.Destroy(_addedBody);
            if (_hadParent && _parent == null) return;
            var t = Data.transform; t.SetParent(_parent, false); t.localPosition = _position;
            t.localRotation = _rotation; t.localScale = _scale;
            Data.gameObject.tag = _tag; Data.gameObject.layer = _layer;
            for (int i = 0; i < _colliders.Length; i++)
                if (_colliders[i] != null) { _colliders[i].enabled = _enabled[i]; _colliders[i].isTrigger = _triggers[i]; }
            if (_body != null)
            {
                _body.mass = _mass; _body.isKinematic = _kinematic; _body.detectCollisions = _collisions;
                if (!_kinematic) { _body.velocity = _velocity; _body.angularVelocity = _angularVelocity; }
            }
            _data.Restore();
            foreach (var saved in _boltPicks) if (saved.Key != null) saved.Key.enabled = saved.Value;
            foreach (var saved in _boltFsms) saved.Restore();
        }
    }
}
