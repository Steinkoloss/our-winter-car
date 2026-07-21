using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Host-owned scalar state for the fixed home radio/CD player. Knob FSM state
    /// is presentation-heavy and cannot be replayed safely; the values that feed
    /// audio/electricity are stable variables and are what actually need syncing.
    /// </summary>
    internal sealed class HomeStereoSync
    {
        private const string ButtonsPath = "HOMENEW/Functions/FunctionsDisable/Stereos/Player/ButtonsCD";
        private const float ScanIntervalSeconds = 5f;
        private const float SendIntervalSeconds = 0.2f;
        private const float PlayerPoseMaxAgeSeconds = 2f;
        private const float PlayerStereoMaxDistance = 6f;
        private const float Epsilon = 0.001f;

        private readonly Dictionary<byte, ushort> _lastIntentSequence = new Dictionary<byte, ushort>();
        private PlayMakerFSM? _volumeFsm;
        private PlayMakerFSM? _bassFsm;
        private PlayMakerFSM? _radioFsm;
        private PlayMakerFSM? _channelFsm;
        private FsmFloat? _volume;
        private FsmFloat? _bass;
        private FsmBool? _radioOn;
        private FsmBool? _channel;
        private Transform? _anchor;
        private HomeStereoState? _pending;
        private float _nextScanAt;
        private float _nextSendAt;
        private ushort _outSequence;
        private ushort _outIntentSequence;
        private ushort _lastRemoteSequence;
        private bool _guestBaselineReady;
        private bool _hasLast;
        private byte _lastFlags;
        private float _lastVolume;
        private float _lastBass;

        public void Clear()
        {
            _lastIntentSequence.Clear();
            _volumeFsm = null;
            _bassFsm = null;
            _radioFsm = null;
            _channelFsm = null;
            _volume = null;
            _bass = null;
            _radioOn = null;
            _channel = null;
            _anchor = null;
            _pending = null;
            _nextScanAt = 0f;
            _nextSendAt = 0f;
            _outSequence = 0;
            _outIntentSequence = 0;
            _lastRemoteSequence = 0;
            _guestBaselineReady = false;
            _hasLast = false;
            _lastFlags = 0;
            _lastVolume = 0f;
            _lastBass = 0f;
        }

        public void Update(SessionManager session)
        {
            Scan();
            ApplyPending();
            if (!IsReady()) return;

            if (session.IsHost)
            {
                if (session.PlayerCount > 0 && Time.unscaledTime >= _nextSendAt)
                {
                    _nextSendAt = Time.unscaledTime + SendIntervalSeconds;
                    var state = BuildState(changedOnly: true);
                    if (state != null)
                        session.SendWorldMessage(state, Channel.ReliableOrdered);
                }
                return;
            }

            if (!_guestBaselineReady || Time.unscaledTime < _nextSendAt) return;
            var local = ReadState(0);
            if (!HasChanged(local)) return;
            _nextSendAt = Time.unscaledTime + SendIntervalSeconds;
            // Deliberately NOT Remember(local) here: the baseline advances only when the
            // host's accepting HomeStereoState echoes back (Apply -> Remember). If the host
            // rejects the intent (e.g. stale pose), the change stays "pending" and this
            // path resends each interval instead of silently losing the guest's setting.
            session.SendWorldMessage(new HomeStereoIntent
            {
                PlayerId = session.LocalPlayerId,
                Flags = local.Flags,
                Sequence = ++_outIntentSequence,
                Volume = local.Volume,
                Bass = local.Bass,
            }, Channel.ReliableOrdered);
        }

        public HomeStereoState? BuildSnapshot()
        {
            Scan(force: true);
            // Join snapshot goes only to the joiner — must NOT advance the periodic
            // broadcaster's change baseline (that would strand connected guests on a
            // not-yet-broadcast host change).
            return IsReady() ? BuildState(changedOnly: false, advanceBaseline: false) : null;
        }

        public bool TryAcceptIntent(HomeStereoIntent message, out HomeStereoState state)
        {
            state = new HomeStereoState();
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || !IsValidIntent(message)) return false;
            Scan();
            if (!IsReady() || !TryGetFreshPlayerPose(session, message.PlayerId, out Vector3 playerPosition)
                || _anchor == null
                || (playerPosition - _anchor.position).sqrMagnitude > PlayerStereoMaxDistance * PlayerStereoMaxDistance)
                return false;

            if (_lastIntentSequence.TryGetValue(message.PlayerId, out ushort last))
            {
                ushort diff = (ushort)(message.Sequence - last);
                if (diff == 0 || diff > short.MaxValue) return false;
            }
            _lastIntentSequence[message.PlayerId] = message.Sequence;

            ApplyValues(message.Flags, message.Volume, message.Bass);
            state = BuildState(changedOnly: false) ?? new HomeStereoState();
            WinterMPPlugin.Log.LogDebug(
                "HomeStereoSync: accepted settings from player " + message.PlayerId + ".");
            return true;
        }

        public void Apply(HomeStereoState message)
        {
            Scan();
            if (!IsReady())
            {
                _pending = message;
                return;
            }

            ushort diff = (ushort)(message.Sequence - _lastRemoteSequence);
            if (_lastRemoteSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            _lastRemoteSequence = message.Sequence;
            if (!IsValidState(message)) return;
            ApplyValues(message.Flags, message.Volume, message.Bass);
            Remember(message);
            _guestBaselineReady = true;
        }

        private void Scan(bool force = false)
        {
            if (!force && Time.unscaledTime < _nextScanAt) return;
            _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;
            try
            {
                var fsms = Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM));
                foreach (var obj in fsms)
                {
                    var fsm = obj as PlayMakerFSM;
                    if (fsm == null) continue;
                    string path = ScenePath.Of(fsm.transform);
                    if (path == ButtonsPath + "/Volume" && fsm.FsmName == "Knob" && _volume == null)
                    {
                        _volumeFsm = fsm;
                        _volume = fsm.FsmVariables.FindFsmFloat("Volume");
                    }
                    else if (path == ButtonsPath + "/Bass" && fsm.FsmName == "Knob" && _bass == null)
                    {
                        _bassFsm = fsm;
                        _bass = fsm.FsmVariables.FindFsmFloat("Bass");
                    }
                    else if (path == ButtonsPath + "/RadioCDSwitch" && fsm.FsmName == "Use" && _radioOn == null)
                    {
                        _radioFsm = fsm;
                        _radioOn = fsm.FsmVariables.FindFsmBool("RadioOn");
                        _anchor = fsm.transform;
                    }
                    else if (path == ButtonsPath + "/TrackChannelSwitch" && fsm.FsmName == "ChangeChannel" && _channel == null)
                    {
                        _channelFsm = fsm;
                        _channel = fsm.FsmVariables.FindFsmBool("Channel");
                    }
                }
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug("HomeStereoSync: scan failed: " + e.Message);
            }
        }

        private void ApplyPending()
        {
            if (_pending == null || !IsReady()) return;
            var pending = _pending;
            _pending = null;
            Apply(pending);
        }

        private bool IsReady()
        {
            return _volumeFsm != null && _bassFsm != null && _radioFsm != null && _channelFsm != null
                && _volume != null && _bass != null && _radioOn != null && _channel != null;
        }

        private HomeStereoState? BuildState(bool changedOnly, bool advanceBaseline = true)
        {
            var state = ReadState(++_outSequence);
            if (changedOnly && !HasChanged(state))
            {
                _outSequence--;
                return null;
            }
            if (advanceBaseline)
                Remember(state);
            return state;
        }

        private HomeStereoState ReadState(ushort sequence)
        {
            byte flags = 0;
            if (_radioOn != null && _radioOn.Value) flags |= HomeStereoState.FlagRadioOn;
            if (_channel != null && _channel.Value) flags |= HomeStereoState.FlagChannel;
            return new HomeStereoState
            {
                Flags = flags,
                Sequence = sequence,
                Volume = _volume != null ? _volume.Value : 0f,
                Bass = _bass != null ? _bass.Value : 0f,
            };
        }

        private bool HasChanged(HomeStereoState state)
        {
            return !_hasLast || state.Flags != _lastFlags
                || Mathf.Abs(state.Volume - _lastVolume) > Epsilon
                || Mathf.Abs(state.Bass - _lastBass) > Epsilon;
        }

        private void Remember(HomeStereoState state)
        {
            _hasLast = true;
            _lastFlags = state.Flags;
            _lastVolume = state.Volume;
            _lastBass = state.Bass;
        }

        private void ApplyValues(byte flags, float volume, float bass)
        {
            if (_volume != null && Mathf.Abs(_volume.Value - volume) > Epsilon)
            {
                _volume.Value = volume;
                EnterState(_volumeFsm, "Set volume");
            }
            if (_bass != null && Mathf.Abs(_bass.Value - bass) > Epsilon)
            {
                _bass.Value = bass;
                EnterState(_bassFsm, "Set volume");
            }
            bool radioOn = (flags & HomeStereoState.FlagRadioOn) != 0;
            bool channel = (flags & HomeStereoState.FlagChannel) != 0;
            if (_radioOn != null && _radioOn.Value != radioOn)
            {
                _radioOn.Value = radioOn;
                EnterState(_radioFsm, radioOn ? "On" : "Off");
            }
            if (_channel != null && _channel.Value != channel)
            {
                _channel.Value = channel;
                EnterState(_channelFsm, channel ? "Channel 1" : "Folkradio");
            }
        }

        private static void EnterState(PlayMakerFSM? fsm, string state)
        {
            if (fsm == null || !FsmHook.HasState(fsm, state)) return;
            var world = WorldSyncManager.Instance;
            bool wasApplyingRemote = world != null && world.ApplyingRemote;
            try
            {
                if (world != null) world.ApplyingRemote = true;
                if (FsmHook.EnsureRemoteEntry(fsm, state))
                    FsmHook.FireRemoteEntry(fsm, state);
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug("HomeStereoSync: state apply failed: " + e.Message);
            }
            finally
            {
                if (world != null) world.ApplyingRemote = wasApplyingRemote;
            }
        }

        private static bool TryGetFreshPlayerPose(SessionManager session, byte playerId, out Vector3 position)
        {
            float now = Time.unscaledTime;
            foreach (var player in session.Players)
            {
                if (player.PlayerId != playerId) continue;
                if (player.LastTransformTime <= 0f || now - player.LastTransformTime > PlayerPoseMaxAgeSeconds)
                    break;
                position = player.Position;
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        private static bool IsValidIntent(HomeStereoIntent message)
        {
            return IsKnownFlags(message.Flags) && IsValidValue(message.Volume, 0f, 1f)
                && IsValidValue(message.Bass, -1f, 1f);
        }

        private static bool IsValidState(HomeStereoState message)
        {
            return IsKnownFlags(message.Flags) && IsValidValue(message.Volume, 0f, 1f)
                && IsValidValue(message.Bass, -1f, 1f);
        }

        private static bool IsKnownFlags(byte flags)
        {
            return (flags & ~(HomeStereoState.FlagRadioOn | HomeStereoState.FlagChannel)) == 0;
        }

        private static bool IsValidValue(float value, float min, float max)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
        }
    }
}
