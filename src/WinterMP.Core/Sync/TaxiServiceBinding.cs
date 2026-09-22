using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class TaxiServiceBinding
    {
        private readonly bool _guest;
        private readonly byte _localPlayer;
        private readonly TaxiServiceData _c;
        private readonly PlayMakerFSM _job, _walker, _ring, _phone, _gui;
        private readonly GameObject _car, _customer, _pivot, _handle, _keypad;
        private readonly GameObject[] _objects;
        private readonly Collider[] _colliders;
        private readonly Animation _rootAnimation, _skeletonAnimation;
        private readonly List<Action> _restore = new List<Action>();
        private readonly List<PlayMakerFSM> _paused = new List<PlayMakerFSM>();
        private readonly FsmBool _answer, _occupied, _driverBreak, _phoneUse;
        private readonly FsmFloat _phoneDistance;
        private readonly FsmString _text, _subtitle, _voice, _pickupName, _destinationName;
        private uint _callId, _revision;
        private byte _owner = TaxiServiceState.Nobody;
        private byte[]? _sent;
        private bool _restored;

        internal static TaxiServiceBinding? TryBind(SessionManager session, Action<TaxiCallAction> send, Action<uint> sendRead)
        {
            SyncCatalog.EnsureLoaded();
            var c = SyncCatalog.TaxiService ?? throw new InvalidOperationException("Missing taxi service catalog.");
            PlayMakerFSM? job = null, meter = null, phone = null;
            foreach (var obj in ScenePath.ScanFsms())
            {
                var f = obj as PlayMakerFSM;
                if (f == null) continue;
                string path = ScenePath.Of(f.transform);
                if (path == c["jobPath"] && f.FsmName == c["jobFsm"]) Assign(ref job, f);
                if (path == c["meterPath"] && f.FsmName == c["meterFsm"]) Assign(ref meter, f);
                if (path == c["phonePath"] && f.FsmName == c["phoneFsm"]) Assign(ref phone, f);
            }
            if (job == null || meter == null || phone == null) return null;
            Init(job); Init(meter); Init(phone);
            var walker = Fsm(ObjectVariable(meter, c["customerVariable"]), c["walkerFsm"]);
            return new TaxiServiceBinding(c, job, walker, phone, session, send, sendRead);
        }

        private TaxiServiceBinding(TaxiServiceData c, PlayMakerFSM job, PlayMakerFSM walker, PlayMakerFSM phone,
            SessionManager session, Action<TaxiCallAction> send, Action<uint> sendRead)
        {
            _c = c; _job = job; _walker = walker; _phone = phone; _guest = !session.IsHost; _localPlayer = session.LocalPlayerId;
            _car = ObjectVariable(job, "Car"); _customer = ObjectVariable(job, "Customer");
            _pivot = ObjectVariable(walker, "CarGetInPivot");
            if (ObjectVariable(walker, "Parent") != _customer || !_pivot.transform.IsChildOf(_car.transform))
                throw new InvalidOperationException("Changed taxi customer parent or seat.");
            _ring = Fsm(ObjectVariable(walker, "Carphone"), c["ringFsm"]);
            if (ObjectVariable(_ring, "Customer") != walker.gameObject || ObjectVariable(phone, "Ring") != _ring.gameObject)
                throw new InvalidOperationException("Changed taxi phone customer.");
            _gui = Fsm(ObjectVariable(walker, "GUI"), "SetText");
            _text = Required(_gui.FsmVariables.FindFsmString("Text"));
            _subtitle = Required(_ring.FsmVariables.FindFsmString("Subtitle"));
            _voice = Required(_ring.FsmVariables.FindFsmString("CallID"));
            _pickupName = Required(walker.FsmVariables.FindFsmString("SubtitlePickupPoint"));
            _destinationName = Required(walker.FsmVariables.FindFsmString("SubtitleDestination"));
            _answer = Required(_ring.FsmVariables.FindFsmBool("Answer"));
            _occupied = Required(_ring.FsmVariables.FindFsmBool("Occupied"));
            _driverBreak = Required(_ring.FsmVariables.FindFsmBool("DriverBreak"));
            _phoneUse = Required(phone.FsmVariables.FindFsmBool("Use"));
            _phoneDistance = Required(phone.FsmVariables.FindFsmFloat("Raycast"));
            _handle = ObjectVariable(phone, "Handle"); _keypad = ObjectVariable(phone, "Keypad");
            _rootAnimation = Required(walker.GetComponent<Animation>());
            _skeletonAnimation = Required(ObjectVariable(walker, "Skeleton").GetComponent<Animation>());
            _objects = new[] { _car, _customer, ObjectVariable(job, "CarPhone"), ObjectVariable(job, "Ignition"),
                ObjectVariable(walker, "Char"), _gui.gameObject, ObjectVariable(walker, "CarMassPassenger") };
            _colliders = new Collider[3];
            for (int i = 0; i < 3; i++)
            {
                var property = Field<FsmProperty>(ActionAt(job, "Enable stuff", i + 2, "SetProperty"), "targetProperty");
                var collider = property.TargetObject.Value as SphereCollider;
                if (collider == null || property.PropertyName != "enabled" || !collider.transform.IsChildOf(_car.transform))
                    throw new InvalidOperationException("Changed taxi availability collider.");
                _colliders[i] = collider;
            }
            ValidatePhone();
            State(walker, c["callState"]); State(phone, c["waitState"]);
            try
            {
                InitializeLuggage();
                if (_guest) InstallGuest(send);
                else
                {
                    Observe(walker, c["callState"], NewCall);
                    Observe(phone, c["answerState"], () => { if (_ring.gameObject.activeInHierarchy) _owner = _localPlayer; });
                    Observe(phone, c["closeState"], () => _owner = TaxiServiceState.Nobody);
                    if (walker.ActiveStateName == c["callState"]) NewCall();
                }
                InitializePayday(sendRead);
                WinterMPPlugin.Log.LogInfo("Taxi service bound: " + (_guest ? "host customer replica" : "native host authority"));
            }
            catch { Restore(); throw; }
        }

        private void NewCall()
        {
            _callId = unchecked(_callId + 1); if (_callId == 0) _callId = 1;
            if (!_occupied.Value && !_answer.Value) _owner = TaxiServiceState.Nobody;
        }

        internal TaxiServiceState Capture()
        {
            var state = new TaxiServiceState { Revision = ++_revision, CallId = _callId, CallOwner = _owner,
                CustomerPosition = Net(_customer.transform.position), CustomerRotation = Net(_customer.transform.rotation),
                WalkerPosition = Net(_walker.transform.localPosition), WalkerRotation = Net(_walker.transform.localRotation),
                Pickup = _pickupName.Value, Destination = _destinationName.Value, IndicatorText = _text.Value,
                Subtitle = _subtitle.Value, Voice = _voice.Value };
            for (int i = 0; i < _objects.Length; i++) if (_objects[i].activeSelf) state.Flags |= 1u << i;
            if (_walker.transform.parent == _pivot.transform) state.Flags |= TaxiServiceState.Boarded;
            else if (_walker.transform.parent != _customer.transform) throw new InvalidOperationException("Unknown taxi customer parent.");
            for (int i = 0; i < 3; i++) if (_colliders[i].enabled) state.ColliderFlags |= (byte)(1 << i);
            state.CallPhase = Phase();
            Clip(_rootAnimation, out state.RootClip, out state.RootTime);
            Clip(_skeletonAnimation, out state.SkeletonClip, out state.SkeletonTime);
            CaptureLuggage(state);
            CapturePayday(state);
            return state;
        }

        private TaxiCallPhase Phase()
        {
            if (!_ring.gameObject.activeInHierarchy || _callId == 0) return TaxiCallPhase.Silent;
            if (_ring.ActiveStateName == "Caller") return TaxiCallPhase.Speaking;
            if (_ring.ActiveStateName == "Hangup" || _ring.ActiveStateName == "Beep beep") return TaxiCallPhase.Finished;
            return _walker.ActiveStateName == _c["callState"] && _ring.ActiveStateName == "State 4"
                && !_answer.Value && !_occupied.Value && !_driverBreak.Value ? TaxiCallPhase.Ringing : TaxiCallPhase.Silent;
        }

        internal bool ShouldSend(TaxiServiceState state, bool keep)
        {
            // Animation clocks alone need no packets: replicas run their native clips.
            uint revision = state.Revision; float rootTime = state.RootTime, skeletonTime = state.SkeletonTime;
            state.Revision = 0; state.RootTime = state.SkeletonTime = 0;
            byte[] bytes;
            try { bytes = PacketCodec.Encode(state); }
            finally { state.Revision = revision; state.RootTime = rootTime; state.SkeletonTime = skeletonTime; }
            bool same = _sent != null && _sent.Length == bytes.Length;
            if (same) for (int i = 0; i < bytes.Length; i++) if (_sent![i] != bytes[i]) { same = false; break; }
            if (same && !keep) return false;
            _sent = bytes; return true;
        }

        internal void Act(SessionManager session, TaxiCallIntent intent, byte actor)
        {
            bool near = GamblingSync.TryPlayerPosition(session, actor, out var position)
                && (position - _phone.transform.position).sqrMagnitude <= 16;
            if (!TaxiServicePolicy.CanAct(Capture(), intent, actor, near))
            { SyncEventLog.Record("taxi-call-rejected", actor + "/" + intent.CallId + "/" + intent.Action); return; }
            if (intent.Action == TaxiCallAction.Answer)
            {
                _owner = actor;
                // These are the two audited SetFsmBool outputs of Pick phone. Opening
                // the host's keypad or changing its hand/camera would affect the wrong player.
                _answer.Value = _occupied.Value = true;
            }
            else CloseCall();
            SyncEventLog.Record("taxi-call", actor + "/" + intent.CallId + "/" + intent.Action);
        }

        internal void CheckCaller(SessionManager session)
        {
            if (_owner == TaxiServiceState.Nobody || _owner == _localPlayer) return;
            foreach (var player in session.Players) if (player.PlayerId == _owner && !player.IsDead) return;
            CloseCall();
        }
        internal void ForgetCaller(byte playerId)
        {
            if (!_guest && _owner == playerId) CloseCall();
        }
        private void CloseCall()
        {
            _answer.Value = _occupied.Value = false; _owner = TaxiServiceState.Nobody;
            if (_ring != null) _ring.gameObject.SetActive(false);
        }

        private static void Clip(Animation animation, out string clip, out float time)
        {
            clip = ""; time = 0;
            foreach (AnimationState state in animation)
                if (animation.IsPlaying(state.name)) { clip = state.name; time = Mathf.Clamp(state.time, 0, 1000000); return; }
        }
        private void Observe(PlayMakerFSM fsm, string name, Action callback)
        {
            var state = State(fsm, name); var original = state.Actions;
            FsmHook.OnStateEnter(fsm, name, callback);
            var installed = state.Actions;
            _restore.Add(() => { if (ReferenceEquals(state.Actions, installed)) state.Actions = original; });
        }
        internal void Restore()
        {
            if (_restored) return; _restored = true;
            ReleaseOutgoingPhone();
            if (!_guest && _owner != TaxiServiceState.Nobody && _owner != _localPlayer) CloseCall();
            for (int i = _restore.Count - 1; i >= 0; i--)
                try { _restore[i](); } catch (Exception e) { WinterMPPlugin.Log.LogWarning("Taxi restore: " + e.Message); }
            _restore.Clear();
            ClearSubtitle();
            foreach (var resume in _resume) resume();
            _resume.Clear();
        }
        private static void Assign(ref PlayMakerFSM? target, PlayMakerFSM fsm)
        { if (target != null) throw new InvalidOperationException("Ambiguous taxi binding."); target = fsm; }
        private static void Init(PlayMakerFSM fsm) { if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm); }
        private static T Required<T>(T? value) where T : class => value ?? throw new InvalidOperationException("Missing taxi binding.");
        private static GameObject ObjectVariable(PlayMakerFSM fsm, string name) => Required(fsm.FsmVariables.FindFsmGameObject(name)?.Value);
        private static PlayMakerFSM Fsm(GameObject go, string name)
        {
            PlayMakerFSM? found = null;
            foreach (var f in go.GetComponents<PlayMakerFSM>()) if (f.FsmName == name) Assign(ref found, f);
            Init(Required(found)); return found!;
        }
        private static FsmState State(PlayMakerFSM fsm, string name) => Required(FsmHook.FindState(fsm, name));
        private static FsmStateAction ActionAt(PlayMakerFSM fsm, string state, int index, string type)
        {
            var a = Required(FsmHook.NativeAction(State(fsm, state), index));
            if (a.GetType().Name != type) throw new InvalidOperationException("Changed taxi action " + state + "/" + index);
            return a;
        }
        private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name).GetValue(target);
        private static NetVector3 Net(Vector3 v) => new NetVector3(v.x, v.y, v.z);
        private static NetQuaternion Net(Quaternion q) => new NetQuaternion(q.x, q.y, q.z, q.w);
        private static Vector3 Unity(NetVector3 v) => new Vector3(v.X, v.Y, v.Z);
        private static Quaternion Unity(NetQuaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W);
    }
}
