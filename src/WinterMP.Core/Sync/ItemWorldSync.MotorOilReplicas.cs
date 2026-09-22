using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        internal void OnMotorOilState(MotorOilBottleState state)
        {
            if(_motorOilFailed || SyncCatalog.MotorOil==null || SessionManager.Instance?.IsHost!=false || _spawnLifecycle.IsRetired(state.ItemId))return;
            _receivedMotorOil.TryGetValue(state.ItemId,out var old);
            if(!MotorOilPolicy.Accept(old,state) || !_pendingMotorOil.ContainsKey(state.ItemId) && _pendingMotorOil.Count>=1024)return;
            _receivedMotorOil[state.ItemId]=state;_pendingMotorOil[state.ItemId]=state;_snapshotSeenIds.Add(state.ItemId);
        }
        private void ApplyPendingMotorOil()
        {
            foreach(uint id in new List<uint>(_pendingMotorOil.Keys))
            {
                var state=_pendingMotorOil[id];
                if(!_spawnLifecycle.IsRetired(id))
                {
                    if(!_motorOil.TryGetValue(id,out var bottle) || bottle.Body==null)bottle=CreateMotorOilReplica(state);
                    ApplyMotorOil(bottle,state);
                }
                _pendingMotorOil.Remove(id);
            }
        }
        private MotorOilBottle CreateMotorOilReplica(MotorOilBottleState s)
        {
            var c=SyncCatalog.MotorOil!;var template=_motorOilPrefab!;
            var fsms=template.GetComponentsInChildren<PlayMakerFSM>(true);var enabled=new bool[fsms.Length];
            GameObject clone;
            try
            {
                // Use immediately loads by name. Prevent that startup from
                // reading this guest's saved bottle into a host replica.
                for(int i=0;i<fsms.Length;i++){enabled[i]=fsms[i].enabled;fsms[i].enabled=false;}
                clone=(GameObject)UnityEngine.Object.Instantiate(template,s.Position.ToUnity(),s.Rotation.ToUnity());
            }
            finally {for(int i=0;i<fsms.Length;i++)fsms[i].enabled=enabled[i];}
            try
            {
                clone.name=c["itemName"];PlayMakerFSM? use=null;
                foreach(var f in clone.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    f.enabled=false;if(!f.Fsm.Initialized)f.Fsm.Init(f);
                    if(f.gameObject==clone && f.FsmName==c["use"])use=f;
                }
                if(use==null)throw new InvalidOperationException("Oil replica Use missing.");
                use.FsmVariables.FindFsmString(c["id"]).Value="wintermp-motor-oil-"+s.ItemId.ToString("X8");
                use.FsmVariables.FindFsmGameObject("Owner").Value=clone;
                var body=clone.GetComponent<Rigidbody>();body.isKinematic=false;
                var bottle=BindMotorOil(body,use,s.NativeId,true);
                foreach(var f in clone.GetComponentsInChildren<PlayMakerFSM>(true))f.Fsm.GlobalTransitions=new FsmTransition[0];
                if(_items.TryGetValue(s.ItemId,out var old))
                {
                    if(old.Body!=null && !IsMotorOilBody(old.Body))throw new InvalidOperationException("Motor-oil ID collision.");
                    if(old.Body!=null)HideMotorOil(old.Body);else RemoveTrackedItem(s.ItemId,old.Body);
                }
                _motorOil[s.ItemId]=bottle;
                BindMotorOilBody(s.ItemId,body);ApplySnapshotPose(_items[s.ItemId],s.Position.ToUnity(),s.Rotation.ToUnity());
                clone.SetActive(true);return bottle;
            }
            catch {UnityEngine.Object.Destroy(clone);throw;}
        }
        private void ApplyMotorOil(MotorOilBottle b,MotorOilBottleState s)
        {
            var c=SyncCatalog.MotorOil!;
            if(!b.Replica || b.NativeId!=s.NativeId)throw new InvalidOperationException("Oil state overlaps a native save object.");
            bool gradeChanged=b.Observed==null || b.Observed.Grade!=s.Grade;
            b.Use.FsmVariables.FindFsmInt(c["grade"]).Value=s.Grade;
            if(gradeChanged)
            {
                var material=FsmHook.FindState(b.Use,c["material"])!;
                foreach(var action in material.Actions)action.OnEnter();
                if(b.Use.FsmVariables.FindFsmMaterial("Material")?.Value==null)throw new InvalidOperationException("Native oil material table unavailable.");
            }
            b.Use.FsmVariables.FindFsmFloat(c["fluid"]).Value=s.Fluid;
            b.Use.FsmVariables.FindFsmFloat(c["viscosity"]).Value=s.Viscosity;
            if(b.Source!=null)
            {
                b.Source.FsmVariables.FindFsmFloat(c["fluid"]).Value=s.Fluid;
                b.Source.FsmVariables.FindFsmFloat(c["viscosity"]).Value=s.Viscosity;
                var pour=b.Source.GetComponent<Collider>();if(pour!=null)pour.enabled=_oilReplicaCap!=null && !_oilRefillFailed;
            }
            b.Body.name=s.Empty?c["emptyName"]:c["itemName"];b.Body.mass=s.Fluid+.5f;
            if(b.Particle!=null)b.Particle.SetActive(false);
            b.Observed=s;
        }
        private void BindMotorOilBody(uint id,Rigidbody body)
        {
            if(_items.TryGetValue(id,out var old) && old.Body!=null && old.Body!=body)throw new InvalidOperationException("Motor-oil identity collision.");
            if(old==null || old.Body==null)_items[id]=new SyncedItem { Id=id, Body=body, Path=ScenePath.Of(body.transform), LastPosition=body.position };
            _trackedBodies[body]=true;
        }
    }
}
