using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static MilkConditionData ParseMilkCondition(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid milk condition catalog.");
            var data = new MilkConditionData {
                ItemName = EngineInputName(fields, "itemName"), SpoiledName = EngineInputName(fields, "spoiledName"),
                Fsm = EngineInputName(fields, "fsm"), Condition = EngineInputName(fields, "condition"),
                Idle = EngineInputName(fields, "idle"), Spoil = EngineInputName(fields, "spoil"),
                Fridge = EngineInputName(fields, "fridge"), Bad = EngineInputName(fields, "bad"), Drink = EngineInputName(fields, "drink") };
            if (data.ItemName != "milk(itemx)" || data.SpoiledName != "spoiled milk(itemx)" || data.Fsm != "Use")
                throw new FormatException("Unsupported food condition item.");
            return data;
        }
    }
    internal sealed class MilkConditionData
    {
        public string ItemName = "", SpoiledName = "", Fsm = "", Condition = "", Idle = "", Spoil = "", Fridge = "", Bad = "", Drink = "";
    }
}
