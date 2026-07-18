using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Sync;
using WinterMP.Net;

namespace WinterMP.Core.Catalog
{
    /// <summary>
    /// Curated FSM descriptors (catalog/sync-catalog.json). Vehicle controls,
    /// house switches, ignitions, and starters are data-driven.
    /// </summary>
    public static class SyncCatalog
    {
        public const string FileName = "sync-catalog.json";

        public static bool Loaded { get; private set; }
        public static uint Hash { get; private set; }

        /// <summary>Loads the catalog on first use (session connect / handshake), not at plugin Awake.</summary>
        public static void EnsureLoaded()
        {
            if (Loaded) return;
            Load();
        }

        public static int ControlCount => _controls.Count;
        public static int SwitchRuleCount => _switchRules.Count;
        public static int BuyRuleCount => _buys.Count;
        public static int PartRuleCount => _parts.Count;
        public static int BoltRuleCount => _bolts.Count;

        private static readonly List<CatalogRule> _doors = new List<CatalogRule>();
        private static readonly List<CatalogRule> _spawnContainers = new List<CatalogRule>();
        private static readonly List<CatalogRule> _controls = new List<CatalogRule>();
        private static readonly List<CatalogRule> _switchRules = new List<CatalogRule>();
        private static readonly List<CatalogRule> _ignitions = new List<CatalogRule>();
        private static readonly List<CatalogRule> _starters = new List<CatalogRule>();
        private static readonly List<BuyCatalogRule> _buys = new List<BuyCatalogRule>();
        private static readonly List<PartCatalogRule> _parts = new List<PartCatalogRule>();
        private static readonly List<BoltCatalogRule> _bolts = new List<BoltCatalogRule>();
        private static VehicleRegistrationConfig _vehicles = new VehicleRegistrationConfig();
        private static PickableRegistrationConfig _pickables = new PickableRegistrationConfig();
        private static ConsumableConfig _consumables = new ConsumableConfig();
        private static VehicleClimateConfig _vehicleClimate = new VehicleClimateConfig();

        public static void Load()
        {
            _doors.Clear();
            _spawnContainers.Clear();
            _controls.Clear();
            _switchRules.Clear();
            _ignitions.Clear();
            _starters.Clear();
            _buys.Clear();
            _parts.Clear();
            _bolts.Clear();
            _vehicles = new VehicleRegistrationConfig();
            _pickables = new PickableRegistrationConfig();
            _consumables = new ConsumableConfig();
            _vehicleClimate = new VehicleClimateConfig();
            Loaded = false;
            Hash = 0;

            string path = Path.Combine(GetPluginDirectory(), FileName);
            if (!File.Exists(path))
            {
                WinterMPPlugin.Log.LogError(
                    $"SyncCatalog: missing '{path}'. Vehicle/house FSM rules will not sync.");
                return;
            }

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogError($"SyncCatalog: could not read '{path}': {e.Message}");
                return;
            }

            Hash = StableHash.Fnv1a32(json);

            SyncCatalogData data;
            try
            {
                data = SyncCatalogJson.Parse(json);
                LoadRules(data.Doors, _doors);
                LoadRules(data.SpawnContainers, _spawnContainers);
                LoadRules(data.Controls, _controls);
                LoadRules(data.SwitchRules, _switchRules);
                LoadRules(data.Ignitions, _ignitions);
                LoadRules(data.Starters, _starters);
                LoadBuyRules(data.Buys, _buys);
                LoadPartRules(data.Parts, _parts);
                LoadBoltRules(data.Bolts, _bolts);
                ApplyVehicleConfig(data);
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogError($"SyncCatalog: invalid JSON in '{path}': {e.Message}");
                return;
            }

            Loaded = true;
            string build = data.GameBuild ?? "?";
            WinterMPPlugin.Log.LogInfo(
                $"SyncCatalog: loaded {_doors.Count} doors, {_spawnContainers.Count} spawn-containers, {_controls.Count} controls, " +
                $"{_switchRules.Count} switch rules, {_ignitions.Count} ignitions, " +
                $"{_starters.Count} starters, {_buys.Count} buys, {_parts.Count} parts, {_bolts.Count} bolts " +
                $"(build '{build}', hash {Hash:X8}).");
        }

        public static string[]? TryMatchDoor(PlayMakerFSM fsm) => TryMatch(_doors, fsm);

        /// <summary>Use-FSM containers (grocery bags) whose "Spawn one/all" states pour
        /// out instantiated products. We replay the spawn state-enter so both peers spill;
        /// unlike doors this is a one-shot action, so it never joins the snapshot/checksum.</summary>
        public static string[]? TryMatchSpawnContainer(PlayMakerFSM fsm) => TryMatch(_spawnContainers, fsm);

        public static string[]? TryMatchControl(PlayMakerFSM fsm) => TryMatch(_controls, fsm);

        public static string[]? TryMatchSwitch(PlayMakerFSM fsm) => TryMatch(_switchRules, fsm);

        public static string[]? TryMatchIgnition(PlayMakerFSM fsm) => TryMatch(_ignitions, fsm);

        public static string[]? TryMatchStarter(PlayMakerFSM fsm) => TryMatch(_starters, fsm);

        /// <summary>Host-authoritative purchase / payment pipelines from catalog buys.</summary>
        public static bool TryMatchBuy(PlayMakerFSM fsm, out CatalogBuyMatch? match)
        {
            match = null;
            if (!Loaded || _buys.Count == 0) return false;

            foreach (var rule in _buys)
            {
                if (!rule.Matches(fsm)) continue;
                if (!rule.TryBuildMatch(fsm, out match)) continue;
                return true;
            }

            return false;
        }

        /// <summary>Car-part assembly Data FSMs (bolt on/off, install, remove).</summary>
        public static string[]? TryMatchPart(PlayMakerFSM fsm)
        {
            if (!Loaded || _parts.Count == 0) return null;

            foreach (var rule in _parts)
            {
                if (!rule.Matches(fsm)) continue;
                if (rule.TryBuildStates(fsm, out string[]? states)) return states;
            }

            return null;
        }

        /// <summary>Wrench Screw FSMs (tighten / untighten).</summary>
        public static bool TryMatchBolt(PlayMakerFSM fsm)
        {
            if (!Loaded || _bolts.Count == 0) return false;

            foreach (var rule in _bolts)
            {
                if (rule.Matches(fsm)) return true;
            }

            return false;
        }

        public static bool IsVehicleRoot(Rigidbody body) => _vehicles.IsVehicleRoot(body);

        public static bool IsPickableRigidbody(Rigidbody body) => _pickables.IsPickable(body);

        /// <summary>Pooled-instance name suffixes ("(itemx)", ...) — spawn template matching strips these.</summary>
        public static string[] PickableNameSuffixes => _pickables.NameSuffixes;

        public static void CollectConsumableDespawnStates(PlayMakerFSM fsm, List<string> states)
            => _consumables.CollectDespawnStates(fsm, states);

        public static bool IsClimateVehicleFsmPath(string path) => _vehicleClimate.IsClimateVehiclePath(path);

        public static bool IsCarTempFsmPath(string path) => _vehicleClimate.IsCarTempRoot(path);

        public static bool IsHeaterFsmPath(string path) => _vehicleClimate.IsHeaterPath(path);

        private static void ApplyVehicleConfig(SyncCatalogData data)
        {
            if (data.Vehicles != null)
            {
                _vehicles.MinMass = data.Vehicles.MinMass;
                _vehicles.RequireRoot = data.Vehicles.RequireRoot;
                if (data.Vehicles.NamePrefixes.Count > 0)
                    _vehicles.NamePrefixes = data.Vehicles.NamePrefixes.ToArray();
            }

            if (data.Pickables != null)
            {
                _pickables.ProbeUseFsm = data.Pickables.ProbeUseFsm;
                if (data.Pickables.ExcludeNameContains.Count > 0)
                    _pickables.ExcludeNameContains = data.Pickables.ExcludeNameContains.ToArray();
                if (data.Pickables.NameSuffixes.Count > 0)
                    _pickables.NameSuffixes = data.Pickables.NameSuffixes.ToArray();
            }

            if (data.Consumables != null)
            {
                if (data.Consumables.FsmName.Length > 0)
                    _consumables.FsmName = data.Consumables.FsmName;
                if (data.Consumables.DrinkCheckState.Length > 0)
                    _consumables.DrinkCheckState = data.Consumables.DrinkCheckState;
                if (data.Consumables.DestroyStates.Count > 0)
                    _consumables.DestroyStates = data.Consumables.DestroyStates.ToArray();
                if (data.Consumables.DrinkEmptyStates.Count > 0)
                    _consumables.DrinkEmptyStates = data.Consumables.DrinkEmptyStates.ToArray();
            }

            if (data.VehicleClimate != null)
            {
                if (data.VehicleClimate.PathPrefixes.Count > 0)
                    _vehicleClimate.PathPrefixes = data.VehicleClimate.PathPrefixes.ToArray();
                if (data.VehicleClimate.CarTempPathContains.Count > 0)
                    _vehicleClimate.CarTempPathContains = data.VehicleClimate.CarTempPathContains.ToArray();
                if (data.VehicleClimate.HeaterPathContains.Count > 0)
                    _vehicleClimate.HeaterPathContains = data.VehicleClimate.HeaterPathContains.ToArray();
            }
        }

        private static void LoadRules(List<CatalogRuleData> source, List<CatalogRule> target)
        {
            foreach (var rule in source)
            {
                target.Add(new CatalogRule(
                    rule.PathPrefix,
                    rule.PathContains,
                    rule.ObjectName,
                    rule.ObjectNameContains,
                    rule.FsmName,
                    rule.States.ToArray(),
                    rule.RequireStates.ToArray(),
                    rule.ExcludePathPrefixes.ToArray()));
            }
        }

        private static void LoadBuyRules(List<BuyRuleData> source, List<BuyCatalogRule> target)
        {
            foreach (var rule in source)
            {
                target.Add(new BuyCatalogRule(
                    rule.Template,
                    rule.PathPrefix,
                    rule.PathContains,
                    rule.ObjectName,
                    rule.ObjectNameContains,
                    rule.FsmName,
                    rule.RequireStates.ToArray(),
                    rule.ResultStates.ToArray(),
                    rule.ExcludePathPrefixes.ToArray(),
                    rule.EntryGuards.ToArray()));
            }
        }

        private static void LoadPartRules(List<PartRuleData> source, List<PartCatalogRule> target)
        {
            foreach (var rule in source)
            {
                target.Add(new PartCatalogRule(
                    rule.PathPrefix,
                    rule.PathContains,
                    rule.ObjectName,
                    rule.ObjectNameContains,
                    rule.FsmName,
                    rule.States.ToArray(),
                    rule.RequireStates.ToArray(),
                    rule.OptionalStates.ToArray(),
                    rule.ExcludePathPrefixes.ToArray()));
            }
        }

        private static void LoadBoltRules(List<BoltRuleData> source, List<BoltCatalogRule> target)
        {
            foreach (var rule in source)
            {
                target.Add(new BoltCatalogRule(
                    rule.PathPrefix,
                    rule.PathContains,
                    rule.ObjectName,
                    rule.ObjectNameContains,
                    rule.FsmName,
                    rule.RequireStates.ToArray(),
                    rule.ExcludePathPrefixes.ToArray()));
            }
        }

        private static string[]? TryMatch(List<CatalogRule> rules, PlayMakerFSM fsm)
        {
            if (!Loaded || rules.Count == 0) return null;

            string scenePath = ScenePath.Of(fsm.transform);
            string objectName = fsm.gameObject.name;
            string fsmName = fsm.FsmName;

            foreach (var rule in rules)
            {
                if (!rule.Matches(scenePath, objectName, fsmName, fsm)) continue;
                return rule.States;
            }

            return null;
        }

        private static string GetPluginDirectory()
        {
            string? location = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(location))
                return ".";
            return Path.GetDirectoryName(location) ?? ".";
        }

        private sealed class CatalogRule
        {
            private readonly string _pathPrefix;
            private readonly string? _pathContains;
            private readonly string? _objectName;
            private readonly string? _objectNameContains;
            private readonly string _fsmName;
            private readonly string[] _requireStates;
            private readonly string[] _excludePathPrefixes;

            internal readonly string[] States;

            internal CatalogRule(
                string pathPrefix,
                string? pathContains,
                string? objectName,
                string? objectNameContains,
                string fsmName,
                string[] states,
                string[] requireStates,
                string[] excludePathPrefixes)
            {
                _pathPrefix = pathPrefix;
                _pathContains = pathContains;
                _objectName = objectName;
                _objectNameContains = objectNameContains;
                _fsmName = fsmName;
                States = states;
                _requireStates = requireStates;
                _excludePathPrefixes = excludePathPrefixes;
            }

            internal bool Matches(string scenePath, string objectName, string fsmName, PlayMakerFSM fsm)
            {
                if (fsmName != _fsmName) return false;

                foreach (string excluded in _excludePathPrefixes)
                {
                    if (scenePath.StartsWith(excluded, StringComparison.Ordinal))
                        return false;
                }

                if (_pathPrefix.Length > 0 && !scenePath.StartsWith(_pathPrefix, StringComparison.Ordinal))
                    return false;

                if (_pathContains != null && scenePath.IndexOf(_pathContains, StringComparison.Ordinal) < 0)
                    return false;

                if (_objectName != null && objectName != _objectName)
                    return false;

                if (_objectNameContains != null
                    && objectName.IndexOf(_objectNameContains, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }

                foreach (string state in States)
                {
                    if (!FsmHook.HasState(fsm, state)) return false;
                }

                foreach (string required in _requireStates)
                {
                    if (!FsmHook.HasState(fsm, required)) return false;
                }

                return States.Length > 0;
            }
        }

        private sealed class BuyCatalogRule
        {
            private readonly string _template;
            private readonly CatalogPathMatch _path;
            private readonly string[] _requireStates;
            private readonly string[] _resultStates;
            private readonly BuyGuardData[] _entryGuards;

            internal BuyCatalogRule(
                string template,
                string pathPrefix,
                string? pathContains,
                string? objectName,
                string? objectNameContains,
                string fsmName,
                string[] requireStates,
                string[] resultStates,
                string[] excludePathPrefixes,
                BuyGuardData[] entryGuards)
            {
                _template = template;
                _path = new CatalogPathMatch(
                    pathPrefix, pathContains, objectName, objectNameContains, fsmName, excludePathPrefixes);
                _requireStates = requireStates;
                _resultStates = resultStates;
                _entryGuards = entryGuards;
            }

            internal bool Matches(PlayMakerFSM fsm) => _path.Matches(fsm);

            internal bool TryBuildMatch(PlayMakerFSM fsm, out CatalogBuyMatch? match)
            {
                if (_template == "shopBuy")
                    return ShopBuyInference.TryInfer(fsm, out match);

                match = null;

                foreach (string required in _requireStates)
                {
                    if (!FsmHook.HasState(fsm, required)) return false;
                }

                var guards = new List<CatalogBuyGuard>(_entryGuards.Length);
                foreach (var guard in _entryGuards)
                {
                    bool exists = FsmHook.HasState(fsm, guard.StateName);
                    if (!exists)
                    {
                        if (guard.Optional) continue;
                        return false;
                    }

                    guards.Add(new CatalogBuyGuard
                    {
                        StateName = guard.StateName,
                        TriggerEvent = guard.TriggerEvent,
                    });
                }

                if (guards.Count == 0) return false;

                var results = new List<string>(_resultStates.Length);
                foreach (string state in _resultStates)
                {
                    if (FsmHook.HasState(fsm, state)) results.Add(state);
                }

                if (results.Count == 0) return false;

                match = new CatalogBuyMatch(guards.ToArray(), results.ToArray());
                return true;
            }
        }

        private sealed class PartCatalogRule
        {
            private readonly CatalogPathMatch _path;
            private readonly string[] _states;
            private readonly string[] _requireStates;
            private readonly string[] _optionalStates;

            internal PartCatalogRule(
                string pathPrefix,
                string? pathContains,
                string? objectName,
                string? objectNameContains,
                string fsmName,
                string[] states,
                string[] requireStates,
                string[] optionalStates,
                string[] excludePathPrefixes)
            {
                _path = new CatalogPathMatch(
                    pathPrefix, pathContains, objectName, objectNameContains, fsmName, excludePathPrefixes);
                _states = states;
                _requireStates = requireStates;
                _optionalStates = optionalStates;
            }

            internal bool Matches(PlayMakerFSM fsm) => _path.Matches(fsm);

            internal bool TryBuildStates(PlayMakerFSM fsm, out string[]? states)
            {
                states = null;

                foreach (string required in _requireStates)
                {
                    if (!FsmHook.HasState(fsm, required)) return false;
                }

                foreach (string state in _states)
                {
                    if (!FsmHook.HasState(fsm, state)) return false;
                }

                var synced = new List<string>(_states.Length + _optionalStates.Length);
                foreach (string state in _states)
                    synced.Add(state);

                foreach (string state in _optionalStates)
                {
                    if (FsmHook.HasState(fsm, state)) synced.Add(state);
                }

                if (synced.Count == 0) return false;

                states = synced.ToArray();
                return true;
            }
        }

        private sealed class BoltCatalogRule
        {
            private readonly CatalogPathMatch _path;
            private readonly string[] _requireStates;

            internal BoltCatalogRule(
                string pathPrefix,
                string? pathContains,
                string? objectName,
                string? objectNameContains,
                string fsmName,
                string[] requireStates,
                string[] excludePathPrefixes)
            {
                _path = new CatalogPathMatch(
                    pathPrefix, pathContains, objectName, objectNameContains, fsmName, excludePathPrefixes);
                _requireStates = requireStates;
            }

            internal bool Matches(PlayMakerFSM fsm)
            {
                if (!_path.Matches(fsm)) return false;

                foreach (string required in _requireStates)
                {
                    if (!FsmHook.HasState(fsm, required)) return false;
                }

                return true;
            }
        }
    }
}
