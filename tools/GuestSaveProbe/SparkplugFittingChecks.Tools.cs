using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class SparkplugFittingChecks
    {
        internal static void RunTools(Action<string, Action> check)
        {
            var savedSession = SessionManager.Instance;
            var globals = FsmVariables.GlobalVariables; var savedBools = globals.BoolVariables; var savedFloats = globals.FloatVariables;
            var repair = globals.FindFsmBool("RepairMode"); bool savedRepair = repair?.Value ?? false;
            var size = globals.FindFsmFloat("ToolWrenchSize"); float savedSize = size?.Value ?? 0;
            using (var f = new Fixture())
            {
                try
                {
                    var session = f.Root.AddComponent<SessionManager>(); session.enabled = false;
                    Property(session, "IsHost", true); Property(session, "State", SessionState.Hosting); Property(session, "LocalPlayerId", (byte)0);
                    typeof(SessionManager).GetProperty("Instance", Static).GetSetMethod(true).Invoke(null, new object[] { session });
                    var player = new RemotePlayer { PlayerId = 1, Peer = new PeerId(445), LastTransformTime = Time.unscaledTime };
                    ((IDictionary)Get(session, "_playersByPeer")).Add(player.Peer, player);
                    var partRows = ReadToolRows("sparkplug-fitting-probe.json");
                    var parent = NativeBagPartChecks.MakeFsm(f.Root, NativeBagPartChecks.Find(partRows, "SPRKPLUG0", "Data"));
                    parent.FsmVariables.FindFsmString("ID").Value = "VIN11101";
                    parent.FsmVariables.FindFsmString("UTAssemblyID").Value = "VIN11101AID"; parent.FsmVariables.FindFsmString("UTPos").Value = "VIN11101POS";
                    parent.FsmVariables.FindFsmInt("AssemblyID").Value = 1;
                    if (!parent.Fsm.Initialized) parent.Fsm.Init(parent); NativeBagPartChecks.Start(parent);
                    PartIdentity.TryItemId("VIN11101", out uint parentId); ((IDictionary)Get(f.Sync, "_nativeParts")).Add(parentId, parent);
                    PartIdentity.TryItemId("SPRKPLUG071", out uint id); ((IDictionary)Get(f.Sync, "_replacementParts")).Add(id, f.Part);
                    var rule = Get(f.Rule, "ToolScrew"); object? control = null;
                    Action<float> seed = tight => { f.Fitted(1, tight); f.Mounts[0].FsmVariables.FindFsmInt("AssemblyID").Value = 1; player.Position = f.Data.transform.position; player.LastTransformTime = Time.unscaledTime;
                        if (control != null) Set(control, "NextRequestAt", 0f); };
                    Func<ReplacementPartState> state = () => (ReplacementPartState)Call(f.Sync, "BuildReplacementPartState", id)!;
                    uint sequence = 0;
                    Func<PartFitOperation, PartFitRequest> request = operation => new PartFitRequest { PlayerId = 1, Token = 445,
                        Sequence = ++sequence, ItemId = id, ExpectedRevision = state().Revision, Operation = operation };
                    Func<PartFitRequest, PartFitStatus> execute = r =>
                    {
                        Call(f.Sync, "OnHostPartFit", r, (byte)1);
                        var status = ((PartFitLedger)Get(f.Sync, "_partFitLedger")).Inspect(r, 1, out _)!.Status;
                        return status;
                    };
                    check("sparkplug tool: native screw graph and host control bind", () =>
                    {
                        seed(4); CallStatic("ValidatePartToolScrew", f.Screw, f.Data, rule); control = Call(f.Sync, "GetPartToolScrew", f.Part);
                        Require(control != null && ReferenceEquals(Get(control, "Pick"), f.Pick), "Native tool control did not bind.");
                    });
                    for (int i = 1; i <= 4; i++)
                    {
                        int slot = i;
                        check("sparkplug tool: host turn in socket " + slot + " updates only that plug and replay does not turn again", () =>
                        {
                            f.Fitted(slot, 3); f.Mounts[slot - 1].FsmVariables.FindFsmInt("AssemblyID").Value = slot; player.Position = f.Data.transform.position; player.LastTransformTime = Time.unscaledTime; Set(control!, "NextRequestAt", 0f);
                            var r = request(PartFitOperation.ToolTighten); Require(execute(r) == PartFitStatus.Accepted, "Host turn was refused.");
                            Require(f.Data.FsmVariables.FindFsmFloat("Tightness").Value == 4 && f.Mounts[slot - 1].FsmVariables.FindFsmFloat("Tightness").Value == 4
                                && f.Data.FsmVariables.FindFsmFloat("Durability").Value == .75f, "Native part/mount condition disagreed.");
                            for (int repeat = 0; repeat < 3; repeat++) Require(execute(r) == PartFitStatus.Accepted, "Retry lost its accepted receipt.");
                            Require(f.Data.FsmVariables.FindFsmFloat("Tightness").Value == 4, "Retried turn mutated tightness.");
                        });
                    }
                    check("sparkplug tool: host repair mode does not block a guest wrench", () =>
                    { seed(3); f.Pick.enabled = false; Require(execute(request(PartFitOperation.ToolLoosen)) == PartFitStatus.Accepted && f.Data.FsmVariables.FindFsmFloat("Tightness").Value == 2, "Host pick visibility blocked a guest."); });
                    check("sparkplug tool: stale negative pose scratch cannot defeat native limits", () =>
                    {
                        seed(8); f.Screw.FsmVariables.FindFsmFloat("Tightness").Value = -.02f;
                        Require(execute(request(PartFitOperation.ToolLoosen)) == PartFitStatus.Accepted && f.Data.FsmVariables.FindFsmFloat("Tightness").Value == 7, "Stale pose scratch corrupted the turn.");
                    });
                    check("sparkplug tool: zero and eight refuse turns beyond their limits", () =>
                    {
                        foreach (int value in new[] { 0, 8 }) { seed(value); Require(execute(request(value == 0 ? PartFitOperation.ToolLoosen : PartFitOperation.ToolTighten)) == PartFitStatus.Blocked
                            && f.Data.FsmVariables.FindFsmFloat("Tightness").Value == value, "A boundary turn mutated the plug."); }
                    });
                    check("sparkplug tool: stale revisions and distant players cannot turn the plug", () =>
                    {
                        seed(3); var stale = request(PartFitOperation.ToolTighten); stale.ExpectedRevision--;
                        Require(execute(stale) == PartFitStatus.Stale, "Stale turn was accepted.");
                        var far = request(PartFitOperation.ToolTighten); player.Position += new Vector3(20, 0, 0);
                        Require(execute(far) == PartFitStatus.TooFar && f.Data.FsmVariables.FindFsmFloat("Tightness").Value == 3, "Distant turn mutated the plug.");
                    });
                    check("sparkplug tool: rapid new clicks respect the native ratchet cooldown", () =>
                    {
                        seed(3); Require(execute(request(PartFitOperation.ToolTighten)) == PartFitStatus.Accepted, "First turn failed.");
                        Require(execute(request(PartFitOperation.ToolTighten)) == PartFitStatus.Busy && f.Data.FsmVariables.FindFsmFloat("Tightness").Value == 4, "Cooldown was bypassed.");
                    });
                    check("sparkplug tool: another socket occupant prevents mutation", () =>
                    {
                        seed(3); var r = request(PartFitOperation.ToolTighten); f.Mounts[0].FsmVariables.FindFsmGameObject("ActivePart").Value = f.Mounts[1].gameObject;
                        Require(execute(r) != PartFitStatus.Accepted && f.Data.FsmVariables.FindFsmFloat("Tightness").Value == 3, "Another occupant was changed.");
                    });

                    check("sparkplug tool: mismatched socket assembly refuses a host turn", () =>
                    {
                        seed(3); f.Data.FsmVariables.FindFsmInt("AssemblyID").Value = 2;
                        Require(execute(request(PartFitOperation.ToolTighten)) == PartFitStatus.Blocked
                            && f.Data.FsmVariables.FindFsmFloat("Tightness").Value == 3, "Mismatched socket accepted a turn.");
                    });
                    check("sparkplug tool: saved plugs use their native array slot despite stale installer scratch", () =>
                    {
                        f.Fitted(3, 3); f.Mounts[2].FsmVariables.FindFsmInt("AssemblyID").Value = 1;
                        player.Position = f.Data.transform.position; player.LastTransformTime = Time.unscaledTime; Set(control!, "NextRequestAt", 0f);
                        Require(execute(request(PartFitOperation.ToolTighten)) == PartFitStatus.Accepted, "Saved socket depended on installer scratch.");
                    });
                    foreach (string field in new[] { "layer", "addValue" })
                    {
                        string changed = field;
                        check("sparkplug tool: changed native " + changed + " is rejected", () =>
                        {
                            var action = State(f.Screw, changed == "layer" ? "Set" : "Screw 2").Actions[changed == "layer" ? 0 : 1];
                            var original = Get(action, changed);
                            try { Set(action, changed, changed == "layer" ? (object)19 : new FsmFloat(2)); RejectTool(() => CallStatic("ValidatePartToolScrew", f.Screw, f.Data, rule)); }
                            finally { Set(action, changed, original); }
                        });
                    }

                    var tools = ReadToolRows("sparkplug-tool-probe.json"); string path = (string)Get(rule, "ToolPath");
                    var toolObject = Child(f.Root, "tool"); var toolRow = NativeBagPartChecks.Find(tools, path, "Check");
                    var rayRow = NativeBagPartChecks.Find(tools, path, "Raycast");
                    var tool = NativeBagPartChecks.MakeFsm(toolObject, toolRow); var ray = NativeBagPartChecks.MakeFsm(toolObject, rayRow);
                    if (size == null) { size = new FsmFloat { Name = "ToolWrenchSize", UseVariable = true }; var a = new List<FsmFloat>(savedFloats) { size }; globals.FloatVariables = a.ToArray(); }
                    if (repair == null) { repair = new FsmBool { Name = "RepairMode", UseVariable = true }; var a = new List<FsmBool>(savedBools) { repair }; globals.BoolVariables = a.ToArray(); }
                    var localFloats = new List<FsmFloat>(tool.FsmVariables.FloatVariables) { size }; tool.FsmVariables.FloatVariables = localFloats.ToArray();
                    NativeBagPartChecks.LoadActions(tool, toolRow, "Check bolt Name", "Sparkplug tool?", "Tighten", "Untighten", "Tighten 2", "Untighten 2");
                    NativeBagPartChecks.LoadActions(ray, rayRow, "State 1");
                    foreach (string boundary in new[] { "Ratchet?", "Wait bolt", "Delay", "Delay 2" }) State(tool, boundary).Transitions = new FsmTransition[0];
                    NativeBagPartChecks.Start(tool); NativeBagPartChecks.Start(ray);
                    check("sparkplug tool: native tool-size gate, root pick and both wrench sends validate", () => CallStatic("ValidateSparkplugTool", tool, ray, rule));
                    check("sparkplug tool: wrong wrench-size binding is rejected", () =>
                    {
                        var compare = State(tool, "Sparkplug tool?").Actions[0]; var original = Get(compare, "float1");
                        try { Set(compare, "float1", new FsmFloat(.8f)); RejectTool(() => CallStatic("ValidateSparkplugTool", tool, ray, rule)); }
                        finally { Set(compare, "float1", original); }
                    });
                    check("sparkplug tool: persistent name is recognized through the native size gate", () =>
                    {
                        seed(3); f.Data.gameObject.name = "spark plug(SPRKPLUG071)"; tool.FsmVariables.FindFsmGameObject("Bolt").Value = f.Data.gameObject; size.Value = .55f;
                        Call(f.Sync, "BindSparkplugTool", tool, ray, rule); NativeBagPartChecks.Fire(tool, "Check bolt Name");
                        Require(tool.ActiveStateName == "Ratchet?", "Persistent part identity was not recognized.");
                        size.Value = .8f; NativeBagPartChecks.Fire(tool, "Check bolt Name"); Require(tool.ActiveStateName == "Wait bolt", "Identity recognition bypassed the tool size.");
                    });
                    check("sparkplug tool: local size guard agrees with native tolerance boundaries", () =>
                    {
                        foreach (float candidate in new[] { .52999f, .53f, .55f, .57f, .57001f })
                        {
                            size.Value = candidate; NativeBagPartChecks.Fire(tool, "Check bolt Name");
                            Require((tool.ActiveStateName == "Ratchet?") == PartToolScrewPolicy.MatchesTool(candidate), "Native wrench tolerance disagreed at " + candidate);
                        }
                    });
                    check("sparkplug tool: unrelated renamed object is not recognized as a registered plug", () =>
                    {
                        tool.FsmVariables.FindFsmGameObject("Bolt").Value = f.Prerequisite.gameObject; size.Value = .55f;
                        NativeBagPartChecks.Fire(tool, "Check bolt Name"); Require(tool.ActiveStateName == "Wait bolt", "Unregistered target was recognized.");
                    });

                    seed(3); var authoritative = state();
                    Property(session, "IsHost", false); Property(session, "State", SessionState.Connected); Property(session, "LocalPlayerId", (byte)1);
                    Set(f.Part, "Replica", true); Set(f.Part, "ToolScrew", null); Set(f.Part, "FittedPresentation", true);
                    var replica = new ReplacementPartReplica(new[] { ((WinterMP.Net.Sync.ReplacementPartRule)Get(f.Rule, "Identity")) }, new ItemSpawnLifecycle());
                    Require(replica.Receive(authoritative, out _), "Replica state fixture was rejected."); Set(f.Sync, "_replacementReplica", replica);
                    Set(f.Part, "AppliedRevision", authoritative.Revision); Set(f.Part, "HasAppliedState", true);
                    Call(Get(f.Factory, "Suppressor"), "Suppress", f.Prerequisite);
                    f.Data.gameObject.layer = 19;
                    control = Call(f.Sync, "GetPartToolScrew", f.Part); Require(control != null, "Replica tool control did not bind.");
                    check("sparkplug tool: preparing its safe graph preserves the loose collider setting", () =>
                    { Require(f.Pick.enabled, "Control binding changed the collider before loose presentation was captured."); });
                    repair.Value = true; size.Value = .55f; tool.FsmVariables.FindFsmGameObject("Bolt").Value = f.Data.gameObject;
                    Call(f.Sync, "UpdateReplicaToolScrew", f.Part);
                    check("sparkplug tool: fitted replica exposes only a guarded layer-12 trigger", () =>
                    { Require(f.Pick.enabled && f.Pick.isTrigger && f.Data.gameObject.layer == 12 && f.Screw.Fsm.States.Length == 3, "Replica tool graph or pick was unsafe."); });
                    foreach (string entry in new[] { "Tighten", "Untighten", "Tighten 2", "Untighten 2" })
                    {
                        string turn = entry;
                        check("sparkplug tool: native " + turn + " creates an intent without a guest scalar write", () =>
                        {
                            Set(f.Sync, "_partFitClient", null); Set(control!, "NextRequestAt", 0f); Call(f.Sync, "UpdateReplicaToolScrew", f.Part);
                            NativeBagPartChecks.Fire(tool, turn);
                            var client = (PartFitClient)Get(f.Sync, "_partFitClient");
                            Require(client != null && client.Pending && client.Operation == (turn.StartsWith("Tighten", StringComparison.Ordinal) ? PartFitOperation.ToolTighten : PartFitOperation.ToolLoosen)
                                && f.Data.FsmVariables.FindFsmFloat("Tightness").Value == 3 && f.Mounts[0].FsmVariables.FindFsmFloat("Tightness").Value == 3
                                && !f.Pick.enabled, "Native tool mutated guest data or failed to create its intent.");
                        });
                    }
                    check("sparkplug tool: pending host state disables the replica pick", () =>
                    {
                        Set(f.Sync, "_partFitClient", null); ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Add(id); Call(f.Sync, "UpdateReplicaToolScrew", f.Part);
                        Require(!f.Pick.enabled, "Unapplied state remained interactive."); ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Remove(id);
                    });
                    check("sparkplug tool: leaving repair mode disables the pick", () =>
                    { repair.Value = false; Call(f.Sync, "UpdateReplicaToolScrew", f.Part); Require(!f.Pick.enabled, "Repair mode did not gate tool input."); });
                    check("sparkplug tool: loose presentation restores the original layer", () =>
                    {
                        Set(f.Part, "FittedPresentation", false); Call(f.Sync, "UpdateReplicaToolScrew", f.Part);
                        Require(!f.Screw.enabled && f.Data.gameObject.layer == 19, "Loose replica kept its tool FSM or fitted layer.");
                    });
                    check("sparkplug tool: failed controls stay disabled on later refreshes", () =>
                    {
                        Set(f.Part, "FittedPresentation", true); repair.Value = true; Set(f.Part, "ToolScrewFailed", true);
                        Call(f.Sync, "UpdateReplicaToolScrew", f.Part); Require(!f.Screw.enabled && !f.Pick.enabled, "Failed control was re-enabled.");
                    });
                    check("sparkplug tool: cleanup removes only the identity recognition hook", () =>
                    {
                        Call(f.Sync, "ClearPartToolScrews"); Require(State(tool, "Check bolt Name").Actions.Length == 3, "Tool recognition hook was retained.");
                    });
                }
                finally
                {
                    if (size != null) size.Value = savedSize; if (repair != null) repair.Value = savedRepair;
                    globals.BoolVariables = savedBools; globals.FloatVariables = savedFloats;
                    typeof(SessionManager).GetProperty("Instance", Static).GetSetMethod(true).Invoke(null, new object?[] { savedSession });
                }
            }
        }

        private static void Property(object target, string name, object value) => target.GetType().GetProperty(name, Members).GetSetMethod(true).Invoke(target, new[] { value });
        private static List<object> ReadToolRows(string name)
        {
            var type = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var reader = Activator.CreateInstance(type, Members, null, new object[] { File.ReadAllText(Path.Combine(Application.dataPath, "../" + name)) }, null);
            return (List<object>)((Dictionary<string, object>)type.GetMethod("ReadObject", Members).Invoke(reader, null))["fsms"];
        }
        private static void RejectTool(Action action)
        {
            try { action(); } catch (System.Reflection.TargetInvocationException e) { if (e.InnerException is InvalidOperationException) return; throw; }
            throw new InvalidOperationException("Changed native tool binding was accepted.");
        }
    }
}
