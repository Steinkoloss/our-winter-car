using System;
using UnityEngine;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Visual NPC mesh plus a hidden Move-FSM driver walker. The visible Pivot
    /// clone keeps network look rotation; the driver supplies skeletal walk cycles.
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
            public GameObject? MoveDriver;
            public Transform? DriverPivot;
            public Vector3 DriverPivotBaseLocalPos;
            public Quaternion DriverPivotBaseLocalRot = Quaternion.identity;
            public Transform? DriverSkeleton;
            public Transform? Skeleton;
            public Transform? LookTarget;
            public Transform BodyPivot = null!;
            public PlayMakerFSM? MoveFsm;
            public float ModelHeight = 1.8f;
            public float FootOffsetY;
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

            StripVisualSimulation(clone);
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
                Skeleton = clone.transform.Find("Char/skeleton"),
                TemplateName = template.name,
            };

            if (!TryCreateMoveDriver(template, playerId, rig))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"PlayerSync: walker '{template.name}' has no Move FSM — legs will stay idle.");
            }

            MeasureFootAndHeight(clone.transform, out rig.ModelHeight, out rig.FootOffsetY);
            rig.ModelYawOffset = ComputeModelYawOffset(clone.transform);
            ApplyPlayerTint(clone, playerId);

            WinterMPPlugin.Log.LogInfo(
                $"PlayerSync: avatar rig '{rig.TemplateName}' for player {playerId} " +
                $"(h={rig.ModelHeight:F2}m foot={rig.FootOffsetY:F2}m yaw={rig.ModelYawOffset:F0}° " +
                $"driver={(rig.MoveFsm != null ? "yes" : "no")}).");

            return rig;
        }

        internal static void CopySkeletonPose(Transform source, Transform dest)
        {
            if (source == null || dest == null) return;
            CopyBonePose(source, dest);
        }

        internal static void CopyPivotPose(Transform sourcePivot, Transform destPivot)
        {
            if (sourcePivot == null || destPivot == null) return;
            CopyBonePose(sourcePivot, destPivot);
        }

        /// <summary>
        /// Copies animated child bones (Char, etc.) without touching the pivot root transform.
        /// The visible rig root keeps network foot offset and look-yaw from <see cref="RemoteAvatar"/>.
        /// </summary>
        internal static void CopyAnimatedPose(Transform sourcePivot, Transform destPivot)
        {
            if (sourcePivot == null || destPivot == null) return;

            for (int i = 0; i < destPivot.childCount; i++)
            {
                var destChild = destPivot.GetChild(i);
                var sourceChild = sourcePivot.Find(destChild.name);
                if (sourceChild != null)
                    CopyBonePose(sourceChild, destChild);
            }
        }

        internal static void BindMoveTarget(PlayMakerFSM? moveFsm, GameObject lookTarget)
        {
            if (moveFsm == null) return;

            try
            {
                var targetVar = moveFsm.FsmVariables.FindFsmGameObject("Target");
                if (targetVar != null)
                    targetVar.Value = lookTarget;
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning($"PlayerSync: could not bind Move target: {e.Message}");
            }
        }

        private static bool TryCreateMoveDriver(GameObject template, byte playerId, CharacterRig rig)
        {
            var driver = UnityEngine.Object.Instantiate(template);
            driver.name = $"WinterMP_Driver_{playerId}";
            driver.transform.position = Vector3.zero;
            driver.transform.rotation = Quaternion.identity;
            driver.SetActive(true);

            StripDriverSimulation(driver);
            SetRenderersEnabled(driver, false);
            TryApplyStandingPoseToDriver(driver.transform);

            var driverPivot = driver.transform.Find("Pivot");
            rig.MoveDriver = driver;
            rig.DriverPivot = driverPivot;
            rig.DriverPivotBaseLocalPos = driverPivot != null ? driverPivot.localPosition : Vector3.zero;
            rig.DriverPivotBaseLocalRot = driverPivot != null ? driverPivot.localRotation : Quaternion.identity;
            rig.DriverSkeleton = driver.transform.Find("Pivot/Char/skeleton");
            rig.MoveFsm = FindMoveFsm(driver);

            if (rig.MoveFsm == null || rig.DriverSkeleton == null || rig.Skeleton == null)
            {
                UnityEngine.Object.Destroy(driver);
                rig.MoveDriver = null;
                rig.MoveFsm = null;
                rig.DriverSkeleton = null;
                rig.LookTarget = null;
                return false;
            }

            var lookTarget = new GameObject("WinterMP_LookTarget");
            lookTarget.transform.parent = driver.transform;
            lookTarget.transform.localPosition = new Vector3(0f, 1.2f, 2f);
            rig.LookTarget = lookTarget.transform;

            NeutralizeMoveFsm(rig);
            if (rig.DriverPivot != null)
                CopyPivotPose(rig.DriverPivot, rig.Root.transform);
            return true;
        }

        private static void TryApplyStandingPoseToDriver(Transform driverRoot)
        {
            var referencePivot = FindStandingReferencePivot();
            if (referencePivot == null) return;

            var driverPivot = driverRoot.Find("Pivot");
            if (driverPivot == null) return;

            var sourceSkeleton = referencePivot.Find("Char/skeleton");
            var destSkeleton = driverPivot.Find("Char/skeleton");
            if (sourceSkeleton == null || destSkeleton == null) return;

            CopyBonePose(sourceSkeleton, destSkeleton);
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

        private static PlayMakerFSM? FindMoveFsm(GameObject root)
        {
            foreach (var fsm in root.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName == "Move")
                    return fsm;
            }

            return null;
        }

        private static void NeutralizeMoveFsm(CharacterRig rig)
        {
            var fsm = rig.MoveFsm;
            if (fsm == null) return;

            try
            {
                var skeletonVar = fsm.FsmVariables.FindFsmGameObject("Skeleton");
                if (skeletonVar != null && rig.DriverSkeleton != null)
                    skeletonVar.Value = rig.DriverSkeleton.gameObject;

                if (rig.LookTarget != null)
                    BindMoveTarget(fsm, rig.LookTarget.gameObject);

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

        private static void StripVisualSimulation(GameObject root)
        {
            DestroyChild(root.transform, "RagDoll");
            DestroyChild(root.transform, "HumanTriggerCrime");
            StripComponents(root, destroyAllFsms: true, destroyAnimation: true);
        }

        private static void StripDriverSimulation(GameObject root)
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

            StripComponents(root, destroyAllFsms: false, destroyAnimation: false);
        }

        private static void StripComponents(GameObject root, bool destroyAllFsms, bool destroyAnimation)
        {
            var fsms = root.GetComponentsInChildren<PlayMakerFSM>(true);
            for (int i = 0; i < fsms.Length; i++)
            {
                if (!destroyAllFsms && Array.IndexOf(KeepFsmNames, fsms[i].FsmName) >= 0)
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

            var animations = root.GetComponentsInChildren<Animation>(true);
            if (destroyAnimation)
            {
                for (int i = 0; i < animations.Length; i++)
                    UnityEngine.Object.Destroy(animations[i]);
            }
        }

        private static void SetRenderersEnabled(GameObject root, bool enabled)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = enabled;
            }
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

            return -YawFromForward(forward) + 180f;
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
            return GetWalkerMoveState(walker) == "Standing";
        }

        private static string? GetWalkerMoveState(GameObject walker)
        {
            foreach (var fsm in walker.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName != "Move" || fsm.Fsm == null) continue;
                try { return fsm.Fsm.ActiveStateName; }
                catch { return null; }
            }

            return null;
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
