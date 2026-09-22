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
    internal sealed partial class ItemWorldSync
    {
        private void ValidateAdverts(AdvertsData c)
        {
            var data = _adData!; var use = _adUse!;
            foreach (string key in new[] { "sheets", "delivered", "stage", "day" })
                if (data.FsmVariables.FindFsmInt(c[key]) == null) throw new InvalidOperationException("Missing advert job variable " + c[key]);
            if (data.FsmVariables.FindFsmFloat(c["salary"]) == null || use.FsmVariables.FindFsmInt(c["sheets"]) == null)
                throw new InvalidOperationException("Missing advert quantity/salary.");
            PackageStateActions(data, "Save game", "GetFsmInt", "SaveTransform", "ArrayListEasySave", "SaveInt", "SaveInt", "SaveInt");
            PackageStateActions(data, "Load game", "ArrayListEasyLoad", "LoadInt", "LoadInt", "LoadInt");
            PackageStateActions(data, "New job", "ArrayListResetValues", "SetIntValue", "SetParent", "ActivateGameObject", "SetIntValue", "SendEventByName");
            var open = PackageStateActions(use, c["open"], "IntAdd", "CreateObject");
            if (PackageField<FsmInt>(open.Actions[0], "intVariable")?.Name != c["sheets"]
                || PackageField<FsmInt>(open.Actions[0], "add")?.Value != -1
                || PackageField<FsmGameObject>(open.Actions[1], "gameObject")?.Value != _adPrefab
                || PackageField<FsmGameObject>(open.Actions[1], "storeObject")?.IsNone != true)
                throw new InvalidOperationException("Advert extraction binding changed.");
            RequireFitTransition(use, c["open"], "FINISHED", "State 1");
            foreach (string state in new[] { c["open"], c["ready"], c["empty"] })
                if (!FsmHook.EnsureRemoteEntry(use, state)) throw new InvalidOperationException("Advert entry missing.");
        }
        private void AdvertHook(FsmState state, Action callback, bool first)
        {
            var old = state.Actions;
            var hook = new FsmHookAction(() => { try { callback(); } catch (Exception e) { FailAdverts(e); } }); hook.Init(state);
            var list = new List<FsmStateAction>(old); if (first) list.Insert(0,hook); else list.Add(hook); state.Actions = list.ToArray();
            _adRestore.Add(() => state.Actions = old);
        }
        private void InstallAdvertPile(AdvertsData c)
        {
            var open = FsmHook.FindState(_adUse!, c["open"])!; var create = open.Actions[1];
            var field = create.GetType().GetField("storeObject"); var old = field.GetValue(create);
            var output = new FsmGameObject { Name = "wintermp-advert-output", UseVariable = true }; field.SetValue(create, output);
            _adRestore.Add(() => field.SetValue(create, old));
            AdvertHook(open, () =>
            {
                var session = SessionManager.Instance;
                if (!_adFailed && session?.IsHost == true && (_adAllowTake || (AdvertPolicy.CanTake(CaptureAdvertJob()) && AdvertActor(session.LocalPlayerId, _adPileId, false)))) return;
                if (!_adFailed && session?.IsHost == false) SendAdvertRequest(_adPileId, 255);
                FsmHook.FireRemoteEntry(_adUse!, c["ready"]);
            }, true);
            AdvertHook(open, () =>
            {
                if (SessionManager.Instance?.IsHost != true || _adFailed) return;
                var body = output.Value != null ? output.Value.GetComponent<Rigidbody>() : null;
                if (body == null || body.name != c["sheetName"]) throw new InvalidOperationException("Advert native output missing.");
                RegisterAdvertSheet(body); _adNextSend = 0;
            }, false);
        }
        private void InstallAdvertBox(AdvertBox box, AdvertsData c, bool host)
        {
            var f = box.Fsm;
            var open = PackageStateActions(f, c["mailboxOpen"], "SetBoolValue", "DestroyObject", "MasterAudioPlaySound", "PlayAnimation");
            var close = PackageStateActions(f, c["mailboxClose"], "SetBoolValue", "AddToFsmInt", "ArrayListSet", "MasterAudioPlaySound", "PlayAnimation");
            if (PackageField<FsmGameObject>(open.Actions[1], "gameObject")?.Name != "AdvertObject"
                || PackageField<FsmBool>(close.Actions[0], "boolVariable")?.Name != c["placed"]
                || PackageField<FsmBool>(close.Actions[0], "boolValue")?.Value != true
                || !FitTargetVariable(PackageField<FsmOwnerDefault>(close.Actions[1], "gameObject"), c["db"])
                || PackageField<FsmString>(close.Actions[1], "variableName")?.Value != c["delivered"]
                || PackageField<FsmInt>(close.Actions[1], "add")?.Value != 1
                || PackageField<FsmInt>(close.Actions[2], "atIndex")?.Name != c["index"])
                throw new InvalidOperationException("Advert mailbox accounting changed.");
            RequireFitTransition(f, c["mailboxOpen"], "FINISHED", c["mailboxClose"]);
            foreach (string key in new[] { "mailboxOpen", "mailboxIdle", "mailboxReset", "mailboxRetry" })
                if (!FsmHook.EnsureRemoteEntry(f, c[key])) throw new InvalidOperationException("Advert mailbox entry missing.");
            if (!host)
            {
                RememberAdvertFsm(f);
                var originalOpen = open.Actions; var originalClose = close.Actions;
                // Observers play only the native hatch presentation. They never run
                // the local DestroyObject, Delivered increment or saved-list write.
                open.Actions = new[] { originalOpen[0], originalOpen[2], originalOpen[3] };
                close.Actions = new[] { originalClose[3], originalClose[4] };
                _adRestore.Add(() => { open.Actions = originalOpen; close.Actions = originalClose; });
            }
            AdvertHook(open, () =>
            {
                var session = SessionManager.Instance;
                if (!_adFailed && session?.IsHost == true)
                {
                    if (_adAllowBox == box.Index && box.Reserved != 0) return;
                    var obj = f.FsmVariables.FindFsmGameObject("AdvertObject")?.Value;
                    if (obj != null && TryAdvertId(obj.GetComponent<Rigidbody>(), out uint id) && ReserveAdvert(box, id, session.LocalPlayerId))
                    {
                        ReleaseHeldBag(_adSheets[id].Body);
                        return;
                    }
                }
                else if (!_adFailed && session?.IsHost == false)
                {
                    if (_adPresenting) return;
                    var obj = f.FsmVariables.FindFsmGameObject("AdvertObject")?.Value;
                    if (obj != null && TryAdvertId(obj.GetComponent<Rigidbody>(), out uint id)) SendAdvertRequest(id, box.Index);
                }
                FsmHook.FireRemoteEntry(f, c["mailboxRetry"]);
            },true);
            if (host) AdvertHook(close, () =>
            {
                if (box.Reserved == 0) return;
                if (!(_adBoxes![box.Index] is bool placed) || !placed) throw new InvalidOperationException("Native advert delivery did not mark its saved flag.");
                RecordItemRetirement(box.Reserved); box.Reserved = 0; _adNextSend = 0;
                SyncEventLog.Record("advert-delivered", box.Index.ToString());
            },false);
        }
        private bool TryAdvertId(Rigidbody? body, out uint id)
        {
            foreach (var pair in _adSheets) if (body != null && pair.Value.Body == body) { id = pair.Key; return true; }
            id = 0; return false;
        }
        private bool AdvertActor(byte actor, uint id, bool held)
        {
            var session = SessionManager.Instance!;
            if (!_items.TryGetValue(id, out var item) || item.Body == null || !item.Body.gameObject.activeInHierarchy
                || !GamblingSync.TryPlayerPosition(session,actor,out var pos) || (pos-item.Body.position).sqrMagnitude > 9) return false;
            if (actor == session.LocalPlayerId) return held ? IsHeldByLocalPlayer(item.Body) : item.RemoteOwner == 255 || item.RemoteOwner == actor;
            return !IsHeldByLocalPlayer(item.Body) && (held ? item.RemoteOwner == actor : item.RemoteOwner == actor || item.RemoteOwner == 255);
        }
        private bool ReserveAdvert(AdvertBox box, uint id, byte actor)
        {
            if (box.Reserved != 0 || _spawnLifecycle.IsRetired(id) || !_adSheets.TryGetValue(id,out var sheet) || sheet.Body == null
                || !box.Fsm.enabled || !box.Fsm.gameObject.activeInHierarchy || !AdvertActor(actor,id,true)
                || !AdvertPolicy.CanDeliver(CaptureAdvertJob(),box.Index) || (sheet.Body.position-box.Fsm.transform.position).sqrMagnitude > 4) return false;
            foreach (var other in _adMailboxes.Values) if (other.Reserved == id) return false;
            box.Reserved = id; box.Deadline = Time.unscaledTime + 10; return true;
        }
        private void SendAdvertRequest(uint id, byte box)
        {
            var session = SessionManager.Instance;
            if (_adReceived == null || session == null) return;
            if (++_adSequence == 0) ++_adSequence;
            session.SendWorldMessage(new AdvertIntent { ItemId=id, Box=box, PlayerId=session.LocalPlayerId,
                Sequence=_adSequence, ExpectedRevision=_adReceived.Revision },Channel.ReliableOrdered);
        }
        internal void OnAdvertIntent(AdvertIntent request, byte actor)
        {
            var session = SessionManager.Instance;
            if (session?.IsHost != true || _adData == null || _adFailed || !AdvertPolicy.Valid(request) || request.PlayerId != actor) return;
            try
            {
                if (_adSequences.TryGetValue(actor,out uint previous) && !AdvertPolicy.Newer(request.Sequence,previous)) return;
                _adSequences[actor]=request.Sequence;
                var state=CaptureAdvertJob();
                if (request.ExpectedRevision != state.Revision) { BroadcastAdvertJob(session); return; }
                if (request.Box == 255)
                {
                    if (request.ItemId != _adPileId || !AdvertPolicy.CanTake(state) || !AdvertActor(actor,_adPileId,false)
                        || _adUse!.ActiveStateName != SyncCatalog.Adverts!["ready"] && _adUse.ActiveStateName != "Wait button") return;
                    _adAllowTake=true;
                    try { FsmHook.FireRemoteEntry(_adUse,SyncCatalog.Adverts["open"]); }
                    finally { _adAllowTake=false; }
                }
                else if (_adMailboxes.TryGetValue(request.Box,out var box) && ReserveAdvert(box,request.ItemId,actor))
                {
                    box.Fsm.FsmVariables.FindFsmGameObject("AdvertObject").Value=_adSheets[request.ItemId].Body.gameObject;
                    _adAllowBox=box.Index;
                    try { FsmHook.FireRemoteEntry(box.Fsm,SyncCatalog.Adverts!["mailboxOpen"]); }
                    finally { _adAllowBox=255; }
                }
                BroadcastAdvertJob(session);
            }
            catch (Exception e) { FailAdverts(e); }
        }
        internal void ForgetAdvertPlayer(byte actor) => _adSequences.Remove(actor);
    }
}
