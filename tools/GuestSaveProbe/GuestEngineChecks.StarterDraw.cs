using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Transport;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineChecks
    {
        private sealed class StarterPacket
        {
            internal Channel Channel;
            internal IMessage Message = null!;
        }
        private sealed class StarterTransport : ITransport
        {
            internal readonly List<StarterPacket> Packets = new List<StarterPacket>();
            public bool IsHost => false;
            public event Action<PeerId>? PeerConnected { add { } remove { } }
            public event Action<PeerId, string>? PeerDisconnected { add { } remove { } }
            public event Action<PeerId, byte[], Channel>? PacketReceived { add { } remove { } }
            public void Send(PeerId peer, byte[] payload, Channel channel) => Send(peer, payload, payload.Length, channel);
            public void Send(PeerId peer, byte[] payload, int length, Channel channel)
            {
                var bytes = new byte[length]; Array.Copy(payload, bytes, length);
                Packets.Add(new StarterPacket { Channel = channel, Message = PacketCodec.Decode(bytes) });
            }
            public void Update() { }
            public void Dispose() { }
        }

        private static void RunStarterDraw(Action<string, Action> check)
        {
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
            object? rule = null;
            foreach (object writer in (IEnumerable)Get(catalog.GetProperty("GuestEngineProtection", Static).GetValue(null, null), "Writers"))
                if ((string)Get(writer, "Fsm") == "Starter") rule = writer;
            Require(rule != null, "Starter protection rule missing.");
            var callback = Guard.GetField("PrepareInputs", Static).GetValue(null);
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protect = policy.GetType().GetProperty("ProtectWorld", Members); object previousProtect = protect.GetValue(policy, null);
            var session = SessionManager.Instance ?? throw new InvalidOperationException("Missing native session.");
            var host = typeof(SessionManager).GetProperty("IsHost", Members); object previousHost = host.GetValue(session, null);
            var sessionState = typeof(SessionManager).GetProperty("State", Members); object previousState = sessionState.GetValue(session, null);
            var localId = typeof(SessionManager).GetProperty("LocalPlayerId", Members); object previousId = localId.GetValue(session, null);
            object oldTransport = Get(session, "_transport"), oldPeer = Get(session, "_hostPeer");
            var world = Core.GetType("WinterMP.Core.Sync.WorldSyncManager", true).GetProperty("Instance", Static).GetValue(null, null);
            object oldItems = Get(world, "_items"), oldVehicles = Get(world, "_vehicles"), oldReady = Get(world, "_syncReady");
            var bridge = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.WorldSyncBridge", true), Members, null,
                new object[] { world, new Dictionary<PlayMakerFSM, bool>() }, null);
            var items = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true), Members, null, new[] { bridge }, null);
            var vehicles = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.VehicleWorldSync", true), Members, null, new[] { bridge, items }, null);
            InstanceCall(items, "BindVehicles", vehicles); InstanceCall(bridge, "BindItems", items);
            var root = new GameObject("CORRIS"); root.SetActive(false);
            var body = root.AddComponent<Rigidbody>(); body.useGravity = false; body.isKinematic = true;
            var extras = new GameObject("starter probe inputs"); extras.SetActive(false);
            var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
            const uint id = 709178; Set(item, "Id", id); Set(item, "Body", body); Set(item, "IsVehicle", true); Set(item, "Path", "CORRIS");
            Set(item, "LocallyOwned", true); Set(item, "RemoteOwner", (byte)1); Set(item, "RemoteEngineAudioSearched", true);
            ((IDictionary)Get(items, "_items")).Add(id, item);
            var transport = new StarterTransport();
            var battery = Empty(PathObject(root, "CORRIS/Assemblies/VINP_Battery"), "Data");
            var batteryPart = Empty(Child(battery.gameObject, "saved battery"), "Data");
            foreach (var data in new[] { battery, batteryPart })
            {
                AddFloat(data, "Charge", 120); AddFloat(data, "ChargeMax", 127); AddFloat(data, "DischargeRate", .1f);
                data.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true } };
                data.FsmVariables.IntVariables = new[] { new FsmInt { Name = "AssemblyID", UseVariable = true, Value = 1 } };
            }
            battery.FsmVariables.GameObjectVariables = new[] { new FsmGameObject { Name = "ActivePart", UseVariable = true, Value = batteryPart.gameObject } };
            var batteryStates = new List<FsmState>(battery.Fsm.States);
            foreach (object paused in (IEnumerable)Get(catalog.GetProperty("GuestEngineProtection", Static).GetValue(null, null), "PausedFsms"))
                if ((string)Get(paused, "Path") == "CORRIS/Assemblies/VINP_Battery")
                    foreach (string state in (string[])Get(paused, "RequiredStates"))
                        batteryStates.Add(new FsmState(battery.Fsm) { Name = state, Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] });
            battery.Fsm.States = batteryStates.ToArray(); battery.Fsm.Init(battery); batteryPart.Fsm.Init(batteryPart);
            var charge = battery.FsmVariables.FindFsmFloat("Charge");
            var parserType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var parser = Activator.CreateInstance(parserType, Members, null, new object[] {
                File.ReadAllText(Path.Combine(Application.dataPath, "../guest-starter-draw-probe.json")) }, null);
            var fixture = (Dictionary<string, object>)parserType.GetMethod("ReadObject", Members).Invoke(parser, null);
            var row = (Dictionary<string, object>)((List<object>)fixture["fsms"])[0];
            var starter = NativeBagPartChecks.MakeFsm(PathObject(root, (string)row["path"]), row);
            var sources = new Dictionary<string, PlayMakerFSM>();
            foreach (var variable in starter.FsmVariables.GameObjectVariables)
            {
                if (variable.Name == "db_Battery") { variable.Value = battery.gameObject; continue; }
                var data = Empty(Child(extras, variable.Name), "Data");
                data.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true }, new FsmBool { Name = "Bolted", UseVariable = true, Value = true } };
                AddFloat(data, "Durability", 2); AddFloat(data, "Wear", 90); AddFloat(data, "Charge", 120); data.Fsm.Init(data);
                variable.Value = data.gameObject; sources.Add(variable.Name, data);
            }
            starter.Fsm.Init(starter); var draws = new List<FsmStateAction>(); var drawStates = new List<string>();
            foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
            {
                string name = (string)stateRow["name"]; var state = NativeBagPartChecks.State(starter, name);
                var raw = (List<object>)stateRow["actions"]; state.Actions = new FsmStateAction[raw.Count]; state.Transitions = new FsmTransition[0];
                for (int i = 0; i < raw.Count; i++)
                {
                    state.Actions[i] = Selected(rule!, name, i) ? NativeAction((Dictionary<string, object>)raw[i], starter) : new Quiet();
                    state.Actions[i].Init(state);
                    if (state.Actions[i].GetType().Name == "AddFsmFloat") { draws.Add(state.Actions[i]); drawStates.Add(name); }
                }
            }
            Action<bool> mode = isHost =>
            {
                protect.GetSetMethod(true).Invoke(policy, new object[] { !isHost }); host.GetSetMethod(true).Invoke(session, new object[] { isHost });
                sessionState.GetSetMethod(true).Invoke(session, new object[] { isHost ? SessionState.Hosting : SessionState.Connected });
                localId.GetSetMethod(true).Invoke(session, new object[] { isHost ? (byte)0 : (byte)1 });
                Set(item, "LocallyOwned", !isHost); Set(item, "RemoteOwner", (byte)1);
            };
            Action<string> fire = name => { NativeBagPartChecks.Fire(starter, "Probe idle"); NativeBagPartChecks.Fire(starter, name); };
            Action flush = () => InstanceCall(vehicles, "FlushStarterDraw", session, item);
            Action clear = () => { InstanceCall(vehicles, "ClearStarterDraws"); transport.Packets.Clear(); };
            Action saved = () => Require(charge.Value == 120 && battery.FsmVariables.FindFsmFloat("ChargeMax").Value == 127
                && batteryPart.FsmVariables.FindFsmFloat("Charge").Value == 120 && batteryPart.FsmVariables.FindFsmFloat("DischargeRate").Value == .1f,
                "Guest starter callback changed the saved battery.");
            try
            {
                Guard.GetField("PrepareInputs", Static).SetValue(null, null);
                Set(world, "_items", items); Set(world, "_vehicles", vehicles); Set(world, "_syncReady", true);
                Set(session, "_transport", transport); Set(session, "_hostPeer", new PeerId(999));
                mode(true); root.SetActive(true); extras.SetActive(true); NativeBagPartChecks.Start(battery); NativeBagPartChecks.Start(batteryPart);
                NativeBagPartChecks.Start(starter); NativeBagPartChecks.Fire(battery, "Check joint");
                check("starter draw: native audit includes four loaded writers and one unloaded writer", () => Require(draws.Count == 5, "Incomplete native starter fixture."));
                for (int i = 0; i < draws.Count; i++)
                {
                    int index = i;
                    check("starter draw: native solo cadence " + drawStates[index], () =>
                    {
                        charge.Value = 120; float expected = 120, rate = ((FsmFloat)Get(draws[index], "addValue")).Value;
                        fire(drawStates[index]); Tick(starter); Tick(starter);
                        for (int step = 0; step < 3; step++) expected += rate;
                        Require(charge.Value == expected, "Native entry/two updates did not produce three draw operations.");
                    });
                }
                charge.Value = 120; mode(false); Require((bool)Call("Prepare", true)!, "Starter observation admission failed.");
                RunStarterHookDraw(check, starter, fire, clear, flush, transport, saved);
                var recorded = new List<StarterDrawRequest>();
                for (int i = 0; i < draws.Count; i++)
                {
                    int index = i;
                    check("starter draw: protected callbacks batch native count " + drawStates[index], () =>
                    {
                        clear(); fire(drawStates[index]); Tick(starter); Tick(starter); flush();
                        Require(draws[index].Enabled && transport.Packets.Count == 1, "Observed writer did not run or emitted one packet per callback.");
                        var packet = transport.Packets[0]; var request = packet.Message as StarterDrawRequest;
                        Require(packet.Channel == Channel.ReliableOrdered && request != null && request.VehicleId == id && request.PlayerId == 1
                            && request.Count == 3 && request.Kind == (drawStates[index] == "No Flywheel" ? 2 : 1), "Wrong native kind/count or channel.");
                        recorded.Add(request!); saved();
                    });
                }
                check("starter draw: changing load kind flushes the previous batch in order", () =>
                {
                    clear(); fire("Turn key"); fire("No Flywheel"); flush();
                    Require(transport.Packets.Count == 2 && ((StarterDrawRequest)transport.Packets[0].Message).Kind == 1
                        && ((StarterDrawRequest)transport.Packets[1].Message).Kind == 2
                        && ((StarterDrawRequest)transport.Packets[1].Message).Sequence == 2, "Load kinds were merged or reordered."); saved();
                });
                check("starter draw: bounded batches preserve every callback and periodic flushing", () =>
                {
                    clear(); fire("Turn key"); var action = NativeBagPartChecks.State(starter, "Turn key").Actions[6];
                    for (int i = 0; i < 512; i++) action.OnUpdate();
                    var pending = ((IDictionary)Get(vehicles, "_starterDraws"))[id]; Set(pending, "SendAt", 0f);
                    InstanceCall(vehicles, "UpdateStarterDraws", session);
                    Require(transport.Packets.Count == 2 && ((StarterDrawRequest)transport.Packets[0].Message).Count == 512
                        && ((StarterDrawRequest)transport.Packets[1].Message).Count == 1, "Full/periodic flush lost or duplicated callbacks."); saved();
                });
                check("starter draw: ownership loss and session clear discard pending work", () =>
                {
                    clear(); fire("Turn key"); Set(item, "LocallyOwned", false); InstanceCall(vehicles, "UpdateStarterDraws", session);
                    Set(item, "LocallyOwned", true); flush(); Require(transport.Packets.Count == 0, "Lost ownership retained pending draw.");
                    fire("Turn key"); InstanceCall(vehicles, "ClearVehicleStateStreams"); flush();
                    Require(transport.Packets.Count == 0, "Session clear retained pending draw."); saved();
                });
                check("starter draw: final vehicle state flushes draw before ownership release", () =>
                {
                    clear(); fire("Turn key"); InstanceCall(vehicles, "SendFinalVehicleState", session, item);
                    Set(item, "LocallyOwned", false); InstanceCall(vehicles, "UpdateStarterDraws", session);
                    Require(transport.Packets.Count >= 1 && transport.Packets[0].Message is StarterDrawRequest, "Final state lost pending draw."); saved();
                    Set(item, "LocallyOwned", true);
                });
                check("starter draw: observers and disconnected guests cannot report or write", () =>
                {
                    clear(); Set(item, "LocallyOwned", false); fire("Turn key"); Tick(starter); flush();
                    Set(item, "LocallyOwned", true); sessionState.GetSetMethod(true).Invoke(session, new object[] { SessionState.Idle });
                    fire("Turn key"); Tick(starter); flush(); Require(transport.Packets.Count == 0, "Inactive guest sent starter demand."); saved(); mode(false);
                });
                RunStarterDrawFailures(check, starter, draws[0], drawStates[0], clear, flush, transport, saved);
                clear(); mode(true); starter.enabled = true; battery.enabled = true; NativeBagPartChecks.Fire(battery, "Check joint"); fire("Probe idle");
                RunHostStarterDraw(check, world, vehicles, item, starter, battery, sources, recorded, charge, mode);
                RunStarterWear(check, world, vehicles, item, starter, battery, sources, row, transport, mode, clear, saved);
            }
            finally
            {
                Set(world, "_items", oldItems); Set(world, "_vehicles", oldVehicles); Set(world, "_syncReady", oldReady);
                Set(session, "_transport", oldTransport); Set(session, "_hostPeer", oldPeer);
                host.GetSetMethod(true).Invoke(session, new[] { previousHost }); sessionState.GetSetMethod(true).Invoke(session, new[] { previousState });
                localId.GetSetMethod(true).Invoke(session, new[] { previousId }); protect.GetSetMethod(true).Invoke(policy, new[] { previousProtect });
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(extras); Call("Prepare", true);
                Guard.GetField("PrepareInputs", Static).SetValue(null, callback);
            }
        }

        private static void RunStarterHookDraw(Action<string, Action> check, PlayMakerFSM starter, Action<string> fire,
            Action clear, Action flush, StarterTransport transport, Action saved)
        {
            var state = NativeBagPartChecks.State(starter, "Start engine"); var original = state.Actions;
            var hook = Core.GetType("WinterMP.Core.Sync.FsmHook", true).GetMethod("OnStateEnter", Static, null,
                new[] { typeof(PlayMakerFSM), typeof(string), typeof(Action) }, null);
            int observed = 0;
            try
            {
                check("starter draw: leading observers preserve native demand counts and saved battery protection", () =>
                {
                    for (int i = 0; i < 2; i++)
                        Require((bool)hook.Invoke(null, new object[] { starter, state.Name, (Action)(() => observed++) }), "Hook installation failed.");
                    Require((bool)Call("Prepare", true)!, "Starter hooks invalidated writer protection.");
                    clear(); fire(state.Name); Tick(starter); Tick(starter); flush();
                    Require(observed == 2 && starter.enabled && transport.Packets.Count == 1
                        && transport.Packets[0].Message is StarterDrawRequest request && request.Count == 3,
                        "Hooked starter lost native entries or changed demand counts."); saved();
                });
                check("starter draw: foreign leading actions still pause guarded writers without battery changes", () =>
                {
                    var hooked = state.Actions; var changed = new FsmStateAction[hooked.Length + 1];
                    changed[0] = new Quiet(); Array.Copy(hooked, 0, changed, 1, hooked.Length);
                    try
                    {
                        state.Actions = changed; clear(); fire(state.Name); flush();
                        Require(!starter.enabled && transport.Packets.Count == 0, "Foreign action offset escaped writer protection."); saved();
                    }
                    finally { state.Actions = hooked; Call("Prepare", true); Call("Prepare", true); }
                    Require(starter.enabled, "Restored starter actions did not recover.");
                });
            }
            finally { state.Actions = original; clear(); Call("Prepare", true); Call("Prepare", true); }
        }

        private static void RunStarterDrawFailures(Action<string, Action> check, PlayMakerFSM starter, FsmStateAction action,
            string state, Action clear, Action flush, StarterTransport transport, Action saved)
        {
            foreach (string field in new[] { "everyFrame", "perSecond", "addValue", "fsmName", "variableName" })
                check("starter draw: changed " + field + " is contained and repaired", () =>
                {
                    clear(); object original = Get(action, field);
                    object changed = field == "everyFrame" ? (object)false : field == "perSecond" ? true
                        : field == "addValue" ? new FsmFloat { Value = -.0094f } : (object)new FsmString { Value = "Changed" };
                    try
                    {
                        Set(action, field, changed); NativeBagPartChecks.Fire(starter, "Probe idle"); NativeBagPartChecks.Fire(starter, state);
                        flush(); Require(!starter.enabled && transport.Packets.Count == 0, "Changed observed callback escaped containment."); saved();
                    }
                    finally { Set(action, field, original); Call("Prepare", true); Call("Prepare", true); }
                    Require(starter.enabled, "Repaired starter did not recover.");
                });
        }

        private static void RunHostStarterDraw(Action<string, Action> check, object world, object vehicles, object item,
            PlayMakerFSM starter, PlayMakerFSM battery, Dictionary<string, PlayMakerFSM> sources,
            List<StarterDrawRequest> recorded, FsmFloat charge, Action<bool> mode)
        {
            var flywheel = sources["db_Flywheel"].FsmVariables.FindFsmBool("Installed");
            starter.FsmVariables.FindFsmFloat("StarterDraw").Value = -.0137f;
            starter.FsmVariables.FindFsmFloat("StarterDrawNoLoad").Value = -.0031f;
            Func<StarterDrawRequest, byte, bool> receive = (r, actor) => (bool)InstanceCall(world, "OnHostStarterDraw",
                PacketCodec.Decode(PacketCodec.Encode(r)), actor)!;
            ushort sequence = 0;
            Func<StarterDrawRequest> request = () => new StarterDrawRequest { VehicleId = 709178, PlayerId = 1,
                Sequence = ++sequence, Kind = StarterDrawRequest.Loaded, Count = 3 };
            foreach (var captured in recorded)
                check("starter draw: captured native batch reaches host once kind " + captured.Kind + " source " + (++sequence), () =>
                {
                    InstanceCall(vehicles, "ClearStarterDraws"); charge.Value = 120; flywheel.Value = captured.Kind == 1;
                    float expected = 120, rate = starter.FsmVariables.FindFsmFloat(captured.Kind == 1 ? "StarterDraw" : "StarterDrawNoLoad").Value;
                    for (int i = 0; i < captured.Count; i++) expected += rate;
                    Require(receive(captured, 1) && charge.Value == expected, "Host did not apply its native starter rate/count.");
                    Require(!receive(captured, 1) && charge.Value == expected, "Duplicate spent battery twice.");
                    Require(battery.FsmVariables.FindFsmFloat("ChargeMax").Value == 127, "Cranking changed battery capacity.");
                });
            InstanceCall(vehicles, "ClearStarterDraws"); sequence = 100; flywheel.Value = true;
            foreach (string fault in new[] { "actor", "owner", "local", "battery removed", "battery inactive", "starter missing", "starter wiring", "harness wiring", "ground wiring", "flywheel", "host cranking", "cadence", "battery target" })
                check("starter draw: host rejects " + fault, () =>
                {
                    charge.Value = 120; var draw = request(); var action = NativeBagPartChecks.State(starter, "Turn key").Actions[6];
                    var batteryTarget = starter.FsmVariables.FindFsmGameObject("db_Battery");
                    if (fault == "owner") Set(item, "RemoteOwner", (byte)2);
                    if (fault == "local") Set(item, "LocallyOwned", true);
                    if (fault == "battery removed") battery.FsmVariables.FindFsmBool("Installed").Value = false;
                    if (fault == "battery inactive") battery.gameObject.SetActive(false);
                    if (fault == "starter missing") sources["db_Starter"].FsmVariables.FindFsmBool("Installed").Value = false;
                    if (fault == "starter wiring") sources["db_WiringStarter"].FsmVariables.FindFsmBool("Bolted").Value = false;
                    if (fault == "harness wiring") sources["db_WiringBatteryHarness"].FsmVariables.FindFsmBool("Bolted").Value = false;
                    if (fault == "ground wiring") sources["db_WiringGround"].FsmVariables.FindFsmBool("Installed").Value = false;
                    if (fault == "flywheel") flywheel.Value = false;
                    if (fault == "cadence") Set(action, "perSecond", true);
                    if (fault == "battery target") batteryTarget.Value = sources["db_Starter"].gameObject;
                    if (fault == "host cranking") { NativeBagPartChecks.Fire(starter, "Turn key"); charge.Value = 120; }
                    try { Require(!receive(draw, fault == "actor" ? (byte)2 : (byte)1) && charge.Value == 120, "Rejected host condition spent battery."); }
                    finally
                    {
                        mode(true); battery.gameObject.SetActive(true); battery.enabled = true; battery.FsmVariables.FindFsmBool("Installed").Value = true;
                        NativeBagPartChecks.Fire(battery, "Check joint"); NativeBagPartChecks.Fire(starter, "Probe idle");
                        foreach (var source in sources.Values) foreach (var value in source.FsmVariables.BoolVariables) value.Value = true;
                        Set(action, "perSecond", false); batteryTarget.Value = battery.gameObject;
                    }
                    if (fault != "actor" && fault != "owner" && fault != "local")
                        Require(!receive(draw, 1) && charge.Value == 120, "Rejected native work replayed after repair.");
                    Require(receive(request(), 1) && charge.Value < 120, "Fresh host request did not recover.");
                });
        }
    }
}
