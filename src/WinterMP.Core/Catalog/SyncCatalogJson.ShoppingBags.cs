using System;
using System.Collections.Generic;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static ShoppingBagsData ParseShoppingBags(object? value)
        {
            if (value is not Dictionary<string, object?> obj || !obj.TryGetValue("factories", out var list)
                || list is not List<object?> entries || entries.Count == 0 || entries.Count > 8)
                throw new FormatException("Invalid shopping bag factories.");
            var data = new ShoppingBagsData();
            foreach (string key in ShoppingBagsData.RequiredBindings) data.Bindings.Add(key, RequiredString(obj, key));
            data.ReadyStates = ReplacementStrings(obj, "readyStates", 1, 16);
            data.SpillFactoryPaths = ReplacementStrings(obj, "spillFactoryPaths", 1, 8);
            foreach (string path in data.SpillFactoryPaths)
                if (!ValidScenePath(path)) throw new FormatException("Invalid shopping bag spill factory path.");
            if (!ValidScenePath(data["pickupPath"])) throw new FormatException("Invalid shopping bag pickup path.");
            if (Array.IndexOf(data.ReadyStates, data["itemIdleState"]) < 0
                || data["oneState"] == data["allState"] || data["oneEvent"] == data["allEvent"]
                || data["keysReference"] == data["valuesReference"])
                throw new FormatException("Invalid shopping bag state or inventory bindings.");
            var ids = new HashSet<uint>(); var prefixes = new HashSet<string>(); var contents = new HashSet<string>();
            foreach (var entry in entries)
            {
                if (entry is not Dictionary<string, object?> rule) throw new FormatException("Invalid shopping bag factory.");
                var factory = new ShoppingBagFactoryData { Path = RequiredString(rule, "path"),
                    Prefix = RequiredString(rule, "prefix"), ContentsPath = RequiredString(rule, "contentsPath") };
                if (!ValidScenePath(factory.Path) || !ValidScenePath(factory.ContentsPath)
                    || !ids.Add(FactoryItemIdentity.FactoryId(factory.Path, data["factoryFsm"]))
                    || !prefixes.Add(factory.Prefix) || !contents.Add(factory.ContentsPath)
                    || !FactoryItemIdentity.IsNativeId(factory.Prefix + "1", factory.Prefix))
                    throw new FormatException("Invalid or duplicate shopping bag identity.");
                foreach (char ch in factory.Prefix)
                    if (!((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z')))
                        throw new FormatException("Shopping bag prefixes must be alphabetic.");
                data.Factories.Add(factory);
            }
            return data;
        }
    }

    internal sealed class ShoppingBagsData
    {
        public static readonly string[] RequiredBindings = { "factoryFsm", "prefabVariable", "prefixVariable",
            "outputVariable", "idVariable", "createState", "loadCreateState", "factoryIdleState", "itemFsm",
            "itemIdVariable", "itemInitState", "itemIdentityState", "itemLoadState", "itemName", "itemIdleState",
            "confirmState", "oneState", "allState", "garbageState", "garbageEvent", "saveState", "saveEvent", "deleteState", "consumedEvent",
            "conditionVariable", "consumedVariable", "ownerVariable", "contentsVariable", "contentsFsm",
            "currentBagVariable", "keysReference", "valuesReference", "oneEvent", "allEvent",
            "contentsIdleState", "contentsStartState", "spillCreateState", "spillIdleState", "spillPrefabVariable",
            "spillOutputVariable", "spillProductVariable", "spillSpawnPointVariable", "spillEvent", "pickupPath", "pickupFsm", "pickupEntryState",
            "pickupIdleState", "pickupHeldState", "pickupDropState", "pickupDropEvent", "pickupObjectVariable",
            "pickupJointVariable", "pickupPivotVariable", "pickupEmptyVariable" };
        public readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public string this[string key] => Bindings[key];
        public string[] ReadyStates = new string[0], SpillFactoryPaths = new string[0];
        public readonly List<ShoppingBagFactoryData> Factories = new List<ShoppingBagFactoryData>();
    }

    internal sealed class ShoppingBagFactoryData
    {
        public string Path = string.Empty, Prefix = string.Empty, ContentsPath = string.Empty;
    }
}
