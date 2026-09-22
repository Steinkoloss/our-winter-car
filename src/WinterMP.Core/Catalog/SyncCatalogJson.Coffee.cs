using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static CoffeeData ParseCoffee(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid home-coffee catalog.");
            var data = new CoffeeData();
            foreach (string key in new[] { "pot", "cup", "cap", "target", "grounds", "water", "surface", "cupSurface", "cupTarget", "capMesh", "functions", "sound", "data", "use", "empty", "fire", "pour", "groundFsm", "waterFsm", "drink", "checkDrink", "fill", "delay", "open", "close", "waitClosed", "waitOpen", "packetName", "packetPrefix", "packetPrefab", "packetReady", "packetEmpty", "packetId", "waterVar", "groundVar", "coffeeVar", "caffeineVar", "capVar", "volumeVar", "groundMaxVar", "saveTagVar", "potSaveTag", "cupSaveTag" })
                data.Names.Add(key, EngineInputPath(fields, key));
            return data;
        }
    }
    internal sealed class CoffeeData
    {
        internal readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);
        internal string this[string key] => Names[key];
    }
}
