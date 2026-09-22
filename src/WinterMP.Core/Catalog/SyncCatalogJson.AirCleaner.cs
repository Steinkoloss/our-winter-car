using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static EngineMountedPartData ParseAirCleaner(Dictionary<string, object?> block, GuestEngineProtectionData protection)
        {
            if (!block.TryGetValue("airCleaner", out var value) || value is not Dictionary<string, object?> fields)
                throw new FormatException("Missing air-cleaner engine input.");
            var data = new EngineMountedPartData { MountPath = EngineInputPath(fields, "mountPath"), RootPrefix = EngineInputName(fields, "rootPrefix"),
                RelativePath = EngineInputName(fields, "relativePath"), PartPrefix = EngineInputName(fields, "partPrefix"),
                Fsm = EngineInputName(fields, "fsm"), ReadyState = EngineInputName(fields, "readyState") };
            if (data.MountPath != "CARPARTS/StartParts/VIN1110/VINP_AirCleaner" || data.RootPrefix != "VIN111"
                || data.RelativePath != "VINP_AirCleaner" || data.PartPrefix != "VIN135" || data.Fsm != "Data" || data.ReadyState != "Update 2"
                || fields.ContainsKey("alternatePartPrefixes")) throw new FormatException("Unsupported air-cleaner source.");
            GuestEnginePausedFsmData? paused = null;
            foreach (var rule in protection.PausedFsms) if (rule.Path == data.MountPath && rule.Fsm == data.Fsm) paused = rule;
            if (paused == null || paused.RootPrefix != data.RootPrefix || paused.RelativePath != data.RelativePath)
                throw new FormatException("Air-cleaner protection must follow its native head.");
            foreach (string state in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", "Update 2" })
                if (Array.IndexOf(paused.RequiredStates, state) < 0) throw new FormatException("Incomplete air-cleaner protection.");
            return data;
        }
        private static void BindAirCleanerSource(Dictionary<string, object?> fields, GuestEngineInputData entry, EngineMountedPartData data)
        {
            if (entry.FamilyPrefix != "AirCleaner" || entry.MountPath != data.MountPath || entry.InputFsm != data.Fsm
                || entry.ReaderPath != "CORRIS/Simulation/Engine/" + (entry.Fsm == "Valves" ? "Valves" : "Fuel")
                || (entry.Fsm != "FuelLine" && entry.Fsm != "Valves")
                || entry.TargetVariable != (entry.Fsm == "FuelLine" ? "db_Airfilter" : "db_AirCleaner") || entry.DirectTarget
                || fields.ContainsKey("alternateFamilies") || fields.ContainsKey("slotIndex") || fields.ContainsKey("mountVariable"))
                throw new FormatException("Air-cleaner inputs must use the protected native head mount.");
            entry.AirCleanerSource = data;
        }
        private static void ValidateAirCleanerReader(GuestEngineInputData entry, GuestEngineInputReaderData reader)
        {
            if (entry.Fsm == "Valves") { ValidateIntakePerformanceReader(reader, "AirFilter"); return; }
            if (reader.EveryFrame || reader.State != "Airfilter" || reader.ActionIndex != 1 || reader.Variable != "Installed" || reader.Output != "Installed1")
                throw new FormatException("Unsupported native air-cleaner read.");
        }
        private static void ValidateIntakePerformanceReader(GuestEngineInputReaderData reader, string state)
        {
            string variable = reader.ActionIndex == 0 ? "DataPower" : reader.ActionIndex == 1 ? "DataTorque" : reader.ActionIndex == 2 ? "DataPowerAdd" : "";
            string output = reader.ActionIndex == 0 ? "PartPower" : reader.ActionIndex == 1 ? "PartTorque" : "PartPowerAdd";
            if (reader.EveryFrame || reader.State != state || variable.Length == 0 || reader.Variable != variable || reader.Output != output)
                throw new FormatException("Unsupported native intake performance read.");
        }
    }
}
