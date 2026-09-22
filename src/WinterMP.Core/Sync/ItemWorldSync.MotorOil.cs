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
        private sealed class MotorOilBottle
        {
            internal uint Id;
            internal string NativeId = string.Empty;
            internal Rigidbody Body = null!;
            internal PlayMakerFSM Use = null!;
            internal PlayMakerFSM? Source;
            internal GameObject? Particle;
            internal bool Replica;
            internal MotorOilBottleState? Observed;
            internal uint SentRevision;
            internal float NextSend;
        }
        private readonly Dictionary<uint, MotorOilBottle> _motorOil = new Dictionary<uint, MotorOilBottle>();
        private readonly Dictionary<uint, MotorOilBottleState> _pendingMotorOil = new Dictionary<uint, MotorOilBottleState>();
        private readonly Dictionary<uint, MotorOilBottleState> _receivedMotorOil = new Dictionary<uint, MotorOilBottleState>();
        private readonly Dictionary<Rigidbody, bool> _localMotorOil = new Dictionary<Rigidbody, bool>();
        private readonly List<Action> _motorOilRestore = new List<Action>();
        private GameObject? _motorOilPrefab;
        private float _motorOilProbe, _motorOilTick;
        private bool _motorOilFailed;

        private static PlayMakerFSM? MotorOilUse(Rigidbody body)
        {
            var c = SyncCatalog.MotorOil;
            if (body == null || c == null) return null;
            foreach (var f in body.GetComponents<PlayMakerFSM>()) if (f.FsmName == c["use"]) return f;
            return null;
        }
        private bool IsMotorOilBody(Rigidbody body)
        {
            var c = SyncCatalog.MotorOil; if (c == null || body == null) return false;
            foreach (var bottle in _motorOil.Values) if (bottle.Body == body) return true;
            var use = MotorOilUse(body);
            return body.name == c["itemName"] || FactoryItemIdentity.IsNativeId(body.name, c["prefix"])
                || use != null && FactoryItemIdentity.IsNativeId(use.FsmVariables.FindFsmString(c["id"])?.Value ?? "", c["prefix"]);
        }
        private void RefreshMotorOil()
        {
            var c = SyncCatalog.MotorOil; var session = SessionManager.Instance;
            if (c == null || session == null || _motorOilFailed || _motorOilPrefab != null || Time.unscaledTime < _motorOilProbe) return;
            _motorOilProbe = Time.unscaledTime + 2;
            try
            {
                foreach (var obj in ScenePath.ScanFsms())
                {
                    var f = obj as PlayMakerFSM;
                    if (f == null || f.FsmName != c["factoryFsm"] || ScenePath.Of(f.transform) != c["factoryPath"] || !f.Fsm.Started) continue;
                    var create = PackageStateActions(f, "Create product", "IntAdd", "CreateObject", "SetFsmInt", "SetParent", "ConvertIntToString", "BuildString", "SetName");
                    var prefab = f.FsmVariables.FindFsmGameObject(c["prefab"])?.Value;
                    if (prefab == null || prefab.name != c["prefix"] || prefab.GetComponent<Rigidbody>() == null
                        || PackageField<FsmGameObject>(create.Actions[1], "gameObject")?.Name != c["prefab"]
                        || PackageField<FsmString>(create.Actions[2], "variableName")?.Value != c["grade"])
                        throw new InvalidOperationException("Motor-oil factory shape changed.");
                    if (!session.IsHost)
                    {
                        // Separate shop products have their own factory. Suppress
                        // its global SPAWNITEM as well as ordinary updates.
                        var globals = f.Fsm.GlobalTransitions;
                        f.Fsm.GlobalTransitions = new FsmTransition[0];
                        _motorOilRestore.Add(() => f.Fsm.GlobalTransitions = globals);
                        var pause = new FsmSuppressor();
                        if (!pause.Suppress(f)) throw new InvalidOperationException("Cannot pause guest oil factory.");
                        _motorOilRestore.Add(pause.Restore);
                    }
                    _motorOilPrefab = prefab;
                    return;
                }
            }
            catch (Exception e) { FailMotorOil(e); }
        }
        private bool TryScanMotorOil(Rigidbody body)
        {
            if (!IsMotorOilBody(body)) return false;
            if (_motorOilFailed || _localMotorOil.ContainsKey(body)) return true;
            foreach (var bottle in _motorOil.Values) if (bottle.Body == body) return true;
            var c = SyncCatalog.MotorOil!; var use = MotorOilUse(body);
            if (_motorOilPrefab == null || use == null || !use.Fsm.Started) return true;
            string native = use.FsmVariables.FindFsmString(c["id"])?.Value ?? "";
            if (!FactoryItemIdentity.IsNativeId(native,c["prefix"])) return true;
            if (use.ActiveStateName != c["copy"] && use.ActiveStateName != c["empty"]
                && use.ActiveStateName != c["save"] && use.ActiveStateName != "Load 3") return true;
            try
            {
                if (SessionManager.Instance?.IsHost == false) { HideMotorOil(body); return true; }
                uint id = MotorOilPolicy.ItemId(native);
                if (_spawnLifecycle.IsRetired(id)) return true;
                if (_motorOil.TryGetValue(id, out var old) && old.Body != null) throw new InvalidOperationException("Duplicate saved motor-oil identity.");
                // Reconcile any earlier generic scan with the durable factory ID.
                foreach (var pair in new List<KeyValuePair<uint,SyncedItem>>(_items))
                    if (pair.Value.Body == body && pair.Key != id) RemoveTrackedItem(pair.Key,body);
                var bottle = BindMotorOil(body,use,native,false);
                ProtectMotorOilSave(bottle);
                BindMotorOilBody(id,body); _motorOil[id] = bottle;
                SyncEventLog.Record("motor-oil-bound", native);
            }
            catch (Exception e) { FailMotorOil(e); }
            return true;
        }
        private MotorOilBottle BindMotorOil(Rigidbody body, PlayMakerFSM use, string native, bool replica)
        {
            var c = SyncCatalog.MotorOil!;
            var empty = PackageStateActions(use,c["empty"],"SetName","DestroyObject","DestroyObject");
            if (PackageField<FsmString>(empty.Actions[0],"name")?.Value != c["emptyName"]
                || use.FsmVariables.FindFsmBool(c["consumed"])?.Value != false
                || use.FsmVariables.FindFsmFloat(c["fluid"]) == null || use.FsmVariables.FindFsmInt(c["grade"]) == null
                || use.FsmVariables.FindFsmFloat(c["viscosity"]) == null)
                throw new InvalidOperationException("Motor-oil saved variables changed.");
            PackageStateActions(use,c["save"],"BoolTest","GetFsmFloat","SaveTransform","SaveInt","SaveFloat");
            PackageStateActions(use,c["material"],"ArrayListGet","SetMaterial","ArrayListGet");
            PlayMakerFSM? source = null;
            var trigger = use.FsmVariables.FindFsmGameObject("Trigger")?.Value;
            if (trigger != null)
            {
                if (trigger.name != c["trigger"] || trigger.transform.parent != body.transform) throw new InvalidOperationException("Motor-oil source changed.");
                foreach (var f in trigger.GetComponents<PlayMakerFSM>()) if (f.FsmName == c["data"]) source = f;
                if (source == null) throw new InvalidOperationException("Motor-oil source Data missing.");
                if (!source.Fsm.Initialized) source.Fsm.Init(source);
                var clamp = PackageStateActions(source,"State 2","FloatClamp","SetFsmFloat","SendEvent","Wait");
                if (PackageField<FsmFloat>(clamp.Actions[0],"maxValue")?.Value != 4
                    || source.FsmVariables.FindFsmFloat(c["fluid"]) == null || source.FsmVariables.FindFsmFloat(c["viscosity"]) == null)
                    throw new InvalidOperationException("Motor-oil capacity changed.");
            }
            else if (replica || body.name != c["emptyName"]) throw new InvalidOperationException("Nonempty oil bottle has no source.");
            return new MotorOilBottle { Id=MotorOilPolicy.ItemId(native), NativeId=native, Body=body, Use=use, Source=source,
                Particle=use.FsmVariables.FindFsmGameObject("Particle")?.Value, Replica=replica };
        }
        private void ProtectMotorOilSave(MotorOilBottle bottle)
        {
            var use = bottle.Use;
            var save = PackageStateActions(use, SyncCatalog.MotorOil!["save"],
                "BoolTest", "GetFsmFloat", "SaveTransform", "SaveInt", "SaveFloat");
            var original = save.Actions;
            var read = original[1];
            OilExternal(read, use, "Trigger", "Fluid");
            var fluid = OilScalar(use, "Fluid");
            if (!ReferenceEquals(PackageField<FsmFloat>(read, "storeValue"), fluid)
                || !ReferenceEquals(PackageField<FsmFloat>(original[4], "saveValue"), fluid))
                throw new InvalidOperationException("Native motor-oil save source changed.");
            var actions = (FsmStateAction[])original.Clone();
            // Empty destroys Trigger. After a cold load, its native save getter
            // can recover the prefab's four litres instead of the retained Fluid.
            // Keep native save tags/actions, but never read a destroyed source.
            actions[1] = new FsmHookAction(() =>
            {
                if (bottle.Source != null) fluid.Value = OilScalar(bottle.Source, "Fluid").Value;
            });
            actions[1].Init(save);
            save.Actions = actions;
            _motorOilRestore.Add(() => { if (use != null) save.Actions = original; });
        }
        private void HideMotorOil(Rigidbody body)
        {
            if (_localMotorOil.ContainsKey(body)) return;
            _localMotorOil[body] = body.gameObject.activeSelf;
            foreach (var f in body.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                var globals=f.Fsm.GlobalTransitions; f.Fsm.GlobalTransitions=new FsmTransition[0];
                _motorOilRestore.Add(() => { if(f!=null)f.Fsm.GlobalTransitions=globals; });
                var pause=new FsmSuppressor(); if(!pause.Suppress(f))throw new InvalidOperationException("Cannot pause original oil.");
                _motorOilRestore.Add(pause.Restore);
            }
            foreach(var pair in new List<KeyValuePair<uint,SyncedItem>>(_items)) if(pair.Value.Body==body)RemoveTrackedItem(pair.Key,body);
            _trackedBodies[body]=true; body.gameObject.SetActive(false);
        }
        internal MotorOilBottleState? BuildMotorOilState(uint id)
        {
            if (_motorOilFailed || SessionManager.Instance?.IsHost != true || !_motorOil.TryGetValue(id,out var b)
                || b.Body == null || b.Use == null || _spawnLifecycle.IsRetired(id)) return null;
            try
            {
                var c=SyncCatalog.MotorOil!;
                int grade=b.Use.FsmVariables.FindFsmInt(c["grade"]).Value;
                if(grade<0 || grade>2)throw new InvalidOperationException("Invalid native motor-oil grade.");
                var s=new MotorOilBottleState { ItemId=id, Revision=b.Observed?.Revision??1, NativeId=b.NativeId,
                    Fluid=(b.Source!=null?b.Source:b.Use).FsmVariables.FindFsmFloat(c["fluid"]).Value,
                    Viscosity=b.Use.FsmVariables.FindFsmFloat(c["viscosity"]).Value,
                    Grade=(byte)grade, Empty=b.Body.name==c["emptyName"],
                    Position=b.Body.position.ToNet(), Rotation=b.Body.rotation.ToNet() };
                if (b.Observed!=null && !MotorOilPolicy.Same(b.Observed,s)) { if(++s.Revision==0)++s.Revision; }
                if(!MotorOilPolicy.Valid(s))throw new InvalidOperationException("Invalid native motor-oil state.");
                b.Observed=s; return s;
            }
            catch(Exception e){FailMotorOil(e);return null;}
        }
        internal IEnumerable<MotorOilBottleState> BuildMotorOilStates()
        { foreach(uint id in _motorOil.Keys){var state=BuildMotorOilState(id);if(state!=null)yield return state;} }
        private void ProcessMotorOil(SessionManager session)
        {
            if(_motorOilFailed)return;
            try
            {
                if (!_oilStartupReady) throw new InvalidOperationException("Motor-oil startup protection unavailable.");
                RefreshMotorOil(); ProcessOilRefill(session); if(_motorOilPrefab==null || Time.unscaledTime<_motorOilTick)return;
                _motorOilTick=Time.unscaledTime+.2f;
                if(!session.IsHost){ApplyPendingMotorOil();return;}
                foreach(var b in _motorOil.Values)
                {
                    if(b.Body==null)
                    { if(!_spawnLifecycle.IsRetired(b.Id)){AnnounceItemDespawn(b.Id,"motor oil removed");RecordItemRetirement(b.Id);} continue; }
                    var s=BuildMotorOilState(b.Id);if(s==null || s.Revision==b.SentRevision && Time.unscaledTime<b.NextSend)continue;
                    session.SendWorldMessage(s,Channel.ReliableOrdered);b.SentRevision=s.Revision;b.NextSend=Time.unscaledTime+5;
                }
            }
            catch(Exception e){FailMotorOil(e);}
        }
        private void FailMotorOil(Exception e)
        {
            if(_motorOilFailed)return;_motorOilFailed=true;
            WinterMPPlugin.Log.LogWarning("Motor-oil bottles disabled: "+e.Message);SyncEventLog.Record("motor-oil-disabled",e.Message);
        }
        private void ClearMotorOil()
        {
            ClearOilRefill();
            ClearMotorOilStartup();
            foreach(var b in _motorOil.Values)
            {
                if(b.Body!=null)
                {
                    if(b.Replica){ReleaseHeldBag(b.Body);UnityEngine.Object.Destroy(b.Body.gameObject);}
                    else if(_items.TryGetValue(b.Id,out var item) && item.KinematicSaved)b.Body.isKinematic=item.OriginalKinematic;
                }
                RemoveTrackedItem(b.Id,b.Body);
            }
            // Reactivate while the FSMs still have RestartOnEnable suppressed;
            // restoring their flags first makes Use restart and overwrite ID.
            foreach(var pair in _localMotorOil)if(pair.Key!=null){_trackedBodies.Remove(pair.Key);pair.Key.gameObject.SetActive(pair.Value);}
            for(int i=_motorOilRestore.Count-1;i>=0;i--)try{_motorOilRestore[i]();}catch(Exception e){WinterMPPlugin.Log.LogWarning("Motor-oil restore: "+e.Message);}
            _motorOil.Clear();_pendingMotorOil.Clear();_receivedMotorOil.Clear();_localMotorOil.Clear();_motorOilRestore.Clear();
            _motorOilPrefab=null;_motorOilFailed=false;_motorOilTick=_motorOilProbe=0;
        }
    }
}
