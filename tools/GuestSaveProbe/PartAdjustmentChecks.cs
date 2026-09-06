using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    // The fixture imports the installed assets' action definitions. It does not
    // substitute a second implementation of the game's rotation arithmetic.
    internal static class PartAdjustmentChecks
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        internal static void Run(Action<string, Action> check)
        {
            string path = Path.Combine(Application.dataPath, "../part-adjustment-probe.json");
            var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var reader = Activator.CreateInstance(readerType, BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { File.ReadAllText(path) }, null);
            var input = (Dictionary<string, object>)readerType.GetMethod("ReadObject", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(reader, null);
            int families = 0;
            foreach (var value in (List<object>)input["fsms"])
            {
                var row = (Dictionary<string, object>)value;
                if ((string)row["fsmName"] != "HandRotate"
                    || ((string)row["path"] != "VIN133/Pivot" && (string)row["path"] != "ALTERNATOR0/Pivot")) continue;
                RunFamily(row, check); families++;
            }
            Require(families == 2);
        }

        private static void RunFamily(Dictionary<string, object> source, Action<string, Action> check)
        {
            string prefix = ((string)source["path"]).Split('/')[0];
            var mount = new GameObject("probe mount"); mount.SetActive(false);
            var part = new GameObject(prefix); part.transform.SetParent(mount.transform, false);
            var pivot = new GameObject("Pivot"); pivot.transform.SetParent(part.transform, false);
            var partValue = new FsmFloat { Name = "SettingRotation", UseVariable = true, Value = 3 };
            var mountValue = new FsmFloat { Name = "SettingRotation", UseVariable = true, Value = 3 };
            var data = MakeFsm(part, "Data", new[] { partValue });
            var mountData = MakeFsm(mount, "Data", new[] { mountValue });
            var hand = MakeFsm(pivot, (string)source["fsmName"], new[] {
                new FsmFloat { Name = "Rotation", UseVariable = true, Value = 3 }, new FsmFloat { Name = "Scroll", UseVariable = true } });
            hand.FsmVariables.GameObjectVariables = new[] { new FsmGameObject { Name = "ThisPart", UseVariable = true, Value = part },
                new FsmGameObject { Name = "VINP", UseVariable = true, Value = mount } };
            var states = new List<FsmState> { new FsmState(hand.Fsm) { Name = "Probe idle", Actions = new FsmStateAction[0] } };
            foreach (var entry in (List<object>)source["states"])
            {
                var row = (Dictionary<string, object>)entry;
                var transitions = new List<FsmTransition>();
                foreach (var transition in (List<object>)row["transitions"])
                {
                    var t = (Dictionary<string, object>)transition;
                    transitions.Add(new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent((string)t["event"]), ToState = (string)t["to"] });
                }
                states.Add(new FsmState(hand.Fsm) { Name = (string)row["name"], Actions = new FsmStateAction[0], Transitions = transitions.ToArray() });
            }
            hand.Fsm.States = states.ToArray(); hand.Fsm.StartState = "Probe idle";
            try
            {
                mount.SetActive(true);
                foreach (var entry in (List<object>)source["states"])
                {
                    var row = (Dictionary<string, object>)entry;
                    var state = states.Find(s => s.Name == (string)row["name"]);
                    var actions = new List<FsmStateAction>();
                    foreach (var action in (List<object>)row["actions"]) actions.Add(ReadAction((Dictionary<string, object>)action, hand));
                    state.Actions = actions.ToArray();
                    foreach (var action in actions) action.Init(state);
                }
                data.enabled = true; mountData.enabled = true; hand.enabled = true;
                if (!hand.Fsm.Started) hand.Fsm.Start();
                var hook = Core.GetType("WinterMP.Core.Sync.FsmHook", true);
                foreach (string state in new[] { "Clockwise", "Counterwise", "Probe idle" })
                    Require((bool)hook.GetMethod("EnsureRemoteEntry").Invoke(null, new object[] { hand, state }));
                var validate = Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true).GetMethod("ValidatePartRotationGraph", Hidden);
                Action verify = () => validate.Invoke(null, new object[] { hand, data, "SettingRotation", "InstallPoint" });
                Action<string> fire = state => hook.GetMethod("FireRemoteEntry").Invoke(null, new object[] { hand, state });
                Action<float> seed = angle => { partValue.Value = mountValue.Value = angle; pivot.transform.localRotation = Quaternion.Euler(0, angle, 0); fire("Probe idle"); };
                Action<float> expected = angle => Require(Math.Abs(partValue.Value - angle) < .001f && Math.Abs(mountValue.Value - angle) < .001f
                    && Quaternion.Angle(pivot.transform.localRotation, Quaternion.Euler(0, angle, 0)) < .01f);
                check(prefix + ": native hand rotation bindings", verify);
                check(prefix + ": clockwise writes part, mount and pivot", () => { seed(3); fire("Clockwise"); expected(3.5f); });
                check(prefix + ": counterwise reaches zero without wrapping", () => { seed(.2f); fire("Counterwise"); expected(0); });
                check(prefix + ": native upper clamp and repeated turn", () => { seed(6.8f); fire("Clockwise"); expected(7); fire("Clockwise"); expected(7); });
                check(prefix + ": rotation preserves assembly hierarchy", () => Require(part.transform.parent == mount.transform
                    && pivot.transform.parent == part.transform && part.transform.localPosition == Vector3.zero && part.transform.localScale == Vector3.one));
                var clockwise = states.Find(s => s.Name == "Clockwise");
                var stepField = clockwise.Actions[2].GetType().GetField("add");
                check(prefix + ": changed native step is refused", () => RejectChanged(verify, stepField, clockwise.Actions[2], new FsmFloat(1)));
                var wait = states.Find(s => s.Name == "Wait");
                check(prefix + ": changed mount scalar is refused", () => RejectChanged(verify, wait.Actions[1].GetType().GetField("variableName"),
                    wait.Actions[1], new FsmString("OtherSetting")));
                check(prefix + ": world-space rotation is refused", () => {
                    var action = clockwise.Actions[4]; var field = action.GetType().GetField("space");
                    RejectChanged(verify, field, action, Enum.ToObject(field.FieldType, 0));
                });
                check(prefix + ": deferred pivot mutation is refused", () => {
                    var action = clockwise.Actions[4];
                    RejectChanged(verify, action.GetType().GetField("lateUpdate"), action, true);
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(mount); }
        }

        private static PlayMakerFSM MakeFsm(GameObject owner, string name, FsmFloat[] floats)
        {
            var fsm = owner.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
            typeof(PlayMakerFSM).GetField("fsm", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(fsm, new Fsm());
            fsm.Fsm.Name = name; fsm.FsmVariables.FloatVariables = floats;
            fsm.Fsm.States = new[] { new FsmState(fsm.Fsm) { Name = "Idle", Actions = new FsmStateAction[0] } };
            fsm.Fsm.StartState = "Idle";
            return fsm;
        }

        private static FsmStateAction ReadAction(Dictionary<string, object> row, PlayMakerFSM fsm)
        {
            Type? type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if ((type = assembly.GetType((string)row["type"])) != null) break;
            var action = (FsmStateAction)Activator.CreateInstance(type ?? throw new InvalidOperationException("Missing native action."));
            action.Reset(); action.Enabled = (bool)row["enabled"];
            var parameters = (List<object>)row["parameters"];
            for (int i = 0; i < parameters.Count; i++)
            {
                var parameter = (Dictionary<string, object>)parameters[i];
                string fieldName = (string)parameter["field"];
                if (fieldName.Length == 0) continue;
                var field = action.GetType().GetField(fieldName);
                if ((string)parameter["type"] == "Array" && fieldName == "layerMask")
                {
                    var mask = new List<FsmInt>();
                    while (i + 1 < parameters.Count && (string)((Dictionary<string, object>)parameters[i + 1])["field"] == string.Empty)
                        mask.Add((FsmInt)ReadParameter((Dictionary<string, object>)parameters[++i], typeof(FsmInt), fsm)!);
                    field.SetValue(action, mask.ToArray()); continue;
                }
                field.SetValue(action, ReadParameter(parameter, field.FieldType, fsm));
            }
            return action;
        }

        private static object? ReadParameter(Dictionary<string, object> p, Type fieldType, PlayMakerFSM fsm)
        {
            string kind = (string)p["type"];
            if (kind == "FsmFloat")
            {
                string name = (string)p["name"];
                return name.Length != 0 && fsm.FsmVariables.FindFsmFloat(name) != null ? fsm.FsmVariables.FindFsmFloat(name)
                    : new FsmFloat { Name = name, UseVariable = (bool)p["useVariable"], Value = Convert.ToSingle(p["value"]) };
            }
            if (kind == "FsmInt") return new FsmInt { Name = (string)p["name"], UseVariable = (bool)p["useVariable"], Value = Convert.ToInt32(p["value"]) };
            if (kind == "FsmBool") return new FsmBool { Name = (string)p["name"], UseVariable = (bool)p["useVariable"], Value = Convert.ToBoolean(p["value"]) };
            if (kind == "FsmString")
            {
                var value = (Dictionary<string, object>)p["value"];
                return new FsmString { Name = (string)value["name"], UseVariable = Convert.ToBoolean(value["useVariable"]), Value = (string)value["value"] };
            }
            if (kind == "FsmOwnerDefault")
            {
                var value = (Dictionary<string, object>)p["value"];
                var target = (Dictionary<string, object>)value["gameObject"];
                return new FsmOwnerDefault { OwnerOption = (OwnerDefaultOption)Convert.ToInt32(value["ownerOption"]),
                    GameObject = fsm.FsmVariables.FindFsmGameObject((string)target["name"]) ?? new FsmGameObject() };
            }
            if (kind == "FsmGameObject") return fsm.FsmVariables.FindFsmGameObject((string)((Dictionary<string, object>)p["value"])["name"]);
            if (kind == "FsmEvent") return FsmEvent.GetFsmEvent((string)p["value"]);
            if (kind == "FsmQuaternion" || kind == "FsmVector3")
            {
                string hex = (string)p["rawHex"]; var raw = new byte[hex.Length / 2];
                for (int i = 0; i < raw.Length; i++) raw[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                int count = kind == "FsmQuaternion" ? 16 : 12;
                string name = System.Text.Encoding.UTF8.GetString(raw, count + 1, raw.Length - count - 1);
                if (kind == "FsmQuaternion") return new FsmQuaternion { Name = name, UseVariable = raw[count] != 0,
                    Value = new Quaternion(BitConverter.ToSingle(raw, 0), BitConverter.ToSingle(raw, 4), BitConverter.ToSingle(raw, 8), BitConverter.ToSingle(raw, 12)) };
                return new FsmVector3 { Name = name, UseVariable = raw[count] != 0,
                    Value = new Vector3(BitConverter.ToSingle(raw, 0), BitConverter.ToSingle(raw, 4), BitConverter.ToSingle(raw, 8)) };
            }
            if (fieldType.IsEnum) return Enum.ToObject(fieldType, Convert.ToInt32(p["value"]));
            return Convert.ChangeType(p["value"], fieldType);
        }

        private static void RejectChanged(Action verify, FieldInfo field, object target, object replacement)
        {
            verify();
            object original = field.GetValue(target);
            try
            {
                field.SetValue(target, replacement);
                try { verify(); } catch (TargetInvocationException e) { if (e.InnerException is InvalidOperationException) return; throw; }
                throw new InvalidOperationException("Changed native binding was accepted.");
            }
            finally { field.SetValue(target, original); }
        }
        private static void Require(bool success)
        {
            if (!success) throw new InvalidOperationException("Native hand adjustment assertion failed.");
        }
    }
}
