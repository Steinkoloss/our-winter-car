using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;

namespace WinterMP.Core.Sync
{
    internal sealed partial class FirewoodDeliverySync
    {
        private void Locate(bool host)
        {
            if (Application.loadedLevelName != "GAME") return;
            SyncCatalog.EnsureLoaded();
            var c = SyncCatalog.FirewoodDelivery ?? throw new InvalidOperationException("Missing firewood delivery catalog.");
            PlayMakerFSM? load = null, ground = null;
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null) continue;
                string path = ScenePath.Of(fsm.transform);
                if (path == c["loadPath"] && fsm.FsmName == c["loadFsm"])
                { if (load != null) throw new InvalidOperationException("Ambiguous wood load."); load = fsm; }
                if (path == c["groundPath"] && fsm.FsmName == c["groundFsm"])
                { if (ground != null) throw new InvalidOperationException("Ambiguous wood ground check."); ground = fsm; }
            }
            if (load == null || ground == null || !load.Fsm.Started || load.ActiveStateName != c["idle"]) return;
            if (!ground.Fsm.Initialized) ground.Fsm.Init(ground);
            foreach (var fsm in new[] { load, ground }) foreach (var s in fsm.Fsm.States) if (!s.IsInitialized) return;
            var values = new FsmFloat[5]; string[] keys = { "logs", "firewood", "mass", "bedScale", "unloaded" };
            for (int i = 0; i < values.Length; i++) values[i] = load.FsmVariables.FindFsmFloat(c[keys[i]]) ?? throw new InvalidOperationException("Missing trailer value " + c[keys[i]]);
            var unload = load.FsmVariables.FindFsmBool(c["unload"]) ?? throw new InvalidOperationException("Missing native unload flag.");
            var bed = load.transform.parent.GetComponent<Rigidbody>();
            var bedPile = Object(load, c["bedPile"]).transform;
            var prefab = Object(ground, c["pilePrefab"]);
            if (bed == null || prefab.transform.Find(c["meshChild"]) == null || prefab.GetComponentsInChildren<PlayMakerFSM>(true).Length != 0)
                throw new InvalidOperationException("Changed native wood pile or bed physics.");
            foreach (string key in new[] { "groundPile", "groundMesh" })
                if (load.FsmVariables.FindFsmGameObject(c[key]) == null) throw new InvalidOperationException("Missing native pile target.");
            if (ground.FsmVariables.FindFsmGameObject(c["newGroundPile"]) == null) throw new InvalidOperationException("Missing new native pile.");
            Signature(load, c["start"], "FloatCompare", "ActivateGameObject", "Wait");
            Signature(load, c["idle"], "TriggerEvent", "BoolTest");
            Signature(load, c["visual"], "FloatClamp", "FloatOperator", "FloatOperator", "FloatOperator", "SetScale", "SetMass");
            Signature(load, c["reset"], "SetFloatValue", "SetFloatValue", "SetFloatValue", "SetFloatValue", "SetBoolValue", "SetScale", "SetScale");
            Signature(load, c["animate"], "GetFsmGameObject", "FindChild", "FloatSubtract", "FloatClamp", "FloatAdd", "FloatClamp", "FloatOperator", "FloatOperator", "SetScale", "SetScale", "FloatOperator", "FloatOperator", "SetProperty", "FloatCompare", "BoolTest");
            _c = c; _load = load; _ground = ground; _bed = bed; _bedPile = bedPile; _prefab = prefab; _values = values; _unload = unload; _guest = !host;
            _originalValues = new float[values.Length];
            for (int i = 0; i < values.Length; i++) _originalValues[i] = values[i].Value;
            _originalUnload = unload.Value; _originalMass = bed.mass; _originalScale = bedPile.localScale.z;
            foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
            {
                var pile = (GameObject)obj;
                if (pile.name != prefab.name + "(Clone)" || pile.transform.Find(c["meshChild"]) == null) continue;
                if (host) _piles.Add(pile);
                else { _originalPiles.Add(pile); _originalPileVisible.Add(pile.activeSelf); pile.SetActive(false); }
            }
            if (_guest)
            {
                if (!_loadPause.Suppress(load) || !_groundPause.Suppress(ground)) throw new InvalidOperationException("Cannot pause guest wood delivery.");
                _shownUnload = false; unload.Value = false;
            }
            else
            {
                Hook(c["visual"], () => _epoch = unchecked(_epoch + 1));
                Hook(c["reset"], () => _epoch = unchecked(_epoch + 1));
                // Closing the hatch halfway through must resume the same ground
                // pile; vanilla's re-entry would create another full-sized pile.
                Hook(c["start"], () => {
                    if (_values[4].Value > 0 && _ground != null && _ground.FsmVariables.FindFsmGameObject(c["newGroundPile"]).Value != null)
                        FsmHook.FireRemoteEntry(load, c["animate"]);
                });
                if (!FsmHook.EnsureRemoteEntry(load, c["animate"])) throw new InvalidOperationException("Missing wood resume state.");
            }
            WinterMPPlugin.Log.LogInfo("Firewood delivery sync: native flatbed load and ground piles bound.");
        }

        private static GameObject Object(PlayMakerFSM fsm, string name) => fsm.FsmVariables.FindFsmGameObject(name)?.Value
            ?? throw new InvalidOperationException("Missing wood object " + name);
        private static void Signature(PlayMakerFSM fsm, string state, params string[] types)
        {
            var s = FsmHook.FindState(fsm, state) ?? throw new InvalidOperationException("Missing wood state " + state);
            for (int i = 0; i < types.Length; i++)
                if (FsmHook.NativeAction(s, i)?.GetType().Name != types[i]) throw new InvalidOperationException("Changed wood state " + state + "/" + i);
            if (FsmHook.NativeAction(s, types.Length) != null) throw new InvalidOperationException("Extra wood action " + state);
        }
        private void Hook(string name, Action callback)
        {
            if (!FsmHook.OnStateEnter(_load!, name, callback, out var hook) || hook == null) throw new InvalidOperationException("Wood state hook unavailable.");
            _hooks.Add(new System.Collections.Generic.KeyValuePair<FsmState, FsmStateAction>(FsmHook.FindState(_load!, name)!, hook));
        }
    }
}
