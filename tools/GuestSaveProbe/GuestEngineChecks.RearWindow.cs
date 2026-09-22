using System;
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
        private static void RunRearWindowHeater(Action<string, Action> check)
        {
            using (var f = new HeaterFixture())
            {
                var reader = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                var parser = Activator.CreateInstance(reader, Members, null, new object[] {
                    File.ReadAllText(Path.Combine(Application.dataPath, "../rear-window-heat-probe.json")) }, null);
                var rows = (List<object>)((Dictionary<string, object>)reader.GetMethod("ReadObject", Members).Invoke(parser, null))["fsms"];
                var bodyRow = NativeBagPartChecks.Find(rows, "CORRIS/BODY", "Save");
                var heaterRow = NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Electricity/PowerON/HeaterUnit", "Function");
                var obj = PathObject(f.Root, "CORRIS/BODY"); obj.SetActive(false);
                var body = NativeBagPartChecks.MakeFsm(obj, bodyRow); body.Fsm.Init(body);
                foreach (var state in body.FsmStates)
                { state.Transitions = new FsmTransition[0]; state.Actions = new[] { new Quiet() }; state.Actions[0].Init(state); }
                obj.SetActive(true);
                var element = body.FsmVariables.FindFsmBool("HeatingSprites");
                var rear = NativeBagPartChecks.State(f.Heater, "Rear defrosting");
                var raw = (List<object>)FindState(heaterRow, rear.Name)["actions"];
                rear.Actions[2] = NativeAction((Dictionary<string, object>)raw[2], f.Heater); rear.Actions[2].Init(rear);
                var read = rear.Actions[2]; ((FsmOwnerDefault)Get(read, "gameObject")).GameObject.Value = body.gameObject;
                var output = (FsmBool)Get(read, "storeValue");
                f.Settled();
                RunRearWindowCapture(check, f, body, bodyRow);
                element.Value = true; body.FsmVariables.FindFsmString("VINcode").Value = "guest option";
                NativeBagPartChecks.Fire(body, "State 4");
                check("rear window: host and solo read their native body option", () =>
                { f.Mode(true); read.OnEnter(); Require(output.Value, "Host body read was projected."); });
                f.SavedWear.Value = 86; f.PartWear.Value = 91; f.Battery.FsmVariables.FindFsmFloat("Charge").Value = 120;
                f.Mode(false); Require((bool)Call("Prepare", true)!, "Rear-window fixture preparation failed.");
                Action saved = () =>
                {
                    f.Saved(); Require(element.Value && body.enabled && body.FsmVariables.FindFsmString("VINcode").Value == "guest option",
                        "Rear-window projection changed saved body option or paused its Save FSM.");
                };
                check("rear window: unseeded host option cannot borrow the saved element", () =>
                { InstanceCall(f.Items, "ClearHeater"); read.OnEnter(); Require(!output.Value, "Unseeded option borrowed saved strips."); saved(); });
                foreach (byte flags in new byte[] { 0, 1, 3 })
                {
                    byte value = flags;
                    check("rear window: independent host element flags " + value, () =>
                    { f.Receive(0, 0, value); read.OnEnter(); Require(output.Value == (value == 3), "Rear option depended on blower installation or saved body."); saved(); });
                }
                check("rear window: packet arrival and entry-only cadence preserve native scratch", () =>
                {
                    f.Receive(3, 80, 3); read.OnEnter(); output.Value = false; f.Receive(3, 79, 3);
                    Require(!output.Value && !(bool)Get(read, "everyFrame"), "Arrival or cadence rewrote scratch.");
                    f.Fire("Rear defrosting"); output.Value = false; Tick(f.Heater); Require(!output.Value, "Entry-only read repeated on update.");
                    read.OnUpdate(); Require(output.Value, "Explicit native helper update did not project."); saved();
                });
                RunRearWindowReadBoundaries(check, f, body, read, saved);
                RunRearWindowGate(check, f, body, heaterRow, saved);
                check("rear window: conflicts stale updates teardown and reconnect preserve authority", () =>
                {
                    f.Receive(3, 80, 3); var old = ((HeaterReplica)Get(f.Items, "_heaterReplica")).Get()!;
                    var conflict = old.Copy(); conflict.RearWindowFlags = 1; InstanceCall(f.World, "OnHeaterState", conflict);
                    f.Receive(3, 80, 1); InstanceCall(f.World, "OnHeaterState", old); read.OnEnter(); Require(!output.Value, "Stale state undid element removal.");
                    InstanceCall(f.Items, "ReleaseSession"); read.OnEnter(); Require(!output.Value, "Teardown retained rear option.");
                    typeof(SessionManager).GetProperty("State", Members).GetSetMethod(true).Invoke(f.Session, new object[] { SessionState.Idle });
                    InstanceCall(f.World, "OnHeaterState", old); Require(((HeaterReplica)Get(f.Items, "_heaterReplica")).Get() == null, "Disconnected guest accepted rear state.");
                    f.Mode(false); f.Receive(3, 80, 3); read.OnEnter(); Require(output.Value, "Reconnected rear option did not recover."); saved();
                });
            }
        }

        private static void RunRearWindowCapture(Action<string, Action> check, HeaterFixture f, PlayMakerFSM body, Dictionary<string, object> row)
        {
            var element = body.FsmVariables.FindFsmBool("HeatingSprites"); element.Value = true;
            check("rear window capture: unstarted body is unavailable without clearing blower state", () =>
            { var state = f.Capture(); Require(state.RearWindowFlags == 0 && state.Flags == 3 && state.Wear == 95, "Unstarted body leaked its default or cleared heater."); });
            NativeBagPartChecks.Start(body);
            foreach (string stateName in new[] { "Load 2", "Disable save 2", "Window Heater?", "State 2", "State 3", "Facelift?" })
            {
                string name = stateName;
                check("rear window capture: native transition " + name + " remains unavailable", () =>
                { NativeBagPartChecks.Fire(body, name); Require(f.Capture().RearWindowFlags == 0, "Transitional option was published."); });
            }
            foreach (string stateName in new[] { "State 1", "State 4", "Save" }) foreach (bool enabled in new[] { false, true })
            {
                string name = stateName; bool present = enabled;
                check("rear window capture: stable " + name + " option " + present, () =>
                { element.Value = present; NativeBagPartChecks.Fire(body, name); Require(f.Capture().RearWindowFlags == (present ? 3 : 1), "Stable native body option was lost."); });
            }
            foreach (string name in new[] { "Window Heater?", "State 2", "State 3" })
            {
                var state = NativeBagPartChecks.State(body, name); var raw = (List<object>)FindState(row, name)["actions"];
                state.Actions = new FsmStateAction[raw.Count];
                for (int i = 0; i < raw.Count; i++)
                { state.Actions[i] = name == "Window Heater?" && i == 0 ? new Quiet() : NativeAction((Dictionary<string, object>)raw[i], body); state.Actions[i].Init(state); }
                state.Transitions = name == "Window Heater?"
                    ? new[] { new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("ENABLE"), ToState = "State 2" }, new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("DISABLE"), ToState = "State 3" } }
                    : new[] { new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("FINISHED"), ToState = "State 4" } };
            }
            foreach (string code in new[] { "M", "-", "S", "" })
            {
                string value = code;
                check("rear window capture: native VIN option comparison " + (value == "" ? "empty" : value), () =>
                {
                    body.FsmVariables.FindFsmString("VINcode").Value = value; NativeBagPartChecks.Fire(body, "Window Heater?");
                    Require(body.ActiveStateName == "State 4" && f.Capture().RearWindowFlags == (value == "M" || value == "-" ? 1 : 3), "Native option comparison or boolean assignment changed.");
                });
            }
            element.Value = true; NativeBagPartChecks.Fire(body, "State 4");
            foreach (string mutation in new[] { "disabled", "inactive", "moved", "missing variable", "ambiguous", "wrong FSM" })
            {
                string kind = mutation;
                check("rear window capture: " + kind + " source clears only the rear option and repairs", () =>
                {
                    string name = body.name; string fsmName = body.FsmName; var vars = body.FsmVariables.BoolVariables; PlayMakerFSM? duplicate = null;
                    try
                    {
                        if (kind == "disabled") body.enabled = false;
                        else if (kind == "inactive") body.gameObject.SetActive(false);
                        else if (kind == "moved") body.name = "moved body";
                        else if (kind == "missing variable") body.FsmVariables.BoolVariables = new FsmBool[0];
                        else if (kind == "ambiguous") duplicate = Empty(body.gameObject, "Save");
                        else body.FsmName = "Other";
                        var state = f.Capture(); Require(state.RearWindowFlags == 0 && state.Flags == 3 && state.Wear == 95, "Bad body source retained element or cleared blower.");
                    }
                    finally
                    {
                        if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate);
                        body.name = name; body.FsmName = fsmName; body.FsmVariables.BoolVariables = vars;
                        body.gameObject.SetActive(true); body.enabled = true; NativeBagPartChecks.Fire(body, "State 4");
                    }
                    Require(f.Capture().RearWindowFlags == 3, "Repaired body source did not recover.");
                });
            }
            check("rear window capture: element remains independent of a missing heater mount", () =>
            {
                f.Mount.gameObject.SetActive(false);
                try { var state = f.Capture(); Require(state.Flags == 0 && state.RearWindowFlags == 3, "Element depended on blower mount availability."); }
                finally { f.Settled(); }
            });
            check("rear window capture: rear-only changes retain pending snapshots and keepalive", () =>
            {
                var publication = (HeaterPublication)Get(f.Items, "_heaterPublication"); var first = f.Capture(); publication.MarkBroadcast(first.Revision);
                element.Value = false; var next = f.Capture(); Require(next.Revision == first.Revision + 1 && next.RearWindowFlags == 1, "Rear-only update was not published.");
                next.RearWindowFlags = 3; var snapshot = f.Capture(); Require(snapshot.RearWindowFlags == 1 && publication.NeedsBroadcast, "Snapshot mutated or consumed pending body input.");
                InstanceCall(f.Items, "ProcessHeater", f.Session); Require(!publication.NeedsBroadcast && (float)Get(f.Items, "_nextHeaterKeepalive") > Time.unscaledTime, "Rear state did not establish reliable keepalive.");
            });
        }

        private static void RunRearWindowReadBoundaries(Action<string, Action> check, HeaterFixture f, PlayMakerFSM body, FsmStateAction read, Action saved)
        {
            var output = (FsmBool)Get(read, "storeValue"); var name = (FsmString)Get(read, "fsmName"); f.Receive(3, 80, 1);
            check("rear window: native source cache and first-FSM fallback stay intact", () =>
            {
                var other = Empty(body.gameObject, "Other"); other.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "HeatingSprites", UseVariable = true, Value = true } }; other.Fsm.Init(other);
                try
                {
                    read.OnEnter(); name.Value = "Other"; read.OnUpdate(); Require(!output.Value, "Same-object name edit changed native cached source.");
                    Set(read, "goLastFrame", null!); read.OnUpdate(); Require(output.Value, "Unrelated source was projected.");
                    foreach (string fallback in new[] { "", "missing" })
                    { name.Value = fallback; Set(read, "goLastFrame", null!); read.OnUpdate(); Require(!output.Value, "Native source fallback borrowed saved body option."); }
                }
                finally { name.Value = "Save"; Set(read, "goLastFrame", null!); UnityEngine.Object.DestroyImmediate(other); }
                saved();
            });
            foreach (string kind in new[] { "source", "source alias in locals", "global", "literal" })
                check("rear window: unsafe " + kind + " output preserves the saved body", () =>
                {
                    var locals = f.Heater.FsmVariables.BoolVariables; var globals = FsmVariables.GlobalVariables.BoolVariables; output.Value = true;
                    try
                    {
                        var source = body.FsmVariables.FindFsmBool("HeatingSprites");
                        if (kind == "literal") Set(read, "storeValue", new FsmBool { Value = true });
                        else if (kind == "global") { var list = new List<FsmBool>(globals); list.Add(output); FsmVariables.GlobalVariables.BoolVariables = list.ToArray(); }
                        else
                        {
                            Set(read, "storeValue", source);
                            if (kind != "source") { var list = new List<FsmBool>(locals); list.Add(source); f.Heater.FsmVariables.BoolVariables = list.ToArray(); }
                        }
                        read.OnEnter(); Require(output.Value, "Unsafe output was assigned."); saved();
                    }
                    finally { Set(read, "storeValue", output); f.Heater.FsmVariables.BoolVariables = locals; FsmVariables.GlobalVariables.BoolVariables = globals; }
                    read.OnEnter(); Require(!output.Value, "Restored local output did not recover.");
                });
            check("rear window: known moved source survives lost metadata without saved fallback", () =>
            {
                var property = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true).GetProperty("GuestEngineInputs", Static);
                var original = property.GetValue(null, null); string nameBefore = body.name;
                try
                {
                    body.name = "moved saved body"; property.GetSetMethod(true).Invoke(null, new object?[] { null });
                    InstanceCall(f.Items, "ClearHeater"); read.OnEnter(); Require(!output.Value, "Missing metadata exposed saved element.");
                    f.Receive(3, 80, 3); read.OnEnter(); Require(output.Value, "Known body source lost its host input."); saved();
                }
                finally { body.name = nameBefore; property.GetSetMethod(true).Invoke(null, new[] { original }); }
            });
        }

        private static void RunRearWindowGate(Action<string, Action> check, HeaterFixture f, PlayMakerFSM body,
            Dictionary<string, object> heaterRow, Action saved)
        {
            var rear = NativeBagPartChecks.State(f.Heater, "Rear defrosting"); var raw = (List<object>)FindState(heaterRow, rear.Name)["actions"];
            var oldActions = (FsmStateAction[])rear.Actions.Clone(); var oldTransitions = rear.Transitions;
            var wire = Empty(PathObject(f.Root, "CORRIS/Wiring/DatabaseWiring/WiringFuseboxWindow"), "Data");
            wire.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = false } }; wire.Fsm.Init(wire);
            f.Heater.FsmVariables.FindFsmGameObject("WiringWindowDefroster").Value = wire.gameObject;
            // Exercise the native heat write against an inert cabin, not a saved car.
            var cabin = Empty(Child(f.Extras, "rear-window cabin sink"), "Freezing"); AddFloat(cabin, "CutoffRear", -.4f); cabin.Fsm.Init(cabin);
            f.Heater.FsmVariables.FindFsmGameObject("InteriorTemp").Value = cabin.gameObject;
            try
            {
                foreach (int index in new[] { 0, 1, 3, 4, 5, 6 }) { rear.Actions[index] = NativeAction((Dictionary<string, object>)raw[index], f.Heater); rear.Actions[index].Init(rear); }
                rear.Transitions = new[] { new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("FINISHED"), ToState = "Blower wear?" } };
                for (int mask = 0; mask < 8; mask++)
                {
                    int bits = mask;
                    check("rear window: native circuit element and control gate " + bits, () =>
                    {
                        InstanceCall(f.World, "OnWiringState", PacketCodec.Decode(PacketCodec.Encode(new WiringState { SourceId = 11, Revision = (uint)bits + 1, Flags = (byte)((bits & 1) != 0 ? 3 : 1) })));
                        f.Receive(3, 80, (byte)((bits & 2) != 0 ? 3 : 1)); f.Heater.FsmVariables.FindFsmBool("GlassDefrosting").Value = (bits & 4) != 0;
                        var consumption = f.Heater.FsmVariables.FindFsmFloat("ConsumptionRate"); var cutoff = cabin.FsmVariables.FindFsmFloat("CutoffRear"); consumption.Value = 0; cutoff.Value = -.4f;
                        f.Fire("Rear defrosting");
                        Require(consumption.Value == (bits == 7 ? f.Heater.FsmVariables.FindFsmFloat("ConsumptionRateDefroster").Value : 0), "Native demand bypassed a host prerequisite.");
                        Require(Math.Abs(cutoff.Value - (bits == 7 ? -.396f : -.4f)) < .000001f, "Native rear-window heat bypassed a prerequisite or failed its addition."); saved();
                    });
                }
            }
            finally { rear.Actions = oldActions; rear.Transitions = oldTransitions; }
        }
    }
}
