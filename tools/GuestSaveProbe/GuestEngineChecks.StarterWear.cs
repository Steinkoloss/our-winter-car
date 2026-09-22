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
    internal static partial class GuestEngineChecks
    {
        private static void RunStarterWear(Action<string, Action> check, object world, object vehicles, object item,
            PlayMakerFSM starter, PlayMakerFSM battery, Dictionary<string, PlayMakerFSM> sources,
            Dictionary<string, object> row, StarterTransport transport, Action<bool> mode, Action clear, Action savedBattery)
        {
            foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
            {
                string name = (string)stateRow["name"];
                int index = name == "Starter damage" ? 1 : name == "Fuel Mixture" ? 10 : -1;
                if (index < 0) continue;
                var state = NativeBagPartChecks.State(starter, name);
                state.Actions[index] = NativeAction((Dictionary<string, object>)((List<object>)stateRow["actions"])[index], starter);
                state.Actions[index].Init(state);
            }
            var session = SessionManager.Instance!;
            var data = sources["db_Starter"]; var wear = data.FsmVariables.FindFsmFloat("Wear");
            var durability = data.FsmVariables.FindFsmFloat("Durability");
            var action = NativeBagPartChecks.State(starter, "Fuel Mixture").Actions[11];
            var calculation = NativeBagPartChecks.State(starter, "Fuel Mixture").Actions[10];
            var read = NativeBagPartChecks.State(starter, "Starter damage").Actions[1];
            var scratch = starter.FsmVariables.FindFsmFloat("StarterWear");
            var scratchDurability = starter.FsmVariables.FindFsmFloat("StarterDurability");
            var rate = (FsmFloat)Get(calculation, "float1");
            Action idle = () => NativeBagPartChecks.Fire(starter, "Probe idle");
            Action fire = () => { idle(); NativeBagPartChecks.Fire(starter, "Starter damage"); NativeBagPartChecks.Fire(starter, "Fuel Mixture"); };
            Action flush = () => InstanceCall(vehicles, "FlushStarterWear", session, item);
            Action saved = () => { savedBattery(); Require(wear.Value == 90 && durability.Value == 2, "Protected native callback changed saved starter condition."); };
            var captured = new List<StarterWearRequest>();
            Action host = () => { mode(true); idle(); battery.enabled = true; NativeBagPartChecks.Fire(battery, "Check joint"); };
            Action guest = () => { mode(false); Call("Prepare", true); };
            Func<StarterWearRequest, byte, bool> receive = (r, actor) => (bool)InstanceCall(world, "OnHostStarterWear", PacketCodec.Decode(PacketCodec.Encode(r)), actor)!;
            ushort sequence = 100;
            Func<StarterWearRequest> request = () => new StarterWearRequest { VehicleId = 709178, PlayerId = 1, Sequence = ++sequence, Seconds = .125f };
            Action restore = () =>
            {
                mode(true); starter.enabled = true; battery.gameObject.SetActive(true); battery.enabled = true;
                battery.FsmVariables.FindFsmBool("Installed").Value = true; battery.FsmVariables.FindFsmFloat("Charge").Value = 120;
                foreach (var source in sources.Values) { source.gameObject.SetActive(true); foreach (var v in source.FsmVariables.BoolVariables) v.Value = true; }
                durability.Value = 2; wear.Value = 90; idle(); NativeBagPartChecks.Fire(battery, "Check joint");
            };
            restore();
            check("starter wear: native duration includes entry and each update", () =>
            {
                Require(Time.deltaTime > 0 && Time.deltaTime * 3 < 1, "Invalid fixture frame interval.");
                float expected = 90; fire(); Tick(starter); Tick(starter);
                for (int i = 0; i < 3; i++) expected -= rate.Value * 2 * Time.deltaTime;
                Require(Math.Abs(wear.Value - expected) < .00001f && wear.Value < 90, "Native starter wear cadence differs from audit.");
            });
            restore(); guest();
            check("starter wear: native protected entry and updates send measured time", () =>
            {
                clear(); fire(); Tick(starter); Tick(starter); flush();
                Require(action.Enabled && transport.Packets.Count == 1, "Wear observation did not batch callbacks.");
                var packet = transport.Packets[0]; var r = packet.Message as StarterWearRequest;
                Require(r != null && r.VehicleId == 709178 && r.PlayerId == 1 && packet.Channel == Channel.ReliableOrdered
                    && Math.Abs(r.Seconds - Time.deltaTime * 3) < .000001f, "Wrong measured time, actor, vehicle or channel.");
                captured.Add(r!); saved();
            });
            check("starter wear: other loaded states and no flywheel do not report wear", () =>
            {
                clear(); foreach (string name in new[] { "Turn key", "Start or not", "Start engine", "No Flywheel" })
                { idle(); NativeBagPartChecks.Fire(starter, name); Tick(starter); }
                flush(); foreach (var packet in transport.Packets) Require(!(packet.Message is StarterWearRequest), "Battery load was mistaken for starter wear."); saved();
            });
            check("starter wear: bounded and timed batches preserve reported duration", () =>
            {
                clear(); idle();
                InstanceCall(vehicles, "RecordStarterWear", starter, .75f); InstanceCall(vehicles, "RecordStarterWear", starter, .5f);
                Set(((IDictionary)Get(vehicles, "_starterWear"))[709178u], "SendAt", 0f); InstanceCall(vehicles, "UpdateStarterWear", session);
                Require(transport.Packets.Count == 2 && ((StarterWearRequest)transport.Packets[0].Message).Seconds == .75f
                    && ((StarterWearRequest)transport.Packets[1].Message).Seconds == .5f
                    && ((StarterWearRequest)transport.Packets[1].Message).Sequence == 2, "Bounded duration batches lost or repeated time."); saved();
            });
            check("starter wear: invalid or paused frame duration creates no demand", () =>
            {
                clear(); foreach (float seconds in new[] { 0f, -1f, 1.001f, float.NaN, float.PositiveInfinity })
                    InstanceCall(vehicles, "RecordStarterWear", starter, seconds);
                flush(); Require(transport.Packets.Count == 0, "Invalid duration entered pending wear."); saved();
            });
            check("starter wear: ownership loss and session clear discard pending duration", () =>
            {
                clear(); fire(); Set(item, "LocallyOwned", false); InstanceCall(vehicles, "UpdateStarterWear", session);
                Set(item, "LocallyOwned", true); flush(); Require(transport.Packets.Count == 0, "Lost ownership retained wear.");
                fire(); InstanceCall(vehicles, "ClearVehicleStateStreams"); flush(); Require(transport.Packets.Count == 0, "Session clear retained wear."); saved();
            });
            check("starter wear: final ownership update flushes duration before release", () =>
            {
                clear(); fire(); InstanceCall(vehicles, "SendFinalVehicleState", session, item); Set(item, "LocallyOwned", false);
                InstanceCall(vehicles, "UpdateStarterWear", session); bool found = false;
                foreach (var packet in transport.Packets) if (packet.Message is StarterWearRequest) found = true;
                Require(found, "Final vehicle state lost wear."); Set(item, "LocallyOwned", true); saved();
            });
            check("starter wear: observing and disconnected guests cannot report", () =>
            {
                clear(); Set(item, "LocallyOwned", false); fire(); flush(); Set(item, "LocallyOwned", true);
                typeof(SessionManager).GetProperty("State", Members).GetSetMethod(true).Invoke(session, new object[] { SessionState.Idle });
                fire(); flush(); Require(transport.Packets.Count == 0, "Inactive guest sent wear."); mode(false); saved();
            });
            foreach (string field in new[] { "everyFrame", "perSecond", "subtractValue", "fsmName", "variableName" })
                check("starter wear: changed " + field + " is contained and repaired", () =>
                {
                    clear(); idle(); var original = Get(action, field);
                    try
                    {
                        Set(action, field, field == "everyFrame" || field == "perSecond" ? (object)false
                            : field == "subtractValue" ? new FsmFloat { Value = 123 } : (object)new FsmString { Value = "Changed" });
                        NativeBagPartChecks.Fire(starter, "Fuel Mixture"); flush();
                        Require(!starter.enabled && transport.Packets.Count == 0, "Changed wear writer escaped containment."); saved();
                    }
                    finally { Set(action, field, original); Call("Prepare", true); Call("Prepare", true); }
                    Require(starter.enabled, "Repaired wear writer stayed paused.");
                });
            clear(); host(); rate.Value = .31f; durability.Value = 3;
            check("starter wear: captured duration uses host rate and durability exactly once", () =>
            {
                wear.Value = 90; scratch.Value = 999; scratchDurability.Value = 777;
                var original = Get(action, "subtractValue"); var r = captured[0]; float expected = 90 - .31f * 3 * r.Seconds;
                Require(receive(r, 1) && wear.Value == expected, "Host used guest condition or stale scratch instead of its native rate.");
                Require(!receive(r, 1) && wear.Value == expected, "Duplicate wear applied twice.");
                Require(ReferenceEquals(Get(action, "subtractValue"), original) && (bool)Get(action, "perSecond")
                    && scratch.Value == 999 && scratchDurability.Value == 777, "Host failed to restore native scratch or cadence.");
            });
            foreach (string fault in new[] { "actor", "owner", "local", "battery removed", "starter absent", "starter inactive", "flywheel", "starter wiring", "harness wiring", "ground wiring", "host cranking", "cadence", "read source", "read variable", "rate operation", "rate cadence", "rate disabled", "durability NaN", "durability negative", "rate NaN", "wear infinity", "stale target" })
                check("starter wear: host rejects " + fault + " and recovers", () =>
                {
                    clear(); restore(); var r = request(); var originalTarget = Get(read, "gameObject");
                    var originalVariable = Get(read, "variableName"); var operation = Get(calculation, "operation");
                    var originalCache = Get(action, "fsm");
                    if (fault == "owner") Set(item, "RemoteOwner", (byte)2);
                    if (fault == "local") Set(item, "LocallyOwned", true);
                    if (fault == "battery removed") battery.FsmVariables.FindFsmBool("Installed").Value = false;
                    if (fault == "starter absent") data.FsmVariables.FindFsmBool("Installed").Value = false;
                    if (fault == "starter inactive") data.gameObject.SetActive(false);
                    if (fault == "flywheel") sources["db_Flywheel"].FsmVariables.FindFsmBool("Installed").Value = false;
                    if (fault == "starter wiring") sources["db_WiringStarter"].FsmVariables.FindFsmBool("Bolted").Value = false;
                    if (fault == "harness wiring") sources["db_WiringBatteryHarness"].FsmVariables.FindFsmBool("Bolted").Value = false;
                    if (fault == "ground wiring") sources["db_WiringGround"].FsmVariables.FindFsmBool("Installed").Value = false;
                    if (fault == "host cranking") { fire(); wear.Value = 90; }
                    if (fault == "cadence") Set(action, "perSecond", false);
                    if (fault == "read source") Set(read, "gameObject", new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner });
                    if (fault == "read variable") Set(read, "variableName", new FsmString { Value = "Wear" });
                    if (fault == "rate operation") Set(calculation, "operation", Enum.ToObject(operation.GetType(), 0));
                    if (fault == "rate cadence") Set(calculation, "everyFrame", true);
                    if (fault == "rate disabled") calculation.Enabled = false;
                    if (fault == "durability NaN") durability.Value = float.NaN;
                    if (fault == "durability negative") durability.Value = -1;
                    if (fault == "rate NaN") rate.Value = float.NaN;
                    if (fault == "wear infinity") wear.Value = float.PositiveInfinity;
                    if (fault == "stale target") Set(action, "fsm", sources["db_Flywheel"]);
                    float before = wear.Value;
                    try { Require(!receive(r, fault == "actor" ? (byte)2 : (byte)1) && wear.Value == before, "Invalid host state applied wear."); }
                    finally
                    {
                        Set(read, "gameObject", originalTarget); Set(read, "variableName", originalVariable); Set(action, "perSecond", true);
                        Set(calculation, "operation", operation); Set(calculation, "everyFrame", false); calculation.Enabled = true;
                        Set(action, "fsm", originalCache); rate.Value = .31f; restore();
                    }
                    if (fault != "actor" && fault != "owner" && fault != "local")
                        Require(!receive(r, 1) && wear.Value == 90, "Rejected wear replayed after repair.");
                    Require(receive(request(), 1) && wear.Value < 90, "Fresh wear request did not recover.");
                });
            check("starter wear: native mounted update publishes wear to the physical part", () =>
            {
                clear(); restore(); Require(receive(request(), 1), "Host wear was unavailable.");
                var physical = Empty(Child(data.gameObject, "Probe physical starter"), "Data"); AddFloat(physical, "Wear", 90); physical.Fsm.Init(physical);
                var originalVariables = data.FsmVariables.GameObjectVariables;
                try
                {
                    data.FsmVariables.GameObjectVariables = new[] { new FsmGameObject { Name = "ActivePart", UseVariable = true, Value = physical.gameObject } };
                    var parserType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                    var parser = Activator.CreateInstance(parserType, Members, null, new object[] {
                        File.ReadAllText(Path.Combine(Application.dataPath, "../starter-engine-input-probe.json")) }, null);
                    var fixture = (Dictionary<string, object>)parserType.GetMethod("ReadObject", Members).Invoke(parser, null);
                    FsmStateAction? publication = null;
                    foreach (Dictionary<string, object> native in (IEnumerable)fixture["fsms"])
                        if (((string)native["path"]).EndsWith("/VINP_Starter"))
                            foreach (Dictionary<string, object> stateRow in (IEnumerable)native["states"])
                                if ((string)stateRow["name"] == "Update 2")
                                    publication = NativeAction((Dictionary<string, object>)((List<object>)stateRow["actions"])[1], data);
                    Require(publication != null, "Native mounted wear publication was missing.");
                    publication!.Init(new FsmState(data.Fsm) { Name = "Probe mounted update" }); publication.OnEnter();
                    Require(physical.FsmVariables.FindFsmFloat("Wear").Value == wear.Value && wear.Value < 90,
                        "Native mounted update did not expose host wear through physical part Data.");
                }
                finally { data.FsmVariables.GameObjectVariables = originalVariables; UnityEngine.Object.DestroyImmediate(physical.gameObject); }
            });
            restore();
        }
    }
}
