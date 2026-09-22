using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static EngineExhaustPartData[] ParseExhaust(Dictionary<string, object?> block, GuestEngineProtectionData protection)
        {
            if (!block.TryGetValue("exhaust", out var value) || value is not List<object?> rows || rows.Count != 4)
                throw new FormatException("Missing four native exhaust input sources.");
            var result = new EngineExhaustPartData[4];
            string[] names = { "Headers", "Front", "Rear", "Muffler" }, prefixes = { "VIN114", "VIN213", "VIN214", "VIN219" };
            string[] paths = { "CARPARTS/StartParts/VIN1110/VINP_ExhaustManifold", "CORRIS/MotorPivot/MassCenter/VINP_ExhaustFront",
                "CORRIS/MotorPivot/MassCenter/Exhaust2MotorPivot/VINP_ExhaustRear", "CORRIS/MotorPivot/MassCenter/Exhaust2MotorPivot/VINP_ExhaustMuffler" };
            string[][] alternatives = { new[] { "VIN114B", "HEADERSa0", "HEADERSc0", "HEADERSd0" }, new[] { "VIN213B" }, new[] { "EXHAUSTRa0" }, new[] { "MUFFLERa0" } };
            for (byte i = 0; i < result.Length; i++)
            {
                if (rows[i] is not Dictionary<string, object?> fields || EngineInputName(fields, "name") != names[i])
                    throw new FormatException("Exhaust sources must retain their native group order.");
                if (!fields.TryGetValue("rootPrefix", out var rootValue) || rootValue is not string rootPrefix)
                    throw new FormatException("Missing exhaust mount root binding.");
                var mount = new EngineMountedPartData { MountPath = EngineInputPath(fields, "mountPath"), RootPrefix = rootPrefix,
                    RelativePath = EngineInputName(fields, "relativePath"), PartPrefix = EngineInputName(fields, "partPrefix"),
                    Fsm = EngineInputName(fields, "fsm"), ReadyState = EngineInputName(fields, "readyState"), AlternatePartPrefixes = ReplacementStrings(fields, "alternatePartPrefixes", 1, 4) };
                if (mount.MountPath != paths[i] || mount.RootPrefix != (i == 0 ? "VIN111" : "") || mount.RelativePath != paths[i].Substring(paths[i].LastIndexOf('/') + 1)
                    || mount.PartPrefix != prefixes[i] || mount.Fsm != "Data" || mount.ReadyState != "Update 2"
                    || mount.AlternatePartPrefixes.Length != alternatives[i].Length) throw new FormatException("Unsupported native exhaust source.");
                for (int n = 0; n < alternatives[i].Length; n++) if (mount.AlternatePartPrefixes[n] != alternatives[i][n]) throw new FormatException("Unsupported exhaust variant.");
                GuestEnginePausedFsmData? pause = null;
                foreach (var candidate in protection.PausedFsms) if (candidate.Path == mount.MountPath && candidate.Fsm == mount.Fsm) pause = candidate;
                if (pause == null || pause.RootPrefix != mount.RootPrefix || i == 0 && pause.RelativePath != mount.RelativePath)
                    throw new FormatException("Native exhaust mount must be protected before projection.");
                foreach (string state in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", "Update 2" })
                    if (Array.IndexOf(pause.RequiredStates, state) < 0) throw new FormatException("Incomplete exhaust mount protection.");
                result[i] = new EngineExhaustPartData { Index = i, Name = "Exhaust" + names[i], Mount = mount,
                    State = i == 1 ? "Exhaust front" : i == 2 ? "Exhaust rear" : names[i],
                    TargetVariable = i == 0 ? "db_ExhaustManifold" : i == 1 ? "db_ExhaustPipeFront" : i == 2 ? "db_ExhaustPipeRear" : "db_ExhaustMuffler" };
            }
            return result;
        }
        private static void BindExhaustSource(Dictionary<string, object?> fields, GuestEngineInputData entry, EngineExhaustPartData[] sources)
        {
            EngineExhaustPartData? source = null;
            foreach (var candidate in sources) if (candidate.Name == entry.FamilyPrefix) source = candidate;
            if (source == null || entry.MountPath != source.Mount.MountPath || entry.InputFsm != source.Mount.Fsm || entry.ReaderPath != "CORRIS/Simulation/Engine/Valves"
                || entry.Fsm != "Valves" || entry.TargetVariable != source.TargetVariable || entry.DirectTarget
                || fields.ContainsKey("alternateFamilies") || fields.ContainsKey("slotIndex") || fields.ContainsKey("mountVariable"))
                throw new FormatException("Exhaust inputs must use the protected native performance mounts.");
            entry.ExhaustSource = source;
        }
    }
    internal sealed class EngineExhaustPartData
    {
        public byte Index;
        public string Name = "", State = "", TargetVariable = "";
        public EngineMountedPartData Mount = null!;
    }
}
