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

namespace WinterMP.GuestSaveProbe
{
    internal static class ValveTurnChecks
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        private static readonly Type Fs = Core.GetType("WinterMP.Core.Sync.FsmWorldSync", true);
        internal static void Run(Action<string, Action> check)
        {
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true); catalog.GetMethod("EnsureLoaded", Members).Invoke(null, null);
            var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var reader = Activator.CreateInstance(readerType, Members, null, new object[] {
                File.ReadAllText(Path.Combine(Application.dataPath, "../native-valve-head.json")) }, null);
            var json = (Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null);
            var rows = (List<object>)json["fsms"];
            var dataRow = NativeBagPartChecks.Find(rows, "CARPARTS/StartParts/VIN1110", "Data");
            var controls = new List<Dictionary<string, object>>();
            foreach (Dictionary<string, object> row in rows)
                if (((string)row["path"]).Contains("ValveAdjustment/Masked/") && (string)row["fsmName"] == "Screw") controls.Add(row);
            Require(controls.Count == 8, "Expected eight installed-game control definitions.");
            var savedSession = SessionManager.Instance;
            var worldType = Core.GetType("WinterMP.Core.Sync.WorldSyncManager", true);
            var savedWorld = worldType.GetProperty("Instance", Members).GetValue(null, null);
            var root = new GameObject("valve turn fixture"); root.SetActive(false);
            var session = root.AddComponent<SessionManager>(); session.enabled = false;
            var world = root.AddComponent(worldType); ((Behaviour)world).enabled = false;
            var bridge = New("WorldSyncBridge", world, new Dictionary<PlayMakerFSM, bool>());
            var items = New("ItemWorldSync", bridge); var vehicles = New("VehicleWorldSync", bridge, items); var sync = New("FsmWorldSync", bridge, vehicles);
            Call(bridge, "BindItems", items); Call(bridge, "BindFsms", sync); Call(items, "BindVehicles", vehicles);
            var head = Child(root, "head"); var data = NativeBagPartChecks.MakeFsm(head, dataRow);
            var vars = data.FsmVariables; vars.FindFsmString("ID").Value = "VIN1117";
            vars.FindFsmString("UTAssemblyID").Value = "VIN1117AID"; vars.FindFsmString("UTPos").Value = "VIN1117POS";
            vars.FindFsmInt("AssemblyID").Value = 1; vars.FindFsmFloat("Tightness").Value = 13; vars.FindFsmFloat("Wear").Value = 71;
            Type? arrayType = null; foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == "Assembly-CSharp") arrayType = assembly.GetType("PlayMakerArrayListProxy");
            var proxy = head.AddComponent(arrayType!); Set(proxy, "referenceName", "Valves");
            IList values = new ArrayList();
            values.Clear(); for (int i = 0; i < 8; i++) values.Add(4.137f + i * .1f);
            var masked = Child(Child(head, "ValveAdjustment"), "Masked"); var fsms = new PlayMakerFSM[8];
            for (int i = 0; i < 8; i++)
            {
                var obj = Child(masked, "BoltPM"); obj.layer = 12;
                var visual = Child(obj, "Bolt" + i); visual.transform.localScale = Vector3.one * .007f;
                var pick = obj.AddComponent<SphereCollider>(); pick.isTrigger = true;
                var fsm = NativeBagPartChecks.MakeFsm(obj, controls[i]); fsms[i] = fsm;
                fsm.FsmVariables.FindFsmGameObject("ThisPart").Value = head;
                fsm.FsmVariables.FindFsmGameObject("ThisBolt").Value = visual;
                fsm.FsmVariables.FindFsmInt("Index").Value = i;
                fsm.FsmVariables.FindFsmFloat("AdjustmentF").Value = (float)values[i];
                fsm.FsmVariables.ObjectVariables = new[] { new FsmObject { Name = "Collider", UseVariable = true, Value = pick, ObjectType = typeof(SphereCollider) } };
            }
            var peer = new RemotePlayer { Peer = new PeerId(1001), PlayerId = 1, Position = Vector3.zero, LastTransformTime = Time.unscaledTime };
            ((IDictionary)Get(session, "_playersByPeer")).Add(peer.Peer, peer);
            var capture = Activator.CreateInstance(typeof(VehicleStateChecks).GetNestedType("CaptureTransport", BindingFlags.NonPublic), true);
            Set(session, "_transport", capture);
            try
            {
                root.SetActive(true); NativeBagPartChecks.Start(data);
                var liveValues = (IList)arrayType!.GetProperty("arrayList").GetValue(proxy, null);
                liveValues.Clear(); foreach (var value in values) liveValues.Add(value); values = liveValues;
                Property(typeof(SessionManager), null, "Instance", session); Property(worldType, null, "Instance", world);
                Property(typeof(SessionManager), session, "IsHost", true); Property(typeof(SessionManager), session, "State", SessionState.Hosting);
                for (int i = 0; i < 8; i++)
                {
                    NativeBagPartChecks.LoadActions(fsms[i], controls[i], "Init", "Tight?", "Loose?", "Calc pos", "Set pos", "On", "Off");
                    NativeBagPartChecks.Start(fsms[i]); fsms[i].Fsm.StartState = "Init";
                }
                Action validate = () => Static("ValidateValveGraph", fsms[0]);
                check("valve: all eight native graphs validate", () => { foreach (var fsm in fsms) Static("ValidateValveGraph", fsm); });
                var calc = NativeBagPartChecks.State(fsms[0], "Calc pos").Actions;
                Changed(check, validate, calc[0], "minValue", new FsmFloat(1), "changed lower limit");
                Changed(check, validate, calc[0], "maxValue", new FsmFloat(9), "changed upper limit");
                Changed(check, validate, calc[1], "reference", new FsmString { Value = "Bolts" }, "wrong array");
                Changed(check, validate, calc[1], "everyFrame", true, "repeated array write");
                Changed(check, validate, calc[2], "float2", new FsmFloat(100), "wrong rotation scale");
                Changed(check, validate, NativeBagPartChecks.State(fsms[0], "Tight?").Actions[0], "perSecond", true, "time-based turn");
                Changed(check, validate, NativeBagPartChecks.State(fsms[0], "Loose?").Actions[0], "subtract", new FsmFloat(.1f), "wrong turn variable");
                Changed(check, validate, NativeBagPartChecks.State(fsms[0], "Set pos").Actions[0], "everyFrame", true, "recurring visual");
                check("valve: native controls bind separately from fastening bolts", () =>
                {
                    foreach (var fsm in fsms) Require((bool)Call(sync, "RegisterBolt", fsm)!, "Valve registration refused.");
                    Require(((IDictionary)Get(sync, "_valves")).Count == 8 && ((IDictionary)Get(sync, "_bolts")).Count == 0, "Misclassified valve.");
                });
                var registered = (IDictionary)Get(sync, "_valves"); var ids = new uint[8];
                foreach (DictionaryEntry pair in registered) ids[(int)Get(pair.Value, "Slot")] = (uint)pair.Key;
                for (int i = 0; i < 8; i++)
                {
                    int slot = i;
                    check("valve: guest native turn and host return on slot " + slot, () =>
                    {
                        float before = (float)values[slot]; peer.LastTransformTime = Time.unscaledTime;
                        fsms[slot].FsmVariables.FindFsmFloat("AdjustmentF").Value = 7;
                        Require((bool)Call(sync, "TryAcceptGuestRawEvent", new FsmRawEvent { NetId = ids[slot], EventName = "TIGHTEN" }, (byte)1)!, "Nearby turn refused.");
                        Require(Math.Abs((float)values[slot] - before - .05f) < .00001f, "Native step used stale scratch.");
                        var state = (ValveAdjustmentState)Call(sync, "BuildValveState", ids[slot])!;
                        Require(state.Setting == (float)values[slot], "Reply did not reflect native array.");
                        fsms[slot].SendEvent("UNTIGHTEN"); Require(Math.Abs((float)values[slot] - before) < .00001f, "Host native turn did not restore setting.");
                        Require(vars.FindFsmFloat("Tightness").Value == 13 && vars.FindFsmFloat("Wear").Value == 71, "Valve turn changed fastening or wear.");
                    });
                }
                check("valve: native partial steps clamp at both limits", () =>
                {
                    values[0] = 2.01f; fsms[0].SendEvent("UNTIGHTEN"); Require((float)values[0] == 2, "Lower clamp changed.");
                    values[0] = 7.99f; fsms[0].SendEvent("TIGHTEN"); Require((float)values[0] == 8, "Upper clamp changed.");
                });
                foreach (string fault in new[] { "far", "stale", "unfitted", "inactive", "unknown event", "unknown actor" })
                    check("valve: " + fault + " refuses guest mutation", () =>
                    {
                        values[0] = 4f; peer.Position = fault == "far" ? new Vector3(100, 0, 0) : Vector3.zero;
                        peer.LastTransformTime = fault == "stale" ? Time.unscaledTime - 3 : Time.unscaledTime;
                        vars.FindFsmInt("AssemblyID").Value = fault == "unfitted" ? 0 : 1;
                        fsms[0].enabled = fault != "inactive";
                        Require(!(bool)Call(sync, "TryAcceptGuestRawEvent", new FsmRawEvent { NetId = ids[0], EventName = fault == "unknown event" ? "SAVEGAME" : "TIGHTEN" },
                            fault == "unknown actor" ? (byte)99 : (byte)1)!, "Unsafe turn accepted.");
                        Require((float)values[0] == 4, "Refused turn mutated array.");
                        vars.FindFsmInt("AssemblyID").Value = 1; fsms[0].enabled = true;
                        // Re-enabling starts Init's NextFrameEvent. Settle the
                        // fixture explicitly before the next same-frame check.
                        fsms[0].SendEvent("MP_SET_POS");
                    });
                check("valve: snapshot includes every setting", () =>
                { int count = 0; foreach (var _ in (IEnumerable)Call(sync, "BuildValveStates")!) count++; Require(count == 8, "Incomplete snapshot."); });
                check("valve: host ignores a forged absolute setting", () =>
                { values[0] = 4f; Call(sync, "OnRemoteValveState", new ValveAdjustmentState { NetId = ids[0], Setting = 7 }); Require((float)values[0] == 4, "Guest changed host setting."); });
                check("valve: reconnect removes hooks and registers each native graph once", () =>
                { Call(sync, "Clear"); foreach (var fsm in fsms) Require((bool)Call(sync, "RegisterBolt", fsm)!, "Rebind refused."); Require(((IDictionary)Get(sync, "_valves")).Count == 8, "Rebind count changed."); });
                Call(sync, "Clear");
                Property(typeof(SessionManager), session, "IsHost", false); Property(typeof(SessionManager), session, "State", SessionState.Connected);
                Set(session, "_hostPeer", new PeerId(999));
                var savedValues = new float[8]; var savedScratch = new float[8]; var savedPoses = new Quaternion[8];
                for (int i = 0; i < 8; i++) { savedValues[i] = (float)values[i]; savedScratch[i] = fsms[i].FsmVariables.FindFsmFloat("AdjustmentF").Value;
                    savedPoses[i] = fsms[i].transform.GetChild(0).localRotation; }
                check("valve: guest graph binds without rewriting saved arrays", () =>
                { foreach (var fsm in fsms) Require((bool)Call(sync, "RegisterBolt", fsm)!, "Guest registration failed.");
                    for (int i = 0; i < 8; i++) Require((float)values[i] == savedValues[i], "Guest initialization wrote its save array."); });
                check("valve: guest cannot turn before a host seed", () =>
                {
                    var packets = (IList)Get(capture, "Packets"); packets.Clear(); fsms[0].SendEvent("TIGHTEN");
                    Require(packets.Count == 0 && (float)values[0] == savedValues[0], "Unseeded guest sent or predicted a turn.");
                });
                for (int i = 0; i < 8; i++)
                {
                    int slot = i;
                    check("valve: host correction updates only guest display on slot " + slot, () =>
                    {
                        var state = new ValveAdjustmentState { NetId = ids[slot], Setting = 3.137f + slot * .1f };
                        Call(sync, "OnRemoteValveState", state); Call(sync, "OnRemoteValveState", state);
                        Require(fsms[slot].FsmVariables.FindFsmFloat("AdjustmentF").Value == state.Setting, "Host correction missing.");
                        Require(Math.Abs(Mathf.DeltaAngle(fsms[slot].transform.GetChild(0).localEulerAngles.z, state.Setting * 50)) < .001f, "Wrong valve visual.");
                        for (int j = 0; j < 8; j++) Require((float)values[j] == savedValues[j], "Guest correction wrote a saved array.");
                    });
                }
                check("valve: guest tool emits one intent with no local prediction", () =>
                {
                    var c = catalog.GetProperty("ReplacementParts", Members).GetValue(null, null);
                    string name = (string)c.GetType().GetProperty("Item").GetValue(c, new object[] { "replicaRepairVariable" });
                    var repair = FsmVariables.GlobalVariables.FindFsmBool(name); bool previous = repair.Value;
                    try
                    {
                        repair.Value = true; fsms[0].SendEvent("REPAIRMODE_ON"); var packets = (IList)Get(capture, "Packets"); packets.Clear();
                        float before = fsms[0].FsmVariables.FindFsmFloat("AdjustmentF").Value;
                        fsms[0].SendEvent("TIGHTEN"); fsms[0].SendEvent("TIGHTEN");
                        Require(packets.Count == 1 && Get(packets[0], "Message") is FsmRawEvent,
                            "Guest native tool did not emit exactly one rate-limited intent.");
                        Require(fsms[0].FsmVariables.FindFsmFloat("AdjustmentF").Value == before && (float)values[0] == savedValues[0], "Guest predicted into its saved tuning.");
                    }
                    finally { repair.Value = previous; }
                });
                check("valve: disconnect restores native controls, scratch and exact poses", () =>
                {
                    Call(sync, "Clear");
                    for (int i = 0; i < 8; i++) Require((float)values[i] == savedValues[i]
                        && fsms[i].FsmVariables.FindFsmFloat("AdjustmentF").Value == savedScratch[i]
                        && fsms[i].transform.GetChild(0).localRotation.Equals(savedPoses[i])
                        && NativeBagPartChecks.State(fsms[i], "Calc pos").Actions[1].GetType().Name == "ArrayListSet", "Saved valve state did not restore.");
                });
            }
            finally
            {
                Call(sync, "Clear"); Set(session, "_transport", null);
                UnityEngine.Object.DestroyImmediate(root);
                Property(typeof(SessionManager), null, "Instance", savedSession); Property(worldType, null, "Instance", savedWorld);
            }
        }
        private static void Changed(Action<string, Action> check, Action validate, object action, string field, object value, string label)
        {
            check("valve: " + label + " is rejected", () =>
            {
                validate(); object original = Get(action, field); Set(action, field, value);
                try { try { validate(); } catch (TargetInvocationException e) { if (e.InnerException is InvalidOperationException) return; throw; }
                    throw new InvalidOperationException("Changed graph accepted."); }
                finally { Set(action, field, original); }
            });
        }
        private static GameObject Child(GameObject parent, string name) { var obj = new GameObject(name); obj.transform.SetParent(parent.transform, false); return obj; }
        private static object New(string name, params object[] args) => Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync." + name, true), Members, null, args, null);
        private static object Get(object obj, string name) => obj.GetType().GetField(name, Members).GetValue(obj);
        private static void Set(object obj, string name, object? value) => obj.GetType().GetField(name, Members).SetValue(obj, value);
        private static void Property(Type type, object? obj, string name, object? value) => type.GetProperty(name, Members).SetValue(obj, value, null);
        private static object? Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Members).Invoke(obj, args);
        private static object? Static(string name, params object[] args) => Fs.GetMethod(name, Members).Invoke(null, args);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
