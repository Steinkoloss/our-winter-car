using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool AdvertProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_ADVERT_TEST")=="1";
        private static AdvertSheetState? _advertCache;
        private static uint AdPileId => (uint)Get(Items,"_adPileId");
        private static PlayMakerFSM AdBox(string index) => (PlayMakerFSM)Get(((IDictionary)Get(Items,"_adMailboxes"))[byte.Parse(index)]!,"Fsm");
        private static bool AdvertCommand(string[] args,List<string> rows)
        {
            if(!AdvertProbe || !args[1].StartsWith("advert-",StringComparison.Ordinal))return false;
            RequirePersistenceSandbox();var session=SessionManager.Instance!;
            if(args[1]=="advert-view")return true;
            var data=(PlayMakerFSM)Get(Items,"_adData");var use=(PlayMakerFSM)Get(Items,"_adUse");var pile=(Rigidbody)Get(Items,"_adPile");
            if(args[1]=="advert-start")
            { if(!session.IsHost)throw new InvalidOperationException("Host job fixture only.");FsmVariables.GlobalVariables.FindFsmInt("GlobalDay").Value=2;Enter(data,"New job");return true; }
            if(args[1]=="advert-payday")
            { if(!session.IsHost)throw new InvalidOperationException("Host payday only.");FsmVariables.GlobalVariables.FindFsmInt("GlobalDay").Value=5;Enter(data,"New job");return true; }
            if(args[1]=="advert-near")
            {
                Vector3 target=args[2]=="pile"?pile.position:args[2]=="box"?AdBox(args[3]).transform.position:((Rigidbody)Get(((IDictionary)Get(Items,"_adSheets"))[uint.Parse(args[2])]!,"Body")).position;
                Player!.position=target+new Vector3(0,0,1);return true;
            }
            if(args[1]=="advert-take"){Enter(use,"Open");return true;}
            if(args[1]=="advert-request")
            {
                session.SendWorldMessage(new AdvertIntent{ItemId=args[2]=="pile"?AdPileId:uint.Parse(args[2]),Box=byte.Parse(args[3]),ExpectedRevision=uint.Parse(args[4]),Sequence=uint.Parse(args[5]),PlayerId=args.Length>6?byte.Parse(args[6]):session.LocalPlayerId},Channel.ReliableOrdered);return true;
            }
            if(args[1]=="advert-replay")
            { if(!session.IsHost || _advertCache==null)throw new InvalidOperationException("No cached advert.");session.SendWorldMessage(_advertCache,Channel.ReliableOrdered);return true; }
            if(args[1]=="advert-ledger")
            {
                var state=Call(Items,"BuildAdvertJobState") as AdvertJobState;
                if(state!=null)rows.Add("advert-wire|"+state.Revision+"|"+state.CompletedMask);return true;
            }
            uint id=uint.Parse(args[2],CultureInfo.InvariantCulture);
            var sheet=((IDictionary)Get(Items,"_adSheets"))[id]??throw new InvalidOperationException("Sheet missing.");var body=(Rigidbody)Get(sheet,"Body");
            if(args[1]=="advert-pick")
            { var hand=Find(Hand,"PickUp");hand.FsmVariables.FindFsmGameObject("PickedObject").Value=body.gameObject;Enter(hand,"Set pivot 2");return true; }
            if(args[1]=="advert-deliver")
            {var box=AdBox(args[3]);box.FsmVariables.FindFsmGameObject("AdvertObject").Value=body.gameObject;Enter(box,"Open");return true;}
            if(args[1]=="advert-cache")
            {if(!session.IsHost)throw new InvalidOperationException("Host cache only.");_advertCache=(AdvertSheetState)Call(Items,"BuildAdvertSheetState",id);return true;}
            throw new InvalidOperationException("Unknown advert command.");
        }
        private static void AdvertSnapshot(List<string> rows)
        {
            if(Application.loadedLevelName!="GAME")return;
            rows.Add("advert-failed|"+Get(Items,"_adFailed"));
            var hand=Find(Hand,"PickUp");var picked=hand.FsmVariables.FindFsmGameObject("PickedObject").Value;
            rows.Add("advert-hand|"+hand.ActiveStateName+"|"+(picked!=null?picked.name:"none"));
            var nativeData=Find("JOBS/ADs","Data");
            var nativePile=nativeData.FsmVariables.FindFsmGameObject("Pile").Value;
            PlayMakerFSM nativeUse=null!;
            foreach(var f in nativePile.GetComponents<PlayMakerFSM>())if(f.FsmName=="Use")nativeUse=f;
            uint nativeMask=0;
            foreach(var component in nativeData.GetComponents<MonoBehaviour>())
                if(component!=null && component.GetType().Name=="PlayMakerArrayListProxy")
                {
                    var flags=(IList)component.GetType().GetProperty("arrayList").GetValue(component,null);
                    for(int i=0;i<flags.Count;i++)if(flags[i] is bool flag && flag)nativeMask|=1u<<i;
                }
            int nativeSheets=0;
            foreach(var obj in UnityEngine.Object.FindObjectsOfType<Rigidbody>())
                if(obj.name=="advert(Clone)" && obj.gameObject.activeInHierarchy)nativeSheets++;
            rows.Add("advert-local|"+nativeData.enabled+"|"+nativeData.FsmVariables.FindFsmInt("JobStage").Value+"|"+nativeData.FsmVariables.FindFsmInt("Delivered").Value+"|"+nativeUse.FsmVariables.FindFsmInt("Sheets").Value+"|"+nativeMask+"|"+nativeSheets);
            var bank=Find("Systems/BankAccount","Data");IList? descriptions=null,amounts=null;
            foreach(var component in bank.GetComponents<MonoBehaviour>())
                if(component!=null && component.GetType().Name=="PlayMakerArrayListProxy")
                {
                    string reference=(string)component.GetType().GetField("referenceName").GetValue(component);
                    var entries=(IList)component.GetType().GetProperty("arrayList").GetValue(component,null);
                    if(reference=="Selite")descriptions=entries;if(reference=="Tapahtumat")amounts=entries;
                }
            if(descriptions!=null && amounts!=null)
                for(int i=0;i<Math.Min(descriptions.Count,amounts.Count);i++)rows.Add("advert-bank-entry|"+i+"|"+descriptions[i]+"|"+amounts[i]);
            var data=Get(Items,"_adData") as PlayMakerFSM;var use=Get(Items,"_adUse") as PlayMakerFSM;var pile=Get(Items,"_adPile") as Rigidbody;
            if(data==null || use==null || pile==null)return;
            var received=Get(Items,"_adReceived") as AdvertJobState;var state=SessionManager.Instance!.IsHost?Call(Items,"BuildAdvertJobState") as AdvertJobState:received;
            rows.Add("advert-job|"+data.enabled+"|"+data.ActiveStateName+"|"+data.FsmVariables.FindFsmInt("JobStage").Value+"|"+data.FsmVariables.FindFsmInt("Delivered").Value+"|"+use.FsmVariables.FindFsmInt("Sheets").Value+"|"+state?.Revision+"|"+state?.CompletedMask+"|"+data.FsmVariables.FindFsmFloat("Salary").Value.ToString("R",CultureInfo.InvariantCulture));
            rows.Add("advert-pile|"+AdPileId+"|"+Vector(pile.position)+"|"+pile.gameObject.activeInHierarchy+"|"+use.ActiveStateName+"|"+pile.transform.Find("ScalePivot").localScale.z.ToString("R",CultureInfo.InvariantCulture));
            rows.Add("bank|"+FsmVariables.GlobalVariables.FindFsmFloat("PlayerBankAccount").Value.ToString("R",CultureInfo.InvariantCulture));
            rows.Add("player|"+(Player==null?"none":Vector(Player.position)));
            foreach(DictionaryEntry entry in (IDictionary)Get(Items,"_adMailboxes"))
            {
                var box=entry.Value;var f=(PlayMakerFSM)Get(box,"Fsm");
                rows.Add("advert-box|"+entry.Key+"|"+f.gameObject.activeInHierarchy+"|"+f.FsmVariables.FindFsmBool("AdvertPlaced").Value+"|"+f.ActiveStateName+"|"+Get(box,"Reserved")+"|"+Vector(f.transform.position));
            }
            foreach(DictionaryEntry entry in (IDictionary)Get(Items,"_adSheets"))
            {
                var b=entry.Value;var body=Get(b,"Body") as Rigidbody;var item=((IDictionary)Get(Items,"_items"))[entry.Key];
                rows.Add("advert-sheet|"+entry.Key+"|"+Get(b,"Replica")+"|"+(body!=null)+"|"+(body!=null?Vector(body.position):"none")+"|"+(item!=null?Get(item,"RemoteOwner"):null));
            }
        }
    }
}
