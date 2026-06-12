using UnityEngine;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Reads pose from the local PLAYER hierarchy (feet position, look yaw).
    /// </summary>
    internal static class PlayerPoseReader
    {
        private const string AnimPivotPath = "Pivot/AnimPivot";
        private const string ViewCameraPath = "Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Camera/Camera";

        public static Vector3 ReadFeetPosition(Transform player, CharacterController? controller)
        {
            if (controller == null)
                return player.position;

            Vector3 worldCenter = player.TransformPoint(controller.center);
            float halfHeight = controller.height * 0.5f;
            return worldCenter - Vector3.up * halfHeight;
        }

        /// <summary>Horizontal look yaw in degrees from the active view.</summary>
        public static float ReadLookYawDegrees(Transform player)
        {
            var view = player.Find(ViewCameraPath);
            if (view != null)
                return YawFromForward(view.forward);

            var camera = FindViewCamera(player);
            if (camera != null)
                return YawFromForward(camera.transform.forward);

            if (Camera.main != null && Camera.main.transform.IsChildOf(player))
                return YawFromForward(Camera.main.transform.forward);

            var pivot = player.Find(AnimPivotPath);
            if (pivot != null)
                return YawFromForward(pivot.forward);

            return YawFromForward(player.forward);
        }

        public static Quaternion ReadLookRotation(Transform player)
        {
            return Quaternion.Euler(0f, ReadLookYawDegrees(player), 0f);
        }

        private static Camera? FindViewCamera(Transform player)
        {
            var cameras = player.GetComponentsInChildren<Camera>(true);
            Camera? best = null;
            float bestDepth = float.NegativeInfinity;

            for (int i = 0; i < cameras.Length; i++)
            {
                var camera = cameras[i];
                if (camera == null) continue;
                if (camera.depth < bestDepth) continue;

                bestDepth = camera.depth;
                best = camera;
            }

            return best;
        }

        private static float YawFromForward(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return 0f;

            return Quaternion.LookRotation(forward.normalized).eulerAngles.y;
        }
    }
}
