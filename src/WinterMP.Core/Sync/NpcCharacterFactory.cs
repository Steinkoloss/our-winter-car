using System;
using UnityEngine;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Builds a visual-only third-person body for remote players by cloning a
    /// HUMANS walker. Keeps the root <c>Move</c> FSM for leg animation but
    /// strips AI/navigation so the rig stays parented to the avatar.
    /// </summary>
    internal static class NpcCharacterFactory
    {
        private const string HumansRoot = "HUMANS";
        private const string WalkersRoot = "HUMANS/Randomizer/Walkers";

        private static readonly string[] WalkerTemplates =
        {
            "Kristian", "Alpo", "Julli", "Kale", "Rauno", "Unto",
        };

        private static readonly string[] KeepFsmNames = { "Move" };

        private static Transform? _walkersRoot;

        public sealed class CharacterRig
        {
            public GameObject Root = null!;
            public Transform? Pivot;
            public Vector3 PivotBaseLocalPos;
            public Quaternion PivotBaseLocalRot = Quaternion.identity;
            public Transform? LookTarget;
            public Transform BodyPivot = null!;
            public PlayMakerFSM? MoveFsm;
            public float ModelHeight = 1.8f;
            public float FootOffsetY;
            public string TemplateName = string.Empty;
        }

        public static CharacterRig? TryCreate(byte playerId)
        {
            var template = FindTemplate(playerId);
            if (template == null) return null;

            var pivot = template.transform.Find("Pivot");
            if (pivot == null)
            {
                WinterMPPlugin.Log.LogWarning(
                    $"PlayerSync: walker '{template.name}' has no Pivot — cannot build avatar.");
                return null;
            }

            // Clone the walker root so we get the Move FSM (skeletal walk/idle).
            // AI FSMs and navigation helpers are stripped; Move is rewired in-place.
            var clone = UnityEngine.Object.Instantiate(template);
            clone.name = $"WinterMP_Char_{playerId}";
            clone.transform.position = Vector3.zero;
            clone.transform.rotation = Quaternion.identity;
            clone.SetActive(true);

            StripSimulation(clone);

            var renderers = clone.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                UnityEngine.Object.Destroy(clone);
                return null;
            }

            var clonePivot = clone.transform.Find("Pivot");
            var rig = new CharacterRig
            {
                Root = clone,
                Pivot = clonePivot,
                PivotBaseLocalPos = clonePivot != null ? clonePivot.localPosition : Vector3.zero,
                PivotBaseLocalRot = clonePivot != null ? clonePivot.localRotation : Quaternion.identity,
                BodyPivot = FindBodyPivot(clone.transform) ?? clone.transform,
                TemplateName = template.name,
            };

            var lookTarget = new GameObject("WinterMP_LookTarget");
            lookTarget.transform.parent = clone.transform;
            lookTarget.transform.localPosition = new Vector3(0f, 1.2f, 2f);
            rig.LookTarget = lookTarget.transform;

            rig.MoveFsm = FindMoveFsm(clone);
            if (rig.MoveFsm == null)
            {
                UnityEngine.Object.Destroy(clone);
                WinterMPPlugin.Log.LogWarning(
                    $"PlayerSync: walker '{template.name}' has no Move FSM — cannot animate avatar.");
                return null;
            }

            NeutralizeMoveFsm(rig);

            MeasureFootAndHeight(clone.transform, out rig.ModelHeight, out rig.FootOffsetY);
            ApplyPlayerTint(clone, playerId);

            WinterMPPlugin.Log.LogInfo(
                $"PlayerSync: avatar rig '{rig.TemplateName}' for player {playerId} " +
                $"(h={rig.ModelHeight:F2}m foot={rig.FootOffsetY:F2}m).");

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
            var pivot = root.Find("Pivot");
            if (pivot == null) return root;

            var body = pivot.Find("Char");
            return body != null ? body : pivot;
        }

        private static PlayMakerFSM? FindMoveFsm(GameObject root)
        {
            foreach (var fsm in root.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName == "Move")
                    return fsm;
            }

            return null;
        }

        /// <summary>
        /// Point Move at our own skeleton with zero travel distance so legs animate
        /// in place instead of pathing toward world TargetPoints.
        /// </summary>
        private static void NeutralizeMoveFsm(CharacterRig rig)
        {
            var fsm = rig.MoveFsm;
            if (fsm == null) return;

            try
            {
                var skeleton = rig.Root.transform.Find("Pivot/Char/skeleton")
                    ?? rig.BodyPivot.Find("skeleton")
                    ?? rig.BodyPivot;

                var skeletonVar = fsm.FsmVariables.FindFsmGameObject("Skeleton");
                if (skeletonVar != null)
                    skeletonVar.Value = skeleton.gameObject;

                // A point in front of the body — Move FSM rotates the skeleton toward Target.
                var targetVar = fsm.FsmVariables.FindFsmGameObject("Target");
                if (targetVar != null && rig.LookTarget != null)
                    targetVar.Value = rig.LookTarget.gameObject;

                var distanceVar = fsm.FsmVariables.FindFsmFloat("Distance");
                if (distanceVar != null)
                    distanceVar.Value = 0f;

                fsm.SendEvent("STAND");
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning($"PlayerSync: could not neutralize Move FSM: {e.Message}");
            }
        }

        private static void StripSimulation(GameObject root)
        {
            DestroyChild(root.transform, "Pivot/RagDoll");
            DestroyChild(root.transform, "Pivot/HumanTriggerCrime");

            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i);
                string name = child.name;
                if (name.StartsWith("HeadTarget", StringComparison.Ordinal)
                    || name == "TargetPoint")
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }

            var fsms = root.GetComponentsInChildren<PlayMakerFSM>(true);
            for (int i = 0; i < fsms.Length; i++)
            {
                if (Array.IndexOf(KeepFsmNames, fsms[i].FsmName) >= 0)
                    continue;
                UnityEngine.Object.Destroy(fsms[i]);
            }

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
        }

        private static void DestroyChild(Transform root, string path)
        {
            var child = root.Find(path);
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

            // Shift the rig so the lowest mesh point sits on the avatar origin (feet).
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
