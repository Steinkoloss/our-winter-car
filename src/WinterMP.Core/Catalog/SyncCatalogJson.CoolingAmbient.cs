using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static EngineCoolingAmbientData ParseCoolingAmbient(Dictionary<string, object?> block)
        {
            if (!block.TryGetValue("coolingAmbient", out var value) || value is not Dictionary<string, object?> fields)
                throw new FormatException("Missing native cooling ambient source.");
            var data = new EngineCoolingAmbientData { Path = EngineInputPath(fields, "path"), Fsm = EngineInputName(fields, "fsm"),
                Variable = EngineInputName(fields, "variable"), States = ReplacementStrings(fields, "states", 4, 4) };
            if (data.Path != "CORRIS/Functions/RoofCheck" || data.Fsm != "Raycast" || data.Variable != "TempCar"
                || data.States[0] != "Cast ray" || data.States[1] != "Check roof" || data.States[2] != "Under roof" || data.States[3] != "Under sky")
                throw new FormatException("Unsupported cooling ambient producer.");
            return data;
        }
        private static void BindCoolingAmbientSource(Dictionary<string, object?> fields, GuestEngineInputData entry, EngineCoolingAmbientData source)
        {
            if (entry.FamilyPrefix != "CoolingAmbient" || entry.MountPath != source.Path || entry.InputFsm != source.Fsm
                || entry.ReaderPath != "CORRIS/Simulation/Systems/Cooling" || entry.Fsm != "Cooling" || entry.TargetVariable != "RoofCheck" || entry.DirectTarget
                || fields.ContainsKey("alternateFamilies") || fields.ContainsKey("slotIndex") || fields.ContainsKey("mountVariable"))
                throw new FormatException("Cooling ambient input must use the native RoofCheck reference.");
            entry.CoolingAmbientSource = source;
        }
        private static void ValidateCoolingAmbientReader(GuestEngineInputData entry, GuestEngineInputReaderData reader)
        {
            if (reader.State != "Reset" || reader.ActionIndex != 1 || reader.ActionType != "GetFsmFloat"
                || reader.Variable != entry.CoolingAmbientSource!.Variable || reader.Output != "TempArea" || reader.EveryFrame)
                throw new FormatException("Unsupported cooling ambient read.");
        }
    }
    internal sealed class EngineCoolingAmbientData
    {
        public string Path = "", Fsm = "", Variable = "";
        public string[] States = new string[0];
    }
}
