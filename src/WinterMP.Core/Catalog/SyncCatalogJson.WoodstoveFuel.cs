using System;
using System.Collections.Generic;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static WoodstoveFuelData ParseWoodstoveFuel(object? value)
        {
            if (!(value is Dictionary<string, object?> obj)) throw new FormatException("Invalid cabin fuel binding.");
            var data = new WoodstoveFuelData();
            foreach (string key in WoodstoveFuelData.Keys) data.Bindings.Add(key, RequiredString(obj, key));
            if (data["source"] != WoodstoveFuelAuthority.CabinPath || data["pieceName"] != "firewood(Clone)"
                || data["loggingPath"] != "CABIN/LOD/Logging/Logwall" || data["loggingFsm"] != "Use"
                || data["createState"] != "Create log")
                throw new FormatException("Cabin fuel source exceeds audited scope.");
            return data;
        }
    }
    internal sealed class WoodstoveFuelData
    {
        internal static readonly string[] Keys = { "source", "loggingPath", "loggingFsm", "createState", "pieceName" };
        internal readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public string this[string key] => Bindings[key];
    }
}
