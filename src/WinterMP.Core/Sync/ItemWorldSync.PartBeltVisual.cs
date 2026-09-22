using System;
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
        private sealed class PartBeltView
        {
            public GameObject Root = null!;
            public Transform Bone = null!;
            public AudioSource Audio = null!;
            public Material Material = null!;
            public PartBeltVisualState? State;
            public readonly System.Random Random = new System.Random();
            public float Remaining, ScrollOffset;
            public bool Pulse, WasRunning;
        }

        private static PartBeltView CreatePartBeltView(PartBeltSource source, Transform parent)
        {
            ValidatePartBeltMesh(source.Mesh, source.Renderer, source.Bone);
            var view = new PartBeltView { Root = new GameObject("WinterMP fitted fan belt") };
            view.Root.SetActive(false);
            try
            {
                var root = view.Root.transform; root.SetParent(parent, false);
                root.localPosition = source.Visual.localPosition; root.localRotation = source.Visual.localRotation;
                root.localScale = source.Visual.localScale;
                var clone = (GameObject)UnityEngine.Object.Instantiate(source.Mesh.gameObject);
                clone.transform.SetParent(root, false);
                clone.transform.localPosition = source.Mesh.localPosition; clone.transform.localRotation = source.Mesh.localRotation;
                clone.transform.localScale = source.Mesh.localScale; clone.SetActive(true);
                var renderers = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                RequireFit(renderers.Length == 1);
                var renderer = renderers[0];
                string bonePath = ScenePath.RelativeTo(source.Bone, source.Mesh)!;
                var bone = ScenePath.FindRelative(clone.transform, bonePath);
                RequireFit(renderer != null && bone != null);
                ValidatePartBeltMesh(clone.transform, renderer!, bone!);
                view.Bone = bone!;
                view.Material = new Material(source.Renderer.sharedMaterial);
                renderer!.sharedMaterial = view.Material; renderer.enabled = true;
                // The engine scrolls its native belt material. This copy owns its
                // material and bones, so those writes cannot animate a guest save.
                var audioObject = new GameObject("Belt sound"); audioObject.transform.SetParent(root, false);
                audioObject.transform.localPosition = source.Audio.transform.localPosition;
                view.Audio = audioObject.AddComponent<AudioSource>();
                view.Audio.playOnAwake = false; view.Audio.loop = source.Audio.loop; view.Audio.clip = source.Audio.clip;
                view.Audio.spatialBlend = source.Audio.spatialBlend; view.Audio.rolloffMode = source.Audio.rolloffMode;
                view.Audio.minDistance = source.Audio.minDistance; view.Audio.maxDistance = source.Audio.maxDistance;
                view.Audio.dopplerLevel = source.Audio.dopplerLevel; view.Audio.spread = source.Audio.spread;
                view.Audio.volume = 0;
                return view;
            }
            catch { ReleasePartBeltView(view); throw; }
        }

        private static void ApplyPartBeltView(PartBeltView view, PartBeltVisualState state, float delta, float realDelta)
        {
            RequireFit(PartBeltVisualPolicy.Valid(state) && !float.IsNaN(delta) && !float.IsInfinity(delta) && delta >= 0
                && !float.IsNaN(realDelta) && !float.IsInfinity(realDelta) && realDelta >= 0);
            view.Root.SetActive(state.Visible);
            if (!state.Visible)
            { view.Audio.Stop(); view.WasRunning = false; view.Pulse = false; return; }
            if (!state.Running)
            { view.Audio.Stop(); view.WasRunning = false; view.Pulse = false; view.Bone.localScale = new Vector3(state.Scale, 1, state.Scale); }
            else
            {
                if (!view.WasRunning)
                { view.WasRunning = true; view.Pulse = false; view.Remaining = .02f + (float)view.Random.NextDouble() * .48f; }
                view.Remaining -= view.Pulse ? realDelta : delta;
                if (view.Remaining <= 0)
                {
                    view.Pulse = !view.Pulse;
                    view.Remaining = view.Pulse ? .05f : .02f + (float)view.Random.NextDouble() * .48f;
                }
                float scale = view.Pulse ? state.Scale : 1;
                view.Bone.localScale = new Vector3(scale, 1, scale);
                view.Audio.pitch = state.Pitch; view.Audio.volume = state.Volume;
                if (!view.Audio.isPlaying) view.Audio.Play();
            }
            // Only phase is local: speed, flutter amplitude and sound are host
            // observations. No wear calculation or native event runs on this view.
            view.ScrollOffset = Mathf.Repeat(view.ScrollOffset + state.ScrollSpeed * delta, 1);
            view.Material.mainTextureOffset = new Vector2(view.ScrollOffset, 0);
        }

        private static void ReleasePartBeltView(PartBeltView view)
        {
            if (view.Audio != null) view.Audio.Stop();
            if (view.Root != null) { view.Root.SetActive(false); UnityEngine.Object.Destroy(view.Root); }
            if (view.Material != null) UnityEngine.Object.Destroy(view.Material);
        }

        private static void DestroyPartBeltView(ReplacementBinding binding)
        {
            if (binding.BeltView == null) return;
            ReleasePartBeltView(binding.BeltView); binding.BeltView = null;
        }

        private void CapturePartBeltVisual(ReplacementBinding binding, ReplacementPartState state)
        {
            var rule = binding.Factory.Rule.BeltVisual;
            if (rule == null || !PartAttachmentPolicy.HasAttachment(state)) return;
            var mount = FindRemovalMount(binding);
            if (mount == null || mount.transform != binding.Data.transform.parent) return;
            var source = GetPartBeltSource(mount, rule, false);
            if (source == null) return;
            try { state.BeltVisual = ReadPartBeltVisual(source); }
            catch (Exception e)
            {
                _partBeltFailures.Add(mount);
                SyncEventLog.Record("part-belt-disabled", state.NativeId + " " + e.Message);
            }
        }

        private void ApplyPartBeltVisual(ReplacementBinding binding, ReplacementPartState state, Transform? parent)
        {
            var rule = binding.Factory.Rule.BeltVisual;
            if (rule == null || !binding.Replica) return;
            try
            {
                var mesh = binding.Data.FsmVariables.FindFsmGameObject(rule.LooseMeshVariable)?.Value;
                RequireFit(mesh != null && mesh != binding.Data.gameObject && ScenePath.RelativeTo(mesh!.transform, binding.Data.transform) != null);
                mesh!.SetActive(!binding.FittedPresentation);
                if (!binding.FittedPresentation || parent == null || state.BeltVisual == null || binding.BeltVisualFailed)
                { DestroyPartBeltView(binding); return; }
                if (binding.BeltView != null && binding.BeltView.Root.transform.parent != parent) DestroyPartBeltView(binding);
                if (binding.BeltView == null)
                {
                    var mount = FindPartAdjustmentFsm(parent, SyncCatalog.ReplacementParts!["itemFsm"]);
                    var source = GetPartBeltSource(mount, rule, true);
                    if (source == null) return;
                    binding.BeltView = CreatePartBeltView(source, parent);
                }
                binding.BeltView.State = PartBeltVisualPolicy.Copy(state.BeltVisual);
                ApplyPartBeltView(binding.BeltView, state.BeltVisual, 0, 0);
            }
            catch (Exception e)
            {
                binding.BeltVisualFailed = true; DestroyPartBeltView(binding);
                WinterMPPlugin.Log.LogWarning("WorldSync: fan belt copy display disabled: " + e.Message);
                SyncEventLog.Record("part-belt-copy-disabled", state.NativeId + " " + e.Message);
            }
        }

        private void IsolatePartBeltSources(SessionManager session)
        {
            if (session.IsHost || !GuestSaveGuard.ProtectWorld) return;
            foreach (var factory in _replacementFactories.Values)
            {
                var rule = factory.Rule.BeltVisual;
                if (rule == null || factory.Failed || !factory.Suppressor.Active) continue;
                foreach (var reference in factory.Rule.References)
                {
                    if (reference.Target != SyncCatalog.ReplacementParts!["installPointVariable"]) continue;
                    var point = factory.Fsm.FsmVariables.FindFsmGameObject(reference.Source)?.Value;
                    if (point == null) continue;
                    foreach (var mount in point.GetComponents<PlayMakerFSM>())
                        if (mount.FsmName == SyncCatalog.ReplacementParts!["itemFsm"] && mount.Fsm.Initialized)
                            GetPartBeltSource(mount, rule, true);
                }
            }
        }

        private void UpdatePartBeltViews(SessionManager session)
        {
            if (session.IsHost) return;
            foreach (var saved in _partBeltIsolation.Values) saved.KeepHidden();
            foreach (var pair in _replacementParts)
            {
                var binding = pair.Value;
                try
                {
                    if (binding.BeltView == null && binding.Factory.Rule.BeltVisual != null && !binding.BeltVisualFailed)
                    {
                        var pendingVisual = _replacementReplica?.Get(pair.Key);
                        if (pendingVisual?.BeltVisual != null) TryApplyReplacementBeltPresentation(pair.Key, pendingVisual);
                    }
                    var view = binding.BeltView;
                    if (view == null) continue;
                    // An unrelated factory can defer materialization. Accepted
                    // removal/visibility receipts must still stop this display now.
                    var latest = _replacementReplica?.Get(pair.Key);
                    var parent = latest == null ? null : ResolveReplacementParent(latest);
                    if (!RefreshPartBeltView(binding, latest, parent))
                    {
                        if (latest?.BeltVisual != null && !binding.BeltVisualFailed
                            && !TryApplyReplacementBeltPresentation(pair.Key, latest)) _pendingReplacements.Add(pair.Key);
                        continue;
                    }
                    ApplyPartBeltView(view, view.State!, Time.deltaTime, Time.unscaledDeltaTime);
                }
                catch (Exception e)
                { binding.BeltVisualFailed = true; DestroyPartBeltView(binding); SyncEventLog.Record("part-belt-copy-disabled", binding.NativeId + " " + e.Message); }
            }
        }

        private bool TryApplyReplacementBeltPresentation(uint id, ReplacementPartState state)
        {
            // The ledger rejects gameplay changes at an unchanged revision. An
            // appearance receipt must not make an applied belt disappear from
            // engine inputs while waiting for the slower materialization poll.
            if (_pendingReplacements.Contains(id) || !_replacementParts.TryGetValue(id, out var binding)
                || !binding.Replica || binding.Factory.Rule.BeltVisual == null || binding.Factory.Failed
                || !binding.Factory.Suppressor.Active || !binding.HasAppliedState || binding.AppliedRevision != state.Revision
                || !binding.FittedPresentation || !PartAttachmentPolicy.HasAttachment(state)
                || binding.Data == null || binding.Body == null || !binding.Data.gameObject.activeInHierarchy
                || binding.NativeId != state.NativeId || binding.Factory.Rule.Identity.FactoryId != state.FactoryId
                || binding.Data.FsmVariables.FindFsmString(SyncCatalog.ReplacementParts!["itemIdVariable"])?.Value != state.NativeId
                || !_bridge.PartIdentities.IsReplica(binding.Data)
                || !_bridge.PartIdentities.TryRootId(binding.Data, out uint currentId) || currentId != id) return false;
            var parent = ResolveReplacementParent(state);
            if (parent == null || binding.Data.transform.parent != parent) return false;
            ApplyPartBeltVisual(binding, state, parent);
            return true;
        }

        private static bool RefreshPartBeltView(ReplacementBinding binding, ReplacementPartState? latest, Transform? parent)
        {
            var view = binding.BeltView;
            if (view == null) return false;
            if (latest == null || latest.BeltVisual == null || !PartAttachmentPolicy.HasAttachment(latest)
                || binding.Data == null || binding.Factory.Failed || !binding.FittedPresentation
                || !binding.Data.gameObject.activeInHierarchy || view.Root == null || parent == null
                || view.Root.transform.parent != parent || binding.Data.transform.parent != parent)
            { DestroyPartBeltView(binding); return false; }
            view.State = PartBeltVisualPolicy.Copy(latest.BeltVisual);
            return true;
        }

        private void RestorePartBeltSources()
        {
            foreach (var saved in _partBeltIsolation.Values) saved.Restore();
            _partBeltIsolation.Clear(); _partBeltSources.Clear(); _partBeltFailures.Clear();
        }
    }
}
