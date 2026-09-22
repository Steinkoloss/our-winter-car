using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static EngineMountedPartData ParseCylinderHead(Dictionary<string, object?> block, GuestEngineProtectionData protection)
        {
            if (!block.TryGetValue("head", out var value) || value is not Dictionary<string, object?> fields)
                throw new FormatException("Missing cylinder-head engine input.");
            var data = new EngineMountedPartData { MountPath = EngineInputPath(fields, "mountPath"), RootPrefix = EngineInputName(fields, "rootPrefix"),
                RelativePath = EngineInputName(fields, "relativePath"), PartPrefix = EngineInputName(fields, "partPrefix"),
                Fsm = EngineInputName(fields, "fsm"), ReadyState = EngineInputName(fields, "readyState") };
            if (data.MountPath != "CARPARTS/StartParts/VIN1010/VINP_Cylinderhead" || data.RootPrefix != "VIN101"
                || data.RelativePath != "VINP_Cylinderhead" || data.PartPrefix != "VIN111" || data.Fsm != "Data" || data.ReadyState != "UPDATE")
                throw new FormatException("Unsupported cylinder-head engine source.");
            GuestEnginePausedFsmData? paused = null;
            foreach (var rule in protection.PausedFsms) if (rule.Path == data.MountPath && rule.Fsm == data.Fsm) paused = rule;
            if (paused == null || paused.RootPrefix != data.RootPrefix || paused.RelativePath != data.RelativePath)
                throw new FormatException("Cylinder-head protection must follow its native block.");
            foreach (string state in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", "UPDATE" })
                if (Array.IndexOf(paused.RequiredStates, state) < 0) throw new FormatException("Incomplete cylinder-head protection.");
            return data;
        }
        private static void BindCylinderHeadSource(Dictionary<string, object?> fields, GuestEngineInputData entry, EngineMountedPartData data)
        {
            if (entry.FamilyPrefix != "CylinderHead" || entry.MountPath != data.MountPath || entry.InputFsm != data.Fsm
                || entry.ReaderPath != "CORRIS/Simulation/Engine/Combustion" || entry.Fsm != "Cylinders"
                || entry.TargetVariable != "db_Cylinderhead" || entry.DirectTarget || fields.ContainsKey("familyPrefix")
                || fields.ContainsKey("wire") || fields.ContainsKey("battery") || fields.ContainsKey("block") || fields.ContainsKey("gearbox")
                || fields.ContainsKey("alternateFamilies") || fields.ContainsKey("slotIndex") || fields.ContainsKey("mountVariable"))
                throw new FormatException("Cylinder-head inputs must use the protected native block mount.");
            entry.HeadSource = data;
        }
    }
    internal sealed class EngineMountedPartData
    {
        internal string[] AlternatePartPrefixes = new string[0];
        public string MountPath = "", RootPrefix = "", RelativePath = "", PartPrefix = "", Fsm = "", ReadyState = "";
    }
}
