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
        private static FleaListingState? _savedFleaListings;
        private static IDictionary FleaItems => (IDictionary)Get(Get(WorldSyncManager.Instance!, "_items"), "_items");
        private static bool FleaListingCommand(string[] args, List<string> rows)
        {
            var s = SessionManager.Instance!;
            switch (args[1])
            {
                case "flea-table":
                    Player!.position = FleaLogic.transform.position + new Vector3(0, 0, 1);
                    return true;
                case "flea-chips":
                    if (!s.IsHost) throw new InvalidOperationException("Host factory fixture only.");
                    Enter(Find("Spawner/CreateItems", "Chips"), "Create product");
                    rows.Add("flea-fixture|native Chips factory invoked; placement occurs separately after identity initialization"); return true;
                case "flea-place":
                    uint id = uint.Parse(args[2]); var item = FleaItems[id];
                    var body = (Rigidbody)Get(item, "Body");
                    body.transform.parent = null; body.gameObject.layer = 19; body.isKinematic = false;
                    body.position = FleaLogic.GetComponent<BoxCollider>().bounds.center + new Vector3(float.Parse(args[3], CultureInfo.InvariantCulture), 0, 0);
                    body.transform.position = body.position;
                    body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero;
                    body.WakeUp();
                    rows.Add("flea-fixture|placed shared item in native table trigger using body movement"); return true;
                case "flea-price":
                    var pricing = Find("Sheets/KirpputoriSticker/Price", "Logic");
                    if (!pricing.transform.parent.gameObject.activeSelf) throw new InvalidOperationException("Price sheet was not opened by gameplay.");
                    pricing.FsmVariables.FindFsmString("Number").Value = args[2]; Enter(pricing, "Convert");
                    pricing.SendEvent("ENT"); return true;
                case "flea-cancel": Enter(Find("Sheets/KirpputoriSticker/Price", "Logic"), "Cancel"); return true;
                case "flea-scan": Call(Get(WorldSyncManager.Instance!, "_items"), "ScanItems"); return true;
                case "flea-cache":
                    if (!s.IsHost) throw new InvalidOperationException("Host snapshot fixture only.");
                    _savedFleaListings = (FleaListingState)Call(FleaCoordinator, "BuildListingSnapshot"); return true;
                case "flea-replay":
                    if (!s.IsHost || _savedFleaListings == null) throw new InvalidOperationException("Host cached snapshot required.");
                    s.SendWorldMessage(_savedFleaListings, Channel.ReliableOrdered); return true;
                case "flea-motion":
                    if (s.IsHost) throw new InvalidOperationException("Guest stale movement fixture only.");
                    s.SendWorldMessage(new ItemTransform { ItemId = uint.Parse(args[2]), OwnerPlayerId = s.LocalPlayerId,
                        Sequence = 5000, Flags = ItemTransform.FlagFinal, Position = new NetVector3(1, 2, 3) }, Channel.ReliableOrdered); return true;
                case "flea-despawn":
                    if (s.IsHost) throw new InvalidOperationException("Guest despawn fixture only.");
                    s.SendWorldMessage(new ItemDespawn { ItemId = uint.Parse(args[2]) }, Channel.ReliableOrdered); return true;
                case "flea-list-request":
                    var request = new FleaListingIntent { PlayerId = s.LocalPlayerId, ItemId = uint.Parse(args[2]), Price = ushort.Parse(args[3]),
                        Sequence = ushort.Parse(args[4]), Revision = uint.Parse(args[5]) };
                    if (s.IsHost) Call(FleaCoordinator, "TryAcceptListing", request);
                    else s.SendWorldMessage(request, Channel.ReliableOrdered);
                    return true;
                case "flea-sell":
                    if (!s.IsHost) throw new InvalidOperationException("Host sale fixture only.");
                    FleaLogic.FsmVariables.FindFsmString("ItemName").Value = args[2];
                    FleaLogic.FsmVariables.FindFsmFloat("Price").Value = 999;
                    FleaLogic.SendEvent("SELL");
                    rows.Add("flea-fixture|native SELL event; callback must use the saved listing price and exact identity"); return true;
                case "flea-expire":
                    if (!s.IsHost) throw new InvalidOperationException("Host rental expiry fixture only.");
                    FleaLogic.FsmVariables.FindFsmInt("RentDays").Value = -1; FleaLogic.SendEvent("RESET"); return true;
                default: return false;
            }
        }
        private static void FleaListingSnapshot(List<string> rows)
        {
            var s = SessionManager.Instance!;
            var state = s.IsHost ? Call(FleaCoordinator, "BuildListingSnapshot") as FleaListingState
                : Get(FleaCoordinator, "_listingReceived") as FleaListingState;
            rows.Add("flea-listings|" + (state == null ? "none" : state.Revision.ToString()));
            if (state != null) foreach (var e in state.Items) rows.Add("flea-listing|" + e.ItemId + "|" + e.NativeNumber + "|" + e.Price);
            var p = Find("Sheets/KirpputoriSticker/Price", "Logic");
            rows.Add("flea-pricing|" + p.transform.parent.gameObject.activeSelf + "|" + p.ActiveStateName);
            foreach (var st in p.FsmStates) if (st.Name == "Set price")
                foreach (var a in st.Actions) rows.Add("flea-price-action|" + a.GetType().Name);
            var box = FleaLogic.GetComponent<BoxCollider>();
            var factory = Find("Spawner/CreateItems", "Chips");
            var created = factory.FsmVariables.FindFsmGameObject("New").Value;
            rows.Add("flea-factory|" + factory.enabled + "|" + factory.gameObject.activeInHierarchy + "|" + factory.ActiveStateName + "|" + (created == null ? "none" : created.name + ":" + created.activeInHierarchy));
            rows.Add("flea-table-bounds|" + box.bounds.center + "|" + box.bounds.size);
            foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = obj as PlayMakerFSM;
                if (f == null || f.FsmName != "Use" || !f.Fsm.Initialized) continue;
                string id = f.FsmVariables.FindFsmString("ID")?.Value ?? "";
                if (id.StartsWith("chips", StringComparison.Ordinal)) rows.Add("flea-native-product|" + id + "|" + f.FsmVariables.FindFsmBool("Consumed")?.Value);
            }
            foreach (DictionaryEntry pair in FleaItems)
            {
                var item = pair.Value; var body = Get(item, "Body") as Rigidbody;
                if (body == null || body.name != "potato chips(itemx)") continue;
                string native = ""; foreach (var f in body.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Use") native = f.FsmVariables.FindFsmString("ID")?.Value ?? "";
                rows.Add("flea-chip|" + pair.Key + "|" + native + "|" + body.position + "|" + body.velocity.magnitude
                    + "|" + body.isKinematic + "|" + body.gameObject.layer + "|" + Get(item, "RemoteOwner") + "|" + Get(item, "LocallyOwned"));
            }
        }
    }
}
