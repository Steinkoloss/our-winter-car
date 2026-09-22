using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static TaxiPassengersData ParseTaxiPassengers(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid taxi passenger catalog.");
            var data = new TaxiPassengersData();
            foreach (string key in new[] { "path", "driveTrigger", "driverMass", "customerMass", "tutorial" })
                data.Names.Add(key, EngineInputPath(fields, key));
            return data;
        }
    }

    internal sealed class TaxiPassengersData
    {
        internal readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);
        internal string this[string key] => Names[key];
    }
}
