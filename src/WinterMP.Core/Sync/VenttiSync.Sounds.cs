using System;
using System.Collections.Generic;
using System.Reflection;
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
    internal sealed partial class VenttiSync
    {
        private readonly VenttiSoundQueue _sounds = new VenttiSoundQueue();
        private readonly List<SoundHook> _soundHooks = new List<SoundHook>();
        private readonly List<PlayingSound> _playingSounds = new List<PlayingSound>();
        private AudioSource[]? _soundTemplates;
        private bool _soundsFailed, _soundWaitingLogged;
        private uint _soundSequence;
        private float _soundProbeAt;

        private sealed class SoundHook
        {
            public FsmState State = null!;
            public FsmHookAction Action = null!;
            public FsmStateAction Native = null!;
            public VenttiSoundSourceData Binding = null!;
            public Transform Origin = null!;
            public FsmString? Variable;
        }
        private sealed class PlayingSound
        {
            public GameObject Object = null!;
            public float Until;
        }

        private void InstallSoundHooks()
        {
            if (_reactionConfig == null || _reactionRoot == null || _soundsFailed) return;
            try
            {
                var hooks = new List<SoundHook>();
                foreach (var source in _reactionConfig.Sources)
                {
                    var fsm = FindChildFsm(_reactionRoot, source.Path, source.Fsm);
                    var origin = _reactionRoot.Find(source.Origin);
                    if (fsm == null || origin == null) throw new InvalidOperationException("Missing Ventti sound source: " + source.Path);
                    if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
                    var state = FsmHook.FindState(fsm, source.State);
                    if (state == null || state.Actions.Length <= source.ActionIndex)
                        throw new InvalidOperationException("Missing Ventti sound state: " + source.State);
                    var native = state.Actions[source.ActionIndex];
                    if (native.GetType().Name != "MasterAudioPlaySound"
                        || SoundField<FsmString>(native, "soundGroupName").Value != source.Group
                        || SoundField<FsmFloat>(native, "delaySound").Value != source.Delay
                        || SoundField<FsmFloat>(native, "volume").Value != 1
                        || !SoundField<FsmBool>(native, "useThisLocation").Value
                        || SoundField<FsmBool>(native, "attachToGameObject").Value
                        || SoundField<FsmBool>(native, "useFixedPitch").Value
                        || fsm.Fsm.GetOwnerDefaultTarget(SoundField<FsmOwnerDefault>(native, "gameObject")) != origin.gameObject)
                        throw new InvalidOperationException("Ventti sound action changed: " + source.State);
                    var variation = SoundField<FsmString>(native, "variationName");
                    FsmString? variable = null;
                    if (source.Variable.Length != 0)
                    {
                        variable = fsm.FsmVariables.FindFsmString(source.Variable);
                        if (variable == null || !variation.UseVariable || variation.Name != source.Variable)
                            throw new InvalidOperationException("Ventti sound variable changed: " + source.State);
                    }
                    else if (variation.UseVariable || variation.Value != source.Variation)
                        throw new InvalidOperationException("Ventti sound variation changed: " + source.State);
                    var hook = new SoundHook { State = state, Native = native, Binding = source, Origin = origin, Variable = variable };
                    hook.Action = new FsmHookAction(() => CaptureSound(hook));
                    hooks.Add(hook);
                }
                // Install only after all sources validate. A hook immediately before
                // MasterAudio sees the host's ArrayList random choice without rerolling it.
                foreach (var hook in hooks)
                {
                    var actions = new List<FsmStateAction>(hook.State.Actions);
                    actions.Insert(actions.IndexOf(hook.Native), hook.Action);
                    hook.State.Actions = actions.ToArray();
                    _soundHooks.Add(hook);
                }
            }
            catch (Exception e) { DisableSounds(e); }
        }

        private static T SoundField<T>(FsmStateAction action, string name) where T : class
        {
            var field = action.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            var value = field?.GetValue(action) as T;
            if (value == null) throw new InvalidOperationException("Missing native Ventti sound field: " + name);
            return value;
        }

        private void CaptureSound(SoundHook source)
        {
            var session = SessionManager.Instance;
            if (_soundsFailed || !source.Native.Enabled || !_reactionsReady || _reactionConfig == null || session == null || !session.IsHost) return;
            try
            {
                string variation = source.Variable == null ? source.Binding.Variation : source.Variable.Value;
                int sound = Array.IndexOf(_reactionConfig.Sounds, "MasterAudio/" + source.Binding.Group + "/" + variation);
                if (sound < 0) throw new InvalidOperationException("Uncataloged native Ventti sound: " + variation);
                if (source.Origin == null) return;
                var p = source.Origin.position;
                session.SendWorldMessage(new VenttiSoundCue
                {
                    TableId = _tableId, LayoutId = _reactionConfig.LayoutId, Sequence = ++_soundSequence,
                    Sound = (byte)sound, Position = new NetVector3(p.x, p.y, p.z), Delay = source.Binding.Delay,
                }, Channel.ReliableOrdered);
                SyncEventLog.Record("ventti-sound", variation);
            }
            catch (Exception e) { DisableSounds(e); }
        }

        public void OnSoundCue(VenttiSoundCue cue)
        {
            var session = SessionManager.Instance;
            var config = SyncCatalog.VenttiTable?.Reactions;
            if (_soundsFailed || session == null || session.IsHost || config == null) return;
            EnsureBuilt();
            _sounds.Receive(_tableId, config.LayoutId, config.Sounds.Length, cue, Time.unscaledTime);
        }

        private bool BindSoundTemplates()
        {
            if (_reactionConfig == null) return false;
            var templates = new AudioSource[_reactionConfig.Sounds.Length];
            // Native variation names differ from clip names (pig1 owns clip pig01).
            // Bind the actual variation AudioSource, including inactive native objects.
            foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(AudioSource)))
            {
                var source = obj as AudioSource;
                if (source == null || source.clip == null) continue;
                int index = Array.IndexOf(_reactionConfig.Sounds, ScenePath.Of(source.transform));
                if (index < 0) continue;
                if (templates[index] != null && templates[index].clip != source.clip)
                    throw new InvalidOperationException("Ambiguous Ventti sound template.");
                templates[index] = source;
            }
            for (int i = 0; i < templates.Length; i++)
                if (templates[i] == null)
                {
                    if (!_soundWaitingLogged)
                    {
                        _soundWaitingLogged = true;
                        WinterMPPlugin.Log.LogWarning("VenttiSync: waiting for native sound template " + _reactionConfig.Sounds[i]);
                        SyncEventLog.Record("ventti-sound-wait", _reactionConfig.Sounds[i]);
                    }
                    return false;
                }
            _soundTemplates = templates;
            if (_soundWaitingLogged) WinterMPPlugin.Log.LogInfo("VenttiSync: native sound templates are ready.");
            _soundWaitingLogged = false;
            return true;
        }

        private void UpdateSounds()
        {
            if (_soundsFailed || _reactionHost) return;
            try
            {
                float now = Time.unscaledTime;
                for (int i = _playingSounds.Count - 1; i >= 0; i--)
                    if (_playingSounds[i].Object == null || now >= _playingSounds[i].Until)
                    {
                        if (_playingSounds[i].Object != null) UnityEngine.Object.Destroy(_playingSounds[i].Object);
                        _playingSounds.RemoveAt(i);
                    }
                _sounds.Expire(now);
                if (_soundTemplates == null && now >= _soundProbeAt)
                {
                    _soundProbeAt = now + 2f;
                    BindSoundTemplates();
                }
                if (_soundTemplates == null) return;
                while (_sounds.TryTake(now, out var cue, out float elapsed))
                {
                    if (cue == null) continue;
                    var template = _soundTemplates[cue.Sound];
                    if (template == null || template.clip == null) { _soundTemplates = null; break; }
                    var camera = Camera.main;
                    var position = Vector(cue.Position);
                    float pitch = template.pitch;
                    if (camera == null || (camera.transform.position - position).sqrMagnitude > template.maxDistance * template.maxDistance
                        || float.IsNaN(pitch) || float.IsInfinity(pitch) || pitch <= 0) continue;
                    float clipTime = elapsed * pitch;
                    if (clipTime >= template.clip.length) continue;
                    if (_playingSounds.Count == VenttiSoundQueue.Capacity)
                    {
                        UnityEngine.Object.Destroy(_playingSounds[0].Object); _playingSounds.RemoveAt(0);
                    }
                    var obj = new GameObject("WinterMP_VenttiSound");
                    _playingSounds.Add(new PlayingSound { Object = obj, Until = now + (template.clip.length - clipTime) / pitch + 1f });
                    obj.transform.position = position;
                    var audio = obj.AddComponent<AudioSource>();
                    audio.playOnAwake = false; audio.loop = false; audio.clip = template.clip;
                    audio.volume = template.volume; audio.pitch = pitch; audio.spatialBlend = 1f;
                    audio.minDistance = template.minDistance; audio.maxDistance = template.maxDistance;
                    audio.rolloffMode = template.rolloffMode; audio.spread = template.spread; audio.dopplerLevel = 0f;
                    audio.time = clipTime;
                    audio.Play();
                }
            }
            catch (Exception e) { DisableSounds(e); }
        }

        private void DisableSounds(Exception e)
        {
            if (_soundsFailed) return;
            _soundsFailed = true;
            _sounds.Clear();
            foreach (var sound in _playingSounds) if (sound.Object != null) UnityEngine.Object.Destroy(sound.Object);
            _playingSounds.Clear();
            WinterMPPlugin.Log.LogWarning("VenttiSync: reaction audio disabled: " + e.Message);
            SyncEventLog.Record("ventti-sound-disabled", e.Message);
        }

        private void ClearSounds()
        {
            foreach (var hook in _soundHooks)
            {
                try
                {
                    var actions = new List<FsmStateAction>(hook.State.Actions);
                    actions.Remove(hook.Action); hook.State.Actions = actions.ToArray();
                }
                catch (Exception e) { WinterMPPlugin.Log.LogDebug("Ventti sound hook restore: " + e.Message); }
            }
            foreach (var sound in _playingSounds) if (sound.Object != null) UnityEngine.Object.Destroy(sound.Object);
            _soundHooks.Clear(); _playingSounds.Clear(); _sounds.Clear(); _soundTemplates = null;
            _soundsFailed = _soundWaitingLogged = false; _soundSequence = 0; _soundProbeAt = 0;
        }
    }
}
