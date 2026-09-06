using System.Collections.Generic;
using UnityEngine;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Builds the stable scene path used for network ids (PLAN §4.1). Matches the
    /// catalog dumper's plain "Root/Child/Leaf" format, except that when several
    /// siblings share a name (e.g. the four BoltPM bolts under a part's Bolts node)
    /// each gets an index suffix: "Bolts/BoltPM[2]". Sibling order comes from the
    /// prefab/scene definition, so it is identical on every machine running the
    /// same game build.
    /// </summary>
    internal static class ScenePath
    {
        private static ScenePathCache<Transform>? _scanPaths;
        private static float _nextSlowScanLogAt;

        /// <summary>The index lives only while the caller enumerates this scan. It never
        /// survives to another Update or hides a later reparent/rename from discovery.</summary>
        public static IEnumerable<UnityEngine.Object> ScanFsms() => Scan(typeof(PlayMakerFSM));
        public static IEnumerable<UnityEngine.Object> ScanRigidbodies() => Scan(typeof(Rigidbody));

        private static IEnumerable<UnityEngine.Object> Scan(System.Type type)
        {
            var previous = _scanPaths;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            _scanPaths = new ScenePathCache<Transform>(
                node => node.parent != null ? node.parent : null,
                node => node.name, node => node.childCount, (node, index) => node.GetChild(index));
            try
            {
                foreach (var obj in Resources.FindObjectsOfTypeAll(type))
                    yield return obj;
            }
            finally
            {
                _scanPaths = previous;
                timer.Stop();
                if (timer.Elapsed.TotalMilliseconds >= 50 && Time.unscaledTime >= _nextSlowScanLogAt)
                {
                    _nextSlowScanLogAt = Time.unscaledTime + 10f;
                    string detail = type.Name + " discovery took " + timer.Elapsed.TotalMilliseconds.ToString("F0") + " ms";
                    WinterMPPlugin.Log.LogWarning("WorldSync: " + detail);
                    Diagnostics.SyncEventLog.Record("slow-scan", detail);
                }
            }
        }

        public static string Of(Transform transform)
        {
            if (_scanPaths != null) return _scanPaths.Of(transform);
            var segments = new List<string>(8);
            var current = transform;
            while (current != null)
            {
                segments.Add(SegmentFor(current));
                current = current.parent;
            }

            segments.Reverse();
            return string.Join("/", segments.ToArray());
        }

        public static string? RelativeTo(Transform node, Transform root)
        {
            if (_scanPaths != null) return _scanPaths.RelativeTo(node, root);
            var segments = new List<string>();
            for (var current = node; current != null; current = current.parent)
            {
                if (current == root) { segments.Reverse(); return string.Join("/", segments.ToArray()); }
                if (current.parent == null) return null;
                segments.Add(SegmentFor(current));
            }
            return null;
        }

        public static Transform? FindRelative(Transform root, string path) =>
            new ScenePathCache<Transform>(node => node.parent != null ? node.parent : null,
                node => node.name, node => node.childCount, (node, index) => node.GetChild(index)).FindRelative(root, path);

        private static string SegmentFor(Transform node)
        {
            var parent = node.parent;
            if (parent == null) return node.name; // roots can't be enumerated on Unity 5.0

            string name = node.name;
            int sameNamed = 0;
            int index = -1;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name != name) continue;
                if (child == node) index = sameNamed;
                sameNamed++;
            }

            return sameNamed > 1 ? name + "[" + index + "]" : name;
        }
    }
}
