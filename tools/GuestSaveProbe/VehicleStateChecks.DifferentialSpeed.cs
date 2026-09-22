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
        private static void RunDifferentialSpeedChecks(Action<string, Action> check, object item, object vehicles,
            SessionManager session, CaptureTransport transport, Action reset, Action<bool, byte> mode)
        {
            string[] fields = { "Body", "Path", "EngineRevsVar", "NativeEngineRpm", "NativeEngineRpmOutput", "RequiresNativeEngineRpm" };
            var saved = new Dictionary<string, object>(); foreach (string name in fields) saved[name] = Get(item, name);
            using (GuestEngineInputChecks.SuspendProjectionForFixture())
            using (var f = new DifferentialFixture())
            try
            {
                reset(); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); f.Item = item;
                foreach (float speed in new[] { 0f, 1f, -1f, 3150.25f, -3150.25f })
                    check("differential speed: exact native signed capture " + speed, () =>
                    {
                        f.SetSpeed(speed); var before = f.Output.Value; var value = f.Capture();
                        Require(value.DifferentialSpeedAvailable && value.DifferentialSpeed == speed, "Signed native speed was lost.");
                        Require(f.Output.Value == before && f.Speed == speed, "Capture changed the native reader or component."); f.Unchanged();
                        var wire = PacketCodec.Encode(value); Require(wire.Length == 35 && ((VehicleState)PacketCodec.Decode(wire)).DifferentialSpeed == speed,
                            "Captured differential speed did not survive framing.");
                    });
                check("differential speed: fresh component replaces old wear scratch without replaying the reader", () =>
                {
                    f.SetSpeed(8); NativeBagPartChecks.Fire(f.Fsm, "State 1"); Require(f.Output.Value == 8, "Native GetProperty baseline failed.");
                    f.ResetSaved(); f.SetSpeed(-42.5f); var value = f.Capture();
                    Require(value.DifferentialSpeed == -42.5f && f.Output.Value == 8 && f.Fsm.ActiveStateName == "State 2", "Capture borrowed stale scratch or entered a native state."); f.Unchanged();
                });
                foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    check("differential speed: nonfinite native source withdraws availability " + invalid, () =>
                    {
                        f.SetSpeed(invalid); var value = f.Capture(); Unknown(value); f.SetSpeed(11);
                        Require(f.Capture().DifferentialSpeed == 11, "Finite native source did not recover."); f.Unchanged();
                    });
                check("differential speed: stopped simulation and disabled producer cannot expose old values", () =>
                {
                    f.SetSpeed(12); var value = new VehicleState { DifferentialSpeedAvailable = true, DifferentialSpeed = 123 };
                    CallStatic("CaptureDifferentialSpeedTelemetry", item, value); Unknown(value);
                    f.Fsm.enabled = false; try { Unknown(f.Capture()); } finally { f.Fsm.enabled = true; }
                    NativeBagPartChecks.Fire(f.Fsm, "Probe idle"); Unknown(f.Capture());
                    NativeBagPartChecks.Fire(f.Fsm, "State 2"); Require(f.Capture().DifferentialSpeed == 12, "Resumed producer did not recover.");
                });
                var property = f.Property;
                foreach (string field in new[] { "PropertyName", "TargetTypeName", "setProperty", "FloatParameter", "TargetObject", "everyFrame", "enabled" })
                    check("differential speed: changed native " + field + " withdraws capture and repairs", () =>
                    {
                        string member = property.PropertyName, typeName = property.TargetTypeName; var output = property.FloatParameter; var target = property.TargetObject;
                        try
                        {
                            if (field == "PropertyName") property.PropertyName = "torque";
                            else if (field == "TargetTypeName") property.TargetTypeName = "Wheel";
                            else if (field == "setProperty") property.setProperty = true;
                            else if (field == "FloatParameter") property.FloatParameter = new FsmFloat { Name = "DiffSpeed", UseVariable = true };
                            else if (field == "TargetObject") property.TargetObject = new FsmObject { ObjectType = f.Drive.GetType(), Value = f.Drive };
                            else if (field == "everyFrame") Set(f.Read, "everyFrame", true);
                            else f.Read.Enabled = false;
                            Unknown(f.Capture()); Require(Get(item, "DifferentialSpeedSource") == null, "Invalid native binding remained cached.");
                        }
                        finally
                        { property.PropertyName = member; property.TargetTypeName = typeName; property.setProperty = false; property.FloatParameter = output;
                            property.TargetObject = target; Set(f.Read, "everyFrame", false); f.Read.Enabled = true; }
                        Set(item, "NextDifferentialProbeAt", 0f); Require(f.Capture().DifferentialSpeedAvailable, "Repaired source did not rebind."); f.Unchanged();
                    });
                check("differential speed: retry waits for its probe interval after a broken signature", () =>
                {
                    property.PropertyName = "torque"; Unknown(f.Capture()); property.PropertyName = "differentialSpeed";
                    Unknown(f.Capture()); Set(item, "NextDifferentialProbeAt", 0f); Require(f.Capture().DifferentialSpeedAvailable, "Scheduled retry did not recover.");
                });
                check("differential speed: ambiguous producer and global output alias are rejected", () =>
                {
                    var duplicate = f.Fsm.gameObject.AddComponent<PlayMakerFSM>(); duplicate.enabled = false; Set(duplicate, "fsm", new Fsm()); duplicate.FsmName = "Wear";
                    try { Unknown(f.Capture()); } finally { UnityEngine.Object.DestroyImmediate(duplicate); }
                    Set(item, "NextDifferentialProbeAt", 0f); Require(f.Capture().DifferentialSpeedAvailable, "Unique producer did not recover.");
                    var globals = FsmVariables.GlobalVariables.FloatVariables; var changed = new List<FsmFloat>(globals) { f.Output };
                    try { FsmVariables.GlobalVariables.FloatVariables = changed.ToArray(); Unknown(f.Capture()); }
                    finally { FsmVariables.GlobalVariables.FloatVariables = globals; }
                    Set(item, "NextDifferentialProbeAt", 0f); Require(f.Capture().DifferentialSpeedAvailable, "Local output did not recover.");
                });
                check("differential speed: replacement body cannot reuse old source and restoration rebinds", () =>
                {
                    var other = new GameObject("replacement CORRIS"); var body = other.AddComponent<Rigidbody>(); body.isKinematic = true;
                    try { Set(item, "Body", body); Unknown(f.Capture()); Require(Get(item, "DifferentialSpeedSource") == null, "Body replacement retained native source."); }
                    finally { Set(item, "Body", f.Body); UnityEngine.Object.DestroyImmediate(other); }
                    Set(item, "NextDifferentialProbeAt", 0f); Require(f.Capture().DifferentialSpeedAvailable, "Restored body did not rebind.");
                });
                RunDifferentialStreamChecks(check, f, item, vehicles, session, transport, mode, saved["EngineRevsVar"]);
                check("differential speed: session teardown clears source and retry state", () =>
                {
                    Call(vehicles, "ClearVehicleStateStreams"); Require(Get(item, "DifferentialSpeedSource") == null
                        && (float)Get(item, "NextDifferentialProbeAt") == 0 && (float)Get(item, "NextDifferentialErrorAt") == 0, "Teardown retained telemetry binding.");
                });
            }
            finally
            {
                Set(item, "DifferentialSpeedSource", null); Set(item, "NextDifferentialProbeAt", 0f); Set(item, "NextDifferentialErrorAt", 0f);
                foreach (string name in fields) Set(item, name, saved[name]); reset();
            }
        }

        private static void Unknown(VehicleState value) => Require(!value.DifferentialSpeedAvailable && value.DifferentialSpeed == 0, "Unavailable reading leaked a value.");

        private static void RunDifferentialStreamChecks(Action<string, Action> check, DifferentialFixture f, object item, object vehicles,
            SessionManager session, CaptureTransport transport, Action<bool, byte> mode, object rpm)
        {
            // Isolate sender integration from the separately tested engine RPM graph.
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true); var property = catalog.GetProperty("VehicleEngineRpm", Static);
            object original = property.GetValue(null, null); VehicleState? sent = null;
            try
            {
                property.GetSetMethod(true).Invoke(null, new object?[] { null }); Set(item, "RequiresNativeEngineRpm", false);
                Set(item, "NativeEngineRpm", null); Set(item, "EngineRevsVar", rpm); Set(item, "SystemsReady", true);
                mode(false, 255); Set(item, "LocallyOwned", true); SetProperty(session, "LocalPlayerId", (byte)1);
                check("differential speed: live and reliable final sender packets contain current native input", () =>
                {
                    foreach (bool final in new[] { false, true })
                    {
                        transport.Packets.Clear(); f.SetSpeed(final ? -45.25f : 16.5f); Set(item, "NextVehicleStateAt", 0f);
                        f.WithDrive(() => Call(vehicles, "SendVehicleState", session, item, Time.unscaledTime, final));
                        Require(transport.Packets.Count > 0, "Sender did not emit state."); var packet = transport.Packets[transport.Packets.Count - 1];
                        sent = packet.Message as VehicleState;
                        Require(sent != null && sent.DifferentialSpeedAvailable && sent.DifferentialSpeed == f.Speed
                            && packet.Channel == (final ? Channel.ReliableOrdered : Channel.UnreliableSequenced), "Sender lost native telemetry or final reliability.");
                    }
                });
                check("differential speed: accepted host state and join snapshot retain guest value without native writes", () =>
                {
                    Require(sent != null, "Missing sender baseline."); mode(true, 1); Set(item, "LocallyOwned", false); f.SetSpeed(777);
                    Require((bool)Call(vehicles, "TryAcceptGuestVehicleState", sent, (byte)1)! && (bool)Call(vehicles, "OnRemoteVehicleState", sent)!, "Host rejected legitimate sender.");
                    sent!.DifferentialSpeed = 999; var approved = (VehicleState)Get(item, "AcceptedVehicleState");
                    Require(approved.DifferentialSpeed == -45.25f && f.Speed == 777, "Accept mutated source or retained caller storage.");
                    var snapshot = (VehicleState)Call(vehicles, "TryBuildVehicleStateMessage", item, (byte)0)!;
                    Require(snapshot.DifferentialSpeedAvailable && snapshot.DifferentialSpeed == -45.25f && snapshot.Sequence == VehicleState.SnapshotSequence,
                        "Join snapshot read host drivetrain instead of accepted guest input.");
                    snapshot.DifferentialSpeed = 321; Require(approved.DifferentialSpeed == -45.25f, "Snapshot aliased accepted input."); f.Unchanged();
                });
                check("differential speed: invalid and duplicate reports cannot replace accepted input", () =>
                {
                    var accepted = (VehicleState)Get(item, "AcceptedVehicleState"); var invalid = VehicleStateStreamPolicy.Copy(accepted);
                    invalid.DifferentialSpeed = 5; Require(!(bool)Call(vehicles, "OnRemoteVehicleState", invalid)!, "Duplicate was accepted.");
                    invalid.Sequence++; invalid.DifferentialSpeed = float.NaN;
                    Require(!(bool)Call(vehicles, "OnRemoteVehicleState", invalid)! && ReferenceEquals(accepted, Get(item, "AcceptedVehicleState")), "Invalid input replaced accepted state.");
                    invalid.DifferentialSpeed = 5; Require((bool)Call(vehicles, "OnRemoteVehicleState", invalid)!, "Invalid report consumed its sequence.");
                });
                check("differential speed: stale owner snapshot is unavailable and handoff begins a fresh input", () =>
                {
                    Set(item, "RemoteEngineUntil", -999f); Require(Call(vehicles, "TryBuildVehicleStateMessage", item, (byte)0) == null, "Stale source reached join snapshot.");
                    mode(true, 2); var next = State(2, 0, 3000); next.DifferentialSpeedAvailable = true; next.DifferentialSpeed = -21;
                    Require((bool)Call(vehicles, "OnRemoteVehicleState", next)!, "Fresh owner rejected.");
                    var snapshot = (VehicleState)Call(vehicles, "TryBuildVehicleStateMessage", item, (byte)0)!;
                    Require(snapshot.DifferentialSpeed == -21 && f.Speed == 777, "Handoff mixed driver inputs or wrote native drivetrain.");
                });
            }
            finally { property.GetSetMethod(true).Invoke(null, new[] { original }); }
        }

        private sealed class DifferentialFixture : IDisposable
        {
            internal readonly List<object> Rows;
            private readonly GameObject _root;
            internal readonly Rigidbody Body;
            internal readonly Behaviour Drive;
            internal readonly PlayMakerFSM Fsm;
            internal readonly FsmStateAction Read;
            internal readonly FsmProperty Property;
            internal readonly FsmFloat Output;
            private readonly List<FsmFloat> _saved = new List<FsmFloat>();
            internal object Item = null!;
            internal DifferentialFixture(bool nativeTargets = false)
            {
                var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                var reader = Activator.CreateInstance(readerType, Members, null, new object[] { File.ReadAllText(Path.Combine(Application.dataPath, "../guest-engine-probe.json")) }, null);
                var rows = (List<object>)((Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null))["fsms"];
                Rows = rows;
                var row = NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Systems/Drivetrain", "Wear");
                _root = new GameObject("CORRIS"); _root.SetActive(false); Body = _root.AddComponent<Rigidbody>(); Body.isKinematic = true; Body.useGravity = false;
                Type? type = null; foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) if ((type = assembly.GetType("Drivetrain")) != null) break;
                Drive = (Behaviour)_root.AddComponent(type ?? throw new InvalidOperationException("Native drivetrain missing."));
                foreach (var behaviour in _root.GetComponents<Behaviour>()) behaviour.enabled = false;
                var obj = Child(Child(Child(_root, "Simulation"), "Systems"), "Drivetrain"); Fsm = NativeBagPartChecks.MakeFsm(obj, row);
                var target = new FsmObject { Name = "Drivetrain", UseVariable = true, ObjectType = type, Value = Drive }; Fsm.FsmVariables.ObjectVariables = new[] { target };
                foreach (string name in new[] { "db_Driveshaft", "db_Gearbox", "db_RearAxle" })
                {
                    var part = _root;
                    if (nativeTargets)
                    {
                        string? path = null;
                        foreach (Dictionary<string, object> variable in (IEnumerable)((Dictionary<string, object>)row["variableDefaults"])["gameObjectVariables"])
                            if ((string)variable["name"] == name) path = (string)((Dictionary<string, object>)variable["value"])["scenePath"];
                        if (path == null) throw new InvalidOperationException("Native saved mount path missing.");
                        string[] pieces = path.Split('/');
                        for (int i = 1; i < pieces.Length; i++)
                        { var existing = part.transform.Find(pieces[i]); part = existing != null ? existing.gameObject : Child(part, pieces[i]); }
                    }
                    else part = Child(_root, name);
                    Fsm.FsmVariables.FindFsmGameObject(name).Value = part;
                    var data = part.AddComponent<PlayMakerFSM>(); data.enabled = false; Set(data, "fsm", new Fsm()); data.FsmName = "Data";
                    data.Fsm.StartState = "Idle"; data.Fsm.States = new[] { new FsmState(data.Fsm) { Name = "Idle", Actions = new FsmStateAction[0] } };
                    var wear = new FsmFloat { Name = "Wear", UseVariable = true, Value = 90 }; _saved.Add(wear); data.FsmVariables.FloatVariables = new[] { wear };
                }
                _root.SetActive(true); Fsm.Fsm.Init(Fsm);
                foreach (var fsm in _root.GetComponentsInChildren<PlayMakerFSM>()) if (fsm != Fsm) NativeBagPartChecks.Start(fsm);
                Output = Fsm.FsmVariables.FindFsmFloat("DiffSpeed");
                Property = new FsmProperty { TargetObject = target, TargetType = type, TargetTypeName = "Drivetrain", PropertyName = "differentialSpeed", setProperty = false, FloatParameter = Output };
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    var state = NativeBagPartChecks.State(Fsm, (string)stateRow["name"]); var actions = new List<FsmStateAction>();
                    foreach (Dictionary<string, object> raw in (IEnumerable)stateRow["actions"])
                    {
                        var parameters = new List<object>(); bool propertyRead = false;
                        foreach (Dictionary<string, object> parameter in (IEnumerable)raw["parameters"])
                            if ((string)parameter["type"] == "FsmProperty") propertyRead = true; else parameters.Add(parameter);
                        var copy = new Dictionary<string, object>(raw); copy["parameters"] = parameters;
                        var action = (FsmStateAction)typeof(NativeBagPartChecks).GetMethod("ReadAction", Static).Invoke(null, new object[] { copy, Fsm });
                        if (propertyRead) Set(action, "targetProperty", Property); actions.Add(action);
                    }
                    state.Actions = actions.ToArray(); foreach (var action in state.Actions) action.Init(state);
                }
                Read = NativeBagPartChecks.State(Fsm, "State 1").Actions[0]; NativeBagPartChecks.Start(Fsm);
                if (nativeTargets) CycleWear(); else NativeBagPartChecks.Fire(Fsm, "State 1");
                Fsm.enabled = true; ResetSaved();
            }
            private static GameObject Child(GameObject parent, string name) { var child = new GameObject(name); child.transform.SetParent(parent.transform, false); return child; }
            internal float Speed => (float)Get(Drive, "differentialSpeed");
            internal void SetSpeed(float speed) => Set(Drive, "differentialSpeed", speed);
            internal void CycleWear()
            {
                FsmExecutionStack.PushFsm(Fsm.Fsm);
                try
                {
                    // Enter through the native boundary without adding a synthetic global transition.
                    typeof(Fsm).GetMethod("SwitchState", Members).Invoke(Fsm.Fsm, new object[] { NativeBagPartChecks.State(Fsm, "State 1") });
                    Fsm.Fsm.UpdateStateChanges();
                }
                finally { FsmExecutionStack.PopFsm(); }
            }
            internal void ResetSaved() { foreach (var value in _saved) value.Value = 90; }
            internal float[] WearValues() { var result = new float[_saved.Count]; for (int i = 0; i < result.Length; i++) result[i] = _saved[i].Value; return result; }
            internal void Unchanged() { foreach (var value in _saved) Require(value.Value == 90, "Telemetry capture wrote saved mount wear."); }
            internal void WithDrive(Action action) { Drive.enabled = true; try { action(); } finally { Drive.enabled = false; } }
            internal VehicleState Capture()
            {
                var value = State(1, 0, 3000); value.DifferentialSpeedAvailable = true; value.DifferentialSpeed = 123;
                WithDrive(() => CallStatic("CaptureDifferentialSpeedTelemetry", Item, value)); return value;
            }
            public void Dispose() { UnityEngine.Object.DestroyImmediate(_root); }
        }
    }
}
