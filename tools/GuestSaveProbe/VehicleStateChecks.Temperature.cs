using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private static void RunTemperatureChecks(Action<string, Action> check, object item, object vehicles,
            SessionManager session, CaptureTransport capture, Action reset, Action<bool, byte> mode, Action<float, bool> sample)
        {
            var originalBody = Get(item, "Body"); var originalPath = Get(item, "Path");
            var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var reader = Activator.CreateInstance(readerType, Members, null, new object[] {
                File.ReadAllText(Path.Combine(Application.dataPath, "../vehicle-temperature-probe.json")) }, null);
            var rows = (List<object>)((Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null))["fsms"];
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
            catalog.GetMethod("EnsureLoaded", Static).Invoke(null, null);
            var profile = catalog.GetProperty("VehicleTemperature", Static).GetValue(null, null);
            using (GuestEngineInputChecks.SuspendProjectionForFixture())
            try
            {
                foreach (object rule in (IEnumerable)Get(profile, "Sources"))
                using (var f = new TemperatureFixture(rows, rule))
                {
                    reset(); Set(item, "Body", f.Body); Set(item, "Path", Get(rule, "RootPath"));
                    Set(item, "NativeTemperature", null); Set(item, "RequiresNativeTemperature", false); f.Item = item;
                    string prefix = "vehicle temperature " + f.Body.name + ": ";
                    check(prefix + "native source replaces stale dashboard scratch", () =>
                    {
                        f.Celsius.Value = 87; f.Output.Value = -999; f.Rebind();
                        Require(f.Read() == 87 && f.Output.Value == -999, "Capture used or rewrote dashboard scratch.");
                    });
                    foreach (float temp in new[] { -25f, 0f, 37f, 60f, 87f, 120f, 140f })
                    {
                        float value = temp;
                        check(prefix + "native dashboard mapping at " + value + " C", () =>
                        {
                            f.Celsius.Value = value;
                            byte wire = (byte)CallStatic("ReadCoolantTempByte", item)!;
                            Require(wire == (byte)Mathf.RoundToInt(Mathf.Clamp(value, 0, 120) / 120f * 255), "Source was clamped by dashboard before encoding.");
                            float decoded = wire / 255f * 120;
                            f.Celsius.Value = decoded; NativeBagPartChecks.Fire(f.Gauge, "Speed");
                            float native = f.Output.Value;
                            f.Celsius.Value = value; f.Output.Value = -999;
                            CallStatic("ApplyTemperatureGauge", item, decoded);
                            Require(Mathf.Abs(f.Output.Value - native) < 0.001f && f.Celsius.Value == value,
                                "Observer units differ from native gauge or wrote simulated temperature.");
                        });
                    }
                    check(prefix + "inactive stopped cooling retains its finite temperature", () =>
                    {
                        f.Celsius.Value = 72; f.Source.enabled = false; f.Source.gameObject.SetActive(false);
                        Require(f.Read() == 72, "Stopped cooling lost stored heat.");
                        f.Source.gameObject.SetActive(true); f.Source.enabled = true;
                    });
                    check(prefix + "uninitialized gauge defers without deserializing actions and later recovers", () =>
                    {
                        var original = f.Gauge.Fsm.States;
                        var pending = new FsmState((Fsm)null!) { Name = "Speed" };
                        try
                        {
                            f.Gauge.Fsm.States = new[] { pending };
                            for (int i = 0; i < 20; i++) f.Rebind();
                            Require(Get(item, "NativeTemperature") == null && !pending.ActionsLoaded,
                                "Pending gauge actions were loaded or accepted.");
                        }
                        finally { f.Gauge.Fsm.States = original; f.Rebind(); }
                        Require(f.Read() == 72, "Restored initialized gauge did not recover.");
                    });
                    check(prefix + "nonfinite temperatures do not reach the wire", () =>
                    {
                        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                        { f.Celsius.Value = invalid; Require((byte)CallStatic("ReadCoolantTempByte", item)! == 0, "Invalid temperature escaped."); }
                        f.Celsius.Value = float.MaxValue; Require((byte)CallStatic("ReadCoolantTempByte", item)! == 255, "Finite hot temperature overflowed quantization.");
                        f.Celsius.Value = -float.MaxValue; Require((byte)CallStatic("ReadCoolantTempByte", item)! == 0, "Finite cold temperature overflowed quantization.");
                        f.Celsius.Value = 87;
                    });
                    var read = f.State.Actions[0]; var scale = f.State.Actions[1]; var clamp = f.State.Actions[2];
                    foreach (string field in new[] { "fsmName", "variableName", "storeValue", "everyFrame" })
                    {
                        string name = field;
                        check(prefix + "changed native reader " + name + " pauses presentation and recovers", () =>
                        {
                            object before = Get(read, name);
                            object replacement = name == "everyFrame" ? (object)false : name == "storeValue" ? new FsmFloat(0) : (object)new FsmString("changed");
                            try { Set(read, name, replacement); f.Reject(); }
                            finally { Set(read, name, before); }
                            f.Rebind(); Require(f.Read() == 87, "Repaired reader did not recover.");
                        });
                    }
                    check(prefix + "foreign source cannot redirect capture or writes", () =>
                    {
                        var target = (FsmOwnerDefault)Get(read, "gameObject"); var before = target.GameObject.Value;
                        try { target.GameObject.Value = f.Gauge.gameObject; f.Reject(); }
                        finally { target.GameObject.Value = before; }
                        f.Rebind(); Require(f.Read() == 87, "Restored source did not recover.");
                    });
                    check(prefix + "unsupported arithmetic and dynamic clamps are rejected", () =>
                    {
                        bool enabled = scale.Enabled; object divisor = Get(scale, "float2"); object operation = Get(scale, "operation");
                        object minimum = Get(clamp, "minValue");
                        try
                        {
                            scale.Enabled = true; Set(scale, "float2", new FsmFloat(0)); f.Reject();
                            Set(scale, "float2", divisor); Set(scale, "operation", Enum.ToObject(operation.GetType(), 0)); f.Reject();
                            Set(scale, "operation", operation); Set(clamp, "minValue", new FsmFloat { Name = "dynamic", UseVariable = true, Value = 0 }); f.Reject();
                        }
                        finally { scale.Enabled = enabled; Set(scale, "float2", divisor); Set(scale, "operation", operation); Set(clamp, "minValue", minimum); }
                        f.Rebind(); Require(f.Read() == 87, "Restored mapping did not recover.");
                    });
                    check(prefix + "moved and duplicate sources cannot retain a stale binding", () =>
                    {
                        string name = f.Source.gameObject.name;
                        try { f.Source.gameObject.name = name + " moved"; f.Reject(); }
                        finally { f.Source.gameObject.name = name; }
                        f.Rebind();
                        var duplicate = f.Source.gameObject.AddComponent<PlayMakerFSM>(); duplicate.enabled = false; duplicate.FsmName = f.Source.FsmName;
                        try { f.Reject(); } finally { UnityEngine.Object.DestroyImmediate(duplicate); }
                        f.Rebind(); Require(f.Read() == 87, "Restored unique source did not recover.");
                    });
                    check(prefix + "reinitialized source variables rebind", () =>
                    {
                        var original = f.Source.FsmVariables.FloatVariables;
                        var next = (FsmFloat[])original.Clone(); var replacement = new FsmFloat { Name = f.Celsius.Name, UseVariable = true, Value = 93 };
                        for (int i = 0; i < next.Length; i++) if (ReferenceEquals(next[i], f.Celsius)) next[i] = replacement;
                        f.Source.FsmVariables.FloatVariables = next;
                        try { f.Rebind(); Require(f.Read() == 93, "Reinitialized source retained an old variable."); }
                        finally { f.Source.FsmVariables.FloatVariables = original; }
                        f.Rebind();
                    });

                    if ((bool)Get(rule, "HostAuthoritative"))
                        RunHostCoolantChecks(check, f, item, vehicles, session, capture, reset, mode);

                    if (((string)Get(rule, "RootPath")).StartsWith("SORBET", StringComparison.Ordinal))
                    {
                        check(prefix + "missing simulator cannot revive stale FuelLine RPM", () =>
                        {
                            var scratch = (FsmFloat)Get(item, "EngineRevsVar"); float saved = scratch.Value;
                            try
                            {
                                scratch.Value = 650;
                                Require((float)CallStatic("ReadBestRpm", item)! == 0 && scratch.Value == 650,
                                    "Inactive engine scratch was accepted as the simulator or rewritten.");
                            }
                            finally { scratch.Value = saved; }
                        });
                        check(prefix + "handoff captures signed native heat without writing its producer", () =>
                        {
                            foreach (float value in new[] { -17.25f, 0f, 34.875f })
                            {
                                f.Celsius.Value = value; f.Output.Value = 999;
                                var state = new VehicleState(); CallStatic("CaptureHandoffTemperature", item, state);
                                Require(state.HandoffTemperatureAvailable && state.HandoffTemperature == value
                                    && f.Celsius.Value == value && f.Output.Value == 999, "Handoff temperature used gauge scratch or mutated its producer.");
                            }
                        });
                        check(prefix + "unready or invalid handoff temperatures withdraw availability", () =>
                        {
                            try
                            {
                                f.Source.enabled = false;
                                var state = new VehicleState(); CallStatic("CaptureHandoffTemperature", item, state);
                                Require(!state.HandoffTemperatureAvailable, "Disabled producer published handoff heat.");
                                f.Source.enabled = true;
                                foreach (float value in new[] { float.NaN, -101f, 301f })
                                {
                                    f.Celsius.Value = value; state = new VehicleState(); CallStatic("CaptureHandoffTemperature", item, state);
                                    Require(!state.HandoffTemperatureAvailable && state.HandoffTemperature == 0, "Invalid heat escaped to handoff.");
                                }
                            }
                            finally { f.Source.enabled = true; f.Celsius.Value = 37; }
                        });
                        check(prefix + "live final and snapshot packets retain degrees before gauge clamping", () =>
                        {
                            reset(); mode(false, 255); Set(item, "LocallyOwned", true); sample(2000, true);
                            f.Celsius.Value = 37; f.Output.Value = 70;
                            Set(item, "NextVehicleStateAt", 0f); Call(vehicles, "UpdateVehicleStates", session);
                            var packet = SingleVehicle(capture); var live = (VehicleState)packet.Message;
                            Require(live.CoolantTemp == 79 && live.HandoffTemperatureAvailable && live.HandoffTemperature == 37 && packet.Channel == Channel.UnreliableSequenced, "Live packet used the dashboard rotation.");
                            var snapshot = (VehicleState)Call(vehicles, "TryBuildVehicleStateMessage", item, (byte)0)!;
                            Require(snapshot.CoolantTemp == 79 && snapshot.HandoffTemperatureAvailable && snapshot.HandoffTemperature == 37 && snapshot.Sequence == VehicleState.SnapshotSequence, "Snapshot used a different source.");
                            capture.Packets.Clear(); sample(0, false); Call(vehicles, "SendFinalVehicleState", session, item);
                            packet = SingleVehicle(capture); Require(((VehicleState)packet.Message).CoolantTemp == 79 && ((VehicleState)packet.Message).HandoffTemperatureAvailable && ((VehicleState)packet.Message).HandoffTemperature == 37 && packet.Channel == Channel.ReliableOrdered,
                                "Final OFF lost coolant temperature.");
                        });
                        check(prefix + "owner validated reception changes only the remote dashboard", () =>
                        {
                            reset(); mode(true, 1); f.Celsius.Value = 37;
                            var live = State(1, 0, 2000); live.CoolantTemp = 170; live.HandoffTemperatureAvailable = true; live.HandoffTemperature = 80;
                            Require((bool)Call(vehicles, "OnRemoteVehicleState", live)!, "Guest owner state rejected.");
                            CallStatic("ApplyRemoteGauges", item);
                            Require(Mathf.Abs(f.Output.Value - 80f / 2.41f) < 0.001f && f.Celsius.Value == 37,
                                "Reception changed thermal state or used the old negative gauge scale.");
                            for (int frame = 0; frame < 5; frame++)
                            {
                                f.Gauge.Fsm.Update();
                                Require(Mathf.Abs(f.Output.Value - 80f / 2.41f) < 0.001f && f.Celsius.Value == 37,
                                    "Native every-frame read replaced remote display or changed simulation.");
                            }
                            live.CoolantTemp = 0; Require(!(bool)Call(vehicles, "OnRemoteVehicleState", live)!, "Duplicate temperature report accepted.");
                            CallStatic("ApplyRemoteGauges", item); Require(Mathf.Abs(f.Output.Value - 80f / 2.41f) < 0.001f, "Duplicate changed display.");
                        });
                        check(prefix + "local seating ownership timeout and disconnect restore native reads", () =>
                        {
                            f.Celsius.Value = 37;
                            Action native = () => { f.Gauge.Fsm.Update(); Require(Mathf.Abs(f.Output.Value - 37f / 2.41f) < 0.001f, "Stale observer input overrode the native source."); };
                            Action remote = () => { f.Gauge.Fsm.Update(); Require(Mathf.Abs(f.Output.Value - 80f / 2.41f) < 0.001f, "Live observer input did not recover."); };
                            Set(item, "LocallyOwned", true); native(); Set(item, "LocallyOwned", false); remote();
                            var world = World.GetProperty("Instance", Static).GetValue(null, null);
                            var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null);
                            var parent = player.parent; player.SetParent(f.Body.transform, false);
                            try { native(); } finally { player.SetParent(parent, false); }
                            remote();
                            Set(item, "RemoteOwner", (byte)2); native(); Set(item, "RemoteOwner", (byte)1); remote();
                            float until = (float)Get(item, "RemoteEngineUntil"); Set(item, "RemoteEngineUntil", -1f); native(); Set(item, "RemoteEngineUntil", until); remote();
                            SetProperty(session, "State", SessionState.Idle); native(); SetProperty(session, "State", SessionState.Hosting); remote();
                            Call(vehicles, "ClearVehicleStateStreams"); native();
                        });
                    }
                }
            }
            finally
            {
                Set(item, "Body", originalBody); Set(item, "Path", originalPath); Set(item, "NativeTemperature", null);
                Set(item, "RequiresNativeTemperature", false); Set(item, "NextTemperatureProbeAt", 0f); reset();
            }
        }

        private sealed class TemperatureFixture : IDisposable
        {
            private readonly GameObject _root;
            internal readonly Rigidbody Body;
            internal readonly PlayMakerFSM Gauge, Source;
            internal readonly FsmFloat Celsius, Output;
            internal readonly FsmState State;
            internal object Item = null!;
            internal TemperatureFixture(List<object> rows, object rule)
            {
                string rootPath = (string)Get(rule, "RootPath");
                _root = new GameObject(rootPath.Split('/')[0]); _root.SetActive(false);
                Body = ObjectAt(rootPath).AddComponent<Rigidbody>(); Body.useGravity = false; Body.isKinematic = true;
                string sourcePath = (string)Get(rule, "SourcePath"), gaugePath = (string)Get(rule, "GaugePath");
                var sourceRow = NativeBagPartChecks.Find(rows, sourcePath, (string)Get(rule, "SourceFsm"));
                var gaugeRow = NativeBagPartChecks.Find(rows, gaugePath, "Temp");
                Source = NativeBagPartChecks.MakeFsm(ObjectAt(sourcePath), sourceRow);
                Gauge = NativeBagPartChecks.MakeFsm(ObjectAt(gaugePath), gaugeRow);
                _root.SetActive(true); Source.Fsm.Init(Source); Gauge.Fsm.Init(Gauge);
                Celsius = Source.FsmVariables.FindFsmFloat((string)Get(rule, "SourceVariable"));
                Output = Gauge.FsmVariables.FindFsmFloat((string)Get(rule, "GaugeVariable"));
                State = NativeBagPartChecks.State(Gauge, "Speed");
                foreach (Dictionary<string, object> row in (IEnumerable)gaugeRow["states"])
                    if ((string)row["name"] == "Speed")
                    {
                        var raw = (List<object>)row["actions"]; var actions = new FsmStateAction[raw.Count];
                        for (int i = 0; i < raw.Count; i++)
                            actions[i] = i < 3 ? (FsmStateAction)typeof(NativeBagPartChecks).GetMethod("ReadAction", Static).Invoke(null, new[] { raw[i], Gauge }) : new TemperatureQuiet();
                        State.Actions = actions; foreach (var action in actions) action.Init(State);
                    }
                ((FsmOwnerDefault)Get(State.Actions[0], "gameObject")).GameObject.Value = Source.gameObject;
                NativeBagPartChecks.Start(Source); NativeBagPartChecks.Start(Gauge);
            }
            private GameObject ObjectAt(string path)
            {
                var current = _root.transform; var names = path.Split('/');
                for (int i = 1; i < names.Length; i++)
                {
                    var child = current.Find(names[i]);
                    if (child == null) { var obj = new GameObject(names[i]); obj.transform.SetParent(current, false); child = obj.transform; }
                    current = child;
                }
                return current.gameObject;
            }
            internal float Read() => (float)CallStatic("ReadCoolantTempC", Item)!;
            internal void Rebind()
            {
                Set(Item, "NextTemperatureProbeAt", 0f); CallStatic("EnsureTemperatureProbe", Item);
                if (Get(Item, "NativeTemperature") == null)
                { Set(Item, "NextTemperatureProbeAt", 0f); CallStatic("EnsureTemperatureProbe", Item); }
            }
            internal void Reject()
            {
                Output.Value = -999; Rebind(); Require(Read() == 0 && Get(Item, "NativeTemperature") == null, "Invalid binding still supplied temperature.");
                CallStatic("ApplyRemoteTemperature", Item, 80f);
                Require(Output.Value == -999 && Celsius.Value == 87, "Invalid binding wrote a display or source.");
            }
            public void Dispose() => UnityEngine.Object.DestroyImmediate(_root);
        }
        private sealed class TemperatureQuiet : FsmStateAction { public override void OnEnter() { Finish(); } }
    }
}
