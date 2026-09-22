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
            internal Variant AddCoilConflict() => AddVariant("VIN212", Mount, "VIN21299");
        }

        internal static void RunIgnitionCoil(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN212", "Cylinders", "ignition-coil-engine-input-probe.json"))
            {
                var distributor = f.Auxiliary("VIN131").Variants[0]; f.ReceiveVariant(distributor, 90, 0, 8, true);
                Action absent = () => { Require(!f.Installed, "Unavailable coil borrowed saved installation."); f.AssertSaved(); };
                Action<bool> ignition = expected => {
                    f.Starter.FsmVariables.FindFsmBool("ShutOff").Value = false; f.Fire("Ignition");
                    Require(f.Reader.ActiveStateName == (expected ? "Plug data" : "Not Ok")
                        && f.Starter.FsmVariables.FindFsmBool("ShutOff").Value == !expected, "Native coil/distributor/wiring ignition gate diverged."); f.AssertSaved(); };
                check("ignition coil: warmed native reader initially uses the saved coil", () =>
                {
                    f.ReadAll(); Require(f.Installed && ReferenceEquals(Get(f.Readers[0], "goLastFrame"), f.Mount.gameObject), "Saved coil reader did not warm."); f.AssertSaved();
                });
                check("ignition coil: no host coil prevents starting despite a fitted saved coil", () =>
                { Require(f.Prepare(), "Coil binding failed."); f.ReadAll(); absent(); ignition(false); });
                check("ignition coil: pending state waits for the applied vehicle attachment", () =>
                {
                    f.Receive(85, 8, 0, false); f.Prepare(); f.ReadAll(); absent(); ignition(false);
                    f.MarkApplied(); Require(f.Prepare(), "Applied coil failed preparation."); ignition(true);
                    var state = ((ReplacementPartReplica)Get(f.Sync, "_replacementReplica")).Get(f.PartId)!;
                    Require(state.ParentKind == PartParentKind.Vehicle && state.ParentId == f.AnchorId && state.ParentPath == "Assemblies/VINP_IgnitionCoil", "Coil attachment is not vehicle-relative.");
                });
                check("ignition coil: projection changes only the coil reader owner", () =>
                {
                    Require(f.Proxy != f.Mount.gameObject && f.Shared.Value == f.Mount.gameObject && !f.ProxyData.enabled
                        && f.ProxyData.FsmVariables.FloatVariables.Length == 0 && f.Proxy.GetComponent<Rigidbody>() == null,
                        "Coil projection acquired saved values or physical simulation.");
                    Require(f.Target(f.Action("Ignition", 1)) != f.Proxy && f.Target(f.Action("Ignition", 2)) != f.Proxy, "Coil replaced distributor or wiring input."); f.AssertSaved();
                });
                for (int bits = 0; bits < 8; bits++)
                {
                    int mask = bits;
                    check("ignition coil: native prerequisite combination " + mask, () =>
                    {
                        bool coilFitted = (mask & 1) != 0, distributorFitted = (mask & 2) != 0, wired = (mask & 4) != 0;
                        f.Receive(85, 0, 0, true, coilFitted); f.ReceiveVariant(distributor, 90, 0, 8, true, distributorFitted);
                        f.SetWire(1, wired, false);
                        try { Require(f.Prepare(), "Coil combination failed preparation."); ignition(mask == 7); }
                        finally { f.SetWire(1, true, false); }
                    });
                }
                check("ignition coil: native installation is independent of condition and bolt threshold", () =>
                {
                    f.Receive(0, 0, 0, true); f.Prepare(); ignition(true);
                    f.Receive(99, 16, 0, true); f.Prepare(); ignition(true);
                });
                check("ignition coil: host removal waits for the next native read", () =>
                {
                    var proxy = f.Proxy; f.Receive(99, 0, 0, true, false); f.Prepare();
                    Require(f.Proxy == proxy && f.Reader.ActiveStateName == "Plug data" && f.Reader.FsmVariables.FindFsmBool("Installed1").Value,
                        "Projection replayed ignition or replaced shared scratch."); ignition(false); f.ReadAll(); absent();
                });
                check("ignition coil: moving the car preserves the live coil mount", () =>
                {
                    f.Receive(85, 8, 0, true); f.Car.transform.position = new Vector3(-30, 2, 14); f.Car.transform.rotation = Quaternion.Euler(0, 80, 0);
                    Require(f.Prepare(), "Moved car lost coil."); ignition(true);
                });
                foreach (string change in new[] { "pending", "revision", "identity", "parent", "hidden", "inactive" })
                {
                    string fault = change;
                    check("ignition coil: " + fault + " cannot supply unapplied installation", () =>
                    {
                        f.Receive(85, 8, 0, true);
                        if (fault == "pending") ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Add(f.PartId);
                        if (fault == "revision") Set(f.Binding, "AppliedRevision", 0u);
                        if (fault == "identity") f.Part.FsmVariables.FindFsmString("ID").Value = "VIN21298";
                        if (fault == "parent") f.Part.transform.SetParent(f.Extras.transform, false);
                        if (fault == "hidden") Set(f.Binding, "FittedPresentation", false);
                        if (fault == "inactive") f.Part.gameObject.SetActive(false);
                        try { Require(f.Prepare(), "Unavailable coil failed preparation."); f.ReadAll(); absent(); ignition(false); }
                        finally { f.Part.gameObject.SetActive(true); f.Part.transform.SetParent(f.Mount.transform, false); f.Part.FsmVariables.FindFsmString("ID").Value = "VIN2127"; f.MarkApplied(); f.Prepare(); }
                    });
                }
                foreach (string change in new[] { "missing vehicle", "wrong kind", "wrong id", "wrong path" })
                {
                    string fault = change;
                    check("ignition coil: " + fault + " cannot use the saved car mount", () =>
                    {
                        f.Receive(85, 8, 0, true); var items = (IDictionary)Get(f.Sync, "_items"); var vehicle = items[f.AnchorId];
                        if (fault == "missing vehicle") items.Remove(f.AnchorId);
                        if (fault == "wrong kind") f.PublishWrongAttachment(PartParentKind.NativePart, f.AnchorId, "Assemblies/VINP_IgnitionCoil");
                        if (fault == "wrong id") f.PublishWrongAttachment(PartParentKind.Vehicle, f.AnchorId + 1, "Assemblies/VINP_IgnitionCoil");
                        if (fault == "wrong path") f.PublishWrongAttachment(PartParentKind.Vehicle, f.AnchorId, "AssembliesTuning/VINP_IgnitionCoil");
                        try { Require(f.Prepare(), "Unavailable coil mount failed preparation."); f.ReadAll(); absent(); }
                        finally { items[f.AnchorId] = vehicle; f.Receive(85, 8, 0, true); f.Prepare(); }
                    });
                }
                var conflict = f.AddCoilConflict();
                check("ignition coil: conflicting host copy blocks starting until removed", () =>
                {
                    f.ReceiveVariant(conflict, 90, 0, 0, false); f.Prepare(); ignition(false);
                    f.ReceiveVariant(conflict, 90, 0, 0, false, false); f.Prepare(); ignition(true);
                });
                foreach (string field in new[] { "variableName", "fsmName", "everyFrame", "storeValue" })
                {
                    string name = field;
                    check("ignition coil: changed " + name + " pauses and repairs combustion", () =>
                    {
                        var read = f.Readers[0]; var original = Get(read, name);
                        Set(read, name, name == "everyFrame" ? (object)true : name == "storeValue" ? new FsmBool { Name = "Installed1", UseVariable = true } : new FsmString { Value = "Other" });
                        try { Require(!f.Prepare() && !f.Reader.enabled, "Changed coil reader escaped validation."); f.AssertSaved(); }
                        finally { Set(read, name, original); f.Prepare(); }
                        Require(f.Reader.enabled, "Repaired coil reader stayed paused."); ignition(true);
                    });
                }
                check("ignition coil: changed factory mount pauses and repairs combustion", () =>
                {
                    var reference = ((PlayMakerFSM)Get(f.Factory, "Fsm")).FsmVariables.FindFsmGameObject("VINP"); reference.Value = f.Original.gameObject;
                    try { Require(!f.Prepare() && !f.Reader.enabled, "Coil factory mismatch escaped validation."); f.AssertSaved(); }
                    finally { reference.Value = f.Mount.gameObject; f.Prepare(); }
                    Require(f.Reader.enabled, "Repaired coil mount stayed paused."); ignition(true);
                });
                check("ignition coil: destroyed proxy repairs the warmed native cache", () =>
                {
                    f.ReadAll(); var proxy = f.Proxy; UnityEngine.Object.DestroyImmediate(f.ProxyData); Require(f.Prepare(), "Coil proxy repair failed."); f.ReadAll();
                    Require(f.Proxy != proxy && f.Installed && ReferenceEquals(Get(f.Readers[0], "goLastFrame"), f.Proxy), "Coil reader retained its destroyed cache."); f.AssertSaved();
                });
                check("ignition coil: stale fitting cannot undo host removal", () =>
                {
                    var old = ((ReplacementPartReplica)Get(f.Sync, "_replacementReplica")).Get(f.PartId)!;
                    f.Receive(85, 0, 0, true, false); Call(f.Sync, "OnReplacementPartState", old); f.Prepare(); ignition(false); f.ReadAll(); absent();
                });
                CheckCoilNativeFactory(f, ignition, check);
                check("ignition coil: disconnect clears host input and restores saved wrappers", () =>
                {
                    f.Receive(85, 8, 0, true); f.Prepare(); ignition(true);
                    Property(f.Session, "State", SessionState.Idle); f.Prepare(); f.ReadAll(); absent();
                    Call(f.Sync, "RestoreGuestEngineInputs"); Require(ReferenceEquals(Get(f.Readers[0], "gameObject"), f.OriginalOwners[0]), "Coil cleanup lost the saved owner wrapper.");
                    f.ReadAll(); Require(f.Installed && ReferenceEquals(Get(f.Readers[0], "goLastFrame"), f.Mount.gameObject), "Saved coil reader did not restore."); f.AssertSaved();
                });
            }
        }

        private static void CheckCoilNativeFactory(Fixture f, Action<bool> ignition, Action<string, Action> check)
        {
            var root = new GameObject("native ignition coil factory probe"); root.SetActive(false);
            var product = Child(root, "VIN212"); product.AddComponent<Rigidbody>().useGravity = false; product.AddComponent<BoxCollider>(); Child(product, "Bolts");
            var row = f.FindNativeRow("VIN212", "Data"); var part = NativeBagPartChecks.MakeFsm(product, row);
            var factoryRow = f.FindNativeRow("CARPARTS/PARTSYSTEM/SPAWNERS_VIN/IgnitionCoil212", "Spawn");
            var factory = NativeBagPartChecks.MakeFsm(Child(root, "factory"), factoryRow);
            var mountRow = f.FindNativeRow("CORRIS/Assemblies/VINP_IgnitionCoil", "Data");
            var mount = NativeBagPartChecks.MakeFsm(Child(root, "native coil mount"), mountRow);
            var c = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true).GetProperty("ReplacementParts", Static).GetValue(null, null);
            var bound = Nested("ReplacementFactory"); Set(bound, "Rule", Get(f.Factory, "Rule")); Set(bound, "Fsm", factory); Set(bound, "TemplateData", part); Set(bound, "Prefab", product);
            var outputs = new List<GameObject>(); object? binding = null; GameObject? replicaObject = null;
            var state = new ReplacementPartState { FactoryId = f.FactoryId, NativeId = "VIN21231", Revision = 1, Scalars = new[] { 57.125f, 0f },
                Rotation = NetQuaternion.Identity, LocalRotation = NetQuaternion.Identity, LocalScale = new NetVector3(1, 1, 1) };
            PartIdentity.TryItemId(state.NativeId, out uint id);
            try
            {
                root.SetActive(true);
                part.FsmVariables.ObjectVariables = new[] { new FsmObject { Name = "Collider", UseVariable = true, ObjectType = typeof(BoxCollider), Value = product.GetComponent<BoxCollider>() } };
                NativeBagPartChecks.LoadActions(part, row, "Init", "Status", "Another part?", "Tightness?", "Bolted", "Unbolted", "Mouse off", "Mouse over", "Remove");
                NativeBagPartChecks.LoadActions(factory, factoryRow, "Create product", "Create");
                NativeBagPartChecks.LoadActions(mount, mountRow, "Idle", "Allow install?", "Far", "Near", "Install 1");
                factory.FsmVariables.FindFsmGameObject("Prefab").Value = product; factory.FsmVariables.FindFsmGameObject("VINP").Value = mount.gameObject;
                factory.FsmVariables.FindFsmString("SaveID").Value = "VIN212"; factory.FsmVariables.FindFsmFloat("MinimumWear").Value = 95;
                foreach (var s in part.Fsm.States) s.SaveActions();
                check("ignition coil factory: actual native template and fresh/saved outputs validate", () =>
                { CallStatic(Items, "ValidateReplacementTemplate", bound, c); CallStatic(Items, "ValidateReplacementOutput", bound, c, true); CallStatic(Items, "ValidateReplacementOutput", bound, c, false); });
                check("ignition coil factory: actual native mount and fitting entry validate", () =>
                {
                    CallStatic(Items, "ValidatePartFitMount", mount, c, false, null);
                    var entry = Nested("ReplacementBinding"); Set(entry, "Factory", bound); Set(entry, "Data", part); CallStatic(Items, "ValidatePartFitEntry", entry);
                    Require((bool)Get(entry, "FitValidated") && !(bool)Get(entry, "FitFailed"), "Native coil fitting entry rejected.");
                });
                check("ignition coil factory: native creation gives distinct IDs and host-selected wear bounds", () =>
                {
                    NativeBagPartChecks.Start(factory);
                    for (int i = 1; i <= 2; i++)
                    {
                        NativeBagPartChecks.Fire(factory, "Create product"); var output = factory.FsmVariables.FindFsmGameObject("New").Value; outputs.Add(output);
                        var data = output.GetComponent<PlayMakerFSM>(); Require(output.name == "VIN212" + i, "Fresh coil lost factory identity.");
                        NativeBagPartChecks.Start(data); NativeBagPartChecks.Fire(data, "Init");
                        Require(data.FsmVariables.FindFsmString("ID").Value == "VIN212" + i && data.FsmVariables.FindFsmString("UTWear").Value == "VIN212" + i + "WEA"
                            && data.FsmVariables.FindFsmGameObject("InstallPoint").Value == mount.gameObject
                            && data.FsmVariables.FindFsmFloat("Wear").Value >= 95 && data.FsmVariables.FindFsmFloat("Wear").Value <= 99,
                            "Native coil initialization lost identity, mount or host wear bounds.");
                    }
                    Require(outputs[0] != outputs[1], "Fresh coils share one object.");
                });
                factory.FsmVariables.FindFsmGameObject("VINP").Value = f.Mount.gameObject; Call(Get(bound, "Suppressor"), "Suppress", factory);
                check("ignition coil factory: real guest copy preserves host identity and wear over native random initialization", () =>
                {
                    Call(f.Sync, "OnReplacementPartState", (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state)));
                    Require((bool)Call(f.Sync, "MaterializeReplacement", bound, id, state)!, "Native coil replica did not materialize.");
                    binding = ((IDictionary)Get(f.Sync, "_replacementParts"))[id]; var data = (PlayMakerFSM)Get(binding!, "Data"); replicaObject = data.gameObject;
                    Require(data.FsmVariables.FindFsmString("ID").Value == state.NativeId && data.FsmVariables.FindFsmFloat("Wear").Value == 57.125f
                        && (bool)Get(binding!, "HasAppliedState") && !(bool)Get(binding!, "FitFailed") && !(bool)Get(binding!, "RemovalFailed"), "Native coil replica lost identity, host wear or controls."); f.AssertSaved();
                });
                check("ignition coil factory: real replica fits the registered car and satisfies native ignition", () =>
                {
                    Require(binding != null, "Coil materialization prerequisite failed."); state.Revision++; state.Installed = true; state.AssemblyId = 1;
                    state.ParentKind = PartParentKind.Vehicle; state.ParentId = f.AnchorId; state.ParentPath = "Assemblies/VINP_IgnitionCoil";
                    Call(f.Sync, "OnReplacementPartState", state); Require((bool)Call(f.Sync, "ApplyReplacementState", binding, id, state)!, "Native coil replica fitting failed.");
                    ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Remove(id); var body = replicaObject!.GetComponent<Rigidbody>();
                    Require(replicaObject.transform.parent == f.Mount.transform && body.isKinematic && !body.detectCollisions, "Coil did not fit to the registered car.");
                    Require(f.Prepare(), "Actual coil input failed preparation."); ignition(true);
                });
                check("ignition coil factory: actual host condition capture and revision replay preserve wear", () =>
                {
                    Require(binding != null, "Coil materialization prerequisite failed."); var data = (PlayMakerFSM)Get(binding!, "Data"); var wear = data.FsmVariables.FindFsmFloat("Wear");
                    Property(f.Session, "IsHost", true); Set(binding!, "Replica", false);
                    try
                    {
                        wear.Value = 43.25f; var captured = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", id);
                        Require(captured != null && captured.Scalars[0] == 43.25f, "Host coil capture substituted prefab wear.");
                        var decoded = (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(captured!)); wear.Value = 39.5f;
                        var changed = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", id);
                        Require(changed != null && changed.Revision != captured!.Revision, "Host coil condition did not advance revision.");
                        Call(f.Sync, "ApplyReplacementScalars", binding, decoded); Require(wear.Value == 43.25f, "Replica lost captured host coil wear."); f.AssertSaved();
                    }
                    finally { wear.Value = 57.125f; Property(f.Session, "IsHost", false); Set(binding!, "Replica", true); }
                });
                check("ignition coil factory: real removal restores the same loose body and blocks starting", () =>
                {
                    Require(binding != null, "Coil materialization prerequisite failed."); var body = replicaObject!.GetComponent<Rigidbody>();
                    state.Revision++; state.Installed = false; state.AssemblyId = 0; state.ParentKind = PartParentKind.None; state.ParentId = 0; state.ParentPath = "";
                    Call(f.Sync, "OnReplacementPartState", state); Require((bool)Call(f.Sync, "ApplyReplacementState", binding, id, state)!, "Native coil replica removal failed.");
                    Require(replicaObject.transform.parent == null && replicaObject.GetComponent<Rigidbody>() == body && !body.isKinematic && body.detectCollisions
                        && ((PlayMakerFSM)Get(binding!, "Data")).FsmVariables.FindFsmFloat("Wear").Value == 57.125f, "Coil removal lost its body or host condition.");
                    f.Prepare(); ignition(false);
                });
            }
            finally
            {
                if (binding != null)
                {
                    var data = (PlayMakerFSM)Get(binding, "Data"); Call(f.Bridge, "ForgetReplacementBolts", data); Call(f.Sync, "RemoveNativeItemMotion", id);
                    ((IDictionary)Get(f.Sync, "_nativeParts")).Remove(id); ((IDictionary)Get(f.Sync, "_replacementParts")).Remove(id); ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Remove(id);
                    Call(f.Bridge.GetType().GetProperty("PartIdentities", Members).GetValue(f.Bridge, null), "Forget", data);
                }
                if (replicaObject != null) UnityEngine.Object.DestroyImmediate(replicaObject);
                foreach (var output in outputs) if (output != null) UnityEngine.Object.DestroyImmediate(output); UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
