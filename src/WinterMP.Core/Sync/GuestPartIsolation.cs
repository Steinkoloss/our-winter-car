using System;
using System.Collections.Generic;
using UnityEngine;

namespace WinterMP.Core.Sync
{
    /// <summary>Retains a native leaf part without allowing its graph or physics to run.</summary>
    internal sealed class GuestPartIsolation
    {
        private readonly GameObject _part;
        private readonly Transform? _parent;
        private readonly bool _hadParent, _active;
        private readonly Vector3 _position, _scale;
        private readonly Quaternion _rotation;
        private readonly List<FsmSuppressor> _fsms = new List<FsmSuppressor>();
        private readonly List<BodyState> _bodies = new List<BodyState>();
        private bool _restored;
        private sealed class BodyState
        {
            internal Rigidbody Body = null!;
            internal bool Kinematic, Collisions;
            internal Vector3 Velocity, AngularVelocity;
        }

        internal GuestPartIsolation(GameObject part, Transform storage)
        {
            if (storage.gameObject.activeInHierarchy || ScenePath.RelativeTo(storage, part.transform) != null)
                throw new InvalidOperationException("Part storage must be inactive and outside the part.");
            _part = part; _parent = part.transform.parent; _hadParent = _parent != null;
            _position = part.transform.localPosition; _rotation = part.transform.localRotation;
            _scale = part.transform.localScale; _active = part.activeSelf;
            try
            {
                foreach (var fsm in part.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    var suppressor = new FsmSuppressor();
                    if (!suppressor.Suppress(fsm)) throw new InvalidOperationException("Cannot pause native part FSM.");
                    _fsms.Add(suppressor);
                }
                foreach (var body in part.GetComponentsInChildren<Rigidbody>(true))
                {
                    _bodies.Add(new BodyState { Body = body, Kinematic = body.isKinematic,
                        Collisions = body.detectCollisions, Velocity = body.velocity, AngularVelocity = body.angularVelocity });
                    body.isKinematic = true; body.detectCollisions = false;
                }
                part.SetActive(false);
                part.transform.SetParent(storage, true);
            }
            catch { Restore(); throw; }
        }

        internal void Restore()
        {
            // Scene teardown owns objects whose original parent has disappeared.
            if (_restored || _part == null || (_hadParent && _parent == null)) return;
            _restored = true;
            _part.SetActive(false);
            _part.transform.SetParent(_parent, false);
            _part.transform.localPosition = _position; _part.transform.localRotation = _rotation;
            _part.transform.localScale = _scale;
            foreach (var saved in _bodies)
                if (saved.Body != null)
                {
                    saved.Body.isKinematic = saved.Kinematic; saved.Body.detectCollisions = saved.Collisions;
                    if (!saved.Kinematic) { saved.Body.velocity = saved.Velocity; saved.Body.angularVelocity = saved.AngularVelocity; }
                }
            // Activate while FSMs are still disabled, then enable them with
            // RestartOnEnable=false. Reversing this order reruns native load/init.
            _part.SetActive(_active);
            foreach (var fsm in _fsms) fsm.Restore();
            _fsms.Clear();
        }
    }
}
