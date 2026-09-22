using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool OilRefillProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_OIL_REFILL_TEST")=="1";
        private static uint _oilPin;
        private static float _oilAngle=30;
        private static float _oilMiss;
        private static PlayMakerFSM NativeOilPart(string id)
        {
            PlayMakerFSM? found=null;
            foreach(var obj in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f=obj as PlayMakerFSM;if(f==null || f.FsmName!="Data")continue;
                string native=f.FsmVariables.FindFsmString("ID")?.Value??"";
                if((id=="VIN1060" || id=="VIN1180") ? !native.StartsWith(id.Substring(0,6),StringComparison.Ordinal) : native!=id)continue;
                if(f.FsmVariables.FindFsmBool("Consumed")?.Value==true)continue;
                if(found!=null)throw new InvalidOperationException("Ambiguous native part "+id);found=f;
            }
            return found??throw new InvalidOperationException("Missing native part "+id);
        }
        private static PlayMakerFSM OilProbeMount(string kind)
        {
            string path=kind=="block"?"CORRIS/MotorPivot/MassCenter/Block/VINP_Block":PathOf(NativeOilPart(kind=="cover"?"VIN1110":"VIN1010").transform)+"/"+(kind=="cover"?"VINP_RockerCover":kind=="head"?"VINP_Cylinderhead":"VINP_Oilpan");
            return Find(path,"Data");
        }
        private static object? OilProbeCap => SessionManager.Instance!.IsHost
            ? Get(Items,"_oilDestination") is object d?Get(d,"Cap"):null : Get(Items,"_oilReplicaCap");
        private static void TickOilRefillFixture()
        {
            if(!OilRefillProbe || _oilPin==0 || Application.loadedLevelName!="GAME")return;
            if(SessionManager.Instance?.State!=SessionState.Hosting && SessionManager.Instance?.State!=SessionState.Connected){_oilPin=0;return;}
            RequirePersistenceSandbox();var cap=OilProbeCap;if(cap==null)return;
            var b=((IDictionary)Get(Items,"_motorOil"))[_oilPin];if(b==null)return;
            var body=Get(b,"Body") as Rigidbody;var source=Get(b,"Source") as PlayMakerFSM;
            if(body==null || source==null)return;
            var target=(SphereCollider)Get(cap,"Collider");var collider=source.GetComponent<CapsuleCollider>();
            body.isKinematic=true;body.rotation=body.transform.rotation=Quaternion.Euler(_oilAngle,0,0);
            body.position+=target.transform.TransformPoint(target.center)-collider.transform.TransformPoint(collider.center)+Vector3.right*_oilMiss;
            body.transform.position=body.position;body.velocity=body.angularVelocity=Vector3.zero;
        }
        private static bool OilRefillCommand(string[] args,List<string> rows)
        {
            if(!OilRefillProbe || !args[1].StartsWith("refill-",StringComparison.Ordinal))return false;
            RequirePersistenceSandbox();var session=SessionManager.Instance!;
            if(args[1]=="refill-view")return true;
            if(args[1]=="refill-seal-plug")
            {
                if(!session.IsHost)throw new InvalidOperationException("Host disposable drain-plug fixture only.");
                var plug=Find(PathOf(NativeOilPart("VIN1060").transform)+"/Details/BoltPM","Screw");
                plug.FsmVariables.FindFsmFloat("TightnessF").Value=7;Enter(plug,"Tight?");return true;
            }
            if(args[1]=="refill-saved")
            {
                if(!session.IsHost)throw new InvalidOperationException("Read host fixture saves only.");
                var pan=NativeOilPart("VIN1060");
                foreach(string name in new[]{"UTOilLevel","UTOilDirt","UTOilViscosity","UTPlug"})
                {
                    string tag=pan.FsmVariables.FindFsmString(name).Value;
                    string file=Path.Combine(Application.persistentDataPath,FsmVariables.GlobalVariables.FindFsmString("SaveCarparts").Value)+"?tag="+tag;
                    rows.Add("oil-disk|"+name+"|"+tag+"|"+ES2.Load<float>(file).ToString("R",CultureInfo.InvariantCulture));
                }
                return true;
            }
            if(args[1]=="refill-isolate-leaks")
            {
                if(!session.IsHost)throw new InvalidOperationException("Host disposable refill fixture only.");
                var drain=NativeOilPart("VIN1060").transform.Find("Bolts/Oil");
                drain.gameObject.SetActive(false);return true;
            }
            if(args[1]=="refill-create")
            {
                if(!session.IsHost)throw new InvalidOperationException("Host part factory fixture only.");
                Find("CARPARTS/PARTSYSTEM/SPAWNERS_VIN/"+(args[2]=="pan"?"Oilpan106":"RockerCover118"),"Spawn").SendEvent("SPAWNITEM");return true;
            }
            if(args[1]=="refill-fit")
            {
                if(!session.IsHost)throw new InvalidOperationException("Host native fitting fixture only.");
                string kind=args[2];var mount=OilProbeMount(kind);var part=NativeOilPart(kind=="block"?"VIN1010":kind=="head"?"VIN1110":kind=="cover"?"VIN1180":"VIN1060");
                float tightness=mount.FsmVariables.FindFsmFloat("TightnessMax").Value;
                part.FsmVariables.FindFsmFloat("Tightness").Value=mount.FsmVariables.FindFsmFloat("Tightness").Value=tightness;
                if(mount.FsmVariables.FindFsmBool("Installed").Value){part.SendEvent("BOLTING");return true;}
                mount.FsmVariables.FindFsmGameObject("ActivePart").Value=part.gameObject;Enter(mount,"Install 1");return true;
            }
            if(args[1]=="refill-remove")
            {if(!session.IsHost)throw new InvalidOperationException("Host removal fixture only.");Enter(OilProbeMount(args[2]),"Remove part");return true;}
            if(args[1]=="refill-pan")
            {
                if(!session.IsHost)throw new InvalidOperationException("Host pan fixture only.");
                var pan=OilProbeMount("pan");var part=NativeOilPart("VIN1060");
                string[] from={"Oil","OilContamination","OilViscosity"},to={"OilLevel","OilDirt","OilViscosity"};
                for(int i=0;i<3;i++){float f=float.Parse(args[i+2],CultureInfo.InvariantCulture);if(float.IsNaN(f)||f<0||f>100)throw new InvalidOperationException("Invalid pan fixture.");pan.FsmVariables.FindFsmFloat(from[i]).Value=part.FsmVariables.FindFsmFloat(to[i]).Value=f;}return true;
            }
            if(args[1]=="refill-park")
            {
                foreach(string path in new[]{"CORRIS","CORRIS/MotorPivot/MassCenter"})
                {var body=GameObject.Find(path).GetComponent<Rigidbody>();body.velocity=body.angularVelocity=Vector3.zero;body.constraints=RigidbodyConstraints.FreezeAll;}
                foreach(string name in new[]{"PlayerHunger","PlayerThirst","PlayerFatigue","PlayerUrine"})FsmVariables.GlobalVariables.FindFsmFloat(name).Value=10;return true;
            }
            var cap=OilProbeCap;var fsm=cap==null?null:(PlayMakerFSM)Get(cap,"Cap");
            if(args[1]=="refill-near"){if(fsm==null)throw new InvalidOperationException("No cap.");Player!.position=fsm.transform.position+new Vector3(0,0,1);return true;}
            if(args[1]=="refill-turn")
            {if(fsm==null)throw new InvalidOperationException("No cap.");Enter(fsm,args[2]=="down"?"Unscrew":"Screw");return true;}
            if(args[1]=="refill-place")
            {
                uint id=uint.Parse(args[2]);var b=((IDictionary)Get(Items,"_motorOil"))[id]??throw new InvalidOperationException("No bottle.");
                var body=(Rigidbody)Get(b,"Body");var item=((IDictionary)Get(Items,"_items"))[id];
                Call(Items,"ClaimItem",session,item,body,Time.unscaledTime);_oilPin=id;_oilAngle=args[3]=="upright"?90:30;_oilMiss=args[3]=="miss"?.2f:0;
                TickOilRefillFixture();return true;
            }
            if(args[1]=="refill-unpin"){_oilPin=0;return true;}
            if(args[1]=="refill-request")
            {
                session.SendWorldMessage(new MotorOilRefillIntent { Epoch=uint.Parse(args[2]),BottleId=uint.Parse(args[3]),Sequence=ushort.Parse(args[4]),Action=byte.Parse(args[5]),PlayerId=args.Length>6?byte.Parse(args[6]):session.LocalPlayerId },Channel.ReliableOrdered);return true;
            }
            throw new InvalidOperationException("Unknown refill probe.");
        }
        private static void OilRefillSnapshot(List<string> rows)
        {
            if(!OilRefillProbe || Application.loadedLevelName!="GAME")return;
            rows.Add("refill-failed|"+Get(Items,"_oilRefillFailed"));
            try { rows.Add("oil-drain-fixture|"+NativeOilPart("VIN1060").transform.Find("Bolts/Oil").gameObject.activeInHierarchy); }
            catch(InvalidOperationException) { rows.Add("oil-drain-fixture|none"); }
            var s=SessionManager.Instance!.IsHost?Call(Items,"BuildMotorOilFillerState") as MotorOilFillerState:Get(Items,"_oilReceived") as MotorOilFillerState;
            if(s!=null)rows.Add("refill|"+s.Revision+"|"+s.Epoch+"|"+s.HeadId+"|"+s.PanId+"|"+s.Rotation+"|"+s.Oil.ToString("R",CultureInfo.InvariantCulture)+"|"+s.Contamination.ToString("R",CultureInfo.InvariantCulture)+"|"+s.Viscosity.ToString("R",CultureInfo.InvariantCulture)+"|"+s.Available);
            foreach(string kind in new[]{"block","head","cover","pan"})
            {
                var m=OilProbeMount(kind);rows.Add("oil-mount|"+kind+"|"+m.enabled+"|"+m.ActiveStateName+"|"+m.FsmVariables.FindFsmBool("Installed").Value);
                if(kind=="pan")rows.Add("oil-pan|"+m.FsmVariables.FindFsmFloat("Oil").Value+"|"+m.FsmVariables.FindFsmFloat("OilContamination").Value+"|"+m.FsmVariables.FindFsmFloat("OilViscosity").Value);
            }
            try { var p=NativeOilPart("VIN1060");rows.Add("oil-saved-pan|"+p.FsmVariables.FindFsmFloat("OilLevel").Value+"|"+p.FsmVariables.FindFsmFloat("OilDirt").Value+"|"+p.FsmVariables.FindFsmFloat("OilViscosity").Value); } catch(InvalidOperationException) { rows.Add("oil-saved-pan|none"); }
            var cap=OilProbeCap;
            if(cap!=null){var f=(PlayMakerFSM)Get(cap,"Cap");rows.Add("oil-cap|"+f.enabled+"|"+f.gameObject.activeInHierarchy+"|"+f.ActiveStateName+"|"+Vector(f.transform.position));}
            foreach(DictionaryEntry e in (IDictionary)Get(Items,"_oilCaps"))
            {var f=(PlayMakerFSM)e.Key;rows.Add("oil-native-cap|"+PathOf(f.transform)+"|"+f.enabled+"|"+f.gameObject.activeSelf);}
        }
    }
}
