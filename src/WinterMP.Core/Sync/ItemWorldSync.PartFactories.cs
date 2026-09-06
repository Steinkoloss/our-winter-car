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
        private sealed class ReplacementFactory
        {
            public ReplacementPartFactoryData Rule = null!;
            public PlayMakerFSM Fsm = null!, TemplateData = null!;
            public GameObject Prefab = null!;
            public readonly List<KeyValuePair<FsmState, FsmStateAction>> Hooks = new List<KeyValuePair<FsmState, FsmStateAction>>();
            public readonly FsmSuppressor Suppressor = new FsmSuppressor();
            public bool Failed;
        }
        private sealed class ReplacementOutput
        {
            public ReplacementFactory Factory = null!;
            public string NativeId = string.Empty;
            public float Deadline;
        }
        private readonly Dictionary<uint, ReplacementFactory> _replacementFactories = new Dictionary<uint, ReplacementFactory>();
        private readonly Dictionary<PlayMakerFSM, ReplacementOutput> _replacementOutputs = new Dictionary<PlayMakerFSM, ReplacementOutput>();
        private readonly HashSet<Rigidbody> _unreadyNativeParts = new HashSet<Rigidbody>();

        private void RefreshReplacementFactories()
        {
            var c = SyncCatalog.ReplacementParts;
            if (c == null || _replacementFactories.Count == c.Factories.Count) return;
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null || fsm.FsmName != c["factoryFsm"] || !fsm.Fsm.Initialized) continue;
                string path = ScenePath.Of(fsm.transform);
                foreach (var rule in c.Factories)
                {
                    if (rule.Path != path || _replacementFactories.ContainsKey(rule.Identity.FactoryId)) continue;
                    var factory = new ReplacementFactory { Rule = rule, Fsm = fsm };
                    _replacementFactories.Add(rule.Identity.FactoryId, factory);
                    try
                    {
                        factory.Prefab = fsm.FsmVariables.FindFsmGameObject(c["prefabVariable"])?.Value
                            ?? throw new InvalidOperationException("Missing replacement prefab.");
                        if (factory.Prefab.name != rule.Prefix || factory.Prefab.GetComponent<Rigidbody>() == null)
                            throw new InvalidOperationException("Replacement prefab changed.");
                        factory.TemplateData = FindReplacementData(factory.Prefab, c);
                        ValidateReplacementTemplate(factory, c);
                        foreach (string key in new[] { "createState", "loadCreateState" })
                        {
                            var state = ValidateReplacementOutput(factory, c, key == "createState");
                            var hook = new FsmHookAction(() => CaptureReplacementOutput(factory));
                            var actions = new List<FsmStateAction>(state.Actions); actions.Add(hook); state.Actions = actions.ToArray();
                            factory.Hooks.Add(new KeyValuePair<FsmState, FsmStateAction>(state, hook));
                        }
                    }
                    catch (Exception e) { FailReplacementFactory(factory, e); }
                }
            }
        }

        private static PlayMakerFSM FindReplacementData(GameObject obj, ReplacementPartsData c)
        {
            foreach (var fsm in obj.GetComponents<PlayMakerFSM>()) if (fsm.FsmName == c["itemFsm"]) return fsm;
            throw new InvalidOperationException("Missing replacement Data FSM.");
        }

        private static FsmState ValidateReplacementOutput(ReplacementFactory factory, ReplacementPartsData c, bool fresh)
        {
            var types = new List<string>();
            if (fresh) types.Add("IntAdd");
            types.Add("CreateObject");
            if (fresh) types.Add("SetFsmFloat");
            foreach (var reference in factory.Rule.References) types.Add("SetFsmGameObject");
            if (fresh) { types.Add("ConvertIntToString"); types.Add("BuildStringFast"); }
            types.Add("SetName");
            var state = PackageStateActions(factory.Fsm, c[fresh ? "createState" : "loadCreateState"], types.ToArray());
            var create = state.Actions[fresh ? 1 : 0];
            if (PackageField<FsmGameObject>(create, "gameObject")?.Name != c["prefabVariable"]
                || PackageField<FsmGameObject>(create, "storeObject")?.Name != c["outputVariable"]
                || PackageField<FsmString>(state.Actions[state.Actions.Length - 1], "name")?.Name != c["idVariable"])
                throw new InvalidOperationException("Replacement output identity changed.");
            int index = fresh ? 3 : 1;
            foreach (var reference in factory.Rule.References)
            {
                var action = state.Actions[index++];
                if (PackageField<FsmOwnerDefault>(action, "gameObject")?.GameObject.Name != c["outputVariable"]
                    || PackageField<FsmString>(action, "fsmName")?.Value != c["itemFsm"]
                    || PackageField<FsmString>(action, "variableName")?.Value != reference.Target
                    || PackageField<FsmGameObject>(action, "setValue")?.Name != reference.Source
                    || factory.Fsm.FsmVariables.FindFsmGameObject(reference.Source) == null
                    || factory.TemplateData.FsmVariables.FindFsmGameObject(reference.Target) == null)
                    throw new InvalidOperationException("Replacement reference changed.");
            }
            return state;
        }

        private static void ValidateReplacementTemplate(ReplacementFactory factory, ReplacementPartsData c)
        {
            var data = factory.TemplateData; var vars = data.FsmVariables;
            if (vars.FindFsmString(c["itemIdVariable"]) == null || vars.FindFsmInt(c["assemblyVariable"]) == null
                || (factory.Rule.SlotCount == 0 && vars.FindFsmBool(c["installedVariable"]) == null)
                || vars.FindFsmBool(c["consumedVariable"]) == null)
                throw new InvalidOperationException("Replacement variables changed.");
            foreach (string name in factory.Rule.Scalars)
                if (vars.FindFsmFloat(name) == null) throw new InvalidOperationException("Missing replacement scalar: " + name);
            var init = PackageStateActions(data, c["itemInitState"], factory.Rule.InitActions);
            var status = PackageStateActions(data, c["itemStatusState"], factory.Rule.StatusActions);
            foreach (var action in init.Actions)
                if (Array.IndexOf(new[] { "GetChild", "RandomFloat", "GetOwner", "GetName", "BuildStringFast", "Exists" }, action.GetType().Name) < 0)
                    throw new InvalidOperationException("Unsafe replacement initialization action.");
            foreach (var action in status.Actions)
                if (Array.IndexOf(new[] { "SetRotation", "BuildStringFast", "SetName", "SetScale", "SetIsKinematic", "IntCompare", "EnableFSM", "FloatClamp" }, action.GetType().Name) < 0)
                    throw new InvalidOperationException("Unsafe replacement presentation action.");
            if (init.Actions[init.Actions.Length - 1].GetType().Name != "Exists"
                || PackageField<FsmEvent>(init.Actions[init.Actions.Length - 1], "ifDoesNotExist")?.Name != c["freshEvent"])
                throw new InvalidOperationException("Replacement fresh initialization changed.");
            foreach (string key in new[] { "itemIdleState", "itemStopState", "saveState", "deleteState", "loadState", "garbageState" })
                if (!FsmHook.HasState(data, c[key])) throw new InvalidOperationException("Missing replacement state: " + c[key]);
            if (!HasReplacementTransition(init, c["freshEvent"], c["itemStatusState"])
                || !HasReplacementTransition(status, "FINISHED", c["itemIdleState"])
                || !HasReplacementTransition(FsmHook.FindState(data, c["itemIdleState"])!, "FINISHED", c["itemStopState"]))
                throw new InvalidOperationException("Replacement initialization transitions changed.");
            PackageStateActions(data, c["itemStopState"]);
        }

        private static bool HasReplacementTransition(FsmState state, string eventName, string target)
        {
            foreach (var transition in state.Transitions)
                if (transition.EventName == eventName && transition.ToState == target) return true;
            return false;
        }

        private void CaptureReplacementOutput(ReplacementFactory factory)
        {
            if (factory.Failed) return;
            try
            {
                var c = SyncCatalog.ReplacementParts!;
                string nativeId = factory.Fsm.FsmVariables.FindFsmString(c["idVariable"]).Value;
                var obj = factory.Fsm.FsmVariables.FindFsmGameObject(c["outputVariable"]).Value;
                var body = obj != null ? obj.GetComponent<Rigidbody>() : null;
                if (obj == null || !factory.Rule.Identity.TryId(nativeId, out _))
                    throw new InvalidOperationException("Invalid replacement output.");
                // Capture every Create product tail, including the multi-output loop.
                _replacementOutputs[FindReplacementData(obj, c)] = new ReplacementOutput { Factory = factory, NativeId = nativeId,
                    Deadline = Time.unscaledTime + 10f };
                if (body != null) ObservePackageOpeningOutput(factory, body, nativeId);
            }
            catch (Exception e) { FailReplacementFactory(factory, e); }
        }

        private void ProcessReplacementOutputs(SessionManager session)
        {
            var c = SyncCatalog.ReplacementParts;
            if (c == null) return;
            foreach (var factory in _replacementFactories.Values)
            {
                if (factory.Failed || session.IsHost || factory.Fsm == null || !factory.Fsm.Fsm.Started
                    || factory.Suppressor.Active || factory.Fsm.ActiveStateName != c["factoryIdleState"]) continue;
                try { factory.Suppressor.Suppress(factory.Fsm); }
                catch (Exception e) { FailReplacementFactory(factory, e); }
            }
            var retry = new List<Rigidbody>(_unreadyNativeParts); _unreadyNativeParts.Clear();
            foreach (var body in retry) if (body != null && body.gameObject.activeInHierarchy) TryScanNativePart(body);
            var done = new List<PlayMakerFSM>();
            foreach (var pair in _replacementOutputs)
            {
                var data = pair.Key; var output = pair.Value;
                if (data == null || output.Factory.Failed) { done.Add(pair.Key); continue; }
                try
                {
                    if (_bridge.PartIdentities.TryRootId(data, out uint id))
                    {
                        if (data.FsmVariables.FindFsmString(c["itemIdVariable"]).Value != output.NativeId)
                            throw new InvalidOperationException("Replacement initialized with another identity.");
                        TrackNativePart(data, id);
                        var body = data.GetComponent<Rigidbody>();
                        if (body != null) TryScanNativePart(body);
                        done.Add(data);
                    }
                    else if (Time.unscaledTime >= output.Deadline) throw new InvalidOperationException("Replacement initialization timed out.");
                }
                catch (Exception e) { FailReplacementFactory(output.Factory, e); done.Add(data); }
            }
            foreach (var data in done) _replacementOutputs.Remove(data);
        }

        private static void FailReplacementFactory(ReplacementFactory factory, Exception e)
        {
            if (factory.Failed) return;
            factory.Failed = true;
            WinterMPPlugin.Log.LogWarning("WorldSync: replacement factory " + factory.Rule.Prefix + " disabled: " + e.Message);
            SyncEventLog.Record("replacement-disabled", factory.Rule.Prefix + " " + e.Message);
        }
    }
}
