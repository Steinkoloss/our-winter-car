using System;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private static void ValidateWireConnection(WireConnection b)
        {
            var c = b.Rule.Connection!;
            if (b.Data.FsmVariables.FindFsmBool("Installed") == null || b.Data.FsmVariables.FindFsmBool("Trigger") == null
                || b.Prerequisite.FsmVariables.FindFsmBool(c["prerequisiteVariable"]) == null
                || b.Data.FsmVariables.FindFsmGameObject("WireMesh")?.Value != b.Mesh
                || b.Data.FsmVariables.FindFsmGameObject("WireTriggers")?.Value != b.Triggers)
                throw new InvalidOperationException("Wire data/prerequisite identity changed.");
            var basic = PackageStateActions(b.Data, b.Rule.SettledState, "ActivateGameObject", "SetBoolValue", "BoolFlip", "ActivateGameObject");
            WireActivation(basic.Actions[0], "WireMesh", "Installed", false);
            WireActivation(basic.Actions[3], "WireTriggers", "Trigger", false);
            PackageStateActions(b.Data, "Destroy", "ActivateGameObject", "SetBoolValue");
            for (int i = 0; i < b.Ends.Length; i++)
            {
                var end = b.Ends[i];
                // Already-installed native wires have inactive endpoints. Their
                // definitions still need guards before the host reopens them.
                if (!end.Fsm.Initialized) end.Fsm.Init(end);
                if (end.FsmVariables.FindFsmGameObject("ThisWire1")?.Value != b.Mesh
                    || end.FsmVariables.FindFsmGameObject("db_ThisPart")?.Value != b.Data.gameObject
                    || end.FsmVariables.FindFsmGameObject("_OtherEnd")?.Value != b.Ends[1 - i].gameObject
                    || end.transform.parent != b.Triggers.transform
                    || Math.Abs(end.FsmVariables.FindFsmFloat("Tolerance").Value - .1f) > .00001f)
                    throw new InvalidOperationException("Native wire endpoint binding changed.");
                PackageStateActions(end, c["resetState"], "GetParent");
                var sound = PackageStateActions(end, "Sound", "SetBoolValue", "SetStringValue", "SendEventByName");
                var close = PackageField<FsmEventTarget>(sound.Actions[2], "eventTarget");
                if (close == null || close.target != FsmEventTarget.EventTarget.GameObjectFSM
                    || !FitTargetVariable(close.gameObject, "_OtherEnd") || close.fsmName.Value != c["endpointFsm"]
                    || PackageField<FsmString>(sound.Actions[2], "sendEvent")?.Value != "CLOSELOOP"
                    || sound.Transitions.Length != 1 || sound.Transitions[0].EventName != "CLOSELOOP"
                    || sound.Transitions[0].ToState != c["finishState"])
                    throw new InvalidOperationException("Native two-endpoint handshake changed.");
                var finish = PackageStateActions(end, c["finishState"], "MasterAudioPlaySound", "SetFsmBool", "ActivateGameObject", "SendEvent", "ActivateGameObject");
                var write = finish.Actions[1];
                if (!FitTargetVariable(PackageField<FsmOwnerDefault>(write, "gameObject"), "db_ThisPart")
                    || PackageField<FsmString>(write, "fsmName")?.Value != b.Rule.Fsm
                    || PackageField<FsmString>(write, "variableName")?.Value != "Installed"
                    || PackageField<FsmBool>(write, "setValue")?.UseVariable != false
                    || PackageField<FsmBool>(write, "setValue")?.Value != true || finish.Transitions.Length != 0)
                    throw new InvalidOperationException("Wire installation saved destination changed.");
                WireActivation(finish.Actions[2], "ThisWire1", null, true);
                WireActivation(finish.Actions[4], "Owner", null, false);
                var target = PackageField<FsmEventTarget>(finish.Actions[3], "eventTarget");
                if (target == null || target.target != FsmEventTarget.EventTarget.BroadcastAll
                    || PackageField<FsmEvent>(finish.Actions[3], "sendEvent")?.Name != c["resetEvent"]
                    || PackageField<FsmFloat>(finish.Actions[3], "delay")?.Value != 0)
                    throw new InvalidOperationException("Native wiring reset changed.");
                foreach (var action in finish.Actions) RequireFitOneShot(action);
            }
            var steering = FsmHook.FindState(b.Status, c["statusState"]);
            var gate = steering == null ? null : FsmHook.NativeAction(steering, 1);
            if (gate == null || gate.GetType().FullName != "HutongGames.PlayMaker.Actions.ActivateGameObject"
                || b.Status.FsmVariables.FindFsmGameObject(c["statusTarget"])?.Value != b.Ends[1].gameObject)
                throw new InvalidOperationException("Native ignition endpoint gate changed.");
            WireActivation(gate, c["statusTarget"], "Installed1", false);
            b.StatusAction = gate; b.OriginalReady = PackageField<FsmBool>(gate, "activate")!;
        }

        private static void WireActivation(FsmStateAction action, string target, string? variable, bool value)
        {
            var flag = PackageField<FsmBool>(action, "activate");
            if (!FitTargetVariable(PackageField<FsmOwnerDefault>(action, "gameObject"), target)
                || flag == null || (variable == null ? flag.UseVariable || flag.Value != value : !flag.UseVariable || flag.Name != variable)
                || PackageField<FsmBool>(action, "recursive")?.Value != false
                || (bool)action.GetType().GetField("resetOnExit").GetValue(action) || (bool)action.GetType().GetField("everyFrame").GetValue(action))
                throw new InvalidOperationException("Native wire presentation binding changed.");
        }
    }
}
