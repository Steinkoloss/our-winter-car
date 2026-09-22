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
        private sealed class SausageSource
        {
            internal PlayMakerFSM Fsm = null!;
            internal Collider Trigger = null!;
            internal FsmGameObject Package = null!;
            internal uint Id, Opening;
            internal float NextRequest;
            internal readonly List<Rigidbody> Outputs = new List<Rigidbody>();
        }
        private readonly Dictionary<uint, SausageSource> _sausageSources = new Dictionary<uint, SausageSource>();
        private readonly HashSet<uint> _openedSausagePackages = new HashSet<uint>();
        private readonly Dictionary<byte, uint> _sausageSequences = new Dictionary<byte, uint>();
        private readonly List<Action> _sausageRestore = new List<Action>();
        private uint _sausageSequence;
        private float _sausageDiscovery;

        private void RefreshSausages()
        {
            var c = SyncCatalog.Sausages;
            if (c == null || _sausageFailed || _sausageSources.Count == c.Paths.Count || Time.unscaledTime < _sausageDiscovery) return;
            _sausageDiscovery = Time.unscaledTime + 1;
            try
            {
                foreach (var obj in ScenePath.ScanFsms())
                {
                    var fsm = obj as PlayMakerFSM;
                    if (fsm == null || !fsm.Fsm.Initialized || fsm.FsmName != c["fsm"]) continue;
                    string path = ScenePath.Of(fsm.transform);
                    if (!c.Paths.Contains(path)) continue;
                    uint id = StableHash.Fnv1a32(path);
                    if (_sausageSources.TryGetValue(id, out var bound))
                    { if (bound.Fsm != fsm) throw new InvalidOperationException("Ambiguous sausage trigger."); continue; }
                    var source = new SausageSource { Fsm = fsm, Id = id,
                        Trigger = fsm.GetComponent<Collider>(), Package = fsm.FsmVariables.FindFsmGameObject(c["package"]) };
                    var prefab = fsm.FsmVariables.FindFsmGameObject(c["prefab"])?.Value;
                    if (source.Trigger == null || !source.Trigger.isTrigger || source.Package == null || prefab == null
                        || prefab.name != c["prefabName"] || prefab.GetComponent<Rigidbody>() == null)
                        throw new InvalidOperationException("Native sausage trigger references changed.");
                    if (_sausagePrefab != null && _sausagePrefab != prefab) throw new InvalidOperationException("Sausage triggers disagree on prefab.");
                    ValidateSausagePrefab(prefab, c); _sausagePrefab = prefab;
                    var state = ValidateSausageOpening(fsm, c);
                    if (!FsmHook.EnsureRemoteEntry(fsm, c["idle"]) || !FsmHook.EnsureRemoteEntry(fsm, c["open"]))
                        throw new InvalidOperationException("Cannot guard native sausage opening.");
                    var original = state.Actions;
                    var actions = new List<FsmStateAction> { new FsmHookAction(() => BeginSausageOpening(source)) };
                    actions.Add(original[0]);
                    for (int n = 0; n < 4; n++)
                    {
                        var create = original[1 + n * 2]; var field = create.GetType().GetField("storeObject");
                        var old = field.GetValue(create); var output = new FsmGameObject { Name = "wintermp-sausage-output-" + n, UseVariable = true };
                        field.SetValue(create, output); _sausageRestore.Add(() => field.SetValue(create, old));
                        actions.Add(create);
                        // Vanilla targets the prefab, not the new object. Preserve
                        // the source condition on each clone without contaminating
                        // the next package's first sausage.
                        actions.Add(new FsmHookAction(() => CaptureSausage(source, output)));
                    }
                    actions.Add(original[9]);
                    actions.Add(new FsmHookAction(() => FinishSausageOpening(source)));
                    state.Actions = actions.ToArray(); _sausageRestore.Add(() => state.Actions = original);
                    _sausageSources.Add(id, source);
                    SyncEventLog.Record("sausage-trigger", path);
                }
            }
            catch (Exception e) { FailSausages(e); }
        }

        private static FsmState ValidateSausageOpening(PlayMakerFSM fsm, SausagesData c)
        {
            var state = PackageStateActions(fsm, c["open"], "GetFsmFloat", "CreateObject", "SetFsmFloat", "CreateObject", "SetFsmFloat", "CreateObject", "SetFsmFloat", "CreateObject", "SetFsmFloat", "SendEventByName");
            if (state.Transitions.Length != 1 || state.Transitions[0].EventName != "FINISHED" || state.Transitions[0].ToState != c["idle"])
                throw new InvalidOperationException("Sausage conversion completion changed.");
            foreach (var action in state.Actions)
            {
                var frame = PackageField<object>(action, "everyFrame");
                if (frame is bool each && each || frame is FsmBool flag && flag.Value) throw new InvalidOperationException("Sausage conversion repeats per frame.");
            }
            var read = state.Actions[0];
            if (!FitTargetVariable(PackageField<FsmOwnerDefault>(read, "gameObject"), c["package"])
                || PackageField<FsmString>(read, "fsmName")?.Value != c["use"]
                || PackageField<FsmString>(read, "variableName")?.Value != c["condition"]
                || PackageField<FsmFloat>(read, "storeValue")?.Name != c["condition"])
                throw new InvalidOperationException("Sausage package condition binding changed.");
            for (int n = 0; n < 4; n++)
            {
                var create = state.Actions[1 + n * 2]; var write = state.Actions[2 + n * 2];
                if (PackageField<FsmGameObject>(create, "gameObject")?.Name != c["prefab"]
                    || PackageField<FsmGameObject>(create, "spawnPoint")?.Name != c["package"]
                    || PackageField<FsmGameObject>(create, "storeObject")?.IsNone != true
                    || !FitTargetVariable(PackageField<FsmOwnerDefault>(write, "gameObject"), c["prefab"])
                    || PackageField<FsmString>(write, "fsmName")?.Value != c["use"]
                    || PackageField<FsmString>(write, "variableName")?.Value != c["condition"]
                    || PackageField<FsmFloat>(write, "setValue")?.Name != c["condition"])
                    throw new InvalidOperationException("Native sausage output binding changed.");
            }
            var send = state.Actions[9]; var target = PackageField<FsmEventTarget>(send, "eventTarget");
            if (target == null || target.target != FsmEventTarget.EventTarget.GameObjectFSM || !FitTargetVariable(target.gameObject, c["package"])
                || target.fsmName.Value != c["use"] || target.sendToChildren.Value
                || PackageField<FsmString>(send, "sendEvent")?.Value != "GARBAGE"
                || PackageField<FsmFloat>(send, "delay")?.Value != 0)
                throw new InvalidOperationException("Sausage package consumption changed.");
            return state;
        }

        private bool SausagePackage(SausageSource source, uint id, out SyncedItem item)
        {
            item = null!; var c = SyncCatalog.Sausages!;
            if (!_items.TryGetValue(id, out item) || item.Body == null || item.Body.name != c["packageName"]
                || _spawnLifecycle.IsRetired(id) || _openedSausagePackages.Contains(id) || IsHeldByLocalPlayer(item.Body)
                || !source.Fsm.enabled || !source.Fsm.Fsm.Started || !source.Fsm.gameObject.activeInHierarchy
                || !source.Trigger.enabled || source.Opening != 0) return false;
            var use = SausageUse(item.Body.gameObject, c["use"]);
            float condition = use?.FsmVariables.FindFsmFloat(c["condition"])?.Value ?? float.NaN;
            return use != null && use.enabled && use.Fsm.Started && !string.IsNullOrEmpty(use.FsmVariables.FindFsmString(c["packageId"])?.Value)
                && condition > 1 && condition <= 100;
        }
        private static bool InSausageTrigger(SausageSource source, Rigidbody body)
            => (source.Trigger.bounds.ClosestPoint(body.position) - body.position).sqrMagnitude <= .0625f;

        private void BeginSausageOpening(SausageSource source)
        {
            var c = SyncCatalog.Sausages!; var session = SessionManager.Instance;
            try
            {
                uint id = 0;
                foreach (var pair in _items) if (pair.Value.Body != null && pair.Value.Body.gameObject == source.Package.Value) { id = pair.Key; break; }
                if (!_sausageFailed && session != null && id != 0 && SausagePackage(source, id, out var item) && InSausageTrigger(source, item.Body))
                {
                    if (session.IsHost)
                    {
                        source.Opening = id; source.Outputs.Clear(); _openedSausagePackages.Add(id); return;
                    }
                    if (Time.unscaledTime >= source.NextRequest && (item.RemoteOwner == 255 || item.RemoteOwner == session.LocalPlayerId || item.LocallyOwned))
                    {
                        source.NextRequest = Time.unscaledTime + 1;
                        if (++_sausageSequence == 0) ++_sausageSequence;
                        session.SendWorldMessage(new SausageOpenIntent { SourceId = source.Id, PackageId = id, PlayerId = session.LocalPlayerId, Sequence = _sausageSequence }, Channel.ReliableOrdered);
                    }
                }
            }
            catch (Exception e) { FailSausages(e); }
            FsmHook.FireRemoteEntry(source.Fsm, c["idle"]);
        }
        internal void ForgetSausageIntents(byte actor) => _sausageSequences.Remove(actor);
        internal void OnSausageOpen(SausageOpenIntent intent, byte actor)
        {
            var session = SessionManager.Instance;
            if (session?.IsHost != true || _sausageFailed || !SausagePolicy.Valid(intent) || intent.PlayerId != actor) return;
            try
            {
                _sausageSequences.TryGetValue(actor, out uint previous);
                if (!TractorTrailerPolicy.Newer(intent.Sequence, previous)) return;
                _sausageSequences[actor] = intent.Sequence;
                if (!_sausageSources.TryGetValue(intent.SourceId, out var source)) return;
                bool available = SausagePackage(source, intent.PackageId, out var item);
                bool near = GamblingSync.TryPlayerPosition(session, actor, out var position) && (source.Trigger.bounds.ClosestPoint(position) - position).sqrMagnitude <= 9;
                bool owned = available && (item.RemoteOwner == 255 || item.RemoteOwner == actor);
                if (!SausagePolicy.CanOpen(intent, actor, previous, available, near, owned, available && InSausageTrigger(source, item.Body)))
                { SyncEventLog.Record("sausage-rejected", actor + "/" + intent.PackageId.ToString("X8")); return; }
                source.Package.Value = item.Body.gameObject;
                FsmHook.FireRemoteEntry(source.Fsm, SyncCatalog.Sausages!["open"]);
            }
            catch (Exception e) { FailSausages(e); }
        }
        private void CaptureSausage(SausageSource source, FsmGameObject output)
        {
            try
            {
                if (_sausageFailed || source.Opening == 0 || SessionManager.Instance?.IsHost != true) throw new InvalidOperationException("Unowned sausage creation.");
                var body = output.Value?.GetComponent<Rigidbody>();
                if (body == null || source.Outputs.Contains(body) || source.Outputs.Count >= 4) throw new InvalidOperationException("Invalid sausage output.");
                var use = SausageUse(body.gameObject, SyncCatalog.Sausages!["use"])!;
                if (!use.Fsm.Initialized) use.Fsm.Init(use);
                use.FsmVariables.FindFsmFloat(SyncCatalog.Sausages["condition"]).Value = source.Fsm.FsmVariables.FindFsmFloat(SyncCatalog.Sausages["condition"]).Value;
                source.Outputs.Add(body); _trackedBodies[body] = true;
            }
            catch (Exception e) { FailSausages(e); FsmHook.FireRemoteEntry(source.Fsm, SyncCatalog.Sausages!["idle"]); }
        }
        private void FinishSausageOpening(SausageSource source)
        {
            if (source.Opening == 0 || _sausageFailed) return;
            try
            {
                if (source.Outputs.Count != 4) throw new InvalidOperationException("Sausage conversion did not produce exactly four items.");
                for (int n = 0; n < 4; n++) RegisterSausage(source.Outputs[n], SausagePolicy.ItemId(source.Opening, n));
                AnnounceItemDespawn(source.Opening, "sausage package opened");
                SyncEventLog.Record("sausage-open", source.Opening.ToString("X8") + " x4");
                source.Opening = 0; source.Outputs.Clear();
            }
            catch (Exception e) { FailSausages(e); }
        }
    }
}
