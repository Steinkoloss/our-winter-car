using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static EngineMountedPartData ParseRockerCover(Dictionary<string, object?> block, GuestEngineProtectionData protection)
        {
            if (!block.TryGetValue("rockerCover", out var value) || value is not Dictionary<string, object?> fields)
                throw new FormatException("Missing rocker-cover engine input.");
            var data = new EngineMountedPartData { MountPath = EngineInputPath(fields, "mountPath"), RootPrefix = EngineInputName(fields, "rootPrefix"),
                RelativePath = EngineInputName(fields, "relativePath"), PartPrefix = EngineInputName(fields, "partPrefix"),
                Fsm = EngineInputName(fields, "fsm"), ReadyState = EngineInputName(fields, "readyState") };
            if (data.MountPath != "CARPARTS/StartParts/VIN1110/VINP_RockerCover" || data.RootPrefix != "VIN111"
                || data.RelativePath != "VINP_RockerCover" || data.PartPrefix != "VIN118" || data.Fsm != "Data" || data.ReadyState != "Update 2"
                || fields.ContainsKey("alternatePartPrefixes")) throw new FormatException("Unsupported rocker-cover source.");
            GuestEnginePausedFsmData? paused = null;
            foreach (var rule in protection.PausedFsms) if (rule.Path == data.MountPath && rule.Fsm == data.Fsm) paused = rule;
            if (paused == null || paused.RootPrefix != data.RootPrefix || paused.RelativePath != data.RelativePath)
                throw new FormatException("RockerCover protection must follow its native head.");
            foreach (string state in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", "Update 2" })
                if (Array.IndexOf(paused.RequiredStates, state) < 0) throw new FormatException("Incomplete rocker-cover protection.");
            return data;
        }
        private static void BindRockerCoverSource(Dictionary<string, object?> fields, GuestEngineInputData entry, EngineMountedPartData data)
        {
            if (entry.FamilyPrefix != "RockerCover" || entry.MountPath != data.MountPath || entry.InputFsm != data.Fsm
                || entry.ReaderPath != "CORRIS/Simulation/Engine/Oil" || entry.Fsm != "Oil"
                || entry.TargetVariable != "db_Rockercover1" || entry.DirectTarget
                || fields.ContainsKey("alternateFamilies") || fields.ContainsKey("slotIndex") || fields.ContainsKey("mountVariable"))
                throw new FormatException("RockerCover inputs must use the protected native head mount.");
            entry.RockerCoverSource = data;
        }
        private static void ValidateRockerCoverReader(GuestEngineInputData entry, GuestEngineInputReaderData reader)
        {
            if (reader.EveryFrame || reader.State != "Valve Cover" || reader.ActionIndex != 0 || reader.Variable != "Tightness"
                || reader.Output != "Tightness" || reader.ActionType != "GetFsmFloat")
                throw new FormatException("Unsupported native rocker-cover read.");
        }
    }
}
