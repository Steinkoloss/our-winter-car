using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static VehicleWheelHealthData ParseVehicleWheelHealth(object? value)
        {
            if (value is not Dictionary<string, object?> obj) throw new FormatException("Invalid wheel health sources.");
            var data = new VehicleWheelHealthData { RootPath = TemperaturePath(obj, "rootPath") };
            var paths = new HashSet<string>(); var targets = new HashSet<string>(); var wheels = new HashSet<int>();
            foreach (var entry in EngineProtectionEntries(obj, "sources", 4, 4))
            {
                var source = new VehicleWheelHealthSourceData { Path = TemperaturePath(entry, "path"), TargetPath = TemperaturePath(entry, "targetPath"),
                    Wheel = SlotNumber(entry.TryGetValue("wheel", out var wheel) ? wheel : null, "health source wheel", 0, 3) };
                if (!source.Path.StartsWith(data.RootPath + "/", StringComparison.Ordinal)
                    || !source.TargetPath.StartsWith(source.Path + "/", StringComparison.Ordinal)
                    || !paths.Add(source.Path) || !targets.Add(source.TargetPath) || !wheels.Add(source.Wheel))
                    throw new FormatException("Ambiguous wheel health source.");
                data.Sources.Add(source);
            }
            return data;
        }
    }
    internal sealed class VehicleWheelHealthData
    {
        public string RootPath = "";
        public readonly List<VehicleWheelHealthSourceData> Sources = new List<VehicleWheelHealthSourceData>();
    }
    internal sealed class VehicleWheelHealthSourceData
    {
        public int Wheel;
        public string Path = "", TargetPath = "";
    }
}
