using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static class PartTimingChecks
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        private static readonly Type Items = Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true);

        internal static void Run(Action<string, Action> check)
        {
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
            catalog.GetMethod("EnsureLoaded", Static).Invoke(null, null);
            var c = catalog.GetProperty("ReplacementParts", Static).GetValue(null, null);
            object? factoryRule = null;
            foreach (object candidate in (IEnumerable)Get(c, "Factories"))
                if ((string)Get(candidate, "Prefix") == "VIN131") factoryRule = candidate;
            if (factoryRule == null) throw new InvalidOperationException("Distributor factory missing.");
            var rule = Get(factoryRule, "DistributorTiming") ?? throw new InvalidOperationException("Timing catalog missing.");
            var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var reader = Activator.CreateInstance(readerType, Members, null, new object[] {
                File.ReadAllText(Path.Combine(Application.dataPath, "../distributor-timing-probe.json")) }, null);
            var input = (Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null);
            var rows = (List<object>)input["fsms"];
            var dataRow = NativeBagPartChecks.Find(rows, "VIN131", "Data");
            var handRow = NativeBagPartChecks.Find(rows, "VIN131", "HandRotate");
            var mountRow = NativeBagPartChecks.Find(rows, "CARPARTS/StartParts/VIN1010/VINP_Distributor", "Data");
            var root = new GameObject("distributor timing probe"); root.SetActive(false);
            var mountObject = Child(root, "VINP_Distributor");
            var partObject = Child(mountObject, "VIN1317"); partObject.layer = 19;
            var pivot = Child(partObject, "Pivot").transform; pivot.localRotation = Quaternion.Euler(0, 0, -10);
            var mesh = Child(pivot.gameObject, "mesh").transform; mesh.localRotation = Quaternion.Euler(0, 0, 10);
            var pick = partObject.AddComponent<SphereCollider>(); pick.isTrigger = true; pick.radius = .04f;
            pick.center = new Vector3(0, 0, .02f); pick.enabled = false;
            var box = partObject.AddComponent<BoxCollider>(); box.size = new Vector3(.077615f, .06979f, .193179f);
            box.center = new Vector3(-.0001465f, 0, -.0323455f); box.isTrigger = true;
            var data = NativeBagPartChecks.MakeFsm(partObject, dataRow);
            var hand = NativeBagPartChecks.MakeFsm(partObject, handRow);
            var mount = NativeBagPartChecks.MakeFsm(mountObject, mountRow);
            data.FsmVariables.ObjectVariables = new[] { new FsmObject { Name = "Collider", UseVariable = true, Value = box, ObjectType = typeof(BoxCollider) } };
            data.FsmVariables.FindFsmGameObject("Owner").Value = partObject;
            data.FsmVariables.FindFsmGameObject("InstallPoint").Value = mountObject;
            data.FsmVariables.FindFsmGameObject("Bolts").Value = Child(partObject, "Bolts");
            data.FsmVariables.FindFsmInt("AssemblyID").Value = 1;
            data.FsmVariables.FindFsmString("ID").Value = "VIN1317";
            data.FsmVariables.FindFsmString("UTAssemblyID").Value = "VIN1317AID";
            data.FsmVariables.FindFsmString("UTPos").Value = "VIN1317POS";
            hand.FsmVariables.FindFsmGameObject("MeshRotate").Value = mesh.gameObject;
            hand.FsmVariables.FindFsmGameObject("VINP").Value = mountObject;
            mount.FsmVariables.FindFsmGameObject("ActivePart").Value = partObject;
            mount.FsmVariables.FindFsmGameObject("AssemblyPoint").Value = mountObject;
            mount.FsmVariables.FindFsmBool("Installed").Value = true;
            var factory = Nested("ReplacementFactory"); Set(factory, "Rule", factoryRule);
            var part = Binding(factory, data);
            object? control = null;
            try
            {
                root.SetActive(true);
                LoadProperties(hand, handRow, pick);
                LoadProperties(data, dataRow, box, "Install 2");
                var installedPose = NativeBagPartChecks.State(data, "Install 2").Actions[0];
                ((FsmOwnerDefault)Get(installedPose, "gameObject")).GameObject.Value = mesh.gameObject;
                NativeBagPartChecks.LoadActions(mount, mountRow, "Update 2");
                NativeBagPartChecks.Start(data); NativeBagPartChecks.Start(mount); NativeBagPartChecks.Start(hand);
                Action verify = () => Call("ValidatePartDistributorTimingGraph", hand, data, rule);
                Action<float, float> seed = (angle, tightness) =>
                {
                    NativeBagPartChecks.Fire(hand, "Probe idle"); NativeBagPartChecks.Fire(mount, "Probe idle");
                    data.FsmVariables.FindFsmFloat("SparkAngle").Value = angle;
                    mount.FsmVariables.FindFsmFloat("SparkAngle").Value = angle;
                    data.FsmVariables.FindFsmFloat("Tightness").Value = tightness;
                    mount.FsmVariables.FindFsmFloat("Tightness").Value = tightness;
                    mount.FsmVariables.FindFsmBool("Bolted").Value = tightness >= 1;
                    mesh.localRotation = Quaternion.Euler(0, 0, angle); pick.enabled = true;
                };
                Action<float, float> expected = (angle, tightness) =>
                {
                    Require(Near(data.FsmVariables.FindFsmFloat("SparkAngle").Value, angle)
                        && Near(mount.FsmVariables.FindFsmFloat("SparkAngle").Value, angle)
                        && Near(data.FsmVariables.FindFsmFloat("Tightness").Value, tightness)
                        && Near(mount.FsmVariables.FindFsmFloat("Tightness").Value, tightness)
                        && mount.FsmVariables.FindFsmBool("Bolted").Value == (tightness >= 1)
                        && Quaternion.Angle(mesh.localRotation, Quaternion.Euler(0, 0, angle)) < .01f
                        && Quaternion.Angle(pivot.localRotation, Quaternion.Euler(0, 0, -10)) < .01f
                        && partObject.transform.parent == mountObject.transform && mesh.parent == pivot
                        && partObject.transform.localPosition == Vector3.zero && partObject.transform.localRotation == Quaternion.identity,
                        "Native timing, mounting pose, or unchanged bolt state disagreed.");
                };
                Action<PartFitOperation, float, float> turn = (operation, angle, tightness) =>
                {
                    Require(control != null, "Control not bound.");
                    float result = Convert.ToSingle(Call("ExecutePartDistributorTiming", control, data, mount, rule, operation));
                    Require(Near(result, angle) && hand.ActiveStateName == "Wait", "Native timing did not reach its cooldown.");
                    expected(angle, tightness);
                };
                check("distributor: installed native timing graph is accepted", verify);
                check("distributor: root control binds the child mesh and independent pick", () =>
                {
                    control = Call("GetPartDistributorTiming", part);
                    Require(control != null && ReferenceEquals(Get(control, "Hand"), hand) && ReferenceEquals(Get(control, "Pick"), pick)
                        && ReferenceEquals(Get(control, "Mesh"), mesh) && ReferenceEquals(Get(control, "Scalar"), data.FsmVariables.FindFsmFloat("SparkAngle"))
                        && ReferenceEquals(Get(control, "Tightness"), data.FsmVariables.FindFsmFloat("Tightness")), "Wrong native control binding.");
                });
                check("distributor: native install applies timing only to the inner mesh", () =>
                {
                    seed(13.7f, 3); mesh.localRotation = Quaternion.Euler(0, 0, 10);
                    installedPose.OnEnter(); expected(13.7f, 3);
                });
                check("distributor: clockwise propagates the native positive timing step", () => { seed(7, 3); turn(PartFitOperation.RotateIncrease, 7.2f, 3); });
                check("distributor: counterwise preserves a fractional saved baseline", () => { seed(7.13f, 3); turn(PartFitOperation.RotateDecrease, 6.93f, 3); });
                check("distributor: stale mesh and scratch are refreshed from part Data", () =>
                {
                    seed(11.3f, 2); mesh.localRotation = Quaternion.Euler(0, 0, 2);
                    hand.FsmVariables.FindFsmFloat("Rotation").Value = 19;
                    turn(PartFitOperation.RotateIncrease, 11.5f, 2);
                });
                check("distributor: native clamps preserve fractional endpoint turns", () =>
                { seed(.05f, 0); turn(PartFitOperation.RotateDecrease, 0, 0); seed(19.95f, 7); turn(PartFitOperation.RotateIncrease, 20, 7); });
                check("distributor: unchanged mount loop preserves the accepted timing", () =>
                {
                    seed(9, 4); turn(PartFitOperation.RotateIncrease, 9.2f, 4);
                    data.FsmVariables.FindFsmFloat("SparkAngle").Value = 1;
                    NativeBagPartChecks.Fire(mount, "Update 2"); expected(9.2f, 4); NativeBagPartChecks.Fire(mount, "Probe idle");
                });
                check("distributor: fitted host mount remains coherent after a native turn", () =>
                { seed(7, 3); turn(PartFitOperation.RotateIncrease, 7.2f, 3); Require((bool)Call("NativeDistributorMountReady", part, control, mount)!, "Valid fitted mount refused."); });
                check("distributor: unrelated mount occupant is refused", () =>
                {
                    seed(7, 3); var active = mount.FsmVariables.FindFsmGameObject("ActivePart"); active.Value = root;
                    try { Require(!(bool)Call("NativeDistributorMountReady", part, control, mount)!, "Wrong occupant accepted."); }
                    finally { active.Value = partObject; }
                });
                check("distributor: mismatched mount timing is refused", () =>
                {
                    seed(7, 3); mount.FsmVariables.FindFsmFloat("SparkAngle").Value = 8;
                    Require(!(bool)Call("NativeDistributorMountReady", part, control, mount)!, "Stale mount timing accepted.");
                });
                check("distributor: changed runtime mesh and mount ownership refuse before mutation", () =>
                {
                    foreach (string change in new[] { "mesh reference", "mesh parent", "mount reference" })
                    {
                        seed(7, 3); var before = mesh.localRotation;
                        if (change == "mesh reference") hand.FsmVariables.FindFsmGameObject("MeshRotate").Value = root;
                        if (change == "mesh parent") mesh.SetParent(root.transform, false);
                        if (change == "mount reference") hand.FsmVariables.FindFsmGameObject("VINP").Value = root;
                        try
                        {
                            Require(!(bool)Call("NativeDistributorMountReady", part, control, mount)!, "Changed runtime ownership remained available.");
                            Reject(() => Call("ExecutePartDistributorTiming", control, data, mount, rule, PartFitOperation.RotateIncrease));
                            Require(Same(mesh.localRotation, before) && data.FsmVariables.FindFsmFloat("SparkAngle").Value == 7
                                && mount.FsmVariables.FindFsmFloat("SparkAngle").Value == 7 && hand.ActiveStateName == "Probe idle",
                                "Changed runtime ownership entered native mutation.");
                        }
                        finally
                        {
                            mesh.SetParent(pivot, false); hand.FsmVariables.FindFsmGameObject("MeshRotate").Value = mesh.gameObject;
                            hand.FsmVariables.FindFsmGameObject("VINP").Value = mountObject;
                        }
                    }
                });
                check("distributor: exact limits refuse a further turn before mutation", () =>
                {
                    foreach (float value in new[] { 0f, 20f })
                    {
                        seed(value, 3); Reject(() => Call("ExecutePartDistributorTiming", control, data, mount, rule,
                            value == 0 ? PartFitOperation.RotateDecrease : PartFitOperation.RotateIncrease));
                        expected(value, 3); Require(hand.ActiveStateName == "Probe idle", "Limit entered native actions.");
                    }
                });
                check("distributor: invalid saved timing is refused before native pose mutation", () =>
                {
                    foreach (float value in new[] { -1f, 21f, float.NaN, float.PositiveInfinity })
                    {
                        seed(7, 3); data.FsmVariables.FindFsmFloat("SparkAngle").Value = value; var before = mesh.localRotation;
                        Reject(() => Call("ExecutePartDistributorTiming", control, data, mount, rule, PartFitOperation.RotateIncrease));
                        Require(Same(mesh.localRotation, before) && mount.FsmVariables.FindFsmFloat("SparkAngle").Value == 7
                            && hand.ActiveStateName == "Probe idle", "Invalid timing changed native state.");
                    }
                });
                check("distributor: invalid or fully tight bolts refuse remote timing before mutation", () =>
                {
                    foreach (float value in new[] { -1f, 8f, float.NaN, float.PositiveInfinity })
                    {
                        seed(7, 3); data.FsmVariables.FindFsmFloat("Tightness").Value = value; var before = mesh.localRotation;
                        Reject(() => Call("ExecutePartDistributorTiming", control, data, mount, rule, PartFitOperation.RotateIncrease));
                        Require(Same(mesh.localRotation, before) && data.FsmVariables.FindFsmFloat("SparkAngle").Value == 7
                            && mount.FsmVariables.FindFsmFloat("SparkAngle").Value == 7 && hand.ActiveStateName == "Probe idle",
                            "Unavailable bolt state changed ignition timing.");
                    }
                });
                RunGates(check, hand, data, rule, verify, seed, pick);
                var clockwise = NativeBagPartChecks.State(hand, "Clockwise").Actions;
                var wait = NativeBagPartChecks.State(hand, "Wait").Actions;
                check("distributor: changed native timing step is refused", () => RejectChanged(verify, clockwise[1], "add", new FsmFloat(.5f)));
                check("distributor: recurring turn mutation is refused", () => RejectChanged(verify, clockwise[1], "everyFrame", true));
                check("distributor: changed timing clamp is refused", () => RejectChanged(verify, wait[1], "maxValue", new FsmFloat(21)));
                check("distributor: mount scalar redirection is refused", () => RejectChanged(verify, wait[3], "variableName", new FsmString { Value = "Wear" }));
                check("distributor: owner scalar redirection is refused", () => RejectChanged(verify, wait[4], "variableName", new FsmString { Value = "Tightness" }));
                check("distributor: world-space pose mutation is refused", () => RejectChanged(verify, wait[2], "space", Enum.ToObject(Get(wait[2], "space").GetType(), 0)));
                check("distributor: external mesh pose target is refused", () => RejectChanged(verify, wait[2], "gameObject",
                    new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = mountObject } }));
                check("distributor: changed installation pose is refused", () => RejectChanged(verify, installedPose, "zAngle", new FsmFloat { Name = "Wear", UseVariable = true }));
                check("distributor: changed native cooldown is refused", () => RejectChanged(verify, wait[5], "time", new FsmFloat(.1f)));
                check("distributor: malformed graph disables only the timing control", () =>
                {
                    var broken = Binding(factory, data); var action = clockwise[1]; object original = Get(action, "add");
                    try
                    {
                        Set(action, "add", new FsmFloat(1));
                        Require(Call("GetPartDistributorTiming", broken) == null && (bool)Get(broken, "DistributorTimingFailed")
                            && !(bool)Get(factory, "Failed") && data.enabled, "Timing fault escaped into the part factory.");
                    }
                    finally { Set(action, "add", original); }
                });
                RunGuest(check, factory, data, hand, mount, partObject, pivot, mesh, seed);
                RunPicks(check, control ?? throw new InvalidOperationException("Host control missing."), box, factory, data);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void RunGates(Action<string, Action> check, PlayMakerFSM hand, PlayMakerFSM data, object rule,
            Action verify, Action<float, float> seed, SphereCollider pick)
        {
            var gate = NativeBagPartChecks.State(hand, "Bolt loose?").Actions;
            var tool = (FsmFloat)Get(gate[0], "float1");
            check("distributor: native loose-bolt gate rejects tightness eight", () =>
            {
                seed(7, 8); tool.Value = 0; NativeBagPartChecks.Fire(hand, "Bolt loose?");
                Require(hand.ActiveStateName == "Delay", "Fully tightened distributor entered timing input.");
                NativeBagPartChecks.Fire(hand, "Wait Player 2"); Require(!pick.enabled, "Tight distributor kept its pick enabled.");
            });
            check("distributor: native bare-hand gate rejects a selected tool", () =>
            { seed(7, 3); tool.Value = 7; NativeBagPartChecks.Fire(hand, "Bolt loose?"); Require(hand.ActiveStateName == "Delay", "Equipped tool entered timing input."); tool.Value = 0; });
            check("distributor: native binding re-enables the loose root sphere", () =>
            {
                seed(7, 3); pick.enabled = false; hand.FsmVariables.FindFsmGameObject("VINP").Value = null;
                NativeBagPartChecks.Fire(hand, "Collider");
                Require(pick.enabled && pick.isTrigger && hand.FsmVariables.FindFsmGameObject("VINP").Value
                    == data.FsmVariables.FindFsmGameObject("InstallPoint").Value, "Native loose pick or mount lookup failed.");
            });
            check("distributor: changed loose-bolt threshold is refused", () => RejectChanged(verify, gate[2], "float2", new FsmFloat(9)));
            check("distributor: changed bare-hand tool gate is refused", () => RejectChanged(verify, gate[0], "float2", new FsmFloat(7)));
            check("distributor: changed native pick distance is refused", () => RejectChanged(verify,
                NativeBagPartChecks.State(hand, "Wait Player").Actions[1], "rayDistance", new FsmFloat(2)));
        }

        private static void RunGuest(Action<string, Action> check, object factory, PlayMakerFSM data, PlayMakerFSM hand,
            PlayMakerFSM mount, GameObject partObject, Transform pivot, Transform mesh, Action<float, float> seed)
        {
            seed(7, 3); var guest = Binding(factory, data); Set(guest, "Replica", true); Set(guest, "FittedPresentation", true);
            var control = Call("GetPartDistributorTiming", guest)
                ?? throw new InvalidOperationException("Guest timing control unavailable before isolation.");
            // Replica Idle rewrites root colliders after binding; the cached
            // timing proof must survive that presentation-only initialization.
            partObject.GetComponent<SphereCollider>().isTrigger = false;
            hand.enabled = false; data.enabled = false; mount.enabled = false;
            var globals = data.Fsm.GlobalTransitions; data.Fsm.GlobalTransitions = new FsmTransition[0];
            string handState = hand.ActiveStateName, dataState = data.ActiveStateName;
            var pivotPose = pivot.localRotation; var rootPose = partObject.transform.localRotation;
            try
            {
                check("distributor: fitted guest mesh applies host timing without native replay", () =>
                {
                    data.FsmVariables.FindFsmFloat("SparkAngle").Value = 14.5f; Call("ApplyPartDistributorTimingPose", guest);
                    Require(Quaternion.Angle(mesh.localRotation, Quaternion.Euler(0, 0, 14.5f)) < .01f
                        && mount.FsmVariables.FindFsmFloat("SparkAngle").Value == 7 && data.FsmVariables.FindFsmFloat("Tightness").Value == 3
                        && hand.ActiveStateName == handState && data.ActiveStateName == dataState && !hand.enabled && !data.enabled && !mount.enabled
                        && ReferenceEquals(Get(guest, "DistributorTiming"), control) && !(bool)Get(guest, "DistributorTimingFailed")
                        && Same(pivot.localRotation, pivotPose) && Same(partObject.transform.localRotation, rootPose)
                        && partObject.transform.parent == mount.transform, "Guest pose replayed native logic or moved an assembly.");
                });
                check("distributor: initial loose replica retains the authored mesh pose", () =>
                {
                    Set(guest, "FittedPresentation", false); mesh.localRotation = Quaternion.Euler(0, 0, 10);
                    var before = mesh.localRotation; Call("ApplyPartDistributorTimingPose", guest);
                    Require(Same(mesh.localRotation, before), "Unfitted Data random timing replaced native prefab pose.");
                });
                check("distributor: removed replica retains its last fitted timing", () =>
                {
                    Set(guest, "FittedPresentation", true); Call("ApplyPartDistributorTimingPose", guest);
                    Set(guest, "FittedPresentation", false); var before = mesh.localRotation;
                    data.FsmVariables.FindFsmFloat("SparkAngle").Value = 2; Call("ApplyPartDistributorTimingPose", guest);
                    Require(Same(mesh.localRotation, before), "Removed timing pose was reset.");
                });
                check("distributor: invalid guest timing disables its control before changing pose", () =>
                {
                    Set(guest, "FittedPresentation", true); data.FsmVariables.FindFsmFloat("SparkAngle").Value = float.NaN;
                    var before = mesh.localRotation; Call("ApplyPartDistributorTimingPose", guest);
                    Require((bool)Get(guest, "DistributorTimingFailed") && !(bool)Get(factory, "Failed") && Same(mesh.localRotation, before)
                        && mount.FsmVariables.FindFsmFloat("SparkAngle").Value == 7, "Invalid guest timing escaped containment.");
                });
                check("distributor: moved guest mesh is refused without touching its new owner", () =>
                {
                    var moved = Binding(factory, data); Set(moved, "Replica", true); Set(moved, "FittedPresentation", true);
                    Set(moved, "DistributorTiming", control); data.FsmVariables.FindFsmFloat("SparkAngle").Value = 4;
                    mesh.SetParent(mount.transform, false); var before = mesh.localRotation;
                    try
                    {
                        Call("ApplyPartDistributorTimingPose", moved);
                        Require((bool)Get(moved, "DistributorTimingFailed") && !(bool)Get(factory, "Failed") && Same(mesh.localRotation, before)
                            && mount.FsmVariables.FindFsmFloat("SparkAngle").Value == 7, "Foreign guest mesh was changed.");
                    }
                    finally { mesh.SetParent(pivot, false); }
                });
            }
            finally { data.Fsm.GlobalTransitions = globals; }
        }

        private static void RunPicks(Action<string, Action> check, object control, BoxCollider box, object factory, PlayMakerFSM data)
        {
            var straight = new Ray(data.transform.TransformPoint(new Vector3(0, 0, -.5f)), data.transform.forward);
            float expectedBox = .5f + box.center.z - box.size.z / 2;
            check("distributor: own enclosing removal box does not hide its timing sphere", () =>
            {
                Require(Pick(control, box, straight, 1, out float distance, out bool selected) && selected
                    && Near(distance, expectedBox), "The own removal box blocked its timing control or lost the visible surface.");
            });
            check("distributor: box outside the timing sphere still obstructs rear controls", () =>
            {
                var ray = new Ray(data.transform.TransformPoint(new Vector3(.035f, .03f, -.5f)), data.transform.forward);
                Require(Pick(control, box, ray, 1, out float distance, out bool selected) && !selected
                    && Near(distance, expectedBox), "Missing timing sphere made the solid removal box transparent.");
            });
            check("distributor: an earlier obstruction limits both pick shapes", () =>
            {
                Require(!Pick(control, box, straight, expectedBox - .01f, out _, out _), "Pick crossed an earlier obstacle.");
                Require(Pick(control, box, straight, expectedBox + .01f, out _, out bool selected) && !selected,
                    "A sphere beyond the original range became selectable through its box.");
            });
            check("distributor: front and rear controls use their nearest visible surfaces", () =>
            {
                var rear = Child(data.transform.parent.gameObject, "rear timing pick"); rear.transform.localPosition = new Vector3(0, 0, .2f);
                var rearSphere = rear.AddComponent<SphereCollider>(); rearSphere.radius = .04f; rearSphere.center = new Vector3(0, 0, .02f);
                var rearBox = rear.AddComponent<BoxCollider>(); rearBox.center = box.center; rearBox.size = box.size;
                var rearControl = Nested("PartDistributorTiming"); Set(rearControl, "Pick", rearSphere);
                try
                {
                    Require(Pick(rearControl, rearBox, straight, 1, out float rearDistance, out bool rearSelected) && rearSelected
                        && Pick(control, box, straight, 1, out float frontDistance, out bool frontSelected) && frontSelected
                        && Near(rearDistance - frontDistance, .2f) && frontDistance < rearDistance,
                        "Candidate ordering used the hidden sphere instead of the front surface.");
                }
                finally { UnityEngine.Object.DestroyImmediate(rear); }
            });
            var binding = Binding(factory, data); Set(binding, "Replica", true); Set(binding, "FittedPresentation", true);
            var state = new ReplacementPartState { Revision = 4, Installed = true, AssemblyId = 1, ParentKind = PartParentKind.Vehicle };
            check("distributor: deferred applied revision cannot offer a newer timing intent", () =>
            {
                Set(binding, "HasAppliedState", true); Set(binding, "AppliedRevision", 3u);
                Require(!(bool)Call("PartAdjustmentViewReady", binding, state)!, "An old visible pose used a newer accepted revision.");
                Set(binding, "AppliedRevision", 4u); Require((bool)Call("PartAdjustmentViewReady", binding, state)!, "Current applied view remained unavailable.");
                state.Revision = 5; Require(!(bool)Call("PartAdjustmentViewReady", binding, state)!, "New receipt skipped application readiness.");
            });
            check("distributor: applied revision zero remains valid after counter wrap", () =>
            {
                state.Revision = 0; Set(binding, "AppliedRevision", 0u); Set(binding, "HasAppliedState", true);
                Require((bool)Call("PartAdjustmentViewReady", binding, state)!, "Wrapped applied revision was treated as missing.");
                Set(binding, "HasAppliedState", false);
                Require(!(bool)Call("PartAdjustmentViewReady", binding, state)!, "Unapplied revision zero was mistaken for a wrapped state.");
            });
            check("distributor: loose or hidden views cannot expose timing controls", () =>
            {
                Set(binding, "HasAppliedState", true); state.Installed = false;
                Require(!(bool)Call("PartAdjustmentViewReady", binding, state)!, "Loose snapshot retained timing input.");
                state.Installed = true; data.gameObject.SetActive(false);
                try { Require(!(bool)Call("PartAdjustmentViewReady", binding, state)!, "Hidden stale mesh retained timing input."); }
                finally { data.gameObject.SetActive(true); }
            });
        }

        private static bool Pick(object control, BoxCollider box, Ray ray, float range, out float distance, out bool selected)
        {
            object?[] args = { control, box, ray, range, 0f, false };
            bool result = (bool)Call("TryDistributorAdjustmentPick", args)!;
            distance = Convert.ToSingle(args[4]); selected = Convert.ToBoolean(args[5]); return result;
        }

        private static void LoadProperties(PlayMakerFSM fsm, Dictionary<string, object> row, Collider collider, params string[] selected)
        {
            var names = new List<string>(); var properties = new Dictionary<string, Dictionary<int, Dictionary<string, object>>>();
            foreach (Dictionary<string, object> state in (IEnumerable)row["states"])
            {
                string name = (string)state["name"];
                if (selected.Length != 0 && Array.IndexOf(selected, name) < 0) continue;
                names.Add(name); var actions = (List<object>)state["actions"];
                for (int i = 0; i < actions.Count; i++)
                {
                    var action = (Dictionary<string, object>)actions[i];
                    foreach (Dictionary<string, object> p in (IEnumerable)action["parameters"])
                        if ((string)p["type"] == "FsmProperty")
                        {
                            if (!properties.ContainsKey(name)) properties.Add(name, new Dictionary<int, Dictionary<string, object>>());
                            properties[name].Add(i, (Dictionary<string, object>)p["value"]);
                        }
                    if (properties.ContainsKey(name) && properties[name].ContainsKey(i)) action["parameters"] = new List<object>();
                }
            }
            NativeBagPartChecks.LoadActions(fsm, row, names.ToArray());
            foreach (var state in properties)
                foreach (var entry in state.Value)
                {
                    var value = entry.Value; var target = (Dictionary<string, object>)value["TargetObject"];
                    var targetObject = new FsmObject { Value = collider, ObjectType = collider.GetType(),
                        Name = (string)target["name"], UseVariable = Convert.ToBoolean(target["useVariable"]) };
                    var property = new FsmProperty { TargetObject = targetObject, TargetTypeName = (string)value["TargetTypeName"],
                        TargetType = collider.GetType(), PropertyName = (string)value["PropertyName"], setProperty = Convert.ToBoolean(value["setProperty"]),
                        BoolParameter = new FsmBool(Convert.ToBoolean(((Dictionary<string, object>)value["BoolParameter"])["value"])) };
                    var action = NativeBagPartChecks.State(fsm, state.Key).Actions[entry.Key]; Set(action, "targetProperty", property);
                    action.Init(NativeBagPartChecks.State(fsm, state.Key));
                }
        }

        private static GameObject Child(GameObject parent, string name) { var value = new GameObject(name); value.transform.SetParent(parent.transform, false); return value; }
        private static object Binding(object factory, PlayMakerFSM data)
        { var value = Nested("ReplacementBinding"); Set(value, "Factory", factory); Set(value, "Data", data); Set(value, "NativeId", "VIN1317"); return value; }
        private static object Nested(string name) => Activator.CreateInstance(Items.GetNestedType(name, BindingFlags.NonPublic), true);
        private static object Get(object target, string name) => target.GetType().GetField(name, Members).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Members).SetValue(target, value);
        private static object? Call(string name, params object?[] args) => Items.GetMethod(name, Static).Invoke(null, args);
        private static bool Near(float left, float right) => Math.Abs(left - right) < .001f;
        private static bool Same(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        private static void RejectChanged(Action verify, object action, string field, object replacement)
        { verify(); object original = Get(action, field); try { Set(action, field, replacement); Reject(verify); } finally { Set(action, field, original); } }
        private static void Reject(Action action)
        {
            try { action(); } catch (TargetInvocationException e) { if (e.InnerException is InvalidOperationException) return; throw; }
            throw new InvalidOperationException("Unsafe native operation or graph was accepted.");
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
