using System.Collections.Generic;
using UnityEngine;

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
        public static string Of(Transform transform)
        {
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
