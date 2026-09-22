using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private void RefreshHouseholdFuses()
        {
            var c = SyncCatalog.HouseholdFuses; var session = SessionManager.Instance;
            if (_fuseFailed || _fuseHolders != null || c == null || session == null || Time.unscaledTime < _fuseProbeAt) return;
            _fuseProbeAt = Time.unscaledTime + 1;
            var tables = new FuseTableBinding[2]; var holders = new FuseHolderBinding[11];
            for (int home = 0; home < 2; home++)
            {
                var rule = c.Tables[home]; GameObject? database = null;
                foreach (var obj in ScenePath.ScanFsms())
                    if (obj is PlayMakerFSM f && f != null && f.FsmName == c["shockFsm"] && ScenePath.Of(f.transform) == rule.Database) database = f.gameObject;
                if (database == null) return;
                var sources = FuseList(database, c["holdersList"]); var slots = FuseList(database, c["slotsList"]); var power = FuseList(database, c["fusesList"]);
                if (sources.Count != rule.Holders.Length || slots.Count != sources.Count || power.Count != sources.Count) throw new InvalidOperationException("Changed household fuse table shape.");
                int offset = home == 0 ? 0 : 7;
                var table = new FuseTableBinding { Database = database, Offset = offset, Fuses = power, Shock = FuseFsm(database, c["shockFsm"]),
                    Slots = new PlayMakerFSM[sources.Count], Pivots = new Transform[sources.Count] };
                tables[home] = table;
                for (int i = 0; i < sources.Count; i++)
                {
                    if (sources[i] is not GameObject source || source == null || slots[i] is not GameObject slot || slot == null || power[i] is not bool)
                        throw new InvalidOperationException("Changed household fuse table entries.");
                    var use = FuseFsm(source, c["useFsm"]);
                    if (!use.Fsm.Initialized || !use.Fsm.Started || string.IsNullOrEmpty(use.FsmVariables.FindFsmString("ID")?.Value)) return;
                    if (use.FsmVariables.FindFsmString("ID").Value != rule.Holders[i] || use.FsmVariables.FindFsmGameObject("Database").Value != database)
                        throw new InvalidOperationException("Changed persistent household holder identity.");
                    var h = new FuseHolderBinding { Index = (byte)(offset + i), Home = home, Original = source, Object = source };
                    BindFuseHolder(h, c); holders[offset + i] = h;
                    table.Slots[i] = FuseFsm(slot, c["assemblyFsm"]);
                    if (!table.Slots[i].Fsm.Initialized || !table.Slots[i].Fsm.Started) return;
                    table.Pivots[i] = table.Slots[i].FsmVariables.FindFsmGameObject("Parent").Value.transform;
                    if (table.Slots[i].FsmVariables.FindFsmInt("ID").Value != i || table.Pivots[i].parent != slot.transform.parent)
                        throw new InvalidOperationException("Changed native household fuse socket.");
                    ValidateFuseSlot(table.Slots[i], table.Pivots[i], c);
                }
            }
            _fuseTables = tables; _fuseHolders = holders;
            try
            {
                if (!session.IsHost) InstallGuestFuses();
                else
                {
                    foreach (var table in tables) foreach (var slot in table.Slots)
                    {
                        PrepareFuseEntry(slot, c["assemblyState"]); PrepareFuseEntry(slot, c["assemblyIdle"]);
                        GuardNativeFuseFit(slot, c["assemblyWait"], table.Offset);
                        GuardNativeFuseFit(slot, c["assemblyState"], table.Offset);
                    }
                    foreach (var h in holders)
                    {
                        PrepareFuseEntry(h.Insert, c["assemblyState"]); PrepareFuseEntry(h.Removal, c["removeState"]);
                        PrepareFuseEntry(h.Screw, c["tightenState"]); PrepareFuseEntry(h.Screw, c["loosenState"]);
                        var state = FsmHook.FindState(h.Screw, "Wait 2")!; var original = state.Actions;
                        var replaced = (FsmStateAction[])original.Clone(); var shock = original[6];
                        var gate = new FsmHookAction(() => { if (_fuseRemoteActor == 255) shock.OnEnter(); }); gate.Init(state); replaced[6] = gate;
                        state.Actions = replaced; _fuseRestore.Add(() => { if (ReferenceEquals(state.Actions, replaced)) state.Actions = original; });
                    }
                }
                foreach (var h in holders) RegisterFuseBody(h);
                if (!session.IsHost && _remoteFuses != null) PresentHouseholdFuses(_remoteFuses);
                WinterMPPlugin.Log.LogInfo("Household fuses bound: 7 house + 4 apartment holders; " + (session.IsHost ? "native host authority" : "isolated guest replicas"));
            }
            catch { ClearHouseholdFuses(); throw; }
        }
        private static IList FuseList(GameObject go, string name)
        {
            IList? found = null;
            foreach (var component in go.GetComponents<MonoBehaviour>())
            {
                if (component == null || component.GetType().Name != "PlayMakerArrayListProxy") continue;
                var type = component.GetType();
                if ((string)type.GetField("referenceName").GetValue(component) != name) continue;
                if (found != null) throw new InvalidOperationException("Ambiguous fuse array.");
                found = type.GetProperty("arrayList")?.GetValue(component, null) as IList;
            }
            return found ?? throw new InvalidOperationException("Missing native fuse array " + name);
        }
        private static PlayMakerFSM FuseFsm(GameObject go, string name)
        {
            PlayMakerFSM? found = null;
            foreach (var f in go.GetComponents<PlayMakerFSM>()) if (f.FsmName == name)
            { if (found != null) throw new InvalidOperationException("Ambiguous household fuse FSM."); found = f; }
            return found ?? throw new InvalidOperationException("Missing household fuse FSM " + name);
        }
        private static T FuseField<T>(FsmStateAction action, string name) where T : class
            => PackageField<T>(action, name) ?? throw new InvalidOperationException("Missing household fuse action field " + name);

        private static void BindFuseHolder(FuseHolderBinding h, HouseholdFuseData c)
        {
            h.Use = FuseFsm(h.Object, c["useFsm"]); h.Screw = FuseFsm(h.Object, c["screwFsm"]); h.Removal = FuseFsm(h.Object, c["removalFsm"]);
            var trigger = h.Object.transform.Find(c["insertTrigger"]);
            if (trigger == null) throw new InvalidOperationException("Missing fuse insertion trigger.");
            h.Insert = FuseFsm(trigger.gameObject, c["assemblyFsm"]);
            foreach (var f in new[] { h.Use, h.Screw, h.Removal, h.Insert }) if (!f.Fsm.Initialized) f.Fsm.Init(f);
            h.Tightness = h.Use.FsmVariables.FindFsmFloat("Tightness") ?? throw new InvalidOperationException("Missing holder tightness.");
            h.Fuse = h.Use.FsmVariables.FindFsmInt("FuseState") ?? throw new InvalidOperationException("Missing fuse condition.");
            h.Mesh = h.Use.FsmVariables.FindFsmGameObject("MeshFuse").Value; h.Tip = h.Use.FsmVariables.FindFsmGameObject("MeshTip").Value;
            if (h.Mesh == null || h.Tip == null || !h.Mesh.transform.IsChildOf(h.Object.transform) || !h.Tip.transform.IsChildOf(h.Mesh.transform))
                throw new InvalidOperationException("Changed fuse meshes.");
            var insert = PackageStateActions(h.Insert, c["assemblyState"], "SetFsmInt", "ActivateGameObject", "SendEventByName", "ActivateGameObject");
            var insertTarget = FuseField<FsmEventTarget>(insert.Actions[2], "eventTarget");
            if (FuseField<FsmOwnerDefault>(insert.Actions[0], "gameObject").GameObject.Value != h.Object
                || FuseField<FsmString>(insert.Actions[0], "variableName").Value != "FuseState" || FuseField<FsmInt>(insert.Actions[0], "setValue").Value != 1
                || insertTarget.target != FsmEventTarget.EventTarget.GameObjectFSM || insertTarget.fsmName.Value != c["useFsm"] || !FitTargetVariable(insertTarget.gameObject, "Part") || FuseField<FsmString>(insert.Actions[2], "sendEvent").Value != "GARBAGE")
                throw new InvalidOperationException("Changed native fuse insertion output.");
            foreach (var pair in new[] { new KeyValuePair<string, float>(c["tightenState"], 1), new KeyValuePair<string, float>(c["loosenState"], -1) })
            {
                var state = PackageStateActions(h.Screw, pair.Key, "FloatAdd");
                if (FuseField<FsmFloat>(state.Actions[0], "floatVariable").Name != "Tightness" || FuseField<FsmFloat>(state.Actions[0], "add").Value != pair.Value)
                    throw new InvalidOperationException("Changed holder turn.");
            }
            var turn = PackageStateActions(h.Screw, "Wait 2", "SetBoolValue", "MasterAudioPlaySound", "FloatClamp", "SetFsmFloat", "SendEventByName", "SendEventByName", "SendEventByName", "Wait");
            var shockTarget = FuseField<FsmEventTarget>(turn.Actions[6], "eventTarget");
            if (FuseField<FsmFloat>(turn.Actions[2], "minValue").Value != 1 || FuseField<FsmFloat>(turn.Actions[2], "maxValue").Value != 8
                || FuseField<FsmString>(turn.Actions[3], "variableName").Value != "Tightness" || FuseField<FsmString>(turn.Actions[6], "sendEvent").Value != c["shockEvent"]
                || shockTarget.target != FsmEventTarget.EventTarget.GameObjectFSM || shockTarget.fsmName.Value != c["shockFsm"] || shockTarget.gameObject.GameObject.Value != h.Use.FsmVariables.FindFsmGameObject("Database").Value)
                throw new InvalidOperationException("Changed holder clamp/shock output.");
            PackageStateActions(h.Removal, c["removeState"], "SetBoolValue", "SetFsmFloat", "MasterAudioPlaySound", "SetTag", "SetLayer", "AddComponent", "SetProperty", "SetMass", "SetParent", "ActivateGameObject", "SendEventByName", "EnableFSM", "EnableFSM");
            PackageStateActions(h.Use, "Save", "SaveFloat", "SaveTransform", "SaveInt", "SaveInt");
        }
        private static void ValidateFuseSlot(PlayMakerFSM slot, Transform pivot, HouseholdFuseData c)
        {
            var assemble = PackageStateActions(slot, c["assemblyState"], "SetTag", "SetLayer", "SetBoolValue", "DestroyComponent", "SetParent");
            if (FuseField<FsmString>(assemble.Actions[3], "component").Value != "Rigidbody" || FuseField<FsmGameObject>(assemble.Actions[4], "parent").Value != pivot.gameObject)
                throw new InvalidOperationException("Changed holder fitting output.");
            PackageStateActions(slot, "End", "SetFsmGameObject", "SetFsmInt", "SendEventByName", "EnableFSM", "EnableFSM", "ActivateGameObject");
        }
        private void PrepareFuseEntry(PlayMakerFSM fsm, string state)
        {
            var events = fsm.Fsm.Events; var transitions = fsm.Fsm.GlobalTransitions;
            if (!FsmHook.EnsureRemoteEntry(fsm, state)) throw new InvalidOperationException("Missing native fuse entry.");
            var installedEvents = fsm.Fsm.Events; var installedTransitions = fsm.Fsm.GlobalTransitions;
            _fuseRestore.Add(() => { if (fsm != null && ReferenceEquals(fsm.Fsm.Events, installedEvents)) fsm.Fsm.Events = events;
                if (fsm != null && ReferenceEquals(fsm.Fsm.GlobalTransitions, installedTransitions)) fsm.Fsm.GlobalTransitions = transitions; });
        }
        private sealed class FuseFitGate : FsmStateAction
        {
            private readonly FsmStateAction[] _native;
            private readonly bool[] _enabled;
            private readonly Func<bool> _allowed;
            private readonly Action _cancel;
            internal FuseFitGate(FsmStateAction[] native, Func<bool> allowed, Action cancel)
            {
                _native = native; _allowed = allowed; _cancel = cancel; _enabled = new bool[native.Length];
                for (int i = 0; i < native.Length; i++) _enabled[i] = native[i].Enabled;
            }
            public override void OnEnter()
            {
                bool allowed = _allowed();
                for (int i = 0; i < _native.Length; i++) _native[i].Enabled = allowed && _enabled[i];
                if (allowed) Finish();
                else _cancel();
            }
            internal void Restore()
            {
                Enabled = false;
                for (int i = 0; i < _native.Length; i++) _native[i].Enabled = _enabled[i];
            }
        }
        private void GuardNativeFuseFit(PlayMakerFSM slot, string stateName, int homeOffset)
        {
            var state = FsmHook.FindState(slot, stateName) ?? throw new InvalidOperationException("Missing holder fit entry.");
            var native = state.Actions;
            var gate = new FuseFitGate(native, () =>
            {
                if (_fuseFailed || _fuseHolders == null) return false;
                var part = slot.FsmVariables.FindFsmGameObject("Part").Value;
                foreach (var h in _fuseHolders) if (h.Object == part) return HouseholdFusePolicy.SameHome(h.Index, homeOffset);
                return false;
            }, () => FsmHook.FireRemoteEntry(slot, SyncCatalog.HouseholdFuses!["assemblyIdle"]));
            gate.Init(state); var actions = new List<FsmStateAction>(native); actions.Insert(0, gate);
            var installed = actions.ToArray(); state.Actions = installed;
            // Keep blocked native actions disabled until the old synchronous entry loop exits.
            _fuseRestore.Add(() => { gate.Restore(); if (ReferenceEquals(state.Actions, installed)) state.Actions = native; });
        }
        private bool IsFusePickupBody(Rigidbody body)
        {
            foreach (var supply in _supplies.Values) if (supply.Body == body) return true;
            if (_fuseHolders != null) foreach (var h in _fuseHolders) if (h.Item?.Body == body) return true;
            return false;
        }
        private void GuardFusePartPickup()
        {
            if (_fusePickupGuardInstalled) return;
            EnsureBagPickup(); if (_bagPickup == null || _bagPickupGate == null) return;
            var state = PackageStateActions(_bagPickup, SyncCatalog.HouseholdFuses!["partPickupState"],
                "SendEventByName", "SetBoolValue", "GetPosition", "SetPosition", "SetPosition", "GetName", "StringCompare");
            var native = state.Actions; var gate = new BagPickupGate(this, native); gate.Init(state);
            var actions = new List<FsmStateAction>(native); actions.Insert(0, gate); var installed = actions.ToArray();
            state.Actions = installed; _fusePickupGuardInstalled = true;
            _fuseRestore.Add(() => { gate.Restore(); if (ReferenceEquals(state.Actions, installed)) state.Actions = native; });
        }
        private bool IsHouseholdFuseItem(SyncedItem item)
        {
            if (_fuseHolders == null) return false;
            foreach (var h in _fuseHolders) if (h.Item == item) return true;
            return false;
        }
        private bool TryScanHouseholdHolder(Rigidbody body)
        {
            foreach (var f in body.GetComponents<PlayMakerFSM>())
                if (f.FsmName == "Use" && f.FsmVariables.FindFsmInt("FuseState") != null && f.FsmVariables.FindFsmFloat("Tightness") != null) return true;
            return false;
        }
        private bool FuseHolderLoose(FuseHolderBinding h) => h.Last?.Slot == 255;
        private void RegisterFuseBody(FuseHolderBinding h)
        {
            var body = h.Object.GetComponent<Rigidbody>();
            if (body == null) { RemoveFuseMotion(h); return; }
            if (h.Item != null && h.Item.Body == body && _items.ContainsKey(h.Item.Id)) return;
            RemoveFuseMotion(h); uint id = HouseholdFusePolicy.ItemId(h.Index);
            if (_items.ContainsKey(id)) throw new InvalidOperationException("Household holder item collision.");
            h.Item = new SyncedItem { Id = id, Body = body, Path = "household:fuseholder:" + h.Index, LastPosition = body.position,
                LastMovedAt = Time.unscaledTime, KinematicSaved = true, OriginalKinematic = body.isKinematic };
            _items.Add(id, h.Item); _trackedBodies[body] = true;
        }
        private void RemoveFuseMotion(FuseHolderBinding h)
        {
            var item = h.Item; if (item == null) return;
            ReleaseHeldBag(item.Body); RestoreCargoPhysics(item); _pendingItemPoses.Remove(item.Id);
            RemoveTrackedItem(item.Id, item.Body); h.Item = null;
        }
        private void ClearHouseholdFuses()
        {
            if (_fuseHolders != null) foreach (var h in _fuseHolders) RemoveFuseMotion(h);
            foreach (var clone in _fuseClones) if (clone != null)
            { foreach (var f in clone.GetComponentsInChildren<PlayMakerFSM>(true)) f.enabled = false; UnityEngine.Object.Destroy(clone); }
            _fuseClones.Clear();
            for (int i = _fuseRestore.Count - 1; i >= 0; i--) try { _fuseRestore[i](); } catch (Exception e) { WinterMPPlugin.Log.LogWarning("Household fuse restore: " + e.Message); }
            _fuseRestore.Clear(); _fuseHolders = null; _fuseTables = null; _remoteFuses = null; _fuseFailed = _fusePickupGuardInstalled = false;
            _fuseSequences.Clear(); _pendingFuseIntents.Clear(); _fuseProbeAt = _fuseTickAt = 0; _fuseRevision = _fuseIntentSequence = 0;
        }
    }
}
