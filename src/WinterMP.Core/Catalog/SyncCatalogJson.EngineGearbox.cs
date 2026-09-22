using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static EngineGearboxData ParseEngineGearbox(Dictionary<string, object?> obj, GuestEngineProtectionData protection)
        {
            if (!obj.TryGetValue("gearbox", out var value) || value is not Dictionary<string, object?> fields)
                throw new FormatException("Missing engine gearbox source.");
            var data = new EngineGearboxData { Path = EngineInputPath(fields, "path"), Fsm = EngineInputName(fields, "fsm"),
                IdleState = EngineInputName(fields, "idleState"), ReadyState = EngineInputName(fields, "readyState") };
            if (data.Path != "CORRIS/MotorPivot/MassCenter/Block/VINP_Gearbox" || data.Fsm != "Data"
                || data.IdleState != "Idle" || data.ReadyState != "Update 2") throw new FormatException("Unsupported engine gearbox source.");
            GuestEnginePausedFsmData? paused = null;
            foreach (var rule in protection.PausedFsms) if (rule.Path == data.Path && rule.Fsm == data.Fsm) paused = rule;
            if (paused == null) throw new FormatException("Guest engine gearbox assembly must be paused before input projection.");
            foreach (string state in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Update 2", "State 1", "Remove other", "Remove other 2", "Allow install?", "Far", "Near", "Check Flywheel" })
                if (Array.IndexOf(paused.RequiredStates, state) < 0) throw new FormatException("Incomplete guest engine gearbox protection.");
            return data;
        }
        private static void BindEngineGearboxSource(Dictionary<string, object?> fields, GuestEngineInputData entry, EngineGearboxData data)
        {
            if (entry.FamilyPrefix != "Gearbox" || entry.MountPath != data.Path || entry.InputFsm != data.Fsm
                || entry.ReaderPath != "CORRIS/Simulation/STARTERxCorris" || entry.Fsm != "Starter"
                || entry.DirectTarget || entry.TargetVariable != "db_Gearbox"
                || fields.ContainsKey("familyPrefix") || fields.ContainsKey("wire") || fields.ContainsKey("battery") || fields.ContainsKey("block")
                || fields.ContainsKey("alternateFamilies") || fields.ContainsKey("slotIndex") || fields.ContainsKey("mountVariable"))
                throw new FormatException("Gearbox inputs must use the protected native transmission mount.");
            entry.GearboxSource = data;
        }
    }
    internal sealed class EngineGearboxData
    {
        public string Path = "", Fsm = "", IdleState = "", ReadyState = "";
    }
}
