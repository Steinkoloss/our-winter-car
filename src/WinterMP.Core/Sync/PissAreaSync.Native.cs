using System;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    internal sealed partial class PissAreaSync
    {
        private PlayMakerFSM? _playerPiss;
        private FsmState? _writeState;
        private FsmStateAction? _nativeWrite;
        private StainWrite? _writeGate;
        private bool _nativeReady;
        private static T? Field<T>(FsmStateAction action, string name) where T : class
            => action.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(action) as T;
        private static bool Flag(FsmStateAction action, string name)
            => action.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(action) is bool value && value;
        private sealed class StainWrite : FsmStateAction
        {
            private readonly PissAreaSync _owner;
            private readonly FsmStateAction _native;
            internal StainWrite(PissAreaSync owner, FsmStateAction native) { _owner = owner; _native = native; }
            private bool Guest => Active(SessionManager.Instance) && !SessionManager.Instance!.IsHost;
            public override void OnEnter() { if (!Guest) _native.OnEnter(); }
            public override void OnUpdate() { if (!Guest) _native.OnUpdate(); }
            public override void OnLateUpdate()
            {
                if (!Guest) { _native.OnLateUpdate(); return; }
                try { _owner.CaptureGuestContribution(); }
                catch (Exception e) { WinterMPPlugin.Log.LogError("PissArea guest contribution: " + e); }
            }
            public override void OnExit() { if (!Guest) _native.OnExit(); _owner._pending = 0; }
        }
        private void BindNative()
        {
            if (_writeGate != null || !Ready) return;
            FsmState? state = null;
            foreach (var candidate in _logic!.FsmStates) if (candidate.Name == "State 2") state = candidate;
            if (state == null || !state.IsInitialized) return;
            var actions = state.Actions;
            var currentTarget = _logic.FsmVariables.FindFsmGameObject("CurrentArea");
            // Install the known world-write boundary before validating the producer.
            // Signature drift must disable contributions, not enable guest speculation.
            int writeIndex = -1;
            for (int i = 0; i < actions.Length; i++)
                if (actions[i].GetType().Name == "SetScale"
                    && Field<FsmOwnerDefault>(actions[i], "gameObject")?.GameObject == currentTarget) {
                    if (writeIndex >= 0) return;
                    writeIndex = i;
                }
            if (currentTarget == null || writeIndex < 0) return;
            _writeState = state; _nativeWrite = actions[writeIndex]; _writeGate = new StainWrite(this, _nativeWrite);
            _writeGate.Init(state);
            var installed = (FsmStateAction[])actions.Clone(); installed[writeIndex] = _writeGate; state.Actions = installed;
            if (actions.Length != 7 || actions[0].GetType().Name != "GetFsmGameObject"
                || actions[1].GetType().Name != "GetScale" || actions[2].GetType().Name != "FloatOperator"
                || actions[3].GetType().Name != "FloatAdd" || actions[4].GetType().Name != "FloatClamp"
                || actions[5].GetType().Name != "SetScale" || actions[6].GetType().Name != "GameObjectChanged") return;
            var change = _logic.FsmVariables.FindFsmFloat("ChangeScale");
            var addition = _logic.FsmVariables.FindFsmFloat("Addition");
            var rate = _logic.FsmVariables.FindFsmFloat("PissRate");
            var clamp = _logic.FsmVariables.FindFsmFloat("Clamp");
            var current = _logic.FsmVariables.FindFsmGameObject("CurrentArea");
            if (change == null || addition == null || rate == null || clamp == null || current == null
                || Field<FsmFloat>(actions[1], "xScale") != change
                || Field<FsmFloat>(actions[2], "float1") != rate || Field<FsmFloat>(actions[2], "float2")?.Value != 3500
                || Convert.ToInt32(actions[2].GetType().GetField("operation")?.GetValue(actions[2])) != 3
                || !Flag(actions[2], "everyFrame")
                || Field<FsmFloat>(actions[2], "storeResult") != addition
                || Field<FsmFloat>(actions[3], "floatVariable") != change || Field<FsmFloat>(actions[3], "add") != addition
                || !Flag(actions[3], "everyFrame") || !Flag(actions[3], "perSecond")
                || Field<FsmFloat>(actions[4], "floatVariable") != change || Field<FsmFloat>(actions[4], "maxValue") != clamp
                || Field<FsmFloat>(actions[4], "minValue")?.Value != 0 || !Flag(actions[4], "everyFrame")
                || Field<FsmOwnerDefault>(actions[5], "gameObject")?.GameObject != current
                || Field<FsmFloat>(actions[5], "x") != change || Field<FsmFloat>(actions[5], "y") != change
                || Field<FsmFloat>(actions[5], "z")?.Value != 1 || !Flag(actions[5], "everyFrame") || !Flag(actions[5], "lateUpdate")) return;
            // Get the serialized target, even when the local Piss object is inactive.
            var playerObject = Field<FsmOwnerDefault>(actions[0], "gameObject")?.GameObject.Value;
            if (playerObject == null) return;
            foreach (var fsm in playerObject.GetComponents<PlayMakerFSM>()) if (fsm.FsmName == "Logic") _playerPiss = fsm;
            if (_playerPiss == null) return;
            _nativeReady = true;
            WinterMPPlugin.Log.LogInfo("PissAreaSync: native stain write gate bound; load/save actions untouched.");
        }
        private void RestoreNative()
        {
            if (_writeState != null && _writeGate != null && _nativeWrite != null) {
                var actions = (FsmStateAction[])_writeState.Actions.Clone();
                for (int i = 0; i < actions.Length; i++) if (ReferenceEquals(actions[i], _writeGate)) actions[i] = _nativeWrite;
                _writeState.Actions = actions;
            }
            _writeState = null; _writeGate = null; _nativeWrite = null; _playerPiss = null; _nativeReady = false;
        }
    }
}
