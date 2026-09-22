using System;
using HutongGames.PlayMaker;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private static void RunWheelReaderSafetyChecks(Action<string, Action> check, WheelHealthFixture f, object item,
            object vehicles, Type guard, Action clean, Action<VehicleCondition> packet,
            Func<ushort, byte, byte, VehicleCondition> state, Action saved)
        {
            Func<bool> prepare = () => (bool)guard.GetMethod("Prepare", Static).Invoke(null, new object[] { true });
            Action recover = () =>
            {
                Require(prepare(), "Repaired tyre reader did not prepare.");
                NativeBagPartChecks.Fire(f.Wheels[0], "State 1"); f.Tick();
                Require(f.Wheels[0].enabled && f.Health[0].Value == 30, "Repaired reader did not resume accepted health."); saved();
            };
            foreach (string stateName in new[] { "State 1", "Flat friction" })
            {
                string nativeState = stateName; int index = nativeState == "State 1" ? 12 : 3;
                foreach (string fieldName in new[] { "storeValue", "variableName", "fsmName", "gameObject", "everyFrame" })
                {
                    string field = fieldName;
                    check("wheel reader safety: " + nativeState + " changed " + field + " stays paused through repeated preparation", () =>
                    {
                        clean(); packet(state(0, 30, 0)); Require(prepare(), "Valid reader was not prepared.");
                        var read = NativeBagPartChecks.State(f.Wheels[0], nativeState).Actions[index]; object previous = Get(read, field);
                        object changed = field == "storeValue" ? (object)f.Saved[0] : field == "everyFrame" ? false
                            : field == "gameObject" ? new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                                GameObject = new FsmGameObject { Name = "ThisTire", UseVariable = true,
                                    Value = f.Wheels[0].FsmVariables.FindFsmGameObject("ThisTire").Value } }
                            : (object)new FsmString { Value = "Changed native input" };
                        try
                        {
                            Set(read, field, changed);
                            Require(!prepare() && !f.Wheels[0].enabled, "Cached binding admitted changed reader operands.");
                            Require(!prepare() && !f.Wheels[0].enabled && !f.Wheels[0].Fsm.RestartOnEnable,
                                "Repeated preparation resumed an unrepaired reader.");
                            Require(f.Wheels[1].enabled, "A changed reader paused an unrelated wheel."); saved();
                        }
                        finally { Set(read, field, previous); prepare(); }
                        recover();
                    });
                }
                foreach (string aliasName in new[] { "global", "saved" })
                {
                    string alias = aliasName;
                    check("wheel reader safety: " + nativeState + " " + alias + " output alias cannot project or resume", () =>
                    {
                        clean(); packet(state(0, 30, 0)); Require(prepare(), "Valid reader was not prepared.");
                        var read = NativeBagPartChecks.State(f.Wheels[0], nativeState).Actions[index];
                        var source = f.Wheels[0].FsmVariables.FindFsmGameObject("ThisTire").Value.GetComponent<PlayMakerFSM>();
                        var vars = alias == "global" ? FsmVariables.GlobalVariables : source.FsmVariables;
                        var before = vars.FloatVariables; var extended = new FsmFloat[before.Length + 1];
                        Array.Copy(before, extended, before.Length); extended[before.Length] = f.Health[0];
                        try
                        {
                            vars.FloatVariables = extended; f.Health[0].Value = 77; read.OnEnter();
                            Require(f.Health[0].Value == 77 && !f.Wheels[0].enabled, "Projection changed an aliased output.");
                            Require(!prepare() && !prepare() && !f.Wheels[0].enabled, "Preparation resumed an unrepaired output alias."); saved();
                        }
                        finally { vars.FloatVariables = before; prepare(); }
                        recover();
                    });
                }
                check("wheel reader safety: " + nativeState + " saved output stays protected when shared state is absent", () =>
                {
                    clean(); Require(prepare(), "Valid reader was not prepared.");
                    var read = NativeBagPartChecks.State(f.Wheels[0], nativeState).Actions[index]; var previous = Get(read, "storeValue");
                    try
                    {
                        Set(read, "storeValue", f.Saved[1]); read.OnEnter();
                        Require(f.Saved[1].Value == 91 && !f.Wheels[0].enabled, "Native fallback wrote into a saved tyre."); saved();
                    }
                    finally { Set(read, "storeValue", previous); f.Saved[1].Value = 91; prepare(); }
                    packet(state(0, 30, 0)); recover();
                });
                check("wheel reader safety: " + nativeState + " missing output pauses the selected read before native execution", () =>
                {
                    clean(); packet(state(0, 30, 0)); Require(prepare(), "Valid reader was not prepared.");
                    var read = NativeBagPartChecks.State(f.Wheels[0], nativeState).Actions[index]; var previous = Get(read, "storeValue");
                    try
                    {
                        read.GetType().GetField("storeValue").SetValue(read, null); read.OnEnter();
                        Require(!f.Wheels[0].enabled, "Missing output was passed into the native reader.");
                        Require(!prepare(), "Missing output was admitted during preparation."); saved();
                    }
                    finally { Set(read, "storeValue", previous); prepare(); }
                    recover();
                });
            }
            check("wheel reader safety: saved alias is unavailable to capture and packet application before preparation", () =>
            {
                clean(); Require(prepare(), "Valid reader was not prepared.");
                var source = f.Wheels[0].FsmVariables.FindFsmGameObject("ThisTire").Value.GetComponent<PlayMakerFSM>();
                var before = source.FsmVariables.FloatVariables; var extended = new FsmFloat[before.Length + 1];
                Array.Copy(before, extended, before.Length); extended[before.Length] = f.Health[0];
                try
                {
                    source.FsmVariables.FloatVariables = extended; f.Health[0].Value = 77;
                    var captured = (VehicleCondition)CallStatic("TryReadConditionState", item)!;
                    Require(!captured.HasWheel(0) && captured.HasWheel(1), "Capture published a saved output alias.");
                    packet(state(0, 30, 0)); Require(f.Health[0].Value == 77 && f.Health[1].Value == 31,
                        "Packet application overwrote aliased data or withheld an unrelated wheel."); saved();
                }
                finally { source.FsmVariables.FloatVariables = before; prepare(); }
                Call(vehicles, "UpdateVehicleCondition", SessionManager.Instance); recover();
            });
            for (int i = 0; i < 4; i++)
            {
                int wheel = i;
                foreach (string identityChange in new[] { "moved", "renamed" })
                {
                    string change = identityChange;
                    check("wheel reader safety: " + change + " wheel " + wheel + " cannot keep a cached reader binding", () =>
                    {
                        clean(); packet(state(0, 30, 0)); Require(prepare(), "Valid reader was not prepared.");
                        var consumer = f.Wheels[wheel]; var parent = consumer.transform.parent;
                        var read = NativeBagPartChecks.State(consumer, "State 1").Actions[12];
                        try
                        {
                            if (change == "moved") consumer.transform.SetParent(f.Body.transform, false);
                            else consumer.Fsm.Name = "Changed condition identity";
                            f.Health[wheel].Value = 77; read.OnEnter();
                            Require(!consumer.enabled && f.Health[wheel].Value == 77, "Changed reader projected through its cached identity.");
                            Require(!prepare() && !consumer.enabled, "Changed reader was readmitted."); saved();
                        }
                        finally { consumer.transform.SetParent(parent, false); consumer.Fsm.Name = "Condition"; prepare(); }
                        NativeBagPartChecks.Fire(consumer, "State 1"); f.Tick();
                        Require(consumer.enabled && f.Health[wheel].Value == 30 + wheel, "Restored wheel identity did not recover."); saved();
                    });
                }
            }
        }
    }
}
