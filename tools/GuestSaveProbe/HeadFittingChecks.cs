using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    internal static class HeadFittingChecks
    {
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        internal static void Run(Action<string, Action> check)
        {
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true); catalog.GetMethod("EnsureLoaded", Members).Invoke(null, null);
            var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var reader = Activator.CreateInstance(readerType, Members, null, new object[] { File.ReadAllText(Path.Combine(Application.dataPath, "../native-head.json")) }, null);
            var json = (Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null);
            var rows = (List<object>)json["fsms"];
            var headRow = NativeBagPartChecks.Find(rows, "CARPARTS/StartParts/VIN1110", "Data");
            var mountRow = NativeBagPartChecks.Find(rows, "CARPARTS/StartParts/VIN1010/VINP_Cylinderhead", "Data");
            var root = new GameObject("native head fitting fixture"); root.SetActive(false);
            var part = new GameObject("head"); part.transform.SetParent(root.transform, false); part.AddComponent<Rigidbody>();
            var point = new GameObject("mount"); point.transform.SetParent(root.transform, false);
            var blocker = new GameObject("native prerequisite"); blocker.transform.SetParent(root.transform, false);
            var head = NativeBagPartChecks.MakeFsm(part, headRow); var mount = NativeBagPartChecks.MakeFsm(point, mountRow);
            var obstacle = NativeBagPartChecks.MakeFsm(blocker, mountRow);
            head.FsmVariables.FindFsmGameObject("Owner").Value = part;
            head.FsmVariables.FindFsmGameObject("InstallPoint").Value = point;
            mount.FsmVariables.FindFsmGameObject("AssemblyPoint").Value = point;
            mount.FsmVariables.FindFsmGameObject("db_Installed1").Value = blocker;
            try
            {
                root.SetActive(true);
                NativeBagPartChecks.LoadActions(head, headRow, "Another part?", "Tightness?", "Remove");
                NativeBagPartChecks.LoadActions(mount, mountRow, "Idle", "Allow install?", "Allow removal?", "Near", "Far", "Install 1");
                NativeBagPartChecks.State(head, "Install 2").Transitions = new FsmTransition[0];
                NativeBagPartChecks.State(mount, "Install 2").Transitions = new FsmTransition[0];
                foreach (string name in new[] { "Mouse off" }) NativeBagPartChecks.State(head, name).Transitions = new FsmTransition[0];
                foreach (var fsm in new[] { head, mount, obstacle }) NativeBagPartChecks.Start(fsm);
                var type = Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true);
                Action validate = () => type.GetMethod("ValidateHeadFitBindings", Members).Invoke(null, new object[] { head, mount });
                Action reset = () => { head.FsmVariables.FindFsmInt("AssemblyID").Value = 0; mount.FsmVariables.FindFsmBool("Installed").Value = false;
                    obstacle.FsmVariables.FindFsmBool("Installed").Value = false; NativeBagPartChecks.Fire(head, "Stop"); NativeBagPartChecks.Fire(mount, "Idle"); };
                check("head fitting: installed native request and mount bindings validate", validate);
                check("head fitting: native entry reaches Near only at the empty nearby mount", () =>
                {
                    reset(); head.SendEvent("ASSEMBLING"); Require(mount.ActiveStateName == "Near"
                        && mount.FsmVariables.FindFsmGameObject("ActivePart").Value == part && head.FsmVariables.FindFsmInt("AssemblyID").Value == 0);
                });
                check("head fitting: native confirmation writes assembly exactly once", () =>
                { mount.SendEvent("PROCEED"); Require(head.FsmVariables.FindFsmInt("AssemblyID").Value == 1 && mount.ActiveStateName == "Install 2"); });
                check("head fitting: native occupied mount refuses a second head", () =>
                { reset(); mount.FsmVariables.FindFsmBool("Installed").Value = true; head.SendEvent("ASSEMBLING"); Require(mount.ActiveStateName == "Idle"); });
                check("head fitting: native installed prerequisite blocks fitting", () =>
                { reset(); obstacle.FsmVariables.FindFsmBool("Installed").Value = true; head.SendEvent("ASSEMBLING"); Require(mount.ActiveStateName == "Idle" && head.FsmVariables.FindFsmInt("AssemblyID").Value == 0); });
                check("head fitting: native distant part stays Far until cancelled", () =>
                { reset(); part.transform.localPosition = new Vector3(2, 0, 0); head.SendEvent("ASSEMBLING"); Require(mount.ActiveStateName == "Far"); mount.SendEvent("BACK"); Require(mount.ActiveStateName == "Idle"); part.transform.localPosition = Vector3.zero; });
                check("head removal: native blocker refuses removal without changing assembly", () =>
                { reset(); head.FsmVariables.FindFsmInt("AssemblyID").Value = 1; obstacle.FsmVariables.FindFsmBool("Installed").Value = true;
                    NativeBagPartChecks.Fire(head, "Remove"); Require(mount.ActiveStateName == "UPDATE" && head.FsmVariables.FindFsmInt("AssemblyID").Value == 1); });
                check("head removal: clear native prerequisite reaches removal write state", () =>
                { obstacle.FsmVariables.FindFsmBool("Installed").Value = false; NativeBagPartChecks.State(mount, "Remove part").Transitions = new FsmTransition[0];
                    NativeBagPartChecks.Fire(head, "Remove"); Require(mount.ActiveStateName == "Remove part"); });
                foreach (float tightness in new[] { 0f, .99f, 1f, 72f })
                {
                    float value = tightness;
                    check("head removal: native tightness boundary " + value, () =>
                    { head.FsmVariables.FindFsmFloat("Tightness").Value = value; head.SendEvent("BOLTING"); Require(head.ActiveStateName == (value < 1 ? "Mouse off" : "Bolted")); });
                }
                check("head fitting: changed blocker destination is refused", () =>
                {
                    var action = NativeBagPartChecks.State(mount, "Allow install?").Actions[0]; var field = action.GetType().GetField("variableName"); var saved = field.GetValue(action);
                    field.SetValue(action, new FsmString { Value = "Unrelated" });
                    try { try { validate(); throw new InvalidOperationException("Changed native prerequisite accepted."); }
                        catch (TargetInvocationException e) { Require(e.InnerException is InvalidOperationException); } }
                    finally { field.SetValue(action, saved); }
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static void Require(bool value) { if (!value) throw new InvalidOperationException("Native head fitting assertion failed."); }
    }
}
