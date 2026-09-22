using System;
using System.Collections.Generic;
using UnityEngine;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class PerformanceChecks
    {
        private static Func<string, GameObject?> NewEngineSourceLookup()
        {
            var type = Core.GetType("WinterMP.Core.Sync.ItemWorldSync+EngineSourceLookup", false);
            if (type == null) return path =>
            {
                var value = GameObject.Find(path);
                return value == null || !value.activeInHierarchy ? null : value;
            };
            var lookup = Activator.CreateInstance(type, true);
            return (Func<string, GameObject?>)Delegate.CreateDelegate(typeof(Func<string, GameObject?>), lookup, type.GetMethod("Find", Members));
        }

        private static void MeasureSceneSearch(List<string> rows, int count, GameObject root, Action<string, Action> check)
        {
            var parent = root.transform.GetChild(0).GetChild(0).GetChild(0).GetChild(0).GetChild(0);
            string prefix = root.name + "/Group0/Nested0/Nested1/Nested2/Nested3/";
            var paths = new string[11];
            for (int i = 0; i < paths.Length; i++) paths[i] = prefix + "Object" + i;
            check("engine source batch: all eleven current active targets resolve " + count, () =>
            {
                var find = NewEngineSourceLookup();
                for (int i = 0; i < paths.Length; i++) Require(find(paths[i]) == parent.GetChild(i).gameObject);
            });
            Measure(rows, count, "engine-source-batch-100x11", () =>
            {
                for (int batch = 0; batch < 100; batch++)
                {
                    var find = NewEngineSourceLookup();
                    foreach (string path in paths) _observed = find(path)!.GetInstanceID();
                }
            });
            check("engine source batch: inactive child and recovery use a fresh capture " + count, () =>
            {
                var child = parent.GetChild(0).gameObject;
                child.SetActive(false);
                try { Require(NewEngineSourceLookup()(paths[0]) == null); }
                finally { child.SetActive(true); }
                Require(NewEngineSourceLookup()(paths[0]) == child);
            });
            check("engine source batch: renamed root is rejected and recovers next capture " + count, () =>
            {
                string name = root.name; root.name += " moved";
                try { Require(NewEngineSourceLookup()(paths[0]) == null); }
                finally { root.name = name; }
                Require(NewEngineSourceLookup()(paths[0]) == parent.GetChild(0).gameObject);
            });
            check("engine source batch: inactive root and recovery use a fresh capture " + count, () =>
            {
                root.SetActive(false);
                try { Require(NewEngineSourceLookup()(paths[0]) == null); }
                finally { root.SetActive(true); }
                Require(NewEngineSourceLookup()(paths[0]) == parent.GetChild(0).gameObject);
            });
        }

        private static void MeasureFullPaths(List<string> rows, Action<string, Action> check)
        {
            var path = (Func<Transform, string>)Delegate.CreateDelegate(typeof(Func<Transform, string>),
                Core.GetType("WinterMP.Core.Sync.ScenePath", true).GetMethod("Of", Static));
            var root = new GameObject("full path fixture"); root.SetActive(false);
            try
            {
                check("full path: root and null retain their text", () => Require(path(root.transform) == root.name && path(null!) == ""));
                var first = Child(root, "Bolt"); var second = Child(root, "Bolt");
                check("full path: duplicate sibling ordinals remain distinct", () =>
                    Require(path(first.transform) == root.name + "/Bolt[0]" && path(second.transform) == root.name + "/Bolt[1]"));
                var literal = Child(root, "Bolt[0]");
                check("full path: literal bracket names retain existing ambiguity", () => Require(path(literal.transform) == path(first.transform)));
                first.transform.SetParent(null, false); first.transform.SetParent(root.transform, false);
                check("full path: sibling reorder updates both duplicate ordinals", () =>
                    Require(path(first.transform) == root.name + "/Bolt[1]" && path(second.transform) == root.name + "/Bolt[0]"));
                first.name = "Renamed";
                check("full path: rename is observed and remaining sibling loses ordinal", () =>
                    Require(path(first.transform) == root.name + "/Renamed" && path(second.transform) == root.name + "/Bolt"));
                first.transform.SetParent(second.transform, false);
                check("full path: reparenting is observed on the next call", () => Require(path(first.transform) == root.name + "/Bolt/Renamed"));
                var odd = Child(first, "/ä"); var empty = Child(odd, "");
                check("full path: empty and literal separator segments retain exact output", () =>
                    Require(path(empty.transform) == root.name + "/Bolt/Renamed//ä/"));
                var destroyed = empty.transform; UnityEngine.Object.DestroyImmediate(empty);
                check("full path: destroyed Unity transforms remain empty", () => Require(path(destroyed) == ""));
                foreach (int width in new[] { 4, 32, 256 })
                {
                    var fixture = Child(root, "Width" + width); var parent = fixture;
                    string expected = root.name + "/" + fixture.name;
                    for (int depth = 0; depth < 8; depth++)
                    {
                        for (int i = 0; i < width; i++) Child(parent, "Unrelated" + i);
                        parent = Child(parent, "Mount"); expected += "/Mount";
                    }
                    var leaf = parent.transform;
                    check("full path: complete deep path at width " + width, () => Require(path(leaf) == expected));
                    Measure(rows, width, "live-full-path-1000", () =>
                    { for (int i = 0; i < 1000; i++) _observed = path(leaf).Length; });
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void MeasureRelativePaths(List<string> rows, Action<string, Action> check)
        {
            var lookup = (Func<Transform, string, Transform>)Delegate.CreateDelegate(typeof(Func<Transform, string, Transform>),
                Core.GetType("WinterMP.Core.Sync.ScenePath", true).GetMethod("FindRelative", Static));
            var root = new GameObject("relative lookup fixture"); root.SetActive(false);
            try
            {
                var first = Child(root, "Bolt"); var second = Child(root, "Bolt");
                check("relative lookup: duplicate ordinals retain native sibling identity", () =>
                    Require(lookup(root.transform, "Bolt[0]") == first.transform && lookup(root.transform, "Bolt[1]") == second.transform));
                check("relative lookup: duplicate plain names are unresolved", () => Require(lookup(root.transform, "Bolt") == null));
                var literal = Child(root, "Bolt[0]");
                check("relative lookup: literal bracket collision remains unresolved", () => Require(lookup(root.transform, "Bolt[0]") == null));
                UnityEngine.Object.DestroyImmediate(literal);
                check("relative lookup: removing a collision is observed immediately", () => Require(lookup(root.transform, "Bolt[0]") == first.transform));
                second.name = "Renamed";
                check("relative lookup: a unique sibling has no indexed alias", () =>
                    Require(lookup(root.transform, "Bolt[0]") == null && lookup(root.transform, "Bolt") == first.transform));
                first.transform.SetParent(second.transform, false);
                check("relative lookup: reparenting invalidates the old mount", () =>
                    Require(lookup(root.transform, "Bolt") == null && lookup(root.transform, "Renamed/Bolt") == first.transform));
                foreach (int width in new[] { 32, 256 })
                {
                    var fixture = Child(root, "Width" + width);
                    var parent = fixture;
                    for (int depth = 0; depth < 3; depth++)
                    {
                        for (int i = 0; i < width; i++) Child(parent, "Unrelated" + i);
                        parent = Child(parent, "Mount");
                    }
                    check("relative lookup: nested mount resolves at width " + width, () =>
                        Require(lookup(fixture.transform, "Mount/Mount/Mount") == parent.transform));
                    var transform = fixture.transform;
                    Measure(rows, width, "relative-mount-lookup-1000", () =>
                    { for (int i = 0; i < 1000; i++) lookup(transform, "Mount/Mount/Mount"); });
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
