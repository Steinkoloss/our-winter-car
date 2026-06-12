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
    /// Curated vehicle-control descriptors (catalog/sync-catalog.json). Adding a
    /// dashboard button is editing JSON, not another Classify* branch.
    /// </summary>
    public static class SyncCatalog
    {
        public const string FileName = "sync-catalog.json";

        public static bool Loaded { get; private set; }
        public static uint Hash { get; private set; }
        public static int ControlCount => _controls.Count;

        private static readonly List<ControlRule> _controls = new List<ControlRule>();

        public static void Load()
        {
            _controls.Clear();
            Loaded = false;
            Hash = 0;

            string path = Path.Combine(GetPluginDirectory(), FileName);
            if (!File.Exists(path))
            {
                WinterMPPlugin.Log.LogError(
                    $"SyncCatalog: missing '{path}'. Vehicle dashboard controls will not sync.");
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
                foreach (var rule in data.Controls)
                {
                    _controls.Add(new ControlRule(
                        rule.PathPrefix,
                        rule.PathContains,
                        rule.ObjectName,
                        rule.FsmName,
                        rule.States.ToArray()));
                }
            }
            catch (Exception e)
            {
                _controls.Clear();
                WinterMPPlugin.Log.LogError($"SyncCatalog: invalid JSON in '{path}': {e.Message}");
                return;
            }

            Loaded = true;
            string build = data.GameBuild ?? "?";
            WinterMPPlugin.Log.LogInfo(
                $"SyncCatalog: loaded {_controls.Count} control rules (build '{build}', hash {Hash:X8}).");
        }

        /// <summary>Returns synced states when an FSM matches a catalog rule.</summary>
        public static string[]? TryMatchControl(PlayMakerFSM fsm)
        {
            if (!Loaded || _controls.Count == 0) return null;

            string scenePath = ScenePath.Of(fsm.transform);
            string objectName = fsm.gameObject.name;
            string fsmName = fsm.FsmName;

            foreach (var rule in _controls)
            {
                if (!rule.Matches(scenePath, objectName, fsmName)) continue;
                if (!rule.HasStatesOn(fsm)) continue;
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

        private sealed class ControlRule
        {
            private readonly string _pathPrefix;
            private readonly string? _pathContains;
            private readonly string? _objectName;
            private readonly string _fsmName;

            internal readonly string[] States;

            internal ControlRule(
                string pathPrefix,
                string? pathContains,
                string? objectName,
                string fsmName,
                string[] states)
            {
                _pathPrefix = pathPrefix;
                _pathContains = pathContains;
                _objectName = objectName;
                _fsmName = fsmName;
                States = states;
            }

            internal bool Matches(string scenePath, string objectName, string fsmName)
            {
                if (fsmName != _fsmName) return false;
                if (!scenePath.StartsWith(_pathPrefix, StringComparison.Ordinal)) return false;
                if (_pathContains != null && scenePath.IndexOf(_pathContains, StringComparison.Ordinal) < 0)
                    return false;
                if (_objectName != null && objectName != _objectName) return false;
                return true;
            }

            internal bool HasStatesOn(PlayMakerFSM fsm)
            {
                foreach (string state in States)
                {
                    if (!FsmHook.HasState(fsm, state)) return false;
                }

                return true;
            }
        }
    }
}
