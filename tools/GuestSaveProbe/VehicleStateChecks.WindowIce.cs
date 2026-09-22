using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private static readonly string[] IceFields = { "CutoffWindshieldVar", "CutoffSideLeftVar", "CutoffSideRightVar",
            "CutoffDoorLeftVar", "CutoffDoorRightVar", "CutoffRearVar" };
        private static readonly string[] IceVariables = { "CutoffWindshield", "CutoffSideLeft", "CutoffSideRight",
            "CutoffDoorleft", "CutoffDoorright", "CutoffRear" };

        private static void RunWindowIceChecks(Action<string, Action> check, object item, object vehicles,
            SessionManager session, CaptureTransport capture, Action reset, Action<bool, byte> mode)
        {
            var original = item;
            var items = (IDictionary)Get(Get(vehicles, "_items"), "_items");
            var reader = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var parser = Activator.CreateInstance(reader, Members, null, new object[] {
                File.ReadAllText(Path.Combine(Application.dataPath, "../window-ice-probe.json")) }, null);
            var rows = (List<object>)((Dictionary<string, object>)reader.GetMethod("ReadObject", Members).Invoke(parser, null))["fsms"];
            try
            {
                foreach (string path in new[] { "CORRIS/Simulation/CarTempCorris", "SORBET(190-200psi)/Simulation/CarTempSorbet",
                    "JOBS/TAXIJOB/MACHTWAGEN/Simulation/CarTempTaxi" })
                using (var f = new WindowIceFixture(rows, path))
                {
                    reset();
                    item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                    items[VehicleId] = item;
                    Set(item, "Id", VehicleId); Set(item, "IsVehicle", true); Set(item, "Body", f.Body);
                    Set(item, "Path", path.Substring(0, path.IndexOf("/Simulation/", StringComparison.Ordinal)));
                    Set(item, "ClimateReady", false); Set(item, "NextClimateProbeAt", 0f);
                    Set(item, "LoggedClimateApply", true); Set(item, "NextClimateDiagAt", float.MaxValue);
                    Set(item, "RemoteWindowHeater", false); Set(item, "RemoteGlassDefrosting", false);
                    Set(item, "RemoteClimateUntil", -999f); Set(item, "OutClimateSequence", (ushort)81);
                    string prefix = "window ice " + f.Body.gameObject.name + ": ";
                    Func<VehicleClimate> sample = () => (VehicleClimate)Call(vehicles, "TryBuildVehicleClimate", item)!;
                    Action<VehicleClimate> receive = message =>
                    { mode(false, 255); message.OwnerPlayerId = 0; Set(item, "LocallyOwned", false); Call(vehicles, "OnRemoteVehicleClimate", message); };
                    Action baseline = () => ((WinterMP.Net.VehicleClimateStreamPolicy)Get(vehicles, "_vehicleClimateStreams")).Clear();
                    check(prefix + "catalog probe binds all six native panes", () =>
                    {
                        f.Seed(); var message = sample(); Require(message.IceMask == VehicleClimate.AllWindows, "Native pane binding incomplete.");
                        for (int i = 0; i < 6; i++) Require(ReferenceEquals(Get(item, IceFields[i]), f.Panes[i]), "Wrong native pane binding " + i);
                        Require(message.Sequence == VehicleClimate.SnapshotSequence && (ushort)Get(item, "OutClimateSequence") == 81,
                            "Snapshot consumed live sequence."); f.Same(f.Initial);
                    });
                    check(prefix + "live capture sends distinct panes without changing native state", () =>
                    {
                        mode(true, 255); f.Seed(); Set(item, "NextClimateAt", 0f); capture.Packets.Clear();
                        Call(vehicles, "UpdateVehicleClimate", session); Require(capture.Packets.Count > 0, "Climate update sent no packet.");
                        foreach (var packet in capture.Packets)
                        {
                            var message = packet.Message as VehicleClimate;
                            Require(message != null && message.Sequence == 82 && packet.Channel == WinterMP.Net.Channel.UnreliableSequenced,
                                "Wrong live climate framing."); AssertIceBytes(message!, f.Initial);
                        }
                        f.Same(f.Initial);
                    });
                    check(prefix + "snapshot and late update preserve each pane and interior frost", () =>
                    {
                        mode(true, 255); f.Seed(); var message = sample();
                        foreach (var pane in f.Panes) pane.Value = 0; receive(message); f.SameQuantized(f.Initial);
                        Require(Mathf.Abs(f.Frost.FsmVariables.FindFsmFloat("Frost").Value - message.Frost / 255f) < .000001f, "Exterior ice overwrote interior frost.");
                        foreach (var pane in f.Panes) pane.Value = .99f;
                        Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime); f.SameQuantized(f.Initial);
                    });
                    for (int pane = 0; pane < 6; pane++)
                    {
                        int index = pane;
                        check(prefix + "native scrape affects only pane " + index + " through snapshot and presentation", () =>
                        {
                            mode(true, 255); f.Seed(); var before = f.Values(); f.Scrape(index);
                            Require(f.Panes[index].Value > before[index], "Native scraping did not raise cutoff.");
                            for (int i = 0; i < 6; i++) if (i != index) Require(f.Panes[i].Value == before[i], "Native scrape changed another pane.");
                            var expected = f.Values(); var message = sample(); AssertIceBytes(message, expected);
                            foreach (var value in f.Panes) value.Value = 0; receive(message); f.SameQuantized(expected);
                            Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime); f.SameQuantized(expected);
                        });
                    }
                    check(prefix + "native initial frost and sheltered reset keep the correct cutoff direction", () =>
                    {
                        mode(true, 255); f.ResetCutoffs(false); var cold = sample();
                        Require(cold.IceMask == 63 && IceBytes(cold)[0] == 0, "Iced cutoff was inverted or omitted.");
                        f.ResetCutoffs(true); var clear = sample(); foreach (byte value in IceBytes(clear)) Require(value == 255, "Sheltered cutoff was inverted.");
                        receive(cold); f.Same(new float[6]); receive(clear); f.Same(new[] { 1f, 1f, 1f, 1f, 1f, 1f });
                    });
                    check(prefix + "native rear heat survives without clearing the windshield", () =>
                    {
                        mode(true, 255); f.Seed(); var before = f.Values(); f.RearHeat(); var after = f.Values();
                        Require(Mathf.Abs(after[5] - before[5] - .004f) < .000001f, "Native rear heat increment changed.");
                        for (int i = 0; i < 5; i++) Require(after[i] == before[i], "Rear heater changed another pane.");
                        var message = sample(); receive(message); Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime); f.SameQuantized(after);
                    });
                    for (int pane = 0; pane < 6; pane++)
                    {
                        int index = pane;
                        check(prefix + "unavailable pane " + index + " cannot copy the windshield or retain a previous hold", () =>
                        {
                            f.Seed(); receive(sample()); Set(item, IceFields[index], null);
                            try
                            {
                                var absent = sample(); Require((absent.IceMask & (1 << index)) == 0 && IceBytes(absent)[index] == 0, "Missing pane was published.");
                                Set(item, IceFields[index], f.Panes[index]); receive(absent); f.Panes[index].Value = .987f;
                                Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime);
                                Require(f.Panes[index].Value == .987f, "Unavailable pane was overwritten by another or stale pane.");
                            }
                            finally { Set(item, IceFields[index], f.Panes[index]); }
                        });
                    }
                    check(prefix + "nonfinite source is omitted while finite visual extremes clamp", () =>
                    {
                        f.Seed(); f.Panes[1].Value = float.NaN; f.Panes[2].Value = float.PositiveInfinity; f.Panes[3].Value = float.NegativeInfinity;
                        f.Panes[4].Value = -float.MaxValue; f.Panes[5].Value = float.MaxValue; var message = sample();
                        Require(message.ValidIce && message.IceMask == 49 && message.IceDoorRight == 0 && message.IceRear == 255,
                            "Invalid source escaped or finite cutoff overflowed.");
                        Require(float.IsNaN(f.Panes[1].Value) && f.Panes[4].Value == -float.MaxValue, "Capture changed native source.");
                    });
                    check(prefix + "stale duplicate and malformed packets cannot change a live pane", () =>
                    {
                        f.Seed(); var accepted = sample(); accepted.Sequence = 400; baseline(); receive(accepted); var expected = f.Values();
                        var rejected = sample(); rejected.Sequence = 399; rejected.IceRear = 0; receive(rejected); f.Same(expected);
                        rejected.Sequence = 400; receive(rejected); f.Same(expected);
                        rejected.Sequence = 401; rejected.IceMask = 128; receive(rejected); f.Same(expected);
                        Require(((VehicleClimate)Get(item, "AcceptedVehicleClimate")).Sequence == 400, "Invalid message consumed sequence.");
                        rejected.IceMask = 63; receive(rejected); Require(f.Panes[5].Value == 0, "Repair after invalid message was lost.");
                    });
                    check(prefix + "snapshot bytes are copied and do not consume live dedup", () =>
                    {
                        f.Seed(); var message = sample(); var expected = f.Values(); baseline(); receive(message);
                        message.IceRear = 0; message.IceMask = 0; Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime); f.SameQuantized(expected);
                        var live = WinterMP.Net.VehicleClimateStreamPolicy.Copy((VehicleClimate)Get(item, "AcceptedVehicleClimate"));
                        live.Sequence = 0; Require((bool)Call(vehicles, "OnRemoteVehicleClimate", live)!, "Snapshot changed live dedup.");
                    });
                    check(prefix + "local owner and expired hold preserve their native panes", () =>
                    {
                        f.Seed(); var message = sample(); receive(message); Set(item, "LocallyOwned", true); f.Seed(); var expected = f.Values();
                        message.IceRear = 0; Call(vehicles, "OnRemoteVehicleClimate", message);
                        Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime); f.Same(expected);
                        Set(item, "LocallyOwned", false); Set(item, "RemoteClimateUntil", Time.unscaledTime - 1);
                        Call(vehicles, "LateUpdateRemoteVehicles", Time.unscaledTime); f.Same(expected);
                    });
                    check(prefix + "foreign vehicle cannot change the bound panes", () =>
                    {
                        f.Seed(); var message = sample(); message.VehicleId++; message.IceRear = 0;
                        Call(vehicles, "OnRemoteVehicleClimate", message); f.Same(f.Initial);
                    });
                }
            }
            finally
            {
                items[VehicleId] = original; reset();
            }
        }

        private static byte[] IceBytes(VehicleClimate message) => new[] { message.Ice, message.IceSideLeft, message.IceSideRight,
            message.IceDoorLeft, message.IceDoorRight, message.IceRear };
        private static void AssertIceBytes(VehicleClimate message, float[] expected)
        {
            var bytes = IceBytes(message); Require(message.IceMask == 63, "Missing source pane.");
            for (int i = 0; i < 6; i++)
                Require(bytes[i] == (byte)Mathf.RoundToInt(Mathf.Clamp01(expected[i]) * 255), "Wrong window byte " + i);
        }

        private sealed class WindowIceFixture : IDisposable
        {
            private readonly GameObject _root;
            private readonly PlayMakerFSM _freezing, _heater;
            private readonly FsmStateAction _rearHeat;
            internal readonly Rigidbody Body;
            internal readonly PlayMakerFSM Frost;
            internal readonly FsmFloat[] Panes = new FsmFloat[6];
            internal readonly float[] Initial = { .125f, .25f, .375f, .5f, .625f, .75f };
            internal WindowIceFixture(List<object> rows, string path)
            {
                var segments = path.Split('/'); _root = new GameObject(segments[0]); _root.SetActive(false);
                var current = _root.transform;
                for (int i = 1; i < segments.Length; i++)
                { var child = new GameObject(segments[i]); child.transform.SetParent(current, false); current = child.transform; }
                var car = current.parent.parent.gameObject; Body = car.AddComponent<Rigidbody>(); Body.useGravity = false; Body.isKinematic = true;
                var raw = NativeBagPartChecks.Find(rows, path, "Freezing");
                _freezing = NativeBagPartChecks.MakeFsm(current.gameObject, raw);
                Frost = NativeBagPartChecks.MakeFsm(current.gameObject, NativeBagPartChecks.Find(rows, path, "GlassFrosting"));
                var heaterRow = NativeBagPartChecks.Find(rows,
                    path.Substring(0, path.IndexOf("/Simulation/", StringComparison.Ordinal)) + "/Simulation/Electricity/PowerON/HeaterUnit", "Function");
                var heaterObject = new GameObject("probe rear heat"); heaterObject.transform.SetParent(_root.transform, false);
                _heater = NativeBagPartChecks.MakeFsm(heaterObject, heaterRow);
                foreach (var fsm in new[] { _freezing, Frost, _heater })
                {
                    fsm.Fsm.Init(fsm);
                    foreach (var state in fsm.FsmStates) { state.Transitions = new FsmTransition[0]; state.Actions = new FsmStateAction[0]; }
                }
                foreach (Dictionary<string, object> state in (IEnumerable)raw["states"])
                {
                    var native = NativeBagPartChecks.State(_freezing, (string)state["name"]); var actions = new List<FsmStateAction>();
                    foreach (Dictionary<string, object> action in (IEnumerable)state["actions"])
                        if (((string)action["type"]).EndsWith(".FloatAdd", StringComparison.Ordinal)
                            && native.Name != "Sound" || ((string)action["type"]).EndsWith(".SetFloatValue", StringComparison.Ordinal))
                            actions.Add(ReadIceAction(action, _freezing));
                    native.Actions = actions.ToArray(); foreach (var action in native.Actions) action.Init(native);
                }
                FsmStateAction? heat = null;
                foreach (Dictionary<string, object> state in (IEnumerable)heaterRow["states"])
                    if ((string)state["name"] == "Rear defrosting")
                        foreach (Dictionary<string, object> action in (IEnumerable)state["actions"])
                            if (((string)action["type"]).EndsWith(".AddFsmFloat", StringComparison.Ordinal)) heat = ReadIceAction(action, _heater);
                _rearHeat = heat ?? throw new InvalidOperationException("Native rear heat missing.");
                ((FsmOwnerDefault)Get(_rearHeat, "gameObject")).GameObject.Value = _freezing.gameObject;
                var rearState = NativeBagPartChecks.State(_heater, "Rear defrosting"); rearState.Actions = new[] { _rearHeat }; _rearHeat.Init(rearState);
                for (int i = 0; i < 6; i++) Panes[i] = _freezing.FsmVariables.FindFsmFloat(IceVariables[i]);
                _root.SetActive(true); foreach (var fsm in new[] { _freezing, Frost, _heater }) NativeBagPartChecks.Start(fsm);
            }
            internal void Seed()
            {
                for (int i = 0; i < 6; i++) Panes[i].Value = Initial[i];
                Frost.FsmVariables.FindFsmFloat("Frost").Value = .9f;
            }
            internal float[] Values() { var result = new float[6]; for (int i = 0; i < 6; i++) result[i] = Panes[i].Value; return result; }
            internal void Same(float[] expected)
            { for (int i = 0; i < 6; i++) Require(Panes[i].Value == expected[i], "Unexpected native cutoff " + i + ": " + Panes[i].Value + " / " + expected[i]); }
            internal void SameQuantized(float[] expected)
            { for (int i = 0; i < 6; i++) Require(Mathf.Abs(Panes[i].Value - expected[i]) <= .5f / 255f + .000001f, "Remote cutoff differs at pane " + i); }
            internal void Scrape(int index)
            { NativeBagPartChecks.Fire(_freezing, new[] { "State 7", "State 2", "State 3", "State 5", "State 6", "State 4" }[index]); }
            internal void ResetCutoffs(bool sheltered) { NativeBagPartChecks.Fire(_freezing, sheltered ? "Check roof" : "State 1"); }
            internal void RearHeat() { NativeBagPartChecks.Fire(_heater, "Rear defrosting"); }
            public void Dispose() { UnityEngine.Object.DestroyImmediate(_root); }
        }
        private static FsmStateAction ReadIceAction(Dictionary<string, object> row, PlayMakerFSM fsm) =>
            (FsmStateAction)typeof(NativeBagPartChecks).GetMethod("ReadAction", Static).Invoke(null, new object[] { row, fsm });
    }
}
