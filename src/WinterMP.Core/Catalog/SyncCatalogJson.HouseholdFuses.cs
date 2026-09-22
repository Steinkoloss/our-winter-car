using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static HouseholdFuseData ParseHouseholdFuses(object? value)
        {
            if (value is not Dictionary<string, object?> fields || !fields.TryGetValue("tables", out var tableValue) || tableValue is not List<object?> tables || tables.Count != 2)
                throw new FormatException("Invalid household fuse tables.");
            var data = new HouseholdFuseData();
            foreach (string key in new[] { "useFsm", "screwFsm", "removalFsm", "assemblyFsm", "fusesList", "holdersList", "slotsList",
                "insertTrigger", "tightenState", "loosenState", "removeState", "assemblyState", "assemblyWait", "screwIdle", "removalIdle", "assemblyIdle", "shockFsm", "shockEvent", "partPickupState" })
                data.Names.Add(key, EngineInputPath(fields, key));
            for (int i = 0; i < tables.Count; i++)
            {
                if (tables[i] is not Dictionary<string, object?> table || !table.TryGetValue("holders", out var holderValue) || holderValue is not List<object?> holders || holders.Count != (i == 0 ? 7 : 4))
                    throw new FormatException("Invalid native holder identity list.");
                var t = new HouseholdFuseTable { Database = EngineInputPath(table, "database"), Holders = new string[holders.Count] };
                for (int j = 0; j < holders.Count; j++)
                {
                    if (holders[j] is not string name || name.Length == 0 || Array.IndexOf(t.Holders, name) >= 0) throw new FormatException("Invalid fuse holder ID.");
                    t.Holders[j] = name;
                }
                data.Tables.Add(t);
            }
            return data;
        }
    }
    internal sealed class HouseholdFuseData
    {
        internal readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);
        internal readonly List<HouseholdFuseTable> Tables = new List<HouseholdFuseTable>();
        internal string this[string key] => Names[key];
    }
    internal sealed class HouseholdFuseTable
    {
        internal string Database = string.Empty;
        internal string[] Holders = new string[0];
    }
}
