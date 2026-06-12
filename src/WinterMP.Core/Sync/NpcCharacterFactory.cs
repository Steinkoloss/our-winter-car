using System;
using UnityEngine;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Visual-only NPC mesh for remote players. Clones the walker <c>Pivot</c>
    /// subtree only — no Move/AI FSMs, which fight network look rotation.
    /// </summary>
    internal static class NpcCharacterFactory
    {
        private const string HumansRoot = "HUMANS";
        private const string WalkersRoot = "HUMANS/Randomizer/Walkers";

        private static readonly string[] WalkerTemplates =
        {
            "Kristian", "Alpo", "Julli", "Kale", "Rauno", "Unto",
        };

        private static Transform? _walkersRoot;

        public sealed class CharacterRig
        {
            public GameObject Root = null!;
            public Transform BodyPivot = null!;
            public float ModelHeight = 1.8f;
            public float FootOffsetY;
            /// <summary>Local Y rotation so the mesh faces avatar +Z at yaw 0.</summary>
            public float ModelYawOffset;
            public string TemplateName = string.Empty;
        }

        public static CharacterRig? TryCreate(byte playerId)
        {
            var template = FindTemplate(playerId);
            if (template == null) return null;

            var templatePivot = template.transform.Find("Pivot");
            if (templatePivot == null)
            {
                WinterMPPlugin.Log.LogWarning(
                    $"PlayerSync: walker '{template.name}' has no Pivot — cannot build avatar.");
                return null;
            }

            var clone = UnityEngine.Object.Instantiate(templatePivot.gameObject);
            clone.name = $"WinterMP_Char_{playerId}";
            clone.transform.position = Vector3.zero;
            clone.transform.rotation = Quaternion.identity;
            clone.SetActive(true);

            StripSimulation(clone);
            TryApplyStandingPose(clone.transform);

            var renderers = clone.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                UnityEngine.Object.Destroy(clone);
                return null;
            }

            var rig = new CharacterRig
            {
                Root = clone,
                BodyPivot = FindBodyPivot(clone.transform) ?? clone.transform,
                TemplateName = template.name,
            };

            MeasureFootAndHeight(clone.transform, out rig.ModelHeight, out rig.FootOffsetY);
            rig.ModelYawOffset = ComputeModelYawOffset(clone.transform);
            ApplyPlayerTint(clone, playerId);

            WinterMPPlugin.Log.LogInfo(
                $"PlayerSync: avatar rig '{rig.TemplateName}' for player {playerId} " +
                $"(h={rig.ModelHeight:F2}m foot={rig.FootOffsetY:F2}m yaw={rig.ModelYawOffset:F0}°).");

            return rig;
        }

        private static GameObject? FindTemplate(byte playerId)
        {
            EnsureWalkersCache();
            if (_walkersRoot != null)
            {
                int index = playerId % WalkerTemplates.Length;
                for (int i = 0; i < WalkerTemplates.Length; i++)
                {
                    string name = WalkerTemplates[(index + i) % WalkerTemplates.Length];
                    var child = _walkersRoot.Find(name);
                    if (child != null)
                        return child.gameObject;
                }

                if (_walkersRoot.childCount > 0)
                    return _walkersRoot.GetChild(0).gameObject;
            }

            var humans = GameObject.Find(HumansRoot);
            if (humans == null || humans.transform.childCount == 0) return null;

            int fallbackIndex = playerId % humans.transform.childCount;
            return humans.transform.GetChild(fallbackIndex).gameObject;
        }

        private static void EnsureWalkersCache()
        {
            if (_walkersRoot != null) return;
            var walkers = GameObject.Find(WalkersRoot);
            if (walkers != null)
                _walkersRoot = walkers.transform;
        }

        public static void ClearCache() => _walkersRoot = null;

        private static Transform? FindBodyPivot(Transform root)
        {
            var body = root.Find("Char");
            return body != null ? body : root;
        }

        private static void StripSimulation(GameObject root)
        {
            DestroyChild(root.transform, "RagDoll");
            DestroyChild(root.transform, "HumanTriggerCrime");

            var fsms = root.GetComponentsInChildren<PlayMakerFSM>(true);
            for (int i = 0; i < fsms.Length; i++)
                UnityEngine.Object.Destroy(fsms[i]);

            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                UnityEngine.Object.Destroy(colliders[i]);

            var bodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
                UnityEngine.Object.Destroy(bodies[i]);

            var controllers = root.GetComponentsInChildren<CharacterController>(true);
            for (int i = 0; i < controllers.Length; i++)
                UnityEngine.Object.Destroy(controllers[i]);

            var audioSources = root.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < audioSources.Length; i++)
                UnityEngine.Object.Destroy(audioSources[i]);

            var animations = root.GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < animations.Length; i++)
                UnityEngine.Object.Destroy(animations[i]);
        }

        private static float ComputeModelYawOffset(Transform rigRoot)
        {
            var pelvis = rigRoot.Find("Char/skeleton/pelvis");
            if (pelvis == null)
                return 0f;

            Vector3 forward = pelvis.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return 0f;

            return -YawFromForward(forward);
        }

        private static float YawFromForward(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return 0f;

            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        private static void TryApplyStandingPose(Transform clonePivot)
        {
            var referencePivot = FindStandingReferencePivot();
            if (referencePivot == null) return;

            var sourceSkeleton = referencePivot.Find("Char/skeleton");
            var destSkeleton = clonePivot.Find("Char/skeleton");
            if (sourceSkeleton == null || destSkeleton == null) return;

            CopyBonePose(sourceSkeleton, destSkeleton);
        }

        private static Transform? FindStandingReferencePivot()
        {
            EnsureWalkersCache();
            if (_walkersRoot == null) return null;

            Transform? fallback = null;
            for (int i = 0; i < _walkersRoot.childCount; i++)
            {
                var walker = _walkersRoot.GetChild(i);
                var pivot = walker.Find("Pivot");
                if (pivot == null) continue;

                if (fallback == null)
                    fallback = pivot;

                if (IsWalkerStanding(walker.gameObject))
                    return pivot;
            }

            return fallback;
        }

        private static bool IsWalkerStanding(GameObject walker)
        {
            foreach (var fsm in walker.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName != "Move" || fsm.Fsm == null) continue;
                try { return fsm.Fsm.ActiveStateName == "Standing"; }
                catch { return false; }
            }

            return false;
        }

        private static void CopyBonePose(Transform source, Transform dest)
        {
            dest.localPosition = source.localPosition;
            dest.localRotation = source.localRotation;
            dest.localScale = source.localScale;

            for (int i = 0; i < dest.childCount; i++)
            {
                var destChild = dest.GetChild(i);
                var sourceChild = source.Find(destChild.name);
                if (sourceChild != null)
                    CopyBonePose(sourceChild, destChild);
            }
        }

        private static void DestroyChild(Transform root, string childName)
        {
            var child = root.Find(childName);
            if (child != null)
                UnityEngine.Object.Destroy(child.gameObject);
        }

        private static void MeasureFootAndHeight(Transform rigRoot, out float height, out float footOffsetY)
        {
            var renderers = rigRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                height = 1.8f;
                footOffsetY = 0f;
                return;
            }

            float minLocalY = float.PositiveInfinity;
            float maxLocalY = float.NegativeInfinity;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;

                AccumulateLocalBoundsY(rigRoot, renderer.bounds, ref minLocalY, ref maxLocalY);
            }

            if (float.IsPositiveInfinity(minLocalY))
            {
                height = 1.8f;
                footOffsetY = 0f;
                return;
            }

            footOffsetY = -minLocalY;
            height = Mathf.Max(1.2f, maxLocalY - minLocalY);
        }

        private static void AccumulateLocalBoundsY(
            Transform rigRoot, Bounds worldBounds, ref float minLocalY, ref float maxLocalY)
        {
            Vector3 center = worldBounds.center;
            Vector3 extents = worldBounds.extents;
            for (int xi = -1; xi <= 1; xi += 2)
            {
                for (int yi = -1; yi <= 1; yi += 2)
                {
                    for (int zi = -1; zi <= 1; zi += 2)
                    {
                        var corner = center + new Vector3(
                            extents.x * xi,
                            extents.y * yi,
                            extents.z * zi);
                        float localY = rigRoot.InverseTransformPoint(corner).y;
                        if (localY < minLocalY) minLocalY = localY;
                        if (localY > maxLocalY) maxLocalY = localY;
                    }
                }
            }
        }

        private static void ApplyPlayerTint(GameObject root, byte playerId)
        {
            Color tint = ColorForPlayer(playerId);
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;

                var material = renderer.material;
                if (material == null) continue;

                material.color = Color.Lerp(material.color, tint, 0.3f);
            }
        }

        private static Color ColorForPlayer(byte playerId)
        {
            float hue = (playerId * 0.618034f) % 1f;
            return HsvToRgb(hue, 0.55f, 0.95f);
        }

        private static Color HsvToRgb(float h, float s, float v)
        {
            float r = Mathf.Abs(h * 6f - 3f) - 1f;
            float g = 2f - Mathf.Abs(h * 6f - 2f);
            float b = 2f - Mathf.Abs(h * 6f - 4f);
            var rgb = new Vector3(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b));
            rgb = Vector3.Lerp(Vector3.one, rgb, s) * v;
            return new Color(rgb.x, rgb.y, rgb.z);
        }
    }
}
