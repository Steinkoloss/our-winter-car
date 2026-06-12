using UnityEngine;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Drives a hidden Move FSM for leg cycles and copies its skeleton onto the
    /// visible mesh each frame. Facing stays on the avatar root rotation.
    /// </summary>
    internal sealed class RemoteCharacterAnimator
    {
        private const float CrouchHeightFactor = 2f / 3f;
        private const float SeatedHeightFactor = 0.55f;
        private const float SeatedForwardPitch = 12f;
        private const float WalkDistanceValue = 1.2f;
        private const float RunDistanceValue = 2.8f;

        private readonly Transform _rigRoot;
        private readonly Transform _bodyPivot;
        private readonly Transform? _driverSkeleton;
        private readonly Transform? _visibleSkeleton;
        private readonly PlayMakerFSM? _moveFsm;
        private readonly HutongGames.PlayMaker.FsmFloat? _moveDistance;
        private readonly float _modelHeight;
        private readonly Renderer[] _renderers;

        private byte _lastMoveState;
        private bool _lastSeated;
        private bool _lastWalking;
        private float _bodyHeightScale = 1f;
        private float _bodyPitch;

        public RemoteCharacterAnimator(NpcCharacterFactory.CharacterRig rig)
        {
            _rigRoot = rig.Root.transform;
            _bodyPivot = rig.BodyPivot;
            _driverSkeleton = rig.DriverSkeleton;
            _visibleSkeleton = rig.Skeleton;
            _moveFsm = rig.MoveFsm;
            _modelHeight = rig.ModelHeight;
            _renderers = rig.Root.GetComponentsInChildren<Renderer>(true);

            if (_moveFsm != null)
            {
                try { _moveDistance = _moveFsm.FsmVariables.FindFsmFloat("Distance"); }
                catch { /* optional */ }
            }

            ApplyBodyLayout();
            SendLocomotion(false);
            CopySkeletonPose();
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
                SendLocomotion(false);
                return;
            }

            if (seated)
            {
                _bodyHeightScale = SeatedHeightFactor;
                _bodyPitch = SeatedForwardPitch;
                _lastWalking = false;
                SendLocomotion(false);
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
            UpdateLocomotion();
        }

        public void Tick()
        {
            if (_lastSeated || PlayerMoveState.Has(_lastMoveState, PlayerMoveState.Swimming))
            {
                CopySkeletonPose();
                return;
            }

            if (_lastWalking)
                UpdateStrideSpeed();

            CopySkeletonPose();
        }

        private void UpdateLocomotion()
        {
            bool walking = !_lastSeated
                && (PlayerMoveState.Has(_lastMoveState, PlayerMoveState.Walking)
                    || PlayerMoveState.Has(_lastMoveState, PlayerMoveState.Running));

            if (walking == _lastWalking)
            {
                UpdateStrideSpeed();
                return;
            }

            _lastWalking = walking;
            SendLocomotion(walking);
        }

        private void SendLocomotion(bool walking)
        {
            if (_moveFsm == null) return;

            try
            {
                _moveFsm.SendEvent(walking ? "WALK" : "STAND");
                UpdateStrideSpeed();
            }
            catch
            {
                // Clone FSM not ready yet.
            }
        }

        private void UpdateStrideSpeed()
        {
            if (_moveDistance == null) return;

            float distance = 0f;
            if (_lastWalking)
            {
                distance = PlayerMoveState.Has(_lastMoveState, PlayerMoveState.Running)
                    ? RunDistanceValue
                    : WalkDistanceValue;
            }

            try { _moveDistance.Value = distance; }
            catch { /* FSM variable unavailable */ }
        }

        private void CopySkeletonPose()
        {
            if (_driverSkeleton == null || _visibleSkeleton == null) return;
            NpcCharacterFactory.CopySkeletonPose(_driverSkeleton, _visibleSkeleton);
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
