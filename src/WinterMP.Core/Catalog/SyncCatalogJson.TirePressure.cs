using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static VehicleTirePressureData ParseVehicleTirePressure(object? value)
        {
            if (value is not Dictionary<string, object?> obj) throw new FormatException("Invalid tyre pressure profile.");
            var data = new VehicleTirePressureData {
                RootPath = TemperaturePath(obj, "rootPath"), Path = TemperaturePath(obj, "path"),
                Fsm = TemperatureName(obj, "fsm"), State = TemperatureName(obj, "state"), Event = TemperatureName(obj, "event"),
                Pressure = TemperatureName(obj, "pressure"), Optimum = TemperatureName(obj, "optimum") };
            if (!data.Path.StartsWith(data.RootPath + "/", StringComparison.Ordinal) || data.Pressure == data.Optimum)
                throw new FormatException("Ambiguous tyre pressure source.");
            var paths = new HashSet<string>(); var names = new HashSet<string>(); var indices = new HashSet<int>();
            foreach (var entry in EngineProtectionEntries(obj, "wheels", 4, 4))
            {
                var wheel = new VehicleTirePressureWheelData { Path = TemperaturePath(entry, "path"),
                    ObjectVariable = TemperatureName(entry, "objectVariable"),
                    PressureIndex = SlotNumber(entry.TryGetValue("pressureIndex", out var p) ? p : null, "pressure action", 0, 7),
                    OptimumIndex = SlotNumber(entry.TryGetValue("optimumIndex", out var o) ? o : null, "optimum action", 0, 7) };
                if (!entry.TryGetValue("enabled", out var enabled) || enabled is not bool nativeEnabled)
                    throw new FormatException("Native tyre pressure action enablement is required.");
                wheel.Enabled = nativeEnabled;
                if (!wheel.Path.StartsWith(data.RootPath + "/", StringComparison.Ordinal) || !paths.Add(wheel.Path)
                    || !names.Add(wheel.ObjectVariable) || !indices.Add(wheel.PressureIndex) || !indices.Add(wheel.OptimumIndex))
                    throw new FormatException("Ambiguous tyre pressure wheel binding.");
                data.Wheels.Add(wheel);
            }
            return data;
        }
    }

    internal sealed class VehicleTirePressureData
    {
        public string RootPath = "", Path = "", Fsm = "", State = "", Event = "", Pressure = "", Optimum = "";
        public readonly List<VehicleTirePressureWheelData> Wheels = new List<VehicleTirePressureWheelData>();
    }
    internal sealed class VehicleTirePressureWheelData
    {
        public string Path = "", ObjectVariable = "";
        public int PressureIndex, OptimumIndex;
        public bool Enabled;
    }
}
