using System;
using System.Globalization;

namespace WinterMP.Net.Sync
{
    public static class ScenePathLookup
    {
        /// <summary>Conservative rejection only: a possible leaf still needs full path validation.</summary>
        public static bool MayMatchLeafName(string path, string name)
        {
            if (path.EndsWith(name, StringComparison.Ordinal)) return true;
            int end = path.Length - 1;
            if (end < 1 || path[end] != ']') return false;
            int bracket = end - 1;
            while (bracket >= 0 && path[bracket] >= '0' && path[bracket] <= '9') bracket--;
            // Duplicate ordinals append to the entire literal name, including
            // names which themselves contain brackets or path separators.
            return bracket >= name.Length && bracket < end - 1 && path[bracket] == '['
                && string.CompareOrdinal(path, bracket - name.Length, name, 0, name.Length) == 0;
        }
    }

    /// <summary>Resolves one current relative path without retaining a hierarchy index.</summary>
    public sealed class ScenePathLookup<T> where T : class
    {
        private readonly Func<T, string> _name;
        private readonly Func<T, int> _childCount;
        private readonly Func<T, int, T> _child;

        public ScenePathLookup(Func<T, string> name, Func<T, int> childCount, Func<T, int, T> child)
        { _name = name; _childCount = childCount; _child = child; }

        public T? FindRelative(T root, string path)
        {
            if (!PartAttachmentPolicy.ValidPath(path)) return null;
            if (path.Length == 0) return root;
            var current = root;
            foreach (string segment in path.Split('/'))
            {
                var next = FindChild(current, segment);
                if (next == null) return null;
                current = next;
            }
            return current;
        }

        private T? FindChild(T parent, string segment)
        {
            string? indexedName = null;
            int ordinal = -1;
            int bracket = segment.LastIndexOf('[');
            if (bracket >= 0 && segment[segment.Length - 1] == ']')
            {
                string number = segment.Substring(bracket + 1, segment.Length - bracket - 2);
                // Only the exact suffix emitted by the sender is an index. A
                // leading zero, sign or malformed bracket can still be a literal name.
                if (int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out ordinal)
                    && number == ordinal.ToString()) indexedName = segment.Substring(0, bracket);
            }

            T? literal = null, indexed = null;
            int literalCount = 0, indexedCount = 0;
            int count = _childCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = _child(parent, i);
                string name = _name(child);
                if (name == segment) { literal = child; literalCount++; }
                if (indexedName != null && name == indexedName)
                {
                    if (indexedCount == ordinal) indexed = child;
                    indexedCount++;
                }
            }

            // A unique name has no [0] alias. Literal bracket names can collide
            // with duplicate ordinals; those mounts must remain unresolved.
            if (literalCount != 1) literal = null;
            if (indexedCount < 2) indexed = null;
            return literal != null && indexed != null ? null : literal ?? indexed;
        }
    }
}
