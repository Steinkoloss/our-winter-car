using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static EngineBatteryData ParseEngineBattery(Dictionary<string, object?> obj, GuestEngineProtectionData protection)
        {
            if (!obj.TryGetValue("battery", out var value) || value is not Dictionary<string, object?> fields)
                throw new FormatException("Missing engine battery source.");
            var data = new EngineBatteryData { Path = EngineInputPath(fields, "path"), Fsm = EngineInputName(fields, "fsm"),
                IdleState = EngineInputName(fields, "idleState"), ReadyStates = ReplacementStrings(fields, "readyStates", 4, 4),
                ReadVariables = ReplacementStrings(fields, "readVariables", 3, 3) };
            foreach (string scalar in new[] { "Installed", "Charge", "ChargeMax" })
                if (Array.IndexOf(data.ReadVariables, scalar) < 0) throw new FormatException("Incomplete native battery read projection.");
            if (data.Path != "CORRIS/Assemblies/VINP_Battery" || data.Fsm != "Data" || data.IdleState != "Idle")
                throw new FormatException("Unsupported engine battery source.");
            var states = new HashSet<string>(data.ReadyStates);
            foreach (string state in new[] { "Check joint", "Delay 2", "Calc", "Died battery" })
                if (!states.Remove(state)) throw new FormatException("Unsupported battery load boundary.");
            GuestEnginePausedFsmData? paused = null;
            foreach (var rule in protection.PausedFsms) if (rule.Path == data.Path && rule.Fsm == data.Fsm) paused = rule;
            if (paused == null || !paused.BlockExternalFloatWrites)
                throw new FormatException("Guest battery simulation and external scalar writes must be protected before input projection.");
            foreach (string state in new[] { "Idle", "Install 2", "Remove part", "Check joint", "Delay 2", "Calc", "Died battery" })
                if (Array.IndexOf(paused.RequiredStates, state) < 0) throw new FormatException("Incomplete guest battery protection.");
            foreach (string spec in new[] { "Starter|Turn key|6|AddFsmFloat|Charge", "Starter|Fuel Mixture|8|AddFsmFloat|Charge",
                "Starter|Wait|13|SetFsmFloat|ChargeMax", "Starter|Start or not|7|AddFsmFloat|Charge", "Starter|Start engine|9|AddFsmFloat|Charge",
                "Starter|No Flywheel|5|AddFsmFloat|Charge", "Electrics|Alternator eff|6|SetFsmFloat|ChargeMax",
                "Electrics|Delay|0|SubtractFsmFloat|Charge", "Electrics|Delay|1|AddFsmFloat|Charge", "Electrics|No charge|1|SetFsmFloat|ChargeMax" })
            {
                string[] parts = spec.Split('|'); bool found = false;
                string path = parts[0] == "Starter" ? "CORRIS/Simulation/STARTERxCorris" : "CORRIS/Simulation/Systems/Electrics";
                foreach (var writer in protection.Writers)
                    if (writer.Path == path && writer.Fsm == parts[0])
                        foreach (var action in writer.Actions)
                            if (action.State == parts[1] && action.Index.ToString() == parts[2] && action.ActionType == parts[3]
                                && action.TargetScalar == parts[4] && action.TargetFsm == "Data" && action.TargetVariable == "db_Battery")
                            {
                                if (parts[0] == "Starter" && parts[3] == "AddFsmFloat"
                                    && action.StarterDraw != (parts[1] == "No Flywheel" ? 2 : 1))
                                    throw new FormatException("Missing starter draw observation.");
                                found = true;
                            }
                if (!found) throw new FormatException("Missing guest battery charge-write protection.");
            }
            bool starterWear = false;
            foreach (var writer in protection.Writers)
                foreach (var action in writer.Actions) if (action.StarterWear) starterWear = true;
            if (!starterWear) throw new FormatException("Missing starter wear observation.");
            return data;
        }

        private static void BindEngineBatterySource(Dictionary<string, object?> fields, GuestEngineInputData entry, EngineBatteryData data)
        {
            if (entry.FamilyPrefix != "Battery" || entry.MountPath != data.Path || entry.InputFsm != data.Fsm
                || fields.ContainsKey("familyPrefix") || fields.ContainsKey("wire") || fields.ContainsKey("alternateFamilies")
                || fields.ContainsKey("slotIndex") || fields.ContainsKey("mountVariable"))
                throw new FormatException("Engine battery inputs must use the protected native battery mount.");
            entry.BatterySource = data;
        }
    }
    internal sealed class EngineBatteryData
    {
        public string Path = "", Fsm = "", IdleState = "";
        public string[] ReadyStates = new string[0];
        public string[] ReadVariables = new string[0];
    }
}
