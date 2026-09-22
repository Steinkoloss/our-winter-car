using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private static void RunCondensationChecks(Action<string, Action> check, object original, object vehicles,
            SessionManager session, CaptureTransport capture, Action reset)
        {
            var items = (IDictionary)Get(Get(vehicles, "_items"), "_items");
            var rows = ReadOccupancyRows("condensation-probe.json"); var iceRows = ReadOccupancyRows("window-ice-probe.json");
            try
            {
                foreach (string path in new[] { "CORRIS/Simulation/CarTempCorris", "SORBET(190-200psi)/Simulation/CarTempSorbet",
                    "JOBS/TAXIJOB/MACHTWAGEN/Simulation/CarTempTaxi" })
                using (var f = new CondensationFixture(rows, iceRows, path))
                {
                    var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                    items[VehicleId] = item; Set(item, "Id", VehicleId); Set(item, "Body", f.Ice.Body); Set(item, "IsVehicle", true);
                    Set(item, "Path", path.Substring(0, path.IndexOf("/Simulation/", StringComparison.Ordinal)));
                    CallStatic("EnsureClimateProbe", item); Set(item, "LoggedClimateApply", true); Set(item, "NextClimateDiagAt", float.MaxValue);
                    string prefix = "condensation " + f.Ice.Body.gameObject.name + ": ";
                    Action<bool, byte> mode = (host, owner) =>
                    {
                        SetProperty(session, "IsHost", host); SetProperty(session, "State", host ? SessionState.Hosting : SessionState.Connected);
                        SetProperty(session, "LocalPlayerId", host ? (byte)0 : (byte)3); Set(item, "RemoteOwner", owner); Set(item, "LocallyOwned", false);
                    };
                    Action clean = () => { Call(vehicles, "ClearClimateStreams"); f.Seed(); mode(true, 255); capture.Packets.Clear(); };
                    Func<VehicleClimate> sample = () => (VehicleClimate)Call(vehicles, "TryBuildVehicleClimate", item)!;
                    Func<byte, byte, ushort, VehicleClimate> report = (frost, legacyFog, seq) => new VehicleClimate {
                        VehicleId = VehicleId, OwnerPlayerId = 0, Sequence = seq, Frost = frost, Fog = legacyFog, CabinTemp = 191, Flags = VehicleClimate.FlagCabinTemperature };
                    Action<VehicleClimate> receive = message =>
                    { mode(false, 0); Require((bool)Call(vehicles, "OnRemoteVehicleClimate", message)!, "Condensation report rejected."); };
                    Action<byte> opacity = expected =>
                    {
                        float value = expected / 255f;
                        Require(Mathf.Abs(f.Amount.Value - value) < .000001f && Mathf.Abs(f.Color.Value.a - value) < .000001f
                            && Mathf.Abs(f.Material.color.a - value) < .000001f, "Native variable/color/material opacity disagree.");
                    };

                    check(prefix + "catalog probe binds native opacity color material and cabin degrees", () =>
                    {
                        clean(); Require(ReferenceEquals(Get(item, "FrostVar"), f.Amount) && ReferenceEquals(Get(item, "FrostColorVar"), f.Color)
                            && ReferenceEquals(Get(item, "FrostGlassMat"), f.MaterialVar) && ReferenceEquals(Get(item, "InteriorTempVar"), f.Cabin),
                            "Native condensation probe selected the wrong source.");
                        f.NativeColor("Defrosting"); Require(f.Material.color == new Color(1, 1, 1, f.Amount.Value), "Native defrosting does not use white RGB and Frost alpha.");
                        f.NativeColor("Warm car"); Require(f.Material.color.a == f.Amount.Value, "Native warm-car material writer differs.");
                    });
                    check(prefix + "white tint and growth scratch cannot become full fog in capture", () =>
                    {
                        clean(); f.Amount.Value = 0; f.Sweat.Value = .1f; f.Color.Value = Color.white; f.Material.color = Color.white;
                        var message = sample(); Require(message.Frost == 0 && message.Fog == 0, "White tint/growth rate was published as condensation.");
                        Require(f.Sweat.Value == .1f && f.Color.Value == Color.white, "Capture changed native scratch/tint.");
                    });
                    check(prefix + "live capture keeps native condensation separate from exterior ice", () =>
                    {
                        clean(); Call(vehicles, "UpdateVehicleClimate", session); Require(capture.Packets.Count > 0, "Missing live climate.");
                        foreach (var packet in capture.Packets)
                            Require(packet.Message is VehicleClimate c && c.Frost == 102 && c.Fog == 0 && c.Ice == 32
                                && packet.Channel == Channel.UnreliableSequenced, "Live condensation or exterior cutoff changed units.");
                    });
                    foreach (byte value in new byte[] { 0, 1, 127, 254, 255 })
                    {
                        byte expected = value;
                        check(prefix + "remote alpha " + expected + " updates material immediately and in LateUpdate", () =>
                        {
                            clean(); receive(report(expected, 255, 1)); opacity(expected);
                            f.Amount.Value = .9f; f.Color.Value = new Color(.2f, .3f, .4f, .9f); f.Material.color = new Color(.6f, .7f, .8f, .9f);
                            Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime); opacity(expected); f.TintsAndCutoff();
                        });
                    }
                    check(prefix + "retired fog cannot overwrite native sweat or temperature arithmetic", () =>
                    {
                        clean(); receive(report(102, 255, 1)); f.Scratch();
                        Require(Mathf.Abs(f.Cabin.Value - (191 / 255f * 80f - 40f)) < .00001f, "Cabin degrees were not applied.");
                        f.Sweat.Value = .08f; f.Temp.Value = .17f; receive(report(102, 0, 2));
                        Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime);
                        Require(f.Sweat.Value == .08f && f.Temp.Value == .17f, "Remote opacity changed live arithmetic scratch.");
                    });
                    check(prefix + "native warm-car divide remains intact across a received cabin temperature", () =>
                    {
                        clean(); f.Temp.Value = 20; NativeBagPartChecks.Fire(f.Ice.Frost, "Warm car");
                        Require(Mathf.Abs(f.Temp.Value - .2f) < .000001f, "Native warm-car scale changed.");
                        receive(report(204, 255, 1)); Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime);
                        Require(Mathf.Abs(f.Temp.Value - .2f) < .000001f, "Remote cabin degrees overwrote native defrosting rate.");
                        float before = f.Amount.Value; f.Action("Warm car", 1).OnUpdate();
                        Require(Mathf.Abs(f.Amount.Value - (before - .2f * Time.deltaTime)) < .000001f, "Native warm-car subtraction changed cadence/units.");
                    });
                    check(prefix + "native color writer agrees with received opacity on the next update", () =>
                    {
                        clean(); receive(report(73, 255, 1)); f.NativeColor("Defrosting"); opacity(73);
                        Require(f.Material.color.r == 1 && f.Material.GetFloat("_Cutoff") == .897f, "Remote condensation rewrote native tint or cutoff.");
                    });
                    check(prefix + "missing material does not prevent native opacity or exterior pane updates", () =>
                    {
                        clean(); f.MaterialVar.Value = null;
                        try
                        {
                            var m = report(51, 255, 1); m.IceMask = VehicleClimate.Rear; m.IceRear = 220; receive(m);
                            Require(f.Amount.Value == .2f && f.Color.Value.a == .2f && f.Ice.Panes[5].Value == 220 / 255f,
                                "Missing material blocked independent climate presentation."); f.Scratch();
                        }
                        finally { f.MaterialVar.Value = f.Material; }
                    });
                    check(prefix + "missing cabin data cannot sample the already divided glass temperature", () =>
                    {
                        clean(); Set(item, "InteriorTempVar", null); f.Temp.Value = .2f;
                        try { Require(sample().CabinTemp == 128, "Glass arithmetic scratch was captured as cabin degrees."); }
                        finally { Set(item, "InteriorTempVar", f.Cabin); }
                    });
                    check(prefix + "stale and locally owned updates cannot change material opacity", () =>
                    {
                        clean(); receive(report(100, 255, 2));
                        Require(!(bool)Call(vehicles, "OnRemoteVehicleClimate", report(0, 0, 1))!, "Stale opacity accepted."); opacity(100);
                        Set(item, "LocallyOwned", true); f.Amount.Value = .3f; f.NativeColor("Defrosting");
                        Require(!(bool)Call(vehicles, "OnRemoteVehicleClimate", report(255, 255, 3))!, "Local owner opacity overwritten.");
                        Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime); Require(f.Material.color.a == .3f, "Remote hold overwrote local owner material.");
                    });
                    check(prefix + "hold expiry and ownership handoff leave native material in control", () =>
                    {
                        clean(); receive(report(180, 255, 1)); Set(item, "RemoteClimateUntil", -999f);
                        f.Amount.Value = .15f; f.NativeColor("Warm car"); Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime);
                        Require(f.Material.color.a == .15f, "Expired hold still painted condensation.");
                        receive(report(190, 255, 2)); Set(item, "RemoteOwner", (byte)1); f.Amount.Value = .25f; f.NativeColor("Defrosting");
                        Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime); Require(f.Material.color.a == .25f, "Former owner painted after handoff.");
                    });
                    clean();
                }
            }
            finally { items[VehicleId] = original; reset(); }
        }

        private sealed class CondensationFixture : IDisposable
        {
            internal readonly WindowIceFixture Ice;
            internal readonly FsmColor Color;
            internal readonly FsmMaterial MaterialVar;
            internal readonly Material Material;
            internal readonly FsmFloat Amount, Sweat, Temp, Cabin;
            internal CondensationFixture(List<object> rows, List<object> iceRows, string path)
            {
                Ice = new WindowIceFixture(iceRows, path);
                var frost = Ice.Frost; var raw = NativeBagPartChecks.Find(rows, path, "GlassFrosting");
                Color = new FsmColor { Name = "Color", UseVariable = true };
                frost.FsmVariables.ColorVariables = new[] { Color };
                Material = new Material(Shader.Find("Standard")) { name = "probe condensation" };
                Require(Material.HasProperty("_Color") && Material.HasProperty("_Cutoff"), "Condensation test shader lacks audited properties.");
                MaterialVar = new FsmMaterial { Name = "FrostGlass", UseVariable = true, Value = Material };
                frost.FsmVariables.MaterialVariables = new[] { MaterialVar };
                Amount = frost.FsmVariables.FindFsmFloat("Frost"); Sweat = frost.FsmVariables.FindFsmFloat("SweatRate");
                Temp = frost.FsmVariables.FindFsmFloat("Temp");
                var data = NativeBagPartChecks.MakeFsm(frost.gameObject, NativeBagPartChecks.Find(rows, path, "Data")); data.Fsm.Init(data);
                foreach (var state in data.FsmStates) { state.Actions = new FsmStateAction[0]; state.Transitions = new FsmTransition[0]; }
                Cabin = data.FsmVariables.FindFsmFloat("InteriorTemp");
                foreach (Dictionary<string, object> state in (IEnumerable)raw["states"])
                {
                    string name = (string)state["name"]; if (name != "Defrosting" && name != "Warm car") continue;
                    var native = NativeBagPartChecks.State(frost, name); var actions = new List<FsmStateAction>();
                    foreach (Dictionary<string, object> action in (IEnumerable)state["actions"])
                        if (!(string.Equals((string)action["type"], "HutongGames.PlayMaker.Actions.Wait", StringComparison.Ordinal)))
                            actions.Add(ReadCondensationAction(action, frost));
                    native.Actions = actions.ToArray(); foreach (var action in native.Actions) action.Init(native);
                }
                NativeBagPartChecks.Start(data);
            }
            internal FsmStateAction Action(string state, int index) => NativeBagPartChecks.State(Ice.Frost, state).Actions[index];
            internal void NativeColor(string state) { Action(state, 3).OnEnter(); Action(state, 4).OnEnter(); }
            internal void Seed()
            {
                NativeBagPartChecks.Fire(Ice.Frost, "Probe idle"); Ice.Seed(); Amount.Value = .4f; Sweat.Value = .031f; Temp.Value = .23f;
                Cabin.Value = 15; Color.Value = new Color(.2f, .3f, .4f, .4f); Material.color = new Color(.6f, .7f, .8f, .4f);
                Material.SetFloat("_Cutoff", .897f);
            }
            internal void Scratch() => Require(Sweat.Value == .031f && Temp.Value == .23f, "Remote climate corrupted native calculation scratch.");
            internal void TintsAndCutoff() => Require(Color.Value.r == .2f && Color.Value.g == .3f && Color.Value.b == .4f
                && Material.color.r == .6f && Material.color.g == .7f && Material.color.b == .8f && Material.GetFloat("_Cutoff") == .897f,
                "Remote alpha changed RGB tint or shader cutoff.");
            public void Dispose() { Ice.Dispose(); UnityEngine.Object.DestroyImmediate(Material); }
        }

        private static FsmStateAction ReadCondensationAction(Dictionary<string, object> row, PlayMakerFSM fsm)
        {
            var action = (FsmStateAction)Activator.CreateInstance(Type.GetType((string)row["type"] + ", Assembly-CSharp", true));
            action.Reset(); action.Enabled = (bool)row["enabled"];
            foreach (Dictionary<string, object> p in (IEnumerable)row["parameters"])
            {
                var field = action.GetType().GetField((string)p["field"], Members); string type = (string)p["type"]; object? value;
                if (type == "FsmColor")
                {
                    string hex = (string)p["rawHex"]; var bytes = new byte[hex.Length / 2];
                    for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                    string name = System.Text.Encoding.UTF8.GetString(bytes, 17, bytes.Length - 17);
                    Require(bytes[16] != 0 && name == "Color", "Native condensation color reference changed.");
                    value = fsm.FsmVariables.FindFsmColor(name);
                }
                else if (type == "FsmMaterial")
                {
                    var v = (Dictionary<string, object>)p["value"];
                    Require(Convert.ToBoolean(v["useVariable"]) && (string)v["name"] == "FrostGlass", "Native material reference changed.");
                    value = fsm.FsmVariables.GetFsmMaterial("FrostGlass");
                }
                else value = typeof(NativeBagPartChecks).GetMethod("ReadParameter", Static).Invoke(null, new object[] { p, field.FieldType, fsm });
                field.SetValue(action, value);
            }
            return action;
        }
    }
}
