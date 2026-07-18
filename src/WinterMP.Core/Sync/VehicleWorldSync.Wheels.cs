using System;
using System.Collections.Generic;
using UnityEngine;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        private const float DefaultWheelRadius = 0.3f;
        private const float WheelMinMoveMeters = 1e-4f;
        /// <summary>A per-frame body move larger than this is a snap/teleport, not a roll —
        /// don't spin the wheels a full turn from a resync jump.</summary>
        private const float WheelMaxMoveMeters = 5f;

        private static readonly Transform[] EmptyWheels = new Transform[0];

        /// <summary>
        /// Observer-side visual wheel roll for a remote car. Synced cars are pinned kinematic
        /// and moved by transform, so the game's own wheel sim never turns the meshes and the
        /// car "skates" on frozen wheels. Roll the tire meshes from the body's own forward
        /// displacement — purely presentational, derived from the already-synced pose (no wire
        /// change). Called from LateUpdate so a game-side wheel reset can't overwrite it.
        /// </summary>
        internal static void RollRemoteWheels(SyncedItem item)
        {
            try
            {
                EnsureWheels(item);
                var wheels = item.WheelSpinners;
                if (wheels == null || wheels.Length == 0 || item.Body == null) return;

                var t = item.Body.transform;
                Vector3 pos = t.position;
                if (!item.WheelLastPosSet)
                {
                    item.WheelLastPos = pos;
                    item.WheelLastPosSet = true;
                    return;
                }

                float forward = Vector3.Dot(pos - item.WheelLastPos, t.forward);
                item.WheelLastPos = pos;
                if (Mathf.Abs(forward) < WheelMinMoveMeters || Mathf.Abs(forward) > WheelMaxMoveMeters)
                    return;

                // Positive rotation about the car's right axis rolls the wheel top forward, so
                // forward motion spins the wheels forward. One shared world axis keeps both
                // sides visually consistent.
                float degrees = forward / item.WheelRadius * Mathf.Rad2Deg;
                Vector3 axis = t.right;
                for (int i = 0; i < wheels.Length; i++)
                {
                    if (wheels[i] != null)
                        wheels[i].Rotate(axis, degrees, Space.World);
                }
            }
            catch (Exception e)
            {
                // A wheel-visual hiccup must never trip the shared world-sync kill switch.
                item.WheelsSearched = true;
                item.WheelSpinners = EmptyWheels;
                WinterMPPlugin.Log.LogDebug("WorldSync: wheel roll failed on '" + item.Path + "': " + e.Message);
            }
        }

        private static void EnsureWheels(SyncedItem item)
        {
            if (item.WheelsSearched || item.Body == null) return;
            item.WheelsSearched = true;

            var spinners = new List<Transform>();
            float radius = 0f;
            foreach (var tr in item.Body.GetComponentsInChildren<Transform>(true))
            {
                if (!IsWheelNodeName(tr.name)) continue;

                // The wheel node holds either just the mesh (SORBET WHEEL_XX) or a "tire"
                // subnode alongside the suspension/spindle (CORRIS WHEELc_XX). Spin only the
                // tire mesh so we never swing the suspension arms nested under WHEELc_XX.
                Transform spinner = tr.Find("tire") ?? tr;
                spinners.Add(spinner);

                float r = WheelRadiusOf(spinner);
                if (r > radius) radius = r;
            }

            item.WheelSpinners = spinners.ToArray();
            item.WheelRadius = radius > 0.05f ? radius : DefaultWheelRadius;
            if (spinners.Count > 0)
                WinterMPPlugin.Log.LogInfo(
                    $"WorldSync: {spinners.Count} wheels on '{item.Path}' (r={item.WheelRadius:F2} m).");
        }

        // SORBET "WHEEL_FR", CORRIS "WHEELc_FR" (+ FL/RR/RL). Excludes containers ("Wheels",
        // "RearWheelsStatic"), spindles ("wheel_spindle_rl"), the steering wheel and flywheel.
        private static bool IsWheelNodeName(string name)
        {
            if (!name.StartsWith("WHEEL", StringComparison.Ordinal)) return false;
            int u = name.IndexOf('_');
            if (u < 0) return false;
            string prefix = name.Substring(0, u);
            if (prefix != "WHEEL" && prefix != "WHEELc") return false;
            switch (name.Substring(u + 1))
            {
                case "FR":
                case "FL":
                case "RR":
                case "RL":
                    return true;
                default:
                    return false;
            }
        }

        private static float WheelRadiusOf(Transform wheel)
        {
            Bounds bounds = default;
            bool has = false;
            foreach (var r in wheel.GetComponentsInChildren<Renderer>(true))
            {
                if (has) bounds.Encapsulate(r.bounds);
                else { bounds = r.bounds; has = true; }
            }

            if (!has) return 0f;
            // World-space AABB; the wheel stands upright so its largest extent is the radius.
            Vector3 e = bounds.extents;
            return Mathf.Max(e.x, Mathf.Max(e.y, e.z));
        }
    }
}
