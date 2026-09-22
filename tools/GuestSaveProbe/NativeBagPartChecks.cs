using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    // Imports installed-game definitions so native ID builders and prerequisite
    // comparisons run in PlayMaker, without entering a player's world or saves.
    internal static class NativeBagPartChecks
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        private static readonly Type Items = Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true);
        private static readonly Type Hook = Core.GetType("WinterMP.Core.Sync.FsmHook", true);

        internal static void Run(Action<string, Action> check)
        {
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
            catalog.GetMethod("EnsureLoaded", Static).Invoke(null, null);
            var c = catalog.GetProperty("ReplacementParts", Static).GetValue(null, null)
                ?? throw new InvalidOperationException("Replacement catalog missing.");
            var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var reader = Activator.CreateInstance(readerType, Members, null, new object[] {
                File.ReadAllText(Path.Combine(Application.dataPath, "../native-bag-part-probe.json")) }, null);
            var input = (Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null);
            var rows = (List<object>)input["fsms"];
            int count = 0;
            foreach (object rule in (IEnumerable)c.GetType().GetField("Factories").GetValue(c))
            {
                if (!(bool)Get(rule, "BagOutput")) continue;
                RunFamily(c, rule, rows, check); count++;
            }
            Require(count == 2, "Expected the two audited grocery part families.");
            RunDisabledActions(c, rows, check);
            RunFanFactory(c, rows, check);
        }

        private static void RunFanFactory(object catalog, List<object> rows, Action<string, Action> check)
        {
            object? rule = null;
            foreach (var candidate in (IEnumerable)Get(catalog, "Factories"))
                if ((string)Get(candidate, "Prefix") == "VIN137") rule = candidate;
            Require(rule != null, "Native radiator fan factory is missing.");
            var root = new GameObject("native radiator fan factory"); root.SetActive(false);
            try
            {
                var dataRow = Find(rows, "VIN137", "Data");
                var factoryRow = Find(rows, (string)Get(rule!, "Path"), "Spawn");
                var data = MakeFsm(Child(root, "VIN137"), dataRow);
                var factory = MakeFsm(Child(root, "RadiatorFan137"), factoryRow);
                var fanMount = Child(root, "VINP_RadiatorFan"); var pulleyMount = Child(root, "VINP_WaterpumpPulley");
                factory.FsmVariables.FindFsmGameObject("VINP").Value = fanMount;
                factory.FsmVariables.FindFsmGameObject("PartBlocking").Value = pulleyMount;
                root.SetActive(true);
                LoadActions(data, dataRow, "Init", "Status"); LoadActions(factory, factoryRow, "Create product", "Create");
                var bound = Activator.CreateInstance(Items.GetNestedType("ReplacementFactory", BindingFlags.NonPublic), true);
                Set(bound, "Rule", rule!); Set(bound, "TemplateData", data); Set(bound, "Prefab", data.gameObject); Set(bound, "Fsm", factory);
                check("fan factory: actual VIN137 template retains its distinct native layout", () =>
                {
                    Call("ValidateReplacementTemplate", bound, catalog);
                    Require(!data.Fsm.Started && data.FsmVariables.FindFsmGameObject("PartBlocking") != null,
                        "Fan validation ran the template or lost its separate pulley reference.");
                });
                foreach (bool fresh in new[] { true, false })
                {
                    bool create = fresh;
                    check("fan factory: actual native " + (create ? "fresh" : "saved") + " output is accepted", () =>
                        Call("ValidateReplacementOutput", bound, catalog, create));
                }
                check("fan factory: replicas retain distinct fan mount and blocking pulley", () =>
                {
                    Call("CopyReplacementReferences", bound, data);
                    Require(data.FsmVariables.FindFsmGameObject("InstallPoint").Value == fanMount
                        && data.FsmVariables.FindFsmGameObject("PartBlocking").Value == pulleyMount,
                        "Fan replica confused its installation mount with the blocking pulley.");
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void RunDisabledActions(object catalog, List<object> rows, Action<string, Action> check)
        {
            foreach (object rule in (IEnumerable)Get(catalog, "Factories"))
            {
                string prefix = (string)Get(rule, "Prefix");
                if (prefix != "VIN103" && prefix != "VIN130") continue;
                var root = new GameObject("native disabled replacement actions"); root.SetActive(false);
                try
                {
                    var dataRow = Find(rows, prefix, "Data");
                    var data = MakeFsm(Child(root, prefix), dataRow); data.gameObject.AddComponent<Rigidbody>();
                    PlayMakerFSM? factory = null; Dictionary<string, object>? factoryRow = null;
                    if (prefix == "VIN130")
                    {
                        factoryRow = Find(rows, (string)Get(rule, "Path"), (string)Get(rule, "Fsm"));
                        factory = MakeFsm(Child(root, "factory"), factoryRow);
                    }
                    root.SetActive(true); LoadActions(data, dataRow, "Init", "Status");
                    var bound = Activator.CreateInstance(Items.GetNestedType("ReplacementFactory", BindingFlags.NonPublic), true);
                    Set(bound, "Rule", rule); Set(bound, "TemplateData", data); Set(bound, "Prefab", data.gameObject);
                    Action validate = () => Call("ValidateReplacementTemplate", bound, catalog);
                    if (factory == null)
                    {
                        check("piston factory: native disabled child lookup is accepted without enabling it", () =>
                        { validate(); Require(!State(data, "Init").Actions[0].Enabled && !data.Fsm.Started, "Piston template ran or enabled its obsolete child lookup."); });
                        check("piston factory: reenabled obsolete lookup is refused", () =>
                            RejectChanged(validate, State(data, "Init").Actions[0], "Enabled", true));
                        check("piston factory: disabled required identity action is refused", () =>
                            RejectChanged(validate, State(data, "Init").Actions[2], "Enabled", false));
                        continue;
                    }
                    LoadActions(factory, factoryRow!, "Create product", "Create"); Set(bound, "Fsm", factory);
                    var mount = Child(root, "native install mount"); var unused = Child(root, "unused factory reference");
                    factory.FsmVariables.FindFsmGameObject("VINP").Value = mount;
                    factory.FsmVariables.FindFsmGameObject("PartBlocking").Value = unused;
                    foreach (bool fresh in new[] { true, false })
                    {
                        string name = fresh ? "Create product" : "Create";
                        var state = State(factory, name); int optionalIndex = fresh ? 4 : 2;
                        Action output = () => Call("ValidateReplacementOutput", bound, catalog, fresh);
                        check("starter factory: native disabled reference is accepted in " + name, () =>
                        { output(); Require(!state.Actions[optionalIndex].Enabled, "Factory validation enabled the unused reference write."); });
                        check("starter factory: reenabled unused reference is refused in " + name, () =>
                            RejectChanged(output, state.Actions[optionalIndex], "Enabled", true));
                        check("starter factory: disabled creation is refused in " + name, () =>
                            RejectChanged(output, state.Actions[fresh ? 1 : 0], "Enabled", false));
                        check("starter factory: disabled reference still validates its destination in " + name, () =>
                            RejectChanged(output, state.Actions[optionalIndex], "variableName", new FsmString { Value = "Wrong" }));
                    }
                    check("starter factory: guest reference copying skips the obsolete missing native destination", () =>
                    {
                        Require(data.FsmVariables.FindFsmGameObject("PartBlocking") == null,
                            "Fixture manufactured the removed native starter variable.");
                        Call("CopyReplacementReferences", bound, data);
                        Require(data.FsmVariables.FindFsmGameObject("InstallPoint").Value == mount
                            && data.FsmVariables.FindFsmGameObject("PartBlocking") == null,
                            "Guest replica applied a reference write disabled in both native factory states.");
                    });
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }

        private static void RunFamily(object c, object rule, List<object> rows, Action<string, Action> check)
        {
            string prefix = (string)Get(rule, "Prefix"), name = (string)Get(rule, "Fsm");
            var root = new GameObject("native bag part probe"); root.SetActive(false);
            var product = Child(root, prefix); var body = product.AddComponent<Rigidbody>(); body.useGravity = false;
            product.AddComponent<BoxCollider>(); Child(product, "Bolts");
            var factoryObject = Child(root, "CreateItems");
            var point = Child(root, "bag position"); point.transform.position = new Vector3(3, 4, 5);
            var mountObject = Child(root, "VINP_" + (name == "Fanbelt" ? "FanBelt" : name));
            var dataRow = Find(rows, prefix, "Data");
            var factoryRow = Find(rows, "Spawner/CreateItems", name);
            var mountRow = Find(rows, "CARPARTS/StartParts/VIN1010/" + mountObject.name, "Data");
            var data = MakeFsm(product, dataRow);
            var factory = MakeFsm(factoryObject, factoryRow);
            var mount = MakeFsm(mountObject, mountRow);
            var alternatorObject = Child(root, "alternator prerequisite");
            var alternator = MakeEmptyFsm(alternatorObject, "Data");
            var setting = new FsmFloat { Name = "SettingRotation", UseVariable = true, Value = 7 };
            alternator.FsmVariables.FloatVariables = new[] { setting };
            var spawned = new List<GameObject>();
            try
            {
                root.SetActive(true);
                LoadActions(data, dataRow, "Init", "Status");
                LoadActions(factory, factoryRow, "Create product", "Create");
                LoadActions(mount, mountRow, "Idle", "Allow install?", "Alternator", "Far", "Near", "Install 1");
                factory.FsmVariables.FindFsmGameObject("Prefab").Value = product;
                factory.FsmVariables.FindFsmGameObject("VINP").Value = mountObject;
                factory.FsmVariables.FindFsmGameObject("ShoppingBagSpawn").Value = point;
                factory.FsmVariables.FindFsmString("SaveID").Value = prefix;
                var bound = Activator.CreateInstance(Items.GetNestedType("ReplacementFactory", BindingFlags.NonPublic), true);
                Set(bound, "Rule", rule); Set(bound, "Fsm", factory); Set(bound, "TemplateData", data); Set(bound, "Prefab", product);
                Action template = () => Call("ValidateReplacementTemplate", bound, c);
                Action fresh = () => Call("ValidateReplacementOutput", bound, c, true);
                check(prefix + ": installed native Data template is accepted", template);
                RunDormantTemplate(c, rule, data, factory, check);
                check(prefix + ": installed fresh and saved factory outputs are accepted", () => {
                    fresh(); Call("ValidateReplacementOutput", bound, c, false);
                });
                var create = State(factory, "Create product").Actions[1];
                check(prefix + ": changed bag spawn point is refused", () => RejectChanged(fresh, create, "spawnPoint",
                    new FsmGameObject { Name = "SpawnPoint", UseVariable = true }));
                check(prefix + ": changed native install reference is refused", () => RejectChanged(fresh,
                    State(factory, "Create product").Actions[2], "variableName", new FsmString { Value = "OtherMount" }));
                check(prefix + ": native factory preserves consecutive IDs and mount references", () =>
                {
                    Start(factory);
                    for (int i = 1; i <= 2; i++)
                    {
                        Fire(factory, "Create product");
                        var output = factory.FsmVariables.FindFsmGameObject("New").Value;
                        if (output == null) throw new InvalidOperationException("Native factory did not create an output.");
                        spawned.Add(output);
                        var outputData = FindData(output);
                        Require(output.name == prefix + i && factory.FsmVariables.FindFsmString("ID").Value == prefix + i
                            && outputData.FsmVariables.FindFsmGameObject("InstallPoint").Value == mountObject
                            && Vector3.Distance(output.transform.position, point.transform.position) < .001f,
                            "Native output identity, reference or bag position changed.");
                    }
                    Require(spawned[0] != spawned[1], "Two native products alias one object.");
                });
                check(prefix + ": native Init retains save ID after display rename", () =>
                {
                    product.name = prefix + "7";
                    Start(data); Fire(data, "Init");
                    var vars = data.FsmVariables;
                    Require(vars.FindFsmString("ID").Value == prefix + "7"
                        && vars.FindFsmString("UTAssemblyID").Value == prefix + "7AID"
                        && vars.FindFsmString("UTPos").Value == prefix + "7POS"
                        && vars.FindFsmString("UTWear").Value == prefix + "7WEA"
                        && vars.FindFsmString("UTTightness").Value == prefix + "7TGH"
                        && product.name == (name == "Fanbelt" ? "Fan belt(VINXX)" : "Oil filter(VINXX)"),
                        "Native initialization lost its persistent identity or save keys.");
                    float value = vars.FindFsmFloat(name == "Fanbelt" ? "Wear" : "Dirt").Value;
                    Require(name == "Fanbelt" ? value >= 90 && value <= 99 : value == 0, "Native condition initialization changed.");
                });
                object? prerequisite = Get(rule, "FitPrerequisite");
                Action verifyMount = () => Call("ValidatePartFitMount", mount, c, false, prerequisite);
                check(prefix + ": complete native fitting mount is accepted", verifyMount);
                if (prerequisite != null)
                {
                    mount.FsmVariables.FindFsmGameObject("db_Installed2").Value = alternatorObject;
                    RunPrerequisite(mount, alternator, setting, c, prerequisite, check);
                }
            }
            finally
            {
                foreach (var output in spawned) if (output != null) UnityEngine.Object.DestroyImmediate(output);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void RunPrerequisite(PlayMakerFSM mount, PlayMakerFSM alternator, FsmFloat setting,
            object c, object prerequisite, Action<string, Action> check)
        {
            Action verify = () => Call("ValidatePartFitPrerequisite", mount, c, prerequisite);
            var gate = State(mount, "Alternator");
            check("fan belt: installed native alternator prerequisite is accepted", verify);
            check("fan belt: changed prerequisite scalar is refused", () => RejectChanged(verify, gate.Actions[0],
                "variableName", new FsmString { Value = "Wear" }));
            check("fan belt: changed prerequisite threshold is refused", () => RejectChanged(verify, gate.Actions[1],
                "float2", new FsmFloat(5)));
            check("fan belt: recurring prerequisite comparison is refused", () => RejectChanged(verify, gate.Actions[1], "everyFrame", true));
            check("fan belt: reversed prerequisite branch is refused", () => RejectChanged(verify, gate.Actions[1],
                "greaterThan", FsmEvent.GetFsmEvent("FINISHED")));
            // Far normally runs distance and mouse-input actions; the probe only
            // needs to observe the native prerequisite's chosen destination.
            State(mount, "Far").Actions = new FsmStateAction[0];
            State(mount, "Far").Transitions = new FsmTransition[0];
            Start(alternator); Start(mount);
            foreach (float value in new[] { 5.9f, 6f, 6.1f })
            {
                float sample = value;
                check("fan belt: native alternator setting " + sample + (sample > 6 ? " permits fitting" : " refuses fitting"), () =>
                {
                    Fire(mount, "Probe idle"); setting.Value = sample; Fire(mount, "Alternator");
                    Require(mount.ActiveStateName == (sample > 6 ? "Far" : "Idle"), "Native prerequisite took the wrong branch.");
                });
            }
        }

        internal static PlayMakerFSM MakeFsm(GameObject owner, Dictionary<string, object> row)
        {
            var fsm = MakeEmptyFsm(owner, (string)row["fsmName"]);
            var defaults = (Dictionary<string, object>)row["variableDefaults"];
            var floats = new List<FsmFloat>(); var ints = new List<FsmInt>(); var bools = new List<FsmBool>();
            var strings = new List<FsmString>(); var objects = new List<FsmGameObject>();
            foreach (Dictionary<string, object> v in (IEnumerable)defaults["floatVariables"])
                floats.Add(new FsmFloat { Name = (string)v["name"], UseVariable = true, Value = Convert.ToSingle(v["value"]) });
            foreach (Dictionary<string, object> v in (IEnumerable)defaults["intVariables"])
                ints.Add(new FsmInt { Name = (string)v["name"], UseVariable = true, Value = Convert.ToInt32(v["value"]) });
            foreach (Dictionary<string, object> v in (IEnumerable)defaults["boolVariables"])
                bools.Add(new FsmBool { Name = (string)v["name"], UseVariable = true, Value = Convert.ToBoolean(v["value"]) });
            foreach (Dictionary<string, object> v in (IEnumerable)defaults["stringVariables"])
                strings.Add(new FsmString { Name = (string)v["name"], UseVariable = true, Value = (string)v["value"] });
            foreach (Dictionary<string, object> v in (IEnumerable)defaults["gameObjectVariables"])
                objects.Add(new FsmGameObject { Name = (string)v["name"], UseVariable = true });
            if (objects.Find(v => v.Name == "ShoppingBagSpawn") == null)
                objects.Add(new FsmGameObject { Name = "ShoppingBagSpawn", UseVariable = true });
            strings.Add(new FsmString { Name = "SaveCarparts", UseVariable = true,
                Value = Path.Combine(Application.dataPath, "../guest-save-probe/native-bag-unused.dat") });
            fsm.FsmVariables.FloatVariables = floats.ToArray(); fsm.FsmVariables.IntVariables = ints.ToArray();
            fsm.FsmVariables.BoolVariables = bools.ToArray(); fsm.FsmVariables.StringVariables = strings.ToArray();
            fsm.FsmVariables.GameObjectVariables = objects.ToArray();
            var states = new List<FsmState>(fsm.Fsm.States);
            foreach (Dictionary<string, object> s in (IEnumerable)row["states"])
                states.Add(new FsmState(fsm.Fsm) { Name = (string)s["name"], Actions = new FsmStateAction[0],
                    Transitions = Transitions((List<object>)s["transitions"]) });
            fsm.Fsm.States = states.ToArray();
            fsm.Fsm.GlobalTransitions = Transitions((List<object>)row["globalTransitions"]);
            return fsm;
        }

        private static void RunDormantTemplate(object catalog, object rule, PlayMakerFSM source, PlayMakerFSM factory,
            Action<string, Action> check)
        {
            string prefix = (string)Get(rule, "Prefix");
            bool active = source.gameObject.activeSelf; string start = source.Fsm.StartState;
            GameObject? template = null, output = null;
            try
            {
                source.gameObject.SetActive(false); source.Fsm.StartState = "Init";
                foreach (var state in source.Fsm.States) state.SaveActions();
                template = (GameObject)UnityEngine.Object.Instantiate(source.gameObject); template.name = prefix;
                var data = FindData(template); data.enabled = true;
                var bound = Activator.CreateInstance(Items.GetNestedType("ReplacementFactory", BindingFlags.NonPublic), true);
                Set(bound, "Rule", rule); Set(bound, "Fsm", factory); Set(bound, "TemplateData", data); Set(bound, "Prefab", template);
                var id = data.FsmVariables.FindFsmString("ID"); id.Value = "dormant template sentinel";
                var scalar = data.FsmVariables.FindFsmFloat(prefix == "FANBELT0" ? "Wear" : "Dirt"); scalar.Value = 41;
                Action validate = () => Call("ValidateReplacementTemplate", bound, catalog);
                check(prefix + ": dormant serialized template validates without starting or changing its native data", () =>
                {
                    Require(!data.Fsm.Initialized && !State(data, "Init").ActionsLoaded,
                        "Fixture did not reproduce a never-awakened serialized template.");
                    validate();
                    Require(data.Fsm.Initialized && State(data, "Init").ActionsLoaded && !data.Fsm.Started
                        && !template.activeSelf && data.enabled && id.Value == "dormant template sentinel" && scalar.Value == 41,
                        "Template preparation entered native initialization or altered its data.");
                });
                check(prefix + ": repeated template validation keeps decoded actions and skips native state entry", () =>
                {
                    Require(data.Fsm.Initialized, "Dormant template did not prepare."); var actions = State(data, "Init").Actions;
                    for (int i = 0; i < 20; i++) validate();
                    Require(ReferenceEquals(actions, State(data, "Init").Actions) && !data.Fsm.Started
                        && id.Value == "dormant template sentinel" && scalar.Value == 41, "Repeated validation reloaded or ran the template.");
                });
                check(prefix + ": output cloned from a prepared template still runs its own native identity initialization", () =>
                {
                    output = (GameObject)UnityEngine.Object.Instantiate(template); output.name = prefix + "23";
                    var product = FindData(output); output.SetActive(true); Start(product);
                    Require(product.Fsm.Started && product.FsmVariables.FindFsmString("ID").Value == prefix + "23"
                        && product.FsmVariables.FindFsmString("UTPos").Value == prefix + "23POS"
                        && id.Value == "dormant template sentinel" && scalar.Value == 41 && !data.Fsm.Started,
                        "Prepared template changed identity or prevented native output initialization.");
                });
                check(prefix + ": initialized template still rejects a changed native action layout", () =>
                {
                    Require(data.Fsm.Initialized, "Dormant template did not prepare.");
                    var state = State(data, "Status"); var actions = state.Actions;
                    try
                    {
                        state.Actions = new FsmStateAction[0];
                        try { validate(); }
                        catch (TargetInvocationException error)
                        { Require(error.InnerException is InvalidOperationException, "Unexpected template validation failure."); return; }
                        throw new InvalidOperationException("Changed template actions were accepted.");
                    }
                    finally { state.Actions = actions; }
                });
            }
            finally
            {
                if (output != null) UnityEngine.Object.DestroyImmediate(output);
                if (template != null) UnityEngine.Object.DestroyImmediate(template);
                source.Fsm.StartState = start; source.gameObject.SetActive(active);
            }
        }

        private static PlayMakerFSM MakeEmptyFsm(GameObject owner, string name)
        {
            var fsm = owner.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
            typeof(PlayMakerFSM).GetField("fsm", Members).SetValue(fsm, new Fsm());
            fsm.Fsm.Name = name; fsm.Fsm.StartState = "Probe idle";
            fsm.Fsm.States = new[] { new FsmState(fsm.Fsm) { Name = "Probe idle", Actions = new FsmStateAction[0] } };
            return fsm;
        }

        private static FsmTransition[] Transitions(List<object> rows)
        {
            var result = new List<FsmTransition>();
            foreach (Dictionary<string, object> row in rows)
                result.Add(new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent((string)row["event"]), ToState = (string)row["to"] });
            return result.ToArray();
        }

        internal static void LoadActions(PlayMakerFSM fsm, Dictionary<string, object> source, params string[] selected)
        {
            foreach (Dictionary<string, object> row in (IEnumerable)source["states"])
            {
                if (Array.IndexOf(selected, (string)row["name"]) < 0) continue;
                var state = State(fsm, (string)row["name"]); var actions = new List<FsmStateAction>();
                foreach (Dictionary<string, object> action in (IEnumerable)row["actions"]) actions.Add(ReadAction(action, fsm));
                state.Actions = actions.ToArray();
                foreach (var action in actions) action.Init(state);
            }
        }

        private static FsmStateAction ReadAction(Dictionary<string, object> row, PlayMakerFSM fsm)
        {
            Type? type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if ((type = assembly.GetType((string)row["type"])) != null) break;
            var action = (FsmStateAction)Activator.CreateInstance(type ?? throw new InvalidOperationException("Missing native action."));
            action.Reset(); action.Enabled = (bool)row["enabled"];
            var parameters = (List<object>)row["parameters"];
            for (int i = 0; i < parameters.Count; i++)
            {
                var p = (Dictionary<string, object>)parameters[i]; string name = (string)p["field"];
                if (name.Length == 0) continue;
                var field = action.GetType().GetField(name);
                if ((string)p["type"] == "Array")
                {
                    var values = new List<object?>(); var element = field.FieldType.GetElementType();
                    while (i + 1 < parameters.Count && (string)((Dictionary<string, object>)parameters[i + 1])["field"] == string.Empty)
                        values.Add(ReadParameter((Dictionary<string, object>)parameters[++i], element, fsm));
                    var array = Array.CreateInstance(element, values.Count);
                    for (int j = 0; j < values.Count; j++) array.SetValue(values[j], j);
                    field.SetValue(action, array);
                }
                else field.SetValue(action, ReadParameter(p, field.FieldType, fsm));
            }
            return action;
        }

        private static object? ReadParameter(Dictionary<string, object> p, Type fieldType, PlayMakerFSM fsm)
        {
            string kind = (string)p["type"];
            if (kind == "FsmFloat" || kind == "FsmInt" || kind == "FsmBool")
            {
                string name = (string)p["name"]; bool use = (bool)p["useVariable"];
                if (kind == "FsmFloat") return use && name.Length != 0 && fsm.FsmVariables.FindFsmFloat(name) != null
                    ? fsm.FsmVariables.FindFsmFloat(name) : new FsmFloat { Name = name, UseVariable = use, Value = Convert.ToSingle(p["value"]) };
                if (kind == "FsmInt") return use && name.Length != 0 && fsm.FsmVariables.FindFsmInt(name) != null
                    ? fsm.FsmVariables.FindFsmInt(name) : new FsmInt { Name = name, UseVariable = use, Value = Convert.ToInt32(p["value"]) };
                return use && name.Length != 0 && fsm.FsmVariables.FindFsmBool(name) != null
                    ? fsm.FsmVariables.FindFsmBool(name) : new FsmBool { Name = name, UseVariable = use, Value = Convert.ToBoolean(p["value"]) };
            }
            if (kind == "FsmString")
            {
                var v = (Dictionary<string, object>)p["value"]; string name = (string)v["name"];
                return Convert.ToBoolean(v["useVariable"]) && name.Length != 0 && fsm.FsmVariables.FindFsmString(name) != null
                    ? fsm.FsmVariables.FindFsmString(name)
                    : new FsmString { Name = name, UseVariable = Convert.ToBoolean(v["useVariable"]), Value = (string)v["value"] };
            }
            if (kind == "FsmVar")
            {
                var v = (Dictionary<string, object>)p["value"];
                if (Convert.ToInt32(v["type"]) != 0) throw new InvalidOperationException("Probe only supports native float FsmVar.");
                return new FsmVar { Type = VariableType.Float, variableName = (string)v["variableName"], useVariable = Convert.ToBoolean(v["useVariable"]), floatValue = Convert.ToSingle(v["floatValue"]) };
            }
            if (kind == "FsmGameObject") return ReadObject((Dictionary<string, object>)p["value"], fsm);
            if (kind == "FsmObject") return ReadComponent((Dictionary<string, object>)p["value"], fsm);
            if (kind == "FsmProperty")
            {
                var v = (Dictionary<string, object>)p["value"];
                string target = (string)v["TargetTypeName"], property = (string)v["PropertyName"];
                if (target == "Drivetrain" && (property == "engineInertia" || property == "maxRPM" || property == "revLimiter"))
                {
                    bool isBool = property == "revLimiter";
                    var scratch = (Dictionary<string, object>)v[isBool ? "BoolParameter" : "FloatParameter"];
                    Require(Convert.ToBoolean(scratch["useVariable"]), "Native drivetrain property must use calculation scratch.");
                    var result = new FsmProperty { TargetObject = ReadComponent((Dictionary<string, object>)v["TargetObject"], fsm),
                        TargetTypeName = target, PropertyName = property, setProperty = Convert.ToBoolean(v["setProperty"]) };
                    if (isBool) result.BoolParameter = fsm.FsmVariables.FindFsmBool((string)scratch["name"])
                        ?? throw new InvalidOperationException("Missing native drivetrain boolean scratch.");
                    else result.FloatParameter = fsm.FsmVariables.FindFsmFloat((string)scratch["name"])
                        ?? throw new InvalidOperationException("Missing native drivetrain float scratch.");
                    return result;
                }
                Require((target == "UnityEngine.BoxCollider" && (property == "isTrigger" || property == "enabled"))
                    || (target == "UnityEngine.SphereCollider" && property == "enabled")
                    || (target == "UnityEngine.Rigidbody" && property == "collisionDetectionMode")
                    || (target == "UnityEngine.MeshCollider" && property == "enabled"), "Unsupported native property in probe.");
                var boolean = (Dictionary<string, object>)v["BoolParameter"]; var text = (Dictionary<string, object>)v["StringParameter"];
                Require(!Convert.ToBoolean(boolean["useVariable"]) && !Convert.ToBoolean(text["useVariable"]),
                    "Probe property values must be native literals.");
                return new FsmProperty { TargetObject = ReadComponent((Dictionary<string, object>)v["TargetObject"], fsm),
                    TargetTypeName = target, PropertyName = property, setProperty = Convert.ToBoolean(v["setProperty"]),
                    BoolParameter = new FsmBool(Convert.ToBoolean(boolean["value"])),
                    StringParameter = new FsmString { Value = (string)text["value"] } };
            }
            if (kind == "FsmOwnerDefault") return ReadOwner((Dictionary<string, object>)p["value"], fsm);
            if (kind == "FsmEventTarget")
            {
                var v = (Dictionary<string, object>)p["value"];
                return new FsmEventTarget { target = (FsmEventTarget.EventTarget)Convert.ToInt32(v["target"]),
                    gameObject = ReadOwner((Dictionary<string, object>)v["gameObject"], fsm),
                    excludeSelf = new FsmBool(Convert.ToBoolean(((Dictionary<string, object>)v["excludeSelf"])["value"])),
                    fsmName = (FsmString)ReadParameter(new Dictionary<string, object> { { "type", "FsmString" }, { "value", v["fsmName"] } }, typeof(FsmString), fsm)!,
                    sendToChildren = new FsmBool(Convert.ToBoolean(((Dictionary<string, object>)v["sendToChildren"])["value"])) };
            }
            if (kind == "FsmEvent") return FsmEvent.GetFsmEvent((string)p["value"]);
            if (kind == "FsmVector3" || kind == "FsmQuaternion")
            {
                string hex = (string)p["rawHex"]; var bytes = new byte[hex.Length / 2];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                int size = kind == "FsmVector3" ? 12 : 16;
                string name = System.Text.Encoding.UTF8.GetString(bytes, size + 1, bytes.Length - size - 1);
                if (kind == "FsmVector3" && bytes[size] != 0 && name.Length != 0 && fsm.FsmVariables.FindFsmVector3(name) != null)
                    return fsm.FsmVariables.FindFsmVector3(name);
                if (kind == "FsmVector3") return new FsmVector3 { Name = name, UseVariable = bytes[size] != 0,
                    Value = new Vector3(BitConverter.ToSingle(bytes, 0), BitConverter.ToSingle(bytes, 4), BitConverter.ToSingle(bytes, 8)) };
                return new FsmQuaternion { Name = name, UseVariable = bytes[size] != 0, Value = new Quaternion(
                    BitConverter.ToSingle(bytes, 0), BitConverter.ToSingle(bytes, 4), BitConverter.ToSingle(bytes, 8), BitConverter.ToSingle(bytes, 12)) };
            }
            return fieldType.IsEnum ? Enum.ToObject(fieldType, Convert.ToInt32(p["value"])) : Convert.ChangeType(p["value"], fieldType);
        }

        private static FsmObject ReadComponent(Dictionary<string, object> v, PlayMakerFSM fsm)
        {
            string name = (string)v["name"];
            Require(Convert.ToBoolean(v["useVariable"]) && name.Length != 0, "Probe component must use an explicit fixture binding.");
            var component = fsm.FsmVariables.FindFsmObject(name) ?? throw new InvalidOperationException("Missing probe component: " + name);
            string type = (string)v["typeName"];
            if (type == "Drivetrain")
            {
                component.ObjectType = Type.GetType("Drivetrain, Assembly-CSharp", true);
                return component;
            }
            Require(type == "UnityEngine.BoxCollider" || type == "UnityEngine.SphereCollider" || type == "UnityEngine.Rigidbody", "Unsupported native component type in probe.");
            component.ObjectType = type == "UnityEngine.BoxCollider" ? typeof(BoxCollider)
                : type == "UnityEngine.SphereCollider" ? typeof(SphereCollider) : typeof(Rigidbody);
            return component;
        }

        private static FsmGameObject ReadObject(Dictionary<string, object> v, PlayMakerFSM fsm)
        {
            string name = (string)v["name"];
            return Convert.ToBoolean(v["useVariable"]) && name.Length != 0 && fsm.FsmVariables.FindFsmGameObject(name) != null
                ? fsm.FsmVariables.FindFsmGameObject(name) : new FsmGameObject { Name = name, UseVariable = Convert.ToBoolean(v["useVariable"]) };
        }
        private static FsmOwnerDefault ReadOwner(Dictionary<string, object> v, PlayMakerFSM fsm) => new FsmOwnerDefault {
            OwnerOption = (OwnerDefaultOption)Convert.ToInt32(v["ownerOption"]), GameObject = ReadObject((Dictionary<string, object>)v["gameObject"], fsm) };
        internal static Dictionary<string, object> Find(List<object> rows, string path, string fsm)
        {
            foreach (Dictionary<string, object> row in rows)
                if ((string)row["path"] == path && (string)row["fsmName"] == fsm) return row;
            throw new InvalidOperationException("Missing native input: " + path + " / " + fsm);
        }
        private static GameObject Child(GameObject parent, string name)
        { var child = new GameObject(name); child.transform.SetParent(parent.transform, false); return child; }
        private static PlayMakerFSM FindData(GameObject owner)
        { foreach (var fsm in owner.GetComponents<PlayMakerFSM>()) if (fsm.FsmName == "Data") return fsm; throw new InvalidOperationException("Native output has no Data."); }
        internal static FsmState State(PlayMakerFSM fsm, string name)
        { foreach (var state in fsm.Fsm.States) if (state.Name == name) return state; throw new InvalidOperationException("Missing state: " + name); }
        internal static void Start(PlayMakerFSM fsm) { fsm.enabled = true; if (!fsm.Fsm.Started) fsm.Fsm.Start(); }
        internal static void Fire(PlayMakerFSM fsm, string state)
        {
            Require((bool)Hook.GetMethod("EnsureRemoteEntry", Static).Invoke(null, new object[] { fsm, state }), "Cannot enter native probe state.");
            Hook.GetMethod("FireRemoteEntry", Static).Invoke(null, new object[] { fsm, state });
        }
        private static object Get(object target, string name) => target.GetType().GetField(name, Members).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Members).SetValue(target, value);
        private static void Call(string name, params object?[] args) => Items.GetMethod(name, Static).Invoke(null, args);
        private static void RejectChanged(Action verify, object action, string fieldName, object replacement)
        {
            verify(); var field = action.GetType().GetField(fieldName);
            var property = field == null ? action.GetType().GetProperty(fieldName) : null;
            object original = field != null ? field.GetValue(action) : property!.GetValue(action, null);
            Action<object> assign = value => { if (field != null) field.SetValue(action, value); else property!.SetValue(action, value, null); };
            try
            {
                assign(replacement);
                try { verify(); } catch (TargetInvocationException e) { if (e.InnerException is InvalidOperationException) return; throw; }
                throw new InvalidOperationException("Changed native binding was accepted.");
            }
            finally { assign(original); }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
