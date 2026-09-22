using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private AdvertJobState CaptureAdvertJob()
        {
            var c=SyncCatalog.Adverts!; var data=_adData!.FsmVariables;
            var s=new AdvertJobState { Revision=_adLast?.Revision ?? 1, Delivered=data.FindFsmInt(c["delivered"]).Value,
                Sheets=(byte)_adUse!.FsmVariables.FindFsmInt(c["sheets"]).Value, Stage=(byte)data.FindFsmInt(c["stage"]).Value,
                NextDay=(byte)data.FindFsmInt(c["day"]).Value, Salary=data.FindFsmFloat(c["salary"]).Value, Scale=_adScale!.localScale.z,
                Flags=(byte)((_adSpawn!.activeSelf?1:0)|(_adPile!.gameObject.activeInHierarchy?2:0)|(_adPile.transform.parent==_adSpawn.transform?4:0)),
                Position=_adPile.position.ToNet(), Rotation=_adPile.rotation.ToNet() };
            for (int i=0;i<28;i++) if ((bool)_adBoxes![i]) s.CompletedMask|=1u<<i;
            if (_adLast!=null && !AdvertPolicy.Same(s,_adLast)) { if (++s.Revision==0) ++s.Revision; }
            if (!AdvertPolicy.Valid(s)) throw new InvalidOperationException("Invalid native advert state.");
            _adLast=s;return s;
        }
        internal AdvertJobState? BuildAdvertJobState() => !_adFailed && _adData!=null && SessionManager.Instance?.IsHost==true ? CaptureAdvertJob() : null;
        internal AdvertSheetState? BuildAdvertSheetState(uint id)
        {
            if (_adFailed || SessionManager.Instance?.IsHost!=true || !_adSheets.TryGetValue(id,out var b) || b.Body==null || _spawnLifecycle.IsRetired(id)) return null;
            return new AdvertSheetState { ItemId=id,Position=b.Body.position.ToNet(),Rotation=b.Body.rotation.ToNet() };
        }
        internal IEnumerable<AdvertSheetState> BuildAdvertSheetStates()
        { foreach (uint id in _adSheets.Keys) { var s=BuildAdvertSheetState(id);if(s!=null)yield return s; } }
        private void BroadcastAdvertJob(SessionManager session)
        { session.SendWorldMessage(CaptureAdvertJob(),Channel.ReliableOrdered);_adNextSend=Time.unscaledTime+2; }
        internal void OnAdvertJobState(AdvertJobState state)
        {
            if (_adFailed || SessionManager.Instance?.IsHost!=false || !AdvertPolicy.Valid(state)) return;
            var old=_adPending??_adReceived;
            if(old!=null && !AdvertPolicy.Newer(state.Revision,old.Revision) && (state.Revision!=old.Revision || !AdvertPolicy.Same(state,old)))return;
            _adPending=state;
        }
        internal void OnAdvertSheetState(AdvertSheetState state)
        {
            if(_adFailed || SessionManager.Instance?.IsHost!=false || state.ItemId==0 || !AdvertPolicy.Pose(state.Position,state.Rotation) || _spawnLifecycle.IsRetired(state.ItemId))return;
            if(!_pendingAdSheets.ContainsKey(state.ItemId) && _pendingAdSheets.Count>=1024)return;
            _snapshotSeenIds.Add(state.ItemId);_pendingAdSheets[state.ItemId]=state;
        }
        private void ApplyAdvertJob(AdvertJobState s)
        {
            var c=SyncCatalog.Adverts!;var vars=_adData!.FsmVariables;var previous=_adReceived;
            vars.FindFsmInt(c["delivered"]).Value=s.Delivered;vars.FindFsmInt(c["sheets"]).Value=s.Sheets;
            vars.FindFsmInt(c["stage"]).Value=s.Stage;vars.FindFsmInt(c["day"]).Value=s.NextDay;vars.FindFsmFloat(c["salary"]).Value=s.Salary;
            _adUse!.FsmVariables.FindFsmInt(c["sheets"]).Value=s.Sheets;
            for(int i=0;i<28;i++)_adBoxes![i]=(s.CompletedMask&(1u<<i))!=0;
            _adSpawn!.SetActive((s.Flags&1)!=0);
            if (!IsHeldByLocalPlayer(_adPile!))
            {
                bool atSpawn=(s.Flags&4)!=0;
                if (atSpawn) _adPile!.transform.parent=_adSpawn.transform;
                else if (_adPile!.transform.parent==_adSpawn.transform) _adPile.transform.parent=null;
                if(previous==null || (previous.Flags&6)!=(s.Flags&6))
                    ApplySnapshotPose(_items[_adPileId],s.Position.ToUnity(),s.Rotation.ToUnity());
            }
            _adPile!.gameObject.SetActive((s.Flags&2)!=0);
            _adScale!.localScale=new Vector3(1,1,s.Scale);
            // A newly activated pile starts its native load-from-database state;
            // the authoritative Sheets value above is already in that database.
            foreach(var box in _adMailboxes.Values)
            {
                bool placed=(s.CompletedMask&(1u<<box.Index))!=0;
                box.Fsm.FsmVariables.FindFsmBool(c["placed"]).Value=placed;
                if(!box.Fsm.Fsm.Started || !box.Fsm.enabled || !box.Fsm.gameObject.activeInHierarchy)continue;
                if(previous==null || !placed && (previous.CompletedMask&(1u<<box.Index))!=0)
                    FsmHook.FireRemoteEntry(box.Fsm,c[placed?"mailboxIdle":"mailboxReset"]);
                else if(placed && (previous.CompletedMask&(1u<<box.Index))==0)
                { _adPresenting=true;try{FsmHook.FireRemoteEntry(box.Fsm,c["mailboxOpen"]);}finally{_adPresenting=false;} }
            }
            _adReceived=s;
        }
        private void MaterializeAdvertSheet(AdvertSheetState s)
        {
            if(_adSheets.TryGetValue(s.ItemId,out var bound) && bound.Body!=null)return;
            if(_items.TryGetValue(s.ItemId,out var old))
            { if(old.Body!=null)throw new InvalidOperationException("Advert sheet ID collision.");RemoveTrackedItem(s.ItemId,old.Body); }
            var clone=(GameObject)UnityEngine.Object.Instantiate(_adPrefab!,s.Position.ToUnity(),s.Rotation.ToUnity());
            var body=clone.GetComponent<Rigidbody>();
            try
            {
                clone.name=SyncCatalog.Adverts!["sheetName"];body.isKinematic=false;clone.SetActive(true);
                _adSheets[s.ItemId]=new AdvertSheet{Body=body,Replica=true};BindAdvertBody(s.ItemId,body);
                ApplySnapshotPose(_items[s.ItemId],s.Position.ToUnity(),s.Rotation.ToUnity());_pendingItemPoses.Remove(s.ItemId);
            }
            catch { _adSheets.Remove(s.ItemId);RemoveTrackedItem(s.ItemId,body);UnityEngine.Object.Destroy(clone);throw; }
        }
        private void ProcessAdverts(SessionManager session)
        {
            if(_adFailed)return;
            try
            {
                RefreshAdverts();if(_adData==null || Time.unscaledTime<_adTick)return;_adTick=Time.unscaledTime+.1f;
                if(session.IsHost)
                {
                    foreach(var box in _adMailboxes.Values)
                        if(box.Reserved!=0 && Time.unscaledTime>box.Deadline)throw new InvalidOperationException("Native advert delivery did not finish.");
                    var previous=_adLast;var state=CaptureAdvertJob();
                    if(previous==null || state.Revision!=previous.Revision || Time.unscaledTime>=_adNextSend)BroadcastAdvertJob(session);
                    foreach(var pair in _adSheets)
                    {
                        var b=pair.Value;
                        if(b.Body==null)
                        { if(!_spawnLifecycle.IsRetired(pair.Key)){AnnounceItemDespawn(pair.Key,"advert removed");RecordItemRetirement(pair.Key);}continue; }
                        if(Time.unscaledTime<b.NextSend)continue;
                        var sheet=BuildAdvertSheetState(pair.Key);if(sheet!=null)session.SendWorldMessage(sheet,Channel.ReliableOrdered);b.NextSend=Time.unscaledTime+5;
                    }
                }
                else
                {
                    if(_adPending!=null){ApplyAdvertJob(_adPending);_adPending=null;}
                    foreach(uint id in new List<uint>(_pendingAdSheets.Keys))
                    { if(!_spawnLifecycle.IsRetired(id))MaterializeAdvertSheet(_pendingAdSheets[id]);_pendingAdSheets.Remove(id); }
                }
            }
            catch(Exception e){FailAdverts(e);}
        }
    }
}
