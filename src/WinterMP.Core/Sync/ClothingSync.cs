using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Replicates each player's worn clothing (PLAN.md §4.4/§4.8). Reads the two
    /// local FsmInts — ClothingStage on PLAYER/BodyTemp (warmth tier) and ClothingType
    /// on the FPS-camera Piss FSM (outfit variant) — and reports changes to the host,
    /// which relays to the other guests. Clothing is owner-authoritative: each player
    /// owns their own dressing, so we never write our own local vars from the wire.
    /// The stored per-player snapshot feeds the remote-avatar visual via TryGetClothing.
    /// </summary>
    internal sealed class ClothingSync
    {
        private const float ProbeIntervalSeconds = 5f;
        private const float ReportIntervalSeconds = 3f;

        private const string ClothingStagePath = "PLAYER/BodyTemp";
        private const string ClothingTypePath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/Piss";

        private FsmInt? _clothingStage;
        private FsmInt? _clothingType;
        private float _nextProbeAt;
        private float _nextReportAt;
        private bool _loggedStage;
        private bool _loggedType;

        private bool _hasSent;
        private byte _lastSentStage;
        private byte _lastSentType;

        private readonly Dictionary<byte, ClothingSnapshot> _remote = new Dictionary<byte, ClothingSnapshot>();

        private struct ClothingSnapshot
        {
            public byte Stage;
            public byte Type;
        }

        public void Clear()
        {
            _clothingStage = null;
            _clothingType = null;
            _nextProbeAt = 0f;
            _nextReportAt = 0f;
            _loggedStage = false;
            _loggedType = false;
            _hasSent = false;
            _lastSentStage = 0;
            _lastSentType = 0;
            _remote.Clear();
        }

        // Runs for BOTH host and guest: the host detects its own clothing change here
        // and SendWorldMessage broadcasts it (IsHost path), while guests report to the host.
        public void Update(SessionManager session)
        {
            // Both host and guest only bother once another player is present.
            if (session.PlayerCount == 0) return;

            if (Time.unscaledTime < _nextReportAt) return;
            _nextReportAt = Time.unscaledTime + ReportIntervalSeconds;

            Locate();
            if (_clothingStage == null && _clothingType == null) return;

            byte stage = ReadByte(_clothingStage);
            byte type = ReadByte(_clothingType);

            if (_hasSent && stage == _lastSentStage && type == _lastSentType)
                return;

            _hasSent = true;
            _lastSentStage = stage;
            _lastSentType = type;

            session.SendWorldMessage(
                new PlayerClothingState
                {
                    PlayerId = session.LocalPlayerId,
                    ClothingStage = stage,
                    ClothingType = type,
                },
                Channel.ReliableOrdered);
        }

        public void OnRemoteClothingState(PlayerClothingState message)
        {
            var session = SessionManager.Instance;
            if (session != null && message.PlayerId == session.LocalPlayerId)
                return; // echo guard: never re-apply our own clothing as a remote

            _remote[message.PlayerId] = new ClothingSnapshot
            {
                Stage = message.ClothingStage,
                Type = message.ClothingType,
            };

            SyncEventLog.Record("clothing",
                $"p{message.PlayerId} stage {message.ClothingStage} type {message.ClothingType}");

            // Visual is applied by the pull in PlayerSyncManager.UpdateAvatars (it calls
            // TryGetClothing -> avatar.SetClothing). The pull is robust to the avatar rig
            // not existing yet when this message arrives, so no direct push here.
        }

        // Host-side: the current clothing of every player except the joiner, so a guest
        // joining mid-session sees everyone in their real outfit/warmth tier. Clothing is
        // otherwise change-only (no keepalive, not in the chunked snapshot), so without this
        // a joiner renders already-dressed players in default clothing until each next changes
        // clothes. Reconstructs exactly the _remote set the joiner would have built had it been
        // present from the start: the host's own current clothing (local FSM vars, not in
        // _remote) plus every relayed guest, minus the joiner itself.
        public IEnumerable<PlayerClothingState> BuildSnapshot(byte localPlayerId, byte excludePlayerId)
        {
            if (localPlayerId != excludePlayerId)
            {
                yield return new PlayerClothingState
                {
                    PlayerId = localPlayerId,
                    ClothingStage = _clothingStage != null ? ReadByte(_clothingStage) : _lastSentStage,
                    ClothingType = _clothingType != null ? ReadByte(_clothingType) : _lastSentType,
                };
            }

            foreach (var kv in _remote)
            {
                if (kv.Key == excludePlayerId) continue;
                yield return new PlayerClothingState
                {
                    PlayerId = kv.Key,
                    ClothingStage = kv.Value.Stage,
                    ClothingType = kv.Value.Type,
                };
            }
        }

        public bool TryGetClothing(byte playerId, out byte stage, out byte type)
        {
            if (_remote.TryGetValue(playerId, out var snapshot))
            {
                stage = snapshot.Stage;
                type = snapshot.Type;
                return true;
            }

            stage = 0;
            type = 0;
            return false;
        }

        // Called when a player leaves so a reused player-id slot (host recycles ids for
        // reconnecting guests, PLAN §4.5) can't briefly wear the previous occupant's outfit,
        // and so _remote can't grow unbounded across a long session's joins/leaves.
        public void Forget(byte playerId) => _remote.Remove(playerId);

        private void Locate()
        {
            if (Time.unscaledTime < _nextProbeAt) return;
            _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds;

            if (_clothingStage == null)
            {
                _clothingStage = FindLocalInt(ClothingStagePath, "Calculations", "ClothingStage");
                if (_clothingStage != null && !_loggedStage)
                {
                    _loggedStage = true;
                    WinterMPPlugin.Log.LogInfo("ClothingSync: found ClothingStage.");
                }
            }

            if (_clothingType == null)
            {
                _clothingType = FindLocalInt(ClothingTypePath, "Logic", "ClothingType");
                if (_clothingType != null && !_loggedType)
                {
                    _loggedType = true;
                    WinterMPPlugin.Log.LogInfo("ClothingSync: found ClothingType.");
                }
            }
        }

        // The FPS-camera Piss object carries two FSMs ("Logic" + "DrinkPiss") and
        // PLAYER/BodyTemp could gain more, so we must match by FsmName — a bare
        // GetComponent<PlayMakerFSM>() returns an arbitrary one and the var would
        // silently never resolve.
        private static FsmInt? FindLocalInt(string scenePath, string fsmName, string varName)
        {
            try
            {
                var go = GameObject.Find(scenePath);
                if (go == null) return null;

                var fsms = go.GetComponents<PlayMakerFSM>();
                if (fsms == null) return null;

                foreach (var fsm in fsms)
                {
                    if (fsm == null || fsm.FsmName != fsmName) continue;
                    var variable = fsm.FsmVariables.FindFsmInt(varName);
                    if (variable != null) return variable;
                }

                return null;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug(
                    "ClothingSync: local int '" + varName + "' on '" + fsmName + "' at '" + scenePath + "' failed: " + e.Message);
                return null;
            }
        }

        private static byte ReadByte(FsmInt? variable)
        {
            if (variable == null) return 0;
            return (byte)Mathf.Clamp(variable.Value, 0, 255);
        }
    }
}
