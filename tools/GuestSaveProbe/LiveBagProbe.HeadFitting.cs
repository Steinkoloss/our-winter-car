using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool _pinHead;
        private static PlayMakerFSM ProbeHead()
        {
            PartIdentity.TryItemId("VIN1110", out uint id);
            return (PlayMakerFSM)((IDictionary)Get(Items, "_nativeParts"))[id];
        }
        private static void TickHeadFixture()
        {
            if (!_pinHead) return;
            var data = ProbeHead(); var body = data.GetComponent<Rigidbody>();
            if (body == null) { _pinHead = false; return; }
            var state = Get(Items, "_headReceived") as CylinderHeadState;
            if (state == null || state.ParentId != 0) { _pinHead = false; return; }
            var point = data.FsmVariables.FindFsmGameObject("InstallPoint").Value;
            body.isKinematic = true;
            body.transform.position = point.transform.position; body.position = point.transform.position; body.velocity = body.angularVelocity = Vector3.zero;
        }
        private static bool HeadFitCommand(string[] args, List<string> rows)
        {
            if (!args[1].StartsWith("headfit-", StringComparison.Ordinal)) return false;
            if (Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_HEAD_FIT_TEST") != "1") throw new InvalidOperationException("Head fixture not enabled.");
            var session = SessionManager.Instance!; var data = ProbeHead();
            var point = data.FsmVariables.FindFsmGameObject("InstallPoint").Value;
            PlayMakerFSM? mount = null;
            foreach (var fsm in point.GetComponents<PlayMakerFSM>()) if (fsm.FsmName == "Data") mount = fsm;
            if (mount == null) throw new InvalidOperationException("Head mount missing.");
            switch (args[1])
            {
                case "headfit-state": break;
                case "headfit-tools":
                    var catalog = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
                    var config = catalog.GetProperty("ReplacementParts", Members).GetValue(null, null);
                    string repairName = (string)config.GetType().GetProperty("Item").GetValue(config, new object[] { "replicaRepairVariable" });
                    FsmVariables.GlobalVariables.FindFsmBool(repairName).Value = args[2] == "1"; break;
                case "headfit-turn":
                    var controls = HeadFastenerBinding(session);
                    if (controls == null) throw new InvalidOperationException("Head fasteners unavailable.");
                    var bolt = ((Array)Get(controls, "Bolts")).GetValue(int.Parse(args[2]) - 1);
                    ((PlayMakerFSM)Get(bolt, "Fsm")).SendEvent(args[3] == "1" ? "TIGHTEN" : "UNTIGHTEN"); break;
                case "headfit-request-turn":
                    rows.Add("began|" + Call(Items, "RequestHeadFastener", byte.Parse(args[2]),
                        args[3] == "1" ? PartFitOperation.ToolTighten : PartFitOperation.ToolLoosen)); break;
                case "headfit-stale-turn":
                    Call(Items, "EnsurePartFitClient"); var old = (CylinderHeadState)Get(Items, "_headReceived");
                    ((PartFitClient)Get(Items, "_partFitClient")).TryBegin(session.LocalPlayerId, old.NetId, old.Revision - 1,
                        PartFitOperation.ToolTighten, byte.Parse(args[2])); break;
                case "headfit-near": Player!.position = point.transform.position + new Vector3(0, 0, 1); break;
                case "headfit-far": Player!.position = point.transform.position + new Vector3(0, 0, 10); break;
                case "headfit-pin":
                    if (session.IsHost) throw new InvalidOperationException("Guest alignment only.");
                    var pickup = Find(Hand, "PickUp");
                    pickup.FsmVariables.FindFsmGameObject("PickedObject").Value = data.gameObject;
                    Enter(pickup, "Set pivot 2"); _pinHead = true; break;
                case "headfit-request":
                    if (session.IsHost) throw new InvalidOperationException("Guest request only.");
                    var operation = args[2] == "install" ? PartFitOperation.Install : PartFitOperation.Remove;
                    if (operation == PartFitOperation.Install) Find(Hand, "PickUp").SendEvent("DROP_PART");
                    rows.Add("began|" + Call(Items, "RequestHeadFit", session, operation)); break;
                case "headfit-drop": _pinHead = false; if (data.GetComponent<Rigidbody>() != null && (Get(Items, "_headReceived") as CylinderHeadState)?.ParentId == 0) data.GetComponent<Rigidbody>().isKinematic = false; Find(Hand, "PickUp").SendEvent("DROP_PART"); break;
                case "headfit-tightness":
                    if (!session.IsHost) throw new InvalidOperationException("Host fixture only.");
                    data.FsmVariables.FindFsmFloat("Tightness").Value = float.Parse(args[2]); data.SendEvent("BOLTING"); break;
                case "headfit-block":
                    if (!session.IsHost) throw new InvalidOperationException("Host fixture only.");
                    var blocker = new GameObject("isolated head prerequisite");
                    var blockerFsm = blocker.AddComponent<PlayMakerFSM>(); blockerFsm.FsmName = "Data";
                    blockerFsm.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = args[2] == "1" } };
                    mount.FsmVariables.FindFsmGameObject("db_Installed1").Value = blocker; break;
                default: throw new InvalidOperationException("Unknown head fitting test command.");
            }
            var body = data.GetComponent<Rigidbody>();
            PartIdentity.TryItemId("VIN1110", out uint id);
            var stateNow = session.IsHost ? Call(Items, "BuildCylinderHeadState") as CylinderHeadState : Get(Items, "_headReceived") as CylinderHeadState;
            rows.Add("headpose|" + Vector(data.transform.position) + "|" + Vector(point.transform.position));
            rows.Add("headfit|" + data.ActiveStateName + "|" + data.FsmVariables.FindFsmInt("AssemblyID").Value
                + "|" + data.FsmVariables.FindFsmFloat("Tightness").Value + "|" + (body != null)
                + "|" + mount.ActiveStateName + "|" + mount.FsmVariables.FindFsmBool("Installed").Value
                + "|" + Vector3.Distance(data.transform.position, point.transform.position)
                + "|" + Get(Items, "_headFitFailed") + "|" + (stateNow == null ? "none" : stateNow.Revision + ":" + stateNow.ParentId));
            var item = ((IDictionary)Get(Items, "_items"))[id];
            if (item != null) rows.Add("headowner|" + Get(item, "LocallyOwned") + "|" + Get(item, "RemoteOwner") + "|" + Get(item, "LastRemoteSequenceOwner"));
            var client = Get(Items, "_partFitClient") as PartFitClient; rows.Add("headpending|" + (client != null && client.Pending));
            foreach (DictionaryEntry pair in (IDictionary)Get(Get(Items, "_partFitLedger"), "_entries"))
                rows.Add("headreceipt|" + pair.Key + "|" + Get(pair.Value, "Status"));
            if (stateNow != null) rows.Add("headfasteners|" + stateNow.FastenersAvailable + "|" + stateNow.Tightness
                + "|" + string.Join(",", Array.ConvertAll(stateNow.Fasteners, x => x.ToString())));
            var bindingNow = HeadFastenerBinding(session);
            if (bindingNow != null)
                foreach (var b in (Array)Get(bindingNow, "Bolts"))
                {
                    var fsm = (PlayMakerFSM)Get(b, "Fsm"); var visual = (Transform)Get(b, "ReplicaVisual");
                    var values = (IList)((System.Reflection.PropertyInfo)Get(b, "ArrayProperty")).GetValue(Get(b, "ArrayProxy"), null);
                    int index = ((FsmInt)Get(b, "IndexVar")).Value;
                    rows.Add("headbolt|" + index + "|" + values[index] + "|" + ((FsmInt)Get(b, "BoltTightnessVar")).Value
                        + "|" + visual.localPosition.z + "|" + ((SphereCollider)Get(b, "ReplicaCollider")).enabled + "|" + fsm.enabled + "|" + fsm.ActiveStateName);
                }
            return true;
        }
        private static object? HeadFastenerBinding(SessionManager session) => session.IsHost ? Get(Items, "_headFasteners")
            : Get(Items, "_headView") == null ? null : Get(Get(Items, "_headView"), "_fasteners");
    }
}
