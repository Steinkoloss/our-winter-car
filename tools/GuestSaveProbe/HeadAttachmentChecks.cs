using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static class HeadAttachmentChecks
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        internal static void Run(Action<string, Action> check)
        {
            var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var reader = Activator.CreateInstance(readerType, Members, null, new object[] {
                File.ReadAllText(Path.Combine(Application.dataPath, "../native-valve-head.json")) }, null);
            var json = (Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null);
            var row = NativeBagPartChecks.Find((List<object>)json["fsms"], "CARPARTS/StartParts/VIN1110", "Data");
            foreach (bool fitted in new[] { false, true }) RunCase(check, row, fitted);
        }

        private static void RunCase(Action<string, Action> check, Dictionary<string, object> row, bool fitted)
        {
            var root = new GameObject("head attachment fixture"); root.SetActive(false);
            var head = new GameObject("head"); head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(2, 3, 4); head.transform.localRotation = Quaternion.Euler(10, 20, 30);
            head.transform.localScale = new Vector3(.9f, 1.1f, 1.2f);
            var first = head.AddComponent<BoxCollider>(); var second = head.AddComponent<BoxCollider>();
            first.enabled = !fitted; second.isTrigger = fitted;
            var child = new GameObject("VINP_Thermostat"); child.transform.SetParent(head.transform, false);
            child.transform.localPosition = new Vector3(.1f, .2f, .3f);
            var nestedPick = child.AddComponent<SphereCollider>(); nestedPick.isTrigger = true;
            var mount = new GameObject("VINP_Cylinderhead"); mount.transform.SetParent(root.transform, false);
            mount.transform.localPosition = new Vector3(30, 0, 0); mount.transform.localRotation = Quaternion.Euler(0, 90, 0);
            var data = NativeBagPartChecks.MakeFsm(head, row);
            data.FsmVariables.FindFsmGameObject("Owner").Value = head;
            var bolts = new GameObject("Bolts"); bolts.transform.SetParent(head.transform, false);
            data.FsmVariables.FindFsmGameObject("Bolts").Value = bolts;
            data.FsmVariables.FindFsmInt("AssemblyID").Value = fitted ? 1 : 0;
            data.FsmVariables.FindFsmFloat("Wear").Value = 71; data.FsmVariables.FindFsmFloat("Tightness").Value = 13;
            Rigidbody? original = fitted ? null : head.AddComponent<Rigidbody>();
            if (original != null) { original.mass = 17; original.isKinematic = true; original.detectCollisions = false; }
            var position = head.transform.localPosition; var rotation = head.transform.localRotation; var scale = head.transform.localScale;
            object? view = null;
            string label = "head " + (fitted ? "saved fitted: " : "saved loose: ");
            try
            {
                root.SetActive(true); NativeBagPartChecks.Start(data);
                view = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.GuestCylinderHead", true), Members, null, new object[] { data }, null);
                Call(view!, "Pause");
                check(label + "native Data is paused before changing hierarchy", () => Require(!data.enabled && (bool)Get(view!, "Paused")));
                var state = new CylinderHeadState { NetId = 1, ParentId = 2, Revision = 2 };
                Call(view!, "Apply", state, mount.transform);
                check(label + "host fitting moves the complete head to its mount", () => Require(head.transform.parent == mount.transform
                    && head.transform.localPosition == Vector3.zero && Quaternion.Angle(head.transform.localRotation, Quaternion.identity) < .001f));
                check(label + "child mount position follows the head", () => Require(Vector3.Distance(child.transform.position,
                    mount.transform.TransformPoint(new Vector3(.1f, .2f, .3f))) < .001f));
                check(label + "child trigger remains available", () => Require(nestedPick.enabled && nestedPick.isTrigger));
                check(label + "fitted head cannot become a dynamic pickup", () => Require(head.GetComponent<Rigidbody>().isKinematic
                    && !first.enabled && !second.enabled && head.tag == "Untagged"));
                check(label + "native saved fitting and condition stay untouched", () => Require(data.FsmVariables.FindFsmInt("AssemblyID").Value == (fitted ? 1 : 0)
                    && data.FsmVariables.FindFsmFloat("Wear").Value == 71 && data.FsmVariables.FindFsmFloat("Tightness").Value == 13));
                Call(view!, "Hold");
                check(label + "missing parent pauses presentation and collisions", () => Require(!(bool)Get(view!, "Ready")
                    && head.GetComponent<Rigidbody>().isKinematic && !head.GetComponent<Rigidbody>().detectCollisions));
                state.ParentId = 0; state.Revision = 3; state.Mass = 23; state.Position = new NetVector3(7, 8, 9);
                Call(view!, "Apply", state, null);
                check(label + "host removal restores loose physical motion", () => Require(head.transform.parent == null
                    && head.transform.position == new Vector3(7, 8, 9) && !head.GetComponent<Rigidbody>().isKinematic
                    && head.GetComponent<Rigidbody>().mass == 23 && first.enabled && second.enabled));
                Call(view!, "Restore");
                check(label + "disconnect restores exact original hierarchy and scale", () => Require(head.transform.parent == root.transform
                    && head.transform.localPosition == position && head.transform.localScale == scale
                    && Quaternion.Angle(head.transform.localRotation, rotation) < .001f));
                check(label + "disconnect restores original collision flags", () => Require(first.enabled == !fitted && second.isTrigger == fitted));
                check(label + "disconnect restores native Data without restarting it", () => Require(data.enabled && data.ActiveStateName == "Probe idle"));
                if (original != null)
                    check(label + "original body properties survive the session", () => Require(original.mass == 17 && original.isKinematic && !original.detectCollisions));
                else check(label + "temporary body is disabled before deferred removal", () => Require(head.GetComponent<Rigidbody>().isKinematic && !head.GetComponent<Rigidbody>().detectCollisions));
            }
            finally
            {
                if (view != null) Call(view, "Restore");
                if (head != null && head.transform.parent == null) UnityEngine.Object.DestroyImmediate(head);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
        private static object Get(object obj, string field) => obj.GetType().GetField(field, Members)?.GetValue(obj)
            ?? obj.GetType().GetProperty(field, Members).GetValue(obj, null);
        private static object Call(object obj, string method, params object?[] args) => obj.GetType().GetMethod(method, Members).Invoke(obj, args);
        private static void Require(bool value) { if (!value) throw new InvalidOperationException("Head attachment assertion failed."); }
    }
}
