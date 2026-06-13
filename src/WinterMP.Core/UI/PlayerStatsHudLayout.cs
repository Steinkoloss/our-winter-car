using UnityEngine;

namespace WinterMP.Core.UI
{
    /// <summary>
    /// MWC leaves a large margin around the player stats HUD (GUI/HUD). Re-pin the
    /// whole block to the top-left with a small pixel padding each frame.
    /// </summary>
    public sealed class PlayerStatsHudLayout : MonoBehaviour
    {
        private const string GuiObjectName = "GUI";
        private const string HudObjectName = "HUD";
        private const float ScreenPaddingPixels = 8f;
        private const float PinEpsilonPixels = 0.75f;

        private Transform? _hud;
        private Vector3 _targetLocalPosition;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private string _lastLevel = string.Empty;
        private bool _loggedPin;

        private void LateUpdate()
        {
            if (IsMainMenu()) return;

            if (!EnsureHud(out var hud, out var camera)) return;

            if (NeedsRetarget(hud, camera))
                Retarget(hud, camera);

            if (Vector3.SqrMagnitude(hud.localPosition - _targetLocalPosition) > 0.000001f)
                hud.localPosition = _targetLocalPosition;
        }

        private bool IsMainMenu()
        {
            string level;
            try
            {
                level = Application.loadedLevelName ?? string.Empty;
            }
            catch
            {
                return true;
            }

            if (level != _lastLevel)
            {
                _lastLevel = level;
                ClearCache();
            }

            return level == "MainMenu";
        }

        private bool EnsureHud(out Transform hud, out Camera camera)
        {
            camera = Camera.main;
            if (_hud != null && camera != null)
            {
                hud = _hud;
                return true;
            }

            _hud = null;
            Transform? resolved = ResolveHudTransform();
            if (resolved == null || camera == null)
            {
                hud = null!;
                return false;
            }

            hud = resolved;
            _hud = resolved;
            return true;
        }

        private bool NeedsRetarget(Transform hud, Camera camera)
        {
            return !_loggedPin
                || _lastScreenWidth != Screen.width
                || _lastScreenHeight != Screen.height
                || Vector3.SqrMagnitude(hud.localPosition - _targetLocalPosition) > 0.04f;
        }

        private void Retarget(Transform hud, Camera camera)
        {
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;

            Vector3 vanillaLocal = hud.localPosition;
            if (!TryMeasureTopLeft(hud, camera, out Vector2 currentTopLeft))
            {
                _targetLocalPosition = vanillaLocal;
                return;
            }

            var desiredTopLeft = new Vector2(
                ScreenPaddingPixels,
                Screen.height - ScreenPaddingPixels);

            float dx = desiredTopLeft.x - currentTopLeft.x;
            float dy = desiredTopLeft.y - currentTopLeft.y;
            if (Mathf.Abs(dx) <= PinEpsilonPixels && Mathf.Abs(dy) <= PinEpsilonPixels)
            {
                _targetLocalPosition = vanillaLocal;
                return;
            }

            float depth = camera.WorldToScreenPoint(hud.position).z;
            Vector3 worldBefore = camera.ScreenToWorldPoint(
                new Vector3(currentTopLeft.x, currentTopLeft.y, depth));
            Vector3 worldAfter = camera.ScreenToWorldPoint(
                new Vector3(desiredTopLeft.x, desiredTopLeft.y, depth));
            Vector3 worldDelta = worldAfter - worldBefore;

            Transform parent = hud.parent;
            Vector3 localDelta = parent != null
                ? parent.InverseTransformVector(worldDelta)
                : worldDelta;

            _targetLocalPosition = vanillaLocal + localDelta;
            hud.localPosition = _targetLocalPosition;

            if (!_loggedPin)
            {
                _loggedPin = true;
                WinterMPPlugin.Log.LogInfo(
                    "PlayerStatsHudLayout: pinned GUI/HUD to top-left (padding "
                    + ScreenPaddingPixels.ToString("0")
                    + " px).");
            }
        }

        private static Transform? ResolveHudTransform()
        {
            var gui = GameObject.Find(GuiObjectName);
            if (gui == null) return null;

            var hud = gui.transform.Find(HudObjectName);
            return hud != null ? hud : null;
        }

        private static bool TryMeasureTopLeft(Transform hud, Camera camera, out Vector2 topLeft)
        {
            topLeft = Vector2.zero;
            bool any = false;
            float minX = float.MaxValue;
            float maxY = float.MinValue;

            var renderers = hud.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled) continue;

                Transform t = renderer.transform;
                if (!t.gameObject.activeInHierarchy) continue;
                if (IsDebugHud(t)) continue;

                Bounds bounds = renderer.bounds;
                Vector3 screenMin = camera.WorldToScreenPoint(bounds.min);
                Vector3 screenMax = camera.WorldToScreenPoint(bounds.max);
                if (screenMin.z <= 0f && screenMax.z <= 0f) continue;

                float left = Mathf.Min(screenMin.x, screenMax.x);
                float top = Mathf.Max(screenMin.y, screenMax.y);

                if (left < minX) minX = left;
                if (top > maxY) maxY = top;
                any = true;
            }

            if (!any) return false;

            topLeft = new Vector2(minX, maxY);
            return true;
        }

        private static bool IsDebugHud(Transform transform)
        {
            Transform? current = transform;
            while (current != null)
            {
                if (current.name == "Debug")
                    return true;
                current = current.parent;
            }

            return false;
        }

        private void ClearCache()
        {
            _hud = null;
            _loggedPin = false;
            _lastScreenWidth = 0;
            _lastScreenHeight = 0;
        }
    }
}
