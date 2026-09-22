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
using WinterMP.Net.Transport;

namespace WinterMP.GuestSaveProbe
{
    internal static class VehicleRpmChecks
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        private static readonly Type Vehicles = Core.GetType("WinterMP.Core.Sync.VehicleWorldSync", true);
        private static readonly Type Items = Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true);
        private static readonly Type World = Core.GetType("WinterMP.Core.Sync.WorldSyncManager", true);
        private const uint VehicleId = 0x705170;

        internal static void Run(Action<string, Action> check)
        {
            using (GuestEngineInputChecks.SuspendProjectionForFixture())
            using (var fixture = new Fixture())
            {
                var f = fixture;
                check("vehicle RPM: real Corris without local Revs discovers its native producer", () =>
                {
                    Require(f.Starter.FsmVariables.FindFsmFloat("RPM") == null
                        && f.Fuel.FsmVariables.FindFsmFloat("Revs") == null
                        && f.Rotate.FsmVariables.FindFsmFloat("Revs") == null,
                        "Fixture manufactured a local RPM source absent from the installed car.");
                    f.Probe(); Require(f.Ready && ReferenceEquals(Get(f.Item, "EngineRevsVar"), f.Rpm),
                        "Installed Corris shape did not bind its native global RPM.");
                });
                check("vehicle RPM: native running GetProperty reads the real drivetrain component", () =>
                {
                    f.DriveRpm(2345); f.Fire("Running");
                    Require(f.Rpm.Value == 2345, "Native GetProperty did not read Drivetrain.rpm.");
                    f.DriveRpm(3120); f.Starter.Fsm.Update();
                    Require(f.Rpm.Value == 3120, "Native everyFrame producer did not update RPM.");
                });
                check("vehicle RPM: Corris live publication uses the discovered native source", () =>
                {
                    f.PowerOn(true); f.Publish(); var packet = f.One();
                    Require(packet.State.Rpm == 3120 && packet.State.EngineOn && packet.State.AccOn
                        && packet.Channel == Channel.UnreliableSequenced, "Native RPM did not reach vehicle60.");
                });
                check("vehicle RPM: native cranking speed survives a stopped drivetrain", () =>
                {
                    f.DriveRpm(0); f.Starter.FsmVariables.FindFsmFloat("StarterSpeed").Value = 180;
                    f.Fire("Turn key"); f.Publish();
                    Require(f.Rpm.Value == 180 && f.One().State.Rpm == 180,
                        "The drivetrain field replaced native StarterSpeed while cranking.");
                    f.Starter.FsmVariables.FindFsmFloat("StarterSpeed").Value = 220; f.Starter.Fsm.Update();
                    Require(f.Rpm.Value == 220, "Native cranking everyFrame producer stopped updating.");
                });
                check("vehicle RPM: native wait zero beats a stale gauge and drivetrain reading", () =>
                {
                    f.DriveRpm(4700); f.Gauge.Value = 6500; f.Fire("Wait"); f.PowerOn(false); f.Publish();
                    var packet = f.One();
                    Require(f.Rpm.Value == 0 && packet.State.Rpm == 0 && !packet.State.EngineOn && !packet.State.AccOn
                        && packet.Channel == Channel.ReliableOrdered, "Stopped native output did not produce a reliable OFF.");
                });
                check("vehicle RPM: Corris accessory power retains ownership before the engine runs", () =>
                {
                    f.PowerOn(true); Require((bool)CallStatic("KeepVehicleIgnitionOwnership", f.Item)!, "ACC-only Corris ownership was released.");
                    f.Publish(); Require(f.One().State.Rpm == 0 && !f.One().State.EngineOn && f.One().State.AccOn,
                        "Accessory-only native state became a running engine.");
                    f.PowerOn(false); Require(!(bool)CallStatic("KeepVehicleIgnitionOwnership", f.Item)!, "Stopped Corris retained ignition ownership.");
                });
                check("vehicle RPM: initial host snapshot uses native Corris RPM without fabricated Revs", () =>
                {
                    f.DriveRpm(1675); f.Fire("Running"); f.PowerOn(true);
                    var snapshot = (VehicleState)Call(f.VehicleSync, "TryBuildVehicleStateMessage", f.Item, (byte)0)!;
                    Require(snapshot != null && snapshot.Rpm == 1675 && snapshot.Sequence == VehicleState.SnapshotSequence,
                        "Host join snapshot omitted the real Corris engine source.");
                });
                check("vehicle RPM: observer reception never writes the native global producer", () =>
                {
                    Set(f.Item, "LocallyOwned", false); Set(f.Item, "RemoteOwner", (byte)1);
                    float rpm = f.Rpm.Value, drive = f.ReadDriveRpm();
                    Require((bool)Call(f.VehicleSync, "OnRemoteVehicleState", new VehicleState {
                        VehicleId = VehicleId, OwnerPlayerId = 1, Sequence = 0, Rpm = 4500,
                        Flags = VehicleState.FlagAccOn | VehicleState.FlagEngineOn, FuelLevel = 100, Gear = 2 })!,
                        "Fixture remote stream was rejected.");
                    Require(f.Rpm.Value == rpm && f.ReadDriveRpm() == drive,
                        "Read-only source binding injected remote RPM into native simulation.");
                    Set(f.Item, "LocallyOwned", true); Set(f.Item, "RemoteOwner", (byte)255);
                });
                check("vehicle RPM: aliased dashboard destinations cannot overwrite native RPM", () =>
                {
                    float before = f.Rpm.Value;
                    Set(f.Item, "GaugeRpmVar", f.Rpm); Set(f.Item, "GaugeTachRevsVar", f.Rpm);
                    try
                    {
                        Set(f.Item, "RemoteRpm", 6000f); CallStatic("ApplyRemoteGauges", f.Item);
                        Require(f.Rpm.Value == before, "Observer gauge replay overwrote the source global.");
                    }
                    finally { Set(f.Item, "GaugeRpmVar", f.Gauge); Set(f.Item, "GaugeTachRevsVar", null); }
                });
                check("vehicle RPM: inactive producer does not advertise its stale running value", () =>
                {
                    f.PowerOn(false); float before = f.Rpm.Value; f.Starter.enabled = false;
                    try
                    {
                        f.Publish(); Require(f.One().State.Rpm == 0 && !f.One().State.EngineOn && f.Rpm.Value == before,
                            "Inactive native source was advertised as running or changed by observation.");
                    }
                    finally { f.Starter.Fsm.RestartOnEnable = false; f.Starter.enabled = true; }
                });
                check("vehicle RPM: inactive native object cannot retain running publication", () =>
                {
                    bool restart = f.Starter.Fsm.RestartOnEnable; float before = f.Rpm.Value;
                    f.Starter.Fsm.RestartOnEnable = false; f.Starter.gameObject.SetActive(false);
                    try
                    {
                        f.Publish(); Require(f.One().State.Rpm == 0 && !f.One().State.EngineOn && f.Rpm.Value == before,
                            "Inactive producer object advertised or changed its retained native RPM.");
                    }
                    finally { f.Starter.gameObject.SetActive(true); f.Starter.Fsm.RestartOnEnable = restart; }
                });
                check("vehicle RPM: unstarted native graph cannot advertise stale global RPM", () =>
                {
                    bool started = f.Starter.Fsm.Started; float before = f.Rpm.Value;
                    Property(f.Starter.Fsm, "Started", false);
                    try
                    {
                        f.Publish(); Require(f.One().State.Rpm == 0 && !f.One().State.EngineOn && f.Rpm.Value == before,
                            "Unstarted native graph advertised or changed its retained RPM.");
                    }
                    finally { Property(f.Starter.Fsm, "Started", started); }
                });
                check("vehicle RPM: a foreign drivetrain reference invalidates cached readiness", () =>
                {
                    var foreign = f.MakeDrivetrain("foreign drivetrain");
                    try
                    {
                        f.DriveReference.Value = foreign; f.Probe();
                        Require(!f.Ready && Get(f.Item, "EngineRevsVar") == null, "Cross-car source retained cached readiness.");
                        f.Publish(); Require(f.Capture.Packets.Count == 0, "Invalid cached source still emitted an engine stream.");
                    }
                    finally { f.DriveReference.Value = f.Drivetrain; f.Probe(); }
                    Require(f.Ready, "Restoring owned native reference did not recover.");
                });
                check("vehicle RPM: invalidated source remains protected from observer gauge aliases", () =>
                {
                    float before = f.Rpm.Value; f.DriveReference.Value = null; f.Probe();
                    Set(f.Item, "GaugeRpmVar", f.Rpm); Set(f.Item, "GaugeTachRevsVar", f.Rpm);
                    try
                    {
                        Require(!f.Ready && Get(f.Item, "EngineRevsVar") == null, "Alias fixture did not invalidate source readiness.");
                        Set(f.Item, "RemoteRpm", 7900f); CallStatic("ApplyRemoteGauges", f.Item);
                        Require(f.Rpm.Value == before, "Invalidating the source reopened a native global write through gauges.");
                    }
                    finally
                    {
                        Set(f.Item, "GaugeRpmVar", f.Gauge); Set(f.Item, "GaugeTachRevsVar", null);
                        f.DriveReference.Value = f.Drivetrain; f.Probe();
                    }
                });
                check("vehicle RPM: changed producer destination is refused and can recover", () =>
                {
                    var property = f.RunningProperty; var original = property.FloatParameter;
                    try
                    {
                        property.FloatParameter = new FsmFloat { Name = "RPM", UseVariable = true, Value = 5000 };
                        f.Probe(); Require(!f.Ready, "Disconnected same-name RPM destination was accepted.");
                    }
                    finally { property.FloatParameter = original; f.Probe(); }
                    Require(f.Ready, "Repaired producer destination did not recover.");
                });
                check("vehicle RPM: changed native property cannot masquerade as engine RPM", () =>
                {
                    var property = f.RunningProperty; string name = property.PropertyName;
                    try { property.PropertyName = "throttle"; f.Probe(); Require(!f.Ready, "Changed drivetrain property was accepted."); }
                    finally { property.PropertyName = name; f.Probe(); }
                    Require(f.Ready, "Repaired native property did not recover.");
                });
                check("vehicle RPM: changed native producer cadence is refused", () =>
                {
                    var action = NativeBagPartChecks.State(f.Starter, "Running").Actions[7];
                    var field = action.GetType().GetField("everyFrame"); object value = field.GetValue(action);
                    try { field.SetValue(action, false); f.Probe(); Require(!f.Ready, "Changed native update cadence was accepted."); }
                    finally { field.SetValue(action, value); f.Probe(); }
                    Require(f.Ready, "Restored native cadence did not recover.");
                });
                check("vehicle RPM: local shadow cannot replace the verified global destination", () =>
                {
                    var values = f.Starter.FsmVariables.FloatVariables;
                    try
                    {
                        var changed = new List<FsmFloat>(values) { new FsmFloat { Name = "RPM", UseVariable = true, Value = 8500 } };
                        f.Starter.FsmVariables.FloatVariables = changed.ToArray(); f.Probe();
                        Require(!f.Ready, "Local shadow accepted an ambiguous global RPM identity.");
                    }
                    finally { f.Starter.FsmVariables.FloatVariables = values; f.Probe(); }
                    Require(f.Ready, "Removing the local shadow did not recover.");
                });
                check("vehicle RPM: reinitialized native object variables rebind only matching producers", () =>
                {
                    var variables = f.Starter.FsmVariables.ObjectVariables;
                    var replacement = new FsmObject { Name = "CarDrivetrain", UseVariable = true,
                        ObjectType = f.Drivetrain.GetType(), Value = f.Drivetrain };
                    var properties = new List<FsmProperty>();
                    foreach (var state in f.Starter.Fsm.States)
                        foreach (var action in state.Actions)
                        {
                            var field = action.GetType().GetField("targetProperty");
                            if (field != null && field.GetValue(action) is FsmProperty property
                                && ReferenceEquals(property.TargetObject, f.DriveReference)) properties.Add(property);
                        }
                    try
                    {
                        f.Starter.FsmVariables.ObjectVariables = new[] { replacement }; f.Probe();
                        Require(!f.Ready, "Reinitialized reference silently retained old producer wrappers.");
                        foreach (var property in properties) property.TargetObject = replacement;
                        f.Probe(); Require(f.Ready, "Matching reinitialized source did not rebind.");
                        f.DriveRpm(1925); f.Fire("Running"); f.Publish();
                        Require(f.One().State.Rpm == 1925, "Rebound native producer did not publish its actual value.");
                    }
                    finally
                    {
                        f.Starter.FsmVariables.ObjectVariables = variables;
                        foreach (var property in properties) property.TargetObject = f.DriveReference;
                        f.Probe();
                    }
                });
                check("vehicle RPM: missing catalog invalidates an already recognized Corris", () =>
                {
                    var property = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true).GetProperty("VehicleEngineRpm", Static);
                    object value = property.GetValue(null, null);
                    try { property.GetSetMethod(true).Invoke(null, new object?[] { null }); f.Probe(); Require(!f.Ready, "Missing catalog retained a trusted Corris source."); }
                    finally { property.GetSetMethod(true).Invoke(null, new[] { value }); f.Probe(); }
                    Require(f.Ready, "Restored RPM catalog did not recover.");
                });
                check("vehicle RPM: invalid numeric output defers publication without gauge fallback", () =>
                {
                    float value = f.Rpm.Value;
                    try
                    {
                        f.Rpm.Value = float.NaN; f.Gauge.Value = 8000; f.Probe(); f.Publish();
                        Require(!f.Ready && f.Capture.Packets.Count == 0, "Non-finite native RPM became a valid stream.");
                    }
                    finally { f.Rpm.Value = value; f.Probe(); }
                    Require(f.Ready, "Restored finite source did not recover.");
                });
                check("vehicle RPM: missing native source defers without a global fallback", () =>
                {
                    f.DriveReference.Value = null; f.Probe();
                    Require(!f.Ready && Get(f.Item, "EngineRevsVar") == null, "Null source adopted unrelated global RPM.");
                    f.DriveReference.Value = f.Drivetrain; f.Probe(); Require(f.Ready, "Late native source did not bind.");
                });
                check("vehicle RPM: destroyed native source cannot retain cached readiness", () =>
                {
                    UnityEngine.Object.DestroyImmediate(f.Drivetrain); f.Probe();
                    Require(!f.Ready && Get(f.Item, "EngineRevsVar") == null, "Destroyed drivetrain retained source readiness.");
                    f.ReplaceDrivetrain(); f.Probe(); Require(f.Ready, "Replacement native component did not rebind.");
                });
                check("vehicle RPM: unrelated car never borrows the Corris global source", () => f.CheckOtherCar(false));
                check("vehicle RPM: other cars keep their supported local Revs source", () => f.CheckOtherCar(true));
            }
            using (GuestEngineInputChecks.SuspendProjectionForFixture())
            using (var fixture = new Fixture())
            {
                fixture.Probe();
                CheckStarterHooks(check, fixture);
            }
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly GameObject Car, Controller;
            internal readonly object Item, ItemSync, VehicleSync;
            internal readonly SessionManager Session;
            internal readonly PlayMakerFSM Starter, Fuel, Rotate, Power;
            internal readonly FsmFloat Rpm, Gauge = new FsmFloat { Name = "RPM", UseVariable = true };
            internal readonly FsmObject DriveReference;
            internal readonly CaptureTransport Capture = new CaptureTransport();
            internal Component Drivetrain;
            internal FsmProperty RunningProperty = null!;
            private readonly Type _driveType;
            private readonly object _world;
            private readonly object? _savedSession, _savedWorld;
            private readonly float _savedRpm;
            private readonly List<GameObject> _extras = new List<GameObject>();

            internal Fixture()
            {
                _savedSession = SessionManager.Instance; _savedWorld = World.GetProperty("Instance", Static).GetValue(null, null);
                var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true); catalog.GetMethod("EnsureLoaded", Static).Invoke(null, null);
                Rpm = FsmVariables.GlobalVariables.FindFsmFloat("RPM") ?? throw new InvalidOperationException("Native global RPM missing.");
                _savedRpm = Rpm.Value;
                var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                var reader = Activator.CreateInstance(readerType, Members, null, new object[] { File.ReadAllText(Path.Combine(Application.dataPath, "../vehicle-rpm-probe.json")) }, null);
                var rows = (List<object>)((Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null))["fsms"];
                Controller = new GameObject("native RPM controller"); Controller.SetActive(false);
                Car = new GameObject("CORRIS"); Car.SetActive(false);
                var body = Car.AddComponent<Rigidbody>(); body.useGravity = false; body.isKinematic = true;
                _driveType = NativeType("Drivetrain"); Drivetrain = AddDrivetrain(Car);
                Session = Controller.AddComponent<SessionManager>(); Session.enabled = false;
                var world = Controller.AddComponent(World); _world = world; ((Behaviour)world).enabled = false;
                var bridge = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.WorldSyncBridge", true), Members, null,
                    new object[] { world, new Dictionary<PlayMakerFSM, bool>() }, null);
                ItemSync = Activator.CreateInstance(Items, Members, null, new[] { bridge }, null);
                VehicleSync = Activator.CreateInstance(Vehicles, Members, null, new[] { bridge, ItemSync }, null);
                Call(ItemSync, "BindVehicles", VehicleSync); Call(bridge, "BindItems", ItemSync);
                Set(world, "_bridge", bridge); Set(world, "_items", ItemSync); Set(world, "_vehicles", VehicleSync); Set(world, "_syncReady", true);
                var player = Child(Controller, "local player"); player.transform.position = new Vector3(10000, 0, 0); Property(world, "LocalPlayer", player.transform);
                Item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                Set(Item, "Id", VehicleId); Set(Item, "Body", body); Set(Item, "IsVehicle", true); Set(Item, "Path", "CORRIS");
                Set(Item, "LocallyOwned", true); Set(Item, "RemoteOwner", (byte)255); Set(Item, "ClimateReady", true); Set(Item, "GaugeRpmVar", Gauge);
                ((IDictionary)Get(ItemSync, "_items")).Add(VehicleId, Item);
                Set(Session, "_transport", Capture); Set(Session, "_hostPeer", new PeerId(999));
                ((IDictionary)Get(Session, "_playersByPeer")).Add(new PeerId(1001), new RemotePlayer { Peer = new PeerId(1001), PlayerId = 1 });
                var starterRow = NativeBagPartChecks.Find(rows, "CORRIS/Simulation/STARTERxCorris", "Starter");
                var fuelRow = NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Engine/Fuel", "FuelLine");
                var rotateRow = NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Engine/SymptomsEngine/Animations", "RotateEngine");
                Starter = NativeBagPartChecks.MakeFsm(PathObject(Car, (string)starterRow["path"]), starterRow);
                Fuel = NativeBagPartChecks.MakeFsm(PathObject(Car, (string)fuelRow["path"]), fuelRow);
                Rotate = NativeBagPartChecks.MakeFsm(PathObject(Car, (string)rotateRow["path"]), rotateRow);
                DriveReference = new FsmObject { Name = "CarDrivetrain", UseVariable = true, ObjectType = _driveType, Value = Drivetrain };
                Starter.FsmVariables.ObjectVariables = new[] { DriveReference };
                Controller.SetActive(true); Car.SetActive(true);
                Load(Starter, starterRow); Load(Fuel, fuelRow);
                Power = (PlayMakerFSM)typeof(VehicleStateChecks).GetMethod("MakePower", Static).Invoke(null, new object[] { PathObject(Car, "CORRIS/Simulation") });
                NativeBagPartChecks.Start(Power); NativeBagPartChecks.Start(Starter);
                StaticProperty(typeof(SessionManager), "Instance", Session); StaticProperty(World, "Instance", world);
                Property(Session, "IsHost", true); Property(Session, "State", SessionState.Hosting); Property(Session, "LocalPlayerId", (byte)0);
            }
            internal bool Ready => (bool)Get(Item, "SystemsReady");
            internal void Probe() { Set(Item, "NextSystemsProbeAt", 0f); CallStatic("EnsureVehicleSystemsProbe", Item); }
            internal void Fire(string state) => NativeBagPartChecks.Fire(Starter, state);
            internal void PowerOn(bool on) => NativeBagPartChecks.Fire(Power, on ? "ON" : "OFF");
            internal void DriveRpm(float value) => _driveType.GetField("rpm").SetValue(Drivetrain, value);
            internal float ReadDriveRpm() => (float)_driveType.GetField("rpm").GetValue(Drivetrain);
            internal void Publish() { Capture.Packets.Clear(); Set(Item, "NextVehicleStateAt", 0f); Call(VehicleSync, "UpdateVehicleStates", Session); }
            internal Packet One() { Require(Capture.Packets.Count == 1, "Expected one native engine packet; got " + Capture.Packets.Count + "."); return Capture.Packets[0]; }
            internal Component MakeDrivetrain(string name)
            {
                var obj = new GameObject(name); obj.SetActive(false); _extras.Add(obj);
                var drive = AddDrivetrain(obj); obj.SetActive(true); return drive;
            }
            internal void ReplaceDrivetrain() { Drivetrain = AddDrivetrain(Car); DriveReference.Value = Drivetrain; }
            private Component AddDrivetrain(GameObject obj)
            {
                var drive = obj.AddComponent(_driveType); ((Behaviour)drive).enabled = false;
                ((Behaviour)obj.GetComponent(NativeType("Axles"))).enabled = false; return drive;
            }
            internal void CheckOtherCar(bool legacy)
            {
                var obj = new GameObject(legacy ? "legacy vehicle" : "unrelated vehicle"); obj.SetActive(false);
                var body = obj.AddComponent<Rigidbody>(); body.isKinematic = true;
                var item = Activator.CreateInstance(Item.GetType(), true); Set(item, "Body", body); Set(item, "IsVehicle", true); Set(item, "Path", obj.name);
                Set(item, "ClimateReady", true); Set(item, "GaugeRpmVar", new FsmFloat(5000));
                try
                {
                    obj.SetActive(true);
                    var power = (PlayMakerFSM)typeof(VehicleStateChecks).GetMethod("MakePower", Static).Invoke(null, new object[] { obj });
                    NativeBagPartChecks.Start(power);
                    FsmFloat? revs = null;
                    if (legacy)
                    {
                        var source = obj.AddComponent<PlayMakerFSM>(); source.enabled = false; Set(source, "fsm", new Fsm()); source.Fsm.Name = "FuelLine";
                        revs = new FsmFloat { Name = "Revs", UseVariable = true, Value = 1300 }; source.FsmVariables.FloatVariables = new[] { revs };
                    }
                    CallStatic("EnsureVehicleSystemsProbe", item);
                    Require((bool)Get(item, "SystemsReady") == legacy && (legacy ? ReferenceEquals(Get(item, "EngineRevsVar"), revs) : Get(item, "EngineRevsVar") == null),
                        "Vehicle source scope or legacy local Revs behavior changed.");
                }
                finally { UnityEngine.Object.DestroyImmediate(obj); }
            }
            private void Load(PlayMakerFSM fsm, Dictionary<string, object> row)
            {
                var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
                var protection = catalog.GetProperty("GuestEngineProtection", Static).GetValue(null, null);
                var guardedActions = new HashSet<string>();
                foreach (object writer in (IEnumerable)Get(protection, "Writers"))
                    if ((string)Get(writer, "Path") == (string)row["path"] && (string)Get(writer, "Fsm") == fsm.FsmName)
                        foreach (object action in (IEnumerable)Get(writer, "Actions"))
                            guardedActions.Add((string)Get(action, "State") + "::" + (int)Get(action, "Index"));
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    string name = (string)stateRow["name"]; var state = NativeBagPartChecks.State(fsm, name); var raw = (List<object>)stateRow["actions"];
                    var actions = new FsmStateAction[raw.Count];
                    for (int index = 0; index < raw.Count; index++)
                    {
                        var actionRow = (Dictionary<string, object>)raw[index]; Dictionary<string, object>? property = null;
                        bool rpm = false;
                        foreach (Dictionary<string, object> parameter in (IEnumerable)actionRow["parameters"])
                        {
                            if ((string)parameter["type"] == "FsmProperty")
                            {
                                var value = (Dictionary<string, object>)parameter["value"];
                                if ((string)value["PropertyName"] == "rpm") { property = value; rpm = true; }
                            }
                            if ((string)parameter["type"] == "FsmFloat" && (string)parameter["field"] == "floatVariable"
                                && (string)parameter["name"] == "RPM") rpm = true;
                        }
                        bool guarded = guardedActions.Contains(name + "::" + index);
                        if (!rpm && !guarded) { actions[index] = new Quiet(); continue; }
                        var importRow = new Dictionary<string, object>(actionRow);
                        if (property != null)
                        {
                            var parameters = new List<object>();
                            foreach (Dictionary<string, object> parameter in (IEnumerable)actionRow["parameters"])
                                if ((string)parameter["type"] != "FsmProperty") parameters.Add(parameter);
                            importRow["parameters"] = parameters;
                        }
                        var action = (FsmStateAction)typeof(NativeBagPartChecks).GetMethod("ReadAction", Static).Invoke(null, new object[] { importRow, fsm });
                        foreach (var field in action.GetType().GetFields())
                            if (field.FieldType == typeof(FsmFloat) && field.GetValue(action) is FsmFloat value && value.UseVariable && value.Name == "RPM") field.SetValue(action, Rpm);
                        if (property != null)
                        {
                            var value = (Dictionary<string, object>)property["FloatParameter"];
                            var binding = new FsmProperty { TargetObject = DriveReference, TargetType = _driveType,
                                TargetTypeName = (string)property["TargetTypeName"], PropertyName = (string)property["PropertyName"],
                                setProperty = Convert.ToBoolean(property["setProperty"]),
                                FloatParameter = Convert.ToBoolean(value["useVariable"]) ? Rpm : new FsmFloat(Convert.ToSingle(value["value"])) };
                            action.GetType().GetField("targetProperty").SetValue(action, binding);
                            if (name == "Running") RunningProperty = binding;
                        }
                        actions[index] = action;
                    }
                    state.Actions = actions; state.Transitions = new FsmTransition[0];
                    foreach (var action in actions) action.Init(state);
                }
            }
            public void Dispose()
            {
                Set(Session, "_transport", null); ((IDictionary)Get(Session, "_playersByPeer")).Clear();
                Property(Session, "State", SessionState.Idle); Set(_world, "_syncReady", false);
                UnityEngine.Object.DestroyImmediate(Car); UnityEngine.Object.DestroyImmediate(Controller);
                foreach (var extra in _extras) UnityEngine.Object.DestroyImmediate(extra);
                StaticProperty(typeof(SessionManager), "Instance", _savedSession); StaticProperty(World, "Instance", _savedWorld);
                Rpm.Value = _savedRpm;
            }
        }

        private static void CheckStarterHooks(Action<string, Action> check, Fixture f)
        {
            var states = new List<FsmState>();
            var originals = new List<FsmStateAction[]>();
            var hook = Core.GetType("WinterMP.Core.Sync.FsmHook", true).GetMethod("OnStateEnter", Static, null,
                new[] { typeof(PlayMakerFSM), typeof(string), typeof(Action) }, null);
            try
            {
                // RegisterStarter prepends these same observation hooks in a live
                // session, both before and after the native RPM binding can exist.
                foreach (string name in new[] { "Running", "Stall engine", "Start engine", "Crank up" })
                {
                    var state = NativeBagPartChecks.State(f.Starter, name);
                    states.Add(state); originals.Add(state.Actions);
                    Require((bool)hook.Invoke(null, new object[] { f.Starter, name, (Action)(() => { }) }), "Starter hook installation failed.");
                }
                check("vehicle RPM: registered starter hooks preserve an existing RPM binding", () =>
                { f.Probe(); Require(f.Ready, "Our starter hooks invalidated native RPM."); });
                check("vehicle RPM: native RPM can bind after starter registration", () =>
                { Set(f.Item, "NativeEngineRpm", null); f.Probe(); Require(f.Ready, "Registered starter prevented first RPM binding."); });
                check("vehicle RPM: starter hooks still allow native RPM publication", () =>
                {
                    f.DriveRpm(3120); f.Fire("Running");
                    Require(f.Rpm.Value == 3120, "Registered starter native RPM was " + f.Rpm.Value + ".");
                    f.PowerOn(true);
                    Require(f.Rpm.Value == 3120, "Power-on altered registered starter RPM to " + f.Rpm.Value + ".");
                    f.Publish();
                    Require(f.Rpm.Value == 3120, "Publication altered registered starter RPM to " + f.Rpm.Value + ".");
                    Require(f.Ready, "Registered starter lost native RPM readiness.");
                    var packet = f.One();
                    Require(Math.Abs(packet.State.Rpm - 3120) < 1, "Registered starter published RPM " + packet.State.Rpm + ".");
                });
                check("vehicle RPM: foreign leading actions cannot masquerade as our starter hooks", () =>
                {
                    var state = states[0]; var hooked = state.Actions;
                    var changed = new FsmStateAction[hooked.Length + 1]; changed[0] = new Quiet();
                    Array.Copy(hooked, 0, changed, 1, hooked.Length);
                    try { state.Actions = changed; f.Probe(); Require(!f.Ready, "Unknown leading action escaped RPM signature validation."); }
                    finally { state.Actions = hooked; f.Probe(); }
                });
            }
            finally
            {
                for (int i = 0; i < states.Count; i++) states[i].Actions = originals[i];
                f.Probe();
            }
        }

        private sealed class Quiet : FsmStateAction { public override void OnEnter() { Finish(); } }
        private sealed class Packet { internal VehicleState State = null!; internal Channel Channel; }
        private sealed class CaptureTransport : ITransport
        {
            internal readonly List<Packet> Packets = new List<Packet>();
            public bool IsHost => true;
            public event Action<PeerId>? PeerConnected { add { } remove { } }
            public event Action<PeerId, string>? PeerDisconnected { add { } remove { } }
            public event Action<PeerId, byte[], Channel>? PacketReceived { add { } remove { } }
            public void Send(PeerId peer, byte[] data, Channel channel) => Send(peer, data, data.Length, channel);
            public void Send(PeerId peer, byte[] data, int length, Channel channel)
            {
                var copy = new byte[length]; Array.Copy(data, copy, length);
                if (PacketCodec.Decode(copy) is VehicleState state) Packets.Add(new Packet { State = state, Channel = channel });
            }
            public void Update() { }
            public void Dispose() { }
        }
        private static GameObject Child(GameObject parent, string name) { var obj = new GameObject(name); obj.transform.SetParent(parent.transform, false); return obj; }
        private static GameObject PathObject(GameObject root, string path)
        {
            var current = root; var parts = path.Split('/');
            for (int i = 1; i < parts.Length; i++) { var found = current.transform.Find(parts[i]); current = found != null ? found.gameObject : Child(current, parts[i]); }
            return current;
        }
        private static Type NativeType(string name)
        { foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) { var type = assembly.GetType(name); if (type != null) return type; } throw new InvalidOperationException("Missing native type: " + name); }
        private static object Get(object target, string field) => target.GetType().GetField(field, Members).GetValue(target);
        private static void Set(object target, string field, object? value) => target.GetType().GetField(field, Members).SetValue(target, value);
        private static void Property(object target, string name, object? value) => target.GetType().GetProperty(name, Members).SetValue(target, value, null);
        private static void StaticProperty(Type target, string name, object? value) => target.GetProperty(name, Static).SetValue(null, value, null);
        private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Members).Invoke(target, args);
        private static object? CallStatic(string name, params object?[] args) => Vehicles.GetMethod(name, Static).Invoke(null, args);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
