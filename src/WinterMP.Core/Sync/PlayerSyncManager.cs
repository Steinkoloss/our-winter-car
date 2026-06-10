using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// M2 player presence: streams the local player's pose to the session and
    /// renders a <see cref="RemoteAvatar"/> for every remote player that has sent
    /// transforms. Lives on the persistent WinterMP GameObject; avatars are scene
    /// objects and die with each level load, so they are lazily (re)created.
    /// </summary>
    public sealed class PlayerSyncManager : MonoBehaviour
    {
        private const float SendRateHz = 12f;
        private const float PlayerSearchIntervalSeconds = 2f;
        /// <summary>Avatar is hidden when no snapshot arrived for this long (e.g. peer in a menu).</summary>
        private const float StaleAfterSeconds = 5f;

        /// <summary>The game's player root object in the GAME scene (verified in catalog dumps).</summary>
        private const string PlayerObjectName = "PLAYER";

        private readonly Dictionary<byte, RemoteAvatar?> _avatars = new Dictionary<byte, RemoteAvatar?>();
        private readonly List<byte> _staleIds = new List<byte>();

        private Transform? _localPlayer;
        private CharacterController? _localController;
        private float _standingControllerHeight = -1f;
        private float _nextSearchAt;
        private float _nextSendAt;
        private ushort _sequence;
        private string _lastLevel = string.Empty;

        private void Update()
        {
            var session = SessionManager.Instance;
            if (session == null || (session.State != SessionState.Hosting && session.State != SessionState.Connected))
            {
                DestroyAllAvatars();
                return;
            }

            WatchLevelChanges();
            SendLocalTransform(session);
            UpdateAvatars(session);
        }

        private void WatchLevelChanges()
        {
            string level;
            try
            {
                level = Application.loadedLevelName ?? string.Empty;
            }
            catch
            {
                return;
            }

            if (level == _lastLevel) return;
            _lastLevel = level;

            // Scene swap destroyed both the game's player object and our avatars.
            _localPlayer = null;
            _localController = null;
            _standingControllerHeight = -1f;
            _nextSearchAt = 0f;
            _avatars.Clear();
        }

        // ------------------------------------------------------------------ sending

        private void SendLocalTransform(SessionManager session)
        {
            if (session.PlayerCount == 0) return;

            if (_localPlayer == null)
            {
                if (Time.unscaledTime < _nextSearchAt) return;
                _nextSearchAt = Time.unscaledTime + PlayerSearchIntervalSeconds;

                var playerObject = GameObject.Find(PlayerObjectName);
                if (playerObject == null) return; // not in the GAME scene yet
                _localPlayer = playerObject.transform;
                _localController = playerObject.GetComponent<CharacterController>();
                _standingControllerHeight = _localController != null ? _localController.height : -1f;
                WinterMPPlugin.Log.LogInfo($"PlayerSync: tracking local player '{GetPath(_localPlayer)}'.");
            }

            if (Time.unscaledTime < _nextSendAt) return;
            _nextSendAt = Time.unscaledTime + 1f / SendRateHz;

            var position = _localPlayer.position;
            var rotation = _localPlayer.rotation;
            session.SendPlayerTransform(new PlayerTransform
            {
                PlayerId = session.LocalPlayerId,
                Sequence = ++_sequence,
                Position = position.ToNet(),
                Rotation = rotation.ToNet(),
                MoveState = ReadLocalMoveState(),
            });
        }

        private byte ReadLocalMoveState()
        {
            if (_localController == null) return 0;

            float height = _localController.height;
            if (height > _standingControllerHeight)
                _standingControllerHeight = height;

            if (_standingControllerHeight <= 0f) return 0;

            // Crouch shrinks the controller noticeably; ignore tiny animation wobble.
            return height < _standingControllerHeight * 0.85f ? PlayerMoveState.Crouch : (byte)0;
        }

        // ------------------------------------------------------------------ avatars

        private void UpdateAvatars(SessionManager session)
        {
            _staleIds.Clear();
            foreach (var pair in _avatars)
                _staleIds.Add(pair.Key);

            foreach (var player in session.Players)
            {
                _staleIds.Remove(player.PlayerId);

                if (player.LastTransformTime <= 0f) continue; // never sent a pose

                _avatars.TryGetValue(player.PlayerId, out var avatar);
                if (avatar == null) // includes destroyed-by-scene-load Unity fake-null
                {
                    avatar = RemoteAvatar.Create(player.PlayerId, player.Name);
                    _avatars[player.PlayerId] = avatar;
                    WinterMPPlugin.Log.LogInfo($"PlayerSync: avatar created for {player.Name} (id {player.PlayerId}).");
                }

                avatar.SetTarget(player.Position, player.Rotation);
                avatar.SetMoveState(player.MoveState);
                avatar.SetVisible(Time.unscaledTime - player.LastTransformTime < StaleAfterSeconds);

                // Drivers and passengers ride their vehicle, not the world-space
                // pose stream (which trails behind the smoothed car).
                var world = WorldSyncManager.Instance;
                var passengers = PassengerController.Instance;
                Transform? seat = null, vehicle = null;
                bool anchored = world != null && world.TryGetDriverAnchor(player.PlayerId, out seat, out vehicle);
                if (!anchored && passengers != null)
                    anchored = passengers.TryGetSeatAnchor(player.PlayerId, out seat, out vehicle);

                if (anchored)
                    avatar.SetAnchor(seat, vehicle);
                else
                    avatar.ClearAnchor();
            }

            // Players that left the session.
            foreach (byte playerId in _staleIds)
            {
                if (_avatars.TryGetValue(playerId, out var avatar) && avatar != null)
                    Destroy(avatar.gameObject);
                _avatars.Remove(playerId);
            }
        }

        private void DestroyAllAvatars()
        {
            if (_avatars.Count == 0) return;
            foreach (var avatar in _avatars.Values)
            {
                if (avatar != null)
                    Destroy(avatar.gameObject);
            }
            _avatars.Clear();
        }

        private static string GetPath(Transform transform)
        {
            string path = transform.name;
            var current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }
    }
}
