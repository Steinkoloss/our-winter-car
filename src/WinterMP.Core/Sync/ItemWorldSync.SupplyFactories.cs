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
        private sealed class SupplyFactory
        {
            public uint Id;
            public PackageFactoryData Rule = null!;
            public PlayMakerFSM Fsm = null!, TemplateUse = null!;
            public GameObject Prefab = null!;
            public FsmState? Create, Load;
            public FsmStateAction? CreateHook, LoadHook;
            public readonly FsmSuppressor Suppressor = new FsmSuppressor();
            public bool Failed;
        }
        private readonly Dictionary<uint, SupplyFactory> _supplyFactories = new Dictionary<uint, SupplyFactory>();
        private readonly HashSet<Rigidbody> _unreadySupplies = new HashSet<Rigidbody>();

        private void RefreshSupplyFactories()
        {
            var c = SyncCatalog.PartsPackages;
            if (c == null) return;
            foreach (var rule in c.Factories)
            {
                if (rule.SupplyContents == null) continue;
                uint id = FactoryItemIdentity.FactoryId(rule.ContentsPath, rule.ContentsFsm);
                if (_supplyFactories.ContainsKey(id)) continue;
                foreach (var obj in ScenePath.ScanFsms())
                {
                    var fsm = obj as PlayMakerFSM;
                    if (fsm == null || !fsm.Fsm.Initialized || fsm.FsmName != rule.ContentsFsm
                        || ScenePath.Of(fsm.transform) != rule.ContentsPath) continue;
                    var factory = new SupplyFactory { Id = id, Rule = rule, Fsm = fsm };
                    _supplyFactories.Add(id, factory);
                    try
                    {
                        factory.Prefab = fsm.FsmVariables.FindFsmGameObject(c["prefabVariable"])?.Value
                            ?? throw new InvalidOperationException("Supply prefab missing.");
                        if (factory.Prefab.name != rule.SupplyContents.Prefix || factory.Prefab.GetComponent<Rigidbody>() == null
                            || fsm.FsmVariables.FindFsmInt(c["contentsCounterVariable"]) == null
                            || fsm.FsmVariables.FindFsmString(c["idVariable"]) == null)
                            throw new InvalidOperationException("Supply factory identity changed.");
                        var fsms = factory.Prefab.GetComponentsInChildren<PlayMakerFSM>(true);
                        if (fsms.Length != 1 || fsms[0].gameObject != factory.Prefab || fsms[0].FsmName != c["itemFsm"])
                            throw new InvalidOperationException("Supply prefab components changed.");
                        factory.TemplateUse = fsms[0];
                        if (!factory.TemplateUse.Fsm.Initialized) factory.TemplateUse.Fsm.Init(factory.TemplateUse);
                        ValidateSupplyUse(factory.TemplateUse, rule.SupplyContents);
                        var create = PackageStateActions(fsm, c["createState"], "IntAdd", "CreateObject", "ConvertIntToString", "BuildString", "SetName");
                        var load = PackageStateActions(fsm, c["loadCreateState"], "CreateObject", "SetName");
                        ValidateSupplyCreation(fsm, create, load, rule);
                        factory.Create = create; factory.Load = load;
                        factory.CreateHook = new FsmHookAction(() => CaptureSupply(factory, true));
                        factory.LoadHook = new FsmHookAction(() => CaptureSupply(factory, false));
                        var actions = new List<FsmStateAction>(create.Actions); actions.Add(factory.CreateHook); create.Actions = actions.ToArray();
                        actions = new List<FsmStateAction>(load.Actions); actions.Add(factory.LoadHook); load.Actions = actions.ToArray();
                    }
                    catch (Exception e) { FailSupply(factory, e); }
                    break;
                }
            }
        }

        private static void ValidateSupplyCreation(PlayMakerFSM fsm, FsmState create, FsmState load, PackageFactoryData rule)
        {
            var c = SyncCatalog.PartsPackages!;
            RequireFitTransition(fsm, c["createState"], "FINISHED", c["factoryIdleState"]);
            PackageStateActions(fsm, c["factoryIdleState"]);
            if (create.Transitions.Length != 1
                || PackageField<FsmInt>(create.Actions[0], "intVariable")?.Name != c["contentsCounterVariable"]
                || PackageField<FsmInt>(create.Actions[0], "add")?.UseVariable != false
                || PackageField<FsmInt>(create.Actions[0], "add")?.Value != 1
                || PackageField<FsmGameObject>(create.Actions[1], "spawnPoint")?.Name != rule.ContentsSpawnPointVariable
                || PackageField<FsmInt>(create.Actions[2], "intVariable")?.Name != c["contentsCounterVariable"]
                || PackageField<FsmString>(create.Actions[2], "stringVariable")?.Name != c["idVariable"])
                throw new InvalidOperationException("Supply counter/spawn point changed.");
            foreach (var pair in new[] { create.Actions[1], load.Actions[0] })
                if (PackageField<FsmGameObject>(pair, "gameObject")?.Name != c["prefabVariable"]
                    || PackageField<FsmGameObject>(pair, "storeObject")?.Name != c["outputVariable"])
                    throw new InvalidOperationException("Supply direct output changed.");
            foreach (var action in new[] { create.Actions[4], load.Actions[1] })
                if (!FitTargetVariable(PackageField<FsmOwnerDefault>(action, "gameObject"), c["outputVariable"])
                    || PackageField<FsmString>(action, "name")?.Name != c["idVariable"])
                    throw new InvalidOperationException("Supply persistent output name changed.");
            foreach (var action in create.Actions) RequireFitOneShot(action);
            foreach (var action in load.Actions) RequireFitOneShot(action);
        }

        private static void ValidateSupplyUse(PlayMakerFSM use, SupplyContentsData rule)
        {
            var c = SyncCatalog.PartsPackages!;
            if (use.Fsm.StartState != c["itemInitState"]
                || use.FsmVariables.FindFsmString(c["itemIdVariable"]) == null
                || use.FsmVariables.FindFsmGameObject(c["ownerVariable"]) == null
                || use.FsmVariables.FindFsmBool("Consumed") == null || use.FsmVariables.FindFsmBool(rule.RetirementVariable) == null)
                throw new InvalidOperationException("Supply initialization changed.");
            var init = PackageStateActions(use, "State 1", "GetOwner", "GetName", "SetName", "SetIsKinematic", "BuildString", "Exists");
            if (PackageField<FsmGameObject>(init.Actions[0], "storeGameObject")?.Name != c["ownerVariable"]
                || PackageField<FsmString>(init.Actions[1], "storeName")?.Name != c["itemIdVariable"]
                || PackageField<FsmString>(init.Actions[2], "name")?.Value != rule.ItemName)
                throw new InvalidOperationException("Supply native ID capture changed.");
            PackageStateActions(use, rule.ReadyState);
            // Fitted fuses retain Consumed in their save; R20 cells delete their
            // save directly when consumed. Validate each native retirement graph.
            bool separateDestroy = rule.RetirementVariable != "Consumed";
            var garbage = separateDestroy
                ? PackageStateActions(use, c["garbageState"], "SetBoolValue", "SetBoolValue", "DestroyComponent", "DestroyComponent", "SetPosition")
                : PackageStateActions(use, c["garbageState"], "SetBoolValue", "DestroyComponent", "DestroyComponent", "SetPosition");
            for (int i = 0; i < (separateDestroy ? 2 : 1); i++)
                if (PackageField<FsmBool>(garbage.Actions[i], "boolVariable")?.Name != (i == 0 ? "Consumed" : "Destroy")
                    || PackageField<FsmBool>(garbage.Actions[i], "boolValue")?.UseVariable != false
                    || PackageField<FsmBool>(garbage.Actions[i], "boolValue")?.Value != true)
                    throw new InvalidOperationException("Supply native retirement changed.");
            PackageStateActions(use, "Load", "LoadBool", "LoadTransform", "SetScale");
            var save = PackageStateActions(use, c["saveState"], "BoolTest", "SaveTransform", "SaveBool");
            var delete = PackageStateActions(use, c["deleteState"], "Delete");
            if (PackageField<FsmBool>(save.Actions[0], "boolVariable")?.Name != rule.RetirementVariable
                || PackageField<FsmEvent>(save.Actions[0], "isTrue")?.Name != c["deleteEvent"]
                || PackageField<FsmString>(delete.Actions[0], "uniqueTag")?.Name != c["itemIdVariable"])
                throw new InvalidOperationException("Supply save deletion changed.");
            RequireFitTransition(use, c["saveState"], c["deleteEvent"], c["deleteState"]);
            bool retires = false, saves = false;
            foreach (var transition in use.Fsm.GlobalTransitions)
            {
                if (transition.EventName == c["garbageEvent"] && transition.ToState == c["garbageState"]) retires = true;
                if (transition.EventName == c["saveEvent"] && transition.ToState == c["saveState"]) saves = true;
            }
            if (!retires || !saves) throw new InvalidOperationException("Supply save/garbage events changed.");
        }

        private void CaptureSupply(SupplyFactory factory, bool created)
        {
            if (factory.Failed) return;
            try
            {
                var c = SyncCatalog.PartsPackages!;
                var obj = factory.Fsm.FsmVariables.FindFsmGameObject(c["outputVariable"]).Value;
                string nativeId = factory.Fsm.FsmVariables.FindFsmString(c["idVariable"]).Value;
                var body = obj != null ? obj.GetComponent<Rigidbody>() : null;
                if (body == null || obj!.name != nativeId || !FactoryItemIdentity.IsNativeId(nativeId, factory.Rule.SupplyContents!.Prefix))
                    throw new InvalidOperationException("Supply factory produced an invalid identity.");
                _unreadySupplies.Add(body);
                var opening = _packageOpening;
                if (created && opening != null && opening.Contents.Supply == factory && opening.Entered && !opening.Dispatched)
                {
                    opening.OutputCount++;
                    if (opening.ExpectedNativeId != nativeId) opening.InvalidOutput = true;
                    else { opening.OutputObject = obj; opening.OutputId = FactoryItemIdentity.ItemId(factory.Id, nativeId); }
                }
            }
            catch (Exception e) { FailSupply(factory, e); }
        }

        private static void FailSupply(SupplyFactory factory, Exception e)
        {
            if (factory.Failed) return;
            factory.Failed = true;
            WinterMPPlugin.Log.LogWarning("WorldSync: supply " + factory.Rule.ContentsFsm + " disabled: " + e.Message);
            SyncEventLog.Record("supply-disabled", factory.Rule.ContentsFsm + " " + e.Message);
        }
    }
}
