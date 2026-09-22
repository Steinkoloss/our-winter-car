using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static EngineBlockData ParseEngineBlock(Dictionary<string, object?> obj, GuestEngineProtectionData protection)
        {
            if (!obj.TryGetValue("block", out var value) || value is not Dictionary<string, object?> fields)
                throw new FormatException("Missing engine block source.");
            var data = new EngineBlockData { Path = EngineInputPath(fields, "path"), Fsm = EngineInputName(fields, "fsm"),
                IdleState = EngineInputName(fields, "idleState"), ReadyState = EngineInputName(fields, "readyState") };
            if (data.Path != "CORRIS/MotorPivot/MassCenter/Block/VINP_Block" || data.Fsm != "Data"
                || data.IdleState != "Idle" || data.ReadyState != "Update") throw new FormatException("Unsupported engine block source.");
            GuestEnginePausedFsmData? paused = null;
            foreach (var rule in protection.PausedFsms) if (rule.Path == data.Path && rule.Fsm == data.Fsm) paused = rule;
            if (paused == null) throw new FormatException("Guest engine block assembly must be paused before input projection.");
            foreach (string state in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Update", "State 1" })
                if (Array.IndexOf(paused.RequiredStates, state) < 0) throw new FormatException("Incomplete guest engine block protection.");
            data.Head = ParseCylinderHead(fields, protection);
            data.Carburettor = ParseCarburettor(fields, protection);
            data.AirCleaner = ParseAirCleaner(fields, protection);
            data.Exhaust = ParseExhaust(fields, protection);
            data.Oilpan = ParseOilpan(fields, protection);
            data.RockerCover = ParseRockerCover(fields, protection);
            data.Radiator = ParseRadiator(fields, protection);
            data.CoolingAmbient = ParseCoolingAmbient(fields);
            data.CoolingAirflow = ParseCoolingAirflow(fields, protection);
            data.CoolantHoses = ParseCoolantHoses(fields, protection);
            return data;
        }
        private static void BindEngineBlockSource(Dictionary<string, object?> fields, GuestEngineInputData entry, EngineBlockData data)
        {
            if (entry.FamilyPrefix != "EngineBlock" || entry.MountPath != data.Path || entry.InputFsm != data.Fsm
                || fields.ContainsKey("familyPrefix") || fields.ContainsKey("wire") || fields.ContainsKey("battery")
                || fields.ContainsKey("alternateFamilies") || fields.ContainsKey("slotIndex") || fields.ContainsKey("mountVariable")
                || (entry.Fsm == "Starter" ? !entry.DirectTarget || fields.ContainsKey("targetVariable")
                    : entry.DirectTarget || entry.TargetVariable != "db_Block"))
                throw new FormatException("Engine block inputs must use the protected native block mount.");
            entry.BlockSource = data;
        }
    }
    internal sealed class EngineBlockData
    {
        public string Path = "", Fsm = "", IdleState = "", ReadyState = "";
        public EngineCoolingAmbientData CoolingAmbient = null!;
        public EngineCoolingAirflowData[] CoolingAirflow = new EngineCoolingAirflowData[0];
        public EngineCoolantHoseData[] CoolantHoses = new EngineCoolantHoseData[0];
        public EngineExhaustPartData[] Exhaust = new EngineExhaustPartData[0];
        public EngineMountedPartData Radiator = null!, RockerCover = null!, Oilpan = null!, Head = null!, Carburettor = null!, AirCleaner = null!;
    }
}
