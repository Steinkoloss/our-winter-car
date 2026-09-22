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
        private sealed class PartBeltSource
        {
            public PlayMakerFSM Mount = null!, Jumping = null!;
            public Transform Visual = null!, Mesh = null!, Bone = null!;
            public SkinnedMeshRenderer Renderer = null!;
            public AudioSource Audio = null!;
            public FsmFloat Scale = null!;
            public PlayMakerFSM? Scroll;
            public FsmFloat? ScrollRpm, ScrollMultiplier;
        }

        private sealed class PartBeltIsolation
        {
            private readonly PartBeltSource _source;
            private readonly FsmSuppressor _paused = new FsmSuppressor();
            private readonly bool _rendererEnabled, _muted;
            public PartBeltIsolation(PartBeltSource source)
            {
                _source = source; _rendererEnabled = source.Renderer.enabled; _muted = source.Audio.mute;
                RequireFit(_paused.Suppress(source.Jumping));
                KeepHidden();
            }
            public void KeepHidden()
            {
                if (_source.Jumping != null) _source.Jumping.enabled = false;
                if (_source.Renderer != null) _source.Renderer.enabled = false;
                if (_source.Audio != null) _source.Audio.mute = true;
            }
            public void Restore()
            {
                if (_source.Renderer != null) _source.Renderer.enabled = _rendererEnabled;
                if (_source.Audio != null) _source.Audio.mute = _muted;
                _paused.Restore();
            }
        }

        private readonly Dictionary<PlayMakerFSM, PartBeltSource> _partBeltSources = new Dictionary<PlayMakerFSM, PartBeltSource>();
        private readonly Dictionary<PlayMakerFSM, PartBeltIsolation> _partBeltIsolation = new Dictionary<PlayMakerFSM, PartBeltIsolation>();
        private readonly HashSet<PlayMakerFSM> _partBeltFailures = new HashSet<PlayMakerFSM>();

        private PartBeltSource? GetPartBeltSource(PlayMakerFSM mount, PartBeltVisualData rule, bool isolate)
        {
            if (_partBeltFailures.Contains(mount)) return null;
            try
            {
                if (!_partBeltSources.TryGetValue(mount, out var source))
                {
                    source = BindPartBeltSource(mount, rule);
                    _partBeltSources.Add(mount, source);
                }
                if (isolate && !_partBeltIsolation.ContainsKey(mount))
                    _partBeltIsolation.Add(mount, new PartBeltIsolation(source));
                // Guest copies use the host's scroll rate. Looking for a dormant
                // local animation here otherwise scans the entire scene every frame.
                if (!isolate && source.Scroll == null) BindPartBeltScroll(source, rule);
                return source;
            }
            catch (Exception e)
            {
                _partBeltFailures.Add(mount);
                WinterMPPlugin.Log.LogWarning("WorldSync: fan belt display unavailable: " + e.Message);
                SyncEventLog.Record("part-belt-disabled", ScenePath.Of(mount.transform) + " " + e.Message);
                return null;
            }
        }

        private static PartBeltSource BindPartBeltSource(PlayMakerFSM mount, PartBeltVisualData rule)
        {
            var visual = ScenePath.FindRelative(mount.transform, rule.VisualPath);
            RequireFit(visual != null && mount.FsmVariables.FindFsmGameObject(rule.MountVisualVariable)?.Value == visual!.gameObject);
            var mesh = ScenePath.FindRelative(visual!, rule.MeshPath);
            var bone = ScenePath.FindRelative(visual!, rule.ScaleBonePath);
            var rendererNode = ScenePath.FindRelative(visual!, rule.RendererPath);
            var animation = ScenePath.FindRelative(visual!, rule.AnimationPath);
            RequireFit(mesh != null && bone != null && rendererNode != null && animation != null);
            var jumping = FindPartAdjustmentFsm(animation!, rule.Fsm);
            if (!jumping.Fsm.Initialized) jumping.Fsm.Init(jumping);
            var renderer = rendererNode!.GetComponent<SkinnedMeshRenderer>();
            var audio = animation!.GetComponent<AudioSource>();
            var scale = jumping.FsmVariables.FindFsmFloat(rule.ScaleVariable);
            RequireFit(renderer != null && renderer.sharedMesh != null && renderer.sharedMaterials.Length == 1
                && renderer.sharedMaterials[0] != null && audio != null && audio.clip != null && audio.loop && scale != null
                && jumping.FsmVariables.FindFsmGameObject("ScaleBone")?.Value == bone!.gameObject
                && jumping.FsmVariables.FindFsmGameObject("db_AlternatorBelt")?.Value == mount.gameObject);
            ValidatePartBeltMesh(mesh!, renderer!, bone!);
            foreach (string state in new[] { "Animate", "Animate 2", "Reset" })
            {
                var actions = FsmHook.FindState(jumping, state)?.Actions;
                RequireFit(actions != null);
                FsmStateAction? setScale = null;
                foreach (var action in actions!)
                    if (action.GetType().Name == "SetScale") { RequireFit(setScale == null); setScale = action; }
                RequireFit(setScale != null && setScale.Enabled
                    && FitTargetVariable(PackageField<FsmOwnerDefault>(setScale, "gameObject"), "ScaleBone")
                    && PackageField<FsmVector3>(setScale, "vector")?.IsNone == true);
                RequireFitOneShot(setScale!); RotationConstant(setScale!, "y", 1);
                foreach (string axis in new[] { "x", "z" })
                    if (state == "Reset") RotationVariable(setScale!, axis, rule.ScaleVariable);
                    else RotationConstant(setScale!, axis, 1);
            }
            return new PartBeltSource { Mount = mount, Visual = visual!, Mesh = mesh!, Bone = bone!,
                Renderer = renderer!, Audio = audio!, Jumping = jumping, Scale = scale! };
        }

        private static void ValidatePartBeltMesh(Transform mesh, SkinnedMeshRenderer renderer, Transform bone)
        {
            RequireFit(ScenePath.RelativeTo(renderer.transform, mesh) != null && ScenePath.RelativeTo(bone, mesh) != null
                && renderer.bones.Length == 2 && renderer.rootBone != null
                && ScenePath.RelativeTo(renderer.rootBone, mesh) != null);
            foreach (var value in renderer.bones) RequireFit(value != null && ScenePath.RelativeTo(value, mesh) != null);
            RequireFit(Array.IndexOf(renderer.bones, bone) >= 0);
            int renderers = 0;
            foreach (var component in mesh.GetComponentsInChildren<Component>(true))
            {
                // Instantiate only a verified render hierarchy. No native script,
                // physics callback or audio component can run on the clone.
                RequireFit(component is Transform || component == renderer);
                if (component == renderer) renderers++;
            }
            RequireFit(renderers == 1);
        }

        private static PartBeltVisualState ReadPartBeltVisual(PartBeltSource source)
        {
            bool visible = source.Visual.gameObject.activeInHierarchy && source.Renderer.gameObject.activeInHierarchy && source.Renderer.enabled;
            bool running = visible && source.Jumping.enabled && source.Jumping.Fsm.Started && source.Audio.enabled;
            var state = new PartBeltVisualState { Visible = visible, Running = running,
                Scale = running ? source.Scale.Value : source.Bone.localScale.x,
                Pitch = source.Audio.pitch, Volume = source.Audio.volume,
                ScrollSpeed = source.Scroll != null && source.Scroll.enabled && source.Scroll.gameObject.activeInHierarchy
                    && source.Scroll.ActiveStateName == "State 1" ? source.ScrollRpm!.Value * source.ScrollMultiplier!.Value : 0 };
            RequireFit(PartBeltVisualPolicy.Valid(state));
            return state;
        }
    }
}
