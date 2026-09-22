using System;
using System.Collections.Generic;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static MooseChopData ParseMooseChop(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid moose chop catalog.");
            var data = new MooseChopData();
            foreach (string key in MooseChopData.Required) data.Bindings.Add(key, RequiredString(fields, key));
            var paths = new HashSet<string>();
            foreach (string key in new[] { "path", "mover", "hitPath", "axe", "axeCollider" })
                if (!ValidScenePath(data[key])) throw new FormatException("Invalid moose path.");
            for (int i = 1; i < 11; i++)
                if (!ValidScenePath(data["body" + i]) || !paths.Add(data["body" + i])) throw new FormatException("Invalid moose body map.");
            return data;
        }

        private static MooseMeatData ParseMooseMeat(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid moose meat catalog.");
            var data = new MooseMeatData();
            foreach (string key in MooseMeatData.Required) data.Bindings.Add(key, RequiredString(fields, key));
            if (!ValidScenePath(data["path"]) || data["prefix"] != data["prefabName"]
                || !FactoryItemIdentity.IsNativeId(data["prefix"] + "1", data["prefix"]))
                throw new FormatException("Invalid native meat factory identity.");
            return data;
        }
    }
    internal sealed class MooseChopData
    {
        public static readonly string[] Required = { "path", "detached", "mover", "hitPath", "hitFsm", "death", "hitIdle", "fsm", "rear", "idle", "compare", "pieces", "create", "cooldown", "sound", "object", "spawnpoint", "spawner", "axe", "axeCollider", "factorySpawnpoint", "body1", "body2", "body3", "body4", "body5", "body6", "body7", "body8", "body9", "body10" };
        public readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public string this[string key] => Bindings[key];
    }
    internal sealed class MooseMeatData
    {
        public static readonly string[] Required = { "path", "factoryFsm", "prefix", "prefab", "output", "factoryId",
            "create", "loadCreate", "factoryIdle", "prefabName", "use", "fire", "id", "owner", "condition", "type",
            "raw", "rotten", "grilled", "charred", "spoiled", "waitPlayer", "waitButton", "eat", "destroy", "deleted" };
        public readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public string this[string key] => Bindings[key];
        public uint FactoryId => FactoryItemIdentity.FactoryId(this["path"], this["factoryFsm"]);
    }
}
