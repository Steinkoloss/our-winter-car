using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
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
        public static int ControlCount => _controls.Count;
        public static int SwitchRuleCount => _switchRules.Count;
        public static int BuyRuleCount => _buys.Count;

        private static readonly List<CatalogRule> _doors = new List<CatalogRule>();
        private static readonly List<CatalogRule> _controls = new List<CatalogRule>();
        private static readonly List<CatalogRule> _switchRules = new List<CatalogRule>();
        private static readonly List<CatalogRule> _ignitions = new List<CatalogRule>();
        private static readonly List<CatalogRule> _starters = new List<CatalogRule>();
        private static readonly List<BuyCatalogRule> _buys = new List<BuyCatalogRule>();

        public static void Load()
        {
            _doors.Clear();
            _controls.Clear();
            _switchRules.Clear();
            _ignitions.Clear();
            _starters.Clear();
            _buys.Clear();
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
                LoadRules(data.Controls, _controls);
                LoadRules(data.SwitchRules, _switchRules);
                LoadRules(data.Ignitions, _ignitions);
                LoadRules(data.Starters, _starters);
                LoadBuyRules(data.Buys, _buys);
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogError($"SyncCatalog: invalid JSON in '{path}': {e.Message}");
                return;
            }

            Loaded = true;
            string build = data.GameBuild ?? "?";
            WinterMPPlugin.Log.LogInfo(
                $"SyncCatalog: loaded {_doors.Count} doors, {_controls.Count} controls, " +
                $"{_switchRules.Count} switch rules, {_ignitions.Count} ignitions, " +
                $"{_starters.Count} starters, {_buys.Count} buys (build '{build}', hash {Hash:X8}).");
        }

        public static string[]? TryMatchDoor(PlayMakerFSM fsm) => TryMatch(_doors, fsm);

        public static string[]? TryMatchControl(PlayMakerFSM fsm) => TryMatch(_controls, fsm);

        public static string[]? TryMatchSwitch(PlayMakerFSM fsm) => TryMatch(_switchRules, fsm);

        public static string[]? TryMatchIgnition(PlayMakerFSM fsm) => TryMatch(_ignitions, fsm);

        public static string[]? TryMatchStarter(PlayMakerFSM fsm) => TryMatch(_starters, fsm);

        /// <summary>Host-authoritative purchase / payment pipelines from catalog buys.</summary>
        public static bool TryMatchBuy(PlayMakerFSM fsm, out CatalogBuyMatch? match)
        {
            match = null;
            if (!Loaded || _buys.Count == 0) return false;

            string scenePath = ScenePath.Of(fsm.transform);
            string objectName = fsm.gameObject.name;
            string fsmName = fsm.FsmName;

            foreach (var rule in _buys)
            {
                if (!rule.MatchesPath(scenePath, objectName, fsmName)) continue;
                if (!rule.TryBuildMatch(fsm, out match)) continue;
                return true;
            }

            return false;
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
            private readonly string _pathPrefix;
            private readonly string? _pathContains;
            private readonly string? _objectName;
            private readonly string? _objectNameContains;
            private readonly string _fsmName;
            private readonly string[] _requireStates;
            private readonly string[] _resultStates;
            private readonly string[] _excludePathPrefixes;
            private readonly BuyGuardData[] _entryGuards;

            internal BuyCatalogRule(
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
                _pathPrefix = pathPrefix;
                _pathContains = pathContains;
                _objectName = objectName;
                _objectNameContains = objectNameContains;
                _fsmName = fsmName;
                _requireStates = requireStates;
                _resultStates = resultStates;
                _excludePathPrefixes = excludePathPrefixes;
                _entryGuards = entryGuards;
            }

            internal bool MatchesPath(string scenePath, string objectName, string fsmName)
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

                return true;
            }

            internal bool TryBuildMatch(PlayMakerFSM fsm, out CatalogBuyMatch? match)
            {
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
    }
}
