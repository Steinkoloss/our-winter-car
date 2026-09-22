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
        private sealed class HeaterFixture : IDisposable
        {
            internal readonly SessionManager Session = SessionManager.Instance!;
            internal readonly object World, Items;
            internal readonly GameObject Root, Extras;
            internal readonly PlayMakerFSM Mount, Part, Heater, Battery, Other;
            internal readonly Dictionary<string, object> HeaterRow;
            internal readonly FsmStateAction InstalledRead, WearRead, WearWrite;
            internal readonly FsmFloat SavedWear, PartWear;
            private readonly object _oldItems, _oldReady, _oldHost, _oldState, _oldProtect, _policy;
            private readonly object? _callback;
            private uint _revision;

            internal HeaterFixture()
            {
                World = Core.GetType("WinterMP.Core.Sync.WorldSyncManager", true).GetProperty("Instance", Static).GetValue(null, null);
                _oldItems = Get(World, "_items"); _oldReady = Get(World, "_syncReady");
                _oldHost = typeof(SessionManager).GetProperty("IsHost", Members).GetValue(Session, null);
                _oldState = typeof(SessionManager).GetProperty("State", Members).GetValue(Session, null);
                _policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
                _oldProtect = _policy.GetType().GetProperty("ProtectWorld", Members).GetValue(_policy, null);
                _callback = Guard.GetField("PrepareInputs", Static).GetValue(null);
                var bridge = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.WorldSyncBridge", true), Members, null,
                    new object[] { World, new Dictionary<PlayMakerFSM, bool>() }, null);
                Items = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true), Members, null, new[] { bridge }, null);
                var reader = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                var parser = Activator.CreateInstance(reader, Members, null, new object[] {
                    File.ReadAllText(Path.Combine(Application.dataPath, "../heater-input-probe.json")) }, null);
                var fixture = (Dictionary<string, object>)reader.GetMethod("ReadObject", Members).Invoke(parser, null);
                var rows = (List<object>)fixture["fsms"];
                Root = new GameObject("CORRIS"); Root.SetActive(false); Extras = new GameObject("heater input controls"); Extras.SetActive(false);
                var mountRow = NativeBagPartChecks.Find(rows, "CORRIS/Assemblies/VINP_Heaterbox", "Data");
                Mount = NativeBagPartChecks.MakeFsm(PathObject(Root, (string)mountRow["path"]), mountRow);
                Part = Empty(Child(Mount.gameObject, "saved heater part"), "Data");
                Part.FsmVariables.IntVariables = new[] { new FsmInt { Name = "AssemblyID", UseVariable = true, Value = 1 } };
                AddFloat(Part, "Wear", 95); Part.Fsm.Init(Part); PartWear = Part.FsmVariables.FindFsmFloat("Wear");
                Mount.FsmVariables.FindFsmGameObject("ActivePart").Value = Part.gameObject;
                SavedWear = Mount.FsmVariables.FindFsmFloat("Wear"); SavedWear.Value = 95;
                HeaterRow = NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Electricity/PowerON/HeaterUnit", "Function");
                Heater = NativeBagPartChecks.MakeFsm(PathObject(Root, (string)HeaterRow["path"]), HeaterRow);
                Battery = Empty(PathObject(Root, "CORRIS/Assemblies/VINP_Battery"), "Data"); AddFloat(Battery, "Charge", 120); AddFloat(Battery, "ChargeMax", 127);
                Other = Empty(Child(Extras, "ordinary data"), "Data"); AddFloat(Other, "Wear", 44);
                Other.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true } }; Other.Fsm.Init(Other);
                var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
                foreach (object paused in (IEnumerable)Get(catalog.GetProperty("GuestEngineProtection", Static).GetValue(null, null), "PausedFsms"))
                    if ((string)Get(paused, "Path") == "CORRIS/Assemblies/VINP_Battery")
                    {
                        var states = new List<FsmState>(Battery.Fsm.States);
                        foreach (string state in (string[])Get(paused, "RequiredStates")) states.Add(new FsmState(Battery.Fsm) { Name = state, Actions = new FsmStateAction[0] });
                        Battery.Fsm.States = states.ToArray();
                    }
                Battery.Fsm.Init(Battery);
                foreach (var reference in Heater.FsmVariables.GameObjectVariables)
                    reference.Value = reference.Name == "db_Heater" ? Mount.gameObject : reference.Name == "VINP_Battery" ? Battery.gameObject : Other.gameObject;
                Heater.Fsm.Init(Heater); Mount.Fsm.Init(Mount);
                LoadActions(Mount, mountRow, (state, i) => state == "Update 2" && i == 0 || state == "Remove part" && i == 4);
                LoadActions(Heater, HeaterRow, (state, i) => state == "Electrics?" && i == 4 || state == "Blower wear?" && i == 1
                    || state == "Blower ok" && i >= 4 && i <= 8);
                InstalledRead = NativeBagPartChecks.State(Heater, "Electrics?").Actions[4];
                WearRead = NativeBagPartChecks.State(Heater, "Blower wear?").Actions[1];
                WearWrite = NativeBagPartChecks.State(Heater, "Blower ok").Actions[7];
                Mode(true); Guard.GetField("PrepareInputs", Static).SetValue(null, null);
                Set(World, "_items", Items); Set(World, "_syncReady", true);
                Root.SetActive(true); Extras.SetActive(true);
                NativeBagPartChecks.Start(Heater); NativeBagPartChecks.Start(Part); NativeBagPartChecks.Start(Battery);
            }

            private static void LoadActions(PlayMakerFSM fsm, Dictionary<string, object> row, Func<string, int, bool> include)
            {
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    string name = (string)stateRow["name"]; var state = NativeBagPartChecks.State(fsm, name); var raw = (List<object>)stateRow["actions"];
                    state.Actions = new FsmStateAction[raw.Count]; state.Transitions = new FsmTransition[0];
                    for (int i = 0; i < raw.Count; i++) { state.Actions[i] = include(name, i) ? NativeAction((Dictionary<string, object>)raw[i], fsm) : new Quiet(); state.Actions[i].Init(state); }
                }
            }
            internal void Mode(bool host)
            {
                _policy.GetType().GetProperty("ProtectWorld", Members).GetSetMethod(true).Invoke(_policy, new object[] { !host });
                typeof(SessionManager).GetProperty("IsHost", Members).GetSetMethod(true).Invoke(Session, new object[] { host });
                typeof(SessionManager).GetProperty("State", Members).GetSetMethod(true).Invoke(Session, new object[] { host ? SessionState.Hosting : SessionState.Connected });
            }
            internal void Fire(string state) { NativeBagPartChecks.Fire(Heater, "Probe idle"); NativeBagPartChecks.Fire(Heater, state); }
            internal void Settled()
            {
                Mode(true); Mount.enabled = true; Mount.gameObject.SetActive(true); Part.gameObject.SetActive(true);
                Part.transform.parent = Mount.transform; Mount.FsmVariables.FindFsmGameObject("ActivePart").Value = Part.gameObject;
                Part.FsmVariables.FindFsmInt("AssemblyID").Value = 1; Mount.FsmVariables.FindFsmBool("Installed").Value = true;
                NativeBagPartChecks.Start(Mount); NativeBagPartChecks.Fire(Mount, "Update 2");
            }
            internal HeaterState Capture() => (HeaterState)InstanceCall(Items, "BuildHeaterState")!;
            internal void Receive(byte flags, float wear = 0, byte rearWindowFlags = 0) => InstanceCall(World, "OnHeaterState", PacketCodec.Decode(PacketCodec.Encode(new HeaterState
                { Revision = ++_revision, Flags = flags, Wear = wear, RearWindowFlags = rearWindowFlags })));
            internal void Read(bool installed, float wear)
            {
                Fire("Electrics?"); Require(((FsmBool)Get(InstalledRead, "storeValue")).Value == installed, "Wrong heater installation input.");
                Fire("Blower wear?"); Require(((FsmFloat)Get(WearRead, "storeValue")).Value == wear, "Wrong heater wear input.");
            }
            internal void Saved() => Require(SavedWear.Value == 86 && PartWear.Value == 91 && Mount.FsmVariables.FindFsmBool("Installed").Value
                && Part.transform.parent == Mount.transform && Part.FsmVariables.FindFsmInt("AssemblyID").Value == 1
                && Battery.FsmVariables.FindFsmFloat("Charge").Value == 120, "Guest heater changed saved wear, attachment or battery.");
            public void Dispose()
            {
                Set(World, "_items", _oldItems); Set(World, "_syncReady", _oldReady);
                typeof(SessionManager).GetProperty("IsHost", Members).GetSetMethod(true).Invoke(Session, new[] { _oldHost });
                typeof(SessionManager).GetProperty("State", Members).GetSetMethod(true).Invoke(Session, new[] { _oldState });
                _policy.GetType().GetProperty("ProtectWorld", Members).GetSetMethod(true).Invoke(_policy, new[] { _oldProtect });
                UnityEngine.Object.DestroyImmediate(Root); UnityEngine.Object.DestroyImmediate(Extras); Call("Prepare", true);
                Guard.GetField("PrepareInputs", Static).SetValue(null, _callback);
            }
        }

        private static void RunHeaterInputs(Action<string, Action> check)
        {
            using (var f = new HeaterFixture())
            {
                RunHeaterCapture(check, f);
                f.Settled(); f.SavedWear.Value = 86; f.PartWear.Value = 91; f.Battery.FsmVariables.FindFsmFloat("Charge").Value = 120;
                f.Mode(false);
                check("heater inputs: native external wear is blocked before discovery", () =>
                {
                    ((FsmFloat)Get(f.WearWrite, "subtractValue")).Value = 4; f.WearWrite.OnEnter(); f.Saved();
                });
                check("heater inputs: unseeded reads cannot borrow the guest heater", () => { f.Read(false, 0); f.Saved(); });
                check("heater protection: admission pauses mounted wear publication and removal", () =>
                {
                    Require((bool)Call("Prepare", true)! && !f.Mount.enabled && !f.Mount.Fsm.RestartOnEnable, "Saved heater mount was not paused.");
                    NativeBagPartChecks.Fire(f.Mount, "Update 2"); Tick(f.Mount); NativeBagPartChecks.Fire(f.Mount, "Remove part"); f.Saved();
                });
                foreach (float wear in new[] { -1f, 0f, 2.9f, 3f, 70.25f })
                {
                    float value = wear;
                    check("heater inputs: host wear " + value + " reaches native readers", () => { f.Receive(3, value); f.Read(true, value); f.Saved(); });
                }
                foreach (byte flags in new byte[] { 0, 1 })
                {
                    byte value = flags;
                    check("heater inputs: absent or unavailable host clears consumer inputs " + flags, () => { f.Receive(value); f.Read(false, 0); f.Saved(); });
                }
                RunHeaterReadBoundaries(check, f);
                check("heater protection: host-driven native blower arithmetic preserves saved guest parts", () =>
                {
                    f.Receive(3, 80); f.Heater.FsmVariables.FindFsmFloat("SettingBlower").Value = 4;
                    f.Heater.FsmVariables.FindFsmFloat("WearRateBlower").Value = 274;
                    f.Fire("Blower ok"); Tick(f.Heater); Require(f.WearWrite.Enabled && f.Heater.enabled, "Consumer simulation was unnecessarily paused."); f.Saved();
                });
                RunHeaterNativeDecision(check, f);
                check("heater inputs: session clear removes cached host state without resuming saved simulation", () =>
                {
                    f.Receive(3, 77); InstanceCall(f.Items, "ReleaseSession"); f.Read(false, 0);
                    NativeBagPartChecks.Fire(f.Mount, "Remove part"); f.WearWrite.OnEnter(); Require(!f.Mount.enabled, "Session release resumed saved mount."); f.Saved();
                });
            }
        }

        private static void RunHeaterCapture(Action<string, Action> check, HeaterFixture f)
        {
            check("heater capture: unstarted mount is unavailable", () => Require(f.Capture().Flags == 0, "Unstarted source was accepted."));
            f.Settled();
            check("heater capture: settled host mount publishes actual native wear", () =>
            { f.SavedWear.Value = 73.25f; f.PartWear.Value = 92; var state = f.Capture(); Require(state.Flags == 3 && state.Wear == 73.25f, "Capture used stale physical-part wear."); });
            foreach (string stateName in new[] { "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Far", "Near" })
            {
                string state = stateName;
                check("heater capture: transition " + state + " remains unavailable", () =>
                { NativeBagPartChecks.Fire(f.Mount, state); Require(f.Capture().Flags == 0, "Transitional heater powered guest."); f.Settled(); });
            }
            foreach (string fault in new[] { "mount disabled", "part inactive", "parent", "active part", "assembly", "NaN", "infinity", "duplicate mount", "duplicate part" })
                check("heater capture: rejects " + fault + " and recovers", () =>
                {
                    f.Settled(); PlayMakerFSM? extra = null; f.SavedWear.Value = 73.25f;
                    if (fault == "mount disabled") f.Mount.enabled = false;
                    if (fault == "part inactive") f.Part.gameObject.SetActive(false);
                    if (fault == "parent") f.Part.transform.parent = f.Extras.transform;
                    if (fault == "active part") f.Mount.FsmVariables.FindFsmGameObject("ActivePart").Value = null;
                    if (fault == "assembly") f.Part.FsmVariables.FindFsmInt("AssemblyID").Value = 0;
                    if (fault == "NaN") f.SavedWear.Value = float.NaN;
                    if (fault == "infinity") f.SavedWear.Value = float.PositiveInfinity;
                    if (fault == "duplicate mount") extra = Empty(f.Mount.gameObject, "Data");
                    if (fault == "duplicate part") extra = Empty(f.Part.gameObject, "Data");
                    try { Require(f.Capture().Flags == 0 && f.Capture().Wear == 0, "Invalid heater source retained installation/wear."); }
                    finally { if (extra != null) UnityEngine.Object.DestroyImmediate(extra); f.SavedWear.Value = 73.25f; f.Settled(); }
                    Require(f.Capture().Flags == 3 && f.Capture().Wear == 73.25f, "Repaired heater did not recover.");
                });
            check("heater capture: settled absence clears wear", () =>
            {
                f.Mount.FsmVariables.FindFsmBool("Installed").Value = false; NativeBagPartChecks.Fire(f.Mount, "Idle");
                Require(f.Capture().Flags == 1 && f.Capture().Wear == 0, "Absent heater kept wear."); f.Settled();
            });
            check("heater capture: join and vehicle resync carry pending wear without consuming live publication", () =>
            {
                var previous = f.Capture(); var publication = (HeaterPublication)Get(f.Items, "_heaterPublication"); publication.MarkBroadcast(previous.Revision);
                f.SavedWear.Value = 51; foreach (string method in new[] { "BuildWorldSnapshot", "BuildResyncMessages" })
                {
                    var messages = (IEnumerable)(method == "BuildWorldSnapshot" ? InstanceCall(f.World, method)! : InstanceCall(f.World, method, WorldResyncRequest.FlagVehicles)!);
                    HeaterState? found = null; foreach (IMessage message in messages) if (message is HeaterState state) { found = state; break; }
                    Require(found != null && found.Wear == 51 && found.Revision == previous.Revision + 1 && publication.NeedsBroadcast, "Snapshot omitted/consumed heater update.");
                }
                InstanceCall(f.Items, "ProcessHeater", f.Session); Require(!publication.NeedsBroadcast && (float)Get(f.Items, "_nextHeaterKeepalive") > Time.unscaledTime, "Live publication did not establish ordered keepalive.");
            });
            check("heater capture: host rejects peer states and guest cannot publish saved condition", () =>
            {
                InstanceCall(f.Items, "OnHeaterState", new HeaterState { Revision = 5, Flags = 3, Wear = 1 });
                Require(((HeaterReplica)Get(f.Items, "_heaterReplica")).Get() == null, "Host accepted peer condition.");
                f.Mode(false); Require(InstanceCall(f.Items, "BuildHeaterState") == null, "Guest published its saved heater."); f.Mode(true);
            });
            check("heater protection: native solo blower wear reaches mounted and physical part", () =>
            {
                f.Settled(); f.SavedWear.Value = 95; f.Heater.FsmVariables.FindFsmFloat("SettingBlower").Value = 4;
                f.Heater.FsmVariables.FindFsmFloat("WearRateBlower").Value = 274; f.Fire("Blower ok");
                NativeBagPartChecks.State(f.Mount, "Update 2").Actions[0].OnUpdate();
                Require(f.SavedWear.Value == 95 - 4f / 274 && f.PartWear.Value == f.SavedWear.Value, "Native host wear did not reach physical part.");
            });
        }

        private static void RunHeaterReadBoundaries(Action<string, Action> check, HeaterFixture f)
        {
            var output = (FsmFloat)Get(f.WearRead, "storeValue"); var name = (FsmString)Get(f.WearRead, "fsmName");
            check("heater inputs: revisions reject stale and conflicting records", () =>
            {
                f.Receive(3, 61); var replica = (HeaterReplica)Get(f.Items, "_heaterReplica"); var current = replica.Get()!;
                InstanceCall(f.World, "OnHeaterState", new HeaterState { Revision = current.Revision, Flags = 3, Wear = 1 });
                InstanceCall(f.World, "OnHeaterState", new HeaterState { Revision = current.Revision - 1, Flags = 3, Wear = 2 });
                f.Read(true, 61); f.Saved();
            });
            check("heater inputs: original entry-only cadence remains intact", () =>
            {
                f.Receive(3, 62); f.Fire("Blower wear?"); output.Value = -7; Tick(f.Heater);
                Require(output.Value == -7 && !(bool)Get(f.WearRead, "everyFrame"), "Projection added automatic read ticks.");
                f.WearRead.OnUpdate(); Require(output.Value == 62, "Native helper callback did not project condition."); f.Saved();
            });
            check("heater inputs: native cached same-object FSM name and fallback are preserved", () =>
            {
                var other = Empty(f.Mount.gameObject, "Other"); AddFloat(other, "Wear", 12); other.Fsm.Init(other);
                try
                {
                    f.Receive(3, 63); f.Fire("Blower wear?"); name.Value = "Other"; f.WearRead.OnUpdate(); Require(output.Value == 63, "Same-object cache changed source.");
                    Set(f.WearRead, "goLastFrame", null!); f.WearRead.OnUpdate(); Require(output.Value == 12, "Unrelated source was projected.");
                    foreach (string fallback in new[] { "", "missing" })
                    { Set(f.WearRead, "goLastFrame", null!); name.Value = fallback; f.WearRead.OnUpdate(); Require(output.Value == 63, "Native fallback did not use first FSM."); }
                }
                finally { name.Value = "Data"; Set(f.WearRead, "goLastFrame", null!); UnityEngine.Object.DestroyImmediate(other); }
                f.Saved();
            });
            check("heater inputs: different source keeps native values and restored source projects again", () =>
            {
                var target = f.Heater.FsmVariables.FindFsmGameObject("db_Heater"); target.Value = f.Other.gameObject;
                try { f.Read(true, 44); } finally { target.Value = f.Mount.gameObject; }
                f.Read(true, 63); f.Saved();
            });
            foreach (string alias in new[] { "mount", "physical part", "literal", "global" })
                check("heater inputs: unsafe " + alias + " output is blocked", () =>
                {
                    var globals = FsmVariables.GlobalVariables.FloatVariables; output.Value = -8;
                    try
                    {
                        Set(f.WearRead, "storeValue", alias == "mount" ? f.SavedWear : alias == "physical part" ? f.PartWear
                            : alias == "literal" ? new FsmFloat { Value = -9 } : output);
                        if (alias == "global") { var list = new List<FsmFloat>(globals); list.Add(output); FsmVariables.GlobalVariables.FloatVariables = list.ToArray(); }
                        f.WearRead.OnUpdate(); Require(output.Value == -8, "Unsafe projected output was assigned."); f.Saved();
                    }
                    finally { Set(f.WearRead, "storeValue", output); FsmVariables.GlobalVariables.FloatVariables = globals; }
                    f.Fire("Blower wear?"); Require(output.Value == 63, "Safe repaired reader did not recover.");
                });
            check("heater protection: moved source and lost metadata retain identity protection", () =>
            {
                var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true); var property = catalog.GetProperty("GuestEngineInputs", Static);
                var inputs = property.GetValue(null, null); string original = f.Mount.name;
                try
                {
                    f.Mount.name = "renamed saved heater"; property.GetSetMethod(true).Invoke(null, new object?[] { null });
                    InstanceCall(f.Items, "ClearHeater"); f.Read(false, 0);
                    ((FsmFloat)Get(f.WearWrite, "subtractValue")).Value = 9; f.WearWrite.OnEnter(); NativeBagPartChecks.Fire(f.Mount, "Remove part"); f.Saved();
                }
                finally { property.GetSetMethod(true).Invoke(null, new[] { inputs }); f.Mount.name = original; }
                f.Receive(3, 64); f.Read(true, 64); f.Saved();
            });
        }

        private static void RunHeaterNativeDecision(Action<string, Action> check, HeaterFixture f)
        {
            var state = NativeBagPartChecks.State(f.Heater, "Blower wear?"); var oldActions = state.Actions; var oldTransitions = state.Transitions;
            var row = FindState(f.HeaterRow, "Blower wear?"); var raw = (List<object>)row["actions"];
            try
            {
                state.Actions = (FsmStateAction[])oldActions.Clone();
                for (int i = 2; i <= 3; i++) { state.Actions[i] = NativeAction((Dictionary<string, object>)raw[i], f.Heater); state.Actions[i].Init(state); }
                state.Transitions = new[] { new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("BROKEN"), ToState = "Blower off" },
                    new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("BAD"), ToState = "BadSound" },
                    new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent("FINISHED"), ToState = "Blower ok" } };
                foreach (float wear in new[] { -1f, 2.9f, 3f, 3.1f, 90f })
                {
                    float value = wear;
                    check("heater inputs: native broken-blower decision follows host wear " + value, () =>
                    {
                        f.Receive(3, value); f.Fire("Blower wear?");
                        Require(f.Heater.ActiveStateName == (value < 3 ? "Blower off" : "Blower ok"), "Native blower threshold followed saved guest condition."); f.Saved();
                    });
                }
            }
            finally { state.Actions = oldActions; state.Transitions = oldTransitions; }
        }
    }
}
