using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static EngineHeaterData ParseEngineHeater(Dictionary<string, object?> obj, GuestEngineProtectionData protection)
        {
            if (!obj.TryGetValue("heater", out var value) || value is not Dictionary<string, object?> fields)
                throw new FormatException("Missing heater input source.");
            var data = new EngineHeaterData { Path = EngineInputPath(fields, "path"), Fsm = EngineInputName(fields, "fsm"),
                IdleState = EngineInputName(fields, "idleState"), ReadyState = EngineInputName(fields, "readyState"),
                ReadVariables = ReplacementStrings(fields, "readVariables", 2, 2) };
            if (data.Path != "CORRIS/Assemblies/VINP_Heaterbox" || data.Fsm != "Data" || data.IdleState != "Idle" || data.ReadyState != "Update 2"
                || Array.IndexOf(data.ReadVariables, "Installed") < 0 || Array.IndexOf(data.ReadVariables, "Wear") < 0)
                throw new FormatException("Unsupported native heater source.");
            GuestEnginePausedFsmData? paused = null;
            foreach (var rule in protection.PausedFsms) if (rule.Path == data.Path && rule.Fsm == data.Fsm) paused = rule;
            if (paused == null || !paused.BlockExternalFloatWrites)
                throw new FormatException("Guest heater mount and external wear writes must be protected before input projection.");
            foreach (string state in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", "Update 2" })
                if (Array.IndexOf(paused.RequiredStates, state) < 0) throw new FormatException("Incomplete guest heater protection.");
            if (!fields.TryGetValue("rearWindow", out var rear) || rear is not Dictionary<string, object?> window)
                throw new FormatException("Missing rear-window heating element source.");
            data.RearWindow = new RearWindowHeaterData { Path = EngineInputPath(window, "path"), Fsm = EngineInputName(window, "fsm"),
                Variable = EngineInputName(window, "variable"), ReadyStates = ReplacementStrings(window, "readyStates", 3, 3) };
            if (data.RearWindow.Path != "CORRIS/BODY" || data.RearWindow.Fsm != "Save" || data.RearWindow.Variable != "HeatingSprites")
                throw new FormatException("Unsupported native rear-window heating element.");
            foreach (string state in new[] { "State 1", "State 4", "Save" })
                if (Array.IndexOf(data.RearWindow.ReadyStates, state) < 0) throw new FormatException("Incomplete rear-window load boundary.");
            return data;
        }
    }
    internal sealed class EngineHeaterData
    {
        public string Path = "", Fsm = "", IdleState = "", ReadyState = "";
        public string[] ReadVariables = new string[0];
        public RearWindowHeaterData RearWindow = null!;
    }
    internal sealed class RearWindowHeaterData
    {
        public string Path = "", Fsm = "", Variable = "";
        public string[] ReadyStates = new string[0];
    }
}
