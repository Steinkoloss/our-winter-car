using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static VehicleEngineHandoffData ParseVehicleEngineHandoff(object? value)
        {
            if (value is not Dictionary<string, object?> obj) throw new FormatException("Invalid engine handoff profile.");
            var data = new VehicleEngineHandoffData(); var roots = new HashSet<string>();
            foreach (var entry in RpmEntries(obj, "sources", 16))
            {
                var source = new VehicleEngineHandoffSource {
                    RootPath = RpmPath(entry, "rootPath"), StarterPath = RpmPath(entry, "starterPath"),
                    IgnitionPath = RpmPath(entry, "ignitionPath") };
                if (!roots.Add(source.RootPath) || !source.StarterPath.StartsWith(source.RootPath + "/", StringComparison.Ordinal)
                    || !source.IgnitionPath.StartsWith(source.RootPath + "/", StringComparison.Ordinal))
                    throw new FormatException("Engine handoff paths must identify one vehicle.");
                data.Sources.Add(source);
            }
            return data;
        }
    }
    internal sealed class VehicleEngineHandoffData
    {
        public readonly List<VehicleEngineHandoffSource> Sources = new List<VehicleEngineHandoffSource>();
    }
    internal sealed class VehicleEngineHandoffSource
    {
        public string RootPath = "", StarterPath = "", IgnitionPath = "";
    }
}
