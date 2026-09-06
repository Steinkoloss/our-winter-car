using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private sealed class PackageFactory
        {
            public PackageFactoryData Rule = null!;
            public uint Id;
            public PlayMakerFSM Fsm = null!, TemplateUse = null!;
            public GameObject Prefab = null!, Contents = null!;
            public FsmState? CreateState, LoadState;
            public FsmStateAction? Hook, LoadHook;
            public readonly FsmSuppressor Suppressor = new FsmSuppressor();
            public bool Failed;
        }
        private sealed class PackageOutput
        {
            public PackageFactory Factory = null!;
            public string NativeId = string.Empty;
            public float Deadline;
        }
        private readonly Dictionary<uint, PackageFactory> _packageFactories = new Dictionary<uint, PackageFactory>();
        private readonly Dictionary<Rigidbody, PackageOutput> _packageOutputs = new Dictionary<Rigidbody, PackageOutput>();
        private readonly HashSet<Rigidbody> _unreadyPackages = new HashSet<Rigidbody>();

        private void RefreshPackageFactories()
        {
            var c = SyncCatalog.PartsPackages;
            if (c == null || _packageFactories.Count == c.Factories.Count) return;
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null || !fsm.Fsm.Initialized || ScenePath.Of(fsm.transform) != c["factoryPath"]) continue;
                foreach (var rule in c.Factories)
                {
                    if (fsm.FsmName != rule.Fsm) continue;
                    uint id = FactoryItemIdentity.FactoryId(c["factoryPath"], rule.Fsm);
                    if (_packageFactories.ContainsKey(id)) break;
                    var factory = new PackageFactory { Rule = rule, Id = id, Fsm = fsm };
                    _packageFactories.Add(id, factory);
                    try
                    {
                        var prefab = fsm.FsmVariables.FindFsmGameObject(c["prefabVariable"]);
                        var contents = fsm.FsmVariables.FindFsmGameObject(c["spawnerVariable"]);
                        if (prefab == null || prefab.Value == null || prefab.Value.name != rule.Prefix
                            || contents == null || contents.Value == null || ScenePath.Of(contents.Value.transform) != rule.ContentsPath
                            || fsm.FsmVariables.FindFsmGameObject(c["outputVariable"]) == null
                            || fsm.FsmVariables.FindFsmString(c["idVariable"]) == null)
                            throw new InvalidOperationException("Package factory references changed.");
                        factory.Prefab = prefab.Value; factory.Contents = contents.Value;
                        factory.TemplateUse = ValidatePackageTemplate(factory, c);
                        var state = PackageStateActions(fsm, c["createState"], "IntAdd", "CreateObject", "SetParent",
                            "SetParent", "SetVelocity", "SetFsmGameObject", "ConvertIntToString", "BuildString", "SetName");
                        if (PackageField<FsmGameObject>(state.Actions[1], "gameObject")?.Name != c["prefabVariable"]
                            || PackageField<FsmGameObject>(state.Actions[1], "storeObject")?.Name != c["outputVariable"]
                            || PackageField<FsmString>(state.Actions[8], "name")?.Name != c["idVariable"])
                            throw new InvalidOperationException("Package direct output changed.");
                        if (state.Transitions.Length != 1 || state.Transitions[0].EventName != "FINISHED"
                            || state.Transitions[0].ToState != c["factoryIdleState"])
                            throw new InvalidOperationException("Package factory completion changed.");
                        PackageStateActions(fsm, c["factoryIdleState"]);
                        var load = PackageStateActions(fsm, c["loadCreateState"], "CreateObject", "SetFsmGameObject", "SetName");
                        if (PackageField<FsmGameObject>(load.Actions[0], "gameObject")?.Name != c["prefabVariable"]
                            || PackageField<FsmGameObject>(load.Actions[0], "storeObject")?.Name != c["outputVariable"]
                            || PackageField<FsmString>(load.Actions[2], "name")?.Name != c["idVariable"])
                            throw new InvalidOperationException("Saved package output changed.");
                        factory.CreateState = state;
                        factory.Hook = new FsmHookAction(() => CapturePackageOutput(factory));
                        var actions = new FsmStateAction[state.Actions.Length + 1];
                        Array.Copy(state.Actions, actions, state.Actions.Length);
                        // New has its persistent name only after all nine one-shot actions.
                        actions[actions.Length - 1] = factory.Hook; state.Actions = actions;
                        factory.LoadState = load;
                        factory.LoadHook = new FsmHookAction(() => CapturePackageOutput(factory));
                        actions = new FsmStateAction[load.Actions.Length + 1];
                        Array.Copy(load.Actions, actions, load.Actions.Length);
                        actions[actions.Length - 1] = factory.LoadHook; load.Actions = actions;
                    }
                    catch (Exception e) { FailPackageFactory(factory, e); }
                    break;
                }
            }
        }

        private static FsmState PackageStateActions(PlayMakerFSM fsm, string name, params string[] types)
        {
            var state = FsmHook.FindState(fsm, name);
            if (state == null || state.Actions.Length != types.Length)
                throw new InvalidOperationException("Package state changed: " + name);
            for (int i = 0; i < types.Length; i++)
                if (!state.Actions[i].Enabled || state.Actions[i].GetType().Name != types[i])
                    throw new InvalidOperationException("Package actions changed: " + name);
            return state;
        }

        private static PlayMakerFSM ValidatePackageTemplate(PackageFactory factory, PartsPackagesData c)
        {
            var fsms = factory.Prefab.GetComponentsInChildren<PlayMakerFSM>(true);
            if (factory.Prefab.GetComponent<Rigidbody>() == null || fsms.Length != 1
                || fsms[0].gameObject != factory.Prefab || fsms[0].FsmName != c["itemFsm"])
                throw new InvalidOperationException("Package prefab components changed.");
            var use = fsms[0];
            if (!use.Fsm.Initialized) use.Fsm.Init(use);
            if (use.Fsm.StartState != c["itemInitState"]
                || use.FsmVariables.FindFsmString(c["itemIdVariable"]) == null
                || use.FsmVariables.FindFsmGameObject(c["contentsVariable"]) == null
                || use.FsmVariables.FindFsmGameObject(c["ownerVariable"]) == null
                || use.FsmVariables.FindFsmInt(c["capacityVariable"]) == null
                || use.FsmVariables.FindFsmInt(c["quantityVariable"])?.Value != factory.Rule.Capacity)
                throw new InvalidOperationException("Package quantity/initialization changed.");
            ValidatePackageGarbage(use);
            ValidatePackageOpening(use, c);
            PackageStateActions(use, c["emptyState"], "DestroyComponent", "SetIntValue", "SetName");
            foreach (string state in c.ReadyStates)
                if (FsmHook.FindState(use, state) == null) throw new InvalidOperationException("Package ready state missing.");
            return use;
        }

        private void CapturePackageOutput(PackageFactory factory)
        {
            if (factory.Failed) return;
            try
            {
                var session = SessionManager.Instance;
                var c = SyncCatalog.PartsPackages;
                if (session == null || c == null || (session.State != SessionState.Hosting && session.State != SessionState.Connected)) return;
                var obj = factory.Fsm.FsmVariables.FindFsmGameObject(c["outputVariable"]).Value;
                string nativeId = factory.Fsm.FsmVariables.FindFsmString(c["idVariable"]).Value;
                var body = obj != null ? obj.GetComponent<Rigidbody>() : null;
                if (body == null || !FactoryItemIdentity.IsNativeId(nativeId, factory.Rule.Prefix))
                    throw new InvalidOperationException("Package factory produced an invalid box.");
                _packageOutputs[body] = new PackageOutput { Factory = factory, NativeId = nativeId, Deadline = Time.unscaledTime + 10f };
            }
            catch (Exception e) { FailPackageFactory(factory, e); }
        }

        private void ProcessPackageOutputs(SessionManager session)
        {
            var c = SyncCatalog.PartsPackages;
            if (c == null) return;
            foreach (var factory in _packageFactories.Values)
            {
                try
                {
                    if (!session.IsHost && !factory.Failed && factory.Fsm != null && factory.Fsm.Fsm.Started
                        && !factory.Suppressor.Active && factory.Fsm.ActiveStateName == c["factoryIdleState"])
                        factory.Suppressor.Suppress(factory.Fsm);
                }
                catch (Exception e) { FailPackageFactory(factory, e); }
            }
            // Saved boxes seen during their initial Wait need another chance before
            // the five-second world scan, especially to hide guest originals promptly.
            if (_unreadyPackages.Count > 0)
            {
                var retry = new List<Rigidbody>(_unreadyPackages);
                _unreadyPackages.Clear();
                foreach (var body in retry)
                    if (body != null && body.gameObject.activeInHierarchy) TryScanPackage(body);
            }
            var done = new List<Rigidbody>();
            foreach (var pair in _packageOutputs)
            {
                var body = pair.Key; var output = pair.Value;
                if (body == null || output.Factory.Failed) { done.Add(pair.Key); continue; }
                try
                {
                    var use = FindPackageUse(body);
                    if (use != null && use.Fsm.Started && Array.IndexOf(c.ReadyStates, use.ActiveStateName) >= 0)
                    {
                        if (use.FsmVariables.FindFsmString(c["itemIdVariable"]).Value != output.NativeId)
                            throw new InvalidOperationException("Package initialized with another ID.");
                        TryScanPackage(body); done.Add(body);
                    }
                    else if (Time.unscaledTime >= output.Deadline)
                        throw new InvalidOperationException("Package initialization timed out.");
                }
                catch (Exception e) { FailPackageFactory(output.Factory, e); done.Add(body); }
            }
            foreach (var body in done) _packageOutputs.Remove(body);
        }

        private static void FailPackageFactory(PackageFactory factory, Exception e)
        {
            if (factory.Failed) return;
            factory.Failed = true;
            WinterMPPlugin.Log.LogWarning("WorldSync: package factory " + factory.Rule.Fsm + " disabled: " + e.Message);
            SyncEventLog.Record("package-disabled", factory.Rule.Fsm + " " + e.Message);
        }

        private void ClearPackageFactories()
        {
            foreach (var factory in _packageFactories.Values)
            {
                if (factory.Fsm != null && factory.CreateState != null && factory.Hook != null)
                {
                    var actions = new List<FsmStateAction>(factory.CreateState.Actions);
                    actions.Remove(factory.Hook); factory.CreateState.Actions = actions.ToArray();
                }
                if (factory.Fsm != null && factory.LoadState != null && factory.LoadHook != null)
                {
                    var actions = new List<FsmStateAction>(factory.LoadState.Actions);
                    actions.Remove(factory.LoadHook); factory.LoadState.Actions = actions.ToArray();
                }
                factory.Suppressor.Restore();
            }
            _packageFactories.Clear(); _packageOutputs.Clear(); _unreadyPackages.Clear();
        }
    }
}
