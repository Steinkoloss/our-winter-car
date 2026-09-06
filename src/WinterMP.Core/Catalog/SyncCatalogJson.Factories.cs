using System;
using System.Collections.Generic;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static ReplacementPartsData ParseReplacementParts(object? value)
        {
            if (value is not Dictionary<string, object?> obj || !obj.TryGetValue("factories", out var list)
                || list is not List<object?> entries || entries.Count == 0 || entries.Count > 64)
                throw new FormatException("Invalid replacement parts.");
            var data = new ReplacementPartsData();
            foreach (string key in ReplacementPartsData.RequiredBindings) data.Bindings.Add(key, RequiredString(obj, key));
            if (!ValidScenePath(data["slotDatabasePath"])) throw new FormatException("Invalid replacement slot database path.");
            foreach (var entry in entries)
            {
                if (entry is not Dictionary<string, object?> rule) throw new FormatException("Invalid replacement factory.");
                var factory = new ReplacementPartFactoryData { Path = RequiredString(rule, "path"), Prefix = RequiredString(rule, "prefix") };
                if (!ValidScenePath(factory.Path)) throw new FormatException("Invalid replacement factory path.");
                if (rule.ContainsKey("slotReference") || rule.ContainsKey("slotCount"))
                {
                    factory.SlotReference = RequiredString(rule, "slotReference");
                    if (!rule.TryGetValue("slotCount", out var slots)) throw new FormatException("Missing replacement slot count.");
                    factory.SlotCount = (byte)SlotNumber(slots, "slotCount", 1, PartSlotPolicy.MaxSlots);
                }
                factory.Scalars = ReplacementStrings(rule, "scalars", 1, 8);
                factory.InitActions = ReplacementStrings(rule, "initActions", 4, 32, false);
                factory.StatusActions = ReplacementStrings(rule, "statusActions", 4, 16, false);
                if (!rule.TryGetValue("references", out var refs) || refs is not List<object?> references
                    || references.Count < 1 || references.Count > 4) throw new FormatException("Missing replacement references.");
                var targets = new HashSet<string>();
                foreach (var reference in references)
                {
                    if (reference is not Dictionary<string, object?> binding) throw new FormatException("Invalid replacement reference.");
                    string target = RequiredString(binding, "target"), source = RequiredString(binding, "source");
                    if (!targets.Add(target)) throw new FormatException("Duplicate replacement reference.");
                    factory.References.Add(new ReplacementPartReference { Target = target, Source = source });
                }
                try { factory.Identity = new ReplacementPartRule(FactoryItemIdentity.FactoryId(factory.Path, data["factoryFsm"]),
                    factory.Prefix, factory.Scalars.Length, Array.IndexOf(factory.Scalars, "Tightness")); }
                catch (ArgumentException e) { throw new FormatException("Invalid replacement identity.", e); }
                data.Factories.Add(factory);
            }
            try { new ReplacementPartReplica(data.IdentityRules(), new ItemSpawnLifecycle()); }
            catch (ArgumentException e) { throw new FormatException("Duplicate replacement factories.", e); }
            return data;
        }

        private static string[] ReplacementStrings(Dictionary<string, object?> obj, string key, int min, int max, bool unique = true)
        {
            if (!obj.TryGetValue(key, out var value) || value is not List<object?> list || list.Count < min || list.Count > max)
                throw new FormatException("Invalid replacement " + key);
            var result = new string[list.Count]; var seen = new HashSet<string>();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is not string name || string.IsNullOrEmpty(name) || name.Length > 128
                    || (unique && !seen.Add(name))) throw new FormatException("Invalid replacement " + key);
                result[i] = name;
            }
            return result;
        }

        private static PartIdentityData ParsePartIdentity(object? value)
        {
            if (value is not Dictionary<string, object?> obj) throw new FormatException("Invalid part identity bindings.");
            var data = new PartIdentityData();
            foreach (string key in PartIdentityData.RequiredBindings) data.Bindings.Add(key, RequiredString(obj, key));
            if (data["assemblyKeyVariable"] == data["positionKeyVariable"]
                || data["assemblyKeySuffix"] == data["positionKeySuffix"])
                throw new FormatException("Part save identity proofs must be distinct.");
            data.InitializingStates = SlotStrings(obj, "initializingStates", 3);
            return data;
        }

        private static PartsPackagesData ParsePartsPackages(object? value)
        {
            if (value is not Dictionary<string, object?> obj || !obj.TryGetValue("factories", out var list)
                || list is not List<object?> entries || entries.Count == 0 || entries.Count > 64)
                throw new FormatException("Invalid parts packages.");
            var data = new PartsPackagesData();
            foreach (string key in PartsPackagesData.RequiredBindings) data.Bindings.Add(key, RequiredString(obj, key));
            if (!ValidScenePath(data["factoryPath"])) throw new FormatException("Invalid package factory path.");
            data.ReadyStates = SlotStrings(obj, "readyStates", 4);
            data.OpenReadyStates = SlotStrings(obj, "openReadyStates", 2);
            foreach (string state in data.OpenReadyStates)
                if (Array.IndexOf(data.ReadyStates, state) < 0 || state == data["emptyState"])
                    throw new FormatException("Invalid package opening ready state.");
            if (data["itemIdleState"] == data["emptyState"] || Array.IndexOf(data.ReadyStates, data["itemIdleState"]) < 0
                || Array.IndexOf(data.ReadyStates, data["emptyState"]) < 0)
                throw new FormatException("Package idle/empty states must be distinct ready states.");
            var identities = new List<PackageIdentityRule>();
            foreach (var entry in entries)
            {
                if (entry is not Dictionary<string, object?> rule) throw new FormatException("Invalid package factory.");
                var factory = new PackageFactoryData { Fsm = RequiredString(rule, "fsm"),
                    Prefix = RequiredString(rule, "prefix"), ContentsPath = RequiredString(rule, "contentsPath") };
                if (!rule.TryGetValue("capacity", out var capacity)) throw new FormatException("Missing package capacity.");
                factory.Capacity = (ushort)SlotNumber(capacity, "capacity", 1, 64);
                if (factory.Fsm.IndexOfAny(new[] { ':', '/', '\r', '\n', '\0' }) >= 0
                    || !ValidScenePath(factory.ContentsPath)) throw new FormatException("Invalid package factory binding.");
                identities.Add(new PackageIdentityRule(FactoryItemIdentity.FactoryId(data["factoryPath"], factory.Fsm),
                    factory.Prefix, factory.ContentsPath, factory.Capacity));
                data.Factories.Add(factory);
            }
            try { data.Identities = new PackageIdentityResolver(identities); }
            catch (ArgumentException e) { throw new FormatException("Invalid package identities.", e); }
            return data;
        }

        private static TrophyFactoriesData ParseTrophyFactories(object? value)
        {
            if (value is not Dictionary<string, object?> obj || !obj.TryGetValue("factories", out var list)
                || list is not List<object?> entries || entries.Count == 0 || entries.Count > 64)
                throw new FormatException("Invalid trophy factories.");
            var data = new TrophyFactoriesData();
            foreach (string key in TrophyFactoriesData.RequiredBindings) data.Bindings.Add(key, RequiredString(obj, key));
            var ids = new HashSet<uint>(); var prefixes = new HashSet<string>(); var prefabs = new HashSet<string>();
            foreach (var entry in entries)
            {
                if (entry is not Dictionary<string, object?> rule) throw new FormatException("Invalid trophy factory.");
                var factory = new TrophyFactoryData { Path = RequiredString(rule, "path"), Fsm = RequiredString(rule, "fsm"),
                    Prefix = RequiredString(rule, "prefix"), PrefabName = RequiredString(rule, "prefabName"),
                    ItemName = RequiredString(rule, "itemName") };
                if (!ValidScenePath(factory.Path) || factory.Fsm.IndexOfAny(new[] { ':', '/', '\r', '\n' }) >= 0
                    || !ids.Add(WinterMP.Net.Sync.FactoryItemIdentity.FactoryId(factory.Path, factory.Fsm))
                    || !prefixes.Add(factory.Prefix) || !prefabs.Add(factory.PrefabName))
                    throw new FormatException("Invalid/duplicate trophy factory identity.");
                foreach (char c in factory.Prefix)
                    if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')))
                        throw new FormatException("Trophy prefixes must be alphabetic, before the native counter.");
                if (factory.Prefix.Length > 100 || factory.ItemName.Length > 128 || factory.PrefabName.Length > 128)
                    throw new FormatException("Trophy names exceed the manifest limit.");
                data.Factories.Add(factory);
            }
            return data;
        }

    }

    internal sealed class TrophyFactoriesData
    {
        public static readonly string[] RequiredBindings = { "prefabVariable", "prefixVariable", "outputVariable",
            "idVariable", "createState", "factoryIdleState", "itemFsm", "itemIdVariable", "itemInitState",
            "itemReadyState", "itemLoadState", "itemSaveState" };
        public readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public string this[string key] => Bindings[key];
        public readonly List<TrophyFactoryData> Factories = new List<TrophyFactoryData>();
    }

    internal sealed class TrophyFactoryData
    {
        public string Path = string.Empty, Fsm = string.Empty, Prefix = string.Empty;
        public string PrefabName = string.Empty, ItemName = string.Empty;
    }

    internal sealed class PartsPackagesData
    {
        public static readonly string[] RequiredBindings = { "factoryPath", "itemFsm", "itemIdVariable",
            "contentsVariable", "quantityVariable", "openState", "garbageState", "garbageEvent",
            "saveEvent", "saveState", "deleteEvent", "deleteState", "itemIdleState", "emptyState",
            "prefabVariable", "spawnerVariable", "outputVariable", "idVariable", "createState", "factoryIdleState",
            "ownerVariable", "capacityVariable", "itemName", "emptyName", "itemInitState", "loadCreateState",
            "contentsFsm", "contentsEvent", "contentsCountVariable", "contentsCounterVariable", "minimumWearVariable",
            "spawnPointVariable", "checkQuantityState", "openFeedbackState" };
        public readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public string this[string key] => Bindings[key];
        public string[] ReadyStates = new string[0], OpenReadyStates = new string[0];
        public readonly List<PackageFactoryData> Factories = new List<PackageFactoryData>();
        public PackageIdentityResolver Identities = null!;
    }

    internal sealed class PackageFactoryData
    {
        public string Fsm = string.Empty, Prefix = string.Empty, ContentsPath = string.Empty;
        public ushort Capacity;
    }

    internal sealed class PartIdentityData
    {
        public static readonly string[] RequiredBindings = { "fsm", "idVariable", "assemblyVariable",
            "consumedVariable", "assemblyKeyVariable", "positionKeyVariable", "assemblyKeySuffix", "positionKeySuffix" };
        public readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public string this[string key] => Bindings[key];
        public string[] InitializingStates = new string[0];
    }

    internal sealed class ReplacementPartsData
    {
        public static readonly string[] RequiredBindings = { "factoryFsm", "prefabVariable", "outputVariable", "idVariable",
            "createState", "loadCreateState", "factoryIdleState", "itemFsm", "itemInitState", "itemStatusState", "itemIdleState",
            "itemStopState", "itemIdVariable", "assemblyVariable", "installedVariable", "consumedVariable", "garbageState",
            "garbageEvent", "saveState", "deleteState", "loadState", "freshEvent", "boltsVariable",
            "installPointVariable", "mountPartVariable", "mountPointVariable", "mountInstalledVariable",
            "fitEvent", "fitCheckState", "fitOwnerVariable", "fitMountCheckEvent", "fitMountIdleState", "fitAllowState", "fitNearState",
            "fitFarState", "fitConfirmEvent", "fitCancelEvent", "fitInstallState", "fitInstallEvent", "fitDistanceVariable", "fitToleranceVariable",
            "removeColliderVariable", "removeTightnessState", "removeTightnessVariable", "removeUnboltedState", "removeBoltedState", "removeMouseOffState", "removeMouseOverState",
            "removeState", "removeEvent", "removeAllowState", "removeRecheckEvent", "removeHandPath", "removeHandFsm", "removeHandIdleState",
            "slotDatabasePath", "slotDatabaseVariable", "slotReferenceVariable", "slotInstallerFsm", "slotInstallerIdleState",
            "slotInstallerReferenceVariable", "slotInstallerIndexVariable", "slotAllowVariable", "slotStopEvent" };
        public readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public string this[string key] => Bindings[key];
        public readonly List<ReplacementPartFactoryData> Factories = new List<ReplacementPartFactoryData>();
        public IEnumerable<ReplacementPartRule> IdentityRules() { foreach (var rule in Factories) yield return rule.Identity; }
    }
    internal sealed class ReplacementPartFactoryData
    {
        public string Path = string.Empty, Prefix = string.Empty;
        public string SlotReference = string.Empty;
        public byte SlotCount;
        public string[] Scalars = new string[0], InitActions = new string[0], StatusActions = new string[0];
        public readonly List<ReplacementPartReference> References = new List<ReplacementPartReference>();
        public ReplacementPartRule Identity = null!;
    }
    internal sealed class ReplacementPartReference
    {
        public string Target = string.Empty, Source = string.Empty;
    }

}
