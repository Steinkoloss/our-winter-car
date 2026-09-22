using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private static void RunDrivetrainStateChecks(Action<string, Action> check, object item, object vehicles,
            SessionManager session, CaptureTransport transport, Action reset, Action<bool, byte> mode)
        {
            var items = (IDictionary)Get(Get(vehicles, "_items"), "_items");
            uint previousId = (uint)Get(item, "Id"), id = StableHash.Fnv1a32("vehicle:CORRIS");
            var previousBody = Get(item, "Body"); var previousPath = Get(item, "Path");
            string[] rpmFields = { "EngineRevsVar", "NativeEngineRpm", "NativeEngineRpmOutput", "RequiresNativeEngineRpm" };
            var savedRpm = new Dictionary<string, object>(); foreach (string field in rpmFields) savedRpm[field] = Get(item, field);
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protection = policy.GetType().GetProperty("ProtectWorld", Members); bool wasProtected = (bool)protection.GetValue(policy, null);
            Action<bool> protect = value => protection.GetSetMethod(true).Invoke(policy, new object[] { value });
            const string prefix = "drivetrain publication: ";
            using (GuestEngineInputChecks.SuspendProjectionForFixture())
            try
            {
                reset(); protect(false);
                using (var f = new DifferentialFixture(true))
                {
                    items.Remove(previousId); items[id] = item; Set(item, "Id", id); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS");
                    Func<VehicleDrivetrainWearState> build = () => (VehicleDrivetrainWearState)Call(vehicles, "BuildVehicleDrivetrainWearState", item)!;
                    Func<VehicleDrivetrainWearState?> read = () => (VehicleDrivetrainWearState?)Call(vehicles, "ReadDrivetrainWearState", id);
                    Func<VehicleDrivetrainWearState, bool> receive = state => (bool)Call(vehicles, "OnVehicleDrivetrainWearState", state)!;
                    Action poll = () => { Set(vehicles, "_nextDrivetrainStatePoll", 0f); Call(vehicles, "UpdateDrivetrainWearStates", session); };
                    Func<VehicleDrivetrainWearState> lastPacket = () =>
                    {
                        Require(transport.Packets.Count == session.PlayerCount, "Wear broadcast did not reach every guest.");
                        VehicleDrivetrainWearState? last = null;
                        foreach (var packet in transport.Packets)
                        {
                            Require(packet.Channel == Channel.ReliableOrdered && packet.Message is VehicleDrivetrainWearState, "Wear used the wrong wire message or channel.");
                            last = (VehicleDrivetrainWearState)PacketCodec.Decode(PacketCodec.Encode(packet.Message));
                        }
                        return last ?? throw new InvalidOperationException("Missing wear packet.");
                    };
                    VehicleDrivetrainWearState? sent = null;
                    check(prefix + "native host wear reaches every guest as exact saved results", () =>
                    {
                        mode(true, 1); var input = State(1, 1, 3000); input.VehicleId = id;
                        input.DifferentialSpeedAvailable = true; input.DifferentialSpeed = 3150;
                        Require((bool)Call(vehicles, "OnRemoteVehicleState", input)!, "Driver sample rejected.");
                        f.SetSpeed(0); f.ResetSaved(); f.CycleWear(); var actual = f.WearValues();
                        Require(actual[2] < 90, "Native wear did not run."); transport.Packets.Clear(); poll(); sent = lastPacket();
                        Require(sent.Flags == 1 && sent.DriveshaftWear == actual[0] && sent.GearboxWear == actual[1] && sent.RearAxleWear == actual[2], "Published wear differs from the host mounts.");
                        Require(sent.VehicleId == id && sent.Revision == 1, "Wrong vehicle or initial host revision.");
                    });
                    check(prefix + "unchanged capture is quiet until reliable keepalive", () =>
                    {
                        transport.Packets.Clear(); poll(); Require(transport.Packets.Count == 0, "Unchanged state flooded guests.");
                        Set(vehicles, "_nextDrivetrainStateKeepalive", 0f); poll(); var next = lastPacket();
                        Require(next.Revision == sent!.Revision && next.SameWear(sent), "Keepalive changed host revision or values.");
                    });
                    int snapshotCase = 0;
                    foreach (string method in new[] { "BuildJoinVehicleSnapshots", "BuildVehicleResyncMessages", "BuildVehicleStateMessages" })
                        check(prefix + method + " includes current wear without consuming the pending broadcast", () =>
                        {
                            f.ResetSaved(); f.SetSpeed(0);
                            float expectedWear = 90 + ++snapshotCase * .125f;
                            f.Fsm.FsmVariables.FindFsmGameObject("db_Driveshaft").Value.GetComponent<PlayMakerFSM>().FsmVariables.FindFsmFloat("Wear").Value = expectedWear;
                            var enumerable = (IEnumerable)Call(vehicles, method, method == "BuildVehicleStateMessages" ? new object[] { item, (byte)0 } : new object[0])!;
                            VehicleDrivetrainWearState? snapshot = null;
                            foreach (object value in enumerable) if (value is VehicleDrivetrainWearState wear) { snapshot = wear; break; }
                            Require(snapshot != null && snapshot.Flags == 1 && snapshot.DriveshaftWear == expectedWear, "Snapshot omitted current wear.");
                            var saved = f.WearValues(); Require(saved[0] == expectedWear && saved[1] == 90 && saved[2] == 90, "Snapshot changed native saved wear."); transport.Packets.Clear();
                            // Marking a snapshot observed must not mark the live result sent.
                            Call(vehicles, "BuildVehicleDrivetrainWearState", item); poll();
                            Require(lastPacket().Revision == snapshot!.Revision, "Snapshot consumed the broadcast or changed its revision.");
                            sent = snapshot;
                        });
                    check(prefix + "parked host repairs advance the result independently of driver condition", () =>
                    {
                        mode(true, 255); f.ResetSaved(); var data = f.Fsm.FsmVariables.FindFsmGameObject("db_Gearbox").Value.GetComponent<PlayMakerFSM>();
                        data.FsmVariables.FindFsmFloat("Wear").Value = 99.75f; transport.Packets.Clear(); poll(); sent = lastPacket();
                        Require(sent.GearboxWear == 99.75f && sent.Revision > 1, "Parked native repair was not published.");
                    });
                    check(prefix + "guest driver observer and parked views retain the same host result without saved writes", () =>
                    {
                        mode(false, 1); f.ResetSaved(); Require(receive(sent!), "Guest rejected host result.");
                        foreach (byte owner in new byte[] { 1, 2, 255 })
                        {
                            mode(false, owner); foreach (bool local in new[] { false, true })
                            { Set(item, "LocallyOwned", local); Require(read()!.SameWear(sent!), "Driver ownership changed host wear."); }
                        }
                        Set(item, "LocallyOwned", false); f.Unchanged();
                        var copy = read()!; copy.GearboxWear = 1; Require(read()!.GearboxWear == 99.75f, "Caller mutated accepted wear.");
                    });
                    check(prefix + "host handler and guest sender cannot turn result packets into saved writes", () =>
                    {
                        mode(true, 1); Require(!receive(sent!), "Host accepted a wear result."); f.Unchanged();
                        Require(!SessionMessagePolicy.IsSenderAllowed(MessageId.VehicleDrivetrainWearState, true, true, true, true), "Authenticated guest can forge wear.");
                        mode(false, 1); Require(Call(vehicles, "BuildVehicleDrivetrainWearState", item) == null, "Guest published saved wear.");
                    });
                    check(prefix + "stale conflicting invalid and foreign messages cannot replace accepted wear", () =>
                    {
                        mode(false, 1); var good = sent!.Copy(); good.Revision += 10; Require(receive(good), "New host revision rejected.");
                        var bad = good.Copy(); bad.GearboxWear = 1; Require(!receive(bad), "Same revision conflict accepted.");
                        bad.Revision--; Require(!receive(bad), "Stale result accepted.");
                        bad = good.Copy(); bad.VehicleId++; Require(!receive(bad), "Foreign vehicle accepted.");
                        bad = good.Copy(); bad.Revision++; bad.RearAxleWear = float.NaN; Require(!receive(bad), "Nonfinite result accepted.");
                        Require(read()!.SameWear(good), "Invalid result altered retained wear."); f.Unchanged();
                        Call(vehicles, "ClearDrivetrainWearStates");
                    });
                    check(prefix + "arrival before discovery survives late registration without touching local mounts", () =>
                    {
                        mode(false, 1); items.Remove(id);
                        try { Require(receive(sent!), "Pre-discovery host result lost."); }
                        finally { items[id] = item; }
                        Require(read()!.SameWear(sent!), "Discovery lost retained host wear."); f.Unchanged();
                    });
                    check(prefix + "withdrawal clears old wear and later valid repair recovers", () =>
                    {
                        var missing = new VehicleDrivetrainWearState { VehicleId = id, Revision = sent!.Revision + 1 };
                        Require(receive(missing), "Withdrawal rejected."); Require(read()!.Flags == 0, "Withdrawal kept old wear available.");
                        Require(!receive(sent), "Old snapshot revived withdrawn wear."); var restored = sent.Copy(); restored.Revision += 2;
                        Require(receive(restored) && read()!.Flags == 1, "New host result did not restore availability.");
                    });
                    check(prefix + "invalid native saved value withdraws all targets and repaired data recovers", () =>
                    {
                        mode(true, 255); var data = f.Fsm.FsmVariables.FindFsmGameObject("db_Gearbox").Value.GetComponent<PlayMakerFSM>();
                        var scalar = data.FsmVariables.FindFsmFloat("Wear"); scalar.Value = float.NaN;
                        var unavailable = build(); Require(unavailable.Flags == 0 && unavailable.DriveshaftWear == 0 && unavailable.RearAxleWear == 0, "Bad saved data leaked partial wear.");
                        f.ResetSaved(); var restored = build(); Require(restored.Flags == 1 && restored.Revision != unavailable.Revision, "Repaired saved wear remained unavailable.");
                    });
                    check(prefix + "paused wear snapshots cannot execute a recovery write", () =>
                    {
                        mode(true, 1); Set(item, "NextDrivetrainWearProbeAt", 0f); CallStatic("EnsureDrivetrainWear", item);
                        Set(item, "RemoteEngineUntil", Time.unscaledTime + 10f);
                        var write = NativeBagPartChecks.State(f.Fsm, "Wear").Actions[1]; var name = Get(write, "variableName");
                        Set(write, "variableName", new FsmString { Value = "Other" });
                        try { f.CycleWear(); Require(!f.Fsm.enabled, "Changed native graph did not pause."); }
                        finally { Set(write, "variableName", name); }
                        f.ResetSaved(); var unavailable = build(); Require(unavailable.Flags == 0 && !f.Fsm.enabled, "Snapshot resumed paused wear."); f.Unchanged();
                        Set(item, "NextDrivetrainWearProbeAt", 0f); CallStatic("EnsureDrivetrainWear", item);
                        Require(build().Flags == 1, "Repaired live binding did not restore capture.");
                    });
                    check(prefix + "changed targets and saved writer caches withdraw without changing native state", () =>
                    {
                        mode(true, 255); var writer = NativeBagPartChecks.State(f.Fsm, "Wear").Actions[1]; var prior = Get(writer, "fsm");
                        var other = f.Fsm.FsmVariables.FindFsmGameObject("db_Gearbox").Value.GetComponent<PlayMakerFSM>();
                        Set(writer, "fsm", other);
                        try { f.ResetSaved(); Require(build().Flags == 0, "Stale writer cache remained available."); f.Unchanged(); }
                        finally { Set(writer, "fsm", prior); }
                        Require(build().Flags == 1, "Repaired cache did not recover capture.");
                    });
                    check(prefix + "retired vehicle broadcasts withdrawal and rediscovery keeps revision history", () =>
                    {
                        mode(true, 255); transport.Packets.Clear(); poll(); var previous = build(); items.Remove(id); transport.Packets.Clear();
                        VehicleDrivetrainWearState missing;
                        try { poll(); missing = lastPacket(); Require(missing.Flags == 0 && missing.Revision != previous.Revision, "Retired vehicle did not withdraw."); }
                        finally { items[id] = item; }
                        transport.Packets.Clear(); poll(); var restored = lastPacket(); Require(restored.Flags == 1 && restored.Revision != missing.Revision, "Rediscovery reset or lost publication.");
                    });
                    check(prefix + "replacement body requires validated targets and preserves the host revision stream", () =>
                    {
                        var previous = build(); Set(item, "Body", previousBody); Require(build().Flags == 0, "Foreign replacement body exposed wear.");
                        Set(item, "Body", f.Body); var next = build(); Require(next.Flags == 1 && next.Revision != previous.Revision, "Restored body did not recover publication.");
                    });
                    check(prefix + "disconnect and stream teardown retire guest state and publisher baselines", () =>
                    {
                        mode(false, 1); SetProperty(session, "State", SessionState.Idle); Require(!receive(sent!) && read() == null, "Disconnected guest exposed or received wear.");
                        mode(false, 1); Call(vehicles, "ClearVehicleStateStreams"); Require(read() == null, "Guest state survived teardown.");
                        var first = sent!.Copy(); first.Revision = 0; Require(receive(first), "New session rejected initial revision zero.");
                        mode(true, 255); Require(build().Revision == 1, "New host session kept an old publication revision.");
                    });
                    RunGearboxOilPublicationChecks(check, f, vehicles, item, session, transport, mode, build, poll, lastPacket);
                }
            }
            finally
            {
                Call(vehicles, "ClearVehicleStateStreams"); items.Remove(id); items[previousId] = item;
                Set(item, "Id", previousId); Set(item, "Body", previousBody); Set(item, "Path", previousPath); protect(wasProtected); reset();
                foreach (string field in rpmFields) Set(item, field, savedRpm[field]);
            }
        }
    }
}
