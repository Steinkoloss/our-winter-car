using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private sealed partial class Fixture
        {
            internal PlayMakerFSM? SavedCarburettor;
            internal PlayMakerFSM SavedCarburettorPart = null!;
            private bool _carburettorSeeded;
            private PlayMakerFSM EnsureSavedCarburettor()
            {
                if (SavedCarburettor != null) return SavedCarburettor;
                SavedCarburettor = Empty(MountObject("CARPARTS/StartParts/VIN1110/VINP_Carburettor"), "Data");
                var states = new List<FsmState>();
                foreach (string name in new[] { "Idle", "Install 1", "Install 2", "Installed", "Remove part", "Allow removal?", "Allow install?", "Far", "Near", "Remove other", "Update 2", "Init 2", "Load 2", "Save 2" })
                    states.Add(new FsmState(SavedCarburettor.Fsm) { Name = name, Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] });
                SavedCarburettor.Fsm.States = states.ToArray(); SavedCarburettor.Fsm.StartState = "Idle";
                SavedCarburettor.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Installed", UseVariable = true, Value = true } };
                SavedCarburettor.FsmVariables.FloatVariables = new[] { Scalar("Tightness", 40), Scalar("Wear", 77), Scalar("FuelChamber", 17), Scalar("CarbReserve", .17f), Scalar("SettingMixture", 16.5f), Scalar("DataPower", 101), Scalar("DataTorque", 251), Scalar("DataPowerAdd", -.01f) };
                SavedCarburettorPart = Data(Child(SavedCarburettor.gameObject, "saved carburettor"), "VIN1139", 1);
                var floats = new List<FsmFloat>(SavedCarburettorPart.FsmVariables.FloatVariables); floats.Add(Scalar("SettingMixture", 16.5f));
                floats.Add(Scalar("DataPower", 101)); floats.Add(Scalar("DataTorque", 251)); floats.Add(Scalar("DataPowerAdd", -.01f));
                SavedCarburettorPart.FsmVariables.FloatVariables = floats.ToArray(); SavedCarburettorPart.FsmVariables.FindFsmFloat("Wear").Value = 77;
                SavedCarburettor.FsmVariables.GameObjectVariables = new[] { ObjectVar("ActivePart", SavedCarburettorPart.gameObject) };
                return SavedCarburettor;
            }
            private static FsmFloat Scalar(string name, float value) => new FsmFloat { Name = name, UseVariable = true, Value = value };
            internal void SetCarburettor(bool installed, float chamber = 0, float reserve = 0, float mixture = 0, byte baseFlags = 11)
            {
                bool present = installed && (baseFlags & 11) == 11;
                var state = new EngineBlockState { Revision = ++_engineBlockRevision, Flags = (byte)(baseFlags | (present ? 16 : 0)),
                    Wear = (baseFlags & 2) != 0 ? 90 : 0, FuelChamber = present ? chamber : 0, CarbReserve = present ? reserve : 0, SettingMixture = present ? mixture : 0 };
                SeedRequiredAmbient(state); Call(Sync, "OnEngineBlockState", PacketCodec.Decode(PacketCodec.Encode(state)));
                Require(((EngineBlockReplica)Get(Sync, "_engineBlockReplica")).Get()?.Revision == _engineBlockRevision, "Fixture carburettor state rejected.");
            }
            private void AssertSavedCarburettor()
            {
                if (SavedCarburettor == null) return;
                var vars = SavedCarburettor.FsmVariables; var part = SavedCarburettorPart.FsmVariables;
                Require(vars.FindFsmBool("Installed").Value && vars.FindFsmFloat("Wear").Value == 77 && vars.FindFsmFloat("FuelChamber").Value == 17
                    && vars.FindFsmFloat("CarbReserve").Value == .17f && vars.FindFsmFloat("SettingMixture").Value == 16.5f
                    && part.FindFsmFloat("Wear").Value == 77 && part.FindFsmFloat("SettingMixture").Value == 16.5f
                    && part.FindFsmString("ID").Value == "VIN1139" && part.FindFsmInt("AssemblyID").Value == 1
                    && SavedCarburettorPart.transform.parent == SavedCarburettor.transform && vars.FindFsmGameObject("ActivePart").Value == SavedCarburettorPart.gameObject,
                    "Carburettor projection changed the guest's saved fuel, tuning or assembly.");
            }
            internal void ConfigureCarburettorDecision()
            {
                var state = NativeBagPartChecks.State(Reader, "Carburator");
                foreach (int index in new[] { 1, 3, 5, 6 }) { state.Actions[index] = Import(state.Name, index); state.Actions[index].Init(state); }
                RestoreNativeTransitions(state.Name);
            }
            internal void ConfigureMixtureMath(PlayMakerFSM reader)
            {
                var row = FindNativeRow("CORRIS/Simulation/Engine/Fuel", "Mixture");
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    string name = (string)stateRow["name"]; if (name != "Calculate density" && name != "Calculate mixture 2") continue;
                    var state = NativeBagPartChecks.State(reader, name); var raw = (List<object>)stateRow["actions"];
                    for (int i = 0; i < raw.Count; i++)
                    {
                        if (name == "Calculate density" ? i == 0 : i < 2) continue;
                        state.Actions[i] = NativeAction((Dictionary<string, object>)raw[i], reader); state.Actions[i].Init(state);
                    }
                }
            }
            internal PlayMakerFSM MakeNativeCarburettor()
            {
                var obj = EnsureSavedCarburettor().gameObject; UnityEngine.Object.DestroyImmediate(SavedCarburettor);
                var row = FindNativeRow("CARPARTS/StartParts/VIN1110/VINP_Carburettor", "Data"); var data = NativeBagPartChecks.MakeFsm(obj, row); data.Fsm.Init(data);
                data.FsmVariables.FindFsmFloat("Tightness").Value = 40; data.FsmVariables.FindFsmBool("Installed").Value = true; data.FsmVariables.FindFsmFloat("Wear").Value = 77;
                data.FsmVariables.FindFsmFloat("FuelChamber").Value = 17; data.FsmVariables.FindFsmFloat("CarbReserve").Value = .17f;
                data.FsmVariables.FindFsmFloat("DataPower").Value = 101; data.FsmVariables.FindFsmFloat("DataTorque").Value = 251; data.FsmVariables.FindFsmFloat("DataPowerAdd").Value = -.01f;
                data.FsmVariables.FindFsmFloat("SettingMixture").Value = 16.5f; data.FsmVariables.FindFsmGameObject("ActivePart").Value = SavedCarburettorPart.gameObject;
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    var state = NativeBagPartChecks.State(data, (string)stateRow["name"]); var raw = (List<object>)stateRow["actions"];
                    var actions = new FsmStateAction[raw.Count];
                    for (int i = 0; i < actions.Length; i++) actions[i] = state.Name == "Update 2" && i == 0
                        || state.Name == "Remove part" && (i == 1 || i == 2 || i == 9 || i == 16 || i == 17)
                        ? NativeAction((Dictionary<string, object>)raw[i], data) : new Quiet();
                    state.Actions = actions; foreach (var action in actions) action.Init(state); state.Transitions = new FsmTransition[0];
                }
                SavedCarburettor = data; Reader.FsmVariables.FindFsmGameObject(Reader.FsmName == "Cooling" ? "db_Carburettor" : "db_Carb1").Value = obj; return data;
            }
        }

        internal static void RunCarburettor(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN125", "FuelLine", "carburettor-input-probe.json"))
            {
                var mixture = f.AddConsumer("Mixture"); f.Receive(90, 0, 0, true); f.ConfigureCarburettorDecision(); f.ConfigureMixtureMath(mixture);
                var installed = f.Action("Carburator", 0); var chamber = f.Action("Carburator", 2); var reserve = f.Action("Carburator", 4);
                var setting = NativeBagPartChecks.State(mixture, "Calculate density").Actions[0]; var actions = new[] { installed, chamber, reserve, setting };
                var originals = new object[actions.Length]; for (int i = 0; i < actions.Length; i++) originals[i] = Get(actions[i], "gameObject");
                Action read = () => { foreach (var action in actions) action.OnEnter(); };
                Action absent = () => Require(!f.Reader.FsmVariables.FindFsmBool("Installed1").Value && f.Reader.FsmVariables.FindFsmFloat("FuelChamber").Value == 0
                    && f.Reader.FsmVariables.FindFsmFloat("CarbReserve").Value == 0 && mixture.FsmVariables.FindFsmFloat("CarbSetting").Value == 0, "Unavailable carburettor borrowed saved fuel or tuning.");
                check("carburettor inputs: warm native caches are replaced without changing saved data", () =>
                {
                    read(); Require(f.Reader.FsmVariables.FindFsmFloat("FuelChamber").Value == 17 && mixture.FsmVariables.FindFsmFloat("CarbSetting").Value == 16.5f, "Saved native cache did not warm.");
                    f.ClearEngineBlockInputs(); Require(f.Prepare(), "Missing host input failed safe preparation."); read(); absent();
                    Require(!f.SavedCarburettor!.enabled && f.Target(installed).GetComponent<Rigidbody>() == null, "Saved mount active or input proxy physical."); f.AssertSaved();
                });
                check("carburettor inputs: arrival waits for native reads and both consumers share the host observation", () =>
                {
                    string state = f.Reader.ActiveStateName; f.SetCarburettor(true, 25, .1f, 14.5f); Require(f.Prepare(), "Host carburettor failed preparation."); absent();
                    Require(f.Reader.ActiveStateName == state, "Arrival replayed native fuel calculation."); read();
                    Require(f.Reader.FsmVariables.FindFsmBool("Installed1").Value && f.Reader.FsmVariables.FindFsmFloat("FuelChamber").Value == 25
                        && f.Reader.FsmVariables.FindFsmFloat("CarbReserve").Value == .1f && mixture.FsmVariables.FindFsmFloat("CarbSetting").Value == 14.5f, "Consumers lost accepted host fields."); f.AssertSaved();
                });
                var temperature = FsmVariables.GlobalVariables.FindFsmFloat("EngineTemp"); float savedTemperature = temperature.Value;
                // The asset importer retains global defaults as detached wrappers;
                // explicitly bind this fixture's native global references.
                Set(f.Action("Carburator", 3), "minValue", temperature);
                Set(NativeBagPartChecks.State(mixture, "Calculate density").Actions[3], "float1", temperature);
                try
                {
                    temperature.Value = 10;
                    foreach (float value in new[] { -1f, 0f, 20f, 35f })
                    {
                        float v = value;
                        check("carburettor inputs: native fuel clamps chamber=" + v, () =>
                        {
                            f.SetCarburettor(true, v, v / 100, 14.5f); f.Prepare(); f.Fire("State 2"); f.Fire("Carburator");
                            Require(f.Reader.ActiveStateName == "Airfilter" && Near(f.Reader.FsmVariables.FindFsmFloat("FuelChamber").Value, Mathf.Clamp(v, 10, 30))
                                && Near(f.Reader.FsmVariables.FindFsmFloat("CarbReserve").Value, Mathf.Clamp(v / 100, 0, .2f)), "Native carburettor clamps changed."); f.AssertSaved();
                        });
                    }
                    foreach (float value in new[] { 12f, 14.5f, 18f })
                    {
                        float v = value;
                        check("carburettor inputs: native mixture arithmetic uses host setting " + v, () =>
                        {
                            f.SetCarburettor(true, 25, .1f, v); f.Prepare(); NativeBagPartChecks.Fire(mixture, "Calculate density");
                            Require(mixture.FsmVariables.FindFsmFloat("CarbSetting").Value == v, "Host setting absent at native density entry.");
                            mixture.FsmVariables.FindFsmFloat("RatioMultip").Value = .5f; mixture.FsmVariables.FindFsmFloat("Choke").Value = 1;
                            NativeBagPartChecks.Fire(mixture, "Calculate mixture 2");
                            Require(Near(mixture.FsmVariables.FindFsmFloat("Mixture").Value, (v + .5f) / .9f / 11.7f - 1f / 3.2f), "Host setting bypassed native mixture math."); f.AssertSaved();
                        });
                    }
                }
                finally { temperature.Value = savedTemperature; }
                foreach (byte flags in new byte[] { 0, 1, 3, 7, 11, 15 })
                {
                    byte value = flags;
                    check("carburettor inputs: absent assembly flags=" + value + " stops native fuel", () =>
                    {
                        f.SetCarburettor(false, baseFlags: value); f.Prepare(); read(); absent(); f.Fire("State 2"); f.Fire("Carburator");
                        Require(f.Reader.ActiveStateName == "Not Ok 4" && f.Starter.FsmVariables.FindFsmBool("ShutOff").Value, "Absent carburettor failed native shutoff."); f.AssertSaved();
                    });
                }
                check("carburettor inputs: damage stale and conflicting updates cannot substitute guest settings", () =>
                {
                    f.SetCarburettor(true, 23, .13f, 13, 15); f.Prepare(); read(); Require(f.Reader.FsmVariables.FindFsmBool("Installed1").Value, "Block damage removed carburettor.");
                    var old = ((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get()!; old.SettingMixture = 99; Call(f.Sync, "OnEngineBlockState", old);
                    old.Revision--; Call(f.Sync, "OnEngineBlockState", old); f.Prepare(); read(); Require(mixture.FsmVariables.FindFsmFloat("CarbSetting").Value == 13, "Conflicting setting changed mixture."); f.AssertSaved();
                });
                check("carburettor inputs: saved head relocation retains all readers and protection", () =>
                {
                    var head = f.SavedCarburettor!.transform.parent; var parent = head.parent; string name = head.name; head.parent = f.Car.transform; head.name = "moved head";
                    try { Require(f.Prepare() && !f.SavedCarburettor.enabled, "Moving saved head lost carburettor protection."); read(); f.AssertSaved(); }
                    finally { head.parent = parent; head.name = name; }
                });
                foreach (string fieldName in new[] { "fsmName", "variableName", "storeValue", "everyFrame", "gameObject" })
                {
                    string field = fieldName;
                    check("carburettor inputs: foreign mixture " + field + " is contained and repairs", () =>
                    {
                        var saved = Get(setting, field); object changed = field == "everyFrame" ? (object)true : field == "storeValue" ? new FsmFloat { Name = "CarbSetting", UseVariable = true }
                            : field == "gameObject" ? new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = f.Mount.gameObject } }
                            : (object)new FsmString { Value = "Other" };
                        Set(setting, field, changed);
                        try { NativeBagPartChecks.Fire(mixture, "Calculate density"); Require(!mixture.enabled && !f.SavedCarburettor!.enabled, "Foreign carburettor read escaped entry guard."); f.AssertSaved(); }
                        finally { Set(setting, field, field == "gameObject" ? originals[3] : saved); if (!f.Prepare()) f.Prepare(); }
                        Require(mixture.enabled, "Repaired carburettor reader stayed paused.");
                    });
                }
                check("carburettor inputs: destroyed proxy repairs from host state", () =>
                { UnityEngine.Object.DestroyImmediate(f.Target(chamber)); Require(f.Prepare(), "Carburettor proxy failed repair."); read(); Require(f.Reader.FsmVariables.FindFsmFloat("FuelChamber").Value == 23, "Repair lost accepted fuel."); f.AssertSaved(); });
                check("carburettor inputs: disconnect restores native targets and retains saved mount protection", () =>
                {
                    Call(f.Sync, "ReleaseSession"); Require(((EngineBlockReplica)Get(f.Sync, "_engineBlockReplica")).Get() == null && !f.SavedCarburettor!.enabled, "Disconnect retained host record or resumed saved carburettor.");
                    for (int i = 0; i < actions.Length; i++) Require(ReferenceEquals(Get(actions[i], "gameObject"), originals[i]), "Disconnect lost original native target."); f.AssertSaved();
                });
            }
            RunCarburettorCapture(check);
        }
    }
}
