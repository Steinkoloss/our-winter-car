using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static EngineCoolantHoseData[] ParseCoolantHoses(Dictionary<string, object?> block, GuestEngineProtectionData protection)
        {
            if (!block.TryGetValue("coolantHoses", out var value) || value is not List<object?> rows || rows.Count != 4)
                throw new FormatException("Missing four native coolant hose sources.");
            var result = new EngineCoolantHoseData[4];
            string[] names = { "Top", "Bottom", "Inlet", "Outlet" }, prefixes = { "VIN202", "VIN203", "VIN216", "VIN217" };
            string[] paths = { "VINP_RadiatorHoseTop", "VINP_RadiatorHoseBottom", "VINP_HeaterHoseInlet", "VINP_HeaterHoseOutlet" };
            string[] targets = { "db_RadiatorHose1", "db_RadiatorHose2", "db_HeaterHose1", "db_HeaterHose2" };
            for (byte i = 0; i < result.Length; i++)
            {
                if (rows[i] is not Dictionary<string, object?> fields || EngineInputName(fields, "name") != names[i])
                    throw new FormatException("Coolant hoses must retain their native order.");
                if (!fields.TryGetValue("rootPrefix", out var root) || root is not string rootPrefix)
                    throw new FormatException("Missing coolant hose mount root binding.");
                var mount = new EngineMountedPartData { MountPath = EngineInputPath(fields, "mountPath"), RootPrefix = rootPrefix,
                    RelativePath = EngineInputName(fields, "relativePath"), PartPrefix = EngineInputName(fields, "partPrefix"),
                    Fsm = EngineInputName(fields, "fsm"), ReadyState = EngineInputName(fields, "readyState") };
                if (mount.MountPath != "CORRIS/Assemblies/" + paths[i] || mount.RootPrefix != "" || mount.RelativePath != paths[i]
                    || mount.PartPrefix != prefixes[i] || mount.Fsm != "Data" || mount.ReadyState != (i < 2 ? "Update 2" : "Update")
                    || fields.ContainsKey("alternatePartPrefixes")) throw new FormatException("Unsupported coolant hose source.");
                GuestEnginePausedFsmData? pause = null;
                foreach (var candidate in protection.PausedFsms) if (candidate.Path == mount.MountPath && candidate.Fsm == mount.Fsm) pause = candidate;
                if (pause == null || pause.RootPrefix != "" || pause.RelativePath != "") throw new FormatException("Coolant hose mount must be protected.");
                foreach (string state in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", mount.ReadyState })
                    if (Array.IndexOf(pause.RequiredStates, state) < 0) throw new FormatException("Incomplete coolant hose protection.");
                bool project = false;
                if (fields.TryGetValue("projectNativeReads", out var nativeReads))
                {
                    if (nativeReads is not bool enabled) throw new FormatException("Invalid native hose read projection.");
                    project = enabled;
                }
                if (project != (i >= 2)) throw new FormatException("Missing or unsupported native heater hose projection.");
                result[i] = new EngineCoolantHoseData { Index = i, Name = "CoolantHose" + names[i], Mount = mount,
                    TargetVariable = targets[i], ProjectNativeReads = project };
            }
            return result;
        }
        private static void BindCoolantHoseSource(Dictionary<string, object?> fields, GuestEngineInputData entry, EngineCoolantHoseData[] sources)
        {
            EngineCoolantHoseData? source = null;
            foreach (var candidate in sources) if (candidate.Name == entry.FamilyPrefix) source = candidate;
            if (source == null || entry.MountPath != source.Mount.MountPath || entry.InputFsm != source.Mount.Fsm
                || entry.ReaderPath != "CORRIS/Simulation/Systems/Cooling" || entry.Fsm != "Cooling" || entry.TargetVariable != source.TargetVariable || entry.DirectTarget
                || fields.ContainsKey("alternateFamilies") || fields.ContainsKey("slotIndex") || fields.ContainsKey("mountVariable"))
                throw new FormatException("Coolant hose input must use its protected native mount.");
            entry.CoolantHoseSource = source;
        }
        private static void ValidateCoolantHoseReader(GuestEngineInputData entry, GuestEngineInputReaderData reader)
        {
            var source = entry.CoolantHoseSource!; bool installed = reader.Variable == "Installed";
            if (reader.EveryFrame || (installed ? source.Index != 1 || reader.State != "Bottom hose" || reader.ActionIndex != 0
                    || reader.ActionType != "GetFsmBool" || reader.Output != "Installed1"
                : reader.Variable != "Tightness" || reader.State != "Hoses" || reader.ActionIndex != source.Index
                    || reader.ActionType != "GetFsmFloat" || reader.Output != "Tightness" + (source.Index + 1)))
                throw new FormatException("Unsupported native coolant hose read.");
        }
    }
    internal sealed class EngineCoolantHoseData
    {
        public byte Index;
        public bool ProjectNativeReads;
        public string Name = "", TargetVariable = "";
        public EngineMountedPartData Mount = null!;
    }
}
