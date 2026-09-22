using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static UtilityPaymentsData ParseUtilityPayments(object? value, bool phone = false)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid utility payments catalog.");
            var data = new UtilityPaymentsData();
            foreach (string key in UtilityPaymentsData.Required) data.Bindings.Add(key, RequiredString(fields, key));
            if (phone)
                foreach (string key in UtilityPaymentsData.PhoneRequired) data.Bindings.Add(key, RequiredString(fields, key));
            foreach (string key in new[] { "sheet1", "sheet2" })
                if (!ValidScenePath(data[key])) throw new FormatException("Invalid utility sheet path.");
            if (data["sheet1"] == data["sheet2"]) throw new FormatException("Duplicate utility sheet.");
            return data;
        }
    }
    internal sealed class UtilityPaymentsData
    {
        public static readonly string[] Required = { "sheet1", "sheet2", "sheetFsm", "payChild", "payFsm",
            "request", "commit", "close", "funds", "idle", "calculate", "total", "totalText", "debt", "payEvent" };
        public static readonly string[] PhoneRequired = { "minutes", "longMinutes", "connects", "longConnects",
            "base", "connectionRate", "minuteRate", "longMinuteRate", "localCalculate", "longCalculate", "totals", "oldBill" };
        public readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public string this[string key] => Bindings[key];
    }
}
