using System;
using System.Collections.Generic;
namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static SausagesData ParseSausages(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid sausage catalog.");
            var data = new SausagesData();
            foreach (string key in new[] { "fsm", "package", "prefab", "prefabName", "packageName", "packagePrefix", "packageId", "open", "idle", "use", "fire", "condition", "grilled", "owner", "wait", "button", "eat", "freshEat", "grilledEat", "destroy", "raw", "cooked", "charred", "spoiled", "freshMesh", "grilledMesh" })
                data.Names.Add(key, EngineInputPath(fields, key));
            if (!fields.TryGetValue("paths", out var paths) || paths is not List<object?> list || list.Count == 0)
                throw new FormatException("Missing sausage conversion paths.");
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in list)
            {
                if (path is not string text || string.IsNullOrEmpty(text) || !found.Add(text)) throw new FormatException("Invalid sausage conversion path.");
                data.Paths.Add(text);
            }
            return data;
        }
    }
    internal sealed class SausagesData
    {
        internal readonly List<string> Paths = new List<string>();
        internal readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);
        internal string this[string key] => Names[key];
    }
}
