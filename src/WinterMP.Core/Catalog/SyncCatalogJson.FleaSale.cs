using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static FleaSaleData ParseFleaSale(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid flea sale catalog.");
            var data = new FleaSaleData();
            foreach (string key in FleaSaleData.Required) data.Bindings.Add(key, RequiredString(fields, key));
            foreach (string key in new[] { "tablePath", "cashPath", "rentPath", "envelopePath", "inventoryPath", "pricingPath" })
                if (!ValidScenePath(data[key])) throw new FormatException("Invalid flea path: " + key);
            return data;
        }
    }
    internal sealed class FleaSaleData
    {
        public static readonly string[] Required = { "tablePath", "logic", "sell", "day", "cashPath", "cashFsm",
            "rentPath", "rentFsm", "envelopePath", "envelopeFsm", "money", "days", "weekPrice", "cashGlobal",
            "request", "collect", "rentEvent", "rentCommit", "idle", "resetCart", "funds", "inventoryPath", "bought", "listingName", "listingIdPrefix", "listingIds", "listingPrices", "pricingPath", "pricingFsm" };
        public readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public string this[string key] => Bindings[key];
    }
}
