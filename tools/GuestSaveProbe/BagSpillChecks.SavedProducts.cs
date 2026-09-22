using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class BagSpillChecks
    {
        private static void RunSavedProductChecks(Action<string, Action> check, object sync, List<GameObject> roots)
        {
            var previous = SessionManager.Instance;
            var owner = Root(roots, "saved product session");
            var session = owner.AddComponent<SessionManager>(); session.enabled = false;
            var instance = typeof(SessionManager).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var host = typeof(SessionManager).GetProperty("IsHost");
            var items = (IDictionary)Get(sync, "_items");
            var manifests = (Dictionary<long, ItemSpawn>)Get(sync, "_hostSpawnManifests");
            var lifecycle = (ItemSpawnLifecycle)Get(sync, "_spawnLifecycle");
            float discovery = (float)Get(sync, "_nextBagSpillDiscovery");
            var bodies = new List<Rigidbody>(); var uses = new List<PlayMakerFSM>();
            Func<List<ItemSpawn>> replay = () => new List<ItemSpawn>((IEnumerable<ItemSpawn>)Call(sync, "BuildSavedProductManifests"));
            try
            {
                instance.SetValue(null, session, null); host.SetValue(session, true, null);
                Set(sync, "_nextBagSpillDiscovery", float.MaxValue);
                for (uint i = 1; i <= 2; i++)
                {
                    var obj = Root(roots, "potato chips(itemx)"); var body = obj.AddComponent<Rigidbody>(); body.useGravity = false;
                    obj.transform.position = new Vector3(i * 5, 1, 2); bodies.Add(body);
                    var use = MakeFsm(obj, "Use", "Idle"); uses.Add(use);
                    use.FsmVariables.StringVariables = new[] { new FsmString { Name = "ID", Value = "chips" + i } };
                    use.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Consumed", Value = false } };
                    obj.SetActive(true); use.Fsm.Init(use); use.enabled = true; use.Fsm.Start();
                    var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                    Set(item, "Body", body); Set(item, "Id", i); items.Add(i, item);
                }
                check("saved products: same-named native items have separate replay identities and current poses", () =>
                {
                    var rows = replay(); Require(rows.Count == 2, "Saved products missing or collapsed.");
                    var ids = new HashSet<uint>();
                    foreach (var row in rows)
                    {
                        var decoded = (ItemSpawn)PacketCodec.Decode(PacketCodec.Encode(row));
                        Require(decoded.IsReplay && decoded.Epoch == 0 && decoded.Items.Count == 1
                            && decoded.Items[0].NetId == decoded.ContainerNetId && ids.Add(decoded.Items[0].NetId)
                            && decoded.Items[0].Position.X == decoded.ContainerNetId * 5, "Invalid saved replay identity or pose.");
                    }
                });
                check("saved products: live spill descriptors are not emitted a second time", () =>
                {
                    var live = new ItemSpawn(); live.Items.Add(new ItemSpawn.Entry { NetId = 1 }); manifests.Add(9, live);
                    try { var rows = replay(); Require(rows.Count == 1 && rows[0].Items[0].NetId == 2, "Live item replay duplicated."); }
                    finally { manifests.Remove(9); }
                });
                check("saved products: native drinks without a Consumed bool still replay", () =>
                {
                    var original = uses[1].FsmVariables.BoolVariables;
                    uses[1].FsmVariables.BoolVariables = new FsmBool[0];
                    try { Require(replay().Count == 2, "Native drink shape was excluded."); }
                    finally { uses[1].FsmVariables.BoolVariables = original; }
                });
                check("saved products: consumed retired and uninitialized native items are excluded", () =>
                {
                    uses[0].FsmVariables.FindFsmBool("Consumed").Value = true; lifecycle.Retire(2);
                    Require(replay().Count == 0, "Retired or consumed item resurrected.");
                    lifecycle.Clear(); uses[0].FsmVariables.FindFsmBool("Consumed").Value = false;
                    foreach (var use in uses) use.FsmVariables.FindFsmString("ID").Value = string.Empty;
                    Require(replay().Count == 0, "Uninitialized native ID was published.");
                });
                check("saved products: guests cannot reconstruct host manifests", () =>
                { host.SetValue(session, false, null); Require(replay().Count == 0, "Guest authored saved world products."); });
            }
            finally
            {
                items.Remove((uint)1); items.Remove((uint)2); lifecycle.Clear();
                Set(sync, "_nextBagSpillDiscovery", discovery); instance.SetValue(null, previous, null);
            }
        }
    }
}
