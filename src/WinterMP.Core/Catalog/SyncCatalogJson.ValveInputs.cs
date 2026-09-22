using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static GuestValveInputsData ParseValveInputs(Dictionary<string, object?> obj)
        {
            if (!obj.TryGetValue("valves", out var value) || value is not Dictionary<string, object?> fields)
                throw new FormatException("Missing native valve array inputs.");
            var data = new GuestValveInputsData { ReaderPath = EngineInputPath(fields, "readerPath"), Fsm = EngineInputName(fields, "fsm"),
                TargetVariable = EngineInputName(fields, "targetVariable"), Reference = EngineInputName(fields, "reference"), Output = EngineInputName(fields, "output"),
                HeadState = EngineInputName(fields, "headState"), HeadActionIndex = SlotNumber(fields.TryGetValue("headActionIndex", out var index) ? index : null, "head action", 0, 255) };
            if (data.ReaderPath != "CORRIS/Simulation/Engine/Valves" || data.Fsm != "Valves" || data.TargetVariable != "Cylinderhead"
                || data.Reference != "Valves" || data.Output != "Data" || data.HeadState != "Get cam profile" || data.HeadActionIndex != 8)
                throw new FormatException("Unsupported native valve array source.");
            int count = 0;
            foreach (var row in EngineInputEntries(fields, "readers", 8))
            {
                string state = EngineInputName(row, "state");
                if (state != "Cyl " + (count / 2 + 1) + (count % 2 == 0 ? " intake" : " exhaust")
                    || SlotNumber(row.TryGetValue("actionIndex", out var action) ? action : null, "valve action", 0, 255) != 0
                    || SlotNumber(row.TryGetValue("arrayIndex", out var slot) ? slot : null, "valve index", 0, 7) != count)
                    throw new FormatException("Native valve inputs must retain cylinder and intake/exhaust order.");
                data.States[count++] = state;
            }
            return data;
        }
    }
    internal sealed class GuestValveInputsData
    {
        public string ReaderPath = "", Fsm = "", TargetVariable = "", Reference = "", Output = "", HeadState = "";
        public int HeadActionIndex;
        public string[] States = new string[8];
    }
}
