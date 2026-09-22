using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Transport;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        private static readonly Type Vehicles = Core.GetType("WinterMP.Core.Sync.VehicleWorldSync", true);
        private static readonly Type Items = Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true);
        private static readonly Type World = Core.GetType("WinterMP.Core.Sync.WorldSyncManager", true);
        private const uint VehicleId = 0x705160;

        internal static void Run(Action<string, Action> check)
        {
            var savedSession = SessionManager.Instance;
            var savedWorld = World.GetProperty("Instance", Static).GetValue(null, null);
            var controller = new GameObject("vehicle stream probe"); controller.SetActive(false);
            var car = new GameObject("stream probe vehicle"); car.SetActive(false);
            var body = car.AddComponent<Rigidbody>(); body.useGravity = false; body.isKinematic = true;
            var session = controller.AddComponent<SessionManager>(); session.enabled = false;
            var world = controller.AddComponent(World); ((Behaviour)world).enabled = false;
            var bridgeType = Core.GetType("WinterMP.Core.Sync.WorldSyncBridge", true);
            var bridge = Activator.CreateInstance(bridgeType, Members, null, new object[] { world, new Dictionary<PlayMakerFSM, bool>() }, null);
            var items = Activator.CreateInstance(Items, Members, null, new[] { bridge }, null);
            var vehicles = Activator.CreateInstance(Vehicles, Members, null, new[] { bridge, items }, null);
            Call(items, "BindVehicles", vehicles); Call(bridge, "BindItems", items);
            Set(world, "_bridge", bridge); Set(world, "_items", items); Set(world, "_vehicles", vehicles); Set(world, "_syncReady", true);
            var player = new GameObject("stream probe local player"); player.transform.SetParent(controller.transform, false);
            player.transform.position = new Vector3(10000, 0, 0); SetProperty(world, "LocalPlayer", player.transform);
            var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
            Set(item, "Id", VehicleId); Set(item, "Body", body); Set(item, "IsVehicle", true); Set(item, "Path", "stream probe vehicle");
            Set(item, "SystemsReady", true); Set(item, "ClimateReady", true);
            var rpm = new FsmFloat { Name = "Revs", UseVariable = true };
            var gaugeRpm = new FsmFloat { Name = "RPM", UseVariable = true };
            var fuel = new FsmFloat { Name = "FuelLevel", UseVariable = true, Value = 50 };
            var gear = new FsmInt { Name = "Gear", UseVariable = true, Value = 2 };
            Set(item, "EngineRevsVar", rpm); Set(item, "GaugeRpmVar", gaugeRpm);
            Set(item, "FuelTankLevelVar", fuel); Set(item, "FuelTankCapacityVar", new FsmFloat(100));
            Set(item, "GaugeSpeedVar", new FsmFloat(12.3f)); Set(item, "GearVar", gear);
            var power = MakePower(car); Set(item, "ElectricityPowerFsm", power);
            var drivetrainObject = new GameObject("Drivetrain"); drivetrainObject.transform.SetParent(car.transform, false);
            var gears = drivetrainObject.AddComponent<PlayMakerFSM>(); gears.enabled = false; Set(gears, "fsm", new Fsm());
            gears.Fsm.Name = "Gears"; gears.FsmVariables.IntVariables = new[] { gear };
            gears.Fsm.StartState = "Idle";
            gears.Fsm.States = new[] { new FsmState(gears.Fsm) { Name = "Idle", Actions = new FsmStateAction[0] } };
            ((IDictionary)Get(items, "_items")).Add(VehicleId, item);
            var capture = new CaptureTransport(); Set(session, "_transport", capture); Set(session, "_hostPeer", new PeerId(999));
            var peers = (IDictionary)Get(session, "_playersByPeer");
            var first = new PeerId(1001); var second = new PeerId(1002);
            peers.Add(first, new RemotePlayer { Peer = first, PlayerId = 1, Position = Vector3.zero, LastTransformTime = Time.unscaledTime });
            peers.Add(second, new RemotePlayer { Peer = second, PlayerId = 2, Position = Vector3.zero, LastTransformTime = Time.unscaledTime });
            try
            {
                controller.SetActive(true); car.SetActive(true); NativeBagPartChecks.Start(power);
                SetStaticProperty(typeof(SessionManager), "Instance", session); SetStaticProperty(World, "Instance", world);
                Action<bool, byte> mode = (host, owner) =>
                {
                    SetProperty(session, "IsHost", host); SetProperty(session, "State", host ? SessionState.Hosting : SessionState.Connected);
                    SetProperty(session, "LocalPlayerId", host ? (byte)0 : (byte)3); Set(item, "RemoteOwner", owner);
                };
                Action<float, bool> sample = (revs, acc) =>
                { rpm.Value = gaugeRpm.Value = revs; NativeBagPartChecks.Fire(power, acc ? "ON" : "OFF"); };
                Action reset = () =>
                {
                    Call(vehicles, "ClearVehicleStateStreams"); ConnectionQuality.Instance.Reset(); capture.Packets.Clear(); mode(true, 255);
                    Set(item, "LocallyOwned", false); Set(item, "RemoteElectricsApplied", false); Set(item, "RemoteEngineOn", false);
                    Set(item, "RemoteAccOn", false); Set(item, "RemoteEngineUntil", -999f); Set(item, "RemoteEngineAudioSearched", true);
                    Set(item, "RemoteBlinkerLeft", false); Set(item, "RemoteBlinkerRight", false); Set(item, "RemoteHazard", false);
                    Set(item, "LastRemoteAt", -999f); Set(item, "LastRemoteReleaseAt", -999f);
                    Set(item, "LastRemoteSequenceOwner", (byte)255); Set(item, "LastRemoteSequence", (ushort)0);
                    Set(item, "RemoteVehicleStream", false); Set(item, "RemoteIsDriver", false); Set(item, "LocalDriveActive", false);
                    Set(item, "LastMovedAt", -999f); Set(item, "LastPosition", body.position); Set(item, "NextSendAt", 0f);
                    Set(item, "SystemsReady", true); Set(item, "NextSystemsProbeAt", 0f); Set(item, "GearVar", gear);
                    Set(item, "ElectricityPowerFsm", power); power.gameObject.name = "Electricity";
                    fuel.Value = 50; gear.Value = 2; sample(0, false);
                    player.transform.SetParent(controller.transform, false); player.transform.position = new Vector3(10000, 0, 0);
                };
                Func<VehicleState, bool> receive = state => (bool)Call(vehicles, "OnRemoteVehicleState", state)!;
                Action<ushort> rpmIs = expected => Require((float)Get(item, "RemoteRpm") == expected, "Wrong accepted engine reading.");
                RunTemperatureChecks(check, item, vehicles, session, capture, reset, mode, sample);
                RunWearChecks(check, item, vehicles, session, reset, mode);
                RunHeatChecks(check, item, vehicles, session, reset, mode);
                RunSpeedChecks(check, item, vehicles, session, reset, mode);
                RunDifferentialSpeedChecks(check, item, vehicles, session, capture, reset, mode);
                RunHostDrivetrainWearChecks(check, item, vehicles, session, reset, mode);
                RunDrivetrainStateChecks(check, item, vehicles, session, capture, reset, mode);
                RunDrivetrainConsumerChecks(check, item, vehicles, session, reset, mode);
                RunEngineTemperatureChecks(check, item, vehicles, session, reset, mode);
                RunCabinTemperatureChecks(check, item, vehicles, session, reset, mode);
                RunElectricalTemperatureChecks(check, item, vehicles, session, reset, mode);
                RunElectricalRpmChecks(check, item, vehicles, session, reset, mode);
                RunWindowIceChecks(check, item, vehicles, session, capture, reset, mode);
                RunClimateOwnershipChecks(check, item, vehicles, session, capture, reset);
                RunClimateOccupancyChecks(check, item, vehicles, session, reset);
                RunCondensationChecks(check, item, vehicles, session, capture, reset);
                RunPassengerCondensationChecks(check, item, vehicles, session, capture, reset);
                RunBodyWarmthChecks(check, session, capture, reset);
                RunPassengerHeatingChecks(check, item, vehicles, session, capture, reset);
                RunPassengerLifecycleChecks(check, session, capture, reset);
                RunPassengerDiscoveryChecks(check, session, capture, reset);
                RunTirePressureChecks(check, item, vehicles, session, capture, reset);
                RunConditionStreamChecks(check, item, vehicles, session, capture, reset);
                RunWheelHealthReads(check, item, vehicles, session, capture, reset);
                RunWheelRimChecks(check, item, vehicles, session, capture, reset);
                RunWheelHealthStateChecks(check, item, vehicles, session, capture, reset);
                RunConditionClaimChecks(check, item, vehicles, session, capture, reset);
                RunConditionReleaseChecks(check, item, vehicles, session, capture, reset);
                RunParkedConditionChecks(check, item, vehicles, session, capture, reset);
                RunPressurePhysicsChecks(check, item, vehicles, session, capture, reset);
                RunConditionReadinessChecks(check, item, vehicles, session, capture, reset);
                RunGearboxConditionChecks(check, item, vehicles, session, capture, reset);
                check("vehicle state: first zero sequence reaches native observer presentation", () =>
                { reset(); mode(true, 1); Require(receive(State(1, 0, 1200)), "First sender zero rejected."); rpmIs(1200); Require(power.ActiveStateName == "ON", "Accepted ignition did not enter native Power ON."); });
                check("vehicle state: same-owner duplicate cannot change fuel or native ignition", () =>
                {
                    float before = fuel.Value; var duplicate = State(1, 0, 0); duplicate.FuelLevel = 0;
                    Require(!receive(duplicate) && fuel.Value == before && power.ActiveStateName == "ON", "Duplicate changed native observer state."); rpmIs(1200);
                });
                check("vehicle state: low sequence from a new driver is accepted", () =>
                { reset(); mode(true, 1); Require(receive(State(1, 500, 1200)), "First driver rejected."); mode(true, 2); Require(receive(State(2, 1, 1800)), "Driver handoff reused the previous sequence space."); rpmIs(1800); });
                check("vehicle state: previous driver cannot overwrite a new owner", () =>
                { Require(!receive(State(1, 501, 6000)), "Former owner overwrote its successor."); rpmIs(1800); });
                check("vehicle state: returning driver retains its earlier sequence baseline", () =>
                { mode(true, 1); Require(!receive(State(1, 499, 7000)), "Returning driver replayed stale data."); Require(receive(State(1, 501, 1300)), "Returning driver fresh sequence rejected."); rpmIs(1300); });
                check("vehicle state: host rejects guest sentinel and unowned observer claims", () =>
                {
                    Require(!receive(State(1, VehicleState.SnapshotSequence, 4000)), "Guest used the host snapshot sentinel.");
                    mode(true, 255); Require(!(bool)Call(vehicles, "TryAcceptGuestVehicleState", State(1, 502, 4000), (byte)1)!
                        && !receive(State(1, 502, 4000)), "Nearby unowned observer acquired ignition authority.");
                });
                check("vehicle state: local driver ignores live and snapshot presentation", () =>
                {
                    reset(); mode(false, 255); Set(item, "LocallyOwned", true); sample(2400, true); float before = fuel.Value;
                    Require(!receive(State(0, 1, 0)) && !receive(State(0, VehicleState.SnapshotSequence, 0))
                        && fuel.Value == before && rpm.Value == 2400 && power.ActiveStateName == "ON" && gear.Value == 2,
                        "Remote view overwrote the active simulator.");
                });
                check("vehicle state: seated driver is protected before its ownership claim", () =>
                {
                    reset(); mode(false, 0); player.transform.SetParent(car.transform, false);
                    Require(!receive(State(0, VehicleState.SnapshotSequence, 5000)) && Get(item, "AcceptedVehicleState") == null,
                        "Snapshot ran before the seated driver's first claim."); player.transform.SetParent(controller.transform, false);
                });
                check("vehicle state: host snapshot never advances a live sender baseline", () =>
                {
                    reset(); mode(false, 255); Require(receive(State(0, 100, 1000)), "Host stream rejected.");
                    Require(receive(State(0, VehicleState.SnapshotSequence, 1600)), "Host parked snapshot rejected.");
                    Require(!receive(State(0, 99, 3000)) && receive(State(0, 101, 1100)), "Snapshot rewrote live dedup baseline."); rpmIs(1100);
                });
                check("vehicle state: guest-owned observer rejects a host snapshot", () =>
                { mode(false, 2); Require(!receive(State(0, VehicleState.SnapshotSequence, 6000)), "Host snapshot replaced an active guest source."); rpmIs(1100); });
                check("vehicle state: engine snapshot clones accepted guest state including gear", () =>
                {
                    reset(); mode(true, 1); var live = State(1, 20, 2700); live.Gear = 6; live.FuelLevel = 83; live.CoolantTemp = 190;
                    Require(receive(live), "Guest source rejected."); sample(10, false); fuel.Value = 99; gear.Value = 0;
                    var snapshot = (VehicleState)Call(vehicles, "TryBuildVehicleStateMessage", item, (byte)0)!;
                    if (snapshot == null) throw new InvalidOperationException("Accepted guest source did not produce a snapshot.");
                    Require(snapshot.Rpm == 2700 && snapshot.Gear == 6 && snapshot.FuelLevel == 83 && snapshot.CoolantTemp == 190
                        && snapshot.OwnerPlayerId == 0 && snapshot.Sequence == VehicleState.SnapshotSequence && (ushort)Get(item, "OutVehicleStateSequence") == 0,
                        "Guest-owned snapshot read host-native state or advanced the live counter.");
                    snapshot.Rpm = 9000; live.Rpm = 8000;
                    Require(((VehicleState)Get(item, "AcceptedVehicleState")).Rpm == 2700, "Snapshot or caller mutated retained state.");
                });
                check("vehicle state: missing mismatched or expired guest views defer snapshots", () =>
                {
                    reset(); mode(true, 1); Require(Call(vehicles, "TryBuildVehicleStateMessage", item, (byte)0) == null, "Missing accepted source fell back to native host state.");
                    Require(receive(State(1, 1, 2000)), "Source rejected."); mode(true, 2);
                    Require(Call(vehicles, "TryBuildVehicleStateMessage", item, (byte)0) == null, "Previous owner's cache fed a new driver snapshot.");
                    mode(true, 1); Set(item, "RemoteEngineUntil", -1f);
                    Require(Call(vehicles, "TryBuildVehicleStateMessage", item, (byte)0) == null, "Expired guest source fed a snapshot.");
                });
                check("vehicle state: native host snapshot preserves selected gear", () =>
                { reset(); sample(1300, true); gear.Value = 4; var snapshot = (VehicleState)Call(vehicles, "TryBuildVehicleStateMessage", item, (byte)0)!; Require(snapshot.Gear == 5 && snapshot.Rpm == 1300, "Native snapshot dropped gear or engine reading."); });
                check("vehicle state: first cold off snapshot discovers and applies selected gear", () =>
                {
                    reset(); mode(false, 255); Set(item, "SystemsReady", false); Set(item, "GearVar", null);
                    var snapshot = State(0, VehicleState.SnapshotSequence, 0); snapshot.Gear = 5;
                    Require(receive(snapshot) && ReferenceEquals(Get(item, "GearVar"), gear) && gear.Value == 4,
                        "Cold parked OFF bound its native gear after applying the only snapshot.");
                });
                check("vehicle state: delayed power binding retries accepted ignition without a new packet", () =>
                {
                    reset(); mode(false, 255); power.gameObject.name = "late electricity";
                    Set(item, "SystemsReady", false); Set(item, "ElectricityPowerFsm", null);
                    Require(receive(State(0, VehicleState.SnapshotSequence, 1600)) && !(bool)Get(item, "HasRemoteElectricsState"),
                        "Missing native power was recorded as successfully applied.");
                    power.gameObject.name = "Electricity"; Set(item, "NextSystemsProbeAt", 0f); Set(item, "NextRemoteElectricsAttemptAt", 0f);
                    CallStatic("UpdateRemoteEngineAudio", item, Time.unscaledTime);
                    Require(power.ActiveStateName == "ON" && (bool)Get(item, "HasRemoteElectricsState"), "Cached ON never retried after native binding appeared.");
                });
                check("vehicle state: delayed first off snapshot extinguishes native saved ignition", () =>
                {
                    reset(); mode(false, 255); sample(0, true); power.gameObject.name = "late electricity";
                    Set(item, "SystemsReady", false); Set(item, "ElectricityPowerFsm", null);
                    Require(receive(State(0, VehicleState.SnapshotSequence, 0)) && power.ActiveStateName == "ON"
                        && !(bool)Get(item, "HasRemoteElectricsState"), "Unknown OFF was treated as a completed transition.");
                    power.gameObject.name = "Electricity"; Set(item, "NextSystemsProbeAt", 0f); Set(item, "NextRemoteElectricsAttemptAt", 0f);
                    CallStatic("UpdateRemoteEngineAudio", item, Time.unscaledTime);
                    Require(power.ActiveStateName == "OFF" && (bool)Get(item, "HasRemoteElectricsState"), "Late native binding never applied cached OFF.");
                });
                RunElectricityRetryChecks(check, item, power, reset, mode, receive);
                check("vehicle state: observers never republish remote ignition", () =>
                {
                    reset(); mode(false, 0); Require(receive(State(0, 1, 1800)), "Host view rejected."); gaugeRpm.Value = 1800;
                    Call(vehicles, "UpdateVehicleStates", session); Require(capture.Packets.Count == 0, "Guest observer echoed applied ACC/RPM.");
                    mode(true, 1); Call(vehicles, "UpdateVehicleStates", session); Require(capture.Packets.Count == 0, "Host echoed a delegated driver's ignition.");
                });
                check("vehicle state: stale dashboard RPM cannot hold local ignition after native stop", () =>
                {
                    reset(); Set(item, "LocallyOwned", true); gaugeRpm.Value = 5000; rpm.Value = 0;
                    Require(!(bool)CallStatic("KeepVehicleIgnitionOwnership", item)!, "Presentation RPM overruled stopped native engine.");
                });
                check("vehicle state: expired transform lease cannot turn observer ACC into host authority", () =>
                {
                    reset(); mode(true, 1); Require(receive(State(1, 1, 1800)), "Driver source rejected.");
                    mode(true, 255); Call(vehicles, "UpdateVehicleStates", session);
                    Require(capture.Packets.Count == 0 && power.ActiveStateName == "ON", "Released pose lease made host echo accepted guest ignition.");
                });
                check("vehicle state: local ignition holds ownership while the driver is out", () =>
                {
                    reset(); Set(item, "LocallyOwned", true); sample(0, true);
                    Require((bool)CallStatic("KeepVehicleIgnitionOwnership", item)!, "ACC-only ownership was released.");
                    sample(1400, false); Require((bool)CallStatic("KeepVehicleIgnitionOwnership", item)!, "Idling ownership was released.");
                });
                check("vehicle state: local live counter skips the snapshot sentinel", () =>
                {
                    reset(); mode(false, 255); Set(item, "LocallyOwned", true); Set(item, "OutVehicleStateSequence", (ushort)65534); sample(1400, true);
                    Call(vehicles, "UpdateVehicleStates", session); var packet = SingleVehicle(capture);
                    Require(((VehicleState)packet.Message).Sequence == 0 && packet.Channel == Channel.UnreliableSequenced, "Live counter emitted snapshot sentinel.");
                });
                check("vehicle state: parked host sends one reliable off transition", () =>
                {
                    reset(); sample(1200, true); Call(vehicles, "UpdateVehicleStates", session); capture.Packets.Clear();
                    sample(0, false); Set(item, "NextVehicleStateAt", 0f); Call(vehicles, "UpdateVehicleStates", session);
                    Require(capture.Packets.Count == 2 && capture.Packets[0].Message is VehicleState off && !off.EngineOn && !off.AccOn
                        && capture.Packets[0].Channel == Channel.ReliableOrdered, "Parked off transition was skipped or unreliable.");
                    capture.Packets.Clear(); Set(item, "NextVehicleStateAt", 0f); Call(vehicles, "UpdateVehicleStates", session);
                    Require(capture.Packets.Count == 0, "Parked stopped source kept echoing OFF.");
                });
                check("vehicle state: final off bypasses the ordinary send timer", () =>
                {
                    reset(); mode(false, 255); Set(item, "LocallyOwned", true); Set(item, "NextVehicleStateAt", float.MaxValue);
                    Call(vehicles, "SendFinalVehicleState", session, item); var packet = SingleVehicle(capture);
                    Require(packet.Channel == Channel.ReliableOrdered && !((VehicleState)packet.Message).AccOn, "Final OFF was rate limited.");
                });
                check("vehicle state: item release sends reliable off before final vehicle pose", () =>
                {
                    reset(); mode(false, 255); Set(item, "LocallyOwned", true); Set(item, "NextVehicleStateAt", float.MaxValue);
                    Call(items, "UpdateItems", session);
                    Require(capture.Packets.Count >= 4 && capture.Packets[0].Message is VehicleState off && !off.EngineOn && !off.AccOn
                        && capture.Packets[1].Message is VehicleClimate && capture.Packets[2].Message is VehicleCondition condition && condition.Availability == 0
                        && capture.Packets[3].Message is ItemTransform pose && pose.IsFinal
                        && capture.Packets[0].Channel == Channel.ReliableOrdered && capture.Packets[1].Channel == Channel.ReliableOrdered
                        && capture.Packets[2].Channel == Channel.ReliableOrdered && capture.Packets[3].Channel == Channel.ReliableOrdered
                        && !(bool)Get(item, "LocallyOwned"), "Item release ordered its final pose ahead of ignition OFF.");
                });
                check("vehicle state: seated claimant cannot receive stale ignition timeout before claiming", () =>
                {
                    reset(); mode(false, 0); Require(receive(State(0, 1, 1500)), "Host source rejected.");
                    Set(item, "RemoteEngineUntil", -999f); player.transform.SetParent(car.transform, false);
                    Call(items, "UpdateItems", session);
                    Require((bool)Get(item, "LocallyOwned") && power.ActiveStateName == "ON", "Pre-claim observer timeout switched the seated driver's native power off.");
                });
                check("vehicle state: reliable off survives old on packets before and after release", () =>
                {
                    reset(); mode(true, 1); Require(receive(State(1, 10, 1500)) && receive(State(1, 11, 0)), "OFF sequence rejected.");
                    Require(!receive(State(1, 10, 2000)) && !((bool)Get(item, "RemoteEngineOn")), "Old ON beat the reliable OFF.");
                    mode(true, 255); Require(!receive(State(1, 12, 2000)) && power.ActiveStateName == "OFF", "Released sender restarted ignition.");
                });
                check("vehicle state: authenticated host relay preserves the reliable channel", () =>
                {
                    reset(); mode(true, 1); var off = State(1, 1, 0);
                    Call(session, "HandleMessage", first, off, Channel.ReliableOrdered);
                    Require(capture.Packets.Count == 1 && capture.Packets[0].Peer == second && capture.Packets[0].Channel == Channel.ReliableOrdered
                        && capture.Packets[0].Message is VehicleState, "Host downgraded or misrouted the reliable final state.");
                });
                check("vehicle state: host relay drops stale and forged packets", () =>
                {
                    capture.Packets.Clear(); Call(session, "HandleMessage", first, State(1, 1, 3000), Channel.UnreliableSequenced);
                    Call(session, "HandleMessage", second, State(1, 2, 3000), Channel.UnreliableSequenced);
                    Require(capture.Packets.Count == 0 && power.ActiveStateName == "OFF", "Rejected engine input was relayed or applied.");
                });
                check("vehicle state: nearby new driver claims a previous non-driver stream before engine relay", () =>
                {
                    reset(); mode(true, 1); Require(receive(State(1, 400, 1500)), "First driver source rejected.");
                    Set(item, "LastRemoteAt", Time.unscaledTime); Set(item, "RemoteVehicleStream", true);
                    ((RemotePlayer)peers[second]).LastTransformTime = Time.unscaledTime;
                    var claim = Pose(2, 1, true); Call(session, "HandleMessage", second, claim, Channel.ReliableOrdered);
                    Require((byte)Get(item, "RemoteOwner") == 2 && (bool)Get(item, "RemoteIsDriver")
                        && capture.Packets.Count == 1 && capture.Packets[0].Message is ItemTransform,
                        "Nearby driver's transform could not establish the new engine source.");
                    capture.Packets.Clear(); Call(session, "HandleMessage", second, State(2, 1, 2200), Channel.UnreliableSequenced);
                    Call(session, "HandleMessage", first, State(1, 401, 7000), Channel.UnreliableSequenced);
                    Require(capture.Packets.Count == 1 && ((VehicleState)capture.Packets[0].Message).Rpm == 2200,
                        "New driver's first engine sample or former owner's rejection was lost during takeover.");
                });
                check("vehicle state: active remote driver blocks third-party claim and relay", () =>
                {
                    capture.Packets.Clear(); ((RemotePlayer)peers[first]).LastTransformTime = Time.unscaledTime;
                    Call(session, "HandleMessage", first, Pose(1, 2, true), Channel.ReliableOrdered);
                    Require(capture.Packets.Count == 0 && (byte)Get(item, "RemoteOwner") == 2,
                        "An active driver was displaced or its rejected pose relayed.");
                });
                check("vehicle state: targeted vehicle repair uses a snapshot without ownership release", () =>
                {
                    reset(); mode(true, 1); Require(receive(State(1, 10, 1600)), "Guest source rejected.");
                    var messages = (IEnumerable)Call(world, "BuildObjectStateMessages", VehicleId)!; bool pose = false, engine = false;
                    foreach (IMessage message in messages)
                    {
                        Require(!(message is ItemTransform), "Repair synthesized a final transform that releases the active driver.");
                        if (message is WorldItemSnapshot snapshot) pose = snapshot.Entries.Count == 1 && snapshot.Entries[0].ItemId == VehicleId;
                        if (message is VehicleState state) engine = state.Rpm == 1600 && state.Sequence == VehicleState.SnapshotSequence;
                    }
                    Require(pose && engine && (byte)Get(item, "RemoteOwner") == 1, "Targeted repair lost pose or accepted engine source.");
                });
                check("vehicle state: disconnected sender can establish a new sequence baseline", () =>
                {
                    reset(); mode(true, 1); Require(receive(State(1, 100, 1000)), "Source rejected.");
                    Call(vehicles, "ForgetVehicleStatePlayer", (byte)1);
                    Require(Get(item, "AcceptedVehicleState") == null && receive(State(1, 0, 1800)), "Disconnected sender retained stale sequence or view.");
                });
                check("vehicle state: session cleanup clears retained view and send bookkeeping", () =>
                {
                    Set(item, "OutVehicleStateSequence", (ushort)55); Set(item, "LastSentIgnitionActive", true);
                    Call(vehicles, "ClearVehicleStateStreams");
                    Require(Get(item, "AcceptedVehicleState") == null && (ushort)Get(item, "OutVehicleStateSequence") == 0
                        && !(bool)Get(item, "LastSentIgnitionActive") && receive(State(1, 0, 900)), "Cleanup left stale receive or send state.");
                });
            }
            finally
            {
                Set(session, "_transport", null); peers.Clear(); SetProperty(session, "State", SessionState.Idle);
                Set(world, "_syncReady", false);
                UnityEngine.Object.DestroyImmediate(car); UnityEngine.Object.DestroyImmediate(controller);
                SetStaticProperty(typeof(SessionManager), "Instance", savedSession); SetStaticProperty(World, "Instance", savedWorld);
            }
        }

        private static int _electricityGuardCalls;
        private static bool _electricityGuardForced;

        private static bool FailElectricityGuard(bool force, ref bool __result)
        {
            _electricityGuardCalls++; _electricityGuardForced &= force;
            __result = false; return false;
        }

        private static void RunElectricityRetryChecks(Action<string, Action> check, object item, PlayMakerFSM power,
            Action reset, Action<bool, byte> mode, Func<VehicleState, bool> receive)
        {
            var paths = Core.GetType("WinterMP.Core.Sync.ScenePath", true);
            var budget = paths.GetField("DiscoveryBudget", Static).GetValue(null);
            object used = Get(budget, "_used"), frame = Get(budget, "_lastFrame");
            Action reserve = () => paths.GetMethod("TryBeginDiscovery", Static).Invoke(null, new object[] { 0f, 3f, true });
            // These synchronous native fixtures cannot advance Unity's frame.
            // Reset only admission storage; Net tests cover actual frame changes.
            Action release = () => Set(budget, "_used", false);
            try
            {
                check("electricity retries: first accepted ON stays immediate in an occupied discovery frame", () =>
                {
                    reset(); mode(false, 255); reserve();
                    Require(receive(State(0, VehicleState.SnapshotSequence, 1600)) && power.ActiveStateName == "ON"
                        && (bool)Get(item, "HasRemoteElectricsState") && (float)Get(item, "NextRemoteElectricsAttemptAt") == 0,
                        "First accepted ON waited for routine discovery or retained a failed-attempt deadline.");
                });
                check("electricity retries: successful ON permits immediate OFF in the same occupied frame", () =>
                {
                    reserve(); Require(receive(State(0, VehicleState.SnapshotSequence, 0)) && power.ActiveStateName == "OFF"
                        && (bool)Get(item, "HasRemoteElectricsState") && (float)Get(item, "NextRemoteElectricsAttemptAt") == 0,
                        "A completed transition delayed the next accepted OFF.");
                });
                foreach (bool on in new[] { true, false })
                {
                    bool wanted = on;
                    check("electricity retries: pending " + (on ? "ON" : "OFF") + " keeps its deadline and native state while discovery is occupied", () =>
                    {
                        reset(); mode(false, 255); NativeBagPartChecks.Fire(power, wanted ? "OFF" : "ON");
                        power.gameObject.name = "late electricity";
                        Set(item, "SystemsReady", false); Set(item, "ElectricityPowerFsm", null);
                        Require(receive(State(0, VehicleState.SnapshotSequence, wanted ? (ushort)1600 : (ushort)0)), "Initial snapshot was rejected.");
                        power.gameObject.name = "Electricity"; Set(item, "NextSystemsProbeAt", 0f);
                        float due = Time.unscaledTime; Require(due > 0, "Retry fixture needs a positive deadline.");
                        Set(item, "NextRemoteElectricsAttemptAt", due); reserve();
                        for (int i = 0; i < 10; i++) CallStatic("UpdateRemoteEngineAudio", item, Time.unscaledTime);
                        Require((float)Get(item, "NextRemoteElectricsAttemptAt") == due && Get(item, "ElectricityPowerFsm") == null
                            && !(bool)Get(item, "HasRemoteElectricsState") && power.ActiveStateName == (wanted ? "OFF" : "ON"),
                            "Deferred retry performed discovery, changed native state or postponed its deadline.");
                        release(); CallStatic("UpdateRemoteEngineAudio", item, Time.unscaledTime);
                        Require(power.ActiveStateName == (wanted ? "ON" : "OFF") && (bool)Get(item, "HasRemoteElectricsState")
                            && (float)Get(item, "NextRemoteElectricsAttemptAt") == 0,
                            "Available discovery slot failed to apply the cached state without a new packet.");
                    });
                }
                check("electricity retries: an available discovery slot does not bypass the retry timer", () =>
                {
                    reset(); mode(false, 255); Set(item, "SystemsReady", false); Set(item, "ElectricityPowerFsm", null);
                    float future = Time.unscaledTime + 100; Set(item, "NextRemoteElectricsAttemptAt", future); release();
                    CallStatic("ApplyRemoteElectricity", item, true);
                    Require((float)Get(item, "NextRemoteElectricsAttemptAt") == future && Get(item, "ElectricityPowerFsm") == null
                        && power.ActiveStateName == "OFF", "A future retry performed native work.");
                });
                check("electricity retries: admitted attempts still require forced engine protection before native power", () =>
                {
                    reset(); mode(false, 255); release();
                    var guard = ActivationGuard();
                    var harmony = new Harmony("WinterMP.ElectricityRetryProbe");
                    _electricityGuardCalls = 0; _electricityGuardForced = true;
                    try
                    {
                        harmony.Patch(guard, prefix: new HarmonyMethod(typeof(VehicleStateChecks).GetMethod("FailElectricityGuard", Static)));
                        Require(receive(State(0, VehicleState.SnapshotSequence, 1600)), "Guarded snapshot was rejected.");
                        Require(_electricityGuardCalls == 1 && _electricityGuardForced && power.ActiveStateName == "OFF"
                            && !(bool)Get(item, "HasRemoteElectricsState"), "Failed preparation allowed a native transition.");
                        Set(item, "NextRemoteElectricsAttemptAt", Time.unscaledTime); reserve();
                        CallStatic("UpdateRemoteEngineAudio", item, Time.unscaledTime);
                        Require(_electricityGuardCalls == 1, "Deferred retry still forced engine discovery.");
                        release(); CallStatic("UpdateRemoteEngineAudio", item, Time.unscaledTime);
                        Require(_electricityGuardCalls == 2 && !_electricityGuardForced && power.ActiveStateName == "OFF"
                            && !(bool)Get(item, "HasRemoteElectricsState"), "Waiting retry forced discovery or marked a failed transition applied.");
                    }
                    finally { harmony.Unpatch(guard, HarmonyPatchType.All, harmony.Id); }
                    Set(item, "NextRemoteElectricsAttemptAt", Time.unscaledTime); release();
                    CallStatic("UpdateRemoteEngineAudio", item, Time.unscaledTime);
                    Require(power.ActiveStateName == "ON" && (bool)Get(item, "HasRemoteElectricsState"),
                        "Repaired protection did not allow the retained host state to recover.");
                });
                RunElectricityReadinessChecks(check, item, power, reset, mode, receive, release);
                RunElectricityStartupChecks(check, item, power, reset, mode, receive, release);
            }
            finally { Set(budget, "_used", used); Set(budget, "_lastFrame", frame); reset(); }
        }

        private static readonly List<bool> ElectricityPreparations = new List<bool>();
        private static bool _electricityRoutineReady, _electricityActivationReady;
        private static Action? _electricityActivationMutation;

        private static bool ControlElectricityReadiness(bool force, ref bool __result)
        {
            ElectricityPreparations.Add(force);
            if (force) _electricityActivationMutation?.Invoke();
            __result = force ? _electricityActivationReady : _electricityRoutineReady;
            return false;
        }

        private static void RunElectricityStartupChecks(Action<string, Action> check, object item, PlayMakerFSM power,
            Action reset, Action<bool, byte> mode, Func<VehicleState, bool> receive, Action release)
        {
            var guard = ActivationGuard(); var harmony = new Harmony("WinterMP.ElectricityStartupProbe");
            Action retry = () =>
            {
                Set(item, "NextRemoteElectricsAttemptAt", Time.unscaledTime); release();
                CallStatic("UpdateRemoteEngineAudio", item, Time.unscaledTime);
            };
            try
            {
                harmony.Patch(guard, prefix: new HarmonyMethod(typeof(VehicleStateChecks).GetMethod("ControlElectricityReadiness", Static)));
                _electricityRoutineReady = _electricityActivationReady = true;
                foreach (bool on in new[] { false, true })
                {
                    bool wanted = on;
                    check("electricity startup: retained " + (wanted ? "ON" : "OFF") + " waits for native startup without forcing engine scans", () =>
                    {
                        reset(); mode(false, 255); release(); ElectricityPreparations.Clear();
                        var root = new GameObject("dormant power fixture"); root.SetActive(false);
                        var dormant = MakePower(root);
                        Set(item, "ElectricityPowerFsm", dormant);
                        try
                        {
                            Require(!dormant.Fsm.Initialized && !dormant.Fsm.Started, "Power fixture was already initialized.");
                            Require(receive(State(0, VehicleState.SnapshotSequence, wanted ? (ushort)1600 : (ushort)0)), "Pending power state was rejected.");
                            Require(!(bool)Get(item, "HasRemoteElectricsState") && !dormant.Fsm.Initialized
                                && ElectricityPreparations.Count == 0, "Dormant power was falsely applied, initialized or forced engine preparation.");
                            root.SetActive(true);
                            Require(dormant.Fsm.Initialized && !dormant.Fsm.Started, "Awakening unexpectedly started the disabled power controller.");
                            retry(); Require(!(bool)Get(item, "HasRemoteElectricsState") && ElectricityPreparations.Count == 0,
                                "Initialized but unstarted power was treated as an applied native transition.");
                            NativeBagPartChecks.Start(dormant); retry();
                            Require((bool)Get(item, "HasRemoteElectricsState") && (bool)Get(item, "RemoteElectricsApplied") == wanted
                                && dormant.ActiveStateName == (wanted ? "ON" : "OFF")
                                && dormant.FsmVariables.FindFsmBool("ACC").Value == wanted
                                && ElectricityPreparations.Count == 2 && !ElectricityPreparations[0] && ElectricityPreparations[1],
                                "Native startup failed to apply retained power through both protection checks.");
                        }
                        finally { Set(item, "ElectricityPowerFsm", power); UnityEngine.Object.DestroyImmediate(root); }
                    });
                }
                foreach (bool replace in new[] { false, true })
                {
                    bool replaced = replace;
                    check("electricity startup: power " + (replaced ? "replacement" : "deactivation") + " during protection cannot be acknowledged", () =>
                    {
                        reset(); mode(false, 255); release(); ElectricityPreparations.Clear();
                        var root = new GameObject("replacement power fixture"); var replacement = MakePower(root);
                        replacement.Fsm.Init(replacement); NativeBagPartChecks.Start(replacement);
                        _electricityActivationMutation = () =>
                        { if (replaced) Set(item, "ElectricityPowerFsm", replacement); else power.gameObject.SetActive(false); };
                        try
                        {
                            Require(receive(State(0, VehicleState.SnapshotSequence, 1600)), "Host ignition was rejected.");
                            Require(!(bool)Get(item, "HasRemoteElectricsState") && !power.FsmVariables.FindFsmBool("ACC").Value
                                && replacement.ActiveStateName == "OFF" && ElectricityPreparations.Count == 1 && ElectricityPreparations[0],
                                "Power changed during forced protection but native activation was still applied.");
                        }
                        finally
                        {
                            _electricityActivationMutation = null; power.gameObject.SetActive(true);
                            Set(item, "ElectricityPowerFsm", power); UnityEngine.Object.DestroyImmediate(root);
                        }
                    });
                }
            }
            finally
            {
                harmony.Unpatch(guard, HarmonyPatchType.All, harmony.Id); _electricityActivationMutation = null;
                ElectricityPreparations.Clear(); _electricityRoutineReady = _electricityActivationReady = false;
            }
        }

        private static void RunElectricityReadinessChecks(Action<string, Action> check, object item, PlayMakerFSM power,
            Action reset, Action<bool, byte> mode, Func<VehicleState, bool> receive, Action release)
        {
            var guard = ActivationGuard();
            var harmony = new Harmony("WinterMP.ElectricityReadinessProbe");
            Action pending = () =>
            {
                reset(); mode(false, 255); release(); ElectricityPreparations.Clear();
                _electricityRoutineReady = _electricityActivationReady = false;
                Require(receive(State(0, VehicleState.SnapshotSequence, 1600)), "Initial host ignition was rejected.");
                Require(ElectricityPreparations.Count == 1 && ElectricityPreparations[0]
                    && !(bool)Get(item, "HasRemoteElectricsState"), "First attempt did not require full activation protection.");
                ElectricityPreparations.Clear();
                Set(item, "NextRemoteElectricsAttemptAt", Time.unscaledTime); release();
            };
            Action requireBoth = () => Require(ElectricityPreparations.Count == 2 && !ElectricityPreparations[0]
                && ElectricityPreparations[1], "Retry did not run readiness followed by forced activation protection.");
            try
            {
                harmony.Patch(guard, prefix: new HarmonyMethod(typeof(VehicleStateChecks).GetMethod("ControlElectricityReadiness", Static)));
                check("electricity readiness: pending initialization retries without a forced scan or lost host state", () =>
                {
                    pending(); object accepted = Get(item, "AcceptedVehicleState");
                    CallStatic("UpdateRemoteEngineAudio", item, Time.unscaledTime);
                    Require(ElectricityPreparations.Count == 1 && !ElectricityPreparations[0]
                        && power.ActiveStateName == "OFF" && !(bool)Get(item, "HasRemoteElectricsState")
                        && ReferenceEquals(accepted, Get(item, "AcceptedVehicleState"))
                        && (bool)Get(item, "RemoteEngineOn") && (float)Get(item, "NextRemoteElectricsAttemptAt") > Time.unscaledTime,
                        "Pending initialization forced discovery, lost the host state or bypassed the retry timer.");
                });
                check("electricity readiness: passing routine readiness cannot bypass failed activation protection", () =>
                {
                    pending(); _electricityRoutineReady = true;
                    CallStatic("UpdateRemoteEngineAudio", item, Time.unscaledTime); requireBoth();
                    Require(power.ActiveStateName == "OFF" && !(bool)Get(item, "HasRemoteElectricsState"),
                        "Routine readiness alone applied native ignition.");
                });
                check("electricity readiness: repaired protection applies retained ON and permits immediate OFF", () =>
                {
                    pending(); _electricityRoutineReady = _electricityActivationReady = true;
                    CallStatic("UpdateRemoteEngineAudio", item, Time.unscaledTime); requireBoth();
                    Require(power.ActiveStateName == "ON" && (bool)Get(item, "RemoteElectricsApplied")
                        && (bool)Get(item, "HasRemoteElectricsState") && (float)Get(item, "NextRemoteElectricsAttemptAt") == 0f,
                        "Repaired protection did not apply retained ON without another packet.");
                    ElectricityPreparations.Clear();
                    Require(receive(State(0, VehicleState.SnapshotSequence, 0)) && power.ActiveStateName == "OFF"
                        && ElectricityPreparations.Count == 1 && ElectricityPreparations[0],
                        "Successful recovery delayed OFF or skipped its forced protection.");
                });
                check("electricity readiness: a newer OFF replaces pending ON before readiness recovers", () =>
                {
                    pending(); Set(item, "NextRemoteElectricsAttemptAt", Time.unscaledTime + 100f);
                    NativeBagPartChecks.Fire(power, "ON");
                    Require(receive(State(0, VehicleState.SnapshotSequence, 0)) && ElectricityPreparations.Count == 0,
                        "New host state bypassed the waiting deadline.");
                    Set(item, "NextRemoteElectricsAttemptAt", Time.unscaledTime); release();
                    _electricityRoutineReady = _electricityActivationReady = true;
                    CallStatic("UpdateRemoteEngineAudio", item, Time.unscaledTime); requireBoth();
                    Require(power.ActiveStateName == "OFF" && (bool)Get(item, "HasRemoteElectricsState")
                        && !(bool)Get(item, "RemoteElectricsApplied"), "Recovery replayed stale ON instead of the latest OFF.");
                });
                check("electricity readiness: expired pending ignition recovers to OFF", () =>
                {
                    pending(); NativeBagPartChecks.Fire(power, "ON");
                    Set(item, "RemoteEngineUntil", Time.unscaledTime - 1f);
                    _electricityRoutineReady = _electricityActivationReady = true;
                    CallStatic("UpdateRemoteEngineAudio", item, Time.unscaledTime); requireBoth();
                    Require(power.ActiveStateName == "OFF" && (bool)Get(item, "HasRemoteElectricsState")
                        && !(bool)Get(item, "RemoteEngineOn") && !(bool)Get(item, "RemoteAccOn"),
                        "Expired host ignition was activated during recovery.");
                });
                check("electricity readiness: an initial activation never waits on routine readiness", () =>
                {
                    reset(); mode(false, 255); release(); ElectricityPreparations.Clear();
                    _electricityRoutineReady = false; _electricityActivationReady = true;
                    Require(receive(State(0, VehicleState.SnapshotSequence, 1600)) && power.ActiveStateName == "ON"
                        && ElectricityPreparations.Count == 1 && ElectricityPreparations[0],
                        "An initial activation was delayed by cached routine readiness.");
                });
            }
            finally
            {
                harmony.Unpatch(guard, HarmonyPatchType.All, harmony.Id);
                ElectricityPreparations.Clear(); _electricityRoutineReady = _electricityActivationReady = false;
            }
        }

        private static MethodInfo ActivationGuard()
        {
            var guard = Core.GetType("WinterMP.Core.Sync.GuestEngineProtection", true);
            // Preserve identical diagnostics for the previous simulation-only gate.
            return guard.GetMethod("PrepareForActivation", Static) ?? guard.GetMethod("Prepare", Static);
        }

        private static VehicleState State(byte owner, ushort sequence, ushort rpm) => new VehicleState {
            VehicleId = VehicleId, OwnerPlayerId = owner, Sequence = sequence, Rpm = rpm,
            Flags = rpm > 0 ? (byte)(VehicleState.FlagEngineOn | VehicleState.FlagAccOn) : (byte)0,
            SpeedTenthsKmh = 234, FuelLevel = 128, CoolantTemp = 170, Gear = 3 };
        private static ItemTransform Pose(byte owner, ushort sequence, bool driver) => new ItemTransform {
            ItemId = VehicleId, OwnerPlayerId = owner, Sequence = sequence,
            Flags = driver ? ItemTransform.FlagDriver : ItemTransform.FlagVehicle,
            Position = new NetVector3(0, 0, 0), Rotation = new NetQuaternion(0, 0, 0, 1) };
        private static Packet SingleVehicle(CaptureTransport capture)
        { Require(capture.Packets.Count == 1 && capture.Packets[0].Message is VehicleState, "Expected exactly one captured engine message."); return capture.Packets[0]; }
        private static PlayMakerFSM MakePower(GameObject car)
        {
            var obj = new GameObject("Electricity"); obj.transform.SetParent(car.transform, false);
            var fsm = obj.AddComponent<PlayMakerFSM>(); fsm.enabled = false; Set(fsm, "fsm", new Fsm()); fsm.Fsm.Name = "Power";
            var acc = new FsmBool { Name = "ACC", UseVariable = true };
            fsm.FsmVariables.BoolVariables = new[] { acc }; fsm.Fsm.StartState = "OFF";
            var states = new List<FsmState>();
            foreach (string name in new[] { "OFF", "ON" })
            {
                Type? type = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    if ((type = assembly.GetType("HutongGames.PlayMaker.Actions.SetBoolValue")) != null) break;
                var action = (FsmStateAction)Activator.CreateInstance(type ?? throw new InvalidOperationException("Native SetBoolValue missing."));
                action.Reset(); action.Enabled = true; Set(action, "boolVariable", acc); Set(action, "boolValue", new FsmBool(name == "ON"));
                Set(action, "everyFrame", false);
                var state = new FsmState(fsm.Fsm) { Name = name, Actions = new[] { action } }; action.Init(state); states.Add(state);
            }
            fsm.Fsm.States = states.ToArray();
            foreach (var state in fsm.Fsm.States) state.SaveActions();
            return fsm;
        }

        private sealed class Packet
        {
            internal PeerId Peer;
            internal Channel Channel;
            internal IMessage Message = null!;
        }
        private sealed class CaptureTransport : ITransport
        {
            internal readonly List<Packet> Packets = new List<Packet>();
            public bool IsHost => true;
            public event Action<PeerId>? PeerConnected { add { } remove { } }
            public event Action<PeerId, string>? PeerDisconnected { add { } remove { } }
            public event Action<PeerId, byte[], Channel>? PacketReceived { add { } remove { } }
            public void Send(PeerId peer, byte[] payload, Channel channel) => Send(peer, payload, payload.Length, channel);
            public void Send(PeerId peer, byte[] payload, int length, Channel channel)
            {
                var copy = new byte[length]; Array.Copy(payload, copy, length);
                Packets.Add(new Packet { Peer = peer, Channel = channel, Message = PacketCodec.Decode(copy) });
            }
            public void Update() { }
            public void Dispose() { }
        }
        private static object Get(object target, string field) => target.GetType().GetField(field, Members).GetValue(target);
        private static void Set(object target, string field, object? value) => target.GetType().GetField(field, Members).SetValue(target, value);
        private static void SetProperty(object target, string property, object? value) => target.GetType().GetProperty(property, Members).SetValue(target, value, null);
        private static void SetStaticProperty(Type type, string property, object? value) => type.GetProperty(property, Static).SetValue(null, value, null);
        private static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, Members).Invoke(target, args);
        private static object? CallStatic(string method, params object?[] args) => Vehicles.GetMethod(method, Static).Invoke(null, args);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
