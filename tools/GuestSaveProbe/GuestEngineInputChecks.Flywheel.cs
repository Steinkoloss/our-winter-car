using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private sealed partial class Fixture
        {
            internal List<object> FlywheelRows => _rows;
            internal void ConfigureStarterFlywheelDecisions()
            {
                var state = NativeBagPartChecks.State(Reader, "Check Flywheel");
                state.Actions[1] = Import("Check Flywheel", 1); state.Actions[1].Init(state);
                RestoreNativeTransitions("Check Flywheel");
            }

            internal void RelayFlywheelState(Variant part, ReplacementPartState state)
            {
                state.Revision = ++part.Revision;
                Call(Sync, "OnReplacementPartState", PacketCodec.Decode(PacketCodec.Encode(state)));
                Require(((ReplacementPartReplica)Get(Sync, "_replacementReplica")).Get(part.PartId)?.Revision == part.Revision, "Flywheel state was not accepted.");
                ((HashSet<uint>)Get(Sync, "_pendingReplacements")).Remove(part.PartId);
                Set(part.Binding, "HasAppliedState", true); Set(part.Binding, "AppliedRevision", part.Revision); Set(part.Binding, "FittedPresentation", true);
            }

            internal Component ConfigureFlywheelDecisions()
            {
                var obj = Child(Extras, "flywheel drivetrain"); obj.SetActive(false);
                var type = Type.GetType("Drivetrain, Assembly-CSharp", true);
                var drive = obj.AddComponent(type); ((Behaviour)drive).enabled = false;
                ((Behaviour)obj.GetComponent(Type.GetType("Axles, Assembly-CSharp", true))).enabled = false;
                Reader.FsmVariables.ObjectVariables = new[] { new FsmObject { Name = "CarDrivetrain", UseVariable = true, ObjectType = type, Value = drive } };
                obj.SetActive(true);
                var shake = Empty(Child(Extras, "native shake input"), "MotorShake");
                shake.FsmVariables.FloatVariables = new[] { new FsmFloat { Name = "Flywheel", UseVariable = true } };
                NativeBagPartChecks.Start(shake);
                var state = NativeBagPartChecks.State(Reader, "Flywheel");
                for (int i = 0; i < state.Actions.Length; i++)
                    if (i != 0 && i != 2) { state.Actions[i] = Import("Flywheel", i); state.Actions[i].Init(state); }
                ((FsmOwnerDefault)Get(state.Actions[5], "gameObject")).GameObject.Value = shake.gameObject;
                RestoreNativeTransitions("Flywheel");
                NativeBagPartChecks.State(Reader, "Ignition").Transitions = new FsmTransition[0];
                ReceiveVariant(Auxiliary("VIN131").Variants[0], 90, 0, 8, true);
                return drive;
            }
        }

        internal static void RunStarterFlywheel(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN130", "Starter", "starter-flywheel-engine-input-probe.json"))
            {
                f.ConfigureStarterFlywheelDecisions();
                var input = f.Auxiliary("VIN120"); var stock = input.Variants[0];
                var read = f.Action("Check Flywheel", 0); var originalOwner = Get(read, "gameObject");
                var output = f.Reader.FsmVariables.FindFsmBool("Installed2");
                var replica = (ReplacementPartReplica)Get(f.Sync, "_replacementReplica");
                replica.Clear(); f.Receive(90, 8, .7f, true);
                Action<bool> decision = installed => {
                    Require(f.Prepare(), "Starter flywheel preparation failed."); f.Fire("Check Flywheel");
                    Require(output.Value == installed && f.Reader.ActiveStateName == (installed ? "Prepare starting" : "No Flywheel"),
                        "Native starter flywheel decision diverged from the applied host part."); f.AssertSaved(); };
                check("starter flywheel: saved native read warms before host projection", () =>
                {
                    read.OnEnter(); Require(output.Value && ReferenceEquals(Get(read, "goLastFrame"), input.Mount.gameObject), "Saved flywheel cache did not warm."); f.AssertSaved();
                });
                check("starter flywheel: missing host state takes the native no-flywheel branch", () =>
                {
                    decision(false); var proxy = f.Target(read); var data = proxy.GetComponent<PlayMakerFSM>();
                    Require(proxy != input.Mount.gameObject && !data.enabled && data.FsmVariables.FloatVariables.Length == 0
                        && data.FsmVariables.BoolVariables.Length == 1 && proxy.GetComponent<Rigidbody>() == null,
                        "Starter input borrowed saved data or acquired physics.");
                    Require(f.Reader.FsmVariables.FindFsmGameObject("db_Flywheel").Value == input.Mount.gameObject,
                        "Projection changed the shared native source variable.");
                    Require(f.Target(f.Action("Fuel Mixture", 11)) == f.Mount.gameObject,
                        "Flywheel projection altered protected starter wear writes.");
                    f.Action("Fuel Mixture", 11).OnUpdate(); f.AssertSaved();
                });
                foreach (var variant in input.Variants)
                {
                    string prefix = (string)Get(Get(variant.Factory, "Rule"), "Prefix");
                    check("starter flywheel: " + prefix + " waits for application and follows native installation without wear or bolt thresholds", () =>
                    {
                        f.ReceiveVariant(variant, 0, 0, .093f, false); decision(false);
                        f.ReceiveVariant(variant, 0, 0, .093f, true); decision(true);
                        var state = replica.Get(variant.PartId)!; state.Scalars[1] = 0; f.RelayFlywheelState(variant, state); decision(true);
                        Require(f.Target(read) != variant.Part.gameObject, "Starter bypassed the accepted state proxy.");
                    });
                    check("starter flywheel: " + prefix + " removal defeats delayed fitted packets and old presentation", () =>
                    {
                        var fitted = replica.Get(variant.PartId)!;
                        f.ReceiveVariant(variant, 0, 0, .093f, false, false); decision(false);
                        Require(variant.Part.transform.parent == input.Mount.transform, "Fixture unexpectedly moved removed presentation.");
                        Call(f.Sync, "OnReplacementPartState", PacketCodec.Decode(PacketCodec.Encode(fitted))); decision(false);
                        Require(replica.Get(variant.PartId)!.Revision == variant.Revision, "A stale fit replaced removal.");
                    });
                }
                check("starter flywheel: refit and removal preserve scratch until the next native attempt", () =>
                {
                    f.ReceiveVariant(stock, 90, 0, .1f, true); decision(true);
                    var proxy = f.Target(read); string state = f.Reader.ActiveStateName;
                    f.ReceiveVariant(stock, 90, 0, .1f, false, false); Require(f.Prepare(), "Removal failed.");
                    Require(output.Value && f.Reader.ActiveStateName == state && f.Target(read) == proxy, "Removal replayed or rewrote an in-progress attempt.");
                    decision(false);
                    f.ReceiveVariant(stock, 90, 0, .1f, true); Require(f.Prepare(), "Refit failed.");
                    Require(!output.Value && f.Reader.ActiveStateName == "No Flywheel", "Refit manufactured a new attempt."); decision(true);
                });
                foreach (string fault in new[] { "pending", "revision", "hidden", "inactive", "identity", "parent", "unapplied", "ownership" })
                {
                    check("starter flywheel: " + fault + " cannot satisfy installation", () =>
                    {
                        f.ReceiveVariant(stock, 90, 0, .1f, true);
                        var parent = stock.Part.transform.parent;
                        if (fault == "pending") ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Add(stock.PartId);
                        if (fault == "revision") Set(stock.Binding, "AppliedRevision", stock.Revision - 1);
                        if (fault == "hidden") Set(stock.Binding, "FittedPresentation", false);
                        if (fault == "inactive") stock.Part.gameObject.SetActive(false);
                        if (fault == "identity") stock.Part.FsmVariables.FindFsmString("ID").Value = "VIN12099";
                        if (fault == "parent") stock.Part.transform.SetParent(f.Extras.transform, false);
                        if (fault == "unapplied") Set(stock.Binding, "HasAppliedState", false);
                        if (fault == "ownership") Set(stock.Binding, "Replica", false);
                        try { decision(false); }
                        finally
                        {
                            stock.Part.gameObject.SetActive(true); stock.Part.transform.SetParent(parent, false);
                            stock.Part.FsmVariables.FindFsmString("ID").Value = stock.NativeId; Set(stock.Binding, "Replica", true);
                            f.ReceiveVariant(stock, 90, 0, .1f, true); decision(true);
                        }
                    });
                }
                foreach (string fault in new[] { "kind", "id", "path" })
                    check("starter flywheel: wrong attachment " + fault + " is unavailable", () =>
                    {
                        var state = replica.Get(stock.PartId)!;
                        if (fault == "kind") state.ParentKind = PartParentKind.Vehicle;
                        if (fault == "id") state.ParentId++;
                        if (fault == "path") state.ParentPath = "CrankshaftParent/VINP_CrankPulley/Other";
                        f.RelayFlywheelState(stock, state);
                        try { decision(false); } finally { f.ReceiveVariant(stock, 90, 0, .1f, true); decision(true); }
                    });
                check("starter flywheel: conflicting host variants block cranking until the conflict clears", () =>
                {
                    var other = input.Variants[1]; f.ReceiveVariant(other, 90, 0, .085f, false); decision(false);
                    f.ReceiveVariant(other, 90, 0, .085f, false, false); decision(true);
                });
                foreach (string field in new[] { "variableName", "fsmName", "everyFrame", "storeValue" })
                    check("starter flywheel: changed " + field + " pauses and repairs the native consumer", () =>
                    {
                        var previous = Get(read, field);
                        Set(read, field, field == "everyFrame" ? (object)true : field == "storeValue"
                            ? new FsmBool { Name = "Installed2", UseVariable = true } : new FsmString { Value = "Other" });
                        try { Require(!f.Prepare() && !f.Reader.enabled, "Changed flywheel reader escaped protection."); f.AssertSaved(); }
                        finally { Set(read, field, previous); if (!f.Prepare()) f.Prepare(); }
                        decision(true);
                    });
                check("starter flywheel: variants must agree on the live mount", () =>
                {
                    var mount = ((PlayMakerFSM)Get(input.Variants[2].Factory, "Fsm")).FsmVariables.FindFsmGameObject("VINP");
                    var previous = mount.Value; mount.Value = f.Mount.gameObject;
                    try { Require(!f.Prepare() && !f.Reader.enabled, "Disagreeing variant factory escaped protection."); f.AssertSaved(); }
                    finally { mount.Value = previous; if (!f.Prepare()) f.Prepare(); }
                    decision(true);
                });
                check("starter flywheel: recreated proxy discards the native saved-target cache", () =>
                {
                    var proxy = f.Target(read); UnityEngine.Object.DestroyImmediate(proxy.GetComponent<PlayMakerFSM>());
                    decision(true); Require(f.Target(read) != proxy, "Destroyed proxy Data remained cached.");
                });
                check("starter flywheel: moving the block into the car preserves its native mount address", () =>
                { f.Anchor.transform.SetParent(f.Car.transform, false); decision(true); });
                check("starter flywheel: starter and combustion agree across variant replacement and removal", () =>
                {
                    var cylinders = f.AddConsumer("Cylinders"); var combustionRead = NativeBagPartChecks.State(cylinders, "Flywheel").Actions[0];
                    var combustionOutput = cylinders.FsmVariables.FindFsmBool("Installed1");
                    foreach (var variant in input.Variants)
                    {
                        foreach (var candidate in input.Variants) f.ReceiveVariant(candidate, 90, 0, .1f, false, false);
                        f.ReceiveVariant(variant, 90, 0, .1f, true); decision(true); combustionRead.OnEnter();
                        Require(combustionOutput.Value && f.Target(read) != f.Target(combustionRead), "Consumers disagreed or shared calculation storage.");
                        f.ReceiveVariant(variant, 90, 0, .1f, false, false); decision(false); combustionRead.OnEnter();
                        Require(!combustionOutput.Value, "Combustion retained a removed starter flywheel.");
                    }
                    f.AssertSaved();
                });
                check("starter flywheel: disconnect restores saved owners and invalidates accepted state", () =>
                {
                    Call(f.Sync, "ReleaseSession"); Require(ReferenceEquals(Get(read, "gameObject"), originalOwner), "Disconnect retained flywheel proxy ownership.");
                    read.OnEnter(); Require(output.Value && f.Target(read) == input.Mount.gameObject, "Restored reader retained proxy cache.");
                    Require(Get(f.Sync, "_replacementReplica") == null || ((ReplacementPartReplica)Get(f.Sync, "_replacementReplica")).Get(stock.PartId) == null,
                        "Accepted flywheel state survived disconnect."); f.AssertSaved();
                });
            }
        }

        internal static void RunFlywheel(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN120", "Cylinders"))
            {
                var drive = f.ConfigureFlywheelDecisions();
                var shake = f.Target(f.Action("Flywheel", 5)).GetComponent<PlayMakerFSM>();
                Action saved = () => f.AssertSaved();
                Action<float> inertia = value => Require(Near(f.Reader.FsmVariables.FindFsmFloat("EngineInertia").Value, value), "Flywheel inertia input diverged.");
                Action absent = () => { Require(!f.Installed, "Unavailable flywheel remained installed."); inertia(0); saved(); };
                foreach (string prefix in new[] { "VIN120", "FLYWHEELa0", "FLYWHEELb0", "VIN138" }) RunFlywheelFactory(f, prefix, check);
                check("flywheel: saved readers warm native caches before projection", () =>
                {
                    f.ReadAll(); Require(f.Installed, "Saved flywheel baseline missing."); inertia(.2f);
                    foreach (var read in f.Readers) Require(ReferenceEquals(Get(read, "goLastFrame"), f.Mount.gameObject), "Flywheel reader cache did not warm."); saved();
                });
                check("flywheel: absent and pending state cannot supply local save inertia", () =>
                {
                    Require(f.Prepare(), "Flywheel binding failed."); f.ReadAll(); absent();
                    f.Receive(90, 0, .1f, false); Require(f.Prepare(), "Pending flywheel failed."); f.ReadAll(); absent();
                    f.MarkApplied(); Require(f.Prepare(), "Applied flywheel failed."); f.ReadAll(); Require(f.Installed, "Host flywheel remained absent."); inertia(.1f); saved();
                });
                check("flywheel: native inertia and shake calculations follow host values", () =>
                {
                    f.Fire("Flywheel");
                    Require(f.Reader.ActiveStateName == "Ignition" && Near((float)Get(drive, "engineInertia"), .1f)
                        && Near(shake.FsmVariables.FindFsmFloat("Flywheel").Value, .4f), "Native drivetrain/shake outputs diverged."); saved();
                });
                check("flywheel: update waits for native read without replaying an active engine state", () =>
                {
                    var proxy = f.Proxy; string active = f.Reader.ActiveStateName;
                    f.Receive(90, 0, .093f, true); Require(f.Prepare(), "Host inertia change failed.");
                    Require(f.Proxy == proxy && f.Reader.ActiveStateName == active && Near((float)Get(drive, "engineInertia"), .1f), "Projection replayed engine outputs.");
                    inertia(.1f); f.Fire("Flywheel"); inertia(.093f);
                    Require(Near((float)Get(drive, "engineInertia"), .093f) && Near(shake.FsmVariables.FindFsmFloat("Flywheel").Value, .04f / .093f), "Native outputs ignored actual host inertia."); saved();
                });
                check("flywheel: moving the assembled block preserves the nested live mount", () =>
                {
                    var parent = f.Anchor.transform.parent; f.Anchor.transform.SetParent(f.Car.transform, false);
                    try { Require(f.Prepare(), "Moved block rejected live flywheel mount."); f.ReadAll(); Require(f.Installed, "Moved block lost flywheel."); inertia(.093f); saved(); }
                    finally { f.Anchor.transform.SetParent(parent, false); }
                });
                check("flywheel: native removal stops starting before dividing by absent inertia", () =>
                {
                    f.Receive(90, 0, .093f, true, false); Require(f.Prepare(), "Flywheel removal failed.");
                    f.Fire("Flywheel");
                    Require(f.Reader.ActiveStateName == "Not Ok" && f.Starter.FsmVariables.FindFsmBool("ShutOff").Value
                        && Near((float)Get(drive, "engineInertia"), .093f), "Absent flywheel bypassed the native stop branch.");
                    f.ReadAll(); absent();
                });
                for (int i = 0; i < f.FlywheelVariants.Count; i++)
                {
                    int index = i; var variant = f.FlywheelVariants[i]; float value = i == 0 ? .085f : i == 1 ? .072f : .11f;
                    check("flywheel: " + variant.NativeId + " supplies its own inertia through the shared mount", () =>
                    {
                        if (index > 0) f.ReceiveVariant(f.FlywheelVariants[index - 1], 90, 0, .085f, false, false);
                        f.ReceiveVariant(variant, 90, 0, value, true); f.AssertVariantReady(variant);
                        Require(f.Prepare(), "Flywheel variant failed."); f.Fire("Flywheel"); inertia(value);
                        Require(f.Reader.ActiveStateName == "Ignition" && Near((float)Get(drive, "engineInertia"), value)
                            && Near(shake.FsmVariables.FindFsmFloat("Flywheel").Value, .04f / value), "Native lighter-flywheel calculation diverged."); saved();
                    });
                }
                check("flywheel: conflicting stock attachment blocks the lightweight input", () =>
                {
                    f.Receive(90, 0, .1f, false); f.Prepare(); f.ReadAll(); absent();
                    f.Receive(90, 0, .1f, false, false); Require(f.Prepare(), "Flywheel conflict did not clear."); f.ReadAll(); inertia(.11f); Require(f.Installed, "Unique flywheel stayed absent."); saved();
                    f.ReceiveVariant(f.FlywheelVariants[2], 90, 0, .11f, false, false); f.Receive(90, 0, .1f, true); f.Prepare(); f.ReadAll();
                });
                foreach (string change in new[] { "pending", "revision", "identity", "parent", "hidden", "inactive" })
                {
                    string fault = change;
                    check("flywheel: " + fault + " cannot supply unapplied host values", () =>
                    {
                        f.Receive(90, 0, .1f, true);
                        if (fault == "pending") ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Add(f.PartId);
                        if (fault == "revision") Set(f.Binding, "AppliedRevision", 0u);
                        if (fault == "identity") f.Part.FsmVariables.FindFsmString("ID").Value = "VIN12099";
                        if (fault == "parent") f.Part.transform.SetParent(f.Extras.transform, false);
                        if (fault == "hidden") Set(f.Binding, "FittedPresentation", false);
                        if (fault == "inactive") f.Part.gameObject.SetActive(false);
                        try { Require(f.Prepare(), "Unavailable flywheel preparation failed."); f.ReadAll(); absent(); }
                        finally { f.Part.gameObject.SetActive(true); f.Part.transform.SetParent(f.Mount.transform, false); f.Part.FsmVariables.FindFsmString("ID").Value = "VIN1207"; f.MarkApplied(); f.Prepare(); }
                    });
                }
                foreach (var variant in f.FlywheelVariants)
                {
                    var chosen = variant;
                    check("flywheel: changed " + chosen.NativeId + " factory mount pauses combustion and recovers", () =>
                    {
                        var reference = ((PlayMakerFSM)Get(chosen.Factory, "Fsm")).FsmVariables.FindFsmGameObject("VINP"); reference.Value = f.Original.gameObject;
                        try { Require(!f.Prepare() && !f.Reader.enabled, "Disagreeing flywheel mount escaped validation."); saved(); }
                        finally { reference.Value = f.Mount.gameObject; f.Prepare(); }
                        Require(f.Reader.enabled, "Repaired flywheel mount remained paused.");
                    });
                }
                foreach (string field in new[] { "variableName", "fsmName", "everyFrame", "storeValue" })
                {
                    string name = field;
                    check("flywheel: changed inertia " + name + " pauses and repairs combustion", () =>
                    {
                        var read = f.Readers[1]; var original = Get(read, name);
                        Set(read, name, name == "everyFrame" ? (object)true : name == "storeValue" ? new FsmFloat { Name = "EngineInertia", UseVariable = true } : new FsmString { Value = "Other" });
                        try { Require(!f.Prepare() && !f.Reader.enabled, "Changed flywheel reader escaped validation."); saved(); }
                        finally { Set(read, name, original); f.Prepare(); }
                        Require(f.Reader.enabled, "Repaired inertia read stayed paused.");
                    });
                }
                check("flywheel: destroyed proxy repairs both warmed native reader caches", () =>
                {
                    f.ReadAll(); var proxy = f.Proxy; UnityEngine.Object.DestroyImmediate(f.ProxyData); Require(f.Prepare(), "Flywheel proxy repair failed."); f.ReadAll();
                    Require(f.Proxy != proxy && f.Installed, "Old flywheel proxy survived repair."); inertia(.1f);
                    foreach (var read in f.Readers) Require(ReferenceEquals(Get(read, "goLastFrame"), f.Proxy), "Native reader retained stale inertia cache."); saved();
                });
                foreach (var variant in f.FlywheelVariants)
                    CheckFlywheelCapture(f, variant.Part, variant.Binding, variant.PartId, variant.NativeId, check);
                CheckFlywheelCapture(f, f.Part, f.Binding, f.PartId, "VIN1207", check);
                check("flywheel: disconnect clears host input and cleanup restores saved wrappers", () =>
                {
                    Property(f.Session, "State", SessionState.Idle); Require(f.Prepare(), "Flywheel disconnect failed."); f.ReadAll(); absent();
                    Call(f.Sync, "RestoreGuestEngineInputs");
                    for (int i = 0; i < f.Readers.Length; i++) Require(ReferenceEquals(Get(f.Readers[i], "gameObject"), f.OriginalOwners[i]), "Flywheel cleanup lost native wrappers.");
                    f.ReadAll(); inertia(.2f); Require(f.Installed, "Saved flywheel cache did not restore."); saved();
                });
            }
        }

        private static void CheckFlywheelCapture(Fixture f, PlayMakerFSM part, object binding, uint id, string name, Action<string, Action> check)
        {
            check("flywheel: " + name + " host capture and packet replay retain actual inertia", () =>
            {
                Property(f.Session, "IsHost", true); Set(binding, "Replica", false);
                var value = part.FsmVariables.FindFsmFloat("InertiaFactor"); float original = value.Value;
                try
                {
                    value.Value = .091f; var state = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", id);
                    Require(state != null && state.Scalars.Length == 3 && Near(state.Scalars[2], .091f), "Capture substituted a flywheel prefab default.");
                    var decoded = (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state!)); value.Value = .087f;
                    var changed = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", id);
                    Require(changed != null && changed.Revision != state!.Revision, "Host inertia change did not advance revision.");
                    Call(f.Sync, "ApplyReplacementScalars", binding, decoded); Require(Near(value.Value, .091f), "Replica lost decoded host inertia."); f.AssertSaved();
                }
                finally { value.Value = original; Property(f.Session, "IsHost", false); Set(binding, "Replica", true); }
            });
        }

        private static object? CallStatic(Type type, string name, params object?[] values) => type.GetMethod(name, Static).Invoke(null, values);

        private static void RunFlywheelFactory(Fixture f, string prefix, Action<string, Action> check)
        {
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
            var c = catalog.GetProperty("ReplacementParts", Static).GetValue(null, null); object? rule = null;
            foreach (object candidate in (IEnumerable)Get(c, "Factories")) if ((string)Get(candidate, "Prefix") == prefix) rule = candidate;
            Require(rule != null, "Flywheel factory catalog missing.");
            var root = new GameObject("flywheel native factory probe"); root.SetActive(false);
            var product = Child(root, prefix); product.AddComponent<Rigidbody>().useGravity = false; product.AddComponent<BoxCollider>(); Child(product, "Bolts");
            var partRow = NativeBagPartChecks.Find(f.FlywheelRows, prefix, "Data");
            var factoryRow = NativeBagPartChecks.Find(f.FlywheelRows, (string)Get(rule!, "Path"), "Spawn");
            var mountRow = NativeBagPartChecks.Find(f.FlywheelRows, "CARPARTS/StartParts/VIN1010/CrankshaftParent/VINP_CrankPulley/VINP_FlywheelFlexplate", "Data");
            var part = NativeBagPartChecks.MakeFsm(product, partRow);
            var factory = NativeBagPartChecks.MakeFsm(Child(root, "factory"), factoryRow);
            var mount = NativeBagPartChecks.MakeFsm(Child(root, "native mount"), mountRow);
            var spawned = new List<GameObject>();
            try
            {
                root.SetActive(true);
                part.FsmVariables.ObjectVariables = new[] { new FsmObject { Name = "Collider", UseVariable = true, ObjectType = typeof(BoxCollider), Value = product.GetComponent<BoxCollider>() } };
                NativeBagPartChecks.LoadActions(part, partRow, "Init", "Status", "Another part?", "Tightness?", "Bolted", "Unbolted", "Mouse off", "Mouse over", "Remove");
                NativeBagPartChecks.LoadActions(factory, factoryRow, "Create product", "Create");
                NativeBagPartChecks.LoadActions(mount, mountRow, "Idle", "Allow install?", "Far", "Near", "Install 1");
                factory.FsmVariables.FindFsmGameObject("Prefab").Value = product;
                factory.FsmVariables.FindFsmGameObject("VINP").Value = mount.gameObject;
                factory.FsmVariables.FindFsmString("SaveID").Value = prefix;
                var bound = Nested("ReplacementFactory"); Set(bound, "Rule", rule); Set(bound, "Fsm", factory); Set(bound, "TemplateData", part); Set(bound, "Prefab", product);
                check("flywheel factory " + prefix + ": native template and both outputs validate", () =>
                {
                    CallStatic(Items, "ValidateReplacementTemplate", bound, c);
                    CallStatic(Items, "ValidateReplacementOutput", bound, c, true); CallStatic(Items, "ValidateReplacementOutput", bound, c, false);
                });
                check("flywheel factory " + prefix + ": native mount and fitting request validate", () =>
                {
                    CallStatic(Items, "ValidatePartFitMount", mount, c, false, null);
                    var binding = Nested("ReplacementBinding"); Set(binding, "Factory", bound); Set(binding, "Data", part);
                    CallStatic(Items, "ValidatePartFitEntry", binding); Require((bool)Get(binding, "FitValidated") && !(bool)Get(binding, "FitFailed"), "Native flywheel fitting entry rejected.");
                });
                check("flywheel factory " + prefix + ": native creation preserves distinct IDs mount and inertia", () =>
                {
                    NativeBagPartChecks.Start(factory);
                    for (int i = 1; i <= 2; i++)
                    {
                        NativeBagPartChecks.Fire(factory, "Create product"); var output = factory.FsmVariables.FindFsmGameObject("New").Value; spawned.Add(output);
                        var data = output.GetComponent<PlayMakerFSM>();
                        Require(output.name == prefix + i && data.FsmVariables.FindFsmGameObject("InstallPoint").Value == mount.gameObject
                            && data.FsmVariables.FindFsmFloat("InertiaFactor").Value == part.FsmVariables.FindFsmFloat("InertiaFactor").Value, "Native flywheel output lost identity/reference/inertia.");
                    }
                    Require(spawned[0] != spawned[1], "Flywheel outputs share an object.");
                });
                check("flywheel factory " + prefix + ": native initialization keeps ID and inertia after display rename", () =>
                {
                    product.name = prefix + "7"; float inertia = part.FsmVariables.FindFsmFloat("InertiaFactor").Value;
                    NativeBagPartChecks.Start(part); NativeBagPartChecks.Fire(part, "Init");
                    Require(part.FsmVariables.FindFsmString("ID").Value == prefix + "7"
                        && part.FsmVariables.FindFsmString("UTAssemblyID").Value == prefix + "7AID"
                        && part.FsmVariables.FindFsmString("UTPos").Value == prefix + "7POS"
                        && part.FsmVariables.FindFsmFloat("InertiaFactor").Value == inertia, "Native flywheel initialization lost save identity or inertia.");
                });
                product.name = prefix;
                foreach (var state in part.Fsm.States) state.SaveActions();
                var reference = factory.FsmVariables.FindFsmGameObject("VINP"); reference.Value = f.Mount.gameObject;
                var received = new ReplacementPartState { FactoryId = (uint)Get(Get(rule!, "Identity"), "FactoryId"), NativeId = prefix + "31",
                    Revision = 1, Scalars = new[] { 89f, 0f, .083f }, Position = new NetVector3(2, 3, 4), Rotation = NetQuaternion.Identity,
                    LocalRotation = NetQuaternion.Identity, LocalScale = new NetVector3(1, 1, 1) };
                PartIdentity.TryItemId(received.NativeId, out uint replicaId); object? replicaBinding = null;
                check("flywheel factory " + prefix + ": actual guest materialization preserves host identity and inertia", () =>
                {
                    Call(f.Sync, "OnReplacementPartState", (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(received)));
                    Require((bool)Call(f.Sync, "MaterializeReplacement", bound, replicaId, received)!, "Guest flywheel did not materialize.");
                    replicaBinding = ((IDictionary)Get(f.Sync, "_replacementParts"))[replicaId];
                    var data = (PlayMakerFSM)Get(replicaBinding!, "Data"); spawned.Add(data.gameObject);
                    Require(data.FsmVariables.FindFsmString("ID").Value == received.NativeId
                        && Near(data.FsmVariables.FindFsmFloat("InertiaFactor").Value, .083f)
                        && data.GetComponent<Rigidbody>() != null && (bool)Get(replicaBinding!, "HasAppliedState")
                        && !(bool)Get(replicaBinding!, "FitFailed") && !(bool)Get(replicaBinding!, "RemovalFailed"), "Actual guest flywheel lost identity, inertia or controls."); f.AssertSaved();
                });
                check("flywheel factory " + prefix + ": actual replica fits and returns loose with the same body", () =>
                {
                    Require(replicaBinding != null, "Guest materialization prerequisite failed."); var data = (PlayMakerFSM)Get(replicaBinding!, "Data"); var body = data.GetComponent<Rigidbody>();
                    received.Revision++; received.Installed = true; received.AssemblyId = 1;
                    received.ParentKind = PartParentKind.NativePart; received.ParentId = f.AnchorId;
                    received.ParentPath = "CrankshaftParent/VINP_CrankPulley/VINP_FlywheelFlexplate";
                    Call(f.Sync, "OnReplacementPartState", received); Require((bool)Call(f.Sync, "ApplyReplacementState", replicaBinding, replicaId, received)!, "Guest flywheel fitting failed.");
                    Require(data.transform.parent == f.Mount.transform && body.isKinematic && !body.detectCollisions
                        && Near(data.FsmVariables.FindFsmFloat("InertiaFactor").Value, .083f), "Guest flywheel fitting changed inertia or physics.");
                    received.Revision++; received.Installed = false; received.AssemblyId = 0; received.ParentKind = PartParentKind.None; received.ParentId = 0; received.ParentPath = string.Empty;
                    Call(f.Sync, "OnReplacementPartState", received); Require((bool)Call(f.Sync, "ApplyReplacementState", replicaBinding, replicaId, received)!, "Guest flywheel removal failed.");
                    Require(data.transform.parent == null && data.GetComponent<Rigidbody>() == body && !body.isKinematic && body.detectCollisions
                        && Near(data.FsmVariables.FindFsmFloat("InertiaFactor").Value, .083f), "Guest flywheel did not return loose with its original body and host inertia."); f.AssertSaved();
                });
                if (replicaBinding != null)
                {
                    var data = (PlayMakerFSM)Get(replicaBinding, "Data"); Call(f.Bridge, "ForgetReplacementBolts", data);
                    Call(f.Sync, "RemoveNativeItemMotion", replicaId); ((IDictionary)Get(f.Sync, "_nativeParts")).Remove(replicaId);
                    ((IDictionary)Get(f.Sync, "_replacementParts")).Remove(replicaId); ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Remove(replicaId);
                    Call(f.Bridge.GetType().GetProperty("PartIdentities", Members).GetValue(f.Bridge, null), "Forget", data);
                }
            }
            finally { foreach (var output in spawned) if (output != null) UnityEngine.Object.DestroyImmediate(output); UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
