using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class TaxiServiceBinding
    {
        private readonly List<Action> _resume = new List<Action>();
        private uint _shownRevision, _shownCall;
        private bool _shown;
        private bool _canDial, _outgoingPhone;
        private TaxiCallPhase _shownPhase;
        private byte _shownOwner = TaxiServiceState.Nobody;
        private float _ringAt;
        private string _shownRootClip = "", _shownSkeletonClip = "", _ownedSubtitle = "";
        private FsmStateAction _ringSound = null!, _callerSound = null!, _stopCaller = null!;

        private void ValidatePhone()
        {
            for (int i = 0; i < 2; i++)
            {
                string variable = i == 0 ? "Answer" : "Occupied";
                foreach (string state in new[] { _c["answerState"], _c["closeState"] })
                {
                    var a = ActionAt(_phone, state, i + 3, "SetFsmBool");
                    if (Field<FsmString>(a, "fsmName").Value != _c["ringFsm"]
                        || Field<FsmString>(a, "variableName").Value != variable
                        || Field<FsmBool>(a, "setValue").Value != (state == _c["answerState"])
                        || Field<FsmOwnerDefault>(a, "gameObject").GameObject.Value != _ring.gameObject)
                        throw new InvalidOperationException("Changed taxi phone intent output.");
                }
            }
            var success = ActionAt(_ring, "Hangup", 3, "SendEventByName");
            if (Field<FsmString>(success, "sendEvent").Value != "SUCCESS") throw new InvalidOperationException("Changed taxi acceptance event.");
            _ringSound = ActionAt(_ring, "State 4", 1, "MasterAudioPlaySound");
            _callerSound = ActionAt(_ring, "Caller", 2, "MasterAudioPlaySound");
            _stopCaller = ActionAt(_ring, "Hangup", 0, "MasterAudioStopAllOfSound");
            if (Field<FsmString>(_ringSound, "variationName").Value != "carphone_ring"
                || !ReferenceEquals(Field<FsmString>(_callerSound, "variationName"), _voice))
                throw new InvalidOperationException("Changed taxi caller audio.");
        }

        private void InstallGuest(Action<TaxiCallAction> send)
        {
            foreach (var go in _objects) RememberActive(go);
            RememberActive(_handle); RememberActive(_keypad); RememberActive(_ring.gameObject);
            RememberPose(_customer.transform); RememberPose(_walker.transform);
            RememberAnimation(_rootAnimation); RememberAnimation(_skeletonAnimation);
            foreach (var c in _colliders) { bool enabled = c.enabled; _restore.Add(() => { if (c != null) c.enabled = enabled; }); }
            foreach (var f in new[] { _job, _walker, _ring, _phone, _gui }) RememberVariables(f);
            var payments = Fsm(_phone.transform.parent.parent.parent.gameObject, "Payments");
            RememberVariables(payments);
            Pause(_job); Pause(_walker); Pause(_ring); Pause(payments);
            var suitcases = Fsm(_walker.gameObject, _c["luggageFsm"]);
            Pause(suitcases);
            foreach (var f in _customer.GetComponents<PlayMakerFSM>()) Pause(f);
            var tutorial = ObjectVariable(_job, "Tutorial");
            RememberActive(tutorial);
            foreach (var f in tutorial.GetComponentsInChildren<PlayMakerFSM>(true)) Pause(f);
            // Fare binding selectively enables shared cash/receipt input after these
            // original values are captured. Guest luggage rolls remain suppressed.
            foreach (string variable in new[] { "Pay", "Receiptrigger" })
            { var go = ObjectVariable(_walker, variable); RememberActive(go); go.SetActive(false); }
            for (int i = 0; i < 5; i++)
            {
                var go = _luggageBodies[i].gameObject; RememberActive(go); go.SetActive(false);
            }
            tutorial.SetActive(false);
            _customer.SetActive(false); _objects[4].SetActive(false); _gui.gameObject.SetActive(false);
            _ring.gameObject.SetActive(false); _keypad.SetActive(false);
            var globals = _phone.Fsm.GlobalTransitions; var events = _phone.Fsm.Events;
            _restore.Add(() => { _phone.Fsm.GlobalTransitions = globals; _phone.Fsm.Events = events; });
            ReplacePhone(_c["answerState"], () => send(TaxiCallAction.Answer));
            ReplacePhone(_c["closeState"], () => send(TaxiCallAction.HangUp));
        }

        private void ReplacePhone(string name, Action callback)
        {
            var state = State(_phone, name); var original = state.Actions;
            bool opening = name == _c["answerState"];
            if (!FsmHook.EnsureRemoteEntry(_phone, _c["waitState"])) throw new InvalidOperationException("Taxi phone idle entry missing.");
            var action = new FsmHookAction(() =>
            {
                if (_canDial)
                { _outgoingPhone = opening; return; }
                callback();
                FsmHook.FireRemoteEntry(_phone, _c["waitState"]);
            });
            action.Init(state);
            var actions = new List<FsmStateAction> { action };
            // Idle outgoing use keeps native hand/keypad/audio control. Answer
            // and Occupied belong to the authoritative incoming taxi call.
            foreach (var native in original) if (native.GetType().Name != "SetFsmBool") actions.Add(native);
            var installed = actions.ToArray(); state.Actions = installed;
            _restore.Add(() => { if (ReferenceEquals(state.Actions, installed)) state.Actions = original; });
        }

        internal void Present(TaxiServiceState state)
        {
            if (!_guest || _restored) return;
            foreach (var fsm in _paused) if (fsm != null && fsm.enabled) fsm.enabled = false;
            _canDial = TaxiServicePolicy.CanDial(state);
            if (_outgoingPhone && !_canDial)
                ReleaseOutgoingPhone();
            bool mine = state.CallOwner == _localPlayer;
            bool holding = mine || _outgoingPhone;
            _phoneUse.Value = holding; _phoneDistance.Value = holding ? 7 : 1.5f;
            _handle.SetActive(!holding); _keypad.SetActive(_outgoingPhone);
            if (_shown && state.Revision == _shownRevision) { RingAudio(state); return; }
            _customer.transform.position = Unity(state.CustomerPosition); _customer.transform.rotation = Unity(state.CustomerRotation);
            var parent = (state.Flags & TaxiServiceState.Boarded) != 0 ? _pivot.transform : _customer.transform;
            if (_walker.transform.parent != parent) _walker.transform.SetParent(parent, false);
            _walker.transform.localPosition = Unity(state.WalkerPosition); _walker.transform.localRotation = Unity(state.WalkerRotation);
            _pickupName.Value = state.Pickup; _destinationName.Value = state.Destination; _text.Value = state.IndicatorText;
            _subtitle.Value = state.Subtitle; _voice.Value = state.Voice;
            // Pause decisions before activation; SetActive must never start a second job.
            for (int i = 0; i < _objects.Length; i++) _objects[i].SetActive((state.Flags & (1u << i)) != 0);
            for (int i = 0; i < 3; i++) _colliders[i].enabled = (state.ColliderFlags & (1 << i)) != 0;
            PresentLuggage(state);
            PresentPayday(state);
            PresentClip(_rootAnimation, state.RootClip, state.RootTime, ref _shownRootClip);
            PresentClip(_skeletonAnimation, state.SkeletonClip, state.SkeletonTime, ref _shownSkeletonClip);
            if (!_shown || _shownCall != state.CallId || _shownPhase != state.CallPhase || _shownOwner != state.CallOwner)
            {
                if (_shownPhase == TaxiCallPhase.Speaking && _ownedSubtitle.Length != 0)
                {
                    _stopCaller.OnEnter(); ClearSubtitle();
                }
                if (mine && state.CallPhase == TaxiCallPhase.Speaking)
                {
                    _callerSound.OnEnter();
                    var text = FsmVariables.GlobalVariables.FindFsmString("GUIsubtitle");
                    if (text != null) { text.Value = state.Subtitle; _ownedSubtitle = state.Subtitle; }
                }
                if (state.CallPhase == TaxiCallPhase.Ringing) _ringAt = 0;
            }
            _shown = true; _shownCall = state.CallId; _shownPhase = state.CallPhase; _shownRevision = state.Revision;
            _shownOwner = state.CallOwner;
            RingAudio(state);
        }

        private void RingAudio(TaxiServiceState state)
        {
            if (state.CallPhase != TaxiCallPhase.Ringing || Time.unscaledTime < _ringAt) return;
            _ringAt = Time.unscaledTime + 3; _ringSound.OnEnter();
        }
        private void ReleaseOutgoingPhone()
        {
            if (!_outgoingPhone) return;
            _outgoingPhone = false;
            var stop = FsmVariables.GlobalVariables.FindFsmBool("PlayerStop");
            if (stop != null) stop.Value = false;
        }
        private void ClearSubtitle()
        {
            var text = FsmVariables.GlobalVariables.FindFsmString("GUIsubtitle");
            if (text != null && text.Value == _ownedSubtitle) text.Value = "";
            _ownedSubtitle = "";
        }
        private void PresentClip(Animation animation, string clip, float time, ref string previous)
        {
            if (_shown && clip == previous) return;
            if (clip.Length == 0) animation.Stop();
            else
            {
                var state = animation[clip];
                if (state == null) throw new InvalidOperationException("Unknown native taxi animation " + clip);
                animation.Play(clip); state.time = time; animation.Sample();
            }
            previous = clip;
        }
        private void Pause(PlayMakerFSM fsm)
        {
            if (_paused.Contains(fsm)) return;
            var pause = new FsmSuppressor();
            if (!pause.Suppress(fsm)) throw new InvalidOperationException("Cannot pause guest taxi decisions.");
            _paused.Add(fsm); _resume.Add(pause.Restore);
        }
        private void RememberActive(GameObject go)
        { bool active = go.activeSelf; _restore.Add(() => { if (go != null) go.SetActive(active); }); }
        private void RememberPose(Transform transform)
        {
            var parent = transform.parent; var position = transform.localPosition; var rotation = transform.localRotation;
            _restore.Add(() => { if (transform != null) { transform.SetParent(parent, false); transform.localPosition = position; transform.localRotation = rotation; } });
        }
        private void RememberAnimation(Animation animation)
        {
            Clip(animation, out var clip, out var time);
            _restore.Add(() => { if (animation == null) return; animation.Stop(); if (clip.Length != 0) { animation.Play(clip); animation[clip].time = time; } });
        }
        private void RememberVariables(PlayMakerFSM fsm)
        {
            foreach (var v in fsm.FsmVariables.FloatVariables) { float value = v.Value; _restore.Add(() => v.Value = value); }
            foreach (var v in fsm.FsmVariables.IntVariables) { int value = v.Value; _restore.Add(() => v.Value = value); }
            foreach (var v in fsm.FsmVariables.BoolVariables) { bool value = v.Value; _restore.Add(() => v.Value = value); }
            foreach (var v in fsm.FsmVariables.StringVariables) { string value = v.Value; _restore.Add(() => v.Value = value); }
        }
    }
}
