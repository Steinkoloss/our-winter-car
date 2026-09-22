using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static EngineMountedPartData ParseCarburettor(Dictionary<string, object?> block, GuestEngineProtectionData protection)
        {
            if (!block.TryGetValue("carburettor", out var value) || value is not Dictionary<string, object?> fields)
                throw new FormatException("Missing carburettor engine input.");
            var data = new EngineMountedPartData { MountPath = EngineInputPath(fields, "mountPath"), RootPrefix = EngineInputName(fields, "rootPrefix"),
                RelativePath = EngineInputName(fields, "relativePath"), PartPrefix = EngineInputName(fields, "partPrefix"),
                Fsm = EngineInputName(fields, "fsm"), ReadyState = EngineInputName(fields, "readyState") };
            if (data.MountPath != "CARPARTS/StartParts/VIN1110/VINP_Carburettor" || data.RootPrefix != "VIN111"
                || data.RelativePath != "VINP_Carburettor" || data.PartPrefix != "VIN113" || data.Fsm != "Data" || data.ReadyState != "Update 2")
                throw new FormatException("Unsupported carburettor engine source.");
            if (!fields.TryGetValue("alternatePartPrefixes", out var alternatives) || alternatives is not List<object?> prefixes
                || prefixes.Count != 2 || prefixes[0] as string != "CARB2BRLa0" || prefixes[1] as string != "CARB4BRLa0")
                throw new FormatException("Unsupported carburettor variants.");
            data.AlternatePartPrefixes = new[] { "CARB2BRLa0", "CARB4BRLa0" };
            GuestEnginePausedFsmData? paused = null;
            foreach (var rule in protection.PausedFsms) if (rule.Path == data.MountPath && rule.Fsm == data.Fsm) paused = rule;
            if (paused == null || paused.RootPrefix != data.RootPrefix || paused.RelativePath != data.RelativePath)
                throw new FormatException("Carburettor protection must follow its native head.");
            foreach (string state in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near",
                "Remove other", "Update 2", "Init 2", "Load 2", "Save 2" })
                if (Array.IndexOf(paused.RequiredStates, state) < 0) throw new FormatException("Incomplete carburettor protection.");
            return data;
        }
        private static void BindCarburettorSource(Dictionary<string, object?> fields, GuestEngineInputData entry, EngineMountedPartData data)
        {
            if (entry.FamilyPrefix != "Carburettor" || entry.MountPath != data.MountPath || entry.InputFsm != data.Fsm
                || entry.ReaderPath != (entry.Fsm == "Cooling" ? "CORRIS/Simulation/Systems/Cooling" : "CORRIS/Simulation/Engine/" + (entry.Fsm == "Valves" ? "Valves" : "Fuel"))
                || (entry.Fsm != "FuelLine" && entry.Fsm != "Mixture" && entry.Fsm != "Valves" && entry.Fsm != "Cooling")
                || entry.TargetVariable != (entry.Fsm == "FuelLine" ? "db_Carb1" : "db_Carburettor") || entry.DirectTarget
                || fields.ContainsKey("alternateFamilies") || fields.ContainsKey("slotIndex") || fields.ContainsKey("mountVariable"))
                throw new FormatException("Carburettor inputs must use the protected native head mount.");
            entry.CarburettorSource = data;
        }
        private static void ValidateCarburettorReader(GuestEngineInputData entry, GuestEngineInputReaderData reader)
        {
            if (entry.Fsm == "Cooling")
            {
                if (reader.State != "Hoses" || reader.ActionIndex != 4 || reader.Variable != "Tightness" || reader.Output != "Tightness5"
                    || reader.EveryFrame || reader.ActionType != "GetFsmFloat") throw new FormatException("Unsupported carburettor coolant read.");
                return;
            }
            if (entry.Fsm == "Valves") { ValidateIntakePerformanceReader(reader, "Carburettor"); return; }
            string variable = entry.Fsm == "Mixture" ? "SettingMixture" : reader.ActionIndex == 0 ? "Installed"
                : reader.ActionIndex == 2 ? "FuelChamber" : reader.ActionIndex == 4 ? "CarbReserve" : "";
            string output = entry.Fsm == "Mixture" ? "CarbSetting" : reader.ActionIndex == 0 ? "Installed1" : variable;
            if (reader.EveryFrame || reader.State != (entry.Fsm == "Mixture" ? "Calculate density" : "Carburator")
                || entry.Fsm == "Mixture" && reader.ActionIndex != 0 || variable.Length == 0 || reader.Variable != variable || reader.Output != output)
                throw new FormatException("Unsupported native carburettor read.");
        }
    }
}
