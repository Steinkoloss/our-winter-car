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
    internal sealed class AdvertPhoneSync
    {
        private readonly Dictionary<byte, AdvertPhoneBinding> _phones = new Dictionary<byte, AdvertPhoneBinding>();
        private readonly AdvertCallLedger _ledger = new AdvertCallLedger();
        private PlayMakerFSM? _listing, _job;
        private bool _ready, _failed, _speaking, _finishing, _beginPending, _completePending, _completionRequested;
        private float _probe, _heartbeat, _localDeadline;
        private uint _sequence, _localCall;
        private AdvertPhoneBinding? _local;

        internal void Update(SessionManager session)
        {
            if (_failed) return;
            try
            {
                if (!_ready && Time.realtimeSinceStartup >= _probe) { _probe = Time.realtimeSinceStartup + 2; Locate(); }
                if (!_ready) return;
                if (_localCall != 0 && _local != null)
                {
                    string nativeState = _local.Calling.ActiveStateName;
                    bool waiting = !_speaking && (nativeState == AdvertPhoneBinding.Waiting || _beginPending && nativeState == "Check type");
                    bool talking = _speaking && _local.Calling.ActiveStateName == "Call";
                    bool ending = _finishing && (nativeState == "Beep beep" || _completePending && nativeState == "Hangup 2");
                    if (!_local.Running || (!waiting && !talking && !ending) || Time.realtimeSinceStartup > _localDeadline)
                        EndLocal(AdvertPhoneIntent.Cancel);
                    else if (_beginPending)
                    { if (nativeState == AdvertPhoneBinding.Waiting) { _beginPending = false; Send(AdvertPhoneIntent.Begin); } }
                    else if (_completePending)
                    { if (nativeState == "Beep beep") { _completePending = false; Send(AdvertPhoneIntent.Complete); } }
                    else if (Time.realtimeSinceStartup >= _heartbeat)
                    { _heartbeat = Time.realtimeSinceStartup + 1; Send(AdvertPhoneIntent.KeepAlive); }
                }
                if (session.IsHost && _ledger.Active)
                {
                    var phone = _phones[_ledger.Phone];
                    if (!_ledger.Live(Time.realtimeSinceStartup) || !Available(phone, _ledger.Player, false)) RejectActive();
                    else
                    {
                        if (_completionRequested) CompleteActive();
                        if (_ledger.Active) phone.Charge(false, Time.deltaTime);
                    }
                }
            }
            catch (Exception e) { Fail(e); }
        }
        internal void BeginLocal(AdvertPhoneBinding phone)
        {
            try
            {
                if (_failed || !_ready) { phone.Stop(); return; }
                if (_localCall != 0) EndLocal(AdvertPhoneIntent.Cancel);
                _local = phone; if (++_sequence == 0) ++_sequence;
                _localCall = _sequence; _speaking = _finishing = _completePending = false;
                _localDeadline = Time.realtimeSinceStartup + AdvertCallLedger.Lease;
                _heartbeat = Time.realtimeSinceStartup + 1;
                // PlayMaker queues the transition out of this hook. Even a
                // local host result must wait until the approval state is active.
                _beginPending = true;
            }
            catch (Exception e) { Fail(e); }
        }
        internal void CompleteLocal(AdvertPhoneBinding phone)
        {
            if (_local != phone || _localCall == 0 || !_speaking || _finishing) return;
            _finishing = true; _localDeadline = Time.realtimeSinceStartup + AdvertCallLedger.Lease;
            _completePending = true;
        }
        private void Send(byte action)
        {
            var session = SessionManager.Instance!;
            var request = new AdvertPhoneIntent { PlayerId = session.LocalPlayerId, Phone = _local!.Id, Call = _localCall, Action = action };
            if (session.IsHost) Intent(request, session.LocalPlayerId);
            else session.SendWorldMessage(request, Channel.ReliableOrdered);
        }
        private void EndLocal(byte action)
        {
            SyncEventLog.Record("advert-call-local-end", _local?.Calling.ActiveStateName ?? "none");
            Send(action); _local?.Stop(); _localCall = 0; _local = null; _speaking = _finishing = _beginPending = _completePending = false;
        }
        internal void Result(AdvertPhoneResult result)
        {
            var session = SessionManager.Instance;
            if (session == null || result.PlayerId != session.LocalPlayerId || result.Call != _localCall || _local == null || result.Phone != _local.Id) return;
            try
            {
                if (result.Status == AdvertPhoneResult.Accepted)
                {
                    if (_speaking || !_local.Running || _local.Calling.ActiveStateName != AdvertPhoneBinding.Waiting) return;
                    _speaking = true; _localDeadline = Time.realtimeSinceStartup + AdvertCallLedger.Duration + AdvertCallLedger.Lease;
                    _local.Present();
                }
                else { _local.Stop(); _local = null; _localCall = 0; _speaking = _finishing = _beginPending = _completePending = false; }
            }
            catch (Exception e) { Fail(e); }
        }
        internal void Intent(AdvertPhoneIntent r, byte actor)
        {
            if (SessionManager.Instance?.IsHost != true || r.PlayerId != actor || actor == 255 || r.Call == 0 || r.Phone > 2 || r.Action > 3) return;
            try
            {
                if (_failed || !_ready || !_phones.TryGetValue(r.Phone, out var phone)) { Reply(r, AdvertPhoneResult.Rejected); return; }
                if (r.Action == AdvertPhoneIntent.Begin)
                {
                    if (_ledger.Matches(actor, r.Phone, r.Call)) return;
                    bool allowed = Available(phone, actor, true);
                    if (!_ledger.Begin(actor, r.Phone, r.Call, Time.realtimeSinceStartup, allowed))
                    { SyncEventLog.Record("advert-call-reject", "player=" + actor + " eligible=" + allowed + " busy=" + _ledger.Active); Reply(r, AdvertPhoneResult.Rejected); return; }
                    phone.Charge(true, 0);
                    _completionRequested = false;
                    Reply(r, AdvertPhoneResult.Accepted);
                    SyncEventLog.Record("advert-call-start", "player=" + actor + " phone=" + r.Phone);
                }
                else if (_ledger.Matches(actor, r.Phone, r.Call))
                {
                    if (r.Action == AdvertPhoneIntent.Cancel) { RejectActive(); return; }
                    if (!Available(phone, actor, false) || !_ledger.Live(Time.realtimeSinceStartup)) { RejectActive(); return; }
                    if (r.Action == AdvertPhoneIntent.KeepAlive) _ledger.Keep(actor, r.Phone, r.Call, Time.realtimeSinceStartup);
                    else if (_ledger.Finishing(Time.realtimeSinceStartup))
                    {
                        // Native Wait and the peer's frame clock can finish a
                        // fraction apart. Hold that result until the host's full
                        // duration; never shorten the authoritative conversation.
                        _completionRequested = true; CompleteActive();
                    }
                    else RejectActive();
                }
            }
            catch (Exception e) { Fail(e); }
        }
        private bool Available(AdvertPhoneBinding phone, byte actor, bool beginning)
        {
            var session = SessionManager.Instance!;
            if (_listing == null || _job == null || !_listing.enabled || !_job.enabled || !_listing.Fsm.Started || !_job.Fsm.Started
                || _listing.FsmVariables.FindFsmInt("Stage").Value != 1 || _job.FsmVariables.FindFsmInt("JobStage").Value != 0
                || _job.ActiveStateName != "Wait call" || !phone.Connected || phone.Ringing.activeSelf
                || !GamblingSync.TryPlayerPosition(session, actor, out var pos) || !((pos - phone.Position).sqrMagnitude <= 16)) return false;
            // Native phone input belongs to the host's camera. A remote caller
            // reserves the idle line without activating or moving that camera.
            if (actor != session.LocalPlayerId && !phone.HostIdle) return false;
            return !beginning || _phones.ContainsKey(phone.Id);
        }
        private void Reply(AdvertPhoneIntent r, byte status)
        {
            var result = new AdvertPhoneResult { Call = r.Call, Phone = r.Phone, PlayerId = r.PlayerId, Status = status };
            if (r.PlayerId == SessionManager.Instance!.LocalPlayerId) Result(result);
            else SessionManager.Instance.SendWorldMessage(result, Channel.ReliableOrdered);
        }
        private void CompleteActive()
        {
            var r = new AdvertPhoneIntent { Call = _ledger.Call, Phone = _ledger.Phone, PlayerId = _ledger.Player };
            if (!_ledger.Complete(r.PlayerId, r.Phone, r.Call, Time.realtimeSinceStartup)) return;
            _completionRequested = false;
            _listing!.SendEvent("CALLED");
            if (_listing.FsmVariables.FindFsmInt("Stage").Value != 3 || _job!.FsmVariables.FindFsmInt("JobStage").Value != 1)
                throw new InvalidOperationException("Native advert enrolment did not commit.");
            Reply(r, AdvertPhoneResult.Completed);
            SyncEventLog.Record("advert-call-complete", "player=" + r.PlayerId);
        }
        private void RejectActive()
        {
            if (!_ledger.Active) return;
            var r = new AdvertPhoneIntent { Call = _ledger.Call, Phone = _ledger.Phone, PlayerId = _ledger.Player };
            _ledger.Cancel(); _completionRequested = false; Reply(r, AdvertPhoneResult.Rejected);
            SyncEventLog.Record("advert-call-cancel", "player=" + r.PlayerId);
        }
        internal void Forget(byte player) { _ledger.Forget(player); }
        internal void Clear()
        {
            _local?.Stop();
            foreach (var phone in _phones.Values) phone.Restore();
            _phones.Clear(); _ledger.Clear(); _listing = _job = null; _local = null;
            _ready = _failed = _speaking = _finishing = _beginPending = _completePending = _completionRequested = false; _probe = _heartbeat = _localDeadline = 0; _sequence = _localCall = 0;
        }
        private void Fail(Exception e)
        {
            _failed = true; RejectActive(); _local?.Stop(); _localCall = 0;
            WinterMPPlugin.Log.LogWarning("Advert telephone enrolment disabled: " + e.Message);
        }
        private void Locate()
        {
            var c = SyncCatalog.AdvertPhone;
            if (c == null) throw new InvalidOperationException("Advert phone catalog unavailable.");
            var fsms = new List<PlayMakerFSM>();
            foreach (var obj in ScenePath.ScanFsms()) if (obj is PlayMakerFSM f) fsms.Add(f);
            _job = Find(fsms, c["job"], "Data");
            if (_job == null || !_job.Fsm.Started) return;
            foreach (var f in fsms)
                if (f.FsmName == "Data" && f.transform.parent != null && ScenePath.Of(f.transform.parent) == c["listings"]
                    && (f.gameObject.name == c["number"] || f.FsmVariables.FindFsmString("Number")?.Value == c["number"])) _listing = f;
            if (_listing != null) ValidateListing(_listing, _job, c);
            for (byte i = 0; i < 3; i++)
            {
                if (_phones.ContainsKey(i)) continue;
                string root = c["phone" + i];
                var calling = Find(fsms, root + "/" + c["keypad" + i], "Calling");
                var handle = Find(fsms, root + "/" + c["handle" + i], "Use", calling?.gameObject);
                var ring = Find(fsms, root + "/" + c["ring" + i], "Ring");
                var cord = i == 2 ? null : Find(fsms, root + "/Cord", "Use");
                var bill = i == 2 ? null : Find(fsms, c["bill" + i], "Data");
                if (calling == null || handle == null || ring == null || i != 2 && (cord == null || bill == null)) return;
                _phones.Add(i, new AdvertPhoneBinding(i, calling, handle, cord, bill, ring.gameObject, this));
            }
            _ready = true;
            SyncEventLog.Record("advert-phone-bound", "three phones; native 72-second call");
        }
        private static PlayMakerFSM? Find(List<PlayMakerFSM> fsms, string path, string name, GameObject? keypad = null)
        {
            PlayMakerFSM? result = null;
            foreach (var f in fsms) if (f.FsmName == name && ScenePath.Of(f.transform) == path)
            {
                // The old house has a legacy incoming-only Use FSM beside the
                // outgoing handle. Its name/path alone cannot identify it.
                if (keypad != null && f.FsmVariables.FindFsmGameObject("Keypad")?.Value != keypad) continue;
                if (result != null) throw new InvalidOperationException("Ambiguous phone: " + path); result = f;
            }
            return result;
        }
        private static void ValidateListing(PlayMakerFSM f, PlayMakerFSM job, AdvertPhoneData c)
        {
            var s = FsmHook.FindState(f, "State 1") ?? throw new InvalidOperationException("Advert phone result missing.");
            AdvertPhoneBinding.Shape(s, "SetName", "SetIntValue", "SendEventByName");
            var target = AdvertPhoneBinding.Field<FsmEventTarget>(s.Actions[2], "eventTarget");
            if (f.FsmVariables.FindFsmFloat("CallerCallLenght")?.Value != AdvertCallLedger.Duration
                || f.FsmVariables.FindFsmString("CallerAudioVariation")?.Value != c["audio"]
                || f.FsmVariables.FindFsmString("CallerSubtitle")?.Value != c["subtitle"]
                || AdvertPhoneBinding.Field<FsmString>(s.Actions[0], "name").Value != "numberdisabled"
                || AdvertPhoneBinding.Field<FsmInt>(s.Actions[1], "intVariable").Name != "Stage"
                || AdvertPhoneBinding.Field<FsmInt>(s.Actions[1], "intValue").Value != 3
                || (int)target.target != 2 || f.Fsm.GetOwnerDefaultTarget(target.gameObject) != job.gameObject || target.fsmName.Value != "Data"
                || AdvertPhoneBinding.Field<FsmString>(s.Actions[2], "sendEvent").Value != "JOB")
                throw new InvalidOperationException("Advert native enrolment changed.");
            AdvertPhoneBinding.RequireTransition(FsmHook.FindState(job, "Wait call")!, "JOB", "State 3");
        }
    }
}
