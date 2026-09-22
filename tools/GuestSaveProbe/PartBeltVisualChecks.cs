using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static class PartBeltVisualChecks
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string MountPath = "CARPARTS/StartParts/VIN1010/VINP_FanBelt";
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        private static readonly Type Items = Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true);

        internal static void Run(Action<string, Action> check)
        {
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
            catalog.GetMethod("EnsureLoaded", Static).Invoke(null, null);
            var parts = catalog.GetProperty("ReplacementParts", Static).GetValue(null, null);
            object? rule = null, factoryRule = null;
            foreach (object factory in (IEnumerable)Get(parts, "Factories"))
                if ((string)Get(factory, "Prefix") == "FANBELT0") { factoryRule = factory; rule = Get(factory, "BeltVisual"); }
            if (rule == null) throw new InvalidOperationException("Fan-belt visual catalog missing.");
            var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var reader = Activator.CreateInstance(readerType, Members, null, new object[] {
                File.ReadAllText(Path.Combine(Application.dataPath, "../belt-visual-probe.json")) }, null);
            var input = (Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null);
            var rows = (List<object>)input["fsms"];
            var mountRow = NativeBagPartChecks.Find(rows, MountPath, "Data");
            var jumpingRow = NativeBagPartChecks.Find(rows, MountPath + "/FanBelt/Animations", "Jumping");
            var scrollRow = NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Engine/SymptomsEngine/Animations", "BeltAnimation");
            Require(GameObject.Find("CORRIS") == null, "Belt probe requires a menu boot without a loaded vehicle.");
            var scrollRoot = new GameObject("CORRIS"); scrollRoot.SetActive(false);
            var scrollObject = Child(Child(Child(Child(scrollRoot, "Simulation"), "Engine"), "SymptomsEngine"), "Animations");
            var scroll = NativeBagPartChecks.MakeFsm(scrollObject, scrollRow);
            var globalRpm = FsmVariables.GlobalVariables.FindFsmFloat("RPM");
            if (globalRpm == null) throw new InvalidOperationException("Native RPM global missing.");
            float savedRpm = globalRpm.Value;
            var root = new GameObject("native belt visual probe"); root.SetActive(false);
            var nodes = new Dictionary<string, GameObject>(StringComparer.Ordinal) { { string.Empty, root } };
            foreach (Dictionary<string, object> row in (IEnumerable)input["transforms"])
            {
                string path = (string)row["path"]; int slash = path.LastIndexOf('/');
                var node = new GameObject(slash < 0 ? path : path.Substring(slash + 1));
                node.transform.SetParent(nodes[slash < 0 ? string.Empty : path.Substring(0, slash)].transform, false);
                node.transform.localPosition = Vector((Dictionary<string, object>)row["position"]);
                node.transform.localRotation = Rotation((Dictionary<string, object>)row["rotation"]);
                node.transform.localScale = Vector((Dictionary<string, object>)row["scale"]);
                node.SetActive((bool)row["active"]); nodes.Add(path, node);
            }
            var visual = nodes["FanBelt"]; var bone = nodes["FanBelt/Mesh/ScaleBone"].transform;
            var renderer = nodes["FanBelt/Mesh/FanbeltMesh"].AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { name = "probe motor_fanbelt" };
            mesh.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1 },
                new BoneWeight { boneIndex0 = 1, weight0 = 1 }, new BoneWeight { boneIndex0 = 0, weight0 = 1 } };
            mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
            renderer.sharedMesh = mesh; renderer.bones = new[] { nodes["FanBelt/Mesh/Bone"].transform, bone };
            renderer.rootBone = renderer.bones[0];
            var material = new Material(Shader.Find("Diffuse")) { name = "probe anim_motor_belts" };
            renderer.sharedMaterial = material;
            var audio = nodes["FanBelt/Animations"].AddComponent<AudioSource>();
            var clip = AudioClip.Create("probe belt_whine", 22050, 1, 22050, false);
            audio.clip = clip; audio.loop = true; audio.playOnAwake = true; audio.volume = 0; audio.pitch = 1;
            audio.rolloffMode = AudioRolloffMode.Linear; audio.minDistance = 1; audio.maxDistance = 100;
            var mount = NativeBagPartChecks.MakeFsm(root, mountRow);
            var jumping = NativeBagPartChecks.MakeFsm(nodes["FanBelt/Animations"], jumpingRow);
            var rpm = new FsmFloat { Name = "RPM", UseVariable = true, Value = 3000 };
            var floats = new List<FsmFloat>(jumping.FsmVariables.FloatVariables); floats.Add(rpm);
            jumping.FsmVariables.FloatVariables = floats.ToArray();
            mount.FsmVariables.FindFsmGameObject("FanBeltMesh").Value = visual;
            jumping.FsmVariables.FindFsmGameObject("ScaleBone").Value = bone.gameObject;
            jumping.FsmVariables.FindFsmGameObject("db_AlternatorBelt").Value = root;
            var alternatorObject = Child(root, "probe alternator");
            var alternator = EmptyData(alternatorObject);
            alternator.FsmVariables.FloatVariables = new[] { new FsmFloat { Name = "SettingRotation", UseVariable = true, Value = 4 } };
            jumping.FsmVariables.FindFsmGameObject("db_Alternator").Value = alternatorObject;
            var parent = Child(root, "guest attachment").transform;
            parent.localPosition = new Vector3(2, 3, 4); parent.localRotation = Quaternion.Euler(15, 25, 35);
            object? source = null, view = null;
            int breakoffs = 0;
            try
            {
                root.SetActive(true); visual.SetActive(true);
                LoadJumping(jumping, jumpingRow, audio);
                scrollRoot.SetActive(true);
                NativeBagPartChecks.LoadActions(scroll, scrollRow, "State 1", "Delay");
                var scrollActions = NativeBagPartChecks.State(scroll, "State 1").Actions;
                scrollActions[2].GetType().GetField("add").SetValue(scrollActions[2], globalRpm);
                foreach (int index in new[] { 4, 5 })
                    scrollActions[index].GetType().GetField("gameObject").SetValue(scrollActions[index],
                        new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                            GameObject = new FsmGameObject { Value = renderer.gameObject } });
                var remove = NativeBagPartChecks.State(mount, "Remove part");
                remove.Actions = new FsmStateAction[] { new Counter(() => breakoffs++) };
                remove.Actions[0].Init(remove);
                NativeBagPartChecks.Start(mount); NativeBagPartChecks.Start(alternator); NativeBagPartChecks.Start(jumping);
                NativeBagPartChecks.Start(scroll);
                Action bind = () =>
                {
                    source = Call("BindPartBeltSource", mount, rule);
                    Require((bool)Call("BindPartBeltScroll", source!, rule)!, "Native engine scroll binding was unavailable.");
                };
                check("belt visual: installed native hierarchy and scale actions bind", bind);
                foreach (string mode in new[] { "ready", "missing", "changed" })
                {
                    string current = mode;
                    check("belt visual: guest isolation ignores " + current + " local scroll inputs", () =>
                    {
                        string name = scrollRoot.name;
                        var field = scrollActions[2].GetType().GetField("perSecond");
                        object value = field.GetValue(scrollActions[2]);
                        try
                        {
                            if (current == "missing") scrollRoot.name = "unrelated scroll root";
                            if (current == "changed") field.SetValue(scrollActions[2], false);
                            CheckGuestSource(mount, jumping, scroll, rule, renderer, audio, parent);
                        }
                        finally { scrollRoot.name = name; field.SetValue(scrollActions[2], value); }
                    });
                }
                check("belt visual: host source still binds native scroll inputs", () =>
                {
                    var sync = Activator.CreateInstance(Items, Members, null, new object?[] { null }, null);
                    var hostSource = Items.GetMethod("GetPartBeltSource", Members).Invoke(sync, new object[] { mount, rule, false });
                    Require(hostSource != null && ReferenceEquals(Get(hostSource, "Scroll"), scroll)
                        && ((IDictionary)Get(sync, "_partBeltIsolation")).Count == 0,
                        "Host capture lost its native scroll binding or paused its own belt.");
                });
                check("belt visual: native Animate changes host wear and output", () =>
                {
                    mount.FsmVariables.FindFsmFloat("Wear").Value = 80;
                    NativeBagPartChecks.Fire(jumping, "Animate");
                    Require(Near(mount.FsmVariables.FindFsmFloat("Wear").Value, 79.985f)
                        && Near(jumping.FsmVariables.FindFsmFloat("Scale").Value, 1.1f)
                        && Near(audio.pitch, 1.1f) && bone.localScale == Vector3.one,
                        "Native fixture did not exercise the audited host wear and presentation actions.");
                });
                check("belt visual: native failure sends the mount BREAKOFF event", () =>
                {
                    NativeBagPartChecks.Fire(mount, "Update 2");
                    NativeBagPartChecks.Fire(jumping, "Belt destroyed");
                    Require(breakoffs == 1 && !audio.enabled, "Native failure event did not reach its bound mount.");
                });
                check("belt visual: running host capture uses the native pulse amplitude", () =>
                {
                    NativeBagPartChecks.Fire(jumping, "Probe idle"); audio.enabled = true;
                    jumping.FsmVariables.FindFsmFloat("Scale").Value = .6f; bone.localScale = Vector3.one;
                    audio.pitch = 1.2f; audio.volume = .4f;
                    var state = (PartBeltVisualState)Call("ReadPartBeltVisual", source!)!;
                    Require(state.Visible && state.Running && Near(state.Scale, .6f) && Near(state.Pitch, 1.2f)
                        && Near(state.Volume, .4f), "Capture read the transient rest bone instead of native amplitude.");
                });
                check("belt visual: stopped host capture preserves the final native pose", () =>
                {
                    audio.enabled = false; bone.localScale = new Vector3(.75f, 1, .75f);
                    var state = (PartBeltVisualState)Call("ReadPartBeltVisual", source!)!;
                    Require(state.Visible && !state.Running && Near(state.Scale, .75f), "Stopped belt lost its final native pose.");
                });
                check("belt visual: inactive native visual is captured as hidden", () =>
                {
                    visual.SetActive(false);
                    try { var state = (PartBeltVisualState)Call("ReadPartBeltVisual", source!)!;
                        Require(!state.Visible && !state.Running, "Inactive mount visual remained visible remotely."); }
                    finally { visual.SetActive(true); }
                });
                check("belt visual: native engine scroll speed is captured without its phase", () =>
                {
                    globalRpm.Value = 3000; NativeBagPartChecks.Fire(scroll, "State 1");
                    var state = (PartBeltVisualState)Call("ReadPartBeltVisual", source!)!;
                    Require(Near(state.ScrollSpeed, -90), "Native RPM and scroll multiplier were not captured.");
                    scroll.FsmVariables.FindFsmFloat("Offset").Value = -900;
                    var next = (PartBeltVisualState)Call("ReadPartBeltVisual", source!)!;
                    Require(state.ScrollSpeed == next.ScrollSpeed, "Changing only native UV phase changed the receipt.");
                });
                check("belt visual: native scroll reset delay is captured as stopped", () =>
                {
                    NativeBagPartChecks.Fire(scroll, "Delay");
                    Require(((PartBeltVisualState)Call("ReadPartBeltVisual", source!)!).ScrollSpeed == 0,
                        "Native reset delay continued scrolling remotely.");
                    NativeBagPartChecks.Fire(scroll, "Probe idle");
                });
                check("belt visual: changed scroll time basis is rejected", () => RejectChanged(bind, scrollActions[2], "perSecond", false));
                check("belt visual: redirected scroll renderer is rejected", () => RejectChanged(bind, scrollActions[5], "gameObject",
                    new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = parent.gameObject } }));
                check("belt visual: changed native scroll reset is rejected", () => RejectChanged(bind, scrollActions[6], "float2", new FsmFloat(-500)));
                check("belt visual: a local RPM shadow is rejected", () =>
                {
                    var original = scroll.FsmVariables.FloatVariables;
                    var values = new List<FsmFloat>(original) { new FsmFloat { Name = "RPM", UseVariable = true, Value = 123 } };
                    try { scroll.FsmVariables.FloatVariables = values.ToArray(); Reject(bind); }
                    finally { scroll.FsmVariables.FloatVariables = original; }
                });
                check("belt visual: render clone owns every bone and contains no native logic", () =>
                {
                    bind(); view = Call("CreatePartBeltView", source!, parent);
                    var clone = (GameObject)Get(view!, "Root");
                    var copy = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true)[0];
                    Require(clone.transform.parent == parent && clone.GetComponentsInChildren<PlayMakerFSM>(true).Length == 0
                        && clone.GetComponentsInChildren<Rigidbody>(true).Length == 0
                        && clone.GetComponentsInChildren<Collider>(true).Length == 0
                        && copy != null && copy.bones.Length == 2 && copy.rootBone.IsChildOf(clone.transform)
                        && copy.bones[0].IsChildOf(clone.transform) && copy.bones[1].IsChildOf(clone.transform)
                        && !ReferenceEquals(copy.bones[1], bone) && ReferenceEquals(copy.sharedMesh, renderer.sharedMesh)
                        && clone.GetComponentsInChildren<AudioSource>(true).Length == 1,
                        "Clone retained external bones or native simulation components.");
                });
                check("belt visual: guest UV scroll uses an owned material", () =>
                {
                    var owned = (Material)Get(view!, "Material"); var original = renderer.sharedMaterial.mainTextureOffset;
                    Require(!ReferenceEquals(owned, renderer.sharedMaterial), "Guest view retained the native mutable material.");
                    Apply(view!, new PartBeltVisualState { Visible = true, Scale = 1, ScrollSpeed = -.25f }, .4f);
                    Require(Near(owned.mainTextureOffset.x, .9f) && owned.mainTextureOffset.y == 0
                        && renderer.sharedMaterial.mainTextureOffset == original, "Guest UV scroll mutated the native material.");
                });
                check("belt visual: stopped guest pose and sound leave host state untouched", () =>
                {
                    float wear = mount.FsmVariables.FindFsmFloat("Wear").Value; string active = jumping.ActiveStateName;
                    var original = bone.localScale; int failures = breakoffs;
                    Apply(view!, new PartBeltVisualState { Visible = true, Scale = .7f, Pitch = 1.3f, Volume = .8f }, .1f);
                    Require(((Transform)Get(view!, "Bone")).localScale == new Vector3(.7f, 1, .7f)
                        && !((AudioSource)Get(view!, "Audio")).isPlaying
                        && bone.localScale == original && jumping.ActiveStateName == active
                        && wear == mount.FsmVariables.FindFsmFloat("Wear").Value && failures == breakoffs,
                        "Cosmetic stopped state moved or mutated the native belt.");
                });
                check("belt visual: guest pulses and damaged squeal never consume wear", () =>
                {
                    float wear = mount.FsmVariables.FindFsmFloat("Wear").Value; int failures = breakoffs;
                    bool sawRest = false, sawPulse = false;
                    var state = new PartBeltVisualState { Visible = true, Running = true, Scale = .55f, Pitch = 1.4f, Volume = 1 };
                    for (int i = 0; i < 200; i++)
                    {
                        Apply(view!, state, .01f); float value = ((Transform)Get(view!, "Bone")).localScale.x;
                        sawRest |= Near(value, 1); sawPulse |= Near(value, .55f);
                        Require(Near(value, 1) || Near(value, .55f), "Guest pulse exceeded the native two poses.");
                    }
                    var sound = (AudioSource)Get(view!, "Audio");
                    Require(sawRest && sawPulse && Near(sound.pitch, 1.4f) && sound.volume == 1
                        && wear == mount.FsmVariables.FindFsmFloat("Wear").Value && failures == breakoffs,
                        "Guest animation failed to pulse or invoked native wear/failure.");
                });
                check("belt visual: hidden receipt stops and hides the guest view", () =>
                {
                    Apply(view!, new PartBeltVisualState(), .1f);
                    Require(!((GameObject)Get(view!, "Root")).activeSelf && !((AudioSource)Get(view!, "Audio")).isPlaying,
                        "Hidden belt retained a visible or sounding replica.");
                });
                check("belt visual: isolation blocks native events and restores a running source", () =>
                {
                    visual.SetActive(true); renderer.enabled = true; audio.mute = false;
                    NativeBagPartChecks.Start(jumping); NativeBagPartChecks.Fire(jumping, "Probe idle");
                    float wear = mount.FsmVariables.FindFsmFloat("Wear").Value; int failures = breakoffs;
                    bool restart = jumping.Fsm.RestartOnEnable;
                    var isolation = Isolate(source!);
                    try
                    {
                        NativeBagPartChecks.Fire(jumping, "Animate"); NativeBagPartChecks.Fire(jumping, "Belt destroyed");
                        Require(!renderer.enabled && audio.mute && !jumping.enabled
                            && !jumping.Fsm.RestartOnEnable && jumping.ActiveStateName == "Probe idle"
                            && wear == mount.FsmVariables.FindFsmFloat("Wear").Value && failures == breakoffs,
                            "Suppressed native actions ran through direct events.");
                    }
                    finally { isolation.GetType().GetMethod("Restore", Members).Invoke(isolation, null); }
                    Require(renderer.enabled && !audio.mute && jumping.enabled && jumping.Fsm.RestartOnEnable == restart,
                        "Native display flags were not restored.");
                });
                check("belt visual: isolation retains an originally inactive and muted source", () =>
                {
                    visual.SetActive(false); renderer.enabled = false; audio.mute = true; jumping.enabled = false;
                    bool restart = jumping.Fsm.RestartOnEnable;
                    var isolation = Isolate(source!); isolation.GetType().GetMethod("Restore", Members).Invoke(isolation, null);
                    Require(!visual.activeSelf && !renderer.enabled && audio.mute && !jumping.enabled
                        && jumping.Fsm.RestartOnEnable == restart, "Restore enabled a native source that was already inactive.");
                    visual.SetActive(true);
                });
                var reset = NativeBagPartChecks.State(jumping, "Reset").Actions[1];
                check("belt visual: redirected native scale target is rejected", () => RejectChanged(bind, reset, "gameObject",
                    new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Name = "db_Alternator", UseVariable = true, Value = alternatorObject } }));
                check("belt visual: recurring native pose writes are rejected", () => RejectChanged(bind, reset, "everyFrame", true));
                check("belt visual: wrong native pulse variable is rejected", () => RejectChanged(bind, reset, "x",
                    new FsmFloat { Name = "Wear", UseVariable = true, Value = .5f }));
                check("belt visual: external skin bones are rejected before cloning", () =>
                {
                    var original = renderer.bones;
                    try { renderer.bones = new[] { original[0], parent }; Reject(bind); }
                    finally { renderer.bones = original; }
                });
                check("belt visual: unexpected physics in render subtree is rejected", () =>
                {
                    var collider = nodes["FanBelt/Mesh"].AddComponent<BoxCollider>();
                    try { Reject(bind); } finally { UnityEngine.Object.DestroyImmediate(collider); }
                });
                check("belt visual: a native script in render subtree is rejected", () =>
                {
                    var script = EmptyData(nodes["FanBelt/Mesh"]);
                    try { Reject(bind); } finally { UnityEngine.Object.DestroyImmediate(script); }
                });
                check("belt visual: missing looping audio is rejected", () =>
                {
                    audio.loop = false; try { Reject(bind); } finally { audio.loop = true; }
                });
                check("belt visual: releasing a guest view immediately hides it and stops sound", () =>
                {
                    Apply(view!, new PartBeltVisualState { Visible = true, Running = true, Scale = .7f, Volume = .8f }, .1f);
                    Call("ReleasePartBeltView", view!);
                    Require(!((GameObject)Get(view!, "Root")).activeSelf && !((AudioSource)Get(view!, "Audio")).isPlaying,
                        "Released replica stayed visible or audible until deferred destruction.");
                });
                check("belt visual: loose receipt restores item mesh and retires the fitted view", () =>
                {
                    bind(); var loose = Child(root, "loose fanbelt"); var looseMesh = Child(loose, "mesh");
                    var data = EmptyData(loose);
                    data.FsmVariables.GameObjectVariables = new[] { new FsmGameObject { Name = "Mesh", UseVariable = true, Value = looseMesh } };
                    var factory = Nested("ReplacementFactory"); Set(factory, "Rule", factoryRule!);
                    var binding = Nested("ReplacementBinding"); Set(binding, "Factory", factory); Set(binding, "Data", data);
                    Set(binding, "Replica", true); Set(binding, "FittedPresentation", false);
                    var fitted = Call("CreatePartBeltView", source!, parent)!; Set(binding, "BeltView", fitted);
                    looseMesh.SetActive(false);
                    var sync = Activator.CreateInstance(Items, Members, null, new object?[] { null }, null);
                    Items.GetMethod("ApplyPartBeltVisual", Members).Invoke(sync, new object?[] { binding, new ReplacementPartState(), null });
                    Require(looseMesh.activeSelf && Get(binding, "BeltView") == null
                        && !((GameObject)Get(fitted, "Root")).activeSelf, "Loose reconciliation left the packaged mesh hidden or fitted model alive.");
                });
                foreach (string change in new[] { "loose", "retired", "absent visual", "parent mismatch", "new cosmetics" })
                {
                    string current = change;
                    check("belt visual: accepted " + current + " refreshes before materialization", () =>
                        CheckRefresh(current, source!, factoryRule!, parent));
                }
                check("belt visual: unavailable cosmetics preserve the complete saved native attachment", () =>
                {
                    var item = Child(root, "saved fanbelt"); var data = EmptyData(item);
                    data.FsmVariables.GameObjectVariables = new[] { new FsmGameObject { Name = "InstallPoint", UseVariable = true, Value = root } };
                    mount.FsmVariables.FindFsmGameObject("AssemblyPoint").Value = root;
                    mount.FsmVariables.FindFsmGameObject("ActivePart").Value = item;
                    mount.FsmVariables.FindFsmBool("Installed").Value = true;
                    var factory = Nested("ReplacementFactory"); Set(factory, "Rule", factoryRule!);
                    visual.SetActive(true); renderer.enabled = true; audio.mute = false; NativeBagPartChecks.Start(jumping);
                    string active = mount.ActiveStateName;
                    try
                    {
                        foreach (bool missingClip in new[] { true, false })
                        {
                            var originalMaterial = renderer.sharedMaterial;
                            var originalClip = audio.clip;
                            var sync = Activator.CreateInstance(Items, Members, null, new object?[] { null }, null);
                            try
                            {
                                if (missingClip) audio.clip = null; else renderer.sharedMaterial = null;
                                bool isolated = (bool)Items.GetMethod("TryIsolateGuestMount", Members).Invoke(sync, new object[] { data, factory });
                                Require(!isolated && item.transform.parent == root.transform && mount.enabled
                                    && mount.ActiveStateName == active && jumping.enabled && renderer.enabled && !audio.mute
                                    && ((IDictionary)Get(sync, "_isolatedGuestMounts")).Count == 0,
                                    "Failed cosmetic binding partially isolated or changed the saved native attachment.");
                            }
                            finally { audio.clip = originalClip; renderer.sharedMaterial = originalMaterial; }
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(item); }
                });
            }
            finally
            {
                if (view != null) Call("ReleasePartBeltView", view);
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(scrollRoot); globalRpm.Value = savedRpm;
            }
        }

        private static void LoadJumping(PlayMakerFSM fsm, Dictionary<string, object> row, AudioSource audio)
        {
            var properties = new Dictionary<string, Dictionary<int, Dictionary<string, object>>>();
            var names = new List<string>();
            foreach (Dictionary<string, object> state in (IEnumerable)row["states"])
            {
                string name = (string)state["name"]; names.Add(name);
                var actions = (List<object>)state["actions"];
                for (int i = 0; i < actions.Count; i++)
                {
                    var action = (Dictionary<string, object>)actions[i];
                    foreach (Dictionary<string, object> parameter in (IEnumerable)action["parameters"])
                        if ((string)parameter["type"] == "FsmProperty")
                        {
                            if (!properties.ContainsKey(name)) properties.Add(name, new Dictionary<int, Dictionary<string, object>>());
                            properties[name].Add(i, (Dictionary<string, object>)parameter["value"]);
                        }
                    if (properties.ContainsKey(name) && properties[name].ContainsKey(i))
                        action["parameters"] = new List<object>();
                }
            }
            NativeBagPartChecks.LoadActions(fsm, row, names.ToArray());
            foreach (var state in properties)
                foreach (var entry in state.Value)
                {
                    var data = entry.Value;
                    var floatValue = (Dictionary<string, object>)data["FloatParameter"];
                    var boolValue = (Dictionary<string, object>)data["BoolParameter"];
                    var property = new FsmProperty { TargetObject = new FsmObject { Value = audio, ObjectType = typeof(AudioSource) },
                        TargetTypeName = (string)data["TargetTypeName"], TargetType = typeof(AudioSource),
                        PropertyName = (string)data["PropertyName"], setProperty = Convert.ToBoolean(data["setProperty"]),
                        FloatParameter = Convert.ToBoolean(floatValue["useVariable"])
                            ? fsm.FsmVariables.FindFsmFloat((string)floatValue["name"])
                            : new FsmFloat(Convert.ToSingle(floatValue["value"])),
                        BoolParameter = new FsmBool(Convert.ToBoolean(boolValue["value"])) };
                    var action = NativeBagPartChecks.State(fsm, state.Key).Actions[entry.Key];
                    action.GetType().GetField("targetProperty").SetValue(action, property);
                    action.Init(NativeBagPartChecks.State(fsm, state.Key));
                }
        }

        private static void CheckGuestSource(PlayMakerFSM mount, PlayMakerFSM jumping, PlayMakerFSM scroll,
            object rule, SkinnedMeshRenderer renderer, AudioSource audio, Transform parent)
        {
            var sync = Activator.CreateInstance(Items, Members, null, new object?[] { null }, null);
            var getSource = Items.GetMethod("GetPartBeltSource", Members);
            bool enabled = jumping.enabled, restart = jumping.Fsm.RestartOnEnable;
            bool rendered = renderer.enabled, muted = audio.mute;
            bool scrollEnabled = scroll.enabled, scrollStarted = scroll.Fsm.Started;
            string active = scroll.ActiveStateName;
            float wear = mount.FsmVariables.FindFsmFloat("Wear").Value;
            var originalOffset = renderer.sharedMaterial.mainTextureOffset;
            object? view = null;
            try
            {
                var source = getSource.Invoke(sync, new object[] { mount, rule, true });
                Require(source != null && Get(source, "Scroll") == null,
                    "Guest isolation consulted its own engine scroll inputs.");
                for (int i = 0; i < 25; i++)
                    Require(ReferenceEquals(source, getSource.Invoke(sync, new object[] { mount, rule, true })),
                        "Repeated isolation lost its native belt source.");
                Require(!jumping.enabled && !jumping.Fsm.RestartOnEnable && !renderer.enabled && audio.mute,
                    "Skipping scroll lookup weakened native belt containment.");
                view = Call("CreatePartBeltView", source!, parent);
                Apply(view!, new PartBeltVisualState { Visible = true, Scale = 1, ScrollSpeed = -.25f }, .4f);
                Require(Near(((Material)Get(view!, "Material")).mainTextureOffset.x, .9f)
                    && renderer.sharedMaterial.mainTextureOffset == originalOffset
                    && mount.FsmVariables.FindFsmFloat("Wear").Value == wear
                    && scroll.enabled == scrollEnabled && scroll.Fsm.Started == scrollStarted && scroll.ActiveStateName == active,
                    "Guest host-rate projection changed native scroll, material or wear.");
            }
            finally
            {
                if (view != null) Call("ReleasePartBeltView", view);
                foreach (object isolation in ((IDictionary)Get(sync, "_partBeltIsolation")).Values)
                    isolation.GetType().GetMethod("Restore", Members).Invoke(isolation, null);
            }
            Require(jumping.enabled == enabled && jumping.Fsm.RestartOnEnable == restart
                && renderer.enabled == rendered && audio.mute == muted, "Guest source did not restore native presentation flags.");
        }

        private static void CheckRefresh(string change, object source, object factoryRule, Transform parent)
        {
            var item = Child(parent.gameObject, "deferred fanbelt"); var data = EmptyData(item);
            var factory = Nested("ReplacementFactory"); Set(factory, "Rule", factoryRule);
            var binding = Nested("ReplacementBinding"); Set(binding, "Factory", factory); Set(binding, "Data", data);
            Set(binding, "Replica", true); Set(binding, "FittedPresentation", true);
            var view = Call("CreatePartBeltView", source, parent)!; Set(binding, "BeltView", view);
            var lifecycle = new ItemSpawnLifecycle();
            var replica = new ReplacementPartReplica(new[] { new ReplacementPartRule(17, "FANBELT0", 2, 1, true) }, lifecycle);
            var state = new ReplacementPartState { Revision = 1, PresentationRevision = 1, FactoryId = 17, NativeId = "FANBELT07",
                Rotation = NetQuaternion.Identity, Scalars = new[] { 80f, 0f }, AssemblyId = 1, Installed = true, ParentKind = PartParentKind.Vehicle, ParentId = 91,
                ParentPath = "VINP_FanBelt", BeltVisual = new PartBeltVisualState { Visible = true, Running = true, Scale = .7f, Volume = .8f } };
            try
            {
                Require(replica.Receive(state, out uint id), "Initial belt receipt was rejected.");
                Apply(view, state.BeltVisual!, .1f);
                Transform? nextParent = parent;
                state = replica.Get(id)!; state.Revision = 2; state.PresentationRevision = 2;
                if (change == "retired") lifecycle.Retire(id);
                else
                {
                    if (change == "loose") state = new ReplacementPartState { Revision = 2, PresentationRevision = 2,
                        FactoryId = 17, NativeId = "FANBELT07", Rotation = NetQuaternion.Identity, Scalars = new[] { 80f, 0f } };
                    else if (change == "absent visual") state.BeltVisual = null;
                    else if (change == "parent mismatch") { state.ParentId = 92; nextParent = parent.parent; }
                    else { state.Revision = 1; state.BeltVisual = new PartBeltVisualState { Visible = true, Scale = .5f, ScrollSpeed = -10 }; }
                    Require(replica.Receive(state, out id), "Updated belt receipt was rejected.");
                }
                var latest = replica.Get(id);
                bool kept = (bool)Call("RefreshPartBeltView", binding, latest, nextParent)!;
                Require(item.transform.parent == parent && (bool)Get(binding, "FittedPresentation"),
                    "Fixture unexpectedly materialized the newer native attachment.");
                if (change == "new cosmetics")
                {
                    var captured = (PartBeltVisualState)Get(view, "State");
                    Require(kept && ReferenceEquals(Get(binding, "BeltView"), view) && Near(captured.Scale, .5f)
                        && !captured.Running && captured.ScrollSpeed == -10 && !ReferenceEquals(captured, latest!.BeltVisual),
                        "Same-parent receipt retained stale cosmetics or a mutable network object.");
                }
                else Require(!kept && Get(binding, "BeltView") == null && !((GameObject)Get(view, "Root")).activeSelf
                    && !((AudioSource)Get(view, "Audio")).isPlaying, "Latest accepted authority left the old fitted view running.");
            }
            finally { Call("ReleasePartBeltView", view); UnityEngine.Object.DestroyImmediate(item); }
        }

        private sealed class Counter : FsmStateAction
        {
            private readonly Action _increment;
            internal Counter(Action increment) { _increment = increment; }
            public override void OnEnter() { _increment(); Finish(); }
        }
        private static PlayMakerFSM EmptyData(GameObject owner)
        {
            var fsm = owner.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
            typeof(PlayMakerFSM).GetField("fsm", Members).SetValue(fsm, new Fsm());
            fsm.Fsm.Name = "Data"; fsm.Fsm.StartState = "Probe idle";
            fsm.Fsm.States = new[] { new FsmState(fsm.Fsm) { Name = "Probe idle", Actions = new FsmStateAction[0] } };
            return fsm;
        }
        private static object Isolate(object source) => Activator.CreateInstance(Items.GetNestedType("PartBeltIsolation", BindingFlags.NonPublic),
            Members, null, new[] { source }, null);
        private static void Apply(object view, PartBeltVisualState state, float delta) => Call("ApplyPartBeltView", view, state, delta, delta);
        private static object Get(object target, string name) => target.GetType().GetField(name, Members).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Members).SetValue(target, value);
        private static object Nested(string name) => Activator.CreateInstance(Items.GetNestedType(name, BindingFlags.NonPublic), true);
        private static object? Call(string name, params object?[] args) => Items.GetMethod(name, Static).Invoke(null, args);
        private static GameObject Child(GameObject parent, string name)
        { var child = new GameObject(name); child.transform.SetParent(parent.transform, false); return child; }
        private static Vector3 Vector(Dictionary<string, object> value) => new Vector3(Convert.ToSingle(value["x"]), Convert.ToSingle(value["y"]), Convert.ToSingle(value["z"]));
        private static Quaternion Rotation(Dictionary<string, object> value) => new Quaternion(Convert.ToSingle(value["x"]), Convert.ToSingle(value["y"]), Convert.ToSingle(value["z"]), Convert.ToSingle(value["w"]));
        private static bool Near(float a, float b) => Math.Abs(a - b) < .0001f;
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static void Reject(Action action)
        {
            try { action(); }
            catch (TargetInvocationException e) { if (e.InnerException is InvalidOperationException) return; throw; }
            throw new InvalidOperationException("Changed native binding was accepted.");
        }
        private static void RejectChanged(Action verify, object action, string field, object replacement)
        {
            verify(); var member = action.GetType().GetField(field); var before = member.GetValue(action);
            try { member.SetValue(action, replacement); Reject(verify); }
            finally { member.SetValue(action, before); }
        }
    }
}
