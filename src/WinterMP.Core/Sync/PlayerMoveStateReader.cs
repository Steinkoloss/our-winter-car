using UnityEngine;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Samples the local PLAYER object into the compact <see cref="PlayerMoveState"/>
    /// bitfield streamed to remote peers.
    /// </summary>
    internal sealed class PlayerMoveStateReader
    {
        private const float WalkSpeedThreshold = 0.28f;
        private const float RunSpeedThreshold = 2.5f;
        private const float StopSpeedThreshold = 0.12f;
        private const float CrouchHeightRatio = 0.85f;

        private static readonly string[] CarryIdleStates =
        {
            "Look for object",
            "Wait",
            "Check if Part",
            "Check if Tool",
            "Check joint",
            "State 1",
            "Get scroll",
        };

        // Only the actual in-water state counts — other Swim FSM states (Randomize,
        // Out water, etc.) are idle/transition states on dry land and must not hide avatars.
        private static readonly string[] SwimActiveStates =
        {
            "In water",
        };

        private static readonly string[] CrouchActiveStates =
        {
            "Crouched 1",
            "Crouched 2",
            "Move down 1",
            "Move down 2",
        };

        private PlayMakerFSM? _runningFsm;
        private PlayMakerFSM? _swimFsm;
        private PlayMakerFSM? _crouchFsm;
        private PlayMakerFSM? _pickupFsm;
        private Vector3 _lastPosition;
        private float _lastSampleTime = -1f;
        private bool _hasLastPosition;
        private bool _wasMoving;

        public void Reset()
        {
            _runningFsm = null;
            _swimFsm = null;
            _crouchFsm = null;
            _pickupFsm = null;
            _hasLastPosition = false;
            _lastSampleTime = -1f;
            _wasMoving = false;
        }

        public byte Read(Transform player, CharacterController? controller, float standingHeight)
        {
            EnsureFsmCache(player);

            byte state = 0;

            var passengers = PassengerController.Instance;
            if (passengers != null && passengers.IsLocalSeated)
                state |= PlayerMoveState.Passenger;
            else if (IsLocalDriving())
                state |= PlayerMoveState.Driving;

            if (IsSwimming())
                state |= PlayerMoveState.Swimming;

            bool immobile = (state & (PlayerMoveState.Driving | PlayerMoveState.Passenger | PlayerMoveState.Swimming)) != 0;

            if (!immobile && IsCrouching(controller, standingHeight))
                state |= PlayerMoveState.Crouch;

            if (!immobile)
            {
                float speed = ReadHorizontalSpeed(player, controller);
                bool running = speed >= RunSpeedThreshold || IsRunningFsmActive();
                bool walking = running
                    || speed >= (_wasMoving ? StopSpeedThreshold : WalkSpeedThreshold);

                if (running)
                    state |= PlayerMoveState.Running;
                else if (walking)
                    state |= PlayerMoveState.Walking;

                _wasMoving = walking || running;
            }

            if (IsCarrying())
                state |= PlayerMoveState.Carry;

            return state;
        }

        private void EnsureFsmCache(Transform player)
        {
            if (_runningFsm == null || _swimFsm == null || _crouchFsm == null)
            {
                foreach (var fsm in player.GetComponents<PlayMakerFSM>())
                {
                    string name = fsm.FsmName;
                    if (name == "Running") _runningFsm = fsm;
                    else if (name == "Swim") _swimFsm = fsm;
                    else if (name == "Crouch") _crouchFsm = fsm;
                }
            }

            if (_pickupFsm == null)
            {
                var hand = player.Find("Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand");
                if (hand != null)
                {
                    foreach (var fsm in hand.GetComponents<PlayMakerFSM>())
                    {
                        if (fsm.FsmName == "PickUp")
                        {
                            _pickupFsm = fsm;
                            break;
                        }
                    }
                }
            }
        }

        private bool IsLocalDriving()
        {
            var world = WorldSyncManager.Instance;
            return world != null && world.IsLocalPlayerDrivingAny();
        }

        private bool IsSwimming()
        {
            if (_swimFsm?.Fsm == null) return false;
            try
            {
                string active = _swimFsm.Fsm.ActiveStateName;
                for (int i = 0; i < SwimActiveStates.Length; i++)
                {
                    if (active == SwimActiveStates[i])
                        return true;
                }
            }
            catch
            {
                // FSM not ready.
            }

            return false;
        }

        private bool IsCrouching(CharacterController? controller, float standingHeight)
        {
            if (IsCrouchFsmActive())
                return true;

            if (controller == null || standingHeight <= 0f) return false;
            return controller.height < standingHeight * CrouchHeightRatio;
        }

        private bool IsCrouchFsmActive()
        {
            if (_crouchFsm?.Fsm == null) return false;
            try
            {
                string active = _crouchFsm.Fsm.ActiveStateName;
                for (int i = 0; i < CrouchActiveStates.Length; i++)
                {
                    if (active == CrouchActiveStates[i])
                        return true;
                }
            }
            catch
            {
                // FSM not ready.
            }

            return false;
        }

        private float ReadHorizontalSpeed(Transform player, CharacterController? controller)
        {
            if (controller != null)
            {
                try
                {
                    Vector3 velocity = controller.velocity;
                    velocity.y = 0f;
                    if (velocity.sqrMagnitude > 0.0001f)
                        return velocity.magnitude;
                }
                catch
                {
                    // Fall back to position delta.
                }
            }

            float now = Time.unscaledTime;
            Vector3 position = player.position;
            if (!_hasLastPosition || _lastSampleTime <= 0f)
            {
                _hasLastPosition = true;
                _lastPosition = position;
                _lastSampleTime = now;
                return 0f;
            }

            float dt = now - _lastSampleTime;
            Vector3 delta = position - _lastPosition;
            _lastPosition = position;
            _lastSampleTime = now;
            if (dt <= 0.0001f) return 0f;

            delta.y = 0f;
            return delta.magnitude / dt;
        }

        private bool IsCarrying()
        {
            if (_pickupFsm?.Fsm == null) return false;
            try
            {
                string active = _pickupFsm.Fsm.ActiveStateName;
                if (string.IsNullOrEmpty(active)) return false;
                for (int i = 0; i < CarryIdleStates.Length; i++)
                {
                    if (active == CarryIdleStates[i])
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsRunningFsmActive()
        {
            if (_runningFsm?.Fsm == null) return false;
            try { return _runningFsm.Fsm.ActiveStateName == "Run"; }
            catch { return false; }
        }
    }
}
