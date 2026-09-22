using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;
namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool AdvertPhoneProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_ADVERT_PHONE_TEST") == "1";
        private static object PhoneSync => Get(Get(WorldSyncManager.Instance!, "_phone"), "Adverts");
        private static object Phone(byte id) => ((IDictionary)Get(PhoneSync, "_phones"))[id] ?? throw new InvalidOperationException("Phone unbound.");
        private static bool AdvertPhoneCommand(string[] args, List<string> rows)
        {
            if (!AdvertPhoneProbe || !args[1].StartsWith("phone-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox(); var session = SessionManager.Instance!;
            if (args[1] == "phone-view") return true;
            if (args[1] == "phone-fixture")
            {
                if (!session.IsHost) throw new InvalidOperationException("Host fixture only.");
                var listing = (PlayMakerFSM)Get(PhoneSync, "_listing");
                if (listing == null) throw new InvalidOperationException("Fixture needs original native advert listing.");
                listing.gameObject.name = "08231206"; listing.FsmVariables.FindFsmInt("Stage").Value = 1;
                var data = Find("JOBS/ADs", "Data"); data.FsmVariables.FindFsmInt("JobStage").Value = 0;
                data.FsmVariables.FindFsmGameObject("AdvertSpawn").Value.SetActive(false);
                Enter(data, "Check job"); return true;
            }
            byte id = byte.Parse(args[2], CultureInfo.InvariantCulture); var b = Phone(id);
            var calling = (PlayMakerFSM)Get(b, "Calling"); var handle = (PlayMakerFSM)Get(b, "Handle");
            if (args[1] == "phone-prepare")
            {
                for (var t = handle.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
                var cord = Get(b, "Cord") as PlayMakerFSM;
                if (cord != null) { cord.FsmVariables.FindFsmBool("CordPhone").Value = true; Enter(cord, "Phone"); }
                Player!.position = handle.transform.position + new Vector3(0, 0, 1); return true;
            }
            if (args[1] == "phone-near") { Player!.position = handle.transform.position + new Vector3(0, 0, 1); return true; }
            if (args[1] == "phone-far") { Player!.position = handle.transform.position + new Vector3(0, 0, 50); return true; }
            if (args[1] == "phone-pick") { Enter(handle, "Pick phone"); return true; }
            if (args[1] == "phone-close") { Enter(handle, "Close phone"); return true; }
            if (args[1] == "phone-key")
            { calling.FsmVariables.FindFsmString("NumberAdd").Value = args[3]; calling.SendEvent("NUMBER"); return true; }
            if (args[1] == "phone-call")
            { calling.FsmVariables.FindFsmString("Number").Value = "08231206"; Enter(calling, "Find number"); return true; }
            if (args[1] == "phone-answer") { Enter(calling, "Check type"); return true; }
            if (args[1] == "phone-finish") { Enter(calling, "Hangup 2"); return true; }
            if (args[1] == "phone-request")
            {
                session.SendWorldMessage(new AdvertPhoneIntent { Phone=id, Call=uint.Parse(args[3]), Action=byte.Parse(args[4]),
                    PlayerId=args.Length>5?byte.Parse(args[5]):session.LocalPlayerId }, Channel.ReliableOrdered); return true;
            }
            if (args[1] == "phone-service")
            {
                if (!session.IsHost) throw new InvalidOperationException("Host service fixture only.");
                ((PlayMakerFSM)Get(b, "Bill")).FsmVariables.FindFsmBool("PhonePaid").Value = args[3] == "on"; return true;
            }
            if (args[1] == "phone-cord")
            {
                ((PlayMakerFSM)Get(b, "Cord")).FsmVariables.FindFsmBool("CordPhone").Value = args[3] == "on"; return true;
            }
            throw new InvalidOperationException("Unknown phone test command.");
        }
        private static void AdvertPhoneSnapshot(List<string> rows)
        {
            if (Application.loadedLevelName != "GAME") return;
            rows.Add("phone-sync|" + Get(PhoneSync, "_ready") + "|" + Get(PhoneSync, "_failed") + "|" + Get(PhoneSync, "_localCall"));
            var log = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Diagnostics.SyncEventLog", true);
            foreach (string line in ((string)log.GetMethod("GetSnapshot", Members).Invoke(null, null)).Split('\n'))
                if (line.Contains("advert-call")) rows.Add("phone-trace|" + line.Trim());
            var ledger = Get(PhoneSync, "_ledger");
            rows.Add("phone-ledger|" + ledger.GetType().GetProperty("Call").GetValue(ledger, null) + "|" + ledger.GetType().GetProperty("Player").GetValue(ledger, null));
            var listing = Get(PhoneSync, "_listing") as PlayMakerFSM;
            rows.Add("phone-listing|" + (listing == null ? "none" : listing.gameObject.name + "|" + listing.FsmVariables.FindFsmInt("Stage").Value));
            var taxi = Get(WorldSyncManager.Instance!, "_taxiJob");
            var taxiState = SessionManager.Instance!.IsHost ? Call(taxi, "BuildServiceSnapshot") as TaxiServiceState : Get(taxi, "_remoteService") as TaxiServiceState;
            if (taxiState != null) rows.Add("phone-taxi|" + taxiState.CallPhase + "|" + taxiState.CallOwner + "|" + taxiState.CallId);
            foreach (DictionaryEntry entry in (IDictionary)Get(PhoneSync, "_phones"))
            {
                var b = entry.Value; var calling = (PlayMakerFSM)Get(b, "Calling"); var bill = Get(b, "Bill") as PlayMakerFSM;
                rows.Add("phone|" + entry.Key + "|" + calling.gameObject.activeInHierarchy + "|" + calling.enabled + "|" + calling.ActiveStateName + "|" + calling.FsmVariables.FindFsmString("Number").Value + "|" + Get(b, "Selected"));
                if (bill != null) rows.Add("phone-bill|" + entry.Key + "|" + bill.FsmVariables.FindFsmBool("PhonePaid").Value + "|" + bill.FsmVariables.FindFsmFloat("Connects").Value.ToString("R", CultureInfo.InvariantCulture) + "|" + bill.FsmVariables.FindFsmFloat("Minutes").Value.ToString("R", CultureInfo.InvariantCulture));
            }
            AdvertSnapshot(rows);
        }
    }
}
