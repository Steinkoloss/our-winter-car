using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static FirewoodDeliveryData ParseFirewoodDelivery(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid firewood delivery catalog.");
            var data = new FirewoodDeliveryData();
            foreach (string key in FirewoodDeliveryData.Fields) data.Values.Add(key, EngineInputPath(fields, key));
            return data;
        }

        private static List<FirewoodBuyerData> ParseFirewoodBuyers(object? value)
        {
            if (value is not List<object?> rows || rows.Count != 4) throw new FormatException("Expected four firewood buyers.");
            var result = new List<FirewoodBuyerData>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (row is not Dictionary<string, object?> fields) throw new FormatException("Invalid firewood buyer.");
                var data = new FirewoodBuyerData { JobPath = EngineInputPath(fields, "jobPath"), BuyerPath = EngineInputPath(fields, "buyerPath"),
                    PaymentPath = EngineInputPath(fields, "paymentPath"), LodPath = EngineInputPath(fields, "lodPath") };
                if (!seen.Add(data.LodPath) || data.LodPath.Length != "JOBS/HouseWood1".Length
                    || !data.LodPath.StartsWith("JOBS/HouseWood", StringComparison.Ordinal)
                    || data.LodPath[data.LodPath.Length - 1] < '1' || data.LodPath[data.LodPath.Length - 1] > '4'
                    || !data.JobPath.StartsWith(data.LodPath + "/", StringComparison.Ordinal)
                    || !data.BuyerPath.StartsWith(data.LodPath + "/LOD/", StringComparison.Ordinal)
                    || !data.PaymentPath.StartsWith(data.BuyerPath + "/", StringComparison.Ordinal)
                    || !data.PaymentPath.EndsWith("/PayMoney", StringComparison.Ordinal))
                    throw new FormatException("Unsupported firewood buyer identity.");
                result.Add(data);
            }
            return result;
        }
    }
    internal sealed class FirewoodDeliveryData
    {
        internal static readonly string[] Fields = { "loadPath", "loadFsm", "groundPath", "groundFsm", "logs", "firewood", "mass", "bedScale", "unloaded", "unload", "bedPile", "groundPile", "groundMesh", "newGroundPile", "pilePrefab", "meshChild", "idle", "visual", "reset", "animate", "start" };
        internal readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
        internal string this[string key] => Values[key];
    }
    internal sealed class FirewoodBuyerData
    {
        public string JobPath = "", BuyerPath = "", PaymentPath = "", LodPath = "";
    }
}
