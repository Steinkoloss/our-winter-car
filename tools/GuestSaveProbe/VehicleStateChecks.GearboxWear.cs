using System;
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
        private static void RunGearboxWearChecks(Action<string, Action> check, DifferentialFixture f,
            Func<bool> prepare, Action<bool, byte> mode, object item)
        {
            var session = SessionManager.Instance ?? throw new InvalidOperationException("Missing native test session.");
            var world = World.GetProperty("Instance", Static).GetValue(null, null); var vehicles = Get(world, "_vehicles");
            var transport = (CaptureTransport)Get(session, "_transport");
            var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null);
            var parent = player.parent; var position = player.localPosition;
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var property = policy.GetType().GetProperty("ProtectWorld", Members); bool previousProtection = (bool)property.GetValue(policy, null);
            Action<bool> protect = value => property.GetSetMethod(true).Invoke(policy, new object[] { value });
            var data = f.Fsm.FsmVariables.FindFsmGameObject("db_Gearbox").Value.GetComponent<PlayMakerFSM>();
            var wear = data.FsmVariables.FindFsmFloat("Wear"); float previousWear = wear.Value;
            var ints = data.FsmVariables.IntVariables; var bools = data.FsmVariables.BoolVariables;
            var damage = new FsmInt { Name = "DamageType", UseVariable = true, Value = 1 };
            var installed = new FsmBool { Name = "Installed", UseVariable = true, Value = true };
            data.FsmVariables.IntVariables = new[] { damage }; data.FsmVariables.BoolVariables = new[] { installed };
            var savedWrites = NativeBagPartChecks.State(f.Fsm, "Wear").Actions; var enabled = new bool[savedWrites.Length];
            for (int i = 0; i < enabled.Length; i++) enabled[i] = savedWrites[i].Enabled;
            uint id = (uint)Get(item, "Id"); ushort sequence = 0; GearboxWearRequest? observed = null;
            Func<GearboxWearRequest> request = () => new GearboxWearRequest { VehicleId = id, PlayerId = 3, Sequence = ++sequence };
            Func<GearboxWearRequest, bool> apply = value => (bool)Call(vehicles, "OnHostGearboxWear", value, (byte)3)!;
            Action budget = () => Call(vehicles, "ClearGearboxWear");
            const string prefix = "gearbox failure wear: ";
            using (var failure = new GearboxConditionFixture())
            {
                var consumerParent = failure.Consumer.transform.parent;
                failure.Consumer.transform.SetParent(f.Fsm.transform, false);
                failure.Consumer.FsmVariables.FindFsmGameObject("db_Gearbox").Value = data.gameObject;
                Action fire = () => { NativeBagPartChecks.Fire(failure.Consumer, "Probe idle"); NativeBagPartChecks.Fire(failure.Consumer, "Reverse"); };
                try
                {
                    check(prefix + "actual guest driver reports native kick-out once without saved wear", () =>
                    {
                        mode(false, 255); Set(item, "LocallyOwned", true); player.SetParent(f.Body.transform, false); prepare();
                        transport.Packets.Clear(); fire(); Require(transport.Packets.Count == 1, "Native failure did not emit exactly one intent.");
                        observed = transport.Packets[0].Message as GearboxWearRequest;
                        Require(observed != null && observed.VehicleId == id && observed.PlayerId == 3
                            && transport.Packets[0].Channel == Channel.ReliableOrdered && PacketCodec.Encode(observed).Length == 9, "Wrong failure intent or channel.");
                        sequence = observed!.Sequence; Require(wear.Value == previousWear && damage.Value == 1, "Guest callback changed saved gearbox data.");
                    });
                    check(prefix + "repeated direct callbacks do not duplicate a native entry", () =>
                    {
                        transport.Packets.Clear(); failure.Writer.OnEnter(); failure.Writer.OnUpdate(); failure.Writer.OnEnter();
                        Require(transport.Packets.Count == 0 && wear.Value == previousWear, "Repeated callback duplicated wear.");
                        fire(); Require(transport.Packets.Count == 1, "Fresh native entry did not report."); observed = (GearboxWearRequest)transport.Packets[0].Message; sequence = observed.Sequence;
                    });
                    check(prefix + "non-driver and disconnected callbacks cannot report or mutate", () =>
                    {
                        player.SetParent(parent, false); player.localPosition = position; prepare(); transport.Packets.Clear(); fire();
                        Require(!failure.Writer.Enabled && transport.Packets.Count == 0, "Non-driver reported wear.");
                        player.SetParent(f.Body.transform, false); SetProperty(session, "State", SessionState.Idle); prepare(); failure.Writer.OnEnter();
                        Require(transport.Packets.Count == 0 && wear.Value == previousWear, "Disconnected callback escaped protection."); mode(false, 255);
                    });
                    foreach (string field in new[] { "subtractValue", "everyFrame", "perSecond" })
                        check(prefix + "changed " + field + " pauses the callback and repairs", () =>
                        {
                            prepare(); var original = Get(failure.Writer, field); transport.Packets.Clear();
                            Set(failure.Writer, field, field == "subtractValue" ? (object)new FsmFloat(.1f) : true);
                            try { fire(); Require(!failure.Consumer.enabled && transport.Packets.Count == 0 && wear.Value == previousWear, "Changed failure callback escaped protection."); }
                            finally { Set(failure.Writer, field, original); prepare(); }
                            Require(failure.Consumer.enabled, "Repaired callback stayed paused.");
                        });
                    check(prefix + "host applies actual captured callback once through native saved helper", () =>
                    {
                        player.SetParent(parent, false); player.localPosition = position; mode(true, 3); protect(false); Set(item, "LocallyOwned", false); budget();
                        for (int i = 0; i < savedWrites.Length; i++) savedWrites[i].Enabled = true;
                        failure.Writer.Enabled = true; wear.Value = 50; failure.Damage.Value = 99;
                        string active = failure.Consumer.ActiveStateName; var operand = Get(failure.Writer, "subtractValue");
                        bool applied = apply(observed!);
                        if (!applied) throw new InvalidOperationException("Captured callback rejected. " + Core.GetType("WinterMP.Core.Diagnostics.SyncEventLog", true).GetMethod("GetSnapshot", Static).Invoke(null, null));
                        Require(wear.Value == new FsmFloat(50f - .0525f).Value && failure.Damage.Value == 99 && damage.Value == 1
                            && failure.Consumer.ActiveStateName == active && ReferenceEquals(operand, Get(failure.Writer, "subtractValue")), "Native wear changed unrelated state or wrong amount.");
                        float after = wear.Value; Require(!apply(observed!) && wear.Value == after, "Duplicate callback subtracted twice.");
                    });
                    check(prefix + "host observer cannot double-charge delegated failure wear", () =>
                    { float before = wear.Value; failure.Writer.OnEnter(); failure.Writer.OnUpdate(); Require(wear.Value == before, "Competing host callback subtracted wear."); });
                    foreach (int kind in new[] { 1, 2, 3 })
                        check(prefix + "native saved damage type " + kind + " admits one wear step", () =>
                        { budget(); damage.Value = kind; wear.Value = 50; Require(apply(request()) && wear.Value == new FsmFloat(50f - .0525f).Value, "Valid saved failure rejected."); });
                    foreach (string fault in new[] { "healthy", "unknown damage", "uninstalled", "duplicate damage", "duplicate installed", "nonfinite wear", "changed amount", "foreign cache" })
                        check(prefix + fault + " rejects once without replay after repair", () =>
                        {
                            budget(); wear.Value = 50; damage.Value = 1;
                            var amount = Get(failure.Writer, "subtractValue"); var cached = Get(failure.Writer, "fsm"); var last = Get(failure.Writer, "goLastFrame");
                            if (fault == "healthy") damage.Value = 0; if (fault == "unknown damage") damage.Value = 255;
                            if (fault == "uninstalled") installed.Value = false;
                            if (fault == "duplicate damage") data.FsmVariables.IntVariables = new[] { damage, new FsmInt { Name = "DamageType", Value = 1 } };
                            if (fault == "duplicate installed") data.FsmVariables.BoolVariables = new[] { installed, new FsmBool { Name = "Installed", Value = true } };
                            if (fault == "nonfinite wear") wear.Value = float.NaN;
                            if (fault == "changed amount") Set(failure.Writer, "subtractValue", new FsmFloat(.1f));
                            if (fault == "foreign cache") { Set(failure.Writer, "goLastFrame", data.gameObject); Set(failure.Writer, "fsm", f.Fsm); }
                            var rejected = request();
                            try { Require(!apply(rejected) && (float.IsNaN(wear.Value) || wear.Value == 50), "Unsafe failure callback changed wear."); }
                            finally
                            {
                                damage.Value = 1; installed.Value = true; wear.Value = 50; data.FsmVariables.IntVariables = new[] { damage }; data.FsmVariables.BoolVariables = new[] { installed };
                                Set(failure.Writer, "subtractValue", amount); Set(failure.Writer, "fsm", cached); Set(failure.Writer, "goLastFrame", last);
                            }
                            Require(!apply(rejected) && wear.Value == 50, "Repair replayed rejected wear.");
                            Require(apply(request()) && wear.Value < 50, "Fresh callback did not recover.");
                        });
                    check(prefix + "matching-path duplicate gearbox cannot receive host wear", () =>
                    {
                        budget(); wear.Value = 50; damage.Value = 1; var twin = MakeGearboxTwin(data); var reference = failure.Consumer.FsmVariables.FindFsmGameObject("db_Gearbox");
                        var cached = Get(failure.Writer, "fsm"); var last = Get(failure.Writer, "goLastFrame"); var rejected = request();
                        try
                        {
                            reference.Value = twin.gameObject;
                            Require(!apply(rejected) && wear.Value == 50 && twin.FsmVariables.FindFsmFloat("Wear").Value == 50,
                                "Matching path and wear substituted a different native saved gearbox.");
                        }
                        finally { reference.Value = data.gameObject; Set(failure.Writer, "fsm", cached); Set(failure.Writer, "goLastFrame", last); UnityEngine.Object.DestroyImmediate(twin.gameObject); }
                        Require(!apply(rejected) && apply(request()), "Target repair revived an old callback or blocked a fresh one.");
                    });
                    check(prefix + "late owners forged senders and host driving cannot spend wear", () =>
                    {
                        budget(); var value = request(); float before = wear.Value;
                        mode(true, 2); Require(!apply(value), "Previous owner retained authority."); mode(true, 3);
                        Require(!(bool)Call(vehicles, "OnHostGearboxWear", value, (byte)2)!, "Forged sender accepted.");
                        Set(item, "LocallyOwned", true); Require(!apply(value), "Local host accepted guest failure."); Set(item, "LocallyOwned", false);
                        Require(wear.Value == before && apply(value), "Rejected identity spent wear or consumed the rightful event.");
                    });
                    check(prefix + "native wear result publishes to every guest", () =>
                    {
                        transport.Packets.Clear(); Call(vehicles, "UpdateDrivetrainWearStates", session);
                        Require(transport.Packets.Count == session.PlayerCount, "Failure wear did not schedule a host result.");
                        foreach (var packet in transport.Packets)
                        { var state = packet.Message as VehicleDrivetrainWearState; Require(state != null && state.Flags == 1 && state.GearboxWear == wear.Value, "Result lost native saved wear."); }
                    });
                    check(prefix + "departure resets the sender while host-local use remains native", () =>
                    {
                        budget(); sequence = 500; Require(apply(request()), "Prior callback rejected."); Call(vehicles, "ForgetVehicleStatePlayer", (byte)3); sequence = 0;
                        Require(apply(request()), "Departed sender history blocked new connection."); mode(true, 255); Set(item, "LocallyOwned", true);
                        wear.Value = 50; failure.Writer.OnEnter(); Require(wear.Value == new FsmFloat(50f - .0525f).Value, "Host-local native failure was blocked.");
                    });
                }
                finally
                {
                    failure.Consumer.transform.SetParent(consumerParent, false); player.SetParent(parent, false); player.localPosition = position;
                    data.FsmVariables.IntVariables = ints; data.FsmVariables.BoolVariables = bools; wear.Value = previousWear;
                    for (int i = 0; i < enabled.Length; i++) savedWrites[i].Enabled = enabled[i];
                    Set(item, "LocallyOwned", false); mode(false, 255); protect(previousProtection); Call(vehicles, "ClearDrivetrainWearStates");
                }
            }
        }
        private static PlayMakerFSM MakeGearboxTwin(PlayMakerFSM original)
        {
            var obj = new GameObject(original.gameObject.name); obj.transform.SetParent(original.transform.parent, false);
            var data = obj.AddComponent<PlayMakerFSM>(); data.enabled = false; Set(data, "fsm", new Fsm()); data.FsmName = "Data";
            var floats = new List<FsmFloat>(); var integers = new List<FsmInt>(); var booleans = new List<FsmBool>();
            foreach (var value in original.FsmVariables.FloatVariables) floats.Add(new FsmFloat { Name = value.Name, UseVariable = true, Value = value.Value });
            foreach (var value in original.FsmVariables.IntVariables) integers.Add(new FsmInt { Name = value.Name, UseVariable = true, Value = value.Value });
            foreach (var value in original.FsmVariables.BoolVariables) booleans.Add(new FsmBool { Name = value.Name, UseVariable = true, Value = value.Value });
            data.FsmVariables.FloatVariables = floats.ToArray(); data.FsmVariables.IntVariables = integers.ToArray(); data.FsmVariables.BoolVariables = booleans.ToArray();
            data.Fsm.StartState = "Idle"; data.Fsm.States = new[] { new FsmState(data.Fsm) { Name = "Idle", Actions = new FsmStateAction[0] } };
            data.Fsm.Init(data); NativeBagPartChecks.Start(data); return data;
        }
    }
}
