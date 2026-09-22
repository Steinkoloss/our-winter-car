using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static EngineMountedPartData ParseRadiator(Dictionary<string, object?> block, GuestEngineProtectionData protection)
        {
            if (!block.TryGetValue("radiator", out var value) || value is not Dictionary<string, object?> fields)
                throw new FormatException("Missing radiator engine input.");
            if (!fields.TryGetValue("rootPrefix", out var root) || root is not string rootPrefix)
                throw new FormatException("Missing radiator mount root binding.");
            var data = new EngineMountedPartData { MountPath = EngineInputPath(fields, "mountPath"), RootPrefix = rootPrefix,
                RelativePath = EngineInputName(fields, "relativePath"), PartPrefix = EngineInputName(fields, "partPrefix"),
                Fsm = EngineInputName(fields, "fsm"), ReadyState = EngineInputName(fields, "readyState"),
                AlternatePartPrefixes = ReplacementStrings(fields, "alternatePartPrefixes", 2, 2) };
            if (data.MountPath != "CORRIS/Assemblies/VINP_Radiator" || data.RootPrefix != ""
                || data.RelativePath != "VINP_Radiator" || data.PartPrefix != "VIN201" || data.Fsm != "Data" || data.ReadyState != "Update 2"
                || data.AlternatePartPrefixes[0] != "RADIATORa0" || data.AlternatePartPrefixes[1] != "RADIATORb0")
                throw new FormatException("Unsupported radiator source.");
            GuestEnginePausedFsmData? paused = null;
            foreach (var rule in protection.PausedFsms) if (rule.Path == data.MountPath && rule.Fsm == data.Fsm) paused = rule;
            if (paused == null || paused.RootPrefix != "" || paused.RelativePath != "")
                throw new FormatException("Radiator protection must use its fixed native mount.");
            foreach (string state in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Remove other", "Allow removal?", "Allow install?", "Far", "Near", "Update 2" })
                if (Array.IndexOf(paused.RequiredStates, state) < 0) throw new FormatException("Incomplete radiator protection.");
            return data;
        }
        private static void BindRadiatorSource(Dictionary<string, object?> fields, GuestEngineInputData entry, EngineMountedPartData data)
        {
            if (entry.FamilyPrefix != "Radiator" || entry.MountPath != data.MountPath || entry.InputFsm != data.Fsm
                || entry.ReaderPath != "CORRIS/Simulation/Systems/Cooling" || entry.Fsm != "Cooling" || entry.TargetVariable != "db_Radiator" || entry.DirectTarget
                || fields.ContainsKey("alternateFamilies") || fields.ContainsKey("slotIndex") || fields.ContainsKey("mountVariable"))
                throw new FormatException("Radiator inputs must use the protected native cooling mount.");
            entry.RadiatorSource = data;
        }
        private static void ValidateRadiatorReader(GuestEngineInputData entry, GuestEngineInputReaderData reader)
        {
            string state = "Radiator Data", output = reader.Variable; int index = 0;
            switch (reader.Variable)
            {
                case "Installed": state = "Radiator installed?"; output = "Installed1"; break;
                case "Coolant": output = "WaterLevel"; break;
                case "PressureCap": index = 1; break;
                case "Wear": index = 3; break;
                case "FlectEfficiency": state = "Flect"; index = 1; output = "FlectEff"; break;
                default: throw new FormatException("Unsupported radiator field.");
            }
            if (reader.EveryFrame || reader.State != state || reader.ActionIndex != index || reader.Output != output
                || reader.ActionType != (reader.Variable == "Installed" ? "GetFsmBool" : "GetFsmFloat"))
                throw new FormatException("Unsupported native radiator read.");
        }
    }
}
