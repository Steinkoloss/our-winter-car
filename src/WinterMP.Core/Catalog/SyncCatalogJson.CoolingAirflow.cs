using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static EngineCoolingAirflowData[] ParseCoolingAirflow(Dictionary<string, object?> block, GuestEngineProtectionData protection)
        {
            if (!block.TryGetValue("coolingAirflow", out var value) || value is not List<object?> rows || rows.Count != 4)
                throw new FormatException("Missing four native cooling airflow sources.");
            var result = new EngineCoolingAirflowData[4];
            string[] names = { "Grille", "GrilleBlockoff", "Hood", "FiberglassHood" }, prefixes = { "VIN413", "BLOCKOFF", "VIN411", "HOODa0" };
            string[] targets = { "db_Grille", "db_GrilleBlockoff", "db_Hood", "db_Hood2" };
            for (byte i = 0; i < result.Length; i++)
            {
                if (rows[i] is not Dictionary<string, object?> fields || EngineInputName(fields, "name") != names[i])
                    throw new FormatException("Cooling airflow sources must retain native order.");
                if (!fields.TryGetValue("rootPrefix", out var root) || root is not string rootPrefix)
                    throw new FormatException("Missing cooling airflow root binding.");
                var mount = new EngineMountedPartData { MountPath = EngineInputPath(fields, "mountPath"), RootPrefix = rootPrefix,
                    RelativePath = EngineInputName(fields, "relativePath"), PartPrefix = EngineInputName(fields, "partPrefix"),
                    Fsm = EngineInputName(fields, "fsm"), ReadyState = EngineInputName(fields, "readyState") };
                if (mount.MountPath != "CORRIS/" + (i == 1 || i == 3 ? "AssembliesTuning/" : "Assemblies/") + "VINP_" + names[i]
                    || mount.RootPrefix != "" || mount.RelativePath != "VINP_" + names[i] || mount.PartPrefix != prefixes[i]
                    || mount.Fsm != "Data" || mount.ReadyState != "Update 2") throw new FormatException("Unsupported cooling airflow source.");
                if (i == 0)
                {
                    mount.AlternatePartPrefixes = ReplacementStrings(fields, "alternatePartPrefixes", 3, 3);
                    if (mount.AlternatePartPrefixes[0] != "VIN413B" || mount.AlternatePartPrefixes[1] != "VIN413C" || mount.AlternatePartPrefixes[2] != "VIN413D")
                        throw new FormatException("Cooling requires all four native grilles.");
                }
                else if (fields.ContainsKey("alternatePartPrefixes")) throw new FormatException("Unsupported airflow variant.");
                GuestEnginePausedFsmData? pause = null;
                foreach (var candidate in protection.PausedFsms) if (candidate.Path == mount.MountPath && candidate.Fsm == mount.Fsm) pause = candidate;
                if (pause == null || pause.RootPrefix != "" || pause.RelativePath != "") throw new FormatException("Cooling airflow mount must be protected.");
                foreach (string state in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", "Update 2" })
                    if (Array.IndexOf(pause.RequiredStates, state) < 0) throw new FormatException("Incomplete cooling airflow protection.");
                result[i] = new EngineCoolingAirflowData { Index = i, Name = names[i], Mount = mount, TargetVariable = targets[i] };
            }
            return result;
        }
        private static void BindCoolingAirflowSource(Dictionary<string, object?> fields, GuestEngineInputData entry, EngineCoolingAirflowData[] sources)
        {
            EngineCoolingAirflowData? source = null;
            foreach (var candidate in sources) if (candidate.Name == entry.FamilyPrefix) source = candidate;
            if (source == null || entry.MountPath != source.Mount.MountPath || entry.InputFsm != source.Mount.Fsm
                || entry.ReaderPath != "CORRIS/Simulation/Systems/Cooling" || entry.Fsm != "Cooling" || entry.TargetVariable != source.TargetVariable || entry.DirectTarget
                || fields.ContainsKey("alternateFamilies") || fields.ContainsKey("slotIndex") || fields.ContainsKey("mountVariable"))
                throw new FormatException("Cooling airflow input must use its protected native mount.");
            entry.CoolingAirflowSource = source;
        }
        private static void ValidateCoolingAirflowReader(GuestEngineInputData entry, GuestEngineInputReaderData reader)
        {
            byte i = entry.CoolingAirflowSource!.Index; bool installed = reader.Variable == "Installed";
            string state = installed ? i == 0 ? "Grille" : i == 1 ? "Grille and Cover" : "Hood"
                : i == 0 ? "Grille and Cover" : i == 2 ? "Hood installed" : "Hood installed 2";
            if (reader.EveryFrame || reader.State != state || reader.ActionIndex != (installed && (i == 1 || i == 3) ? 2 : 0)
                || reader.ActionType != (installed ? "GetFsmBool" : "GetFsmFloat") || reader.Output != (installed ? "Installed1" : "Data")
                || !installed && (i == 1 || reader.Variable != "CoolingAirRateModifier")) throw new FormatException("Unsupported native cooling airflow read.");
        }
    }
    internal sealed class EngineCoolingAirflowData
    {
        public byte Index;
        public string Name = "", TargetVariable = "";
        public EngineMountedPartData Mount = null!;
    }
}
