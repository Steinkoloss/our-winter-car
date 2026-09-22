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
    internal static class PartHandScrewChecks
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
                if ((string)Get(candidate, "Prefix") == "OILFILTR0") factoryRule = candidate;
            if (factoryRule == null) throw new InvalidOperationException("Oil-filter catalog rule missing.");
            var rule = Get(factoryRule, "HandScrew") ?? throw new InvalidOperationException("Hand-screw catalog missing.");
            var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var reader = Activator.CreateInstance(readerType, Members, null, new object[] {
                File.ReadAllText(Path.Combine(Application.dataPath, "../hand-screw-probe.json")) }, null);
            var input = (Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null);
            var rows = (List<object>)input["fsms"];
            var dataRow = NativeBagPartChecks.Find(rows, "OILFILTR0", "Data");
            var screwRow = NativeBagPartChecks.Find(rows, "OILFILTR0", "Screw");
            var mountRow = NativeBagPartChecks.Find(rows, "CARPARTS/StartParts/VIN1010/VINP_Oilfilter", "Data");
            // Native Bolted/Unbolted retain disabled collider actions. Preserve
            // their type and disabled flag without resolving unused asset pointers.
            foreach (Dictionary<string, object> state in (IEnumerable)dataRow["states"])
                foreach (Dictionary<string, object> action in (IEnumerable)state["actions"])
                    if (!(bool)action["enabled"]) action["parameters"] = new List<object>();
            var mountObject = new GameObject("probe oil-filter mount"); mountObject.SetActive(false);
            var partObject = new GameObject("OILFILTR01"); partObject.transform.SetParent(mountObject.transform, false);
            partObject.layer = 19;
            var pick = partObject.AddComponent<BoxCollider>(); pick.isTrigger = true;
            pick.size = new Vector3(.07350899f, .07350802f, .10816502f);
            pick.center = new Vector3(-.0000034999f, 0, .0031145f);
            var data = NativeBagPartChecks.MakeFsm(partObject, dataRow);
            var screw = NativeBagPartChecks.MakeFsm(partObject, screwRow);
            var mount = NativeBagPartChecks.MakeFsm(mountObject, mountRow);
            data.FsmVariables.ObjectVariables = new[] { new FsmObject { Name = "Collider", UseVariable = true, Value = pick } };
            data.FsmVariables.FindFsmGameObject("Owner").Value = partObject;
            data.FsmVariables.FindFsmGameObject("InstallPoint").Value = mountObject;
            data.FsmVariables.FindFsmInt("AssemblyID").Value = 1;
            data.FsmVariables.FindFsmString("ID").Value = "OILFILTR01";
            data.FsmVariables.FindFsmString("UTAssemblyID").Value = "OILFILTR01AID";
            data.FsmVariables.FindFsmString("UTPos").Value = "OILFILTR01POS";
            mount.FsmVariables.FindFsmGameObject("ActivePart").Value = partObject;
            mount.FsmVariables.FindFsmGameObject("AssemblyPoint").Value = mountObject;
            mount.FsmVariables.FindFsmBool("Installed").Value = true;
            try
            {
                mountObject.SetActive(true);
                NativeBagPartChecks.LoadActions(data, dataRow, "Tightness?", "Bolted", "Unbolted", "Mouse off", "Mouse over");
                var names = new List<string>();
                foreach (Dictionary<string, object> state in (IEnumerable)screwRow["states"]) names.Add((string)state["name"]);
                NativeBagPartChecks.LoadActions(screw, screwRow, names.ToArray());
                NativeBagPartChecks.Start(data); NativeBagPartChecks.Start(mount); NativeBagPartChecks.Start(screw);
                Action verify = () => Call("ValidatePartHandScrewGraph", screw, data, rule);
                Action<float> seed = value =>
                {
                    NativeBagPartChecks.Fire(screw, "Probe idle");
                    data.FsmVariables.FindFsmFloat("Tightness").Value = value;
                    mount.FsmVariables.FindFsmFloat("Tightness").Value = value;
                    mount.FsmVariables.FindFsmBool("Bolted").Value = value >= 1;
                    partObject.transform.localPosition = new Vector3(0, 0, -value / 400f);
                    partObject.transform.localRotation = Quaternion.Euler(0, 0, value * 20);
                };
                Action<float> expected = value =>
                {
                    Require(Math.Abs(data.FsmVariables.FindFsmFloat("Tightness").Value - value) < .001f
                        && Math.Abs(mount.FsmVariables.FindFsmFloat("Tightness").Value - value) < .001f
                        && mount.FsmVariables.FindFsmBool("Bolted").Value == (value >= 1)
                        && Vector3.Distance(partObject.transform.localPosition, new Vector3(0, 0, -value / 400f)) < .00001f
                        && Quaternion.Angle(partObject.transform.localRotation, Quaternion.Euler(0, 0, value * 20)) < .01f
                        && partObject.transform.parent == mountObject.transform && pick.enabled && pick.isTrigger,
                        "Part, mount, collider or native root pose disagreed.");
                };
                Action<PartFitOperation, float> turn = (operation, value) =>
                {
                    float actual = Convert.ToSingle(Call("ExecutePartHandScrew", screw, data, rule, operation));
                    Require(Math.Abs(actual - value) < .001f && screw.ActiveStateName == "Wait1 2", "Native turn did not reach its cooldown.");
                    expected(value);
                };
                check("hand screw: installed native graph is accepted", verify);
                var factory = Nested("ReplacementFactory"); Set(factory, "Rule", factoryRule);
                var part = Nested("ReplacementBinding"); Set(part, "Factory", factory); Set(part, "Data", data); Set(part, "NativeId", "OILFILTR01");
                object? control = null;
                check("hand screw: native host control binds the root pick and scalar", () =>
                {
                    control = Call("GetPartHandScrew", part);
                    Require(control != null && ReferenceEquals(Get(control, "Screw"), screw) && ReferenceEquals(Get(control, "Pick"), pick)
                        && ReferenceEquals(Get(control, "Scalar"), data.FsmVariables.FindFsmFloat("Tightness"))
                        && Convert.ToInt32(Get(control, "ScalarIndex")) == 1, "Host control binding failed.");
                });
                check("hand screw: native tighten writes Data, mount and root pose", () => { seed(3); turn(PartFitOperation.HandTighten, 4); });
                check("hand screw: native loosen clears the mount bolted flag at zero", () => { seed(1); turn(PartFitOperation.HandLoosen, 0); });
                check("hand screw: stale pose scratch is refreshed before loosening", () =>
                { seed(8); screw.FsmVariables.FindFsmFloat("Tightness").Value = -.02f; turn(PartFitOperation.HandLoosen, 7); });
                check("hand screw: stale upper scratch cannot block a valid tighten", () =>
                { seed(0); screw.FsmVariables.FindFsmFloat("Tightness").Value = 8; turn(PartFitOperation.HandTighten, 1); });
                check("hand screw: installed host mount remains coherent after native turn", () =>
                {
                    seed(4); turn(PartFitOperation.HandLoosen, 3);
                    Require(control != null && (bool)Call("NativeHandScrewMountReady", part, control, mount)!, "Host rejected the resulting native attachment.");
                });
                check("hand screw: mount mismatch is unavailable before a turn", () =>
                {
                    seed(3); mount.FsmVariables.FindFsmFloat("Tightness").Value = 4;
                    Require(control != null && !(bool)Call("NativeHandScrewMountReady", part, control, mount)!, "Mismatched mount scalar was accepted.");
                });
                check("hand screw: zero and eight refuse further turns without mutation", () =>
                {
                    foreach (int value in new[] { 0, 8 })
                    {
                        seed(value); Reject(() => Call("ExecutePartHandScrew", screw, data, rule,
                            value == 0 ? PartFitOperation.HandLoosen : PartFitOperation.HandTighten)); expected(value);
                        Require(screw.ActiveStateName == "Probe idle", "A refused boundary turn entered native actions.");
                    }
                });
                check("hand screw: fractional saved tightness is refused before mutation", () =>
                {
                    seed(7.5f); Reject(() => Call("ExecutePartHandScrew", screw, data, rule, PartFitOperation.HandTighten)); expected(7.5f);
                    Require(screw.ActiveStateName == "Probe idle", "An invalid saved scalar entered native actions.");
                });
                var increase = NativeBagPartChecks.State(screw, "Screw 2").Actions;
                var pose = NativeBagPartChecks.State(screw, "Set").Actions;
                check("hand screw: changed step is refused", () => RejectChanged(verify, increase[1], "addValue", new FsmFloat(.5f)));
                check("hand screw: changed saved scalar is refused", () => RejectChanged(verify, increase[1], "variableName", new FsmString { Value = "Dirt" }));
                check("hand screw: changed upper guard is refused", () => RejectChanged(verify, increase[0], "float2", new FsmFloat(9)));
                check("hand screw: recurring native mutation is refused", () => RejectChanged(verify, increase[1], "everyFrame", true));
                check("hand screw: changed BOLTING notification is refused", () => RejectChanged(verify, increase[2], "sendEvent", new FsmString { Value = "UNINSTALL" }));
                check("hand screw: deferred native notification is refused", () => RejectChanged(verify, increase[2], "delay", new FsmFloat(.1f)));
                check("hand screw: changed cooldown is refused", () => RejectChanged(verify,
                    NativeBagPartChecks.State(screw, "Wait1 2").Actions[0], "time", new FsmFloat(.1f)));
                check("hand screw: changed bare-hand tool gate is refused", () => RejectChanged(verify,
                    NativeBagPartChecks.State(screw, "Check tool").Actions[0], "float2", new FsmFloat(7)));
                check("hand screw: changed pick distance is refused", () => RejectChanged(verify,
                    NativeBagPartChecks.State(screw, "Mouse off 2").Actions[1], "rayDistance", new FsmFloat(3)));
                check("hand screw: world-space pose is refused", () => RejectChanged(verify, pose[3], "space",
                    Enum.ToObject(pose[3].GetType().GetField("space").FieldType, 0)));
                check("hand screw: external pose target is refused", () => RejectChanged(verify, pose[4], "gameObject",
                    new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = mountObject } }));
                check("hand screw: changed root translation ratio is refused", () => RejectChanged(verify, pose[2], "divideBy", new FsmFloat(-200)));
                check("hand screw: changed BOLTING mount write is refused", () => RejectChanged(verify,
                    NativeBagPartChecks.State(data, "Tightness?").Actions[0], "variableName", new FsmString { Value = "Dirt" }));
                check("hand screw: enabled native collider removal is refused", () =>
                {
                    verify(); var disabled = NativeBagPartChecks.State(data, "Bolted").Actions[1];
                    try { disabled.Enabled = true; Reject(verify); }
                    finally { disabled.Enabled = false; }
                });
                check("hand screw: guest pose applies the received scalar without native writes", () =>
                {
                    seed(2); screw.enabled = false;
                    part = Nested("ReplacementBinding"); Set(part, "Factory", factory); Set(part, "Data", data);
                    Set(part, "NativeId", "OILFILTR01"); Set(part, "Replica", true); Set(part, "FittedPresentation", true);
                    var guestControl = Call("GetPartHandScrew", part);
                    Require(guestControl != null, "Guest control did not bind before native globals were suppressed.");
                    data.FsmVariables.FindFsmFloat("Tightness").Value = 5;
                    string before = data.ActiveStateName;
                    var globals = data.Fsm.GlobalTransitions;
                    try
                    {
                        var retained = new List<FsmTransition>();
                        foreach (var transition in globals) if (transition.EventName == "GARBAGE") retained.Add(transition);
                        data.Fsm.GlobalTransitions = retained.ToArray();
                        Call("ApplyPartHandScrewPose", part);
                        Require(Quaternion.Angle(partObject.transform.localRotation, Quaternion.Euler(0, 0, 100)) < .01f
                            && Vector3.Distance(partObject.transform.localPosition, new Vector3(0, 0, -.0125f)) < .00001f
                            && mount.FsmVariables.FindFsmFloat("Tightness").Value == 2 && data.ActiveStateName == before
                            && !screw.enabled && partObject.transform.parent == mountObject.transform
                            && !(bool)Get(part, "HandScrewFailed") && ReferenceEquals(Get(part, "HandScrew"), guestControl),
                            "Suppressed guest pose lost its control or replayed native BOLTING.");
                    }
                    finally { data.Fsm.GlobalTransitions = globals; }
                });
                check("hand screw: loose guest pose does not override free item motion", () =>
                {
                    Set(part, "FittedPresentation", false);
                    var position = new Vector3(1, 2, 3); var rotation = Quaternion.Euler(20, 30, 40);
                    partObject.transform.localPosition = position; partObject.transform.localRotation = rotation;
                    position = partObject.transform.localPosition; rotation = partObject.transform.localRotation;
                    Call("ApplyPartHandScrewPose", part);
                    var after = partObject.transform.localRotation;
                    Require(partObject.transform.localPosition == position && after.x == rotation.x && after.y == rotation.y
                        && after.z == rotation.z && after.w == rotation.w,
                        "Loose filter was moved back onto its screw mount.");
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(mountObject); }
        }

        private static object Nested(string name) => Activator.CreateInstance(Items.GetNestedType(name, BindingFlags.NonPublic), true);
        private static object Get(object target, string name) => target.GetType().GetField(name, Members).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Members).SetValue(target, value);
        private static object? Call(string name, params object?[] args) => Items.GetMethod(name, Static).Invoke(null, args);
        private static void RejectChanged(Action verify, object action, string fieldName, object replacement)
        {
            verify(); var field = action.GetType().GetField(fieldName); object original = field.GetValue(action);
            try { field.SetValue(action, replacement); Reject(verify); }
            finally { field.SetValue(action, original); }
        }
        private static void Reject(Action action)
        {
            try { action(); }
            catch (TargetInvocationException e) { if (e.InnerException is InvalidOperationException) return; throw; }
            throw new InvalidOperationException("Unsafe native operation or binding was accepted.");
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
