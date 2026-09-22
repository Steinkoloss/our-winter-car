using System;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ApplianceSync
    {
        private static T? StoveField<T>(object action, string name) where T : class =>
            action.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(action) as T;

        private static FsmStateAction StoveAction(PlayMakerFSM fsm, string state, int index, string type)
        {
            var s = FsmHook.FindState(fsm, state);
            var action = s == null ? null : FsmHook.NativeAction(s, index);
            if (action == null || !action.Enabled || action.GetType().FullName != "HutongGames.PlayMaker.Actions." + type)
                throw new InvalidOperationException("Native stove action changed: " + fsm.FsmName + "::" + state + "[" + index + "]");
            return action;
        }

        private static void StoveFloat(FsmStateAction action, string field, FsmFloat expected)
        {
            if (!ReferenceEquals(StoveField<FsmFloat>(action, field), expected)) throw new InvalidOperationException("Native stove float target changed.");
        }

        private static void ValidateKnob(StoveKnob k, StoveData c)
        {
            if (k.Up.Actions != k.UpActions || k.Down.Actions != k.DownActions || k.UpActions.Length != 4 || k.DownActions.Length != 4
                || k.Step.Value != 51.4f || !k.Fsm.Fsm.Started) throw new InvalidOperationException("Native stove knob changed.");
            var up = StoveAction(k.Fsm, c["up"], 0, "FloatAdd");
            var down = StoveAction(k.Fsm, c["down"], 0, "FloatSubtract");
            StoveFloat(up, "floatVariable", k.Rotation); StoveFloat(up, "add", k.Step);
            StoveFloat(down, "floatVariable", k.Rotation); StoveFloat(down, "subtract", k.Step);
            foreach (var action in new[] { up, down })
                if ((bool)action.GetType().GetField("everyFrame").GetValue(action) || (bool)action.GetType().GetField("perSecond").GetValue(action))
                    throw new InvalidOperationException("Native stove step timing changed.");
            foreach (var pair in new[] { c["up"], c["down"] })
            {
                if (FsmHook.NativeAction(FsmHook.FindState(k.Fsm, pair)!, 1)!.Enabled) throw new InvalidOperationException("Native stove clamp changed.");
                var compare = StoveAction(k.Fsm, pair, 3, "FloatCompare"); StoveFloat(compare, "float1", k.Rotation);
                var limit = StoveField<FsmFloat>(compare, "float2");
                if (limit == null || limit.UseVariable || limit.Value != (pair == c["up"] ? 330 : -360)) throw new InvalidOperationException("Native stove wrap changed.");
                var rotation = StoveAction(k.Fsm, pair, 2, "SetRotation");
                StoveFloat(rotation, "yAngle", k.Rotation); ValidateKnobMesh(k, rotation, c);
            }
            foreach (string reset in new[] { c["resetUp"], c["resetDown"] })
            {
                var zero = StoveAction(k.Fsm, reset, 0, "SetFloatValue"); StoveFloat(zero, "floatVariable", k.Rotation);
                var value = StoveField<FsmFloat>(zero, "floatValue");
                if (value == null || value.UseVariable || value.Value != 0) throw new InvalidOperationException("Native stove reset changed.");
            }
            var read = StoveAction(k.Fsm, c["read"], 0, "GetRotation");
            StoveFloat(read, "yAngle", k.Data); ValidateKnobMesh(k, read, c);
        }

        private static void ValidateKnobMesh(StoveKnob k, FsmStateAction action, StoveData c)
        {
            var target = StoveField<FsmOwnerDefault>(action, "gameObject");
            var mesh = k.Fsm.FsmVariables.FindFsmGameObject(c["mesh"]);
            if (target == null || target.OwnerOption != OwnerDefaultOption.SpecifyGameObject || !ReferenceEquals(target.GameObject, mesh)
                || mesh?.Value == null || mesh.Value.transform.parent != k.Fsm.transform
                || Convert.ToInt32(action.GetType().GetField("space").GetValue(action)) != 1)
                throw new InvalidOperationException("Native stove mesh target changed.");
        }

        private static EllipsoidParticleEmitter StoveSmoke(PlayMakerFSM sim, StoveData c, string state, bool enabled,
            EllipsoidParticleEmitter? expected)
        {
            var action = StoveAction(sim, state, 0, "SetProperty");
            var property = StoveField<FsmProperty>(action, "targetProperty");
            var smoke = property?.TargetObject?.Value as EllipsoidParticleEmitter;
            if (property == null || !property.setProperty || property.PropertyName != "emit" || property.PropertyName != c["smokeProperty"]
                || property.TargetTypeName != "UnityEngine.EllipsoidParticleEmitter" || smoke == null
                || (expected != null && smoke != expected) || property.BoolParameter == null
                || property.BoolParameter.IsNone || property.BoolParameter.UseVariable || property.BoolParameter.Value != enabled
                || action.GetType().GetField("everyFrame")?.GetValue(action) is not bool everyFrame || everyFrame)
                throw new InvalidOperationException("Native stove smoke target changed.");
            return smoke;
        }

        private void BindStove(Oven oven)
        {
            var c = SyncCatalog.Stoves;
            if (oven.Stove != null || oven.StoveFailed || c == null || !c.Paths.Contains(oven.ContainerPath) || oven.Sim == null) return;
            try
            {
                var sim = oven.Sim; var root = sim.transform.parent;
                if (!sim.Fsm.Initialized || !sim.Fsm.Started) return;
                if (sim.name != c["simulation"] || sim.FsmName != c["simFsm"] || FsmHook.FindState(sim, c["idle"]) == null)
                    throw new InvalidOperationException("Native stove simulation changed.");
                var s = new StoveBinding { Guest = SessionManager.Instance?.IsHost == false, OldFuse = oven.Fuse?.Value ?? false };
                s.Light = sim.FsmVariables.FindFsmGameObject(c["light"])?.Value!;
                if (s.Light == null || s.Light.transform.parent != root) throw new InvalidOperationException("Native stove indicator changed.");
                s.OldLight = s.Light.activeSelf;
                s.Smoke = StoveSmoke(sim, c, c["smokeOff"], false, null);
                s.OldSmoke = s.Smoke.emit;
                for (int i = 0; i < 4; i++)
                {
                    var knob = root.Find(c["knobPrefix"] + (i + 1));
                    if (knob == null) throw new InvalidOperationException("Native stove knob missing.");
                    PlayMakerFSM? fsm = null;
                    foreach (var f in knob.GetComponents<PlayMakerFSM>()) if (f.FsmName == c["knobFsm"])
                    { if (fsm != null) throw new InvalidOperationException("Ambiguous stove knob."); fsm = f; }
                    if (fsm == null) throw new InvalidOperationException("Native stove control missing.");
                    if (!fsm.Fsm.Initialized || !fsm.Fsm.Started) return;
                    var vars = fsm.FsmVariables;
                    var k = new StoveKnob { Fsm = fsm, Rotation = vars.FindFsmFloat(c["rotation"]), Data = vars.FindFsmFloat(c["data"]), Step = vars.FindFsmFloat(c["step"]),
                        Up = FsmHook.FindState(fsm, c["up"])!, Down = FsmHook.FindState(fsm, c["down"])! };
                    if (k.Rotation == null || k.Data == null || k.Step == null || k.Up == null || k.Down == null) throw new InvalidOperationException("Native stove control variables missing.");
                    k.UpActions = k.Up.Actions; k.DownActions = k.Down.Actions;
                    k.OldRotation = k.Rotation.Value; k.OldData = k.Data.Value;
                    ValidateKnob(k, c);
                    k.Reset = StoveAction(fsm, c["resetUp"], 0, "SetFloatValue");
                    k.Render = StoveAction(fsm, c["up"], 2, "SetRotation"); k.Read = StoveAction(fsm, c["read"], 0, "GetRotation");
                    s.Knobs[i] = k;
                    var heat = sim.FsmVariables.FindFsmFloat(c["platePrefix"] + (i + 1) + c["heatSuffix"]);
                    if (heat == null) throw new InvalidOperationException("Native stove heat missing.");
                    oven.Heats[i] = heat; s.OldHeat[i] = heat.Value;
                    var plate = root.Find(c["platePrefix"] + (i + 1));
                    var grill = plate?.Find(c["grill"]); var burn = plate?.Find(c["burn"]);
                    if (grill == null || burn == null || grill.GetComponent<Collider>() == null || burn.GetComponent<Collider>() == null)
                        throw new InvalidOperationException("Native stove cooking triggers changed.");
                    s.Grills[i] = grill.gameObject; s.Burns[i] = burn.gameObject;
                    s.OldGrills[i] = grill.gameObject.activeSelf; s.OldBurns[i] = burn.gameObject.activeSelf;
                    StoveSmoke(sim, c, c["smoke"] + (i == 0 ? string.Empty : " " + (i + 1)), true, s.Smoke);
                    var hazard = StoveAction(sim, IgnitionStates[i], 0, "SetFloatValue");
                    var fireHazard = sim.FsmVariables.FindFsmFloat(c["platePrefix"] + (i + 1) + c["hazardSuffix"]);
                    if (fireHazard == null) throw new InvalidOperationException("Native stove fire hazard missing.");
                    StoveFloat(hazard, "floatVariable", fireHazard);
                    s.FireHazards[i] = fireHazard; s.OldFireHazards[i] = fireHazard.Value;
                    var fire = FsmHook.NativeAction(FsmHook.FindState(sim, IgnitionStates[i])!, 1);
                    if (fire == null || fire.Enabled != c.IgnitionEnabled[c.Paths.IndexOf(oven.ContainerPath)] || fire.GetType().Name != "SendEventByName")
                        throw new InvalidOperationException("Native stove ignition behavior changed.");
                    s.Ignitions[i] = new[] { hazard, fire };
                }
                oven.Stove = s;
                if (s.Guest)
                {
                    if (!s.Pause.Suppress(sim)) throw new InvalidOperationException("Could not pause guest stove simulation.");
                    for (byte i = 0; i < 4; i++)
                    {
                        byte plate = i;
                        s.Knobs[i].Up.Actions = new FsmStateAction[] { new FsmHookAction(() => QueueStoveTurn(oven, plate, 1)) };
                        s.Knobs[i].Down.Actions = new FsmStateAction[] { new FsmHookAction(() => QueueStoveTurn(oven, plate, 0)) };
                    }
                }
                SyncEventLog.Record("stove-bound", oven.ContainerPath + " guest=" + s.Guest);
            }
            catch (Exception e) { FailStove(oven, e); }
        }
    }
}
