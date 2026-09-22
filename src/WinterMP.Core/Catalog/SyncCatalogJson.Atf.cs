using System;
using System.Collections.Generic;
using WinterMP.Net;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static AtfRefillData ParseAtfRefill(object? value)
        {
            if (value is not Dictionary<string, object?> obj) throw new FormatException("Invalid ATF refill catalog.");
            var result = new AtfRefillData();
            foreach (string key in new[] { "prefix", "prefabName", "itemName", "emptyName", "use", "data", "trigger", "particle", "id", "fluid", "consumed", "owner", "uniqueFluid", "pouring", "mass", "getCan", "copyToChild", "copyToRoot", "save", "empty", "emptyCheck", "emptyEvent", "dataFsm", "capFsm", "fillFsm", "gaugeFsm", "rotation", "step", "mesh", "capGui", "up", "down", "open", "closed", "wait", "gaugeState", "gaugeTarget", "gaugeFluid", "gaugeMax", "gaugeScale", "pour", "stop", "fillIdle", "fillCheck", "fillGearbox", "fillCapacity", "fillOil", "fillPouring", "fillName", "fillGui", "oil", "oilMax", "activePart", "installed", "assembly", "type", "mountReady", "bottleTrigger", "bottleFluid", "bottlePouring", "capTrigger" }) result.Names.Add(key, RpmName(obj, key));
            foreach (string key in new[] { "rootPath", "mountPath", "capPath", "fillPath", "gaugePath" }) result.Names.Add(key, RpmPath(obj, key));
            result.Names.Add("factoryIdentity", RequiredString(obj, "factoryIdentity"));
            if (result["prefix"] != "atfoil0" || result["factoryIdentity"] != "Spawner/CreateItems::ATFOil")
                throw new FormatException("ATF native identity differs from the protocol.");
            return result;
        }
    }

    internal sealed class AtfRefillData
    {
        internal readonly Dictionary<string, string> Names = new Dictionary<string, string>();
        internal string this[string key] => Names[key];
        internal uint FactoryId => StableHash.Fnv1a32(this["factoryIdentity"]);
    }
}
