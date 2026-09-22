using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static PaneScrapeData ParsePaneScrape(object? value)
        {
            if (value is not Dictionary<string, object?> obj) throw new FormatException("Invalid pane scrape binding.");
            var data = new PaneScrapeData();
            for (int i = 0; i < PaneScrapeData.Keys.Length; i++)
            {
                string key = PaneScrapeData.Keys[i];
                string binding = RequiredString(obj, key);
                if (binding != PaneScrapeData.Audited[i])
                    throw new FormatException("Pane scrape binding exceeds the audited one-pane scope: " + key);
                data.Bindings.Add(key, binding);
            }
            return data;
        }
    }
    internal sealed class PaneScrapeData
    {
        internal static readonly string[] Keys = { "vehicle", "panePath", "paneFsm", "freezingPath", "freezingFsm",
            "cutoff", "deltaState", "strokeState", "strokeEvent", "toolName", "handPath", "handFsm", "picked",
            "pickup", "equip", "off", "drop", "throw", "eyePath", "insidePath" };
        internal static readonly string[] Audited = { "CORRIS", "CORRIS/BODY/Windshield/collider", "Scrape",
            "CORRIS/Simulation/CarTempCorris", "Freezing", "CutoffWindshield", "State 7", "Scrape 2", "WINDSHIELD",
            "ice scraper(itemx)", "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand", "PickUp", "PickedObject",
            "Set pivot 2", "Ice Scraper", "Off", "Drop part", "Drop part 2",
            "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Camera/Camera", "CORRIS/Functions/PlayerTrigger" };
        internal readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public string this[string key] => Bindings[key];
    }
}
