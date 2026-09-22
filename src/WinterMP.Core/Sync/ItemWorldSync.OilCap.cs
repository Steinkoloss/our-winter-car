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
        private sealed class OilCap
        {
            internal PlayMakerFSM Cap = null!, Fill = null!;
            internal GameObject Mesh = null!, Sound = null!;
            internal SphereCollider Collider = null!;
            internal FsmFloat Rotation = null!;
            internal FsmState Up = null!, Down = null!;
            internal bool Replica;
        }
        private readonly Dictionary<PlayMakerFSM,OilCap> _oilCaps = new Dictionary<PlayMakerFSM,OilCap>();
        private readonly List<Action> _oilCapRestore = new List<Action>();
        private OilCap? _oilReplicaCap;
        private PlayMakerFSM? _oilGauge;
        private GameObject? _oilGui;
        private Transform[]? _oilGuiNodes;
        private float _oilCapScan;

        private static FsmFloat OilScalar(PlayMakerFSM f,string name)
        {
            var v=f.FsmVariables.FindFsmFloat(name);
            if(v==null || float.IsNaN(v.Value) || float.IsInfinity(v.Value))throw new InvalidOperationException("Missing/invalid oil scalar "+name);
            return v;
        }
        private static void OilExternal(FsmStateAction action,PlayMakerFSM owner,string target,string scalar)
        {
            var obj=PackageField<FsmOwnerDefault>(action,"gameObject");
            if(obj==null || obj.OwnerOption!=OwnerDefaultOption.SpecifyGameObject
                || !ReferenceEquals(obj.GameObject,owner.FsmVariables.FindFsmGameObject(target))
                || PackageField<FsmString>(action,"fsmName")?.Value!="Data"
                || PackageField<FsmString>(action,"variableName")?.Value!=scalar)
                throw new InvalidOperationException("Native oil transfer target changed.");
        }
        private static void OilRate(FsmStateAction action,string field,float value)
        {
            var v=PackageField<FsmFloat>(action,field);
            if(v==null || v.UseVariable || v.Value!=value || !(bool)action.GetType().GetField("perSecond").GetValue(action)
                || !(bool)action.GetType().GetField("everyFrame").GetValue(action))throw new InvalidOperationException("Native oil rate changed.");
        }
        private OilCap BindOilCap(PlayMakerFSM cap,bool replica)
        {
            var c=SyncCatalog.MotorOil!;
            var fill=EngineBlockDataFsm(cap.transform.Find(c["fill"]).gameObject,c["fillFsm"]);
            foreach(var f in new[]{cap,fill})if(!f.Fsm.Initialized)f.Fsm.Init(f);
            var result=new OilCap { Cap=cap,Fill=fill,Replica=replica,Rotation=OilScalar(cap,"Rot"),
                Up=PackageStateActions(cap,"Screw","FloatAdd","FloatClamp","SetRotation","FloatCompare"),
                Down=PackageStateActions(cap,"Unscrew","FloatSubtract","FloatClamp","SetRotation","FloatCompare") };
            result.Mesh=cap.FsmVariables.FindFsmGameObject("CapMesh").Value;
            result.Collider=fill.GetComponent<SphereCollider>();
            if(result.Mesh==null || result.Mesh.transform.parent!=cap.transform || result.Collider==null
                || !result.Collider.isTrigger || result.Collider.radius<=0 || result.Collider.radius>.1f
                || cap.FsmVariables.FindFsmGameObject("FluidTrigger").Value!=fill.gameObject
                || cap.GetComponentsInChildren<Rigidbody>(true).Length!=0 || OilScalar(cap,"ScrewAmount").Value!=33
                || OilScalar(fill,"MaxCapacity").Value!=MotorOilRefillPolicy.Capacity)
                throw new InvalidOperationException("Native oil cap geometry or capacity changed.");
            foreach(var state in new[]{result.Up,result.Down})
            {
                var first=state.Actions[0];var clamp=state.Actions[1];var render=state.Actions[2];
                if(!ReferenceEquals(PackageField<FsmFloat>(first,"floatVariable"),result.Rotation)
                    || !ReferenceEquals(PackageField<FsmFloat>(first,state==result.Up?"add":"subtract"),OilScalar(cap,"ScrewAmount"))
                    || PackageField<FsmFloat>(clamp,"minValue")?.Value!=1 || PackageField<FsmFloat>(clamp,"maxValue")?.Value!=359
                    || !ReferenceEquals(PackageField<FsmFloat>(render,"zAngle"),result.Rotation)
                    || PackageField<FsmOwnerDefault>(render,"gameObject")?.GameObject.Value!=result.Mesh)
                    throw new InvalidOperationException("Native oil cap controls changed.");
            }
            PackageStateActions(cap,"State 1","MasterAudioPlaySound","ActivateGameObject","ActivateGameObject");
            PackageStateActions(cap,"State 2","MasterAudioPlaySound","ActivateGameObject","ActivateGameObject");
            var pour=PackageStateActions(fill,"Pouring","ActivateGameObject","ActivateGameObject","SubtractFsmFloat","FloatAdd","FloatClamp","AddFsmFloat","SubtractFsmFloat","SetFsmFloat","GetFsmFloat","GetFsmBool","TriggerEvent","FloatCompare","BoolTest");
            OilExternal(pour.Actions[2],fill,"FluidTrigger","Fluid");OilRate(pour.Actions[2],"subtractValue",.1f);
            OilExternal(pour.Actions[5],fill,"Oilpan","Oil");OilRate(pour.Actions[5],"addValue",.1f);
            OilExternal(pour.Actions[6],fill,"Oilpan","OilContamination");OilRate(pour.Actions[6],"subtractValue",3.2f);
            OilExternal(pour.Actions[7],fill,"Oilpan","OilViscosity");
            foreach(string name in new[]{"Add","Remove"})
            {
                var state=PackageStateActions(fill,name,"SetFloatValue","SetFloatValue","SetFloatValue");
                if(PackageField<FsmFloat>(state.Actions[2],"floatValue")?.Value!=(name=="Add"?.06f:-.06f))throw new InvalidOperationException("Native oil mixing rate changed.");
            }
            result.Sound=PackageField<FsmOwnerDefault>(pour.Actions[1],"gameObject")!.GameObject.Value;
            if(result.Sound==null || result.Sound.transform.parent!=fill.transform)throw new InvalidOperationException("Native oil sound changed.");
            return result;
        }
        private void PauseOilFsm(PlayMakerFSM f)
        {
            var globals=f.Fsm.GlobalTransitions;f.Fsm.GlobalTransitions=new FsmTransition[0];
            var pause=new FsmSuppressor();if(!pause.Suppress(f))throw new InvalidOperationException("Cannot isolate native oil writer.");
            _oilCapRestore.Add(()=>{pause.Restore();if(f!=null)f.Fsm.GlobalTransitions=globals;});
        }
        private void DiscoverOilCaps(SessionManager session)
        {
            if(Time.unscaledTime<_oilCapScan)return;_oilCapScan=Time.unscaledTime+1;
            var c=SyncCatalog.MotorOil!;
            foreach(var obj in ScenePath.ScanFsms())
            {
                var f=obj as PlayMakerFSM;
                if(f==null || f.FsmName!=c["capFsm"] || f.name!=c["cap"] || f.transform.parent==null
                    || f.transform.parent.name!=SyncCatalog.GuestEngineInputs?.Block?.RockerCover.RelativePath || _oilCaps.ContainsKey(f))continue;
                // Resource scans include uninstantiated factory prefabs. Only a
                // started part with its native save identity can supply a cap.
                var data=NativePartIdentity.FindData(f.transform.parent);
                var identity=SyncCatalog.PartIdentity;
                if(data==null || identity==null || !data.Fsm.Started || !PartIdentity.TryPersistentId(
                    data.FsmVariables.FindFsmString(identity["idVariable"])?.Value??string.Empty,
                    data.FsmVariables.FindFsmString(identity["assemblyKeyVariable"])?.Value??string.Empty,
                    data.FsmVariables.FindFsmString(identity["positionKeyVariable"])?.Value??string.Empty,
                    identity["assemblyKeySuffix"],identity["positionKeySuffix"],out _))continue;
                var cap=BindOilCap(f,false);_oilCaps.Add(f,cap);PauseOilFsm(cap.Fill);
                if(!session.IsHost)
                {
                    if(_oilReplicaCap==null)CreateOilCapReplica(cap);
                    bool active=f.gameObject.activeSelf;PauseOilFsm(f);f.gameObject.SetActive(false);
                    // Reverse cleanup activates before resuming native startup flags.
                    _oilCapRestore.Add(()=>{if(f!=null)f.gameObject.SetActive(active);});
                }
            }
            if(_oilGauge!=null)return;
            foreach(var obj in ScenePath.ScanFsms())
            {
                var f=obj as PlayMakerFSM;if(f==null || f.FsmName!=c["gaugeFsm"] || ScenePath.Of(f.transform)!=c["gaugePath"])continue;
                if(!f.Fsm.Initialized)f.Fsm.Init(f);
                PackageStateActions(f,"State 1","GetFsmFloat","GetFsmFloat","FloatOperator","SetScale");
                _oilGauge=f;_oilGui=f.transform.parent.gameObject;_oilGuiNodes=_oilGui.GetComponentsInChildren<Transform>(true);
                var nodes=_oilGuiNodes;var active=new bool[nodes.Length];for(int i=0;i<nodes.Length;i++)active[i]=nodes[i].gameObject.activeSelf;
                Vector3 size=f.transform.localScale;float fluid=OilScalar(f,"Fluid").Value,max=OilScalar(f,"Max").Value,scale=OilScalar(f,"Scale").Value;
                PauseOilFsm(f);
                _oilCapRestore.Add(()=>{if(f==null)return;f.transform.localScale=size;OilScalar(f,"Fluid").Value=fluid;OilScalar(f,"Max").Value=max;OilScalar(f,"Scale").Value=scale;for(int i=0;i<nodes.Length;i++)if(nodes[i]!=null)nodes[i].gameObject.SetActive(active[i]);});
                break;
            }
        }
        private void CreateOilCapReplica(OilCap template)
        {
            var fsms=template.Cap.GetComponentsInChildren<PlayMakerFSM>(true);var enabled=new bool[fsms.Length];GameObject clone;
            try
            {
                for(int i=0;i<fsms.Length;i++){enabled[i]=fsms[i].enabled;fsms[i].enabled=false;}
                clone=(GameObject)UnityEngine.Object.Instantiate(template.Cap.gameObject);
            }
            finally{for(int i=0;i<fsms.Length;i++)fsms[i].enabled=enabled[i];}
            try
            {
                clone.name="wintermp-engine-oil-cap";var cap=EngineBlockDataFsm(clone,SyncCatalog.MotorOil!["capFsm"]);
                var binding=BindOilCap(cap,true);
                foreach(var f in clone.GetComponentsInChildren<PlayMakerFSM>(true)){f.enabled=false;f.Fsm.GlobalTransitions=new FsmTransition[0];f.Fsm.RestartOnEnable=false;}
                foreach(var state in new[]{binding.Up,binding.Down})
                {
                    byte action=state==binding.Up?MotorOilRefillIntent.Screw:MotorOilRefillIntent.Unscrew;
                    state.Actions=new FsmStateAction[]{new FsmHookAction(()=>SendOilIntent(0,action))};state.Actions[0].Init(state);
                }
                _oilReplicaCap=binding;cap.enabled=true;clone.SetActive(false);
            }
            catch{UnityEngine.Object.Destroy(clone);throw;}
        }
        private void RenderOilCap(MotorOilFillerState state)
        {
            var cap=_oilReplicaCap;if(cap==null)return;
            cap.Cap.transform.position=state.CapPosition.ToUnity();cap.Cap.transform.rotation=state.CapRotation.ToUnity();
            cap.Rotation.Value=state.Rotation;
            var angle=cap.Mesh.transform.localEulerAngles;angle.z=state.Rotation;cap.Mesh.transform.localEulerAngles=angle;
            if(cap.Cap.gameObject.activeSelf!=state.Available)cap.Cap.gameObject.SetActive(state.Available);
            bool open=state.Available && state.Rotation<=1;
            cap.Mesh.SetActive(!open);cap.Fill.gameObject.SetActive(open);
        }
        private void OilGauge(bool show,OilCap? cap,float oil)
        {
            if(_oilGauge==null || _oilGui==null || _oilGuiNodes==null)return;
            if(show)
            {
                foreach(var n in _oilGuiNodes)if(n!=null)n.gameObject.SetActive(true);
                OilScalar(_oilGauge,"Fluid").Value=oil;OilScalar(_oilGauge,"Max").Value=MotorOilRefillPolicy.Capacity;
                float ratio=Mathf.Clamp01(oil/MotorOilRefillPolicy.Capacity);OilScalar(_oilGauge,"Scale").Value=ratio;
                var scale=_oilGauge.transform.localScale;scale.x=ratio;_oilGauge.transform.localScale=scale;
            }
            _oilGui.SetActive(show);if(cap!=null && cap.Sound!=null)cap.Sound.SetActive(show);
        }
        private void ClearOilCaps()
        {
            if(_oilReplicaCap!=null && _oilReplicaCap.Cap!=null)UnityEngine.Object.Destroy(_oilReplicaCap.Cap.gameObject);
            for(int i=_oilCapRestore.Count-1;i>=0;i--)try{_oilCapRestore[i]();}catch(Exception e){WinterMPPlugin.Log.LogWarning("Engine-oil restoration: "+e.Message);}
            _oilCaps.Clear();_oilCapRestore.Clear();_oilReplicaCap=null;_oilGauge=null;_oilGui=null;_oilGuiNodes=null;_oilCapScan=0;
        }
    }
}
