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
        private sealed class WireConnection
        {
            internal EngineWireData Rule = null!;
            internal PlayMakerFSM Data = null!, Prerequisite = null!, Status = null!;
            internal GameObject Mesh = null!, Triggers = null!, Tool = null!;
            internal PlayMakerFSM[] Ends = new PlayMakerFSM[0];
            internal FsmState[] Finish = new FsmState[0];
            internal FsmStateAction[][] Originals = new FsmStateAction[0][];
            internal readonly List<FsmSuppressor> Pauses = new List<FsmSuppressor>();
            internal FsmBool EndpointReady = new FsmBool(), OriginalReady = null!;
            internal FsmStateAction StatusAction = null!;
            internal bool Guest, Failed, Reset, MeshActive, TriggersActive, IgnitionActive;
        }
        private WireConnection? _wireConnection;
        private readonly WiringInstallLedger _wireInstallLedger = new WiringInstallLedger();
        private WiringInstallClient? _wireInstallClient;
        private WiringInstallRequest? _wireInstalling;
        private WiringState? _wireBefore;
        private bool _wireMeshBefore, _wireTriggersBefore, _wireIgnitionBefore, _wireTriggerBefore;
        private int _wireFinishCount;
        private float _wireDeadline, _nextWireBind, _nextWireNotice;

        private static GameObject WireObject(string path)
        {
            int slash = path.IndexOf('/');
            var root = GameObject.Find("/" + (slash < 0 ? path : path.Substring(0, slash)));
            var target = root == null ? null : slash < 0 ? root.transform : root.transform.Find(path.Substring(slash + 1));
            if (target == null || ScenePath.Of(target) != path) throw new InvalidOperationException("Missing wiring object " + path);
            return target.gameObject;
        }

        private static PlayMakerFSM WireFsm(GameObject obj, string name)
        {
            PlayMakerFSM? found = null;
            foreach (var candidate in obj.GetComponents<PlayMakerFSM>())
                if (candidate.FsmName == name)
                {
                    if (found != null) throw new InvalidOperationException("Ambiguous wiring FSM.");
                    found = candidate;
                }
            return found ?? throw new InvalidOperationException("Missing wiring FSM " + name);
        }

        private void BindWireConnection(SessionManager session)
        {
            if (_wireConnection != null || Time.unscaledTime < _nextWireBind) return;
            _nextWireBind = Time.unscaledTime + 1;
            var profile = SyncCatalog.GuestEngineInputs;
            if (profile == null) return;
            foreach (var rule in profile.Wires)
            {
                var c = rule.Connection; if (c == null) continue;
                var data = WireFsm(WireObject(rule.Path), rule.Fsm);
                if (!data.Fsm.Started || data.ActiveStateName != rule.SettledState) return;
                var b = new WireConnection { Rule = rule, Data = data, Guest = !session.IsHost,
                    Mesh = WireObject(c["meshPath"]), Triggers = WireObject(c["triggersPath"]),
                    Prerequisite = WireFsm(WireObject(c["prerequisitePath"]), c["prerequisiteFsm"]),
                    Status = WireFsm(WireObject(c["statusPath"]), c["statusFsm"]) };
                b.Ends = new[] { WireFsm(WireObject(c["triggersPath"] + "/" + c["firstEndpoint"]), c["endpointFsm"]),
                    WireFsm(WireObject(c["triggersPath"] + "/" + c["secondEndpoint"]), c["endpointFsm"]) };
                b.MeshActive = b.Mesh.activeSelf; b.TriggersActive = b.Triggers.activeSelf; b.IgnitionActive = b.Ends[1].gameObject.activeSelf;
                _wireConnection = b;
                b.Tool = FindWiringTool(c["toolPath"]);
                ValidateWireConnection(b);
                b.Finish = new FsmState[2]; b.Originals = new FsmStateAction[2][];
                for (int i = 0; i < 2; i++)
                {
                    var end = b.Ends[i]; var finish = FsmHook.FindState(end, c["finishState"])!;
                    b.Finish[i] = finish; b.Originals[i] = finish.Actions;
                    if (!FsmHook.EnsureRemoteEntry(end, "Sound") || !FsmHook.EnsureRemoteEntry(end, c["resetState"]))
                        throw new InvalidOperationException("Cannot guard native wire installation.");
                    var actions = new List<FsmStateAction> { new FsmHookAction(() => OnWireFinish(b, end)) };
                    actions.AddRange(finish.Actions); finish.Actions = actions.ToArray();
                }
                if (b.Guest)
                {
                    var pause = new FsmSuppressor(); b.Pauses.Add(pause);
                    if (!pause.Suppress(data)) throw new InvalidOperationException("Cannot protect saved guest wire.");
                    // Only this endpoint's visual gate changes. The shared Status
                    // FSM must keep evaluating every other native wiring circuit.
                    b.StatusAction.GetType().GetField("activate").SetValue(b.StatusAction, b.EndpointReady);
                    ApplyWireConnection(b);
                }
                SyncEventLog.Record("wire-connection-bound", rule.Name + " guest=" + b.Guest);
            }
        }

        private bool WireConnectable(WireConnection b)
        {
            var c = b.Rule.Connection!;
            return !b.Failed && b.Data != null && b.Data.enabled && b.Data.Fsm.Started
                && b.Data.ActiveStateName == b.Rule.SettledState && !b.Data.FsmVariables.FindFsmBool("Installed").Value
                && b.Prerequisite != null && b.Prerequisite.enabled && b.Prerequisite.Fsm.Started
                && b.Prerequisite.FsmVariables.FindFsmBool(c["prerequisiteVariable"]).Value;
        }

        private static GameObject FindWiringTool(string path)
        {
            // Pickup reparents the saved tool, so a scene path ceases to identify
            // it. Native Use registers the same persistent object globally.
            var tool = FsmVariables.GlobalVariables.FindFsmGameObject("WiringTool")?.Value;
            string name = path.Substring(path.LastIndexOf('/') + 1);
            if (tool == null || tool.name != name || tool.GetComponent<Rigidbody>() == null
                || WireFsm(tool, "Save").FsmVariables.FindFsmString("UniqueTag")?.Value != name)
                throw new InvalidOperationException("Native wiring tool identity unavailable.");
            WireFsm(tool, "Use");
            return tool;
        }

        private void ApplyWireConnection(WireConnection b)
        {
            var state = _wiringReplica.Get(b.Rule.Id);
            bool installed = !b.Failed && state != null && (state.Flags & WiringState.Installed) != 0;
            bool ready = !b.Failed && state != null && (state.Flags & WiringState.Connectable) != 0;
            b.EndpointReady.Value = ready;
            b.Mesh.SetActive(installed);
            b.Ends[1].gameObject.SetActive(ready);
            b.Triggers.SetActive(ready);
        }

        private void OnWireFinish(WireConnection b, PlayMakerFSM end)
        {
            if (!b.Guest)
            {
                if (_wireInstalling != null && ++_wireFinishCount > 1)
                    FsmHook.FireRemoteEntry(end, b.Rule.Connection!["resetState"]);
                return;
            }
            // A self-transition at the head prevents every native saved write and
            // broadcast in Finish assembly. The two-endpoint handshake stays native.
            FsmHook.FireRemoteEntry(end, b.Rule.Connection!["resetState"]); b.Reset = true;
            var session = SessionManager.Instance; var state = _wiringReplica.Get(b.Rule.Id);
            if (session == null || session.IsHost || session.State != SessionState.Connected || b.Failed
                || state == null || (state.Flags & WiringState.Connectable) == 0) return;
            if (_wireInstallClient == null)
            {
                ulong token = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
                _wireInstallClient = new WiringInstallClient(token == 0 ? 1 : token);
            }
            _wireInstallClient.TryBegin(session.LocalPlayerId, b.Rule.Id, state.Revision);
        }

        private bool GuestCanReachWire(SessionManager session, byte actor, WireConnection b)
        {
            if (b.Tool == null || !b.Tool.activeInHierarchy) return false;
            // The binding survives pickup, but a replaced global, changed saved
            // identity or removed Use FSM must not authorize the cached object.
            try { if (FindWiringTool(b.Rule.Connection!["toolPath"]) != b.Tool) return false; }
            catch (InvalidOperationException) { return false; }
            SyncedItem? tool = null;
            foreach (var item in _items.Values) if (item.Body != null && item.Body.gameObject == b.Tool) { tool = item; break; }
            if (tool == null || tool.LocallyOwned || (tool.RemoteOwner != WorldSyncIds.NoOwner && tool.RemoteOwner != actor)) return false;
            foreach (var end in b.Ends)
                if (GuestNearPackage(session, actor, end.transform.position)
                    && (b.Tool.transform.position - end.transform.position).sqrMagnitude <= .36f) return true;
            return false;
        }

        internal void OnHostWireInstall(WiringInstallRequest request, byte actor)
        {
            var session = SessionManager.Instance; if (session == null || !session.IsHost) return;
            var replay = _wireInstallLedger.Inspect(request, actor, out bool begin);
            if (!begin) { if (replay != null) SendWireReceipt(session, replay); return; }
            try
            {
                var b = _wireConnection; WiringState? state = null;
                foreach (var wire in BuildWiringStates()) if (wire.SourceId == request.SourceId) state = wire;
                var status = WiringInstallLedger.Check(request, state, b != null && !b.Failed && b.Rule.Id == request.SourceId,
                    b != null && GuestCanReachWire(session, actor, b), b != null && WireConnectable(b) && _wireInstalling == null);
                var receipt = _wireInstallLedger.Begin(request, actor, status);
                if (receipt == null) return;
                if (status == WiringInstallStatus.Pending && b != null)
                {
                    _wireBefore = state;
                    _wireMeshBefore = b.Mesh.activeSelf; _wireTriggersBefore = b.Triggers.activeSelf;
                    _wireIgnitionBefore = b.Ends[1].gameObject.activeSelf;
                    _wireTriggerBefore = b.Data.FsmVariables.FindFsmBool("Trigger").Value;
                    _wireFinishCount = 0;
                    _wireInstalling = WiringInstallLedger.Copy(request); _wireDeadline = Time.unscaledTime + 2;
                    // Cancel an incomplete local selection, then run both native
                    // Sound/CLOSELOOP actions. Never jump over the handshake.
                    foreach (var end in b.Ends) FsmHook.FireRemoteEntry(end, b.Rule.Connection!["resetState"]);
                    FsmHook.FireRemoteEntry(b.Ends[0], "Sound");
                    FsmHook.FireRemoteEntry(b.Ends[1], "Sound");
                }
                SendWireReceipt(session, receipt);
            }
            catch (Exception error)
            {
                if (_wireInstalling != null) FinishWireInstall(session, false);
                else
                {
                    var receipt = _wireInstallLedger.Complete(request, false);
                    if (receipt != null) SendWireReceipt(session, receipt);
                }
                WinterMPPlugin.Log.LogWarning("Wire installation failed: " + error.Message);
            }
        }

        private void SendWireReceipt(SessionManager session, WiringInstallReceipt receipt)
        {
            foreach (var state in BuildWiringStates()) if (state.SourceId == receipt.SourceId) session.SendWorldMessage(state, Channel.ReliableOrdered);
            session.SendWorldMessage(receipt, Channel.ReliableOrdered);
            SyncEventLog.Record("wire-install", receipt.PlayerId + ":" + receipt.Sequence + " " + receipt.Status);
        }

        internal void OnWireInstallReceipt(WiringInstallReceipt receipt)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || receipt.PlayerId != session.LocalPlayerId
                || _wireInstallClient == null || !_wireInstallClient.Receive(receipt)) return;
            if (receipt.Status != WiringInstallStatus.Accepted && receipt.Status != WiringInstallStatus.Installed
                && Time.unscaledTime >= _nextWireNotice)
            { _nextWireNotice = Time.unscaledTime + 3; session.AddSystemChat("The wire could not be connected. Move the wiring tool to the connector and try again."); }
        }

        private void ProcessWireConnection(SessionManager session)
        {
            try
            {
                BindWireConnection(session); var b = _wireConnection;
                if (b == null || b.Failed) return;
                if (b.Guest)
                {
                    ApplyWireConnection(b);
                    if (b.Reset)
                    {
                        b.Reset = false;
                        foreach (var end in b.Ends) end.SendEvent(b.Rule.Connection!["resetEvent"]);
                    }
                    var request = _wireInstallClient?.Poll(Time.unscaledTime);
                    if (request != null) session.SendWorldMessage(request, Channel.ReliableOrdered);
                }
                else if (_wireInstalling != null)
                {
                    bool installed = _wireFinishCount == 1 && b.Data.FsmVariables.FindFsmBool("Installed").Value
                        && b.Mesh.activeSelf && !b.Triggers.activeSelf;
                    if (installed || Time.unscaledTime >= _wireDeadline)
                        FinishWireInstall(session, installed);
                }
            }
            catch (Exception error)
            {
                if (_wireInstalling != null) FinishWireInstall(session, false);
                else FailWireConnection(error);
            }
        }

        private void RestoreWireInstall()
        {
            var b = _wireConnection;
            if (b == null || _wireBefore == null) return;
            // Reset both selections before restoring the checkpoint so a late
            // CLOSELOOP cannot turn a timed-out request into a saved installation.
            foreach (var end in b.Ends) FsmHook.FireRemoteEntry(end, b.Rule.Connection!["resetState"]);
            b.Data.FsmVariables.FindFsmBool("Installed").Value = (_wireBefore.Flags & WiringState.Installed) != 0;
            b.Data.FsmVariables.FindFsmBool("Trigger").Value = _wireTriggerBefore;
            b.Mesh.SetActive(_wireMeshBefore); b.Triggers.SetActive(_wireTriggersBefore);
            b.Ends[1].gameObject.SetActive(_wireIgnitionBefore);
        }

        private void FinishWireInstall(SessionManager session, bool accepted)
        {
            var request = _wireInstalling;
            if (request == null) return;
            if (!accepted) RestoreWireInstall();
            var receipt = _wireInstallLedger.Complete(request, accepted);
            _wireInstalling = null; _wireBefore = null;
            if (receipt != null) SendWireReceipt(session, receipt);
        }

        private void FailWireConnection(Exception error)
        {
            var b = _wireConnection;
            if (b != null && b.Failed) return;
            if (b != null)
            {
                b.Failed = true;
                if (b.Guest)
                {
                    // Validation can fail before the normal guest pause is installed.
                    if (b.Pauses.Count == 0)
                    { var pause = new FsmSuppressor(); b.Pauses.Add(pause); pause.Suppress(b.Data); }
                    foreach (var end in b.Ends) { var pause = new FsmSuppressor(); b.Pauses.Add(pause); pause.Suppress(end); }
                    b.EndpointReady.Value = false; b.Triggers.SetActive(false); b.Mesh.SetActive(false);
                }
            }
            WinterMPPlugin.Log.LogWarning("Wire connection unavailable: " + error.Message);
            SyncEventLog.Record("wire-connection-failed", error.Message);
        }

        internal void ForgetWirePlayer(byte actor) => _wireInstallLedger.ForgetPlayer(actor);

        private void ClearWireConnection()
        {
            if (_wireInstalling != null) RestoreWireInstall();
            var b = _wireConnection; _wireConnection = null;
            if (b != null)
            {
                for (int i = 0; i < b.Finish.Length; i++)
                    if (b.Finish[i] != null && b.Originals[i] != null) b.Finish[i].Actions = b.Originals[i];
                if (b.Guest)
                {
                    if (b.StatusAction != null && b.OriginalReady != null)
                        b.StatusAction.GetType().GetField("activate").SetValue(b.StatusAction, b.OriginalReady);
                    if (b.Mesh != null) b.Mesh.SetActive(b.MeshActive);
                    if (b.Ends.Length == 2 && b.Ends[1] != null) b.Ends[1].gameObject.SetActive(b.IgnitionActive);
                    if (b.Triggers != null) b.Triggers.SetActive(b.TriggersActive);
                    foreach (var pause in b.Pauses) pause.Restore();
                    foreach (var end in b.Ends) if (end != null) end.SendEvent(b.Rule.Connection!["resetEvent"]);
                }
            }
            _wireInstallLedger.Clear(); _wireInstallClient = null; _wireInstalling = null; _wireBefore = null;
            _nextWireBind = _nextWireNotice = 0;
        }
    }
}
