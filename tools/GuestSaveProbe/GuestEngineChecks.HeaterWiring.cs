using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineChecks
    {
        private static void RunHeaterWiring(Action<string, Action> check)
        {
            using (var f = new HeaterFixture())
            {
                var reader = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                var parser = Activator.CreateInstance(reader, Members, null, new object[] {
                    File.ReadAllText(Path.Combine(Application.dataPath, "../heater-wiring-probe.json")) }, null);
                var rows = (List<object>)((Dictionary<string, object>)reader.GetMethod("ReadObject", Members).Invoke(parser, null))["fsms"];
                var wires = new Dictionary<uint, PlayMakerFSM>(); var actions = new Dictionary<uint, FsmStateAction>();
                var revisions = new Dictionary<uint, uint>();
                for (uint id = 9; id <= 11; id++)
                {
                    string path = "CORRIS/Wiring/DatabaseWiring/" + WiringPolicy.Name(id); var row = NativeBagPartChecks.Find(rows, path, "Data");
                    var wire = NativeBagPartChecks.MakeFsm(PathObject(f.Root, path), row);
                    foreach (var state in wire.FsmStates) { state.Transitions = new FsmTransition[0]; state.Actions = new[] { new Quiet() }; state.Actions[0].Init(state); }
                    wire.FsmVariables.FindFsmBool("Installed").Value = true;
                    NativeBagPartChecks.Start(wire); NativeBagPartChecks.Fire(wire, "Basic state"); wires.Add(id, wire);
                    string reference = id == 9 ? "db_wHeaterControl" : id == 10 ? "db_wHeaterbox" : "WiringWindowDefroster";
                    f.Heater.FsmVariables.FindFsmGameObject(reference).Value = wire.gameObject;
                    string stateName = id == 11 ? "Rear defrosting" : "Electrics?"; int index = id == 9 ? 2 : id == 10 ? 3 : 0;
                    var stateRow = FindState(f.HeaterRow, stateName); var stateTarget = NativeBagPartChecks.State(f.Heater, stateName);
                    var action = NativeAction((Dictionary<string, object>)((List<object>)stateRow["actions"])[index], f.Heater);
                    stateTarget.Actions[index] = action; action.Init(stateTarget); actions.Add(id, action);
                }
                Action<uint, byte> receive = (id, flags) =>
                {
                    revisions.TryGetValue(id, out uint revision); revisions[id] = ++revision;
                    InstanceCall(f.World, "OnWiringState", PacketCodec.Decode(PacketCodec.Encode(new WiringState { SourceId = id, Revision = revision, Flags = flags })));
                };
                Action saved = () =>
                {
                    foreach (var wire in wires.Values) Require(wire.FsmVariables.FindFsmBool("Installed").Value, "Projected heater circuit overwrote saved installation.");
                    f.Saved();
                };
                foreach (var pair in actions)
                {
                    uint id = pair.Key;
                    check("heater wiring: native solo reader uses saved circuit " + id, () =>
                    { pair.Value.OnEnter(); Require(((FsmBool)Get(pair.Value, "storeValue")).Value, "Solo circuit was projected."); });
                }
                f.Settled(); f.SavedWear.Value = 86; f.PartWear.Value = 91; f.Battery.FsmVariables.FindFsmFloat("Charge").Value = 120;
                f.Mode(false); f.Receive(3, 80); Require((bool)Call("Prepare", true)!, "Heater wire fixture admission failed.");
                foreach (var pair in actions)
                {
                    uint id = pair.Key; var action = pair.Value; var output = (FsmBool)Get(action, "storeValue");
                    check("heater wiring: unseeded and available circuit states project independently " + id, () =>
                    {
                        InstanceCall(f.Items, "ClearWiring"); action.OnEnter(); Require(!output.Value, "Unseeded circuit borrowed saved wiring.");
                        foreach (byte flags in new byte[] { 0, 1, 3 })
                        { receive(id, flags); action.OnEnter(); Require(output.Value == (flags == 3), "Wrong host circuit state."); saved(); }
                        var state = ((WiringReplica)Get(f.Items, "_wiringReplica")).Get(id)!;
                        InstanceCall(f.World, "OnWiringState", new WiringState { SourceId = id, Revision = state.Revision, Flags = 1 });
                        InstanceCall(f.World, "OnWiringState", new WiringState { SourceId = id, Revision = state.Revision - 1, Flags = 1 });
                        action.OnEnter(); Require(output.Value, "Conflicting or stale circuit undid repair."); saved();
                    });
                    check("heater wiring: native entry-only callback cadence stays intact " + id, () =>
                    {
                        receive(id, 3); f.Fire(id == 11 ? "Rear defrosting" : "Electrics?"); output.Value = false; Tick(f.Heater);
                        Require(!output.Value && !(bool)Get(action, "everyFrame"), "Projection added native reads.");
                        action.OnUpdate(); Require(output.Value, "Explicit native helper update did not project."); saved();
                    });
                }
                RunHeaterWireBoundaries(check, f, wires, actions[9], receive, saved);
                RunHeaterWireDecisions(check, f, receive, saved);
                check("heater wiring: session reset and reconnect cannot reuse stale circuit state", () =>
                {
                    foreach (uint id in wires.Keys) receive(id, 3);
                    InstanceCall(f.Items, "ReleaseSession");
                    foreach (var action in actions.Values) { action.OnEnter(); Require(!((FsmBool)Get(action, "storeValue")).Value, "Session clear retained wiring."); }
                    typeof(SessionManager).GetProperty("State", Members).GetSetMethod(true).Invoke(f.Session, new object[] { SessionState.Idle });
                    receive(9, 3); Require(((WiringReplica)Get(f.Items, "_wiringReplica")).Get(9) == null, "Disconnected guest accepted a wire update.");
                    f.Mode(false); foreach (uint id in wires.Keys) receive(id, 3);
                    foreach (var action in actions.Values) { action.OnEnter(); Require(((FsmBool)Get(action, "storeValue")).Value, "Reconnected circuit did not recover."); } saved();
                });
            }
        }

        private static void RunHeaterWireBoundaries(Action<string, Action> check, HeaterFixture f,
            Dictionary<uint, PlayMakerFSM> wires, FsmStateAction action, Action<uint, byte> receive, Action saved)
        {
            var output = (FsmBool)Get(action, "storeValue"); var source = wires[9]; var name = (FsmString)Get(action, "fsmName");
            receive(9, 1);
            check("heater wiring: cached same-object name and native fallback choose the actual source", () =>
            {
                var other = Empty(source.gameObject, "Other"); other.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true } }; other.Fsm.Init(other);
                try
                {
                    action.OnEnter(); name.Value = "Other"; action.OnUpdate(); Require(!output.Value, "Same-object cache changed wire source.");
                    Set(action, "goLastFrame", null!); action.OnUpdate(); Require(output.Value, "Unrelated same-object FSM was projected.");
                    foreach (string fallback in new[] { "", "missing" })
                    { name.Value = fallback; Set(action, "goLastFrame", null!); action.OnUpdate(); Require(!output.Value, "Native first-FSM fallback was not projected."); }
                }
                finally { name.Value = "Data"; Set(action, "goLastFrame", null!); UnityEngine.Object.DestroyImmediate(other); }
                saved();
            });
            check("heater wiring: other sources retain native reads and restoration recovers", () =>
            {
                var reference = f.Heater.FsmVariables.FindFsmGameObject("db_wHeaterControl"); reference.Value = f.Other.gameObject;
                try { action.OnEnter(); Require(output.Value, "Unrelated native source was projected."); } finally { reference.Value = source.gameObject; }
                action.OnEnter(); Require(!output.Value, "Restored wire did not project."); saved();
            });
            foreach (string kind in new[] { "source", "other circuit", "local alias of source", "global", "literal" })
                check("heater wiring: unsafe " + kind + " output cannot change saved wiring", () =>
                {
                    var locals = f.Heater.FsmVariables.BoolVariables; var globals = FsmVariables.GlobalVariables.BoolVariables; output.Value = true;
                    var aliased = wires[kind == "other circuit" ? 10u : 9u].FsmVariables.FindFsmBool("Installed");
                    try
                    {
                        if (kind == "source" || kind == "other circuit" || kind == "local alias of source")
                        {
                            Set(action, "storeValue", aliased);
                            if (kind != "source") { var list = new List<FsmBool>(locals); list.Add(aliased); f.Heater.FsmVariables.BoolVariables = list.ToArray(); }
                        }
                        else if (kind == "global") { var list = new List<FsmBool>(globals); list.Add(output); FsmVariables.GlobalVariables.BoolVariables = list.ToArray(); }
                        else Set(action, "storeValue", new FsmBool { Value = true });
                        action.OnEnter(); Require(output.Value, "Unsafe output was written."); saved();
                    }
                    finally { Set(action, "storeValue", output); f.Heater.FsmVariables.BoolVariables = locals; FsmVariables.GlobalVariables.BoolVariables = globals; }
                    action.OnEnter(); Require(!output.Value, "Restored local output did not recover.");
                });
            check("heater wiring: moved identity and missing metadata do not expose saved circuit state", () =>
            {
                var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true); var property = catalog.GetProperty("GuestEngineInputs", Static);
                var previous = property.GetValue(null, null); string oldName = source.name;
                try
                {
                    source.name = "moved saved circuit"; property.GetSetMethod(true).Invoke(null, new object?[] { null });
                    InstanceCall(f.Items, "ClearWiring"); action.OnEnter(); Require(!output.Value, "Lost metadata exposed saved installation.");
                    receive(9, 3); action.OnEnter(); Require(output.Value, "Known moved source lost its circuit identity."); saved();
                }
                finally { source.name = oldName; property.GetSetMethod(true).Invoke(null, new[] { previous }); }
            });
        }

        private static void RunHeaterWireDecisions(Action<string, Action> check, HeaterFixture f, Action<uint, byte> receive, Action saved)
        {
            var power = NativeBagPartChecks.State(f.Heater, "Electrics?"); var rear = NativeBagPartChecks.State(f.Heater, "Rear defrosting");
            var oldPower = power.Actions; var oldRear = rear.Actions; var powerTransitions = power.Transitions; var rearTransitions = rear.Transitions;
            var powerRaw = (List<object>)FindState(f.HeaterRow, "Electrics?")["actions"];
            var rearRaw = (List<object>)FindState(f.HeaterRow, "Rear defrosting")["actions"];
            try
            {
                power.Actions = (FsmStateAction[])power.Actions.Clone(); power.Actions[5] = NativeAction((Dictionary<string, object>)powerRaw[5], f.Heater); power.Actions[5].Init(power);
                power.Transitions = new[] { new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("PROCEED"), ToState = "Rear defrosting" } };
                for (int bits = 0; bits < 8; bits++)
                {
                    int mask = bits;
                    check("heater wiring: native supply gate requires both host circuits and heater " + mask, () =>
                    {
                        receive(9, (byte)((mask & 1) != 0 ? 3 : 1)); receive(10, (byte)((mask & 2) != 0 ? 3 : 1)); f.Receive((byte)((mask & 4) != 0 ? 3 : 1), (mask & 4) != 0 ? 80 : 0);
                        f.Fire("Electrics?"); Require(f.Heater.ActiveStateName == (mask == 7 ? "Rear defrosting" : "Electrics?"), "Native heater gate used saved wiring or ignored a prerequisite."); saved();
                    });
                }
                rear.Actions = (FsmStateAction[])rear.Actions.Clone();
                foreach (int index in new[] { 1, 5 }) { rear.Actions[index] = NativeAction((Dictionary<string, object>)rearRaw[index], f.Heater); rear.Actions[index].Init(rear); }
                rear.Transitions = new[] { new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("FINISHED"), ToState = "Blower wear?" } };
                foreach (bool connected in new[] { false, true })
                {
                    bool on = connected;
                    check("heater wiring: native rear defroster demand follows host circuit " + on, () =>
                    {
                        receive(11, on ? (byte)3 : (byte)1);
                        var consumption = f.Heater.FsmVariables.FindFsmFloat("ConsumptionRate"); consumption.Value = 0;
                        f.Fire("Rear defrosting");
                        Require(consumption.Value == (on ? f.Heater.FsmVariables.FindFsmFloat("ConsumptionRateDefroster").Value : 0), "Native wire gate did not control defroster demand."); saved();
                    });
                }
            }
            finally { power.Actions = oldPower; rear.Actions = oldRear; power.Transitions = powerTransitions; rear.Transitions = rearTransitions; }
        }
    }
}
