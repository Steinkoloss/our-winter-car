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
            internal Component ConfigureLimiterDecisions()
            {
                var obj = Child(Extras, "limiter drivetrain"); obj.SetActive(false);
                var type = Type.GetType("Drivetrain, Assembly-CSharp", true);
                var drive = obj.AddComponent(type); ((Behaviour)drive).enabled = false;
                ((Behaviour)obj.GetComponent(Type.GetType("Axles, Assembly-CSharp", true))).enabled = false;
                Reader.FsmVariables.ObjectVariables = new[] { new FsmObject { Name = "CarDrivetrain", UseVariable = true, ObjectType = type, Value = drive } };
                obj.SetActive(true);
                var state = NativeBagPartChecks.State(Reader, "Limiter");
                foreach (int index in new[] { 1, 2, 4 }) { state.Actions[index] = Import("Limiter", index); state.Actions[index].Init(state); }
                RestoreNativeTransitions("Limiter");
                return drive;
            }
            internal Variant AddLimiterConflict() => AddVariant("REVLIMITER0", Mount, "REVLIMITER099");

        }

        internal static void RunRevlimiter(Action<string, Action> check)
        {
            using (var f = new Fixture("REVLIMITER0", "Cylinders", "revlimiter-engine-input-probe.json"))
            {
                var drive = f.ConfigureLimiterDecisions();
                Action absent = () => { Require(!f.Installed && f.ProxyData.FsmVariables.FindFsmFloat("SettingRPM").Value == 0, "Unavailable limiter supplied saved RPM."); f.AssertSaved(); };
                Action<float> running = rpm => {
                    Require(f.Reader.ActiveStateName == "Wait" && (bool)Get(drive, "revLimiter")
                        && Near((float)Get(drive, "maxRPM"), rpm), "Native limiter ignored host installation/RPM."); f.AssertSaved(); };
                check("revlimiter: saved readers warm native caches before projection", () =>
                {
                    f.ReadAll(); Require(f.Installed && f.Reader.FsmVariables.FindFsmFloat("RevlimitRPM").Value == 6200, "Saved limiter fixture missing.");
                    foreach (var read in f.Readers) Require(ReferenceEquals(Get(read, "goLastFrame"), f.Mount.gameObject), "Limiter cache did not warm."); f.AssertSaved();
                });
                check("revlimiter: absent host state disables native limiter without replacing maximum RPM", () =>
                {
                    Require(f.Prepare(), "Limiter projection failed."); f.ReadAll(); absent(); Set(drive, "maxRPM", 7777f); f.Fire("Limiter");
                    Require(!(bool)Get(drive, "revLimiter") && (float)Get(drive, "maxRPM") == 7777f && f.Reader.ActiveStateName == "Wait", "Absent limiter bypassed native early exit.");
                });
                check("revlimiter: pending state waits for the applied vehicle attachment", () =>
                {
                    f.Receive(0, 8, 7300, false); f.Prepare(); f.ReadAll(); absent();
                    f.MarkApplied(); Require(f.Prepare(), "Applied limiter failed."); f.Fire("Limiter"); running(7300);
                    var state = ((ReplacementPartReplica)Get(f.Sync, "_replacementReplica")).Get(f.PartId)!;
                    Require(state.ParentKind == PartParentKind.Vehicle && state.ParentId == f.AnchorId
                        && state.ParentPath == "AssembliesTuning/VINP_Revlimiter", "Limiter used an engine-block attachment.");
                });
                check("revlimiter: host adjustment waits for the next native read", () =>
                {
                    var proxy = f.Proxy; f.Receive(0, 8, 7654.5f, true); f.Prepare();
                    Require(f.Proxy == proxy && f.Reader.ActiveStateName == "Wait" && (float)Get(drive, "maxRPM") == 7300, "Projection replayed native limiter state.");
                    f.Fire("Limiter"); running(7654.5f);
                });
                foreach (float value in new[] { 0f, 5000f, 7000f, 9500f, 9999.755f })
                {
                    float rpm = value;
                    check("revlimiter: native engine preserves host RPM " + rpm, () =>
                    { f.Receive(0, 8, rpm, true); f.Prepare(); f.Fire("Limiter"); running(rpm); });
                }
                check("revlimiter: moving the vehicle preserves its live attachment", () =>
                {
                    f.Car.transform.position = new Vector3(20, 2, -30); f.Car.transform.rotation = Quaternion.Euler(0, 70, 0);
                    Require(f.Prepare(), "Moved car lost limiter."); f.Fire("Limiter"); running(9999.755f);
                });
                foreach (string change in new[] { "pending", "revision", "identity", "parent", "hidden", "inactive" })
                {
                    string fault = change;
                    check("revlimiter: " + fault + " cannot supply unapplied host values", () =>
                    {
                        f.Receive(0, 8, 7000, true);
                        if (fault == "pending") ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Add(f.PartId);
                        if (fault == "revision") Set(f.Binding, "AppliedRevision", 0u);
                        if (fault == "identity") f.Part.FsmVariables.FindFsmString("ID").Value = "REVLIMITER098";
                        if (fault == "parent") f.Part.transform.SetParent(f.Extras.transform, false);
                        if (fault == "hidden") Set(f.Binding, "FittedPresentation", false);
                        if (fault == "inactive") f.Part.gameObject.SetActive(false);
                        try { Require(f.Prepare(), "Unavailable limiter preparation failed."); f.ReadAll(); absent(); f.Fire("Limiter"); Require(!(bool)Get(drive, "revLimiter"), "Unavailable limiter remained enabled."); }
                        finally { f.Part.gameObject.SetActive(true); f.Part.transform.SetParent(f.Mount.transform, false); f.Part.FsmVariables.FindFsmString("ID").Value = "REVLIMITER07"; f.MarkApplied(); f.Prepare(); }
                    });
                }
                foreach (string change in new[] { "missing registration", "not vehicle", "wrong kind", "wrong id", "wrong path" })
                {
                    string fault = change;
                    check("revlimiter: " + fault + " cannot borrow the local vehicle mount", () =>
                    {
                        f.Receive(0, 8, 7000, true); var items = (IDictionary)Get(f.Sync, "_items"); var item = items[f.AnchorId];
                        if (fault == "missing registration") items.Remove(f.AnchorId);
                        if (fault == "not vehicle") Set(item, "IsVehicle", false);
                        if (fault == "wrong kind") f.PublishWrongAttachment(PartParentKind.NativePart, f.AnchorId, "AssembliesTuning/VINP_Revlimiter");
                        if (fault == "wrong id") f.PublishWrongAttachment(PartParentKind.Vehicle, f.AnchorId + 1, "AssembliesTuning/VINP_Revlimiter");
                        if (fault == "wrong path") f.PublishWrongAttachment(PartParentKind.Vehicle, f.AnchorId, "Assemblies/VINP_Revlimiter");
                        try { Require(f.Prepare(), "Unavailable vehicle mount failed preparation."); f.ReadAll(); absent(); }
                        finally { items[f.AnchorId] = item; Set(item, "IsVehicle", true); f.Receive(0, 8, 7000, true); f.Prepare(); }
                    });
                }
                var conflict = f.AddLimiterConflict();
                check("revlimiter: conflicting host attachment blocks both candidates until removal", () =>
                {
                    f.ReceiveVariant(conflict, 0, 0, 9000, false); f.Prepare(); f.ReadAll(); absent();
                    f.ReceiveVariant(conflict, 0, 0, 9000, false, false); f.Prepare(); f.Fire("Limiter"); running(7000);
                });
                foreach (string field in new[] { "variableName", "fsmName", "everyFrame", "storeValue" })
                {
                    string name = field;
                    check("revlimiter: changed RPM " + name + " pauses and repairs combustion", () =>
                    {
                        var read = f.Readers[1]; var original = Get(read, name);
                        Set(read, name, name == "everyFrame" ? (object)true : name == "storeValue" ? new FsmFloat { Name = "RevlimitRPM", UseVariable = true } : new FsmString { Value = "Other" });
                        try { Require(!f.Prepare() && !f.Reader.enabled, "Changed limiter read escaped validation."); f.AssertSaved(); }
                        finally { Set(read, name, original); f.Prepare(); }
                        Require(f.Reader.enabled, "Repaired limiter read remained paused.");
                    });
                }
                check("revlimiter: changed factory mount pauses and repairs combustion", () =>
                {
                    var reference = ((PlayMakerFSM)Get(f.Factory, "Fsm")).FsmVariables.FindFsmGameObject("VINP"); reference.Value = f.Original.gameObject;
                    try { Require(!f.Prepare() && !f.Reader.enabled, "Wrong limiter factory mount escaped validation."); f.AssertSaved(); }
                    finally { reference.Value = f.Mount.gameObject; f.Prepare(); }
                    Require(f.Reader.enabled, "Repaired limiter mount remained paused.");
                });
                check("revlimiter: destroyed proxy repairs both native caches", () =>
                {
                    f.ReadAll(); var proxy = f.Proxy; UnityEngine.Object.DestroyImmediate(f.ProxyData); Require(f.Prepare(), "Limiter cache repair failed."); f.ReadAll();
                    Require(f.Proxy != proxy && f.Installed, "Limiter proxy repair did not recover.");
                    foreach (var read in f.Readers) Require(ReferenceEquals(Get(read, "goLastFrame"), f.Proxy), "Limiter cache retained destroyed proxy."); f.AssertSaved();
                });
                CheckLimiterNativeKnob(f, check);
                check("revlimiter: removal disables native limiter and stale state cannot re-enable it", () =>
                {
                    var old = ((ReplacementPartReplica)Get(f.Sync, "_replacementReplica")).Get(f.PartId)!;
                    f.Receive(0, 0, 7000, true, false); f.Prepare(); f.Fire("Limiter");
                    Require(!(bool)Get(drive, "revLimiter"), "Removed limiter remained enabled.");
                    Call(f.Sync, "OnReplacementPartState", old); f.Prepare(); f.ReadAll(); absent();
                });
                CheckLimiterMaterialization(f, check);
                check("revlimiter: disconnect clears input and restores saved reader wrappers", () =>
                {
                    f.Receive(0, 8, 7000, true); f.Prepare(); f.Fire("Limiter"); running(7000);
                    Property(f.Session, "State", SessionState.Idle); f.Prepare(); f.ReadAll(); absent();
                    Call(f.Sync, "RestoreGuestEngineInputs");
                    for (int i = 0; i < f.Readers.Length; i++) Require(ReferenceEquals(Get(f.Readers[i], "gameObject"), f.OriginalOwners[i]), "Limiter cleanup lost native wrappers.");
                    f.ReadAll(); Require(f.Installed && f.Reader.FsmVariables.FindFsmFloat("RevlimitRPM").Value == 6200, "Saved limiter cache did not restore."); f.AssertSaved();
                });
            }
        }

        private static void CheckLimiterNativeKnob(Fixture f, Action<string, Action> check)
        {
            var hostMount = Data(Child(f.Extras, "host limiter setting"), null, 0); NativeBagPartChecks.Start(hostMount);
            var row = f.FindNativeRow("REVLIMITER0/Bolts/Button", "Use");
            var knob = NativeBagPartChecks.MakeFsm(Child(f.Extras, "native limiter knob"), row);
            knob.FsmVariables.GameObjectVariables = new[] { ObjectVar("ThisPart", f.Part.gameObject), ObjectVar("VINP", hostMount.gameObject), ObjectVar("Knob", Child(f.Extras, "knob mesh")) };
            NativeBagPartChecks.LoadActions(knob, row, "Calc", "Knob", "Increase", "Decrease");
            NativeBagPartChecks.State(knob, "Calc").Transitions = new FsmTransition[0];
            NativeBagPartChecks.State(knob, "Knob").Transitions = new FsmTransition[0]; NativeBagPartChecks.Start(knob);
            var oldReferences = f.Part.FsmVariables.GameObjectVariables;
            f.Part.FsmVariables.GameObjectVariables = new[] { ObjectVar("InstallPoint", hostMount.gameObject) };
            float saved = f.Part.FsmVariables.FindFsmFloat("SettingRPM").Value;
            uint previousRevision = 0;
            try
            {
                foreach (float position in new[] { 0f, 137.5f, 265f })
                {
                    float angle = position;
                    check("revlimiter: native host knob at " + angle + " survives capture and packet replay", () =>
                    {
                        Property(f.Session, "IsHost", true); Set(f.Binding, "Replica", false);
                        try
                        {
                            knob.FsmVariables.FindFsmFloat("Angle").Value = angle; NativeBagPartChecks.Fire(knob, "Calc");
                            float rpm = f.Part.FsmVariables.FindFsmFloat("SettingRPM").Value;
                            float expected = angle * 18.867000579833984f + 5000f;
                            Require(Math.Abs(rpm - expected) < .002f && hostMount.FsmVariables.FindFsmFloat("SettingRPM").Value == rpm,
                                "Native knob did not update host part and mount: " + rpm.ToString("R") + " expected " + expected.ToString("R")
                                + " mount " + hostMount.FsmVariables.FindFsmFloat("SettingRPM").Value.ToString("R"));
                            var state = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", f.PartId);
                            Require(state != null && state.Scalars.Length == 2 && state.Scalars[1] == rpm, "Capture lost native knob setting.");
                            Require(state!.Revision > previousRevision, "Native host adjustment did not advance gameplay revision."); previousRevision = state.Revision;
                            var decoded = (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state!));
                            f.Part.FsmVariables.FindFsmFloat("SettingRPM").Value = -1;
                            Call(f.Sync, "ApplyReplacementScalars", f.Binding, decoded); Require(f.Part.FsmVariables.FindFsmFloat("SettingRPM").Value == rpm, "Replica replaced host RPM with a preset.");
                            NativeBagPartChecks.Fire(knob, "Knob"); Require(Math.Abs(knob.FsmVariables.FindFsmFloat("Angle").Value - angle) < .001f, "Native knob inverse calculation diverged.");
                            f.AssertSaved();
                        }
                        finally { Property(f.Session, "IsHost", false); Set(f.Binding, "Replica", true); }
                    });
                }
            }
            finally { f.Part.FsmVariables.GameObjectVariables = oldReferences; f.Part.FsmVariables.FindFsmFloat("SettingRPM").Value = saved; }
        }

        private static void CheckLimiterMaterialization(Fixture f, Action<string, Action> check)
        {
            var root = new GameObject("native limiter replica probe"); root.SetActive(false);
            var product = Child(root, "REVLIMITER0"); product.AddComponent<Rigidbody>().useGravity = false; product.AddComponent<BoxCollider>(); Child(product, "Bolts");
            var row = f.FindNativeRow("REVLIMITER0", "Data"); var part = NativeBagPartChecks.MakeFsm(product, row);
            var factoryRow = f.FindNativeRow("CARPARTS/PARTSYSTEM/SPAWNERS_Fleetari/Revlimiter", "Spawn");
            var factory = NativeBagPartChecks.MakeFsm(Child(root, "factory"), factoryRow);
            var c = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true).GetProperty("ReplacementParts", Static).GetValue(null, null);
            var bound = Nested("ReplacementFactory"); Set(bound, "Rule", Get(f.Factory, "Rule")); Set(bound, "Fsm", factory); Set(bound, "TemplateData", part); Set(bound, "Prefab", product);
            object? binding = null; GameObject? replicaObject = null;
            var state = new ReplacementPartState { FactoryId = f.FactoryId, NativeId = "REVLIMITER031", Revision = 1, Scalars = new[] { 0f, 8321.5f },
                Rotation = NetQuaternion.Identity, LocalRotation = NetQuaternion.Identity, LocalScale = new NetVector3(1, 1, 1) };
            PartIdentity.TryItemId(state.NativeId, out uint id);
            try
            {
                root.SetActive(true);
                part.FsmVariables.ObjectVariables = new[] { new FsmObject { Name = "Collider", UseVariable = true, ObjectType = typeof(BoxCollider), Value = product.GetComponent<BoxCollider>() } };
                NativeBagPartChecks.LoadActions(part, row, "Init", "Status", "Another part?", "Tightness?", "Bolted", "Unbolted", "Mouse off", "Mouse over", "Remove");
                NativeBagPartChecks.LoadActions(factory, factoryRow, "Create product", "Create");
                factory.FsmVariables.FindFsmGameObject("Prefab").Value = product; factory.FsmVariables.FindFsmGameObject("VINP").Value = f.Mount.gameObject;
                NativeBagPartChecks.Start(factory); Call(Get(bound, "Suppressor"), "Suppress", factory);
                foreach (var s in part.Fsm.States) s.SaveActions();
                check("revlimiter: native factory template and outputs still validate", () =>
                { CallStatic(Items, "ValidateReplacementTemplate", bound, c); CallStatic(Items, "ValidateReplacementOutput", bound, c, true); CallStatic(Items, "ValidateReplacementOutput", bound, c, false); });
                check("revlimiter: real guest materialization retains host RPM and identity", () =>
                {
                    Call(f.Sync, "OnReplacementPartState", (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state)));
                    Require((bool)Call(f.Sync, "MaterializeReplacement", bound, id, state)!, "Native limiter did not materialize.");
                    binding = ((IDictionary)Get(f.Sync, "_replacementParts"))[id]; var data = (PlayMakerFSM)Get(binding!, "Data"); replicaObject = data.gameObject;
                    Require(data.FsmVariables.FindFsmString("ID").Value == state.NativeId && data.FsmVariables.FindFsmFloat("SettingRPM").Value == 8321.5f
                        && (bool)Get(binding!, "HasAppliedState") && !(bool)Get(binding!, "FitFailed") && !(bool)Get(binding!, "RemovalFailed"), "Native guest limiter lost state or controls."); f.AssertSaved();
                });
                check("revlimiter: real replica attaches to the vehicle and supplies its engine setting", () =>
                {
                    Require(binding != null, "Limiter replica prerequisite failed.");
                    state.Revision++; state.Installed = true; state.AssemblyId = 1; state.ParentKind = PartParentKind.Vehicle;
                    state.ParentId = f.AnchorId; state.ParentPath = "AssembliesTuning/VINP_Revlimiter";
                    Call(f.Sync, "OnReplacementPartState", state); Require((bool)Call(f.Sync, "ApplyReplacementState", binding, id, state)!, "Native guest limiter fitting failed.");
                    ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Remove(id);
                    var body = replicaObject!.GetComponent<Rigidbody>();
                    Require(replicaObject.transform.parent == f.Mount.transform && body.isKinematic && !body.detectCollisions, "Limiter did not fit to registered vehicle.");
                    Require(f.Prepare(), "Actual limiter input preparation failed."); f.ReadAll();
                    Require(f.Installed && f.Reader.FsmVariables.FindFsmFloat("RevlimitRPM").Value == 8321.5f, "Actual limiter replica did not supply the engine."); f.AssertSaved();
                });
                check("revlimiter: real replica removal restores loose body and clears engine input", () =>
                {
                    Require(binding != null, "Limiter replica prerequisite failed."); var body = replicaObject!.GetComponent<Rigidbody>();
                    state.Revision++; state.Installed = false; state.AssemblyId = 0; state.ParentKind = PartParentKind.None; state.ParentId = 0; state.ParentPath = "";
                    Call(f.Sync, "OnReplacementPartState", state); Require((bool)Call(f.Sync, "ApplyReplacementState", binding, id, state)!, "Native guest limiter removal failed.");
                    Require(replicaObject.transform.parent == null && replicaObject.GetComponent<Rigidbody>() == body && !body.isKinematic && body.detectCollisions, "Limiter removal lost body or loose physics.");
                    f.Prepare(); f.ReadAll(); Require(!f.Installed, "Removed actual limiter still supplied engine input."); f.AssertSaved();
                });
            }
            finally
            {
                if (binding != null)
                {
                    var data = (PlayMakerFSM)Get(binding, "Data"); Call(f.Bridge, "ForgetReplacementBolts", data); Call(f.Sync, "RemoveNativeItemMotion", id);
                    ((IDictionary)Get(f.Sync, "_nativeParts")).Remove(id); ((IDictionary)Get(f.Sync, "_replacementParts")).Remove(id);
                    ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Remove(id);
                    Call(f.Bridge.GetType().GetProperty("PartIdentities", Members).GetValue(f.Bridge, null), "Forget", data);
                }
                if (replicaObject != null) UnityEngine.Object.DestroyImmediate(replicaObject); UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
