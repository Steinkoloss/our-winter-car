using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static StoveData ParseStoves(object? value)
        {
            if (value is not Dictionary<string, object?> obj) throw new FormatException("Invalid stove catalog.");
            var data = new StoveData();
            foreach (string key in new[] { "simulation", "simFsm", "knobPrefix", "knobFsm", "rotation", "data", "mesh", "step",
                "up", "down", "read", "resetUp", "resetDown", "platePrefix", "heatSuffix", "grill", "burn", "idle", "light",
                "smoke", "smokeOff", "smokeProperty", "hazardSuffix" })
                data.Names.Add(key, RpmName(obj, key));
            if (!obj.TryGetValue("paths", out var paths) || paths is not List<object?> list || list.Count == 0 || list.Count > 8)
                throw new FormatException("Missing stove paths.");
            foreach (var path in list)
            {
                var field = new Dictionary<string, object?> { { "path", path } }; string text = RpmPath(field, "path");
                if (data.Paths.Contains(text)) throw new FormatException("Duplicate stove path.");
                data.Paths.Add(text);
            }
            if (!obj.TryGetValue("ignitionEnabled", out var enabled) || enabled is not List<object?> flags || flags.Count != data.Paths.Count)
                throw new FormatException("Missing native stove ignition settings.");
            foreach (var flag in flags)
            {
                if (flag is not bool active) throw new FormatException("Invalid native stove ignition setting.");
                data.IgnitionEnabled.Add(active);
            }
            return data;
        }
    }
    internal sealed class StoveData
    {
        public readonly List<string> Paths = new List<string>();
        public readonly List<bool> IgnitionEnabled = new List<bool>();
        public readonly Dictionary<string, string> Names = new Dictionary<string, string>();
        public string this[string key] => Names[key];
    }
}
