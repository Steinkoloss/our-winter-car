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
        private sealed class TrophyFactory
        {
            public TrophyFactoryData Rule = null!;
            public uint Id;
            public PlayMakerFSM Fsm = null!, TemplateUse = null!;
            public GameObject Prefab = null!;
            public FsmState CreateState = null!;
            public FsmStateAction Hook = null!;
            public readonly FsmSuppressor Suppressor = new FsmSuppressor();
            public bool Failed;
        }

        private sealed class FactoryItem
        {
            public TrophyFactory Factory = null!;
            public Rigidbody Body = null!;
            public string NativeId = string.Empty;
            public bool Replica;
        }

        private sealed class PendingFactoryItem
        {
            public uint FactoryId;
            public ItemSpawn.Entry Entry;
            public byte Owner;
        }

        private sealed class NativeFactoryOutput
        {
            public TrophyFactory Factory = null!;
            public string NativeId = string.Empty;
            public float Deadline;
        }

        private sealed class LocalTrophy
        {
            public bool Active;
            public readonly FsmSuppressor Suppressor = new FsmSuppressor();
        }

        private readonly Dictionary<uint, TrophyFactory> _trophyFactories = new Dictionary<uint, TrophyFactory>();
        private readonly Dictionary<uint, FactoryItem> _factoryItems = new Dictionary<uint, FactoryItem>();
        private readonly Dictionary<uint, PendingFactoryItem> _pendingFactoryItems = new Dictionary<uint, PendingFactoryItem>();
        private readonly Dictionary<Rigidbody, NativeFactoryOutput> _nativeFactoryOutputs = new Dictionary<Rigidbody, NativeFactoryOutput>();
        private readonly Dictionary<GameObject, LocalTrophy> _localTrophies = new Dictionary<GameObject, LocalTrophy>();
        private bool _trophyDiscoveryFailed;

        private void RefreshTrophyFactories()
        {
            if (_trophyDiscoveryFailed) return;
            try { DiscoverTrophyFactories(); }
            catch (Exception e)
            {
                _trophyDiscoveryFailed = true;
                WinterMPPlugin.Log.LogWarning("WorldSync: trophy discovery disabled: " + e.Message);
                SyncEventLog.Record("factory-disabled", "discovery " + e.Message);
            }
        }

        private void DiscoverTrophyFactories()
        {
            SyncCatalog.EnsureLoaded();
            var c = SyncCatalog.TrophyFactories;
            if (c == null || _trophyFactories.Count == c.Factories.Count) return;
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null || !fsm.Fsm.Initialized) continue;
                foreach (var rule in c.Factories)
                {
                    if (fsm.FsmName != rule.Fsm || ScenePath.Of(fsm.transform) != rule.Path) continue;
                    uint id = FactoryItemIdentity.FactoryId(rule.Path, rule.Fsm);
                    if (_trophyFactories.ContainsKey(id)) break;
                    var factory = new TrophyFactory { Rule = rule, Id = id, Fsm = fsm };
                    _trophyFactories.Add(id, factory);
                    try
                    {
                        var prefab = fsm.FsmVariables.FindFsmGameObject(c["prefabVariable"]);
                        var prefix = fsm.FsmVariables.FindFsmString(c["prefixVariable"]);
                        if (prefab == null || prefab.Value == null || prefab.Value.name != rule.PrefabName
                            || prefix == null || prefix.Value != rule.Prefix
                            || fsm.FsmVariables.FindFsmGameObject(c["outputVariable"]) == null
                            || fsm.FsmVariables.FindFsmString(c["idVariable"]) == null)
                            throw new InvalidOperationException("Native trophy factory variables changed.");
                        factory.Prefab = prefab.Value;
                        factory.TemplateUse = ValidateTrophyTemplate(factory.Prefab, c);
                        var state = TrophyState(fsm, c["createState"], "IntAdd", "CreateObject", "ConvertIntToString", "BuildString", "SetName");
                        var create = state.Actions[1];
                        var source = create.GetType().GetField("gameObject")?.GetValue(create) as FsmGameObject;
                        var output = create.GetType().GetField("storeObject")?.GetValue(create) as FsmGameObject;
                        if (source == null || source.Name != c["prefabVariable"] || output == null || output.Name != c["outputVariable"])
                            throw new InvalidOperationException("Native trophy output reference changed.");
                        if (state.Transitions.Length != 1 || state.Transitions[0].EventName != "FINISHED"
                            || state.Transitions[0].ToState != c["factoryIdleState"])
                            throw new InvalidOperationException("Native trophy completion transition changed.");
                        TrophyState(fsm, c["factoryIdleState"]);
                        factory.CreateState = state;
                        factory.Hook = new FsmHookAction(() => CaptureTrophyOutput(factory));
                        var actions = new FsmStateAction[state.Actions.Length + 1];
                        Array.Copy(state.Actions, actions, state.Actions.Length);
                        // These five one-shot actions do not transition before FINISHED.
                        // Capture New after SetName; a radius scan can steal adjacent items.
                        actions[actions.Length - 1] = factory.Hook;
                        state.Actions = actions;
                        SyncEventLog.Record("factory-bind", rule.Path + "::" + rule.Fsm);
                    }
                    catch (Exception e) { FailTrophyFactory(factory, e); }
                    break;
                }
            }
        }

        private static FsmState TrophyState(PlayMakerFSM fsm, string name, params string[] types)
        {
            var state = FsmHook.FindState(fsm, name);
            if (state == null || state.Actions.Length != types.Length)
                throw new InvalidOperationException("Trophy state changed: " + name);
            for (int i = 0; i < types.Length; i++)
                if (!state.Actions[i].Enabled || state.Actions[i].GetType().Name != types[i])
                    throw new InvalidOperationException("Trophy actions changed: " + name);
            return state;
        }

        private static PlayMakerFSM ValidateTrophyTemplate(GameObject prefab, TrophyFactoriesData c)
        {
            var fsms = prefab.GetComponentsInChildren<PlayMakerFSM>(true);
            if (prefab.GetComponent<Rigidbody>() == null || fsms.Length != 1 || fsms[0].gameObject != prefab
                || fsms[0].FsmName != c["itemFsm"])
                throw new InvalidOperationException("Trophy prefab components changed.");
            var use = fsms[0];
            if (!use.Fsm.Initialized) use.Fsm.Init(use);
            TrophyState(use, c["itemInitState"], "GetOwner", "GetName", "SetName", "SetIsKinematic", "BuildString", "Exists");
            TrophyState(use, c["itemReadyState"], "SetScale");
            TrophyState(use, c["itemLoadState"], "LoadTransform");
            TrophyState(use, c["itemSaveState"], "SaveTransform");
            if (use.FsmVariables.FindFsmString(c["itemIdVariable"]) == null)
                throw new InvalidOperationException("Trophy persistent ID missing.");
            return use;
        }

        private void CaptureTrophyOutput(TrophyFactory factory)
        {
            if (factory.Failed) return;
            try
            {
                var session = SessionManager.Instance;
                var c = SyncCatalog.TrophyFactories;
                if (session == null || c == null || (session.State != SessionState.Hosting && session.State != SessionState.Connected)) return;
                var obj = factory.Fsm.FsmVariables.FindFsmGameObject(c["outputVariable"]).Value;
                string nativeId = factory.Fsm.FsmVariables.FindFsmString(c["idVariable"]).Value;
                var body = obj != null ? obj.GetComponent<Rigidbody>() : null;
                if (body == null || !FactoryItemIdentity.IsNativeId(nativeId, factory.Rule.Prefix))
                    throw new InvalidOperationException("Invalid native trophy output.");
                _trackedBodies[body] = true;
                _nativeFactoryOutputs[body] = new NativeFactoryOutput { Factory = factory, NativeId = nativeId,
                    Deadline = Time.unscaledTime + 10f };
            }
            catch (Exception e) { FailTrophyFactory(factory, e); }
        }

        private bool TryScanTrophy(Rigidbody body)
        {
            var c = SyncCatalog.TrophyFactories;
            var session = SessionManager.Instance;
            if (c == null || session == null) return false;
            bool candidate = false;
            string name = body.gameObject.name;
            foreach (var rule in c.Factories)
                if (name == rule.ItemName || name == rule.PrefabName + "(Clone)" || FactoryItemIdentity.IsNativeId(name, rule.Prefix))
                { candidate = true; break; }
            if (!candidate) return false;
            foreach (var use in body.GetComponents<PlayMakerFSM>())
            {
                if (use.FsmName != c["itemFsm"]) continue;
                var id = use.FsmVariables.FindFsmString(c["itemIdVariable"]);
                foreach (var rule in c.Factories)
                {
                    string nativeId = id != null ? id.Value : string.Empty;
                    if (!FactoryItemIdentity.IsNativeId(nativeId, rule.Prefix)) nativeId = body.gameObject.name;
                    if (!FactoryItemIdentity.IsNativeId(nativeId, rule.Prefix)) continue;
                    // Known native trophies never acquire transient scanner ordinals,
                    // including while their own save-load actions are still running.
                    if (_trophyFactories.TryGetValue(FactoryItemIdentity.FactoryId(rule.Path, rule.Fsm), out var factory)
                        && !factory.Failed && use.ActiveStateName == c["itemReadyState"])
                    {
                        try { RegisterNativeTrophy(factory, body, nativeId, session); }
                        catch (Exception e) { FailTrophyFactory(factory, e); }
                    }
                    return true;
                }
                foreach (var rule in c.Factories)
                    if (body.gameObject.name == rule.ItemName || body.gameObject.name == rule.PrefabName + "(Clone)") return true;
            }
            return false;
        }

        private void RegisterNativeTrophy(TrophyFactory factory, Rigidbody body, string nativeId, SessionManager session)
        {
            uint id = FactoryItemIdentity.ItemId(factory.Id, nativeId);
            if (_factoryItems.TryGetValue(id, out var existing) && existing.Body == body) return;
            if (!session.IsHost)
            {
                // Hide local save objects, retaining their native FSMs and transforms
                // for disconnect. A guest replica must never overwrite those objects.
                if (!_localTrophies.ContainsKey(body.gameObject))
                {
                    var local = new LocalTrophy { Active = body.gameObject.activeSelf };
                    foreach (var use in body.GetComponents<PlayMakerFSM>())
                        if (use.FsmName == SyncCatalog.TrophyFactories!["itemFsm"] && !local.Suppressor.Suppress(use))
                            throw new InvalidOperationException("Could not pause local saved trophy.");
                    _localTrophies.Add(body.gameObject, local);
                }
                _trackedBodies[body] = true;
                body.gameObject.SetActive(false);
                return;
            }
            if (_spawnLifecycle.IsRetired(id)) return;
            if (_items.ContainsKey(id) || existing != null)
                throw new InvalidOperationException("Trophy item ID collision: " + nativeId);
            _trackedBodies[body] = true;
            BindOfferedBodyAsOwner(body, id, Time.unscaledTime);
            var item = new FactoryItem { Factory = factory, NativeId = nativeId, Body = body };
            _factoryItems.Add(id, item);
            var message = TrophyManifest(factory, false, session.LocalPlayerId);
            message.Items.Add(TrophyEntry(id, item));
            session.SendWorldMessage(message, Channel.ReliableOrdered);
            SyncEventLog.Record("factory-spawn", nativeId + " " + id.ToString("X8"));
        }

        private bool ValidateTrophyManifest(ItemSpawn message)
        {
            SyncCatalog.EnsureLoaded();
            var c = SyncCatalog.TrophyFactories;
            if (c == null || message.Items == null) return false;
            foreach (var rule in c.Factories)
            {
                if (FactoryItemIdentity.FactoryId(rule.Path, rule.Fsm) != message.ContainerNetId) continue;
                if (message.StateName != c["createState"]) return false;
                foreach (var entry in message.Items)
                    if (!FactoryItemIdentity.IsNativeId(entry.TemplateName, rule.Prefix)) return false;
                return true;
            }
            return false;
        }

        private void QueueTrophyManifest(ItemSpawn message)
        {
            foreach (var entry in message.Items)
            {
                var normalized = entry;
                if (!TryNormalize(entry.Rotation.ToUnity(), out var rotation)) continue;
                normalized.Rotation = rotation.ToNet();
                _pendingFactoryItems[entry.NetId] = new PendingFactoryItem { FactoryId = message.ContainerNetId,
                    Entry = normalized, Owner = message.OwnerPlayerId };
            }
        }

        private void ProcessTrophySpawns(SessionManager session)
        {
            var c = SyncCatalog.TrophyFactories;
            if (c == null) return;
            foreach (var factory in _trophyFactories.Values)
            {
                if (factory.Failed || factory.Fsm == null || session.IsHost) continue;
                if (!factory.Suppressor.Active && factory.Fsm.ActiveStateName == c["factoryIdleState"])
                    factory.Suppressor.Suppress(factory.Fsm);
            }
            var finished = new List<Rigidbody>();
            foreach (var pair in _nativeFactoryOutputs)
            {
                var body = pair.Key; var pending = pair.Value;
                if (body == null || pending.Factory.Failed) { finished.Add(pair.Key); continue; }
                try
                {
                    foreach (var use in body.GetComponents<PlayMakerFSM>())
                    {
                        if (use.FsmName != c["itemFsm"] || use.ActiveStateName != c["itemReadyState"]) continue;
                        if (use.FsmVariables.FindFsmString(c["itemIdVariable"]).Value != pending.NativeId)
                            throw new InvalidOperationException("Trophy initialized with a different persistent ID.");
                        RegisterNativeTrophy(pending.Factory, body, pending.NativeId, session);
                        finished.Add(body); break;
                    }
                    if (!finished.Contains(body) && Time.unscaledTime >= pending.Deadline)
                        throw new InvalidOperationException("Trophy initialization did not finish.");
                }
                catch (Exception e) { FailTrophyFactory(pending.Factory, e); finished.Add(body); }
            }
            foreach (var body in finished) _nativeFactoryOutputs.Remove(body);
            if (session.IsHost || _pendingFactoryItems.Count == 0) return;
            var done = new List<uint>();
            foreach (var pair in _pendingFactoryItems)
            {
                var pending = pair.Value;
                if (!_spawnLifecycle.ShouldMaterialize(pair.Key, _items.TryGetValue(pair.Key, out var item) && item.Body != null))
                { done.Add(pair.Key); continue; }
                if (!_trophyFactories.TryGetValue(pending.FactoryId, out var factory) || factory.Failed || !factory.Suppressor.Active) continue;
                try { MaterializeTrophy(factory, pending); done.Add(pair.Key); }
                catch (Exception e) { FailTrophyFactory(factory, e); }
            }
            foreach (uint id in done) _pendingFactoryItems.Remove(id);
        }

        private void MaterializeTrophy(TrophyFactory factory, PendingFactoryItem pending)
        {
            GameObject? clone = null;
            bool enabled = factory.TemplateUse.enabled;
            try
            {
                // Trophy Use only initializes scale/name/physics and loads/saves a
                // transform. Disable it on the actual prefab for the synchronous copy
                // so even prefabs with no initial Wait cannot read the guest's save.
                factory.TemplateUse.enabled = false;
                clone = (GameObject)UnityEngine.Object.Instantiate(factory.Prefab, pending.Entry.Position.ToUnity(), pending.Entry.Rotation.ToUnity());
            }
            finally { factory.TemplateUse.enabled = enabled; }
            try
            {
                var body = clone.GetComponent<Rigidbody>();
                clone.name = factory.Rule.ItemName;
                clone.transform.localScale = Vector3.one;
                body.isKinematic = false;
                var c = SyncCatalog.TrophyFactories!;
                foreach (var use in clone.GetComponents<PlayMakerFSM>())
                {
                    use.enabled = false;
                    if (!use.Fsm.Initialized) use.Fsm.Init(use);
                    use.FsmVariables.FindFsmString(c["itemIdVariable"]).Value = pending.Entry.TemplateName;
                }
                if (_items.TryGetValue(pending.Entry.NetId, out var dead)) RemoveTrackedItem(pending.Entry.NetId, dead.Body);
                _trackedBodies[body] = true;
                BindSpawnedBody(body, pending.Entry, pending.Owner, Time.unscaledTime);
                _factoryItems[pending.Entry.NetId] = new FactoryItem { Factory = factory, Body = body,
                    NativeId = pending.Entry.TemplateName, Replica = true };
                clone.SetActive(true);
                SyncEventLog.Record("factory-replica", pending.Entry.TemplateName + " " + pending.Entry.NetId.ToString("X8"));
            }
            catch { if (clone != null) UnityEngine.Object.Destroy(clone); throw; }
        }

        private ItemSpawn TrophyManifest(TrophyFactory factory, bool replay, byte owner) => new ItemSpawn
        {
            ContainerNetId = factory.Id, Epoch = replay ? (ushort)0 : MintSpawnEpoch(factory.Id), OwnerPlayerId = owner,
            StateName = SyncCatalog.TrophyFactories!["createState"],
            Flags = (byte)(ItemSpawn.FlagFactory | (replay ? ItemSpawn.FlagReplay : 0)),
        };

        private static ItemSpawn.Entry TrophyEntry(uint id, FactoryItem item) => new ItemSpawn.Entry
        {
            NetId = id, TemplateName = item.NativeId,
            Position = item.Body.transform.position.ToNet(), Rotation = item.Body.transform.rotation.ToNet(),
        };

        private IEnumerable<ItemSpawn> BuildTrophyReplayManifests()
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) yield break;
            foreach (var factory in _trophyFactories.Values)
            {
                if (factory.Failed) continue;
                var replay = TrophyManifest(factory, true, session.LocalPlayerId);
                foreach (var pair in _factoryItems)
                {
                    var item = pair.Value;
                    if (item.Factory != factory || item.Body == null || _spawnLifecycle.IsRetired(pair.Key)
                        || !_items.ContainsKey(pair.Key)) continue;
                    replay.Items.Add(TrophyEntry(pair.Key, item));
                    if (replay.Items.Count < ItemSpawn.MaxItems) continue;
                    yield return replay; replay = TrophyManifest(factory, true, session.LocalPlayerId);
                }
                if (replay.Items.Count > 0) yield return replay;
            }
        }

        private static void FailTrophyFactory(TrophyFactory factory, Exception e)
        {
            if (factory.Failed) return;
            factory.Failed = true;
            WinterMPPlugin.Log.LogWarning("WorldSync: disabled trophy factory " + factory.Rule.Path + "::" + factory.Rule.Fsm + ": " + e.Message);
            SyncEventLog.Record("factory-disabled", factory.Rule.Path + "::" + factory.Rule.Fsm + " " + e.Message);
        }

        private void ClearTrophyFactories()
        {
            foreach (var pair in _factoryItems)
            {
                var item = pair.Value;
                if (_items.TryGetValue(pair.Key, out var tracked) && tracked.Body == item.Body)
                {
                    if (item.Body != null && tracked.KinematicSaved) item.Body.isKinematic = tracked.OriginalKinematic;
                    RemoveTrackedItem(pair.Key, item.Body);
                }
                if (item.Replica && item.Body != null) UnityEngine.Object.Destroy(item.Body.gameObject);
            }
            foreach (var body in _nativeFactoryOutputs.Keys) _trackedBodies.Remove(body);
            foreach (var pair in _localTrophies)
            {
                if (pair.Key == null) continue;
                var body = pair.Key.GetComponent<Rigidbody>();
                if (body != null) _trackedBodies.Remove(body);
                pair.Key.SetActive(pair.Value.Active);
                pair.Value.Suppressor.Restore();
            }
            foreach (var factory in _trophyFactories.Values)
            {
                if (factory.Fsm != null && factory.CreateState != null && factory.Hook != null)
                {
                    var actions = new List<FsmStateAction>(factory.CreateState.Actions);
                    actions.Remove(factory.Hook); factory.CreateState.Actions = actions.ToArray();
                }
                factory.Suppressor.Restore();
            }
            _factoryItems.Clear(); _pendingFactoryItems.Clear(); _nativeFactoryOutputs.Clear();
            _localTrophies.Clear(); _trophyFactories.Clear();
            _trophyDiscoveryFailed = false;
        }
    }
}
