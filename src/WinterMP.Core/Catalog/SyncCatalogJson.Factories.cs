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
                var factory = new ReplacementPartFactoryData { Path = RequiredString(rule, "path"), Prefix = RequiredString(rule, "prefix"),
                    Fsm = rule.ContainsKey("fsm") ? RequiredString(rule, "fsm") : data["factoryFsm"] };
                if (!ValidScenePath(factory.Path) || factory.Fsm.IndexOfAny(new[] { ':', '/', '\\', '\r', '\n', '\0' }) >= 0)
                    throw new FormatException("Invalid replacement factory path or FSM.");
                if (rule.TryGetValue("bagOutput", out var bagOutput))
                {
                    if (bagOutput is not bool enabled) throw new FormatException("Invalid replacement bag output flag.");
                    factory.BagOutput = enabled;
                }
                if (rule.ContainsKey("spawnPointVariable"))
                {
                    factory.SpawnPointVariable = FactoryBindingName(rule, "spawnPointVariable");
                    if (factory.BagOutput) throw new FormatException("Replacement spawn point cannot also use a shopping bag.");
                }
                if (rule.ContainsKey("slotReference") || rule.ContainsKey("slotCount"))
                {
                    factory.SlotReference = RequiredString(rule, "slotReference");
                    if (!rule.TryGetValue("slotCount", out var slots)) throw new FormatException("Missing replacement slot count.");
                    factory.SlotCount = (byte)SlotNumber(slots, "slotCount", 1, PartSlotPolicy.MaxSlots);
                }
                if (rule.TryGetValue("removalLayer", out var removalLayer))
                    factory.RemovalLayer = SlotNumber(removalLayer, "removalLayer", 0, 31);
                if (rule.TryGetValue("fitPrerequisite", out var prerequisiteValue))
                {
                    if (prerequisiteValue is not Dictionary<string, object?> prerequisite || factory.SlotCount != 0)
                        throw new FormatException("Invalid replacement fitting prerequisite.");
                    var threshold = GetFloat(prerequisite, "minimumExclusive", float.NaN);
                    if (float.IsNaN(threshold) || float.IsInfinity(threshold))
                        throw new FormatException("Invalid replacement fitting threshold.");
                    factory.FitPrerequisite = new PartFitPrerequisiteData { State = RequiredString(prerequisite, "state"),
                        Reference = RequiredString(prerequisite, "reference"), Scalar = RequiredString(prerequisite, "scalar"),
                        Result = RequiredString(prerequisite, "result"), MinimumExclusive = threshold };
                }
                factory.Scalars = ReplacementStrings(rule, "scalars", 1, 8);
                if (rule.ContainsKey("alternatorDamageVariable"))
                {
                    factory.AlternatorDamageVariable = RequiredString(rule, "alternatorDamageVariable");
                    if (factory.AlternatorDamageVariable != "Damaged" || factory.SlotCount != 0
                        || (factory.Prefix != "VIN133" && factory.Prefix != "ALTERNATOR0"))
                        throw new FormatException("Unsupported replacement alternator damage binding.");
                }
                if (rule.ContainsKey("camProfileVariable"))
                {
                    factory.CamProfileVariable = RequiredString(rule, "camProfileVariable");
                    if (factory.CamProfileVariable != "CamProfile" || factory.SlotCount != 0
                        || Array.IndexOf(new[] { "VIN115", "CAMTUNEa0", "CAMTUNEb0", "CAMTUNEc0", "CAMTUNEd0" }, factory.Prefix) < 0)
                        throw new FormatException("Unsupported replacement cam profile.");
                }
                if (rule.TryGetValue("handRotation", out var rotationValue))
                {
                    if (rotationValue is not Dictionary<string, object?> rotation) throw new FormatException("Invalid hand rotation binding.");
                    factory.HandRotation = new PartHandRotationData { Path = RequiredString(rotation, "path"),
                        Fsm = RequiredString(rotation, "fsm"), Scalar = RequiredString(rotation, "scalar"),
                        BoltPath = RequiredString(rotation, "boltPath") };
                    if (!PartAttachmentPolicy.ValidPath(factory.HandRotation.Path) || factory.HandRotation.Path.Length == 0
                        || !PartAttachmentPolicy.ValidPath(factory.HandRotation.BoltPath) || factory.HandRotation.BoltPath.Length == 0
                        || Array.IndexOf(factory.Scalars, factory.HandRotation.Scalar) < 0 || factory.SlotCount != 0)
                        throw new FormatException("Invalid hand rotation paths or scalar.");
                }
                factory.InitActions = ReplacementStrings(rule, "initActions", 4, 32, false);
                factory.StatusActions = ReplacementStrings(rule, "statusActions", 4, 16, false);
                if (rule.TryGetValue("disabledInitActions", out var disabledValue))
                {
                    if (disabledValue is not List<object?> disabled || disabled.Count > factory.InitActions.Length)
                        throw new FormatException("Invalid disabled replacement initialization actions.");
                    var indices = new List<int>();
                    foreach (var indexValue in disabled)
                    {
                        int index = SlotNumber(indexValue, "disabledInitActions", 0, factory.InitActions.Length - 1);
                        if (indices.Contains(index) || factory.InitActions[index] != "GetChild")
                            throw new FormatException("Unsupported disabled replacement initialization action.");
                        indices.Add(index);
                    }
                    factory.DisabledInitActions = indices.ToArray();
                }
                if (!rule.TryGetValue("references", out var refs) || refs is not List<object?> references
                    || references.Count < 1 || references.Count > 4) throw new FormatException("Missing replacement references.");
                var targets = new HashSet<string>();
                foreach (var reference in references)
                {
                    if (reference is not Dictionary<string, object?> binding) throw new FormatException("Invalid replacement reference.");
                    string target = RequiredString(binding, "target"), source = RequiredString(binding, "source");
                    if (!targets.Add(target)) throw new FormatException("Duplicate replacement reference.");
                    bool enabled = true;
                    if (binding.TryGetValue("enabled", out var enabledValue))
                    {
                        if (enabledValue is not bool flag) throw new FormatException("Invalid replacement reference enabled flag.");
                        enabled = flag;
                    }
                    if (!enabled && target == data["installPointVariable"])
                        throw new FormatException("Replacement install-point reference must remain enabled.");
                    factory.References.Add(new ReplacementPartReference { Target = target, Source = source, Enabled = enabled });
                }
                if (rule.TryGetValue("handScrew", out var screwValue)) factory.HandScrew = ParsePartHandScrew(screwValue, factory);
                if (rule.TryGetValue("toolScrew", out var toolValue)) factory.ToolScrew = ParsePartToolScrew(toolValue, factory);
                if (rule.TryGetValue("beltVisual", out var beltValue)) factory.BeltVisual = ParsePartBeltVisual(beltValue, factory);
                if (rule.TryGetValue("distributorTiming", out var timingValue)) factory.DistributorTiming = ParsePartDistributorTiming(timingValue, factory);
                try { factory.Identity = new ReplacementPartRule(FactoryItemIdentity.FactoryId(factory.Path, factory.Fsm),
                    factory.Prefix, factory.Scalars.Length, Array.IndexOf(factory.Scalars, "Tightness"),
                    supportsBeltVisual: factory.BeltVisual != null, supportsCamProfile: factory.CamProfileVariable != null,
                    supportsAlternatorDamage: factory.AlternatorDamageVariable != null,
                    inertiaIndex: Array.IndexOf(factory.Scalars, "InertiaFactor")); }
                catch (ArgumentException e) { throw new FormatException("Invalid replacement identity.", e); }
                data.Factories.Add(factory);
            }
            try { new ReplacementPartReplica(data.IdentityRules(), new ItemSpawnLifecycle()); }
            catch (ArgumentException e) { throw new FormatException("Duplicate replacement factories.", e); }
            return data;
        }

        private static PartToolScrewData ParsePartToolScrew(object? value, ReplacementPartFactoryData factory)
        {
            if (value is not Dictionary<string, object?> obj || factory.HandScrew != null || factory.HandRotation != null
                || factory.Prefix != "SPRKPLUG0" || factory.SlotCount != 4 || factory.RemovalLayer != 12)
                throw new FormatException("Invalid tool screw profile.");
            var rule = new PartToolScrewData { Fsm = FactoryBindingName(obj, "fsm"), Scalar = FactoryBindingName(obj, "scalar"),
                ScratchVariable = FactoryBindingName(obj, "scratchVariable"), TightenState = PartProfileString(obj, "tightenState"),
                LoosenState = PartProfileString(obj, "loosenState"), PoseState = PartProfileString(obj, "poseState"),
                IdleState = PartProfileString(obj, "idleState"), ToolPath = RequiredString(obj, "toolPath"),
                ToolFsm = FactoryBindingName(obj, "toolFsm"), RaycastFsm = FactoryBindingName(obj, "raycastFsm") };
            if (rule.Scalar != "Tightness" || Array.IndexOf(factory.Scalars, rule.Scalar) < 0
                || !ValidScenePath(rule.ToolPath) || !rule.ToolPath.StartsWith("PLAYER/", StringComparison.Ordinal)
                || new HashSet<string> { rule.TightenState, rule.LoosenState, rule.PoseState, rule.IdleState }.Count != 4)
                throw new FormatException("Invalid tool screw states, scalar or player path.");
            return rule;
        }

        private static PartHandScrewData ParsePartHandScrew(object? value, ReplacementPartFactoryData factory)
        {
            if (value is not Dictionary<string, object?> obj || factory.SlotCount != 0 || factory.HandRotation != null)
                throw new FormatException("Invalid hand screw binding.");
            var screw = new PartHandScrewData { Fsm = PartProfileString(obj, "fsm"), Scalar = PartProfileString(obj, "scalar"),
                ScratchVariable = PartProfileString(obj, "scratchVariable"), RotationVariable = PartProfileString(obj, "rotationVariable"),
                TightenState = PartProfileString(obj, "tightenState"), LoosenState = PartProfileString(obj, "loosenState"),
                PoseState = PartProfileString(obj, "poseState"), InputState = PartProfileString(obj, "inputState"),
                WaitState = PartProfileString(obj, "waitState"), ToolState = PartProfileString(obj, "toolState"),
                InitState = PartProfileString(obj, "initState"), PickState = PartProfileString(obj, "pickState"),
                ReadyStates = ReplacementStrings(obj, "readyStates", 3, 3), Cooldown = GetFloat(obj, "cooldown", float.NaN) };
            if (Array.IndexOf(factory.Scalars, screw.Scalar) != Array.IndexOf(factory.Scalars, "Tightness")
                || screw.ScratchVariable == screw.RotationVariable || screw.Cooldown != .2f)
                throw new FormatException("Invalid hand screw scalar or cooldown.");
            var states = new HashSet<string> { screw.TightenState, screw.LoosenState, screw.PoseState, screw.InputState,
                screw.WaitState, screw.ToolState, screw.InitState, screw.PickState };
            if (states.Count != 8 || Array.IndexOf(screw.ReadyStates, screw.PickState) < 0
                || Array.IndexOf(screw.ReadyStates, screw.ToolState) < 0 || Array.IndexOf(screw.ReadyStates, screw.InputState) < 0)
                throw new FormatException("Hand screw states must be distinct with only input states ready.");
            return screw;
        }

        private static PartBeltVisualData ParsePartBeltVisual(object? value, ReplacementPartFactoryData factory)
        {
            if (value is not Dictionary<string, object?> obj || factory.Prefix != "FANBELT0" || factory.SlotCount != 0
                || factory.HandRotation != null || factory.HandScrew != null)
                throw new FormatException("Invalid belt visual binding.");
            var belt = new PartBeltVisualData { LooseMeshVariable = PartProfileString(obj, "looseMeshVariable"),
                MountVisualVariable = PartProfileString(obj, "mountVisualVariable"), VisualPath = PartProfileString(obj, "visualPath"),
                MeshPath = PartProfileString(obj, "meshPath"), RendererPath = PartProfileString(obj, "rendererPath"),
                ScaleBonePath = PartProfileString(obj, "scaleBonePath"), AnimationPath = PartProfileString(obj, "animationPath"),
                Fsm = PartProfileString(obj, "fsm"), ScaleVariable = PartProfileString(obj, "scaleVariable"),
                ScrollPath = PartProfileString(obj, "scrollPath"), ScrollFsm = PartProfileString(obj, "scrollFsm") };
            foreach (string path in new[] { belt.VisualPath, belt.MeshPath, belt.RendererPath, belt.ScaleBonePath, belt.AnimationPath })
                if (!PartAttachmentPolicy.ValidPath(path)) throw new FormatException("Invalid belt visual path.");
            if (!ValidScenePath(belt.ScrollPath) || !PartAttachmentPolicy.ValidPath(belt.ScrollPath))
                throw new FormatException("Invalid belt scroll scene path.");
            if (!belt.RendererPath.StartsWith(belt.MeshPath + "/", StringComparison.Ordinal)
                || !belt.ScaleBonePath.StartsWith(belt.MeshPath + "/", StringComparison.Ordinal)
                || belt.RendererPath == belt.ScaleBonePath || belt.AnimationPath == belt.MeshPath
                || belt.AnimationPath.StartsWith(belt.MeshPath + "/", StringComparison.Ordinal)
                || belt.MeshPath.StartsWith(belt.AnimationPath + "/", StringComparison.Ordinal))
                throw new FormatException("Belt renderer and bone must be inside the mesh without native animation logic.");
            return belt;
        }

        private static PartDistributorTimingData ParsePartDistributorTiming(object? value, ReplacementPartFactoryData factory)
        {
            if (value is not Dictionary<string, object?> obj || factory.Prefix != "VIN131" || factory.SlotCount != 0
                || factory.HandRotation != null || factory.HandScrew != null || factory.BeltVisual != null)
                throw new FormatException("Invalid distributor timing binding.");
            var timing = new PartDistributorTimingData { Fsm = PartProfileString(obj, "fsm"), Scalar = PartProfileString(obj, "scalar"),
                MeshPath = PartProfileString(obj, "meshPath"), MeshVariable = PartProfileString(obj, "meshVariable"),
                MountVariable = PartProfileString(obj, "mountVariable"), RotationVariable = PartProfileString(obj, "rotationVariable"),
                ScrollVariable = PartProfileString(obj, "scrollVariable"), TightnessVariable = PartProfileString(obj, "tightnessVariable"),
                ClockwiseState = PartProfileString(obj, "clockwiseState"), CounterwiseState = PartProfileString(obj, "counterwiseState"),
                WaitState = PartProfileString(obj, "waitState"), InputState = PartProfileString(obj, "inputState"),
                PickState = PartProfileString(obj, "pickState"), TightnessState = PartProfileString(obj, "tightnessState"),
                BindState = PartProfileString(obj, "bindState"), ToolState = PartProfileString(obj, "toolState"),
                DelayState = PartProfileString(obj, "delayState"), InstalledPoseState = PartProfileString(obj, "installedPoseState"),
                ReadyStates = ReplacementStrings(obj, "readyStates", 3, 3), Cooldown = GetFloat(obj, "cooldown", float.NaN) };
            if (!PartAttachmentPolicy.ValidPath(timing.MeshPath) || Array.IndexOf(factory.Scalars, timing.Scalar) < 0
                || timing.Scalar == "Wear" || timing.Scalar == "Tightness" || timing.MeshVariable == timing.MountVariable
                || timing.Cooldown != .01f)
                throw new FormatException("Invalid distributor timing path, scalar or cooldown.");
            var scratch = new HashSet<string> { timing.RotationVariable, timing.ScrollVariable, timing.TightnessVariable };
            var states = new HashSet<string> { timing.ClockwiseState, timing.CounterwiseState, timing.WaitState, timing.InputState,
                timing.PickState, timing.TightnessState, timing.BindState, timing.ToolState, timing.DelayState };
            if (scratch.Count != 3 || states.Count != 9 || Array.IndexOf(timing.ReadyStates, timing.PickState) < 0
                || Array.IndexOf(timing.ReadyStates, timing.ToolState) < 0 || Array.IndexOf(timing.ReadyStates, timing.InputState) < 0)
                throw new FormatException("Distributor timing states and scratch variables must be distinct with only input states ready.");
            return timing;
        }

        private static string PartProfileString(Dictionary<string, object?> obj, string key)
        {
            string value = RequiredString(obj, key);
            if (value.Trim().Length == 0 || value.Length > 128) throw new FormatException("Invalid part profile " + key);
            foreach (char c in value) if (char.IsControl(c)) throw new FormatException("Invalid part profile " + key);
            return value;
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
            var supplyIds = new HashSet<uint>();
            bool hasBulbContents = false;
            foreach (var entry in entries)
            {
                if (entry is not Dictionary<string, object?> rule) throw new FormatException("Invalid package factory.");
                var factory = new PackageFactoryData { Fsm = PartProfileString(rule, "fsm"),
                    Prefix = RequiredString(rule, "prefix"), ContentsPath = RequiredString(rule, "contentsPath"),
                    Path = rule.ContainsKey("path") ? RequiredString(rule, "path") : data["factoryPath"],
                    ContentsFsm = rule.ContainsKey("contentsFsm") ? FactoryBindingName(rule, "contentsFsm") : data["contentsFsm"],
                    ItemName = rule.ContainsKey("itemName") ? RequiredString(rule, "itemName") : data["itemName"],
                    OpenState = rule.ContainsKey("openState") ? PartProfileString(rule, "openState") : data["openState"] };
                if (rule.ContainsKey("contentsSpawnPointVariable"))
                    factory.ContentsSpawnPointVariable = FactoryBindingName(rule, "contentsSpawnPointVariable");
                if (rule.TryGetValue("fixedCapacity", out var fixedCapacity))
                {
                    if (fixedCapacity is not bool fixedValue) throw new FormatException("Invalid package fixed capacity flag.");
                    factory.FixedCapacity = fixedValue;
                }
                if (factory.FixedCapacity != (factory.ContentsSpawnPointVariable != null))
                    throw new FormatException("Direct contents require the audited fixed-capacity box profile.");
                if (rule.TryGetValue("supplyContents", out var supplyValue))
                {
                    if (supplyValue is not Dictionary<string, object?> supply || !factory.FixedCapacity)
                        throw new FormatException("Ordinary supply contents require a fixed-capacity box.");
                    factory.SupplyContents = new SupplyContentsData { Prefix = RequiredString(supply, "prefix"),
                        ItemName = PartProfileString(supply, "itemName"), ReadyState = PartProfileString(supply, "readyState") };
                    if (supply.ContainsKey("retirementVariable"))
                        factory.SupplyContents.RetirementVariable = PartProfileString(supply, "retirementVariable");
                    if (factory.SupplyContents.RetirementVariable != "Consumed" && factory.SupplyContents.RetirementVariable != "Destroy")
                        throw new FormatException("Unsupported supply retirement flag.");
                    if (!FactoryItemIdentity.IsNativeId(factory.SupplyContents.Prefix + "1", factory.SupplyContents.Prefix)
                        || factory.SupplyContents.Prefix == factory.Prefix
                        || !supplyIds.Add(FactoryItemIdentity.FactoryId(factory.ContentsPath, factory.ContentsFsm)))
                        throw new FormatException("Invalid supply contents identity.");
                }
                if (rule.TryGetValue("loadClampIndex", out var clampIndex))
                    factory.LoadClampIndex = SlotNumber(clampIndex, "loadClampIndex", 1, 2);
                if (rule.TryGetValue("bulbContents", out var bulbValue))
                {
                    if (bulbValue is not Dictionary<string, object?> bulb || hasBulbContents || !factory.FixedCapacity || factory.SupplyContents != null
                        || !supplyIds.Add(FactoryItemIdentity.FactoryId(factory.ContentsPath, factory.ContentsFsm)))
                        throw new FormatException("Invalid transient bulb contents.");
                    hasBulbContents = true;
                    factory.BulbContents = new BulbContentsData();
                    foreach (string key in new[] { "prefab", "itemName", "fsm", "wear", "initial", "ready", "idle" })
                        factory.BulbContents.Names.Add(key, PartProfileString(bulb, key));
                    if (factory.BulbContents["initial"] == factory.BulbContents["ready"])
                        throw new FormatException("Bulb initialization and ready states must differ.");
                }
                if (!factory.FixedCapacity && factory.LoadClampIndex != 1)
                    throw new FormatException("Only fixed-capacity boxes have a load clamp.");
                if (!rule.TryGetValue("capacity", out var capacity)) throw new FormatException("Missing package capacity.");
                factory.Capacity = (ushort)SlotNumber(capacity, "capacity", 1, 64);
                if (factory.BulbContents != null && factory.Capacity != 1) throw new FormatException("Native bulb boxes contain exactly one bulb.");
                if (factory.Fsm.IndexOfAny(new[] { ':', '/', '\r', '\n', '\0' }) >= 0
                    || !ValidScenePath(factory.Path) || !ValidScenePath(factory.ContentsPath)) throw new FormatException("Invalid package factory binding.");
                identities.Add(new PackageIdentityRule(FactoryItemIdentity.FactoryId(factory.Path, factory.Fsm),
                    factory.Prefix, factory.ContentsPath, factory.Capacity));
                data.Factories.Add(factory);
            }
            try { data.Identities = new PackageIdentityResolver(identities); }
            catch (ArgumentException e) { throw new FormatException("Invalid package identities.", e); }
            return data;
        }

        private static string FactoryBindingName(Dictionary<string, object?> obj, string key)
        {
            string value = PartProfileString(obj, key);
            if (value.IndexOfAny(new[] { ':', '/', '\\' }) >= 0) throw new FormatException("Invalid factory binding: " + key);
            return value;
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
        public string Path = string.Empty, Fsm = string.Empty, Prefix = string.Empty, ContentsPath = string.Empty,
            ContentsFsm = string.Empty, ItemName = string.Empty, OpenState = string.Empty;
        public string? ContentsSpawnPointVariable;
        public bool FixedCapacity;
        public int LoadClampIndex = 1;
        public SupplyContentsData? SupplyContents;
        public BulbContentsData? BulbContents;
        public ushort Capacity;
    }

    internal sealed class SupplyContentsData
    {
        public string Prefix = string.Empty, ItemName = string.Empty, ReadyState = string.Empty;
        public string RetirementVariable = "Destroy";
    }

    internal sealed class BulbContentsData
    {
        internal readonly Dictionary<string, string> Names = new Dictionary<string, string>();
        internal string this[string key] => Names[key];
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
            "slotInstallerReferenceVariable", "slotInstallerIndexVariable", "slotAllowVariable", "slotStopEvent", "replicaRepairVariable" };
        public readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public string this[string key] => Bindings[key];
        public readonly List<ReplacementPartFactoryData> Factories = new List<ReplacementPartFactoryData>();
        public IEnumerable<ReplacementPartRule> IdentityRules() { foreach (var rule in Factories) yield return rule.Identity; }
    }
    internal sealed class ReplacementPartFactoryData
    {
        public string Path = string.Empty, Prefix = string.Empty, Fsm = string.Empty;
        public bool BagOutput;
        public string? SpawnPointVariable;
        public string SlotReference = string.Empty;
        public byte SlotCount;
        public int RemovalLayer = 19;
        public PartHandRotationData? HandRotation;
        public PartHandScrewData? HandScrew;
        public PartToolScrewData? ToolScrew;
        public PartBeltVisualData? BeltVisual;
        public string? CamProfileVariable;
        public string? AlternatorDamageVariable;
        public PartDistributorTimingData? DistributorTiming;
        public PartFitPrerequisiteData? FitPrerequisite;
        public string[] Scalars = new string[0], InitActions = new string[0], StatusActions = new string[0];
        public int[] DisabledInitActions = new int[0];
        public readonly List<ReplacementPartReference> References = new List<ReplacementPartReference>();
        public ReplacementPartRule Identity = null!;
    }
    internal sealed class ReplacementPartReference
    {
        public string Target = string.Empty, Source = string.Empty;
        public bool Enabled = true;
    }

    internal sealed class PartToolScrewData
    {
        public string Fsm = string.Empty, Scalar = string.Empty, ScratchVariable = string.Empty,
            TightenState = string.Empty, LoosenState = string.Empty, PoseState = string.Empty, IdleState = string.Empty,
            ToolPath = string.Empty, ToolFsm = string.Empty, RaycastFsm = string.Empty;
    }

    internal sealed class PartHandRotationData
    {
        public string Path = string.Empty, Fsm = string.Empty, Scalar = string.Empty, BoltPath = string.Empty;
    }

    internal sealed class PartHandScrewData
    {
        public string Fsm = string.Empty, Scalar = string.Empty, ScratchVariable = string.Empty, RotationVariable = string.Empty;
        public string TightenState = string.Empty, LoosenState = string.Empty, PoseState = string.Empty, InputState = string.Empty;
        public string WaitState = string.Empty, ToolState = string.Empty, InitState = string.Empty, PickState = string.Empty;
        public string[] ReadyStates = new string[0];
        public float Cooldown;
    }

    internal sealed class PartFitPrerequisiteData
    {
        public string State = string.Empty, Reference = string.Empty, Scalar = string.Empty, Result = string.Empty;
        public float MinimumExclusive;
    }

    internal sealed class PartDistributorTimingData
    {
        public string Fsm = string.Empty, Scalar = string.Empty, MeshPath = string.Empty;
        public string MeshVariable = string.Empty, MountVariable = string.Empty, RotationVariable = string.Empty;
        public string ScrollVariable = string.Empty, TightnessVariable = string.Empty;
        public string ClockwiseState = string.Empty, CounterwiseState = string.Empty, WaitState = string.Empty, InputState = string.Empty;
        public string PickState = string.Empty, TightnessState = string.Empty, BindState = string.Empty;
        public string ToolState = string.Empty, DelayState = string.Empty, InstalledPoseState = string.Empty;
        public string[] ReadyStates = new string[0];
        public float Cooldown;
    }

    internal sealed class PartBeltVisualData
    {
        public string LooseMeshVariable = string.Empty, MountVisualVariable = string.Empty, VisualPath = string.Empty;
        public string MeshPath = string.Empty, RendererPath = string.Empty, ScaleBonePath = string.Empty, AnimationPath = string.Empty;
        public string Fsm = string.Empty, ScaleVariable = string.Empty, ScrollPath = string.Empty, ScrollFsm = string.Empty;
    }

}
