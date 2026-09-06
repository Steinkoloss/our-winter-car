using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private readonly Dictionary<PlayMakerFSM, GuestPartIsolation> _isolatedGuestParts = new Dictionary<PlayMakerFSM, GuestPartIsolation>();
        private readonly Dictionary<PlayMakerFSM, FsmSuppressor> _isolatedGuestMounts = new Dictionary<PlayMakerFSM, FsmSuppressor>();
        private GameObject? _guestPartStorage;

        internal bool IsPendingGuestPartIsolation(PlayMakerFSM data)
        {
            var session = SessionManager.Instance;
            return session != null && !session.IsHost && GuestSaveGuard.ProtectWorld
                && !_bridge.PartIdentities.IsReplica(data) && IsReplacementPart(data);
        }

        internal void PrepareGuestPartIsolation()
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.State != SessionState.Connected || !GuestSaveGuard.ProtectWorld) return;
            RefreshReplacementFactories();
            ScanNativeParts();
            ProcessReplacementOutputs(session);
            IsolateGuestParts(session);
        }

        private void IsolateGuestParts(SessionManager session)
        {
            var c = SyncCatalog.ReplacementParts;
            if (session.IsHost || !GuestSaveGuard.ProtectWorld || c == null) return;
            foreach (var pair in new List<KeyValuePair<uint, PlayMakerFSM>>(_nativeParts))
            {
                var data = pair.Value;
                if (data == null || _bridge.PartIdentities.IsReplica(data) || _isolatedGuestParts.ContainsKey(data)) continue;
                var phase = NativePartIdentity.Phase(data);
                if (phase != NativePartPhase.Loose && phase != NativePartPhase.Fitted) continue;
                string nativeId = data.FsmVariables.FindFsmString(c["itemIdVariable"]).Value;
                foreach (var factory in _replacementFactories.Values)
                {
                    if (factory.Failed || !factory.Suppressor.Active || !factory.Rule.Identity.TryId(nativeId, out uint id) || id != pair.Key) continue;
                    try
                    {
                        // All 30 catalogued templates are leaves. Preserve an
                        // unexpected nested assembly instead of hiding unrelated parts.
                        foreach (var child in data.GetComponentsInChildren<PlayMakerFSM>(true))
                            if (child != data && child.FsmName == c["itemFsm"])
                                throw new InvalidOperationException("Replacement contains another part or mount.");
                        if (phase == NativePartPhase.Fitted && !TryIsolateGuestMount(data, factory)) break;
                        if (_guestPartStorage == null)
                        {
                            _guestPartStorage = new GameObject("WinterMP saved guest parts");
                            _guestPartStorage.SetActive(false);
                        }
                        RemoveNativeItemMotion(id);
                        _bridge.ForgetNativePartBindings(data);
                        var saved = new GuestPartIsolation(data.gameObject, _guestPartStorage.transform);
                        _isolatedGuestParts.Add(data, saved);
                        _bridge.PartIdentities.MarkIsolated(data);
                        _nativeParts.Remove(id);
                        _pendingReplacements.Add(id);
                        SyncEventLog.Record("guest-part-isolated", nativeId + " " + id.ToString("X8"));
                    }
                    catch (Exception e) { FailReplacementFactory(factory, e); }
                    break;
                }
            }
        }

        private bool TryIsolateGuestMount(PlayMakerFSM data, ReplacementFactory factory)
        {
            var c = SyncCatalog.ReplacementParts!;
            var point = data.FsmVariables.FindFsmGameObject(c["installPointVariable"])?.Value;
            if (point == null || data.transform.parent != point.transform)
                return false;
            PlayMakerFSM? mount = null;
            foreach (var fsm in point.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == c["itemFsm"])
                {
                    if (mount != null) throw new InvalidOperationException("Ambiguous native mount.");
                    mount = fsm;
                }
            if (mount == null || !mount.Fsm.Started
                || mount.FsmVariables.FindFsmGameObject(c["mountPointVariable"])?.Value != point
                || mount.FsmVariables.FindFsmGameObject(c["mountPartVariable"])?.Value != data.gameObject
                || mount.FsmVariables.FindFsmBool(c["mountInstalledVariable"])?.Value != true)
                return false;
            if (_isolatedGuestMounts.ContainsKey(mount)) return true;
            _bridge.ForgetNativePartBindings(mount, descendants: false);
            ValidatePartFitMount(mount, c, factory.Rule.SlotCount != 0);
            var suppressor = new FsmSuppressor();
            if (!suppressor.Suppress(mount)) throw new InvalidOperationException("Cannot pause native mount.");
            _isolatedGuestMounts.Add(mount, suppressor);
            return true;
        }

        private bool PartFitMountReady(PlayMakerFSM mount)
        {
            if (_isolatedGuestMounts.TryGetValue(mount, out var paused))
                return paused.Active && mount.Fsm.Started && mount.gameObject.activeInHierarchy;
            return FitFsmReady(mount);
        }

        private bool GuestPartMountFree(PlayMakerFSM mount)
        {
            var c = SyncCatalog.ReplacementParts!;
            if (!TryMountAddress(mount.transform, out var kind, out uint id, out string path)) return false;
            if (_replacementReplica == null || _replacementReplica.Occupies(kind, id, path)) return false;
            // An unrecognised local occupant is still preserved and must not be
            // overlaid by a supported replacement. Only our paused mounts ignore
            // their deliberately retained vanilla Installed/Part values.
            return _isolatedGuestMounts.ContainsKey(mount)
                || mount.FsmVariables.FindFsmBool(c["mountInstalledVariable"])?.Value == false;
        }

        private bool TryMountAddress(Transform parent, out PartParentKind kind, out uint id, out string path)
        {
            kind = PartParentKind.None; id = 0; path = string.Empty;
            for (var current = parent; current != null; current = current.parent)
            {
                foreach (var fsm in current.GetComponents<PlayMakerFSM>())
                    if (NativePartIdentity.IsData(fsm))
                    {
                        if (!_bridge.PartIdentities.TryRootId(fsm, out id)) return false;
                        kind = PartParentKind.NativePart;
                        path = ScenePath.RelativeTo(parent, current) ?? string.Empty;
                        return PartAttachmentPolicy.ValidPath(path);
                    }
                foreach (var item in _items.Values)
                    if (item.IsVehicle && item.Body != null && item.Body.transform == current)
                    {
                        kind = PartParentKind.Vehicle; id = item.Id;
                        path = ScenePath.RelativeTo(parent, current) ?? string.Empty;
                        return PartAttachmentPolicy.ValidPath(path);
                    }
            }
            return false;
        }

        private void RestoreIsolatedGuestParts()
        {
            bool applying = _bridge.ApplyingRemote;
            try
            {
                _bridge.ApplyingRemote = true;
                foreach (var pair in _isolatedGuestParts)
                {
                    try { pair.Value.Restore(); }
                    catch (Exception e) { WinterMPPlugin.Log.LogWarning("WorldSync: guest part restore: " + e.Message); }
                    _bridge.PartIdentities.UnmarkIsolated(pair.Key);
                }
                foreach (var mount in _isolatedGuestMounts.Values) mount.Restore();
                _isolatedGuestParts.Clear(); _isolatedGuestMounts.Clear();
                if (_guestPartStorage != null && _guestPartStorage.transform.childCount == 0) UnityEngine.Object.Destroy(_guestPartStorage);
                else if (_guestPartStorage != null)
                    WinterMPPlugin.Log.LogWarning("WorldSync: saved guest parts retained inactive because their original hierarchy could not be restored; restart to reload the local save.");
                _guestPartStorage = null;
            }
            finally { _bridge.ApplyingRemote = applying; }
        }
    }
}
