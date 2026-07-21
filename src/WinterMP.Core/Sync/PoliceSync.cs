using System;
using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Converts a guest-local police checkpoint result into a host-validated fine.
    /// Police collision/controller FSMs cannot be replayed on the host because a
    /// remote guest has no host-side PLAYER collider, so the host instead checks
    /// the reported checkpoint against fresh transform authority and applies only
    /// the durable Fines record.
    /// </summary>
    internal sealed class PoliceSync
    {
        private const string CheckpointPrefix = "TRAFFIC/Police/Checkpoints/Cops/";
        private const string FinesPath = "HOMENEW/Functions/FunctionsDisable/Fines";
        private const float ScanIntervalSeconds = 5f;
        private const float IntentIntervalSeconds = 1f;
        private const float PlayerPoseMaxAgeSeconds = 2f;
        private const float PlayerCheckpointMaxDistance = 28f;
        private const float VehicleCheckpointMaxDistance = 36f;
        private const float FineEpsilon = 0.01f;
        private const float MaximumFine = 10000f;

        private sealed class Checkpoint
        {
            public uint Id;
            public Transform Transform = null!;
            public FsmBool? Alcohol;
            public FsmBool? Fuel;
            public FsmBool? Helmet;
            public FsmBool? Inspection;
            public FsmBool? Radar;
            public FsmBool? Regplates;
            public FsmBool? Seatbelts;
            public FsmBool? Speeding;
        }

        private readonly ItemWorldSync _items;
        private readonly Dictionary<uint, Checkpoint> _checkpoints = new Dictionary<uint, Checkpoint>();
        private readonly Dictionary<byte, ushort> _lastIntentSequence = new Dictionary<byte, ushort>();
        private FsmString? _finePrice;
        private PoliceState? _active;
        private PoliceState? _pending;
        private float _nextScanAt;
        private float _nextIntentAt;
        private float _lastReportedFine = -1f;
        private uint _lastReportedCheckpoint;
        private byte _lastReportedFlags;
        private ushort _outIntentSequence;
        private ushort _outStateSequence;
        private ushort _lastRemoteStateSequence;

        public PoliceSync(ItemWorldSync items)
        {
            _items = items;
        }

        public void Clear()
        {
            _checkpoints.Clear();
            _lastIntentSequence.Clear();
            _finePrice = null;
            _active = null;
            _pending = null;
            _nextScanAt = 0f;
            _nextIntentAt = 0f;
            _lastReportedFine = -1f;
            _lastReportedCheckpoint = 0;
            _lastReportedFlags = 0;
            _outIntentSequence = 0;
            _outStateSequence = 0;
            _lastRemoteStateSequence = 0;
        }

        public void Update(SessionManager session)
        {
            Scan();
            ApplyPending();
            if (session.IsHost)
                ObserveHostFine(session);
            else
                SendObservedIntent(session);
        }

        public PoliceState? BuildSnapshot()
        {
            Scan(force: true);
            return _active;
        }

        public bool TryAcceptIntent(PoliceIntent message, out PoliceState state)
        {
            state = new PoliceState();
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return false;
            if (message.OffenceFlags == 0 || (message.OffenceFlags & ~PoliceIntent.KnownOffenceMask) != 0
                || !IsValidFine(message.Fine))
                return false;

            Scan();
            if (!_checkpoints.TryGetValue(message.CheckpointId, out var checkpoint)) return false;
            if (!TryGetFreshPlayerPose(session, message.PlayerId, out Vector3 playerPosition)) return false;
            if ((playerPosition - checkpoint.Transform.position).sqrMagnitude
                > PlayerCheckpointMaxDistance * PlayerCheckpointMaxDistance)
                return false;
            if (!HasNearbyDelegatedVehicle(message.PlayerId, checkpoint.Transform.position)) return false;

            if (_lastIntentSequence.TryGetValue(message.PlayerId, out ushort last))
            {
                ushort diff = (ushort)(message.Sequence - last);
                if (diff == 0 || diff > short.MaxValue) return false;
            }
            _lastIntentSequence[message.PlayerId] = message.Sequence;

            state = new PoliceState
            {
                PlayerId = message.PlayerId,
                OffenceFlags = message.OffenceFlags,
                Flags = PoliceState.FlagActive,
                Sequence = ++_outStateSequence,
                CheckpointId = message.CheckpointId,
                Fine = message.Fine,
            };
            _active = state;
            WriteFine(message.Fine);
            WinterMPPlugin.Log.LogInfo(
                string.Format(CultureInfo.InvariantCulture,
                    "PoliceSync: accepted player {0} checkpoint {1:X8}, flags {2:X2}, fine {3:0.##}.",
                    message.PlayerId, message.CheckpointId, message.OffenceFlags, message.Fine));
            return true;
        }

        public void Apply(PoliceState message)
        {
            Scan();
            if (_finePrice == null)
            {
                _pending = message;
                return;
            }

            ushort diff = (ushort)(message.Sequence - _lastRemoteStateSequence);
            if (_lastRemoteStateSequence != 0 && (diff == 0 || diff > short.MaxValue)) return;
            _lastRemoteStateSequence = message.Sequence;
            _active = message.IsActive ? message : null;
            if (message.IsActive && IsValidFine(message.Fine))
                WriteFine(message.Fine);

            // Host ack of our own report: only now advance the send-dedup baseline.
            // Latching at send time meant a transiently rejected intent (stale pose,
            // vehicle momentarily out of range) was silently lost forever.
            var session = SessionManager.Instance;
            if (message.IsActive && session != null && message.PlayerId == session.LocalPlayerId)
            {
                _lastReportedCheckpoint = message.CheckpointId;
                _lastReportedFlags = message.OffenceFlags;
                _lastReportedFine = message.Fine;
            }
        }

        private void SendObservedIntent(SessionManager session)
        {
            if (_finePrice == null || Time.unscaledTime < _nextIntentAt) return;
            if (!TryReadFine(out float fine) || !TryFindActiveCheckpoint(out var checkpoint, out byte flags)) return;

            bool changed = _lastReportedCheckpoint != checkpoint.Id || _lastReportedFlags != flags
                || Mathf.Abs(_lastReportedFine - fine) > FineEpsilon;
            if (!changed) return;

            _nextIntentAt = Time.unscaledTime + IntentIntervalSeconds;
            // Deliberately NOT latching _lastReported* here — the baseline advances when
            // the host's accepting PoliceState echoes back (see Apply), so a transiently
            // rejected report re-sends each interval instead of vanishing.
            session.SendWorldMessage(new PoliceIntent
            {
                PlayerId = session.LocalPlayerId,
                OffenceFlags = flags,
                Sequence = ++_outIntentSequence,
                CheckpointId = checkpoint.Id,
                Fine = fine,
            }, Channel.ReliableOrdered);
        }

        private void ObserveHostFine(SessionManager session)
        {
            if (_finePrice == null) return;
            if (!TryReadFine(out float fine))
            {
                // Price cleared = fine paid/reset (the game's Pay flow wipes it). Retire
                // the shared record so late joiners don't inherit a phantom fine. NOTE:
                // "no active checkpoint" below is NOT retirement — checkpoint crime flags
                // reset when the stop ends while the fine is still owed.
                RetireHostFine(session);
                return;
            }
            if (!TryFindActiveCheckpoint(out var checkpoint, out byte flags))
                return;

            bool changed = _active == null
                || _active.PlayerId != session.LocalPlayerId
                || _active.CheckpointId != checkpoint.Id
                || _active.OffenceFlags != flags
                || Mathf.Abs(_active.Fine - fine) > FineEpsilon;
            if (!changed) return;

            _active = new PoliceState
            {
                PlayerId = session.LocalPlayerId,
                OffenceFlags = flags,
                Flags = PoliceState.FlagActive,
                Sequence = ++_outStateSequence,
                CheckpointId = checkpoint.Id,
                Fine = fine,
            };
            if (session.PlayerCount > 0)
                session.SendWorldMessage(_active, Channel.ReliableOrdered);
        }

        private void RetireHostFine(SessionManager session)
        {
            if (_active == null || !_active.IsActive) return;
            var retired = new PoliceState
            {
                PlayerId = _active.PlayerId,
                OffenceFlags = _active.OffenceFlags,
                Flags = 0,
                Sequence = ++_outStateSequence,
                CheckpointId = _active.CheckpointId,
                Fine = 0f,
            };
            _active = null;
            if (session.PlayerCount > 0)
                session.SendWorldMessage(retired, Channel.ReliableOrdered);
            WinterMPPlugin.Log.LogInfo("PoliceSync: fine paid/reset — retired shared record.");
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
                    if (path == FinesPath && fsm.FsmName == "Activate" && _finePrice == null)
                    {
                        _finePrice = fsm.FsmVariables.FindFsmString("Price");
                        WinterMPPlugin.Log.LogInfo("PoliceSync: registered fines record.");
                    }
                    else if (path.StartsWith(CheckpointPrefix, StringComparison.Ordinal)
                        && fsm.FsmName == "Animations")
                    {
                        uint id = StableHash.Fnv1a32(path + "::" + fsm.FsmName);
                        if (!_checkpoints.ContainsKey(id)) BindCheckpoint(path, fsm, id);
                    }
                }
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug("PoliceSync: scan failed: " + e.Message);
            }
        }

        private void BindCheckpoint(string path, PlayMakerFSM fsm, uint id)
        {
            _checkpoints[id] = new Checkpoint
            {
                Id = id,
                Transform = fsm.transform,
                Alcohol = fsm.FsmVariables.FindFsmBool("CrimeAlcohol"),
                Fuel = fsm.FsmVariables.FindFsmBool("CrimeFuel"),
                Helmet = fsm.FsmVariables.FindFsmBool("CrimeHelmet"),
                Inspection = fsm.FsmVariables.FindFsmBool("CrimeInspection"),
                Radar = fsm.FsmVariables.FindFsmBool("CrimeRadar"),
                Regplates = fsm.FsmVariables.FindFsmBool("CrimeRegplates"),
                Seatbelts = fsm.FsmVariables.FindFsmBool("CrimeSeatbelts"),
                Speeding = fsm.FsmVariables.FindFsmBool("CrimeSpeeding"),
            };
            WinterMPPlugin.Log.LogInfo("PoliceSync: registered checkpoint " + path + ".");
        }

        private void ApplyPending()
        {
            if (_pending == null || _finePrice == null) return;
            var pending = _pending;
            _pending = null;
            Apply(pending);
        }

        private bool TryFindActiveCheckpoint(out Checkpoint checkpoint, out byte flags)
        {
            foreach (var candidate in _checkpoints.Values)
            {
                byte candidateFlags = ReadFlags(candidate);
                if (candidateFlags == 0) continue;
                checkpoint = candidate;
                flags = candidateFlags;
                return true;
            }

            checkpoint = null!;
            flags = 0;
            return false;
        }

        private static byte ReadFlags(Checkpoint checkpoint)
        {
            byte flags = 0;
            if (Read(checkpoint.Alcohol)) flags |= 1;
            if (Read(checkpoint.Fuel)) flags |= 2;
            if (Read(checkpoint.Helmet)) flags |= 4;
            if (Read(checkpoint.Inspection)) flags |= 8;
            if (Read(checkpoint.Radar)) flags |= 16;
            if (Read(checkpoint.Regplates)) flags |= 32;
            if (Read(checkpoint.Seatbelts)) flags |= 64;
            if (Read(checkpoint.Speeding)) flags |= 128;
            return flags;
        }

        private bool HasNearbyDelegatedVehicle(byte playerId, Vector3 checkpointPosition)
        {
            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || item.Body == null || item.RemoteOwner != playerId) continue;
                if ((item.Body.transform.position - checkpointPosition).sqrMagnitude
                    <= VehicleCheckpointMaxDistance * VehicleCheckpointMaxDistance)
                    return true;
            }
            return false;
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

        private bool TryReadFine(out float fine)
        {
            fine = 0f;
            if (_finePrice == null) return false;
            string text = _finePrice.Value ?? string.Empty;
            text = text.Replace("mk", string.Empty).Replace("MK", string.Empty).Trim();
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out fine)
                && !float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out fine))
                return false;
            return IsValidFine(fine);
        }

        private void WriteFine(float fine)
        {
            if (_finePrice == null) return;
            try
            {
                _finePrice.Value = fine.ToString("0.##", CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogDebug("PoliceSync: fine write failed: " + e.Message);
            }
        }

        private static bool IsValidFine(float fine)
        {
            return !float.IsNaN(fine) && !float.IsInfinity(fine)
                && fine > FineEpsilon && fine <= MaximumFine;
        }

        private static bool Read(FsmBool? value)
        {
            return value != null && value.Value;
        }
    }
}
