using System;
using System.Collections.Generic;

namespace WinterMP.Net.Sync
{
    /// <summary>
    /// Path index for one synchronous hierarchy scan. Recreate for each scan so
    /// reparenting, renaming and duplicate sibling changes cannot stale network IDs.
    /// </summary>
    public sealed class ScenePathCache<T> where T : class
    {
        private readonly Func<T, T?> _parent;
        private readonly Func<T, string> _name;
        private readonly Func<T, int> _childCount;
        private readonly Func<T, int, T> _child;
        private readonly Dictionary<T, string> _paths = new Dictionary<T, string>();
        private readonly Dictionary<T, string> _segments = new Dictionary<T, string>();

        public ScenePathCache(Func<T, T?> parent, Func<T, string> name,
            Func<T, int> childCount, Func<T, int, T> child)
        {
            _parent = parent;
            _name = name;
            _childCount = childCount;
            _child = child;
        }

        public string Of(T node)
        {
            if (_paths.TryGetValue(node, out var path)) return path;
            var parent = _parent(node);
            if (parent == null) path = _name(node);
            else
            {
                if (!_segments.TryGetValue(node, out var segment))
                {
                    IndexChildren(parent);
                    segment = _segments[node];
                }
                path = Of(parent) + "/" + segment;
            }
            _paths[node] = path;
            return path;
        }

        public string? RelativeTo(T node, T root)
        {
            var segments = new List<string>();
            for (T? current = node; current != null; current = _parent(current))
            {
                if (ReferenceEquals(current, root))
                {
                    segments.Reverse();
                    return string.Join("/", segments.ToArray());
                }
                var parent = _parent(current);
                if (parent == null) return null;
                if (!_segments.TryGetValue(current, out var segment))
                { IndexChildren(parent); segment = _segments[current]; }
                segments.Add(segment);
            }
            return null;
        }

        public T? FindRelative(T root, string path)
        {
            if (!PartAttachmentPolicy.ValidPath(path)) return null;
            if (path.Length == 0) return root;
            var current = root;
            foreach (string segment in path.Split('/'))
            {
                IndexChildren(current);
                T? found = null;
                for (int i = 0; i < _childCount(current); i++)
                {
                    var child = _child(current, i);
                    if (_segments[child] != segment) continue;
                    if (found != null) return null; // Literal bracket names can collide with indexed sibling names.
                    found = child;
                }
                if (found == null) return null;
                current = found;
            }
            return current;
        }

        private void IndexChildren(T parent)
        {
            // Enumerate siblings once for the whole group, rather than once per
            // child, ancestor, catalog rule and FSM sharing the same transform.
            int count = _childCount(parent);
            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            var first = new Dictionary<string, T>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                var child = _child(parent, i);
                string name = _name(child);
                if (!totals.TryGetValue(name, out int ordinal))
                {
                    first[name] = child;
                    _segments[child] = name;
                }
                else
                {
                    if (ordinal == 1) _segments[first[name]] = name + "[0]";
                    _segments[child] = name + "[" + ordinal + "]";
                }
                totals[name] = ordinal + 1;
            }
        }
    }
}
