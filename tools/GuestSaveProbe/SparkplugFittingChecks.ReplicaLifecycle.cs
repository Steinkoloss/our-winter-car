using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class SparkplugFittingChecks
    {
        private static void RunGuestLifecycle(Action<string, Action> check, List<ReplacementPartState> snapshots)
        {
            Require(snapshots.Count == 16, "Host lifecycle snapshots are incomplete.");
            using (var f = new LifecycleFixture())
            {
                var b = f.Base; var session = SessionManager.Instance!;
                Property(session, "IsHost", false); Property(session, "State", SessionState.Connected); Property(session, "LocalPlayerId", (byte)1);
                var bridge = Get(b.Sync, "_bridge");
                var vehicles = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.VehicleWorldSync", true), Members, null, new[] { bridge, b.Sync }, null);
                var fsms = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.FsmWorldSync", true), Members, null, new[] { bridge, vehicles }, null);
                Call(b.Sync, "BindVehicles", vehicles); Call(bridge, "BindItems", b.Sync); Call(bridge, "BindFsms", fsms);
                var rows = ReadToolRows("sparkplug-lifecycle-probe.json");
                NativeBagPartChecks.LoadActions(b.Data, NativeBagPartChecks.Find(rows, "SPRKPLUG0", "Data"), "Init", "Status");
                b.Data.gameObject.tag = "PART"; b.Data.gameObject.layer = 19;
                b.Pick.isTrigger = false; b.Pick.enabled = true;
                foreach (var template in new[] { b.Data, b.Screw }) foreach (var state in template.Fsm.States) state.SaveActions();
                b.Data.gameObject.SetActive(false);
                var references = new List<FsmGameObject>(b.Installer.FsmVariables.GameObjectVariables) { new FsmGameObject { Name = "VINP", UseVariable = true } };
                b.Installer.FsmVariables.GameObjectVariables = references.ToArray();
                Set(b.Factory, "Fsm", b.Installer); Set(b.Factory, "Prefab", b.Data.gameObject); Set(b.Factory, "TemplateData", b.Data);
                var rule = (ReplacementPartRule)Get(b.Rule, "Identity");
                ((IDictionary)Get(b.Sync, "_replacementFactories")).Add(rule.FactoryId, b.Factory);
                ((IDictionary)Get(b.Sync, "_replacementParts")).Remove(f.Id);
                Call(Get(b.Factory, "Suppressor"), "Suppress", b.Installer);
                PlayMakerFSM? replica = null; Rigidbody? body = null;
                try
                {
                    for (int i = 0; i < snapshots.Count; i++)
                    {
                        int index = i, slot = i / 4 + 1;
                        var state = snapshots[i];
                        check("sparkplug replica lifecycle: socket " + slot + " applies host " + (i % 4 == 0 ? "loose" : i % 4 == 1 ? "fitted" : i % 4 == 2 ? "tightened" : "removed") + " state", () =>
                        {
                            Call(b.Sync, "OnReplacementPartState", state); Set(b.Sync, "_nextReplacementPoll", 0f); Call(b.Sync, "ProcessReplacementParts", session);
                            var bindings = (IDictionary)Get(b.Sync, "_replacementParts"); Require(bindings.Contains(f.Id), "Guest did not materialize the host plug.");
                            var binding = bindings[f.Id]; var data = (PlayMakerFSM)Get(binding, "Data");
                            if (replica == null) { replica = data; body = data.GetComponent<Rigidbody>(); }
                            Require(data == replica && body != null && data.GetComponent<Rigidbody>() == body
                                && (uint)Get(binding, "AppliedRevision") == state.Revision && (bool)Get(binding, "HasAppliedState"), "State recreated or failed to update its replica.");
                            Require((bool)Get(binding, "RemovalValidated") && !(bool)Get(binding, "ToolScrewFailed")
                                && Get(binding, "ToolScrew") != null, "Cloned native controls did not bind.");
                            Require(data.FsmVariables.FindFsmFloat("Wear").Value == state.Scalars[0]
                                && data.FsmVariables.FindFsmFloat("Tightness").Value == state.Scalars[1]
                                && data.FsmVariables.FindFsmFloat("Durability").Value == state.Scalars[2], "Replica lost host condition.");
                            var pick = data.GetComponent<BoxCollider>(); var items = (IDictionary)Get(b.Sync, "_items");
                            if (state.Installed)
                            {
                                Require(body!.isKinematic && data.transform.parent == b.Mounts[slot - 1].transform && !items.Contains(f.Id)
                                    && data.gameObject.layer == 12 && pick.isTrigger && (data.transform.localPosition - new Vector3(state.LocalPosition.X, state.LocalPosition.Y, state.LocalPosition.Z)).sqrMagnitude < .000001f,
                                    "Fitted replica retained loose physics or missed its socket pose.");
                            }
                            else
                            {
                                Require(!body!.isKinematic && body.detectCollisions && data.transform.parent == null && items.Contains(f.Id)
                                    && data.gameObject.tag == "PART" && data.gameObject.layer == 19 && pick.enabled && !pick.isTrigger,
                                    "Loose replica did not restore pickup physics.");
                                var item = items[f.Id]; Require(!(bool)Get(item, "LocallyOwned") && (byte)Get(item, "RemoteOwner") == byte.MaxValue,
                                    "Removed replica inherited its previous ownership.");
                                // The next fitting update must end this simulated local pickup.
                                if (index % 4 == 0) Set(item, "LocallyOwned", true);
                            }
                            Require(Mathf.Abs(f.Mass.Value - 100) < .0001f && !b.Mounts[slot - 1].FsmVariables.FindFsmBool("Installed").Value,
                                "Replica executed host-only mount physics.");
                        });
                        if (i % 4 == 3)
                        {
                            var old = snapshots[i - 2];
                            check("sparkplug replica lifecycle: socket " + slot + " ignores a delayed fitted state after removal", () =>
                            {
                                Call(b.Sync, "OnReplacementPartState", old); Set(b.Sync, "_nextReplacementPoll", 0f); Call(b.Sync, "ProcessReplacementParts", session);
                                Require(replica != null && replica.transform.parent == null && !body!.isKinematic
                                    && ((ReplacementPartReplica)Get(b.Sync, "_replacementReplica")).Get(f.Id)!.Revision == state.Revision,
                                    "Delayed packet refitted the removed plug.");
                            });
                        }
                    }
                }
                finally
                {
                    var bindings = (IDictionary)Get(b.Sync, "_replacementParts");
                    if (bindings.Contains(f.Id)) UnityEngine.Object.DestroyImmediate(((PlayMakerFSM)Get(bindings[f.Id], "Data")).gameObject);
                    Call(Get(b.Factory, "Suppressor"), "Restore");
                }
            }
        }
    }
}
