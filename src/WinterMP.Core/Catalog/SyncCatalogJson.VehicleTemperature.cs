using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static VehicleTemperatureData ParseVehicleTemperature(object? value)
        {
            if (value is not Dictionary<string, object?> obj || !obj.TryGetValue("sources", out var entries)
                || entries is not List<object?> list || list.Count < 1 || list.Count > 16)
                throw new FormatException("Invalid vehicle temperature sources.");
            var data = new VehicleTemperatureData();
            var roots = new HashSet<string>();
            foreach (var entry in list)
            {
                if (entry is not Dictionary<string, object?> fields)
                    throw new FormatException("Invalid vehicle temperature source.");
                var source = new VehicleTemperatureSourceData {
                    RootPath = TemperaturePath(fields, "rootPath"), GaugePath = TemperaturePath(fields, "gaugePath"),
                    SourcePath = TemperaturePath(fields, "sourcePath"), SourceFsm = TemperatureName(fields, "sourceFsm"),
                    SourceVariable = TemperatureName(fields, "sourceVariable"), GaugeVariable = TemperatureName(fields, "gaugeVariable") };
                if (!fields.TryGetValue("hostAuthoritative", out var authority) || authority is not bool host)
                    throw new FormatException("Vehicle temperature authority is required.");
                source.HostAuthoritative = host;
                if (host)
                {
                    source.ReadyState = TemperatureName(fields, "readyState");
                    source.EngineGlobal = TemperatureName(fields, "engineGlobal");
                    if (source.EngineGlobal == source.SourceVariable || !fields.TryGetValue("engineInputs", out var inputs))
                        throw new FormatException("Engine temperature inputs are required and must have a separate source.");
                    source.EngineInputs = ParseEngineTemperatureInputs(inputs, source.RootPath);
                    if (!fields.TryGetValue("cabinInputs", out var cabin)) throw new FormatException("Cabin temperature inputs are required.");
                    source.CabinInputs = ParseCabinTemperatureInputs(cabin, source);
                    if (!fields.TryGetValue("electricalInputs", out var electrical)) throw new FormatException("Electrical temperature inputs are required.");
                    source.ElectricalInputs = ParseElectricalTemperatureInputs(electrical, source);
                }
                else if (fields.ContainsKey("readyState")) throw new FormatException("Driver temperature cannot declare a host readiness state.");
                if (!host && (fields.ContainsKey("engineGlobal") || fields.ContainsKey("engineInputs") || fields.ContainsKey("cabinInputs") || fields.ContainsKey("electricalInputs")))
                    throw new FormatException("Driver temperature cannot declare host engine inputs.");
                if (!roots.Add(source.RootPath) || !source.GaugePath.StartsWith(source.RootPath + "/", StringComparison.Ordinal)
                    || !source.SourcePath.StartsWith(source.RootPath + "/", StringComparison.Ordinal) || source.GaugePath == source.SourcePath)
                    throw new FormatException("Ambiguous vehicle temperature paths.");
                data.Sources.Add(source);
            }
            return data;
        }

        private static string TemperaturePath(Dictionary<string, object?> fields, string key)
        {
            string value = RequiredString(fields, key);
            if (value.Length > 512 || !ValidScenePath(value) || value.IndexOf(':') >= 0)
                throw new FormatException("Invalid vehicle temperature path.");
            foreach (string segment in value.Split('/'))
                if (segment == "." || segment.Trim() != segment || segment.Length == 0)
                    throw new FormatException("Invalid vehicle temperature path segment.");
            foreach (char ch in value) if (char.IsControl(ch)) throw new FormatException("Invalid vehicle temperature path.");
            return value;
        }

        private static string TemperatureName(Dictionary<string, object?> fields, string key)
        {
            string value = RequiredString(fields, key);
            if (value.Length > 128 || value.Trim() != value || value.Length == 0 || value.IndexOfAny(new[] { ':', '/', '\\' }) >= 0)
                throw new FormatException("Invalid vehicle temperature variable or FSM name.");
            foreach (char ch in value) if (char.IsControl(ch)) throw new FormatException("Invalid vehicle temperature name.");
            return value;
        }
    }

    internal sealed class VehicleTemperatureData
    {
        public readonly List<VehicleTemperatureSourceData> Sources = new List<VehicleTemperatureSourceData>();
    }

    internal sealed class VehicleTemperatureSourceData
    {
        public string RootPath = "", GaugePath = "", SourcePath = "", SourceFsm = "", SourceVariable = "", GaugeVariable = "";
        public bool HostAuthoritative;
        public string ReadyState = "";
        public string EngineGlobal = "";
        public EngineTemperatureInputData? EngineInputs;
        public CabinTemperatureInputData? CabinInputs;
        public ElectricalTemperatureInputData? ElectricalInputs;
    }
}
