using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static CylinderHeadData ParseCylinderHead(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid cylinder head catalog.");
            var data = new CylinderHeadData { NativeId = EngineInputName(fields, "nativeId"),
                ParentNativeId = EngineInputName(fields, "parentNativeId"), MountPath = EngineInputPath(fields, "mountPath"),
                BoltPath = EngineInputPath(fields, "boltPath") };
            if (data.NativeId != "VIN1110" || data.ParentNativeId != "VIN1010" || data.MountPath != "VINP_Cylinderhead"
                || data.BoltPath != "Bolts/Masked/BoltPM")
                throw new FormatException("Unsupported cylinder head attachment.");
            return data;
        }
        private static ValveAdjustmentData ParseValveAdjustment(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid valve adjustment catalog.");
            var data = new ValveAdjustmentData { ParentPath = EngineInputPath(fields, "parentPath"),
                Fsm = EngineInputName(fields, "fsm"), Reference = EngineInputName(fields, "reference"),
                HeadPrefix = EngineInputName(fields, "headPrefix") };
            if (data.ParentPath != "ValveAdjustment/Masked" || data.Fsm != "Screw" || data.Reference != "Valves" || data.HeadPrefix != "VIN111")
                throw new FormatException("Unsupported valve adjustment binding.");
            return data;
        }
    }
    internal sealed class ValveAdjustmentData
    {
        public string ParentPath = "", Fsm = "", Reference = "", HeadPrefix = "";
    }
    internal sealed class CylinderHeadData
    {
        public string NativeId = "", ParentNativeId = "", MountPath = "", BoltPath = "";
    }
}
