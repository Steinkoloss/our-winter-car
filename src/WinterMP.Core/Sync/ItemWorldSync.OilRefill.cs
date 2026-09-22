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
        private sealed class OilDestination
        {
            internal OilCap Cap = null!;
            internal PlayMakerFSM Mount = null!, Part = null!;
            internal uint HeadId, PanId;
        }
        private sealed class OilLease { internal uint Bottle, Epoch; internal float At; }
        private OilDestination? _oilDestination;
        private MotorOilFillerState? _oilObserved, _oilReceived;
        private readonly MotorOilIntentLedger _oilIntentLedger = new MotorOilIntentLedger();
        private readonly Dictionary<byte,OilLease> _oilLeases = new Dictionary<byte,OilLease>();
        private uint _oilEpoch=1, _oilGuestBottle;
        private ushort _oilSequence;
        private float _oilPublishAt, _oilIntentAt;
        private bool _oilRefillFailed;

        private OilDestination? ResolveOilDestination()
        {
            var rule=SyncCatalog.GuestEngineInputs?.Block;if(rule==null)return null;
            CaptureEngineBlock(new EngineSourceLookup(),rule,out _,out _,out var head,out var block);
            if(head==null || block==null)return null;
            var cover=ReadMountedIntake(head,rule.RockerCover,"Tightness");
            var pan=ReadMountedIntake(block,rule.Oilpan,"OilLevel");
            if(cover==null || pan==null)return null;
            var capObject=cover.FsmVariables.FindFsmGameObject("OilCap")?.Value;
            if(capObject==null || capObject.transform.parent!=cover.transform || !capObject.activeInHierarchy)return null;
            var cap=EngineBlockDataFsm(capObject,SyncCatalog.MotorOil!["capFsm"]);
            if(!_oilCaps.TryGetValue(cap,out var binding) || !cap.enabled || !cap.Fsm.Started)return null;
            var part=EngineBlockDataFsm(pan.FsmVariables.FindFsmGameObject("ActivePart").Value,"Data");
            if(OilScalar(pan,"OilMax").Value!=MotorOilRefillPolicy.Capacity)return null;
            var copy=PackageStateActions(pan,"Update 2","SetBoolValue","FloatClamp","FloatClamp","SetFsmFloat","SetFsmFloat","SetFsmFloat","SetFsmFloat","Wait");
            string[] mounted={"OilContamination","Oil","OilViscosity"},saved={"OilDirt","OilLevel","OilViscosity"};
            for(int i=0;i<3;i++)
            {
                OilExternal(copy.Actions[i+3],pan,"ActivePart",saved[i]);
                if(!ReferenceEquals(PackageField<FsmFloat>(copy.Actions[i+3],"setValue"),OilScalar(pan,mounted[i]))
                    || ReferenceEquals(OilScalar(pan,mounted[i]),OilScalar(part,saved[i])))throw new InvalidOperationException("Oilpan saved mirror changed.");
            }
            if(!MotorOilRefillPolicy.Pan(OilScalar(pan,"Oil").Value,OilScalar(pan,"OilContamination").Value,OilScalar(pan,"OilViscosity").Value))return null;
            return new OilDestination { Cap=binding,Mount=pan,Part=part,
                HeadId=StableHash.Fnv1a32("oil-head:"+EngineBlockDataFsm(head,"Data").FsmVariables.FindFsmString("ID").Value),
                PanId=StableHash.Fnv1a32("oil-pan:"+part.FsmVariables.FindFsmString("ID").Value) };
        }
        private void RefreshOilDestination()
        {
            var next=ResolveOilDestination();var old=_oilDestination;
            if((old==null)!=(next==null) || old!=null && next!=null && (old.Cap!=next.Cap || old.Part!=next.Part || old.HeadId!=next.HeadId || old.PanId!=next.PanId))
            {
                if(++_oilEpoch==0)++_oilEpoch;_oilLeases.Clear();
                if(old!=null && old.Cap.Sound!=null)old.Cap.Sound.SetActive(false);
                SyncEventLog.Record("oil-refill-target",next==null?"unavailable":next.HeadId+":"+next.PanId+" epoch="+_oilEpoch);
            }
            _oilDestination=next;
        }
        internal MotorOilFillerState? BuildMotorOilFillerState()
        {
            if(_oilRefillFailed || SessionManager.Instance?.IsHost!=true || SyncCatalog.MotorOil==null)return null;
            try
            {
                var d=_oilDestination;
                var s=new MotorOilFillerState { Epoch=_oilEpoch,Revision=_oilObserved?.Revision??1 };
                if(d!=null)
                {
                    s.HeadId=d.HeadId;s.PanId=d.PanId;s.Rotation=d.Cap.Rotation.Value;
                    s.Oil=OilScalar(d.Mount,"Oil").Value;s.Contamination=OilScalar(d.Mount,"OilContamination").Value;s.Viscosity=OilScalar(d.Mount,"OilViscosity").Value;
                    s.CapPosition=d.Cap.Cap.transform.position.ToNet();s.CapRotation=d.Cap.Cap.transform.rotation.ToNet();
                }
                if(_oilObserved!=null && !MotorOilRefillPolicy.Same(_oilObserved,s)){if(++s.Revision==0)++s.Revision;}
                if(!MotorOilRefillPolicy.Valid(s))throw new InvalidOperationException("Invalid captured engine-oil filler.");
                _oilObserved=s;return s;
            }
            catch(Exception e){FailOilRefill(e);return null;}
        }

        internal void OnMotorOilFillerState(MotorOilFillerState s)
        {
            if(_oilRefillFailed || SessionManager.Instance?.IsHost!=false || !MotorOilRefillPolicy.Accept(_oilReceived,s))return;
            _oilReceived=s;
        }
        private void ProcessOilRefill(SessionManager session)
        {
            if(_oilRefillFailed || SyncCatalog.MotorOil==null)return;
            try
            {
                DiscoverOilCaps(session);
                bool localPour=false;OilCap? active=null;float oil=0;
                if(session.IsHost)
                {
                    RefreshOilDestination();var d=_oilDestination;active=d?.Cap;
                    foreach(var pair in new List<KeyValuePair<byte,OilLease>>(_oilLeases))
                    {
                        if(pair.Value.Epoch!=_oilEpoch || !AtfPolicy.LeaseFresh(Time.unscaledTime,pair.Value.At) || !CanRemoteOilPour(pair.Key,pair.Value.Bottle))
                        {_oilLeases.Remove(pair.Key);continue;}
                        TransferOil(_motorOil[pair.Value.Bottle]);
                    }
                    if(OilPlayer(session.LocalPlayerId,out var player))
                        foreach(var b in _motorOil.Values)
                            if(b.Body!=null && _items.TryGetValue(b.Id,out var item) && item.RemoteOwner==WorldSyncIds.NoOwner
                                && CanOilPour(b,player,b.Body.position,b.Body.rotation,false))
                            {localPour=true;TransferOil(b);}
                    if(d!=null)oil=OilScalar(d.Mount,"Oil").Value;
                    if(Time.unscaledTime>=_oilPublishAt)
                    { _oilPublishAt=Time.unscaledTime+.1f;var s=BuildMotorOilFillerState();if(s!=null)session.SendWorldMessage(s,Channel.ReliableOrdered); }
                }
                else
                {
                    if(_oilReceived!=null)RenderOilCap(_oilReceived);
                    active=_oilReplicaCap;oil=_oilReceived?.Oil??0;uint pouring=0;
                    if(_oilReceived?.Available==true && active!=null && OilPlayer(session.LocalPlayerId,out var player))
                        foreach(var b in _motorOil.Values)
                            if(b.Body!=null && _items.TryGetValue(b.Id,out var item) && item.LocallyOwned
                                && CanOilPour(b,player,b.Body.position,b.Body.rotation,false)){pouring=b.Id;break;}
                    localPour=pouring!=0;
                    if(_oilGuestBottle!=pouring)
                    {if(_oilGuestBottle!=0)SendOilIntent(_oilGuestBottle,MotorOilRefillIntent.Stop);_oilGuestBottle=pouring;_oilIntentAt=0;}
                    if(pouring!=0 && Time.unscaledTime>=_oilIntentAt)
                    {_oilIntentAt=Time.unscaledTime+.2f;SendOilIntent(pouring,MotorOilRefillIntent.Pour);}
                }
                foreach(var b in _motorOil.Values)
                {
                    if(b.Body==null || b.Use==null)continue;
                    float fluid=OilScalar(b.Source!=null?b.Source:b.Use,"Fluid").Value;
                    if(!b.Replica && fluid<.02f && b.Body.name!=SyncCatalog.MotorOil["emptyName"])
                    {OilScalar(b.Use,"Fluid").Value=fluid;b.Use.SendEvent("GLOBALEVENT");}
                    if(b.Replica && b.Source!=null)
                    {
                        var collider=b.Source.GetComponent<Collider>();if(collider!=null)collider.enabled=_oilReplicaCap!=null;
                        bool tilted=b.Body.rotation.eulerAngles.x<80 && fluid>0 && !b.Observed!.Empty;
                        b.Source.FsmVariables.FindFsmBool("Pouring").Value=tilted;
                        if(b.Particle!=null)b.Particle.SetActive(tilted);
                    }
                }
                OilGauge(localPour,active,oil);
            }
            catch(Exception e){FailOilRefill(e);}
        }
        private bool OilPlayer(byte actor,out Vector3 position)
        {
            position=Vector3.zero;var s=SessionManager.Instance;if(s==null)return false;
            if(actor==s.LocalPlayerId)
                return DeathSyncManager.Instance?.IsLocalDead!=true && (s.IsHost || PlayerSyncManager.Instance?.IsLocalSpawnReady==true) && TryGetLocalPlayerPosition(out position);
            foreach(var p in s.Players)
                if(p.PlayerId==actor && !p.IsDead && p.LastTransformTime>0 && Time.unscaledTime>=p.LastTransformTime && Time.unscaledTime-p.LastTransformTime<=2)
                {position=p.Position;return true;}
            return false;
        }
        private bool CanOilPour(MotorOilBottle bottle,Vector3 player,Vector3 position,Quaternion rotation,bool remote)
        {
            var c=SessionManager.Instance?.IsHost==true?_oilDestination?.Cap:_oilReplicaCap;
            if(c==null || c.Rotation.Value>1 || !c.Cap.gameObject.activeInHierarchy || bottle.Body==null || bottle.Source==null
                || bottle.Body.name==SyncCatalog.MotorOil!["emptyName"] || _spawnLifecycle.IsRetired(bottle.Id))return false;
            float fluid=OilScalar(bottle.Source,"Fluid").Value;
            if(fluid<=0 || fluid>4 || (player-position).sqrMagnitude>9 || (player-c.Cap.transform.position).sqrMagnitude>9
                || !AdvertPolicy.Pose(position.ToNet(),rotation.ToNet()))return false;
            float length=Mathf.Sqrt(rotation.x*rotation.x+rotation.y*rotation.y+rotation.z*rotation.z+rotation.w*rotation.w);
            rotation=new Quaternion(rotation.x/length,rotation.y/length,rotation.z/length,rotation.w/length);
            if(rotation.eulerAngles.x>=80)return false;
            var source=bottle.Source.GetComponent<CapsuleCollider>();
            return remote?AtfRefillSync.OverlapsAtPose(source,c.Collider,bottle.Body.transform,position,rotation):AtfRefillSync.Overlaps(source,c.Collider);
        }
        private bool CanRemoteOilPour(byte actor,uint id)
        {
            if(!_motorOil.TryGetValue(id,out var b) || b.Replica || b.Body==null || !_items.TryGetValue(id,out var item)
                || item.Body!=b.Body || item.RemoteOwner!=actor || item.LocallyOwned || !AtfPolicy.LeaseFresh(Time.unscaledTime,item.LastRemoteAt)
                || !OilPlayer(actor,out var player))return false;
            return CanOilPour(b,player,item.TargetPosition,item.TargetRotation,true);
        }
        private void TransferOil(MotorOilBottle b)
        {
            var d=_oilDestination;if(d==null || b.Source==null)return;
            var source=OilScalar(b.Source,"Fluid");var root=OilScalar(b.Use,"Fluid");
            var oil=OilScalar(d.Mount,"Oil");var dirt=OilScalar(d.Mount,"OilContamination");var viscosity=OilScalar(d.Mount,"OilViscosity");
            var savedOil=OilScalar(d.Part,"OilLevel");var savedDirt=OilScalar(d.Part,"OilDirt");var savedViscosity=OilScalar(d.Part,"OilViscosity");
            float amount=MotorOilRefillPolicy.Amount(source.Value,oil.Value,Time.deltaTime);if(amount<=0)return;
            float nextDirt=MotorOilRefillPolicy.Clean(dirt.Value,amount),nextViscosity=MotorOilRefillPolicy.Mix(viscosity.Value,OilScalar(b.Use,"Viscosity").Value,amount);
            // Resolve and validate every reference before this scalar-only pair;
            // the native one-second mirror must not lose an immediate save.
            savedOil.Value=oil.Value=oil.Value+amount;savedDirt.Value=dirt.Value=nextDirt;savedViscosity.Value=viscosity.Value=nextViscosity;
            root.Value=source.Value=source.Value-amount;b.Body.mass=source.Value+.5f;
        }
        private void SendOilIntent(uint bottle,byte action)
        {
            var session=SessionManager.Instance;var state=_oilReceived;
            if(_oilRefillFailed || session==null || session.IsHost || state==null || !state.Available)return;
            if(action>=2 && (_oilReplicaCap==null || !OilPlayer(session.LocalPlayerId,out var p) || (p-_oilReplicaCap.Cap.transform.position).sqrMagnitude>9))return;
            session.SendWorldMessage(new MotorOilRefillIntent { Epoch=state.Epoch,BottleId=bottle,Action=action,PlayerId=session.LocalPlayerId,Sequence=unchecked(++_oilSequence) },Channel.ReliableOrdered);
        }
        internal void OnMotorOilRefillIntent(MotorOilRefillIntent request)
        {
            if(_oilRefillFailed || SessionManager.Instance?.IsHost!=true)return;
            try
            {
                RefreshOilDestination();var d=_oilDestination;
                bool valid=d!=null && request.Epoch==_oilEpoch && (request.Action==0 || (request.Action==1?CanRemoteOilPour(request.PlayerId,request.BottleId)
                    :OilPlayer(request.PlayerId,out var p) && (p-d.Cap.Cap.transform.position).sqrMagnitude<=9));
                if(!_oilIntentLedger.Accept(request,valid))return;
                if(request.Action==MotorOilRefillIntent.Stop)_oilLeases.Remove(request.PlayerId);
                else if(request.Action==MotorOilRefillIntent.Pour)_oilLeases[request.PlayerId]=new OilLease { Bottle=request.BottleId,Epoch=request.Epoch,At=Time.unscaledTime };
                else
                {
                    var cap=d!.Cap;var state=request.Action==MotorOilRefillIntent.Screw?cap.Up:cap.Down;
                    for(int i=0;i<3;i++)state.Actions[i].OnEnter();
                    bool open=cap.Rotation.Value<=1;cap.Mesh.SetActive(!open);cap.Fill.gameObject.SetActive(open);
                    if(!open)_oilLeases.Clear();
                    SyncEventLog.Record("oil-cap",request.PlayerId+":"+request.Sequence+" rotation="+cap.Rotation.Value);
                }
            }
            catch(Exception e){FailOilRefill(e);}
        }
        internal void ForgetOilRefill(byte actor){_oilLeases.Remove(actor);_oilIntentLedger.Forget(actor);}
        private void ClearOilRefill()
        {
            ClearOilCaps();_oilDestination=null;_oilObserved=_oilReceived=null;_oilLeases.Clear();_oilIntentLedger.Clear();
            _oilEpoch=1;_oilGuestBottle=0;_oilSequence=0;_oilPublishAt=_oilIntentAt=0;_oilRefillFailed=false;
        }
        private void FailOilRefill(Exception e)
        {
            if(_oilRefillFailed)return;_oilRefillFailed=true;_oilLeases.Clear();
            OilGauge(false,_oilDestination?.Cap??_oilReplicaCap,0);
            WinterMPPlugin.Log.LogWarning("Engine-oil refill disabled: "+e.Message);SyncEventLog.Record("oil-refill-disabled",e.Message);
        }
    }
}
