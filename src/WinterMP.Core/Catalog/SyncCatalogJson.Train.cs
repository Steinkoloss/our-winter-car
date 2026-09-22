using System;
using System.Collections.Generic;
namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static TrainData ParseTrain(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid train catalog.");
            var data = new TrainData();
            foreach (string key in new[] { "root", "east", "west", "targetEast", "targetWest", "body", "mesh", "lights", "lightFsmPath", "lightFsm", "sound", "horn", "raycastPath", "raycastFsm", "move", "reset", "player", "tunnel", "whistle", "westbound", "westWait", "eastbound", "eastWait", "hornState", "idle", "die" })
                data.Names.Add(key, EngineInputPath(fields, key));
            if (!fields.TryGetValue("colliders", out var raw) || raw is not List<object?> list || list.Count != 11) throw new FormatException("Expected eleven native train colliders.");
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in list)
            {
                if (entry is not string path || string.IsNullOrEmpty(path) || path.StartsWith("/", StringComparison.Ordinal) || path.IndexOf("..", StringComparison.Ordinal) >= 0 || !found.Add(path))
                    throw new FormatException("Invalid train collider path.");
                data.Colliders.Add(path);
            }
            return data;
        }
    }
    internal sealed class TrainData
    {
        internal readonly Dictionary<string,string> Names = new Dictionary<string,string>(StringComparer.Ordinal);
        internal readonly List<string> Colliders = new List<string>();
        internal string this[string key] => Names[key];
    }
}
