using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private sealed partial class Fixture
        {
            private readonly Dictionary<uint, PlayMakerFSM> _savedWires = new Dictionary<uint, PlayMakerFSM>();
            private readonly Dictionary<uint, uint> _wireRevisions = new Dictionary<uint, uint>();
            private void ConfigureWireSource(object rule, FsmGameObject reference)
            {
                uint id = (uint)Get(rule, "Id");
                if (!_savedWires.TryGetValue(id, out var data))
                {
                    data = Data(PathObject(Car, (string)Get(rule, "Path")), null, 0);
                    data.FsmVariables.FindFsmBool("Installed").Value = true;
                    if (WiringPolicy.SupportsBolted(id))
                    {
                        var booleans = new List<FsmBool>(data.FsmVariables.BoolVariables) {
                            new FsmBool { Name = "Bolted", UseVariable = true, Value = true } };
                        data.FsmVariables.BoolVariables = booleans.ToArray();
                    }
                    NativeBagPartChecks.Start(data); _savedWires.Add(id, data);
                }
                reference.Value = data.gameObject;
            }
            internal void SetWire(uint id, bool installed, bool bolted, bool available = true)
            {
                _wireRevisions.TryGetValue(id, out uint revision); _wireRevisions[id] = ++revision;
                var state = new WiringState { SourceId = id, Revision = revision,
                    Flags = available ? (byte)(WiringState.Available | (installed ? WiringState.Installed : 0) | (bolted ? WiringState.Bolted : 0)) : (byte)0 };
                Call(Sync, "OnWiringState", PacketCodec.Decode(PacketCodec.Encode(state)));
                Require(((WiringReplica)Get(Sync, "_wiringReplica")).Get(id)?.Revision == revision, "Fixture wire state rejected.");
            }
            private void AssertSavedWires()
            {
                foreach (var wire in _savedWires)
                    Require(wire.Value.FsmVariables.FindFsmBool("Installed").Value
                        && (!WiringPolicy.SupportsBolted(wire.Key) || wire.Value.FsmVariables.FindFsmBool("Bolted").Value),
                        "Engine input projection changed saved wiring.");
            }
            internal PlayMakerFSM SavedWire(uint id) => _savedWires[id];
            internal PlayMakerFSM MakeHostWire(uint id)
            {
                string path = "CORRIS/Wiring/DatabaseWiring/" + WiringPolicy.Name(id);
                var obj = PathObject(Car, path);
                foreach (var existing in obj.GetComponents<PlayMakerFSM>()) UnityEngine.Object.DestroyImmediate(existing);
                var row = FindNativeRow(path, "Data"); var data = NativeBagPartChecks.MakeFsm(obj, row);
                data.Fsm.Init(data);
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    var state = NativeBagPartChecks.State(data, (string)stateRow["name"]);
                    bool native = state.Name == "Tightness?" || state.Name == "Bolted" || state.Name == "Unbolted";
                    var raw = (List<object>)stateRow["actions"]; var actions = new FsmStateAction[raw.Count];
                    for (int i = 0; i < actions.Length; i++) actions[i] = native ? NativeAction((Dictionary<string, object>)raw[i], data) : new Quiet();
                    state.Actions = actions; foreach (var action in actions) action.Init(state);
                    if (!native) state.Transitions = new FsmTransition[0];
                }
                return data;
            }
            internal List<WiringState> CaptureWires()
            {
                var values = new List<WiringState>();
                foreach (WiringState state in (IEnumerable)Call(Sync, "BuildWiringStates")!)
                    values.Add((WiringState)PacketCodec.Decode(PacketCodec.Encode(state)));
                return values;
            }
        }

        internal static void RunWiring(Action<string, Action> check)
        {
            foreach (string consumer in new[] { "Cylinders", "Starter", "Electrics" })
            using (var f = new Fixture(consumer == "Cylinders" ? "VIN212" : consumer == "Starter" ? "VIN130" : "VIN133", consumer, "wiring-engine-input-probe.json"))
            {
                var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
                var profile = catalog.GetProperty("GuestEngineInputs", Static).GetValue(null, null);
                foreach (object entry in (IEnumerable)Get(profile, "Entries"))
                {
                    var wire = Get(entry, "WiringSource");
                    if (wire == null || (string)Get(entry, "Fsm") != consumer) continue;
                    uint id = (uint)Get(wire, "Id");
                    foreach (object read in (IEnumerable)Get(entry, "Readers"))
                    {
                        string state = (string)Get(read, "State"), field = (string)Get(read, "Variable");
                        int index = (int)Get(read, "ActionIndex"); var action = f.Action(state, index);
                        var output = f.Reader.FsmVariables.FindFsmBool((string)Get(read, "Output"));
                        check("wiring inputs: " + consumer + " " + state + " #" + index + " uses only accepted host " + field, () =>
                        {
                            Call(f.Sync, "RestoreGuestEngineInputs"); action.OnEnter();
                            Require(output.Value && f.Target(action) == f.SavedWire(id).gameObject, "Native wiring cache did not warm from saved source.");
                            ((WiringReplica)Get(f.Sync, "_wiringReplica")).Clear();
                            Require(f.Prepare(), "Wire reader did not bind without host data."); action.OnEnter(); Require(!output.Value, "Wire reader borrowed saved wiring.");
                            var proxy = f.Target(action);
                            Require(proxy != f.SavedWire(id).gameObject && proxy.GetComponent<Rigidbody>() == null, "Wire input retained saved or physical ownership.");
                            for (int bits = 0; bits < (WiringPolicy.SupportsBolted(id) ? 4 : 2); bits++)
                            {
                                bool installed = (bits & 1) != 0, bolted = (bits & 2) != 0, before = output.Value;
                                f.SetWire(id, installed, bolted); Require(f.Prepare(), "Wire state failed preparation.");
                                Require(output.Value == before && f.Target(action) == proxy, "Wire arrival replayed a native read or replaced a proxy.");
                                action.OnEnter(); Require(output.Value == (field == "Bolted" ? bolted : installed), "Native wire field was substituted.");
                            }
                            f.SetWire(id, false, false, false); f.Prepare(); action.OnEnter(); Require(!output.Value, "Unavailable source retained true.");
                            f.SetWire(id, true, WiringPolicy.SupportsBolted(id)); f.Prepare(); action.OnEnter(); Require(output.Value, "Late wire data did not recover.");
                            var stale = ((WiringReplica)Get(f.Sync, "_wiringReplica")).Get(id)!; stale.Revision--; stale.Flags = 0;
                            Call(f.Sync, "OnWiringState", stale); f.Prepare(); action.OnEnter(); Require(output.Value, "Stale wire data undid a repair.");
                            f.AssertSaved();
                        });
                    }
                }
                for (uint id = 1; id <= WiringPolicy.SourceCount; id++) f.SetWire(id, true, WiringPolicy.SupportsBolted(id));
                f.Receive(90, 16, .7f, true);
                if (consumer == "Cylinders")
                {
                    f.ReceiveVariant(f.Auxiliary("VIN131").Variants[0], 90, 0, 8, true);
                    check("wiring inputs: native ignition follows host coil harness removal and repair", () =>
                    {
                        foreach (bool connected in new[] { false, true })
                        {
                            f.SetWire(1, connected, false); Require(f.Prepare(), "Ignition wire failed."); f.Fire("Ignition");
                            Require(f.Reader.ActiveStateName == (connected ? "Plug data" : "Not Ok"), "Host wire did not decide ignition."); f.AssertSaved();
                        }
                    });
                }
                if (consumer == "Starter")
                    for (int bits = 0; bits < 8; bits++)
                    {
                        int mask = bits;
                        check("wiring inputs: native starter terminal combination " + mask, () =>
                        {
                            f.SetWire(2, false, (mask & 1) != 0); f.SetWire(3, false, (mask & 2) != 0); f.SetWire(4, (mask & 4) != 0, false);
                            Require(f.Prepare(), "Starter wiring failed."); f.Fire("Wiring");
                            Require(f.Reader.ActiveStateName == (mask == 7 ? "Check Flywheel" : "Wait"), "Starter confused Installed with Bolted."); f.AssertSaved();
                        });
                    }
                if (consumer == "Electrics")
                    for (int bits = 0; bits < 4; bits++)
                    {
                        int mask = bits;
                        check("wiring inputs: native charging connection combination " + mask, () =>
                        {
                            f.SetWire(6, (mask & 1) != 0, false); f.SetWire(7, (mask & 2) != 0, false);
                            f.Reader.FsmVariables.FindFsmBool("Battery").Value = true;
                            Require(f.Prepare(), "Charging wire failed."); f.Fire("Check alternator");
                            Require(f.Reader.ActiveStateName == (mask == 3 ? "Engine running?" : "No charge"), "Charging ignored host wiring."); f.AssertSaved();
                        });
                    }
                check("wiring inputs: " + consumer + " disconnect clears accepted wiring and restores saved readers", () =>
                {
                    Call(f.Sync, "ReleaseSession");
                    for (uint id = 1; id <= WiringPolicy.SourceCount; id++) Require(((WiringReplica)Get(f.Sync, "_wiringReplica")).Get(id) == null, "Wire state survived disconnect.");
                    f.AssertSaved();
                });
            }
            RunWiringCapture(check);
        }

        private static void RunWiringCapture(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN212", "Cylinders", "wiring-engine-input-probe.json"))
            {
                Property(f.Session, "IsHost", true);
                var data = new Dictionary<uint, PlayMakerFSM>();
                for (uint id = 1; id <= WiringPolicy.SourceCount; id++) data.Add(id, f.MakeHostWire(id));
                foreach (var pair in data)
                {
                    uint id = pair.Key; var wire = pair.Value;
                    Func<WiringState> capture = () => f.CaptureWires().Find(s => s.SourceId == id)!;
                    Func<string> detail = () => " flags=" + capture().Flags + " state=" + wire.ActiveStateName + " enabled=" + wire.enabled
                        + " initialized=" + wire.Fsm.Initialized + " started=" + wire.Fsm.Started + " installed=" + wire.FsmVariables.FindFsmBool("Installed").Value
                        + " found=" + (GameObject.Find("CORRIS/Wiring/DatabaseWiring/" + WiringPolicy.Name(id)) == wire.gameObject);
                    check("wiring capture: " + WiringPolicy.Name(id) + " waits for native load and bolt completion", () =>
                    {
                        wire.FsmVariables.FindFsmBool("Installed").Value = true;
                        Require(capture().Flags == 0, "Unstarted database published default wiring.");
                        NativeBagPartChecks.Start(wire); NativeBagPartChecks.Fire(wire, "Load game");
                        Require(capture().Flags == 0, "Loading database published intermediate wiring.");
                        if (WiringPolicy.SupportsBolted(id))
                        {
                            wire.FsmVariables.FindFsmFloat("Tightness").Value = 0; NativeBagPartChecks.Fire(wire, "Tightness?");
                            Require(capture().Flags == 3 && wire.ActiveStateName == "Set bolt", "Native untightened terminal was not published." + detail());
                            wire.FsmVariables.FindFsmFloat("Tightness").Value = 8; NativeBagPartChecks.Fire(wire, "Tightness?");
                            Require(capture().Flags == 7, "Native tightened terminal was not published.");
                        }
                        else { NativeBagPartChecks.Fire(wire, "Basic state"); Require(capture().Flags == 3, "Settled wiring not published." + detail()); }
                    });
                    check("wiring capture: " + WiringPolicy.Name(id) + " snapshots preserve pending updates and unavailable recovery", () =>
                    {
                        var first = capture(); var publication = (WiringPublication)((IDictionary)Get(f.Sync, "_wiringPublications"))[id];
                        publication.MarkBroadcast(first.Revision); Require(!publication.NeedsBroadcast, "Initial publication not acknowledged.");
                        wire.FsmVariables.FindFsmBool("Installed").Value = false;
                        var snapshot = capture(); Require(snapshot.Revision == first.Revision + 1 && publication.NeedsBroadcast, "Join snapshot swallowed wire update.");
                        Require(capture().Revision == snapshot.Revision && publication.NeedsBroadcast, "Live broadcast lost snapshot revision.");
                        wire.gameObject.SetActive(false); var absent = capture(); Require(absent.Flags == 0 && absent.Revision == snapshot.Revision + 1, "Missing source retained old state.");
                        wire.gameObject.SetActive(true); NativeBagPartChecks.Fire(wire, WiringPolicy.SupportsBolted(id) ? "Set bolt" : "Basic state");
                        Require(capture().Flags == snapshot.Flags && capture().Revision == absent.Revision + 1, "Reappearing wire did not recover.");
                    });
                }
                foreach (var pair in data)
                {
                    uint id = pair.Key; var wire = pair.Value;
                    Func<WiringState> capture = () => f.CaptureWires().Find(s => s.SourceId == id)!;
                    check("wiring capture: " + WiringPolicy.Name(id) + " rejects changed live paths and duplicate siblings", () =>
                    {
                        string name = wire.name; var parent = wire.transform.parent; byte flags = capture().Flags;
                        var other = new GameObject("unrelated wiring parent"); GameObject? duplicate = null;
                        try
                        {
                            Require(flags != 0, "Fixture wiring was not ready.");
                            wire.name = name + " moved";
                            Require(capture().Flags == 0, "Renamed wire retained input.");
                            wire.name = name; wire.transform.SetParent(other.transform, false);
                            Require(capture().Flags == 0, "Wrong-parent wire retained input.");
                            wire.transform.SetParent(parent, false);
                            Require(capture().Flags == flags, "Restored wire did not recover immediately.");
                            duplicate = new GameObject(name); duplicate.transform.SetParent(parent, false);
                            Require(capture().Flags == 0, "Duplicate wire path was accepted.");
                            duplicate.transform.SetAsFirstSibling();
                            Require(capture().Flags == 0, "Reordered duplicate wire path was accepted.");
                            UnityEngine.Object.DestroyImmediate(duplicate);
                            Require(capture().Flags == flags, "Removing duplicate did not recover immediately.");
                        }
                        finally
                        {
                            wire.name = name; wire.transform.SetParent(parent, false);
                            if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate);
                            UnityEngine.Object.DestroyImmediate(other);
                        }
                    });
                }
                check("wiring capture: replacing the scene root cannot reuse a preceding capture", () =>
                {
                    var wire = data[1]; var root = wire.transform;
                    while (root.parent != null) root = root.parent;
                    string name = root.name; byte flags = f.CaptureWires().Find(s => s.SourceId == 1)!.Flags;
                    GameObject? replacement = null;
                    try
                    {
                        root.name += " retired"; replacement = new GameObject(name);
                        foreach (var state in f.CaptureWires()) Require(state.Flags == 0, "Capture reused the previous root.");
                        UnityEngine.Object.DestroyImmediate(replacement); root.name = name;
                        Require(f.CaptureWires().Find(s => s.SourceId == 1)!.Flags == flags, "Restored root remained unavailable.");
                    }
                    finally
                    {
                        root.name = name;
                        if (replacement != null) UnityEngine.Object.DestroyImmediate(replacement);
                    }
                });
                check("wiring capture: broken source is isolated and repairs without losing other wires", () =>
                {
                    var wire = data[1]; var saved = wire.FsmVariables.BoolVariables; wire.FsmVariables.BoolVariables = new FsmBool[0];
                    try { var states = f.CaptureWires(); Require(states.Count == WiringPolicy.SourceCount && states.Find(s => s.SourceId == 1)!.Flags == 0
                        && states.Find(s => s.SourceId == 2)!.Flags != 0, "A broken wire disabled unrelated captures."); }
                    finally { wire.FsmVariables.BoolVariables = saved; }
                    Require(f.CaptureWires().Find(s => s.SourceId == 1)!.Flags != 0, "Repaired native field remained unavailable.");
                });
                check("wiring capture: guests cannot publish and hosts refuse incoming wire state", () =>
                {
                    Call(f.Sync, "ClearWiring"); Call(f.Sync, "OnWiringState", new WiringState { SourceId = 1, Revision = 500, Flags = 3 });
                    Require(((WiringReplica)Get(f.Sync, "_wiringReplica")).Get(1) == null, "Host accepted a peer wiring record.");
                    Property(f.Session, "IsHost", false); Require(f.CaptureWires().Count == 0, "Guest published saved wiring.");
                });
            }
        }
    }
}
