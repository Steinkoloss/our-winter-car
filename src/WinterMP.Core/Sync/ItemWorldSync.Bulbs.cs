using System;
using System.Collections.Generic;
using System.Globalization;
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
        private sealed class BulbFactory
        {
            public PackageFactoryData Rule = null!;
            public PlayMakerFSM Fsm = null!, Template = null!;
            public GameObject Prefab = null!;
            public FsmState? Create;
            public FsmStateAction? Hook;
            public readonly FsmSuppressor Suppressor = new FsmSuppressor();
            public bool Failed;
        }
        private sealed class BulbBinding
        {
            public Rigidbody Body = null!;
            public PlayMakerFSM Data = null!;
            public bool Replica;
            public BulbState? Last, Received;
            public uint Sent;
            public float NextSend;
        }
        private BulbFactory? _bulbFactory;
        private uint _bulbOrdinal;
        private float _bulbTick;
        private readonly Dictionary<Rigidbody, uint> _unreadyBulbs = new Dictionary<Rigidbody, uint>();
        private readonly Dictionary<uint, BulbBinding> _bulbs = new Dictionary<uint, BulbBinding>();
        private readonly Dictionary<uint, BulbState> _pendingBulbs = new Dictionary<uint, BulbState>();
        private readonly Dictionary<PlayMakerFSM, HiddenPackage> _hiddenBulbs = new Dictionary<PlayMakerFSM, HiddenPackage>();

        private void RefreshBulbs()
        {
            var c = SyncCatalog.PartsPackages;
            if (c == null || _bulbFactory != null) return;
            foreach (var rule in c.Factories)
            {
                var bulb = rule.BulbContents;
                if (bulb == null) continue;
                foreach (var obj in ScenePath.ScanFsms())
                {
                    var fsm = obj as PlayMakerFSM;
                    if (fsm == null || !fsm.Fsm.Initialized || fsm.FsmName != rule.ContentsFsm
                        || ScenePath.Of(fsm.transform) != rule.ContentsPath) continue;
                    var factory = new BulbFactory { Rule = rule, Fsm = fsm }; _bulbFactory = factory;
                    try
                    {
                        factory.Prefab = fsm.FsmVariables.FindFsmGameObject(c["prefabVariable"])?.Value
                            ?? throw new InvalidOperationException("Bulb prefab missing.");
                        var fsms = factory.Prefab.GetComponentsInChildren<PlayMakerFSM>(true);
                        if (factory.Prefab.name != bulb["prefab"] || factory.Prefab.GetComponent<Rigidbody>() == null
                            || fsms.Length != 1 || fsms[0].gameObject != factory.Prefab || fsms[0].FsmName != bulb["fsm"])
                            throw new InvalidOperationException("Bulb prefab components changed.");
                        factory.Template = fsms[0];
                        if (!factory.Template.Fsm.Initialized) factory.Template.Fsm.Init(factory.Template);
                        ValidateBulbData(factory.Template, bulb);
                        var create = PackageStateActions(fsm, c["createState"], "CreateObject", "SetFsmFloat");
                        var spawn = create.Actions[0]; var wear = create.Actions[1];
                        RequireFitTransition(fsm, c["createState"], "FINISHED", bulb["idle"]);
                        PackageStateActions(fsm, bulb["idle"], "RandomFloat");
                        if (create.Transitions.Length != 1
                            || PackageField<FsmGameObject>(spawn, "gameObject")?.Name != c["prefabVariable"]
                            || PackageField<FsmGameObject>(spawn, "spawnPoint")?.Name != rule.ContentsSpawnPointVariable
                            || PackageField<FsmGameObject>(spawn, "storeObject")?.Name != c["outputVariable"]
                            || !FitTargetVariable(PackageField<FsmOwnerDefault>(wear, "gameObject"), c["outputVariable"])
                            || PackageField<FsmString>(wear, "fsmName")?.Value != bulb["fsm"]
                            || PackageField<FsmString>(wear, "variableName")?.Value != bulb["wear"]
                            || PackageField<FsmFloat>(wear, "setValue")?.Name != bulb["wear"])
                            throw new InvalidOperationException("Bulb output or condition binding changed.");
                        foreach (var action in create.Actions) RequireFitOneShot(action);
                        factory.Create = create; factory.Hook = new FsmHookAction(() => CaptureBulb(factory));
                        var actions = new List<FsmStateAction>(create.Actions); actions.Add(factory.Hook); create.Actions = actions.ToArray();
                    }
                    catch (Exception e) { FailBulbs(e); }
                    return;
                }
            }
        }

        private static void ValidateBulbData(PlayMakerFSM data, BulbContentsData rule)
        {
            if (data.Fsm.StartState != rule["initial"] || data.FsmVariables.FindFsmFloat(rule["wear"]) == null
                || data.Fsm.GlobalTransitions.Length != 0 || data.Fsm.States.Length != 2)
                throw new InvalidOperationException("Bulb initialization changed.");
            var init = PackageStateActions(data, rule["initial"], "RandomFloat", "SetName", "SetIsKinematic");
            PackageStateActions(data, rule["ready"]);
            RequireFitTransition(data, rule["initial"], "FINISHED", rule["ready"]);
            if (PackageField<FsmFloat>(init.Actions[0], "storeResult")?.Name != rule["wear"]
                || PackageField<FsmString>(init.Actions[1], "name")?.Value != rule["itemName"])
                throw new InvalidOperationException("Bulb native condition/name changed.");
        }

        private uint NewLooseBulbId()
        {
            uint id;
            do { id = StableHash.Fnv1a32("bulb:loose:" + (++_bulbOrdinal).ToString(CultureInfo.InvariantCulture)); }
            while (id == 0 || _items.ContainsKey(id) || _bulbs.ContainsKey(id) || _spawnLifecycle.IsRetired(id) || _unreadyBulbs.ContainsValue(id));
            return id;
        }

        private void CaptureBulb(BulbFactory factory)
        {
            if (factory.Failed || SessionManager.Instance?.IsHost != true) return;
            try
            {
                var obj = factory.Fsm.FsmVariables.FindFsmGameObject(SyncCatalog.PartsPackages!["outputVariable"]).Value;
                var body = obj != null ? obj.GetComponent<Rigidbody>() : null;
                if (body == null) throw new InvalidOperationException("Bulb factory lost its output.");
                uint id;
                var opening = _packageOpening;
                if (opening != null && opening.Contents.Bulb == factory && opening.Entered && !opening.Dispatched)
                {
                    id = BulbPolicy.BoxOutput(opening.ItemId); opening.OutputCount++;
                    opening.OutputId = id; opening.OutputObject = obj;
                }
                else id = NewLooseBulbId();
                if (_unreadyBulbs.ContainsKey(body) || _bulbs.ContainsKey(id) || _items.ContainsKey(id) || _spawnLifecycle.IsRetired(id))
                    throw new InvalidOperationException("Duplicate bulb output.");
                _unreadyBulbs.Add(body, id);
            }
            catch (Exception e) { FailBulbs(e); }
        }

        private static PlayMakerFSM? BulbData(Rigidbody body)
        {
            var c = SyncCatalog.PartsPackages;
            if (c == null || body == null) return null;
            foreach (var rule in c.Factories)
            {
                var bulb = rule.BulbContents;
                if (bulb == null || (body.name != bulb["itemName"] && body.name != bulb["prefab"] + "(Clone)")) continue;
                foreach (var data in body.GetComponents<PlayMakerFSM>()) if (data.FsmName == bulb["fsm"]) return data;
            }
            return null;
        }

        private bool TryScanBulb(Rigidbody body)
        {
            var data = BulbData(body);
            if (data == null) return false;
            foreach (var b in _bulbs.Values) if (b.Body == body) return true;
            var factory = _bulbFactory; var session = SessionManager.Instance;
            if (factory == null || factory.Failed || session == null) return true;
            try
            {
                if (!data.Fsm.Started || data.ActiveStateName != factory.Rule.BulbContents!["ready"])
                { if (!_unreadyBulbs.ContainsKey(body)) _unreadyBulbs.Add(body, session.IsHost ? NewLooseBulbId() : 0); return true; }
                if (session.IsHost)
                {
                    uint id = _unreadyBulbs.TryGetValue(body, out uint captured) ? captured : NewLooseBulbId();
                    if (id == 0 || _items.ContainsKey(id) || _trackedBodies.ContainsKey(body) || _spawnLifecycle.IsRetired(id))
                        throw new InvalidOperationException("Bulb identity collision.");
                    ValidateBulbData(data, factory.Rule.BulbContents);
                    _bulbs.Add(id, new BulbBinding { Body = body, Data = data });
                    _items.Add(id, new SyncedItem { Id = id, Body = body, Path = ScenePath.Of(body.transform), LastPosition = body.position });
                    _trackedBodies[body] = true;
                    SyncEventLog.Record("bulb-bind", id.ToString("X8"));
                }
                else if (!_hiddenBulbs.ContainsKey(data))
                {
                    var hidden = new HiddenPackage { Body = body, Active = body.gameObject.activeSelf, Kinematic = body.isKinematic };
                    if (!hidden.Suppressor.Suppress(data)) throw new InvalidOperationException("Cannot preserve guest bulb.");
                    _hiddenBulbs.Add(data, hidden); body.gameObject.SetActive(false);
                }
                _unreadyBulbs.Remove(body);
            }
            catch (Exception e) { FailBulbs(e); }
            return true;
        }

        private void FailBulbs(Exception e)
        {
            if (_bulbFactory == null || _bulbFactory.Failed) return;
            _bulbFactory.Failed = true;
            WinterMPPlugin.Log.LogWarning("Bulb sync disabled: " + e.Message);
            SyncEventLog.Record("bulb-disabled", e.Message);
        }

        private void ClearBulbs()
        {
            foreach (var pair in _bulbs)
            {
                var b = pair.Value;
                if (_items.TryGetValue(pair.Key, out var item) && item.Body == b.Body)
                { if (b.Body != null && item.KinematicSaved) b.Body.isKinematic = item.OriginalKinematic; RemoveTrackedItem(pair.Key, b.Body); }
                if (b.Replica && b.Data != null) { b.Data.enabled = false; UnityEngine.Object.Destroy(b.Data.gameObject); }
            }
            foreach (var pair in _hiddenBulbs)
            {
                if (pair.Key == null) continue;
                if (pair.Value.Body != null) pair.Value.Body.isKinematic = pair.Value.Kinematic;
                pair.Key.gameObject.SetActive(pair.Value.Active); pair.Value.Suppressor.Restore();
            }
            if (_bulbFactory != null)
            {
                if (_bulbFactory.Create != null && _bulbFactory.Hook != null) RemoveReplacementHook(_bulbFactory.Create, _bulbFactory.Hook);
                _bulbFactory.Suppressor.Restore();
            }
            _bulbFactory = null; _bulbOrdinal = 0; _bulbTick = 0;
            _bulbs.Clear(); _hiddenBulbs.Clear(); _unreadyBulbs.Clear(); _pendingBulbs.Clear();
        }
    }
}
