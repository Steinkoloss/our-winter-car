using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private const string ReplacementPoseState = "WinterMP replacement pose";
        private sealed class ReplacementCollider
        {
            public Collider Collider = null!;
            public bool Enabled, Trigger;
        }
        private sealed class ReplacementBody
        {
            public Rigidbody Body = null!;
            public bool Kinematic, Collisions;
        }

        private void CaptureReplacementAttachment(ReplacementBinding binding, ReplacementPartState state)
        {
            if (!state.Installed) return;
            var c = SyncCatalog.ReplacementParts!;
            var data = binding.Data;
            var install = data.FsmVariables.FindFsmGameObject(c["installPointVariable"])?.Value;
            if (install == null) return;
            PlayMakerFSM? mount = null;
            foreach (var fsm in install.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == c["itemFsm"]) { mount = fsm; break; }
            // AssemblyID is set before the native mount reparents/destroys the body.
            // Publish attachment only after the mount and the actual hierarchy agree.
            var parent = data.transform.parent;
            if (mount == null || parent == null || mount.FsmVariables.FindFsmGameObject(c["mountPartVariable"])?.Value != data.gameObject
                || mount.FsmVariables.FindFsmGameObject(c["mountPointVariable"])?.Value != parent.gameObject
                || mount.FsmVariables.FindFsmBool(c["mountInstalledVariable"])?.Value != true) return;
            for (var current = parent; current != null; current = current.parent)
            {
                foreach (var fsm in current.GetComponents<PlayMakerFSM>())
                {
                    if (!NativePartIdentity.IsData(fsm)) continue;
                    if (!_bridge.PartIdentities.TryRootId(fsm, out uint rootId)) return;
                    SetReplacementAttachment(state, PartParentKind.NativePart, rootId, parent, current, data.transform);
                    return;
                }
                foreach (var item in _items.Values)
                    if (item.IsVehicle && item.Body != null && item.Body.transform == current)
                    {
                        SetReplacementAttachment(state, PartParentKind.Vehicle, item.Id, parent, current, data.transform);
                        return;
                    }
            }
        }

        private static void SetReplacementAttachment(ReplacementPartState state, PartParentKind kind, uint rootId,
            Transform parent, Transform root, Transform part)
        {
            string? path = ScenePath.RelativeTo(parent, root);
            if (path == null || !PartAttachmentPolicy.ValidPath(path)) return;
            state.ParentKind = kind; state.ParentId = rootId; state.ParentPath = path;
            state.LocalPosition = part.localPosition.ToNet(); state.LocalRotation = part.localRotation.ToNet();
            state.LocalScale = part.localScale.ToNet();
            PartIdentity.TryItemId(state.NativeId, out uint id);
            if (!PartAttachmentPolicy.Valid(state, id)) throw new InvalidOperationException("Invalid native part attachment.");
        }

        private Transform? ResolveReplacementParent(ReplacementPartState state)
        {
            if (!PartAttachmentPolicy.HasAttachment(state) || _spawnLifecycle.IsRetired(state.ParentId)) return null;
            Transform? root = null;
            if (state.ParentKind == PartParentKind.NativePart && _nativeParts.TryGetValue(state.ParentId, out var data)
                && data != null && NativePartIdentity.Phase(data) != NativePartPhase.Retired)
                root = data.transform;
            else if (state.ParentKind == PartParentKind.Vehicle && _items.TryGetValue(state.ParentId, out var item)
                && item.IsVehicle && item.Body != null) root = item.Body.transform;
            if (root == null || !root.gameObject.activeInHierarchy) return null;
            var parent = ScenePath.FindRelative(root, state.ParentPath);
            if (parent == null || !parent.gameObject.activeInHierarchy) return null;
            PartIdentity.TryItemId(state.NativeId, out uint id);
            foreach (var pair in _nativeParts)
            {
                if (pair.Key == id || pair.Value == null || !pair.Value.gameObject.activeInHierarchy) continue;
                // Preserve a different guest-save occupant until full mount isolation
                // exists; overlaying it would present two parts in one assembly slot.
                if (pair.Value.transform.parent == parent && NativePartIdentity.Phase(pair.Value) == NativePartPhase.Fitted)
                    return null;
            }
            return parent;
        }

        private static void PrepareReplacementPresentation(ReplacementBinding binding)
        {
            var data = binding.Data;
            binding.LooseScale = data.transform.localScale;
            binding.LooseTag = data.gameObject.tag;
            foreach (var collider in data.GetComponentsInChildren<Collider>(true))
                binding.Colliders.Add(new ReplacementCollider { Collider = collider, Enabled = collider.enabled, Trigger = collider.isTrigger });
            foreach (var body in data.GetComponentsInChildren<Rigidbody>(true))
                binding.Bodies.Add(new ReplacementBody { Body = body, Kinematic = body.isKinematic, Collisions = body.detectCollisions });
            var rotations = new List<FsmStateAction>();
            foreach (var action in FsmHook.FindState(data, SyncCatalog.ReplacementParts!["itemStatusState"])!.Actions)
            {
                if (action.GetType().Name != "SetRotation") continue;
                var target = data.Fsm.GetOwnerDefaultTarget(PackageField<FsmOwnerDefault>(action, "gameObject"));
                if (target == null || ScenePath.RelativeTo(target.transform, data.transform) == null)
                    throw new InvalidOperationException("Replacement presentation points outside its replica.");
                rotations.Add(action);
            }
            var pose = new FsmState(data.Fsm) { Name = ReplacementPoseState, Actions = rotations.ToArray() };
            var states = new List<FsmState>(data.Fsm.States); states.Add(pose); data.Fsm.States = states.ToArray();
            if (!FsmHook.EnsureRemoteEntry(data, pose.Name)) throw new InvalidOperationException("Cannot restore replacement presentation.");
        }

        private bool ApplyReplacementState(ReplacementBinding binding, uint id, ReplacementPartState state)
        {
            bool loose = binding.Factory.Rule.Identity.CanCreate(state);
            var obj = binding.Data.gameObject;
            var parent = loose ? null : ResolveReplacementParent(state);
            if (!loose && (parent == null || ScenePath.RelativeTo(parent, obj.transform) != null))
            {
                HideReplacement(binding, id);
                return false;
            }
            bool restoreLoosePose = binding.FittedPresentation || !obj.activeSelf || !_items.ContainsKey(id);
            bool applying = _bridge.ApplyingRemote;
            try
            {
                _bridge.ApplyingRemote = true;
                if (!loose) RemoveNativeItemMotion(id);
                obj.transform.SetParent(parent, false);
                binding.FittedPresentation = !loose;
                ApplyReplacementScalars(binding, state);
                binding.Data.FsmVariables.FindFsmInt(SyncCatalog.ReplacementParts!["assemblyVariable"]).Value = state.AssemblyId;
                obj.SetActive(true);
                FsmHook.FireRemoteEntry(binding.Data, ReplacementPoseState);
                foreach (var saved in binding.Bodies)
                    if (saved.Body != null)
                    {
                        if (!loose) { saved.Body.velocity = Vector3.zero; saved.Body.angularVelocity = Vector3.zero; }
                        if (!loose || restoreLoosePose) saved.Body.isKinematic = !loose || saved.Kinematic;
                        saved.Body.detectCollisions = loose && saved.Collisions;
                    }
                foreach (var saved in binding.Colliders)
                    if (saved.Collider != null)
                    { saved.Collider.enabled = loose && saved.Enabled; saved.Collider.isTrigger = saved.Trigger; }
                obj.tag = loose ? binding.LooseTag : "Untagged";
                obj.transform.localScale = loose ? binding.LooseScale : state.LocalScale.ToUnity();
                if (loose)
                {
                    if (restoreLoosePose)
                    {
                        _pendingItemPoses.Remove(id);
                        TryScanNativePart(binding.Body);
                        if (!_items.TryGetValue(id, out var item) || item.Body != binding.Body)
                            throw new InvalidOperationException("Replacement loose registration failed.");
                        ApplySnapshotPose(item, state.Position.ToUnity(), state.Rotation.ToUnity());
                    }
                }
                else
                {
                    obj.transform.localPosition = state.LocalPosition.ToUnity();
                    obj.transform.localRotation = state.LocalRotation.ToUnity();
                }
                SyncEventLog.Record("replacement-attachment", state.NativeId + (loose ? " loose" : " fitted " + state.ParentId.ToString("X8")));
                return true;
            }
            finally { _bridge.ApplyingRemote = applying; }
        }

        private void HideReplacement(ReplacementBinding binding, uint id)
        {
            RemoveNativeItemMotion(id);
            binding.Data.transform.SetParent(null, true);
            binding.Data.gameObject.SetActive(false);
            binding.FittedPresentation = true;
        }

        private void RefreshReplacementAttachmentReadiness()
        {
            foreach (var pair in _replacementParts)
            {
                var binding = pair.Value;
                if (!binding.Replica || binding.Factory.Failed) continue;
                try
                {
                    if (binding.Data == null) { _pendingReplacements.Add(pair.Key); continue; }
                    if (!binding.FittedPresentation) continue;
                    var state = _replacementReplica?.Get(pair.Key);
                    if (state == null) continue;
                    var parent = ResolveReplacementParent(state);
                    if (parent == null || binding.Data.transform.parent != parent || !binding.Data.gameObject.activeInHierarchy)
                        _pendingReplacements.Add(pair.Key);
                }
                catch (Exception e) { FailReplacementFactory(binding.Factory, e); }
            }
        }

        private void DetachReplacementChildren(uint parentId)
        {
            foreach (var pair in _replacementParts)
            {
                var binding = pair.Value; var state = _replacementReplica?.Get(pair.Key);
                if (!binding.Replica || binding.Data == null || state == null || state.ParentKind == PartParentKind.None
                    || state.ParentId != parentId) continue;
                HideReplacement(binding, pair.Key);
                _pendingReplacements.Add(pair.Key);
            }
        }
    }
}
