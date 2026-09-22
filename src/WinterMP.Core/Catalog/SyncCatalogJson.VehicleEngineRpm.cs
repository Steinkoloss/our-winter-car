using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static VehicleEngineRpmData ParseVehicleEngineRpm(object? value)
        {
            if (value is not Dictionary<string, object?> obj)
                throw new FormatException("Invalid vehicle engine RPM profile.");
            var data = new VehicleEngineRpmData();
            var roots = new HashSet<string>();
            var producers = new HashSet<string>();
            var globals = new HashSet<string>();
            foreach (var entry in RpmEntries(obj, "sources", 16))
            {
                var source = new VehicleEngineRpmSourceData {
                    RootPath = RpmPath(entry, "rootPath"), ProducerPath = RpmPath(entry, "producerPath"),
                    Fsm = RpmName(entry, "fsm"), ObjectVariable = RpmName(entry, "objectVariable"),
                    ComponentType = RpmName(entry, "componentType"), RpmMember = RpmName(entry, "rpmMember"),
                    GlobalVariable = RpmName(entry, "globalVariable") };
                if (!source.ProducerPath.StartsWith(source.RootPath + "/", StringComparison.Ordinal)
                    || !roots.Add(source.RootPath) || !producers.Add(source.ProducerPath + "::" + source.Fsm)
                    || !globals.Add(source.GlobalVariable))
                    throw new FormatException("Ambiguous vehicle engine RPM source.");
                var actions = new HashSet<string>();
                bool hasProperty = false;
                foreach (var action in RpmEntries(entry, "producers", 64))
                {
                    var binding = new VehicleEngineRpmProducerData {
                        State = RpmName(action, "state"),
                        Index = SlotNumber(action.TryGetValue("index", out var index) ? index : null, "RPM action index", 0, 255),
                        ActionType = RpmName(action, "actionType") };
                    if (!action.TryGetValue("everyFrame", out var everyFrame) || everyFrame is not bool frame)
                        throw new FormatException("Missing vehicle engine RPM action timing.");
                    binding.EveryFrame = frame;
                    bool variable = action.ContainsKey("sourceVariable"), constant = action.ContainsKey("sourceConstant");
                    if (binding.ActionType == "GetProperty")
                    {
                        if (variable || constant) throw new FormatException("RPM property source cannot have a float value binding.");
                        hasProperty = true;
                    }
                    else if (binding.ActionType == "SetFloatValue")
                    {
                        if (variable == constant) throw new FormatException("RPM float source requires exactly one value binding.");
                        if (variable) binding.SourceVariable = RpmName(action, "sourceVariable");
                        else
                        {
                            object? number = action["sourceConstant"];
                            if (number is not double && number is not long)
                                throw new FormatException("Invalid RPM source constant.");
                            float scalar = number is double d ? (float)d : (long)number;
                            if (float.IsNaN(scalar) || float.IsInfinity(scalar))
                                throw new FormatException("Invalid RPM source constant.");
                            binding.SourceConstant = scalar;
                        }
                    }
                    else throw new FormatException("Unsupported vehicle engine RPM producer type.");
                    if (!actions.Add(binding.State + "::" + binding.Index))
                        throw new FormatException("Duplicate vehicle engine RPM producer action.");
                    source.Producers.Add(binding);
                }
                if (!hasProperty) throw new FormatException("Vehicle engine RPM source requires a component property producer.");
                data.Sources.Add(source);
            }
            return data;
        }

        private static IEnumerable<Dictionary<string, object?>> RpmEntries(Dictionary<string, object?> obj, string key, int max)
        {
            if (!obj.TryGetValue(key, out var value) || value is not List<object?> list || list.Count < 1 || list.Count > max)
                throw new FormatException("Invalid vehicle engine RPM " + key + ".");
            foreach (var entry in list)
            {
                if (entry is not Dictionary<string, object?> fields)
                    throw new FormatException("Invalid vehicle engine RPM " + key + " entry.");
                yield return fields;
            }
        }

        private static string RpmPath(Dictionary<string, object?> obj, string key)
        {
            string path = RequiredString(obj, key);
            if (path.Length > 512 || !ValidScenePath(path) || path.IndexOf(':') >= 0)
                throw new FormatException("Invalid vehicle engine RPM " + key + ".");
            foreach (string part in path.Split('/'))
                if (part == "." || part.Trim().Length == 0)
                    throw new FormatException("Invalid vehicle engine RPM path segment.");
            foreach (char ch in path)
                if (char.IsControl(ch)) throw new FormatException("Invalid vehicle engine RPM path.");
            return path;
        }

        private static string RpmName(Dictionary<string, object?> obj, string key)
        {
            string name = RequiredString(obj, key);
            if (name.Trim().Length == 0 || name.Length > 128 || name.IndexOfAny(new[] { ':', '/', '\\' }) >= 0)
                throw new FormatException("Invalid vehicle engine RPM " + key + ".");
            foreach (char ch in name)
                if (char.IsControl(ch)) throw new FormatException("Invalid vehicle engine RPM " + key + ".");
            return name;
        }
    }

    internal sealed class VehicleEngineRpmData
    {
        public readonly List<VehicleEngineRpmSourceData> Sources = new List<VehicleEngineRpmSourceData>();
    }

    internal sealed class VehicleEngineRpmSourceData
    {
        public string RootPath = "", ProducerPath = "", Fsm = "", ObjectVariable = "", ComponentType = "", RpmMember = "", GlobalVariable = "";
        public readonly List<VehicleEngineRpmProducerData> Producers = new List<VehicleEngineRpmProducerData>();
    }

    internal sealed class VehicleEngineRpmProducerData
    {
        public string State = "", ActionType = "";
        public int Index;
        public bool EveryFrame;
        public string? SourceVariable;
        public float? SourceConstant;
    }
}
