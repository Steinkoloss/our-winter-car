using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class HeatSourceSync
    {
        private SaunaNative? _sauna;
        private SaunaTimerAuthority? _saunaAuthority;
        private SaunaTimerClient? _saunaClient;
        private bool _saunaFailed;
        private float _nextSaunaState;
        private readonly Dictionary<FsmState, FsmStateAction[]> _saunaStates = new Dictionary<FsmState, FsmStateAction[]>();
        private readonly Dictionary<FsmStateAction, bool> _saunaEnabled = new Dictionary<FsmStateAction, bool>();
        private void BindSauna(Source source)
        {
            if (source.Id != SaunaTimerAuthority.SourceId || _sauna != null || _saunaFailed || source.Anchor == null) return;
            var session = SessionManager.Instance;
            var knob = FindChildFsm(source.Anchor, SaunaTimerAuthority.ButtonPath, "Screw");
            var simulation = source.Simulation;
            if (session == null || knob == null || simulation == null || !SaunaReady(knob) || !SaunaReady(simulation)) return;
            try
            {
                var native = new SaunaNative(knob, simulation);
                native.Validate(); // No suppression or timer writes before full native validation and vanilla load.
                _sauna = native;
                _saunaClient = _saunaClient ?? new SaunaTimerClient(session.LocalPlayerId);
                if (session.IsHost) _saunaAuthority = new SaunaTimerAuthority(NewSaunaEpoch(), native);
                HookSauna(native.Increase, true); HookSauna(native.Decrease, false);
                DisableSauna(native.Wait.Actions[0]); DisableSauna(native.Wait.Actions[1]);
                if (!session.IsHost)
                {
                    foreach (var state in simulation.Fsm.States)
                        foreach (var action in state.Actions)
                        {
                            var output = action.GetType().GetField("storeResult")?.GetValue(action) as FsmFloat;
                            // Only timer/countdown/knob writers: leave electricity, temperature, steam and Burn alone.
                            if ((action.GetType().Name == "FloatOperator" && (output?.Name == "Time" || output?.Name == "KnobRotation"))
                                || (action.GetType().Name == "SetRotation" && state.Name == "Heat up")) DisableSauna(action);
                        }
                }
                WinterMPPlugin.Log.LogInfo("Sauna ButtonTime timer bound (portable candidate; native acceptance separate).");
            }
            catch (Exception e) { FailSauna(e); }
        }
        // The game's net35 provider has no IDisposable API. Reuse one provider, as the cabin seam does.
        private static readonly System.Security.Cryptography.RNGCryptoServiceProvider SaunaRandom = new System.Security.Cryptography.RNGCryptoServiceProvider();
        private static uint NewSaunaEpoch()
        {
            var bytes = new byte[4]; uint value;
            do { SaunaRandom.GetBytes(bytes); value = BitConverter.ToUInt32(bytes, 0); } while (value == 0);
            return value;
        }
        private static bool SaunaReady(PlayMakerFSM fsm) => fsm != null && fsm.enabled && fsm.gameObject.activeInHierarchy
            && fsm.Fsm.Initialized && fsm.Fsm.Started && fsm.ActiveStateName != "Check data"
            && fsm.ActiveStateName != "Load game" && fsm.ActiveStateName != "Save game";
        private void HookSauna(FsmState state, bool increase)
        {
            _saunaStates.Add(state, state.Actions);
            state.Actions = new FsmStateAction[] { new FsmHookAction(() => LocalSaunaTurn(increase)) };
        }
        private void DisableSauna(FsmStateAction action)
        { if (!_saunaEnabled.ContainsKey(action)) _saunaEnabled.Add(action, action.Enabled); action.Enabled = false; }
        private void LocalSaunaTurn(bool increase)
        {
            var session = SessionManager.Instance;
            if (session == null || _sauna == null || _saunaFailed || _saunaClient == null || Camera.main == null) return;
            // The input state is reached via vanilla MousePickEvent. The host independently rechecks the ray.
            var eye = Camera.main.transform.position.ToNet(); var direction = Camera.main.transform.forward.ToNet();
            if (session.IsHost) _saunaAuthority!.Publish(session.LocalPlayerId, state => _saunaClient.Receive(true, state));
            var intent = _saunaClient.Create(increase, eye, direction);
            if (intent == null) return;
            if (session.IsHost) OnSaunaIntent(intent, session.LocalPlayerId);
            else session.SendWorldMessage(intent, Channel.ReliableOrdered);
        }
        internal void OnSaunaIntent(SaunaTimerIntent intent, byte authenticatedActor)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || _saunaAuthority == null || _saunaFailed) return;
            _saunaAuthority.Execute(authenticatedActor, intent, state =>
            {
                _saunaClient!.Receive(true, state);
                session.SendWorldMessage(state, Channel.ReliableOrdered);
            });
            if (_saunaAuthority.Faulted) FailSauna(new InvalidOperationException("Native turn/observation/publication failed; no retry."));
        }
        internal void OnSaunaState(SaunaTimerState state)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || _saunaFailed) return;
            _saunaClient = _saunaClient ?? new SaunaTimerClient(session.LocalPlayerId);
            if (_saunaClient.Receive(true, state)) ApplySauna();
        }
        private void ApplySauna()
        {
            if (_sauna == null || _saunaClient?.Current == null || _saunaFailed) return;
            try
            {
                if (!SaunaReady(_sauna.Knob) || !SaunaReady(_sauna.Simulation)) return;
                var state = _saunaClient.Current;
                _sauna.Timer.Value = state.Timer; _sauna.Clock.Value = state.Time;
                _sauna.Mesh.localEulerAngles = new Vector3(0, state.KnobAngle, 0);
            }
            catch (Exception e) { FailSauna(e); }
        }
        private void UpdateSauna(SessionManager session)
        {
            if (_sauna == null || _saunaFailed) return;
            if (!session.IsHost) { ApplySauna(); return; }
            if (!SaunaReady(_sauna.Knob) || !SaunaReady(_sauna.Simulation) || Time.unscaledTime < _nextSaunaState) return;
            _nextSaunaState = Time.unscaledTime + 1f;
            foreach (var player in session.Players)
                _saunaAuthority!.Publish(player.PlayerId, state => session.SendWorldMessage(state, Channel.ReliableOrdered));
            if (_saunaAuthority!.Faulted) FailSauna(new InvalidOperationException("Native sauna observation failed."));
        }
        private void FailSauna(Exception e)
        { _saunaFailed = true; WinterMPPlugin.Log.LogError("Sauna timer disabled without retry: " + e); }
        private void ClearSauna()
        {
            foreach (var pair in _saunaStates) pair.Key.Actions = pair.Value;
            foreach (var pair in _saunaEnabled) pair.Key.Enabled = pair.Value;
            _saunaStates.Clear(); _saunaEnabled.Clear(); _sauna = null; _saunaAuthority = null; _saunaClient = null;
            _saunaFailed = false; _nextSaunaState = 0;
        }
        private sealed class SaunaNative : ISaunaTimerNative
        {
            internal readonly PlayMakerFSM Knob, Simulation;
            internal readonly FsmFloat Timer, Clock;
            internal readonly Transform Mesh;
            internal readonly FsmState Increase, Decrease, Wait;
            private readonly FsmStateAction[] _increase, _decrease;
            internal SaunaNative(PlayMakerFSM knob, PlayMakerFSM simulation)
            {
                Knob = knob; Simulation = simulation;
                Timer = knob.FsmVariables.FindFsmFloat("Timer") ?? throw new InvalidOperationException("Missing Timer");
                Clock = simulation.FsmVariables.FindFsmFloat("Time") ?? throw new InvalidOperationException("Missing Time");
                Mesh = knob.transform.Find("mesh") ?? throw new InvalidOperationException("Missing timer mesh");
                Increase = Require(knob, "Screw", "FloatAdd", "FloatClamp", "SetRotation");
                Decrease = Require(knob, "Unscrew", "FloatSubtract", "FloatClamp", "SetRotation");
                Wait = Require(knob, "Wait", "FloatOperator", "SetFsmFloat", "Wait");
                _increase = Increase.Actions; _decrease = Decrease.Actions;
            }
            private static FsmState Require(PlayMakerFSM fsm, string name, params string[] types)
            {
                var s = FsmHook.FindState(fsm, name);
                if (s == null || !s.IsInitialized || s.Actions.Length != types.Length) throw new InvalidOperationException("Sauna state changed: " + name);
                for (int i=0; i<types.Length; i++) if (s.Actions[i].GetType().Name != types[i] || !s.Actions[i].Enabled)
                    throw new InvalidOperationException("Sauna action changed: " + name);
                return s;
            }
            private static T Field<T>(FsmStateAction a, string n) where T:class => a.GetType().GetField(n)?.GetValue(a) as T
                ?? throw new InvalidOperationException("Sauna field changed: " + n);
            internal void Validate()
            {
                foreach (var actions in new[] {_increase, _decrease})
                {
                    var delta = actions[0]; var clamp = actions[1];
                    var step = Field<FsmFloat>(delta, delta.GetType().Name == "FloatAdd" ? "add" : "subtract");
                    if (Field<FsmFloat>(delta,"floatVariable") != Timer || step.Name != "ScrewAmount" || step.Value != 10
                        || !Equals(delta.GetType().GetField("everyFrame")?.GetValue(delta),false)
                        || !Equals(delta.GetType().GetField("perSecond")?.GetValue(delta),false)
                        || Field<FsmFloat>(clamp,"floatVariable") != Timer || Field<FsmFloat>(clamp,"minValue").Value != 1
                        || Field<FsmFloat>(clamp,"maxValue").Value != 120)
                        throw new InvalidOperationException("Sauna timer step/clamp changed.");
                }
                if (Field<FsmFloat>(Wait.Actions[0],"float1") != Timer || Field<FsmFloat>(Wait.Actions[0],"float2").Value != 6
                    || Convert.ToInt32(Wait.Actions[0].GetType().GetField("operation")?.GetValue(Wait.Actions[0])) != 2
                    || Field<FsmFloat>(Wait.Actions[0],"storeResult").Name != "Math1"
                    || Field<FsmString>(Wait.Actions[1],"fsmName").Value != "Time"
                    || Field<FsmString>(Wait.Actions[1],"variableName").Value != "Time"
                    || Field<FsmFloat>(Wait.Actions[1],"setValue").Name != "Math1"
                    || Field<FsmOwnerDefault>(Wait.Actions[1],"gameObject").GameObject.Value != Simulation.gameObject)
                    throw new InvalidOperationException("Sauna native Time propagation changed.");
                if (Knob.GetComponent<Collider>() == null) throw new InvalidOperationException("Missing knob collider.");
            }
            public SaunaTimerValues Read()
            {
                if (!SaunaReady(Knob) || !SaunaReady(Simulation)) throw new InvalidOperationException("Sauna native unavailable.");
                return new SaunaTimerValues { Timer = Timer.Value, Time = Clock.Value, KnobAngle = Mesh.localEulerAngles.y };
            }
            public SaunaTimerContact Observe(byte actor, NetVector3 eye, NetVector3 direction)
            {
                var c = new SaunaTimerContact { PoseAge = float.PositiveInfinity, DistanceSquared = float.PositiveInfinity };
                var session = SessionManager.Instance!; Vector3 feet = Vector3.zero;
                if (actor == session.LocalPlayerId)
                {
                    c.Present = PlayerSyncManager.Instance != null && PlayerSyncManager.Instance.TryReadLocalPose(out feet, out var rotation);
                    c.Alive = DeathSyncManager.Instance?.IsLocalDead != true; c.PoseAge = 0;
                }
                else foreach (var player in session.Players)
                {
                    if (player.PlayerId != actor) continue;
                    c.Present = player.LastTransformTime > 0; c.Alive = !player.IsDead;
                    c.PoseAge = Time.unscaledTime - player.LastTransformTime; feet = player.Position; break;
                }
                c.DistanceSquared = (feet - Knob.transform.position).sqrMagnitude;
                var collider = Knob.GetComponent<Collider>();
                c.Available = SaunaReady(Knob) && SaunaReady(Simulation) && collider != null && collider.enabled;
                var origin = new Vector3(eye.X,eye.Y,eye.Z); var ray = new Vector3(direction.X,direction.Y,direction.Z);
                float eyeDistance = (origin - feet).sqrMagnitude, length = ray.sqrMagnitude;
                if (!SaunaTimerValues.Finite(eyeDistance) || eyeDistance > 5.29f || !SaunaTimerValues.Finite(length) || length < .99f || length > 1.01f) return c;
                c.Contact = Physics.Raycast(origin, ray, out var hit, 1f, Physics.DefaultRaycastLayers) && hit.collider == collider;
                return c;
            }
            public void Turn(bool increase)
            {
                var actions = increase ? _increase : _decrease;
                for (int i=0;i<actions.Length;i++) actions[i].OnEnter();
                // These are the original native Timer*6 and SetFsmFloat actions, detached from ambient Wait execution.
                for (int i=0;i<2;i++)
                {
                    var a = Wait.Actions[i]; bool enabled = a.Enabled;
                    try { a.Enabled = true; a.OnEnter(); } finally { a.Enabled = enabled; }
                }
            }
        }
    }
}
