using UnityEngine;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Procedural posture on a frozen NPC mesh. Facing comes from the avatar root
    /// rotation — not from PlayMaker FSMs.
    /// </summary>
    internal sealed class RemoteCharacterAnimator
    {
        private const float CrouchHeightFactor = 2f / 3f;
        private const float SeatedHeightFactor = 0.55f;
        private const float SeatedForwardPitch = 12f;
        private const float WalkBobAmplitude = 0.04f;
        private const float WalkSwayAmplitude = 0.02f;
        private const float WalkBobHz = 3.5f;
        private const float RunBobHz = 5.5f;

        private readonly Transform _rigRoot;
        private readonly Transform _bodyPivot;
        private readonly float _modelHeight;
        private readonly Renderer[] _renderers;
        private readonly Vector3 _bodyBaseLocalPos;

        private byte _lastMoveState;
        private bool _lastSeated;
        private bool _lastWalking;
        private float _bodyHeightScale = 1f;
        private float _bodyPitch;
        private float _bobPhase;

        public RemoteCharacterAnimator(NpcCharacterFactory.CharacterRig rig)
        {
            _rigRoot = rig.Root.transform;
            _bodyPivot = rig.BodyPivot;
            _modelHeight = rig.ModelHeight;
            _renderers = rig.Root.GetComponentsInChildren<Renderer>(true);
            _bodyBaseLocalPos = _bodyPivot.localPosition;
            ApplyBodyLayout();
        }

        public void Apply(byte moveState, bool vehicleAnchored)
        {
            bool seated = vehicleAnchored
                || PlayerMoveState.Has(moveState, PlayerMoveState.Driving)
                || PlayerMoveState.Has(moveState, PlayerMoveState.Passenger);

            if (moveState == _lastMoveState && seated == _lastSeated)
                return;

            _lastMoveState = moveState;
            _lastSeated = seated;

            bool swimming = PlayerMoveState.Has(moveState, PlayerMoveState.Swimming);
            SetVisible(!swimming);

            if (swimming)
            {
                _lastWalking = false;
                ResetBob();
                return;
            }

            if (seated)
            {
                _bodyHeightScale = SeatedHeightFactor;
                _bodyPitch = SeatedForwardPitch;
                _lastWalking = false;
                ResetBob();
            }
            else if (PlayerMoveState.Has(moveState, PlayerMoveState.Crouch))
            {
                _bodyHeightScale = CrouchHeightFactor;
                _bodyPitch = 0f;
            }
            else
            {
                _bodyHeightScale = 1f;
                _bodyPitch = 0f;
            }

            ApplyBodyLayout();
            UpdateWalking();
        }

        public void Tick()
        {
            if (_lastSeated || PlayerMoveState.Has(_lastMoveState, PlayerMoveState.Swimming))
            {
                ResetBob();
                return;
            }

            if (!_lastWalking)
            {
                ResetBob();
                return;
            }

            bool running = PlayerMoveState.Has(_lastMoveState, PlayerMoveState.Running);
            float hz = running ? RunBobHz : WalkBobHz;
            _bobPhase += Time.deltaTime * hz * Mathf.PI * 2f;

            float bob = Mathf.Sin(_bobPhase) * WalkBobAmplitude;
            float sway = Mathf.Sin(_bobPhase * 0.5f) * WalkSwayAmplitude;
            _bodyPivot.localPosition = _bodyBaseLocalPos + new Vector3(sway, bob, 0f);
        }

        private void UpdateWalking()
        {
            _lastWalking = !_lastSeated
                && (PlayerMoveState.Has(_lastMoveState, PlayerMoveState.Walking)
                    || PlayerMoveState.Has(_lastMoveState, PlayerMoveState.Running));
        }

        private void ResetBob()
        {
            _bobPhase = 0f;
            _bodyPivot.localPosition = _bodyBaseLocalPos;
        }

        private void ApplyBodyLayout()
        {
            _bodyPivot.localScale = new Vector3(1f, _bodyHeightScale, 1f);
            _bodyPivot.localRotation = Quaternion.Euler(_bodyPitch, 0f, 0f);
        }

        public float NameTagHeight
        {
            get
            {
                float y = _rigRoot.localPosition.y + _modelHeight * _bodyHeightScale;
                return Mathf.Max(1.2f, y) + 0.2f;
            }
        }

        private void SetVisible(bool visible)
        {
            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                    _renderers[i].enabled = visible;
            }
        }
    }
}
