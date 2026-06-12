using UnityEngine;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Reads pose from the local PLAYER hierarchy (feet position, look yaw).
    /// </summary>
    internal static class PlayerPoseReader
    {
        private const string AnimPivotPath = "Pivot/AnimPivot";
        private const string FpsCameraPath = "Pivot/AnimPivot/Camera/FPSCamera";

        /// <summary>World position at the feet — matches where remote avatars should stand.</summary>
        public static Vector3 ReadFeetPosition(Transform player, CharacterController? controller)
        {
            if (controller == null)
                return player.position;

            Vector3 worldCenter = player.TransformPoint(controller.center);
            float halfHeight = controller.height * 0.5f;
            return worldCenter - Vector3.up * halfHeight;
        }

        /// <summary>Horizontal facing from the active view (where the player is looking).</summary>
        public static Quaternion ReadLookRotation(Transform player)
        {
            Transform? source = ResolveLookTransform(player);
            if (source == null)
                return player.rotation;

            Vector3 forward = source.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return Quaternion.Euler(0f, source.eulerAngles.y, 0f);

            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private static Transform? ResolveLookTransform(Transform player)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                var t = cam.transform;
                while (t != null)
                {
                    if (t == player) return cam.transform;
                    t = t.parent;
                }
            }

            var fps = player.Find(FpsCameraPath);
            if (fps != null) return fps;

            return player.Find(AnimPivotPath);
        }
    }
}
