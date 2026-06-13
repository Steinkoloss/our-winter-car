using UnityEngine;
using WinterMP.Core;
using WinterMP.Net;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Teleports the local guest PLAYER to a chosen spawn pose once.
    /// </summary>
    internal sealed class GuestSpawnRelocator
    {
        private const string PlayerObjectName = "PLAYER";

        private bool _pending;
        private NetVector3 _targetFeet;
        private NetQuaternion _targetRotation = NetQuaternion.Identity;

        public bool HasPending => _pending;

        public void ApplyImmediate(NetVector3 feet, NetQuaternion rotation)
        {
            _targetFeet = feet;
            _targetRotation = rotation;
            _pending = true;
            TryApply();
        }

        public void TryApply()
        {
            if (!_pending) return;

            var playerObject = GameObject.Find(PlayerObjectName);
            if (playerObject == null) return;

            var player = playerObject.transform;
            var controller = playerObject.GetComponent<CharacterController>();
            bool controllerWasEnabled = controller != null && controller.enabled;

            if (controller != null)
                controller.enabled = false;

            ApplyFeetPosition(player, controller, _targetFeet.ToUnity());
            ApplyYaw(player, _targetRotation.ToUnity());

            if (controller != null && controllerWasEnabled)
                controller.enabled = true;

            _pending = false;
        }

        private static void ApplyFeetPosition(Transform player, CharacterController? controller, Vector3 targetFeet)
        {
            if (controller == null)
            {
                player.position = targetFeet;
                return;
            }

            Vector3 currentFeet = PlayerPoseReader.ReadFeetPosition(player, controller);
            player.position += targetFeet - currentFeet;
        }

        private static void ApplyYaw(Transform player, Quaternion targetYaw)
        {
            float yaw = targetYaw.eulerAngles.y;
            player.rotation = Quaternion.Euler(0f, yaw, 0f);

            var pivot = player.Find("Pivot/AnimPivot");
            if (pivot != null)
                pivot.localRotation = Quaternion.identity;
        }
    }
}
