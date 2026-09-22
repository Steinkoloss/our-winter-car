using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private sealed partial class Fixture
        {
            internal PlayMakerFSM MakeHeaterHoseConsumer()
            {
                var row = FindNativeRow("CORRIS/Simulation/Electricity/PowerON/HeaterUnit", "Function");
                var obj = PathObject(Car, (string)row["path"]); obj.SetActive(false);
                var heater = NativeBagPartChecks.MakeFsm(obj, row);
                foreach (var variable in heater.FsmVariables.GameObjectVariables) variable.Value = Mount.gameObject;
                heater.FsmVariables.FindFsmGameObject("Cooling").Value = Reader.gameObject;
                heater.FsmVariables.FindFsmGameObject("db_HeaterInlet").Value = SavedCoolantHoses[2]!.gameObject;
                heater.FsmVariables.FindFsmGameObject("db_HeaterOutlet").Value = SavedCoolantHoses[3]!.gameObject;
                heater.Fsm.Init(heater);
                foreach (Dictionary<string, object> stateRow in (List<object>)row["states"])
                {
                    var state = NativeBagPartChecks.State(heater, (string)stateRow["name"]);
                    var raw = (List<object>)stateRow["actions"];
                    var actions = new FsmStateAction[raw.Count];
                    for (int i = 0; i < actions.Length; i++) actions[i] = state.Name == "Heater pipes?"
                        ? NativeAction((Dictionary<string, object>)raw[i], heater) : new Quiet();
                    state.Actions = actions; foreach (var action in actions) action.Init(state);
                    if (state.Name != "Heater pipes?") state.Transitions = new FsmTransition[0];
                }
                obj.SetActive(true); NativeBagPartChecks.Start(heater);
                return heater;
            }

            internal void SetHeaterSupply(byte mask, float coolant = 4, bool radiator = true)
            {
                var state = new EngineBlockState { CoolantHoseFlags = mask, RadiatorInstalled = radiator,
                    RadiatorCoolant = radiator ? coolant : 0, RadiatorWear = radiator ? 85 : 0 };
                for (int i = 0; i < 4; i++) if ((mask & (1 << i)) != 0) state.CoolantHoseTightness[i] = 16;
                ReceiveIntake(state);
            }
        }

        private static void RunHeaterHoses(Action<string, Action> check)
        {
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protection = policy.GetType().GetProperty("ProtectWorld", Members);
            bool originalProtection = (bool)protection.GetValue(policy, null);
            try
            {
                protection.GetSetMethod(true).Invoke(policy, new object[] { false });
                using (var f = new Fixture("VIN126", "Cooling", "heater-hose-input-probe.json"))
                {
                    var heater = f.MakeHeaterHoseConsumer();
                    var pipes = NativeBagPartChecks.State(heater, "Heater pipes?");
                    Require(pipes.Actions.Length == 5, "Native heater pipe actions were lost during fixture initialization.");
                    var inlet = pipes.Actions[2]; var outlet = pipes.Actions[3];
                    f.ConfigureRadiatorCooling();
                    Action saved = () =>
                    {
                        f.AssertSaved();
                        foreach (var hose in f.SavedCoolantHoses) Require(hose != null && !hose.enabled, "Saved hose Data resumed.");
                        Require(!f.SavedRadiator!.enabled, "Saved radiator Data resumed.");
                    };
                    Action fire = () =>
                    {
                        NativeBagPartChecks.Fire(heater, "Electrics?");
                        NativeBagPartChecks.Fire(heater, "Heater pipes?");
                    };
                    check("heater hoses: host and solo readers retain saved installation", () =>
                    {
                        foreach (bool host in new[] { false, true })
                        {
                            Property(f.Session, "IsHost", host); inlet.OnEnter(); outlet.OnEnter();
                            Require(((FsmBool)Get(inlet, "storeValue")).Value && ((FsmBool)Get(outlet, "storeValue")).Value, "Host/solo hose read was projected.");
                        }
                        Property(f.Session, "IsHost", false);
                    });
                    protection.GetSetMethod(true).Invoke(policy, new object[] { true });
                    Require(f.Prepare(), "Heater hose preparation failed.");
                    check("heater hoses: unseeded state cannot borrow saved hoses", () =>
                    {
                        ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Clear(); inlet.OnEnter(); outlet.OnEnter();
                        Require(!((FsmBool)Get(inlet, "storeValue")).Value && !((FsmBool)Get(outlet, "storeValue")).Value, "Unseeded heater used saved hose installation."); saved();
                    });
                    for (byte flags = 0; flags < 16; flags++)
                    {
                        byte mask = flags;
                        check("heater hoses: native heater gate follows independent host mask " + mask, () =>
                        {
                            f.SetHeaterSupply(mask); Require(f.Prepare(), "Host hose update failed.");
                            f.Fire("Radiator installed?"); fire();
                            Require(heater.ActiveStateName == ((mask & 12) == 12 ? "Calc defrosting" : "Electrics?"), "Native heater gate borrowed another hose or saved installation.");
                            Require(heater.FsmVariables.FindFsmFloat("WaterLevel").Value == 4, "Native heater did not read host-derived coolant."); saved();
                        });
                    }
                    foreach (float amount in new[] { -1f, 0, .4999f, .5f, .5001f, 25, 30 })
                    {
                        float coolant = amount;
                        check("heater hoses: native coolant threshold and clamp " + coolant, () =>
                        {
                            f.SetHeaterSupply(12, coolant); Require(f.Prepare(), "Host coolant update failed.");
                            f.Fire("Radiator installed?"); fire();
                            Require(heater.FsmVariables.FindFsmFloat("WaterLevel").Value == Mathf.Clamp(coolant, 0, 25)
                                && heater.ActiveStateName == (coolant < .5f ? "Electrics?" : "Calc defrosting"), "Native coolant clamp or 0.5 heater threshold diverged."); saved();
                        });
                    }
                    check("heater hoses: missing host radiator stops heating despite installed hoses", () =>
                    {
                        f.SetHeaterSupply(12, 0, false); Require(f.Prepare(), "Removed radiator failed preparation.");
                        f.Fire("Radiator installed?"); fire();
                        Require(heater.ActiveStateName == "Electrics?" && heater.FsmVariables.FindFsmFloat("WaterLevel").Value == 0, "Removed radiator left saved coolant available."); saved();
                    });
                    check("heater hoses: arrival and native entry-only reads preserve scratch timing", () =>
                    {
                        f.SetHeaterSupply(12); Require(f.Prepare(), "Refit failed."); inlet.OnEnter();
                        var output = (FsmBool)Get(inlet, "storeValue"); output.Value = false;
                        f.SetHeaterSupply(12, 5); f.Prepare();
                        Require(!output.Value, "Packet arrival wrote consumer scratch.");
                        Require(!(bool)Get(inlet, "everyFrame") && !(bool)Get(outlet, "everyFrame"), "Hose cadence changed.");
                        inlet.OnUpdate(); Require(output.Value, "Explicit native helper update lost host installation."); saved();
                    });
                    RunHeaterHoseReadBoundaries(check, f, heater, inlet, saved);
                    check("heater hoses: reset rejects stale state and reconnect restores native hose reads", () =>
                    {
                        f.SetHeaterSupply(12); var old = ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get()!;
                        f.SetHeaterSupply(0); Call(f.Sync, "OnEngineBlockState", old); inlet.OnEnter();
                        Require(!((FsmBool)Get(inlet, "storeValue")).Value, "Stale state undid hose removal.");
                        Call(f.Sync, "ReleaseSession"); inlet.OnEnter(); outlet.OnEnter();
                        Require(!((FsmBool)Get(inlet, "storeValue")).Value && !((FsmBool)Get(outlet, "storeValue")).Value, "Session reset retained hoses.");
                        Property(f.Session, "State", SessionState.Idle); Call(f.Sync, "OnEngineBlockState", old);
                        Require(((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get() == null, "Disconnected guest accepted hoses.");
                        // ReleaseSession retires this fixture's synthetic part factories.
                        // Native hose reads can recover before scene/factory rediscovery.
                        Property(f.Session, "State", SessionState.Connected); f.SetHeaterSupply(12);
                        inlet.OnEnter(); outlet.OnEnter();
                        Require(((FsmBool)Get(inlet, "storeValue")).Value && ((FsmBool)Get(outlet, "storeValue")).Value,
                            "Reconnected native hose reads did not recover before factory discovery."); saved();
                    });
                }
            }
            finally { protection.GetSetMethod(true).Invoke(policy, new object[] { originalProtection }); }
        }

        private static void RunHeaterHoseReadBoundaries(Action<string, Action> check, Fixture f,
            PlayMakerFSM heater, FsmStateAction inlet, Action saved)
        {
            var output = (FsmBool)Get(inlet, "storeValue"); var name = (FsmString)Get(inlet, "fsmName");
            var source = f.SavedCoolantHoses[2]!;
            f.SetHeaterSupply(0);
            check("heater hoses: native same-object cache and missing-name fallback retain source identity", () =>
            {
                var other = Empty(source.gameObject, "Other"); other.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true } }; other.Fsm.Init(other);
                try
                {
                    inlet.OnEnter(); name.Value = "Other"; inlet.OnUpdate(); Require(!output.Value, "Same-object cache switched the native source.");
                    Set(inlet, "goLastFrame", null!); inlet.OnUpdate(); Require(output.Value, "Unrelated native source was projected.");
                    foreach (string fallback in new[] { "", "missing" })
                    { name.Value = fallback; Set(inlet, "goLastFrame", null!); inlet.OnUpdate(); Require(!output.Value, "Native first-FSM fallback borrowed saved hose installation."); }
                }
                finally { name.Value = "Data"; Set(inlet, "goLastFrame", null!); UnityEngine.Object.DestroyImmediate(other); }
                saved();
            });
            foreach (string kind in new[] { "source", "source alias in locals", "other hose", "global", "literal" })
                check("heater hoses: unsafe " + kind + " output preserves saved data", () =>
                {
                    var locals = heater.FsmVariables.BoolVariables; var globals = FsmVariables.GlobalVariables.BoolVariables; output.Value = true;
                    try
                    {
                        var alias = f.SavedCoolantHoses[kind == "other hose" ? 3 : 2]!.FsmVariables.FindFsmBool("Installed");
                        if (kind == "literal") Set(inlet, "storeValue", new FsmBool { Value = true });
                        else if (kind == "global") { var list = new List<FsmBool>(globals); list.Add(output); FsmVariables.GlobalVariables.BoolVariables = list.ToArray(); }
                        else
                        {
                            Set(inlet, "storeValue", alias);
                            if (kind != "source") { var list = new List<FsmBool>(locals); list.Add(alias); heater.FsmVariables.BoolVariables = list.ToArray(); }
                        }
                        inlet.OnEnter(); Require(output.Value, "Unsafe output was assigned."); saved();
                    }
                    finally { Set(inlet, "storeValue", output); heater.FsmVariables.BoolVariables = locals; FsmVariables.GlobalVariables.BoolVariables = globals; }
                    inlet.OnEnter(); Require(!output.Value, "Repaired local output stayed unavailable.");
                });
            check("heater hoses: moved known source survives missing metadata without saved-state fallback", () =>
            {
                var profile = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true).GetProperty("GuestEngineInputs", Static);
                var original = profile.GetValue(null, null); string oldName = source.name;
                try
                {
                    source.name = "moved saved heater hose"; profile.GetSetMethod(true).Invoke(null, new object?[] { null });
                    f.ClearEngineBlockInputs(); inlet.OnEnter(); Require(!output.Value, "Missing metadata exposed saved hose state.");
                    f.SetHeaterSupply(12); inlet.OnEnter(); Require(output.Value, "Known hose identity was lost."); saved();
                }
                finally { source.name = oldName; profile.GetSetMethod(true).Invoke(null, new[] { original }); }
            });
        }
    }
}
