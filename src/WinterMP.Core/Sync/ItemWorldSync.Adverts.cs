using System;
using System.Collections;
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
        private sealed class AdvertBox
        {
            internal byte Index;
            internal PlayMakerFSM Fsm = null!;
            internal uint Reserved;
            internal float Deadline;
        }
        private sealed class AdvertSheet
        {
            internal Rigidbody Body = null!;
            internal bool Replica;
            internal float NextSend;
        }
        private PlayMakerFSM? _adData, _adUse;
        private Rigidbody? _adPile;
        private GameObject? _adSpawn, _adPrefab;
        private Transform? _adScale;
        private IList? _adBoxes;
        private readonly Dictionary<byte, AdvertBox> _adMailboxes = new Dictionary<byte, AdvertBox>();
        private readonly Dictionary<uint, AdvertSheet> _adSheets = new Dictionary<uint, AdvertSheet>();
        private readonly Dictionary<uint, AdvertSheetState> _pendingAdSheets = new Dictionary<uint, AdvertSheetState>();
        private readonly Dictionary<Rigidbody, bool> _hiddenAdSheets = new Dictionary<Rigidbody, bool>();
        private readonly List<Action> _adRestore = new List<Action>();
        private readonly Dictionary<byte, uint> _adSequences = new Dictionary<byte, uint>();
        private AdvertJobState? _adLast, _adReceived, _adPending;
        private uint _adPileId, _adOrdinal, _adSequence;
        private bool _adHost, _adFailed, _adAllowTake, _adPresenting;
        private byte _adAllowBox = 255;
        private float _adProbe, _adTick, _adNextSend;

        internal static bool IsAdvertFsm(PlayMakerFSM f)
        {
            var c = SyncCatalog.Adverts;
            return c != null && (f.FsmName == c["mailboxFsm"] || f.gameObject.name == c["pile"] || ScenePath.Of(f.transform) == c["root"]);
        }
        private void RefreshAdverts()
        {
            var c = SyncCatalog.Adverts; var session = SessionManager.Instance;
            if (c == null || session == null || _adData != null || _adFailed || Time.unscaledTime < _adProbe) return;
            _adProbe = Time.unscaledTime + 2;
            try
            {
                PlayMakerFSM? data = null, reset = null;
                foreach (var obj in ScenePath.ScanFsms())
                {
                    var f = obj as PlayMakerFSM; if (f == null || ScenePath.Of(f.transform) != c["root"]) continue;
                    if (f.FsmName == c["data"]) data = f;
                    if (f.FsmName == c["reset"]) reset = f;
                }
                if (data == null || reset == null || !data.Fsm.Started) return;
                _adData = data; _adHost = session.IsHost;
                var pile = data.FsmVariables.FindFsmGameObject("Pile")?.Value;
                _adSpawn = data.FsmVariables.FindFsmGameObject("AdvertSpawn")?.Value;
                if (pile == null || _adSpawn == null || ScenePath.Of(_adSpawn.transform) != c["root"] + "/" + c["spawn"])
                    throw new InvalidOperationException("Advert pile/spawn references changed.");
                _adPile = pile.GetComponent<Rigidbody>(); _adUse = CoffeeFsm(pile.transform, c["use"]); _adScale = pile.transform.Find(c["scale"]);
                _adPrefab = _adUse.FsmVariables.FindFsmGameObject("Prefab")?.Value;
                if (_adPile == null || _adScale == null || _adPrefab == null || _adPrefab.name + "(Clone)" != c["sheetName"]
                    || _adPrefab.GetComponent<Rigidbody>() == null || _adPrefab.GetComponentsInChildren<PlayMakerFSM>(true).Length != 0)
                    throw new InvalidOperationException("Advert sheet or pile shape changed.");
                foreach (var component in data.GetComponents<MonoBehaviour>())
                    if (component != null && component.GetType().Name == "PlayMakerArrayListProxy"
                        && component.GetType().GetField("referenceName")?.GetValue(component) as string == "")
                    {
                        if (_adBoxes != null) throw new InvalidOperationException("Ambiguous advert list.");
                        _adBoxes = component.GetType().GetProperty("arrayList")?.GetValue(component, null) as IList;
                    }
                if (_adBoxes == null || _adBoxes.Count != 28) throw new InvalidOperationException("Advert saved list must contain 28 flags.");
                foreach (var flag in _adBoxes) if (!(flag is bool)) throw new InvalidOperationException("Advert list type changed.");
                ValidateAdverts(c);
                _adPileId = StableHash.Fnv1a32("adverts:pile:" + c["root"]);
                BindAdvertBody(_adPileId, _adPile);
                if (!session.IsHost)
                {
                    RememberAdvertFsm(data); RememberAdvertFsm(_adUse);
                    var oldFlags = new object[28]; _adBoxes.CopyTo(oldFlags, 0); var savedList = _adBoxes;
                    _adRestore.Add(() => { for (int i = 0; i < 28; i++) savedList[i] = oldFlags[i]; });
                    RememberAdvertObject(pile); RememberAdvertObject(_adSpawn);
                    var scale = _adScale; var size = scale.localScale; _adRestore.Add(() => { if (scale != null) scale.localScale = size; });
                    PauseAdvert(data); PauseAdvert(reset);
                }
                InstallAdvertPile(c);
                foreach (var obj in ScenePath.ScanFsms())
                {
                    var f = obj as PlayMakerFSM;
                    if (f == null || f.FsmName != c["mailboxFsm"] || !c.Boxes.ContainsValue(AdvertMailboxPath(f.transform))) continue;
                    if (!f.Fsm.Initialized) f.Fsm.Init(f);
                    int index = f.FsmVariables.FindFsmInt(c["index"])?.Value ?? -1;
                    if (!c.Boxes.TryGetValue(index, out var path) || path != AdvertMailboxPath(f.transform)
                        || f.FsmVariables.FindFsmGameObject(c["db"])?.Value != data.gameObject)
                        throw new InvalidOperationException("Advert mailbox identity changed: " + index + " " + ScenePath.Of(f.transform));
                    byte slot = (byte)index;
                    if (_adMailboxes.ContainsKey(slot)) throw new InvalidOperationException("Duplicate advert mailbox index.");
                    var box = new AdvertBox { Index = slot, Fsm = f }; _adMailboxes.Add(slot, box);
                    InstallAdvertBox(box, c, session.IsHost);
                    KeepAdvertMailboxAvailable(f);
                }
                if (_adMailboxes.Count != c.Boxes.Count) throw new InvalidOperationException("Advert mailbox inventory incomplete.");
                SyncEventLog.Record("adverts-bound", "27 mailboxes; 28 native saved flags");
            }
            catch (Exception e) { FailAdverts(e); }
        }
        private static string AdvertMailboxPath(Transform node)
        {
            // The asset catalog records literal names; ScenePath adds sibling
            // suffixes. BoxIndex distinguishes these repeated mailbox paths.
            var names = new List<string>();
            for (var current = node; current != null; current = current.parent) names.Add(current.name);
            names.Reverse(); return string.Join("/", names.ToArray());
        }
        private void KeepAdvertMailboxAvailable(PlayMakerFSM hatch)
        {
            var mailbox = hatch.transform.parent;
            if (mailbox == null || mailbox.name != SyncCatalog.Adverts!["mailboxRoot"]) throw new InvalidOperationException("Advert mailbox root changed.");
            var lod = mailbox.parent;
            if (lod == null || lod.name != SyncCatalog.Adverts!["lod"]) return;
            if (lod.parent == null) throw new InvalidOperationException("Advert mailbox LOD has no scene parent.");
            // Native house LOD tests only the local camera. Keep these small
            // shared targets outside it so a distant guest can deliver while
            // the host leaves the house and its unrelated NPCs unloaded.
            RememberAdvertObject(mailbox.gameObject);
            mailbox.parent = lod.parent;
        }
        private void BindAdvertBody(uint id, Rigidbody body)
        {
            if (_items.TryGetValue(id, out var old) && old.Body != null && old.Body != body) throw new InvalidOperationException("Advert identity collision.");
            if (old == null) _items[id] = new SyncedItem { Id = id, Body = body, Path = ScenePath.Of(body.transform), LastPosition = body.position };
            _trackedBodies[body] = true;
        }
        private static bool IsAdvertBody(Rigidbody body)
        {
            var c = SyncCatalog.Adverts;
            return c != null && (body.name == c["pile"] || body.name == c["sheetName"]);
        }
        private bool TryScanAdvert(Rigidbody body)
        {
            if (!IsAdvertBody(body)) return false;
            if (_adData == null || _adFailed || body == _adPile) return true;
            foreach (var sheet in _adSheets.Values) if (sheet.Body == body) return true;
            if (SessionManager.Instance?.IsHost == true) RegisterAdvertSheet(body);
            else if (!_hiddenAdSheets.ContainsKey(body)) { _hiddenAdSheets.Add(body, body.gameObject.activeSelf); body.gameObject.SetActive(false); }
            return true;
        }
        private uint RegisterAdvertSheet(Rigidbody body)
        {
            uint id;
            do { id = StableHash.Fnv1a32("advert:sheet:" + (++_adOrdinal).ToString(CultureInfo.InvariantCulture)); }
            while (id == 0 || _items.ContainsKey(id) || _spawnLifecycle.IsRetired(id));
            _adSheets.Add(id, new AdvertSheet { Body = body }); BindAdvertBody(id, body);
            SyncEventLog.Record("advert-sheet", id.ToString("X8")); return id;
        }
        private void PauseAdvert(PlayMakerFSM f)
        {
            var pause = new FsmSuppressor(); if (!pause.Suppress(f)) throw new InvalidOperationException("Cannot pause guest advert writer.");
            _adRestore.Add(pause.Restore);
        }
        private void RememberAdvertObject(GameObject obj)
        {
            var parent = obj.transform.parent; var pos = obj.transform.localPosition; var rot = obj.transform.localRotation; bool active = obj.activeSelf;
            _adRestore.Add(() => { if (obj == null) return; obj.transform.parent = parent; obj.transform.localPosition = pos; obj.transform.localRotation = rot; obj.SetActive(active); });
        }
        private void RememberAdvertFsm(PlayMakerFSM f)
        {
            foreach (var v in f.FsmVariables.IntVariables) { int old = v.Value; _adRestore.Add(() => v.Value = old); }
            foreach (var v in f.FsmVariables.FloatVariables) { float old = v.Value; _adRestore.Add(() => v.Value = old); }
            foreach (var v in f.FsmVariables.BoolVariables) { bool old = v.Value; _adRestore.Add(() => v.Value = old); }
        }
        private void FailAdverts(Exception e)
        {
            if (_adFailed) return;
            _adFailed = true;
            WinterMPPlugin.Log.LogWarning("Advert sync disabled: " + e.Message); SyncEventLog.Record("adverts-disabled", e.Message);
        }
        private void ClearAdverts()
        {
            if (!_adHost)
                foreach (var box in _adMailboxes.Values)
                    if (box.Fsm != null && box.Fsm.Fsm.Started && box.Fsm.enabled) FsmHook.FireRemoteEntry(box.Fsm, SyncCatalog.Adverts!["mailboxReset"]);
            foreach (var pair in _adSheets)
            {
                if (pair.Value.Replica && pair.Value.Body != null)
                {
                    ReleaseHeldBag(pair.Value.Body);
                    UnityEngine.Object.Destroy(pair.Value.Body.gameObject);
                }
                RemoveTrackedItem(pair.Key, pair.Value.Body);
            }
            if (_adPile != null)
            {
                if (_items.TryGetValue(_adPileId, out var item) && item.KinematicSaved) _adPile.isKinematic = item.OriginalKinematic;
                RemoveTrackedItem(_adPileId, _adPile);
            }
            for (int i = _adRestore.Count - 1; i >= 0; i--)
                try { _adRestore[i](); } catch (Exception e) { WinterMPPlugin.Log.LogWarning("Advert restore: " + e.Message); }
            foreach (var pair in _hiddenAdSheets) if (pair.Key != null) pair.Key.gameObject.SetActive(pair.Value);
            _adRestore.Clear(); _adMailboxes.Clear(); _adSheets.Clear(); _hiddenAdSheets.Clear(); _pendingAdSheets.Clear(); _adSequences.Clear();
            _adData = _adUse = null; _adPile = null; _adSpawn = _adPrefab = null; _adScale = null; _adBoxes = null;
            _adLast = _adReceived = _adPending = null; _adOrdinal = _adPileId = _adSequence = 0; _adProbe = _adTick = _adNextSend = 0;
            _adAllowBox = 255; _adHost = _adFailed = _adAllowTake = _adPresenting = false;
        }
    }
}
