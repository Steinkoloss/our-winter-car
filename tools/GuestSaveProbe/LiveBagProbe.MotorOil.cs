using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool MotorOilProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_MOTOROIL_TEST")=="1";
        private static MotorOilBottleState? _oilCache;
        private static bool MotorOilCommand(string[] args,List<string> rows)
        {
            if(!MotorOilProbe || !args[1].StartsWith("oil-",StringComparison.Ordinal))return false;
            RequirePersistenceSandbox();var session=SessionManager.Instance!;
            if(args[1]=="oil-view")return true;
            if(args[1]=="oil-create" || args[1]=="oil-local-create")
            {
                if(args[1]=="oil-create"?!session.IsHost:session.State!=SessionState.Idle)throw new InvalidOperationException("Factory fixture requires host or disconnected local profile.");
                int grade=int.Parse(args[2]);if(grade<0 || grade>2)throw new InvalidOperationException("Invalid grade.");
                var factory=Find("Spawner/CreateItemsSeparate/MotorOil","MotorOil");
                factory.FsmVariables.FindFsmInt("Type").Value=grade;Enter(factory,"Create product");return true;
            }
            if(args[1]=="oil-replay")
            {if(!session.IsHost || _oilCache==null)throw new InvalidOperationException("Host cache required.");session.SendWorldMessage(_oilCache,Channel.ReliableOrdered);return true;}
            uint id=uint.Parse(args[2]);
            var bottle=((IDictionary)Get(Items,"_motorOil"))[id]??throw new InvalidOperationException("Oil missing.");
            var body=Get(bottle,"Body") as Rigidbody;var use=Get(bottle,"Use") as PlayMakerFSM;var source=Get(bottle,"Source") as PlayMakerFSM;
            if(args[1]=="oil-cache")
            {if(!session.IsHost)throw new InvalidOperationException("Host cache only.");_oilCache=(MotorOilBottleState)Call(Items,"BuildMotorOilState",id);return true;}
            if(body==null || use==null)throw new InvalidOperationException("Oil body missing.");
            if(args[1]=="oil-near"){Player!.position=body.position+Vector3.up;return true;}
            if(args[1]=="oil-pick")
            {var hand=Find(Hand,"PickUp");hand.FsmVariables.FindFsmGameObject("PickedObject").Value=body.gameObject;Enter(hand,"Set pivot 2");return true;}
            if(args[1]=="oil-fluid")
            {
                if(!session.IsHost || source==null)throw new InvalidOperationException("Host source fixture only.");
                float fluid=float.Parse(args[3],CultureInfo.InvariantCulture);if(float.IsNaN(fluid)||fluid<0||fluid>4)throw new InvalidOperationException("Invalid fixture quantity.");
                source.FsmVariables.FindFsmFloat("Fluid").Value=fluid;use.FsmVariables.FindFsmFloat("Fluid").Value=fluid;
                use.SendEvent("GLOBALEVENT");return true;
            }
            if(args[1]=="oil-retire")
            {if(!session.IsHost)throw new InvalidOperationException("Host garbage only.");use.SendEvent("GARBAGE");return true;}
            throw new InvalidOperationException("Unknown oil command.");
        }
        private static void MotorOilSnapshot(List<string> rows)
        {
            if(Application.loadedLevelName!="GAME")return;
            OilRefillSnapshot(rows);
            rows.Add("oil-failed|"+Get(Items,"_motorOilFailed"));
            var factory=Find("Spawner/CreateItemsSeparate/MotorOil","MotorOil");
            rows.Add("oil-factory|"+factory.enabled+"|"+factory.ActiveStateName+"|"+factory.FsmVariables.FindFsmInt("ObjectNumberInt").Value+"|"+factory.FsmVariables.FindFsmString("SaveID").Value);
            var hand=Find(Hand,"PickUp");var held=hand.FsmVariables.FindFsmGameObject("PickedObject").Value;
            rows.Add("oil-hand|"+hand.ActiveStateName+"|"+(held!=null?held.name:"none"));
            foreach(var obj in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f=obj as PlayMakerFSM;if(f==null || f.FsmName!="Use")continue;
                string native=f.FsmVariables.FindFsmString("ID")?.Value??"";
                if(!FactoryItemIdentity.IsNativeId(native,MotorOilPolicy.NativePrefix))continue;
                rows.Add("oil-local|"+native+"|"+f.enabled+"|"+f.gameObject.activeSelf+"|"+f.FsmVariables.FindFsmFloat("Fluid")?.Value+"|"+f.FsmVariables.FindFsmInt("Type")?.Value);
            }
            foreach(DictionaryEntry entry in (IDictionary)Get(Items,"_motorOil"))
            {
                var b=entry.Value;var body=Get(b,"Body") as Rigidbody;var use=Get(b,"Use") as PlayMakerFSM;var source=Get(b,"Source") as PlayMakerFSM;
                var item=((IDictionary)Get(Items,"_items"))[entry.Key];var state=Get(b,"Observed") as MotorOilBottleState;
                var material=body!=null?body.GetComponent<Renderer>()?.sharedMaterial:null;
                rows.Add("oil-bottle|"+entry.Key+"|"+Get(b,"NativeId")+"|"+Get(b,"Replica")+"|"+(body!=null)+"|"+use?.FsmVariables.FindFsmInt("Type")?.Value+"|"+(source!=null?source:use)?.FsmVariables.FindFsmFloat("Fluid")?.Value.ToString("R",CultureInfo.InvariantCulture)+"|"+use?.FsmVariables.FindFsmFloat("Viscosity")?.Value.ToString("R",CultureInfo.InvariantCulture)+"|"+(material!=null?material.name:"none")+"|"+(body!=null?body.name:"none")+"|"+state?.Revision+"|"+(item!=null?Get(item,"RemoteOwner"):null)+"|"+(body!=null?Vector(body.position):"none"));
            }
        }
    }
}
