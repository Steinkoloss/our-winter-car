using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static EngineMountedPartData ParseOilpan(Dictionary<string, object?> block, GuestEngineProtectionData protection)
        {
            if (!block.TryGetValue("oilpan", out var value) || value is not Dictionary<string, object?> fields)
                throw new FormatException("Missing oilpan engine input.");
            var data = new EngineMountedPartData { MountPath = EngineInputPath(fields, "mountPath"), RootPrefix = EngineInputName(fields, "rootPrefix"),
                RelativePath = EngineInputName(fields, "relativePath"), PartPrefix = EngineInputName(fields, "partPrefix"),
                Fsm = EngineInputName(fields, "fsm"), ReadyState = EngineInputName(fields, "readyState") };
            if (data.MountPath != "CARPARTS/StartParts/VIN1010/VINP_Oilpan" || data.RootPrefix != "VIN101"
                || data.RelativePath != "VINP_Oilpan" || data.PartPrefix != "VIN106" || data.Fsm != "Data" || data.ReadyState != "Update 2"
                || fields.ContainsKey("alternatePartPrefixes")) throw new FormatException("Unsupported oilpan source.");
            GuestEnginePausedFsmData? paused = null;
            foreach (var rule in protection.PausedFsms) if (rule.Path == data.MountPath && rule.Fsm == data.Fsm) paused = rule;
            if (paused == null || paused.RootPrefix != data.RootPrefix || paused.RelativePath != data.RelativePath)
                throw new FormatException("Oilpan protection must follow its native block.");
            foreach (string state in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", "Update 2" })
                if (Array.IndexOf(paused.RequiredStates, state) < 0) throw new FormatException("Incomplete oilpan protection.");
            return data;
        }
        private static void BindOilpanSource(Dictionary<string, object?> fields, GuestEngineInputData entry, EngineMountedPartData data)
        {
            if (entry.FamilyPrefix != "Oilpan" || entry.MountPath != data.MountPath || entry.InputFsm != data.Fsm
                || entry.ReaderPath != "CORRIS/Simulation/Engine/" + (entry.Fsm == "Cylinders" ? "Combustion" : "Oil")
                || (entry.Fsm != "Oil" && entry.Fsm != "Wearing" && entry.Fsm != "Cylinders") || entry.TargetVariable != "db_Oilpan" || entry.DirectTarget
                || fields.ContainsKey("alternateFamilies") || fields.ContainsKey("slotIndex") || fields.ContainsKey("mountVariable"))
                throw new FormatException("Oilpan inputs must use the protected native block mount.");
            entry.OilpanSource = data;
        }
        private static void ValidateOilpanReader(GuestEngineInputData entry, GuestEngineInputReaderData reader)
        {
            string state = "", output = reader.Variable; int index = 0;
            if (entry.Fsm == "Cylinders") { state = "Plug data"; output = "SparkPlugOilCont"; index = 1; }
            else if (entry.Fsm == "Wearing") { state = reader.Variable == "Oil" ? "Oil level" : "Oil contamination"; if (reader.Variable == "OilContamination") output = "Math1"; }
            else switch (reader.Variable)
            {
                case "Wear": state = "Major damage?"; index = 2; break;
                case "OilViscosity": state = "Friction"; break;
                case "Tightness": state = "Oilpan leak"; break;
                case "Oil": state = "Get oil"; break;
            }
            if (reader.EveryFrame || reader.State != state || reader.ActionIndex != index || reader.Output != output || reader.ActionType != "GetFsmFloat")
                throw new FormatException("Unsupported native oilpan read.");
        }
    }
}
