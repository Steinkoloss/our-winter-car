using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
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

        public static PlayerSyncManager? Instance { get; private set; }

        /// <summary>The game's player root object in the GAME scene (verified in catalog dumps).</summary>
        private const string PlayerObjectName = "PLAYER";

        private readonly Dictionary<byte, RemoteAvatar?> _avatars = new Dictionary<byte, RemoteAvatar?>();
        private readonly List<byte> _staleIds = new List<byte>();

        private Transform? _localPlayer;
        private CharacterController? _localController;
        private float _standingControllerHeight = -1f;
        private readonly PlayerMoveStateReader _moveStateReader = new PlayerMoveStateReader();
        private float _nextSearchAt;
        private float _nextSendAt;
        private ushort _sequence;
        private string _lastLevel = string.Empty;
        private bool _playerSyncDisabled;
        private readonly GuestSpawnRelocator _guestRelocator = new GuestSpawnRelocator();
        private readonly PlayerNeedsSync _needsSync = new PlayerNeedsSync();
        private readonly PlayerSleepHook _sleepHook = new PlayerSleepHook();

        internal GuestSpawnRelocator GuestRelocator => _guestRelocator;
        internal PlayerNeedsSync NeedsSync => _needsSync;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void OnGuestSpawn(GuestSpawn message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;

            var prompt = UI.GuestSpawnPrompt.Instance;
            if (prompt != null)
                prompt.ShowOffer(message);
            else if (!message.HasLastPosition)
                _guestRelocator.ApplyImmediate(message.HostPosition, message.HostRotation);
        }

        private void Update()
        {
            if (_playerSyncDisabled) return;

            try
            {
                UpdatePlayerSync();
            }
            catch (Exception e)
            {
                _playerSyncDisabled = true;
                WinterMPPlugin.Log.LogError($"PlayerSync disabled after unhandled error: {e}");
                SyncEventLog.Record("fatal", $"PlayerSync: {e}");
                SyncEventLog.DumpToFile();
            }
        }

        private void UpdatePlayerSync()
        {
            var session = SessionManager.Instance;
            if (session == null || (session.State != SessionState.Hosting && session.State != SessionState.Connected))
            {
                _playerSyncDisabled = false;
                DestroyAllAvatars();
                return;
            }

            WatchLevelChanges();
            if (_guestRelocator.HasPending)
                _guestRelocator.TryApply();

            try { _needsSync.Locate(); _needsSync.UpdateGuest(session); }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning($"PlayerNeedsSync: {e.Message}"); }

            if (session.IsHost)
            {
                try { _sleepHook.Probe(session); }
                catch (Exception e) { WinterMPPlugin.Log.LogWarning($"PlayerSleepHook: {e.Message}"); }
            }

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
            _moveStateReader.Reset();
            _needsSync.Reset();
            _sleepHook.Reset();
            NpcCharacterFactory.ClearCache();
            _nextSearchAt = 0f;
            _avatars.Clear();
        }

        // ------------------------------------------------------------------ sending

        private void SendLocalTransform(SessionManager session)
        {
            // Host only streams once someone has joined; guests always stream to the host.
            if (session.IsHost && session.PlayerCount == 0) return;

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

            var position = PlayerPoseReader.ReadFeetPosition(_localPlayer, _localController);
            var rotation = PlayerPoseReader.ReadLookRotation(_localPlayer);
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
            if (_localPlayer == null) return 0;

            if (_localController != null)
            {
                float height = _localController.height;
                if (height > _standingControllerHeight)
                    _standingControllerHeight = height;
            }

            return _moveStateReader.Read(_localPlayer, _localController, _standingControllerHeight);
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
                    try
                    {
                        avatar = RemoteAvatar.Create(player.PlayerId, player.Name);
                    }
                    catch (Exception e)
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"PlayerSync: avatar rig failed for {player.Name} (id {player.PlayerId}): {e.Message}");
                        avatar = RemoteAvatar.CreateCapsule(player.PlayerId, player.Name);
                    }

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
