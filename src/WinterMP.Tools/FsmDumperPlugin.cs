using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using WinterMP.Net;

namespace WinterMP.Tools
{
    /// <summary>
    /// M0 catalog tooling. Dumps every PlayMakerFSM (states, transitions, action
    /// types, events, variables) plus all rigidbodies to JSON under &lt;game&gt;\WinterMP\dumps\.
    ///
    /// Triggers: automatically ~20s after each level finishes loading (once per level),
    /// or manually with F9. Dumps are diffed across game updates and feed the sync
    /// catalog in catalog/ (PLAN.md §3.3, §4.2).
    ///
    /// Targets Unity 5.0 / .NET 3.5 — PlayMaker is accessed purely via reflection.
    /// </summary>
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class FsmDumperPlugin : BaseUnityPlugin
    {
        private const float AutoDumpDelaySeconds = 20f;

        private static readonly string[] VariableArrayNames =
        {
            "FloatVariables", "IntVariables", "BoolVariables", "StringVariables",
            "Vector2Variables", "Vector3Variables", "ColorVariables", "RectVariables",
            "QuaternionVariables", "GameObjectVariables", "ObjectVariables",
            "MaterialVariables", "TextureVariables", "ArrayVariables", "EnumVariables",
        };

        internal static ManualLogSource Log = null!;

        private ConfigEntry<bool> _autoDumpEnabled = null!;

        private readonly Dictionary<string, bool> _dumpedLevels = new Dictionary<string, bool>();
        private string _lastLevel = string.Empty;
        private float _scheduledDumpAt = -1f;
        private string _scheduledLevel = string.Empty;

        private void Awake()
        {
            Log = Logger;

            _autoDumpEnabled = Config.Bind(
                "Catalog", "AutoDumpOnLevelLoad", false,
                "Schedule an FSM catalog dump ~20s after each new level. F9 always dumps manually.");
            Log.LogInfo($"WinterMP Tools {MyPluginInfo.PLUGIN_VERSION} loaded. " +
                        (_autoDumpEnabled.Value
                            ? $"Auto-dump {AutoDumpDelaySeconds}s after each level load; "
                            : string.Empty)
                        + "F9 = manual dump.");
        }

        private void Update()
        {
            WatchLevels();

            if (_scheduledDumpAt > 0f && Time.realtimeSinceStartup >= _scheduledDumpAt)
            {
                _scheduledDumpAt = -1f;
                RunDump($"auto:{_scheduledLevel}");
            }

            if (Input.GetKeyDown(KeyCode.F9))
                RunDump("manual");
        }

        private void WatchLevels()
        {
            string level;
            try
            {
                level = Application.loadedLevelName ?? string.Empty;
            }
            catch
            {
                return;
            }

            if (level == _lastLevel) return;
            _lastLevel = level;

            if (level.Length == 0 || _dumpedLevels.ContainsKey(level)) return;

            _dumpedLevels[level] = true;
            if (!_autoDumpEnabled.Value) return;

            _scheduledLevel = level;
            _scheduledDumpAt = Time.realtimeSinceStartup + AutoDumpDelaySeconds;
            Log.LogInfo($"Level '{level}' loaded — catalog dump scheduled in {AutoDumpDelaySeconds}s.");
        }

        private void RunDump(string trigger)
        {
            try
            {
                string path = DumpAll(trigger);
                Log.LogInfo($"Catalog dump ({trigger}) written: {path}");
            }
            catch (Exception e)
            {
                Log.LogError($"Catalog dump failed: {e}");
            }
        }

        private static string DumpAll(string trigger)
        {
            var json = new JsonWriter();
            json.BeginObject();

            json.Key("meta");
            json.BeginObject();
            json.Key("game"); json.Value(SafeAppString("productName"));
            json.Key("gameVersion"); json.Value(SafeAppString("version"));
            json.Key("unityVersion"); json.Value(Application.unityVersion);
            json.Key("level"); json.Value(SafeLoadedLevelName());
            json.Key("trigger"); json.Value(trigger);
            json.Key("dumpedAtUtc"); json.Value(DateTime.UtcNow.ToString("o"));
            json.Key("toolsVersion"); json.Value(MyPluginInfo.PLUGIN_VERSION);
            json.Key("schemaVersion"); json.Value(3L);
            json.EndObject();

            DumpFsms(json);
            DumpGlobalVariables(json);
            DumpRigidbodies(json);

            json.EndObject();

            string dir = Path.Combine(Path.Combine(Paths.GameRootPath, "WinterMP"), "dumps");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"catalog-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.WriteAllText(file, json.ToString());
            return file;
        }

        private static void DumpFsms(JsonWriter json)
        {
            json.Key("fsms");
            json.BeginArray();

            var fsmType = ReflectionUtil.FindType("PlayMakerFSM");
            if (fsmType == null)
            {
                json.EndArray();
                Log.LogWarning("PlayMakerFSM type not found — PlayMaker not loaded yet?");
                return;
            }

            var components = Resources.FindObjectsOfTypeAll(fsmType);
            int dumped = 0;

            foreach (var obj in components)
            {
                var component = obj as Component;
                if (component == null) continue;

                GameObject go;
                bool active;
                try
                {
                    go = component.gameObject;
                    active = go.activeInHierarchy;
                }
                catch
                {
                    continue; // destroyed/in-flight objects
                }

                string scenePath = GetScenePath(go.transform);
                string fsmName = ReflectionUtil.GetString(component, "FsmName") ?? "?";
                object? fsm = ReflectionUtil.GetMember(component, "Fsm");

                json.BeginObject();
                json.Key("path"); json.Value(scenePath);
                json.Key("fsmName"); json.Value(fsmName);
                json.Key("netId"); json.Value(StableHash.Fnv1a32(scenePath + "::" + fsmName));
                json.Key("active"); json.Value(active);
                json.Key("activeState"); json.Value(ReflectionUtil.GetString(fsm, "ActiveStateName"));

                json.Key("states");
                json.BeginArray();
                var states = ReflectionUtil.GetMember(fsm, "States") as IEnumerable;
                if (states != null)
                {
                    foreach (var state in states)
                    {
                        json.BeginObject();
                        json.Key("name"); json.Value(ReflectionUtil.GetString(state, "Name"));
                        json.Key("transitions");
                        json.BeginArray();
                        var transitions = ReflectionUtil.GetMember(state, "Transitions") as IEnumerable;
                        if (transitions != null)
                        {
                            foreach (var transition in transitions)
                            {
                                json.BeginObject();
                                json.Key("event"); json.Value(ReflectionUtil.GetString(transition, "EventName"));
                                json.Key("to"); json.Value(ReflectionUtil.GetString(transition, "ToState"));
                                json.EndObject();
                            }
                        }
                        json.EndArray();

                        // Action type names are deliberately enough to identify what a
                        // state does (payment, save, spawn, random roll) without dumping
                        // arbitrary object references or save data from each action.
                        json.Key("actionTypes");
                        json.BeginArray();
                        var actions = ReflectionUtil.GetMember(state, "Actions") as IEnumerable;
                        if (actions != null)
                        {
                            foreach (var action in actions)
                                json.Value(ActionTypeName(action));
                        }
                        json.EndArray();
                        json.EndObject();
                    }
                }
                json.EndArray();

                json.Key("events");
                json.BeginArray();
                var events = ReflectionUtil.GetMember(fsm, "Events") as IEnumerable;
                if (events != null)
                {
                    foreach (var fsmEvent in events)
                        json.Value(ReflectionUtil.GetString(fsmEvent, "Name"));
                }
                json.EndArray();

                // Global transitions are how most game FSMs are actually driven from the
                // outside (SendEvent("RENT"), "NORMAL", ...). Without them a dump shows an
                // event in the events[] list with no state referencing it, and you cannot
                // tell where — or whether — firing it lands. Sync work leans on this to
                // pick suppression/replay targets, so dump it alongside the per-state ones.
                json.Key("globalTransitions");
                json.BeginArray();
                var globalTransitions = ReflectionUtil.GetMember(fsm, "GlobalTransitions") as IEnumerable;
                if (globalTransitions != null)
                {
                    foreach (var transition in globalTransitions)
                    {
                        json.BeginObject();
                        json.Key("event"); json.Value(ReflectionUtil.GetString(transition, "EventName"));
                        json.Key("to"); json.Value(ReflectionUtil.GetString(transition, "ToState"));
                        json.EndObject();
                    }
                }
                json.EndArray();

                json.Key("variables");
                json.BeginObject();
                object? variables = ReflectionUtil.GetMember(fsm, "Variables");
                foreach (string arrayName in VariableArrayNames)
                {
                    var namedVars = ReflectionUtil.GetMember(variables, arrayName) as IEnumerable;
                    if (namedVars == null) continue;

                    json.Key(arrayName);
                    json.BeginArray();
                    foreach (var namedVar in namedVars)
                        json.Value(ReflectionUtil.GetString(namedVar, "Name"));
                    json.EndArray();
                }
                json.EndObject();

                json.EndObject();
                dumped++;
            }

            json.EndArray();
            Log.LogInfo($"Dumped {dumped} FSMs.");
        }

        // Cash, the bank balance, and several economy scalars live in PlayMaker GLOBALS,
        // which no per-FSM record carries — without this section a dump cannot answer
        // "what is the bank balance variable actually called" (COVERAGE-ROADMAP R1.1) and
        // global bindings stay unverifiable by tools/check_fsm_bindings.py. Scalar VALUES
        // are included on purpose: a recognizable balance is what disambiguates
        // similarly-named candidates.
        private static void DumpGlobalVariables(JsonWriter json)
        {
            json.Key("globalVariables");
            json.BeginObject();

            var globalsType = ReflectionUtil.FindType("PlayMakerGlobals");
            object? instance = ReflectionUtil.GetStaticMember(globalsType, "Instance");
            object? variables = ReflectionUtil.GetMember(instance, "Variables");
            if (variables == null)
            {
                json.EndObject();
                Log.LogWarning("PlayMakerGlobals not found — global variables not dumped.");
                return;
            }

            foreach (string arrayName in VariableArrayNames)
            {
                var namedVars = ReflectionUtil.GetMember(variables, arrayName) as IEnumerable;
                if (namedVars == null) continue;

                bool scalar = arrayName == "FloatVariables" || arrayName == "IntVariables"
                    || arrayName == "BoolVariables" || arrayName == "StringVariables";

                json.Key(arrayName);
                json.BeginArray();
                foreach (var namedVar in namedVars)
                {
                    json.BeginObject();
                    json.Key("name"); json.Value(ReflectionUtil.GetString(namedVar, "Name"));
                    if (scalar)
                    {
                        object? value = ReflectionUtil.GetMember(namedVar, "Value");
                        json.Key("value"); json.Value(value != null ? value.ToString() : null);
                    }
                    json.EndObject();
                }
                json.EndArray();
            }

            json.EndObject();
        }

        private static string? ActionTypeName(object? action)
        {
            if (action == null) return null;
            try
            {
                return action.GetType().FullName ?? action.GetType().Name;
            }
            catch
            {
                return "unknown";
            }
        }

        private static void DumpRigidbodies(JsonWriter json)
        {
            json.Key("rigidbodies");
            json.BeginArray();

            var bodies = Resources.FindObjectsOfTypeAll(typeof(Rigidbody));
            int count = 0;

            foreach (var obj in bodies)
            {
                var body = obj as Rigidbody;
                if (body == null) continue;

                string scenePath;
                bool active;
                float mass;
                bool kinematic;
                try
                {
                    scenePath = GetScenePath(body.transform);
                    active = body.gameObject.activeInHierarchy;
                    mass = body.mass;
                    kinematic = body.isKinematic;
                }
                catch
                {
                    continue;
                }

                json.BeginObject();
                json.Key("path"); json.Value(scenePath);
                json.Key("netId"); json.Value(StableHash.Fnv1a32(scenePath));
                json.Key("active"); json.Value(active);
                json.Key("mass"); json.Value(mass);
                json.Key("kinematic"); json.Value(kinematic);
                json.EndObject();
                count++;
            }

            json.EndArray();
            Log.LogInfo($"Dumped {count} rigidbodies.");
        }

        private static string SafeAppString(string property)
        {
            try
            {
                var prop = typeof(Application).GetProperty(property, BindingFlags.Public | BindingFlags.Static);
                return prop?.GetValue(null, null) as string ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static string SafeLoadedLevelName()
        {
            try
            {
                return Application.loadedLevelName ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static string GetScenePath(Transform transform)
        {
            string path = transform.name;
            var current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
