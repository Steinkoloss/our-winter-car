using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed class ItemWorldSync
    {
        private const float PendingTtlSeconds = 120f;
        private const float SnapshotPoseTtlSeconds = 300f;
        private const int DoorSnapshotChunk = 60;
        private const int BoltSnapshotChunk = 80;
        private const int PartSnapshotChunk = 80;
        private const int ItemSnapshotChunk = 40;
        private static readonly string[] AllowedRawEvents = { "TIGHTEN", "UNTIGHTEN" };

        private readonly WorldSyncBridge _bridge;
        private VehicleWorldSync _vehicles = null!;

        public ItemWorldSync(WorldSyncBridge bridge)
        {
            _bridge = bridge;
        }

        public void BindVehicles(VehicleWorldSync vehicles) => _vehicles = vehicles;


        private readonly Dictionary<uint, SyncedItem> _items = new Dictionary<uint, SyncedItem>();
        private readonly Dictionary<Rigidbody, bool> _trackedBodies = new Dictionary<Rigidbody, bool>();
        private readonly Dictionary<uint, PendingPose> _pendingItemPoses = new Dictionary<uint, PendingPose>();
        private readonly HashSet<uint> _sessionDespawnedItems = new HashSet<uint>();
        private readonly HashSet<uint> _pendingDespawnedItems = new HashSet<uint>();

        internal IEnumerable<uint> SessionDespawnedIds => _sessionDespawnedItems;
        internal IDictionary<uint, SyncedItem> Items => _items;
        public int ItemCount => _items.Count;

        internal void Clear()
        {
            _items.Clear();
            _trackedBodies.Clear();
            _pendingItemPoses.Clear();
            _sessionDespawnedItems.Clear();
            _pendingDespawnedItems.Clear();
        }

        internal void ReleaseSession()
        {
            foreach (var item in _items.Values)
            {
                if (item.Body != null && item.RemoteOwner != WorldSyncIds.NoOwner)
                    item.Body.isKinematic = item.OriginalKinematic;
                item.RemoteOwner = WorldSyncIds.NoOwner;
                item.RemoteIsDriver = false;
                item.RemoteVehicleStream = false;
                item.LocalDriveActive = false;
                item.LocallyOwned = false;
                item.LastRemoteAt = -999f;
                item.CargoFollowActive = false;
                item.CargoFollowVehicleId = 0;
                item.RemoteEngineUntil = -999f;
                item.RemoteClimateUntil = -999f;
                item.RemoteEngineOn = false;
                item.RemoteAccOn = false;
                item.RemoteElectricsApplied = false;
                if (item.RemoteEngineAudio != null && item.RemoteEngineAudio.isPlaying)
                    item.RemoteEngineAudio.Stop();
                SetSeatBlocked(item, false);
            }

            _pendingItemPoses.Clear();
            _pendingDespawnedItems.Clear();
        }



        private const float ItemSendRateHz = 10f;
        private const float VehicleSendRateHz = 15f;
        /// <summary>You can only push/carry items near you; claims need proximity.</summary>
        private const float ItemClaimRadius = 4f;
        /// <summary>Vehicle claims: covers the cabin (driver) and pushing from any side.</summary>
        private const float VehicleClaimRadius = 7f;
        /// <summary>Remote stream is considered live for this long after the last packet.</summary>
        private const float RemoteHoldSeconds = ItemTransformPolicy.ItemRemoteHoldSeconds;
        /// <summary>Remote drivers keep the vehicle frozen longer — packet loss must not
        /// wake local physics and let nearby players fight over the car.</summary>
        private const float RemoteDriverHoldSeconds = ItemTransformPolicy.DriverRemoteHoldSeconds;
        /// <summary>A seated driver holds the vehicle with keepalives even when parked,
        /// so ownership can't flap when two players sit in (their copies of) one car.</summary>
        private const float DriverKeepaliveSeconds = 0.4f;
        /// <summary>Owner releases an item (final packet) after this long without motion.</summary>
        private const float ItemStillSeconds = 1.5f;
        private const float VehicleStillSeconds = 3f;
        private const float MoveEpsilonSqr = 1e-6f; // 1 mm — carried items move slowly
        private const float VehicleMoveEpsilonSqr = 2.5e-3f; // 5 cm — ignore idle-engine jitter
        /// <summary>Root rigidbodies at least this heavy are treated as vehicles.</summary>
        /// <summary>Remote pose smoothing (same feel as RemoteAvatar).</summary>
        private const float RemoteLerpSpeed = 12f;
        private const float RemoteSnapDistance = 15f;
        /// <summary>Items inside a driven vehicle are never claimed locally —
        /// cargo rides with the driver's physics / vehicle stream, not independent
        /// world-space item packets (which desync and fight the car).</summary>
        private const float VehicleInteriorRadius = 7f;
        

        internal int ScanItems()
        {
            // Collect new candidates first so same-path clones (six sausages at the
            // scene root...) get deterministic ordinals from one consistent batch.
            var newcomers = new List<SyncedItem>();
            var bodies = Resources.FindObjectsOfTypeAll(typeof(Rigidbody));
            foreach (var obj in bodies)
            {
                var body = obj as Rigidbody;
                if (body == null || _trackedBodies.ContainsKey(body)) continue;

                try
                {
                    if (!body.gameObject.activeInHierarchy) continue;

                    bool isVehicle = SyncCatalog.IsVehicleRoot(body);
                    bool isItem = !isVehicle && SyncCatalog.IsPickableRigidbody(body);
                    if (!isItem && !isVehicle) continue;

                    newcomers.Add(new SyncedItem
                    {
                        Body = body,
                        Path = ScenePath.Of(body.transform),
                        LastPosition = body.transform.position,
                        IsVehicle = isVehicle,
                    });
                }
                catch (Exception e)
                {
                    WinterMPPlugin.Log.LogDebug($"WorldSync: skipped rigidbody during scan: {e.Message}");
                }
            }

            if (newcomers.Count == 0) return 0;

            // Same-path groups: ordinal by initial position (same save => same order
            // on every machine). Quantized to 1 mm so float noise can't flip ties.
            var byPath = new Dictionary<string, List<SyncedItem>>();
            foreach (var item in newcomers)
            {
                if (!byPath.TryGetValue(item.Path, out var group))
                    byPath[item.Path] = group = new List<SyncedItem>();
                group.Add(item);
            }

            int added = 0;
            foreach (var pair in byPath)
            {
                var group = pair.Value;
                if (group.Count > 1)
                    group.Sort(CompareByInitialPosition);

                for (int i = 0; i < group.Count; i++)
                {
                    var item = group[i];
                    string prefix = item.IsVehicle ? "vehicle:" : "item:";
                    string idSource = group.Count > 1 ? prefix + item.Path + "#" + i : prefix + item.Path;
                    item.Id = StableHash.Fnv1a32(idSource);
                    _trackedBodies[item.Body] = true;

                    if (_pendingDespawnedItems.Contains(item.Id))
                    {
                        _pendingDespawnedItems.Remove(item.Id);
                        UnityEngine.Object.Destroy(item.Body.gameObject);
                        WinterMPPlugin.Log.LogInfo(
                            $"WorldSync: item '{item.Path}' removed from snapshot despawn list.");
                        continue;
                    }

                    if (_items.ContainsKey(item.Id))
                    {
                        // Late-discovered clone of an existing group — its ordinal may
                        // disagree across machines, so it is safer not to sync it.
                        WinterMPPlugin.Log.LogWarning($"WorldSync: item id collision, not syncing '{item.Path}'.");
                        continue;
                    }

                    _items[item.Id] = item;
                    if (item.IsVehicle)
                        WinterMPPlugin.Log.LogInfo($"WorldSync: vehicle registered: '{item.Path}' ({item.Body.mass:0} kg).");
                    else
                        TryRegisterConsumableHooks(item);
                    added++;

                    // A join snapshot may have arrived before this item was scanned.
                    if (_pendingItemPoses.TryGetValue(item.Id, out var pose))
                    {
                        _pendingItemPoses.Remove(item.Id);
                        if (Time.unscaledTime < pose.ExpiresAt)
                            ApplySnapshotPose(item, pose.Position, pose.Rotation);
                    }
                }
            }

            return added;
        }

        private void TryRegisterConsumableHooks(SyncedItem item)
        {
            if (item.Body == null || item.IsVehicle) return;

            foreach (var fsm in item.Body.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName != "Use" || _bridge.HookedFsms.ContainsKey(fsm)) continue;

                var despawnStates = new List<string>();
                SyncCatalog.CollectConsumableDespawnStates(fsm, despawnStates);
                if (despawnStates.Count == 0) continue;

                bool hooked = false;
                uint itemId = item.Id;
                foreach (string state in despawnStates)
                {
                    string captured = state;
                    if (!FsmHook.OnStateEnter(fsm, state, () => OnItemConsumed(itemId, captured))) continue;
                    hooked = true;
                }

                if (hooked)
                    _bridge.HookedFsms[fsm] = true;
            }
        }

        private void OnItemConsumed(uint itemId, string stateName)
        {
            if (_bridge.ApplyingRemote) return;
            AnnounceItemDespawn(itemId, $"consumed ({stateName})");
        }

        private static bool SessionSyncActive(SessionManager session)
        {
            if (session.State != SessionState.Hosting && session.State != SessionState.Connected)
                return false;
            return session.IsHost ? session.PlayerCount > 0 : true;
        }

        private void AnnounceItemDespawn(uint itemId, string reason)
        {
            if (!_items.TryGetValue(itemId, out var item) || item.DespawnSent) return;

            var session = SessionManager.Instance;
            if (session == null || !SessionSyncActive(session)) return;

            item.DespawnSent = true;
            TrackSessionDespawn(itemId);
            WinterMPPlugin.Log.LogInfo($"WorldSync: item {itemId:X8} despawn — {reason} (local).");
            SyncEventLog.Record("despawn", $"{itemId:X8} {reason}");
            session.SendWorldMessage(new ItemDespawn { ItemId = itemId }, Channel.ReliableOrdered);
        }

        private void TrackSessionDespawn(uint itemId)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return;
            _sessionDespawnedItems.Add(itemId);
        }

        public void OnRemoteItemDespawn(ItemDespawn message)
        {
            TrackSessionDespawn(message.ItemId);

            if (!_items.TryGetValue(message.ItemId, out var item)) return;

            WinterMPPlugin.Log.LogInfo($"WorldSync: item {message.ItemId:X8} despawn (remote).");
            item.DespawnSent = true;

            _bridge.ApplyingRemote = true;
            try
            {
                if (item.Body != null)
                    UnityEngine.Object.Destroy(item.Body.gameObject);
            }
            finally
            {
                _bridge.ApplyingRemote = false;
            }

            RemoveTrackedItem(message.ItemId, item.Body);
        }

        private void RemoveTrackedItem(uint itemId, Rigidbody? body)
        {
            _items.Remove(itemId);
            if (body != null)
                _trackedBodies.Remove(body);
        }

        private static int CompareByInitialPosition(SyncedItem a, SyncedItem b)
        {
            long ax = Quantize(a.LastPosition.x), bx = Quantize(b.LastPosition.x);
            if (ax != bx) return ax.CompareTo(bx);
            long ay = Quantize(a.LastPosition.y), by = Quantize(b.LastPosition.y);
            if (ay != by) return ay.CompareTo(by);
            return Quantize(a.LastPosition.z).CompareTo(Quantize(b.LastPosition.z));
        }

        private static long Quantize(float value) => (long)Math.Round(value * 1000f);

        internal uint ComputeItemCrc()
        {
            uint crc = StableHash.OffsetBasis;
            var ids = new List<uint>(_items.Keys);
            ids.Sort();
            foreach (uint id in ids)
            {
                if (!_items.TryGetValue(id, out var item) || item.Body == null || item.IsVehicle) continue;
                if (item.LocallyOwned || item.RemoteOwner != WorldSyncIds.NoOwner) continue;

                var body = item.Body;
                if (!body.IsSleeping() && body.velocity.sqrMagnitude > 0.04f) continue;

                var pos = body.transform.position;
                var rot = body.transform.rotation;
                crc = StableHash.Combine(crc, id);
                crc = StableHash.Combine(crc, (uint)Quantize(pos.x));
                crc = StableHash.Combine(crc, (uint)Quantize(pos.y));
                crc = StableHash.Combine(crc, (uint)Quantize(pos.z));
                crc = StableHash.Combine(crc, (uint)Quantize(rot.x * 1000f));
                crc = StableHash.Combine(crc, (uint)Quantize(rot.y * 1000f));
                crc = StableHash.Combine(crc, (uint)Quantize(rot.z * 1000f));
                crc = StableHash.Combine(crc, (uint)Quantize(rot.w * 1000f));
            }

            return crc;
        }

        internal uint ComputeVehicleCrc()
        {
            uint crc = StableHash.OffsetBasis;
            var ids = new List<uint>();
            foreach (var pair in _items)
            {
                if (pair.Value.IsVehicle) ids.Add(pair.Key);
            }

            ids.Sort();
            foreach (uint id in ids)
            {
                if (!_items.TryGetValue(id, out var item) || !_vehicles.TryReadVehicleChecksum(item, out byte flags,
                        out ushort rpm, out byte fuel, out byte coolant, out byte frost, out byte fog, out byte cabinTemp))
                {
                    continue;
                }

                crc = StableHash.Combine(crc, id);
                crc = StableHash.Combine(crc, flags);
                crc = StableHash.Combine(crc, rpm);
                crc = StableHash.Combine(crc, fuel);
                crc = StableHash.Combine(crc, coolant);
                crc = StableHash.Combine(crc, frost);
                crc = StableHash.Combine(crc, fog);
                crc = StableHash.Combine(crc, cabinTemp);
            }

            return crc;
        }

        internal IEnumerable<WorldItemSnapshot> BuildItemSnapshotChunks()
        {
            var items = new WorldItemSnapshot();
            foreach (var pair in _items)
            {
                var item = pair.Value;
                if (item.Body == null) continue;

                items.Entries.Add(new WorldItemSnapshot.Entry
                {
                    ItemId = pair.Key,
                    Position = item.Body.transform.position.ToNet(),
                    Rotation = item.Body.transform.rotation.ToNet(),
                });
                if (items.Entries.Count >= ItemSnapshotChunk)
                {
                    yield return items;
                    items = new WorldItemSnapshot();
                }
            }

            if (items.Entries.Count > 0)
                yield return items;
        }

        /// <summary>
        /// Host side: world state for a fresh joiner — every door we saw change
        /// plus the current pose of every item/vehicle, in send-ready chunks.
        /// </summary>
        public void OnRemoteItemSnapshot(WorldItemSnapshot message)
        {
            int applied = 0, parked = 0;
            foreach (var entry in message.Entries)
            {
                var position = entry.Position.ToUnity();
                var rotation = entry.Rotation.ToUnity();

                if (_items.TryGetValue(entry.ItemId, out var item) && item.Body != null)
                {
                    // Live streams beat the snapshot (it was built moments ago).
                    if (item.LocallyOwned || Time.unscaledTime - item.LastRemoteAt < GetRemoteHoldSeconds(item))
                        continue;
                    ApplySnapshotPose(item, position, rotation);
                    applied++;
                }
                else
                {
                    _pendingItemPoses[entry.ItemId] = new PendingPose
                    {
                        Position = position,
                        Rotation = rotation,
                        ExpiresAt = Time.unscaledTime + SnapshotPoseTtlSeconds,
                    };
                    parked++;
                }
            }

            WinterMPPlugin.Log.LogInfo($"WorldSync: item snapshot — {message.Entries.Count} entries, {applied} applied, {parked} parked.");
        }

        public void OnRemoteItemDespawnSnapshot(WorldItemDespawnSnapshot message)
        {
            int removed = 0, parked = 0;
            foreach (uint itemId in message.ItemIds)
            {
                if (_items.TryGetValue(itemId, out var item) && item.Body != null)
                {
                    item.DespawnSent = true;
                    _bridge.ApplyingRemote = true;
                    try
                    {
                        UnityEngine.Object.Destroy(item.Body.gameObject);
                    }
                    finally
                    {
                        _bridge.ApplyingRemote = false;
                    }

                    RemoveTrackedItem(itemId, item.Body);
                    removed++;
                }
                else
                {
                    _pendingDespawnedItems.Add(itemId);
                    parked++;
                }
            }

            WinterMPPlugin.Log.LogInfo(
                $"WorldSync: despawn snapshot — {message.ItemIds.Count} ids, {removed} removed, {parked} parked.");
        }

        private static void ApplySnapshotPose(SyncedItem item, Vector3 position, Quaternion rotation)
        {
            var body = item.Body;
            body.transform.position = position;
            body.transform.rotation = rotation;
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.Sleep();
            }

            // The teleport must not register as local motion (no claim, no stream).
            item.LastPosition = position;
            item.LastMovedAt = -999f;
        }


        // ------------------------------------------------------------------ items

        public void OnRemoteItemTransform(ItemTransform message)
        {
            if (!_items.TryGetValue(message.ItemId, out var item) || item.Body == null) return;

            float now = Time.unscaledTime;
            if (TryGetContainingVehicle(item, out SyncedItem? cargoVehicle)
                && cargoVehicle != null)
            {
                // Driver simulates cargo with the vehicle — never apply guest item streams.
                if (IsLocalVehicleOperator(cargoVehicle))
                    return;

                if (ItemTransformPolicy.ShouldIgnoreRemoteItemTransformForVehicleCargo(
                        item.IsVehicle,
                        IsVehicleInMotion(cargoVehicle, now),
                        message.IsFinal))
                    return;
            }

            // Stale unreliable packets from the same owner are dropped (wrap-aware).
            if (message.OwnerPlayerId == item.RemoteOwner)
            {
                if (ItemTransformPolicy.IsStaleSequence(item.LastRemoteSequence, message.Sequence))
                {
                    if (!message.IsFinal && !message.IsDriver)
                        ConnectionQuality.Instance.NoteUnreliableDropped();
                    return;
                }
            }

            if (!message.IsFinal && !message.IsDriver)
                ConnectionQuality.Instance.NoteUnreliableReceived();

            var session = SessionManager.Instance;
            if (item.LocallyOwned && session != null)
            {
                bool localIsDriver = item.IsVehicle && IsLocalVehicleOperator(item);
                if (!ItemTransformPolicy.RemoteClaimWinsOverLocal(
                        localIsDriver,
                        message.IsDriver,
                        session.LocalPlayerId,
                        message.OwnerPlayerId))
                    return;
                item.LocallyOwned = false;
                item.LocalDriveActive = false;
            }

            bool firstPacket = item.RemoteOwner != message.OwnerPlayerId;
            item.RemoteOwner = message.OwnerPlayerId;
            item.RemoteIsDriver = message.IsDriver;
            item.RemoteVehicleStream = message.IsVehicle && !message.IsFinal;
            item.LastRemoteSequence = message.Sequence;

            var body = item.Body;
            if (!item.KinematicSaved)
            {
                item.OriginalKinematic = body.isKinematic;
                item.KinematicSaved = true;
            }

            var position = message.Position.ToUnity();
            var rotation = message.Rotation.ToUnity();

            if (message.IsFinal)
            {
                body.isKinematic = item.OriginalKinematic;
                body.transform.position = position;
                body.transform.rotation = rotation;
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.Sleep();
                }

                item.RemoteOwner = WorldSyncIds.NoOwner;
                item.RemoteIsDriver = false;
                item.RemoteVehicleStream = false;
                item.LastRemoteAt = -999f;
                item.LastMovedAt = -999f; // the landing must not look like local motion
                item.LastPosition = position;
                SetSeatBlocked(item, false);
            }
            else
            {
                // Frozen while remote-driven; UpdateItems eases toward the target so
                // vehicles glide instead of teleporting at packet rate.
                body.isKinematic = true;
                if (firstPacket && item.IsVehicle)
                {
                    WinterMPPlugin.Log.LogInfo(
                        $"WorldSync: '{item.Path}' now {(message.IsDriver ? "driven" : "moved")} by player {message.OwnerPlayerId}.");
                    // Snap on first packet so a car that drove away doesn't stay parked
                    // locally until someone walks up and triggers a huge correction.
                    body.transform.position = position;
                    body.transform.rotation = rotation;
                }

                item.TargetPosition = position;
                item.TargetRotation = rotation;
                item.LastRemoteAt = Time.unscaledTime;

                // An occupied driver's seat must not be enterable locally. Push
                // streams (FlagVehicle without FlagDriver) leave the seat free —
                // policy lets a seated local player out-claim those.
                if (item.IsVehicle)
                    SetSeatBlocked(item, message.IsDriver);
            }
        }

        private readonly List<uint> _deadItemIds = new List<uint>();

        internal void UpdateItems(SessionManager session)
        {
            if (session.PlayerCount == 0) return;

            _bridge.FindLocalPlayer();
            float now = Time.unscaledTime;

            foreach (var pair in _items)
            {
                var item = pair.Value;
                var body = item.Body;
                if (body == null)
                {
                    if (!item.DespawnSent && !_bridge.ApplyingRemote)
                        AnnounceItemDespawn(pair.Key, "removed");
                    // Removing here would invalidate the enumerator (this killed
                    // world sync mid-session); collect and remove after the loop.
                    _deadItemIds.Add(pair.Key);
                    continue;
                }

                bool operating = item.IsVehicle && IsLocalPlayerDriving(item);
                bool remoteDriven = item.RemoteOwner != WorldSyncIds.NoOwner
                    && ItemTransformPolicy.IsRemoteStreamLive(
                        item.LastRemoteAt, now, item.RemoteIsDriver, item.RemoteVehicleStream);

                if (item.IsVehicle)
                    VehicleWorldSync.UpdateRemoteEngineAudio(item, now);

                if (operating && !item.LocallyOwned
                    && LocalDriverOutClaimsRemote(session, item, remoteDriven))
                {
                    ClaimItem(session, item, body, now);
                    remoteDriven = false;
                }

                if (remoteDriven && !item.LocallyOwned)
                {
                    if (TryGetContainingVehicle(item, out SyncedItem? cargoVehicle)
                        && cargoVehicle != null
                        && ShouldFollowVehicleCargo(item, cargoVehicle, now))
                    {
                        item.RemoteOwner = WorldSyncIds.NoOwner;
                        item.RemoteIsDriver = false;
                        item.RemoteVehicleStream = false;
                        item.LastRemoteAt = -999f;
                        ApplyVehicleCargoFollow(item, cargoVehicle, body);
                        continue;
                    }

                    ApplyRemoteSmoothing(item, body);
                    continue;
                }

                if (TryGetContainingVehicle(item, out SyncedItem? localCargoVehicle)
                    && localCargoVehicle != null
                    && IsLocalVehicleOperator(localCargoVehicle)
                    && IsVehicleInMotion(localCargoVehicle, now))
                {
                    ClearCargoFollow(item);
                    ReleaseLocalCargoOwnership(item, body);
                    item.LastPosition = body.transform.position;
                    item.LastMovedAt = now;
                    continue;
                }

                ClearCargoFollow(item);

                if (item.RemoteOwner != WorldSyncIds.NoOwner && !remoteDriven)
                {
                    body.isKinematic = item.OriginalKinematic;
                    item.RemoteOwner = WorldSyncIds.NoOwner;
                    item.RemoteIsDriver = false;
                    item.RemoteVehicleStream = false;
                    SetSeatBlocked(item, false);
                }

                var position = body.transform.position;
                if ((position - item.LastPosition).sqrMagnitude > item.MoveThresholdSqr)
                {
                    item.LastMovedAt = now;
                    item.LastPosition = position;
                }

                bool moving = now - item.LastMovedAt < item.StillSeconds;

                if (item.LocallyOwned)
                {
                    if (item.IsVehicle)
                    {
                        // Latch driver status while the car is in motion (seat
                        // detection can flicker mid-drive), but release it once
                        // the car is parked and the seat is empty — a permanent
                        // latch kept streaming "driver" keepalives forever and
                        // the other player could never use the car again.
                        if (operating)
                            item.LocalDriveActive = true;
                        else if (!moving)
                            item.LocalDriveActive = false;

                        if (item.LocalDriveActive || moving)
                        {
                            if (now >= item.NextSendAt)
                            {
                                SendItem(session, item, body, false);
                                item.NextSendAt = now + (moving
                                    ? 1f / item.SendRateHz
                                    : DriverKeepaliveSeconds);
                            }
                        }
                        else if (!ConnectionQuality.Instance.ShouldPauseOwnershipTransfers)
                        {
                            SendItem(session, item, body, true);
                            item.LocallyOwned = false;
                            item.LocalDriveActive = false;
                        }
                        else if (now >= item.NextSendAt)
                        {
                            SendItem(session, item, body, false);
                            item.NextSendAt = now + DriverKeepaliveSeconds;
                        }
                    }
                    else if (!moving)
                    {
                        if (!ConnectionQuality.Instance.ShouldPauseOwnershipTransfers)
                        {
                            SendItem(session, item, body, true);
                            item.LocallyOwned = false;
                        }
                        else if (now >= item.NextSendAt)
                        {
                            SendItem(session, item, body, false);
                            item.NextSendAt = now + DriverKeepaliveSeconds;
                        }
                    }
                    else if (now >= item.NextSendAt)
                    {
                        SendItem(session, item, body, false);
                        item.NextSendAt = now + 1f / item.SendRateHz;
                    }
                }
                else if (moving && CanClaim(item, position, now)
                         && !ConnectionQuality.Instance.ShouldPauseOwnershipTransfers)
                {
                    ClaimItem(session, item, body, now);
                }
            }

            if (_deadItemIds.Count > 0)
            {
                foreach (uint id in _deadItemIds)
                    RemoveTrackedItem(id, null);
                _deadItemIds.Clear();
            }
        }

        private bool CanClaim(SyncedItem item, Vector3 position, float now)
        {
            _bridge.FindLocalPlayer();
            if (_bridge.LocalPlayer == null) return false;

            if (item.IsVehicle && IsLocalVehicleOperator(item)) return true;

            if (item.IsVehicle && !IsLocalVehicleOperator(item)
                && !ItemTransformPolicy.AllowsVehicleProximityClaim(
                    item.RemoteIsDriver,
                    item.RemoteVehicleStream && ItemTransformPolicy.IsRemoteStreamLive(
                        item.LastRemoteAt, now, item.RemoteIsDriver, isVehicle: true),
                    item.RemoteOwner,
                    item.LastRemoteAt,
                    now))
                return false;

            if ((position - _bridge.LocalPlayer.position).sqrMagnitude >= item.ClaimRadius * item.ClaimRadius)
                return false;

            if (TryGetContainingVehicle(item, out SyncedItem? cargoVehicle)
                && cargoVehicle != null
                && ItemTransformPolicy.ShouldBlockClaimForVehicleCargo(
                    item.IsVehicle,
                    IsVehicleInMotion(cargoVehicle, now)))
                return false;

            return true;
        }

        private void ClaimItem(SessionManager session, SyncedItem item, Rigidbody body, float now)
        {
            // Taking over from a remote stream: restore physics before simulating.
            if (item.RemoteOwner != WorldSyncIds.NoOwner || (body.isKinematic && item.KinematicSaved))
            {
                body.isKinematic = item.OriginalKinematic;
                item.RemoteOwner = WorldSyncIds.NoOwner;
                item.RemoteIsDriver = false;
                item.RemoteVehicleStream = false;
                item.LastRemoteAt = -999f;
                SetSeatBlocked(item, false);
            }

            item.RemoteEngineUntil = -999f;

            item.LocallyOwned = true;
            item.LastMovedAt = now;
            // Driver status only when actually seated — a proximity claim (pushing
            // the car, standing nearby) must stream as FlagVehicle, not FlagDriver,
            // or it blocks the real driver's seat on the other machine.
            if (item.IsVehicle)
                item.LocalDriveActive = IsLocalPlayerDriving(item);
            SendItem(session, item, body, false);
            item.NextSendAt = now + 1f / item.SendRateHz;

            if (item.IsVehicle)
                WinterMPPlugin.Log.LogInfo($"WorldSync: claimed vehicle '{item.Path}' " +
                    $"({(IsLocalVehicleOperator(item) ? "driving" : "pushing")}).");
            else
                WinterMPPlugin.Log.LogDebug($"WorldSync: claimed item '{item.Path}'.");
        }

        private static void ApplyRemoteSmoothing(SyncedItem item, Rigidbody body)
        {
            var transform = body.transform;
            if ((transform.position - item.TargetPosition).sqrMagnitude > RemoteSnapDistance * RemoteSnapDistance)
            {
                transform.position = item.TargetPosition;
                transform.rotation = item.TargetRotation;
            }
            else
            {
                float t = 1f - Mathf.Exp(-RemoteLerpSpeed * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, item.TargetPosition, t);
                transform.rotation = Quaternion.Slerp(transform.rotation, item.TargetRotation, t);
            }

            item.LastPosition = transform.position;
        }

        private static void ClearCargoFollow(SyncedItem item)
        {
            item.CargoFollowActive = false;
            item.CargoFollowVehicleId = 0;
        }

        private static void ReleaseLocalCargoOwnership(SyncedItem item, Rigidbody body)
        {
            if (item.RemoteOwner != WorldSyncIds.NoOwner)
            {
                body.isKinematic = item.KinematicSaved ? item.OriginalKinematic : body.isKinematic;
                item.RemoteOwner = WorldSyncIds.NoOwner;
                item.RemoteIsDriver = false;
                item.RemoteVehicleStream = false;
                item.LastRemoteAt = -999f;
            }

            if (item.KinematicSaved)
                body.isKinematic = item.OriginalKinematic;

            item.LocallyOwned = false;
        }

        private void ApplyVehicleCargoFollow(SyncedItem item, SyncedItem vehicle, Rigidbody body)
        {
            if (vehicle.Body == null) return;

            var vehicleTransform = vehicle.Body.transform;
            if (!item.CargoFollowActive || item.CargoFollowVehicleId != vehicle.Id)
            {
                item.CargoFollowVehicleId = vehicle.Id;
                item.CargoFollowLocalPos = vehicleTransform.InverseTransformPoint(body.transform.position);
                item.CargoFollowLocalRot = Quaternion.Inverse(vehicleTransform.rotation) * body.transform.rotation;
                item.CargoFollowActive = true;
            }

            if (!item.KinematicSaved)
            {
                item.OriginalKinematic = body.isKinematic;
                item.KinematicSaved = true;
            }

            body.isKinematic = true;
            body.transform.position = vehicleTransform.TransformPoint(item.CargoFollowLocalPos);
            body.transform.rotation = vehicleTransform.rotation * item.CargoFollowLocalRot;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            item.LastPosition = body.transform.position;
        }

        private bool ShouldFollowVehicleCargo(SyncedItem item, SyncedItem vehicle, float now)
        {
            if (item.IsVehicle || IsPlayerHeldItem(item) || vehicle.Body == null) return false;
            if (vehicle.RemoteOwner == WorldSyncIds.NoOwner) return false;
            if (!ItemTransformPolicy.IsRemoteStreamLive(
                    vehicle.LastRemoteAt, now, vehicle.RemoteIsDriver, vehicle.RemoteVehicleStream))
                return false;

            return IsVehicleInMotion(vehicle, now);
        }

        private bool IsVehicleInMotion(SyncedItem vehicle, float now)
        {
            if (vehicle.Body == null) return false;
            if (vehicle.LocalDriveActive) return true;
            return now - vehicle.LastMovedAt < vehicle.StillSeconds;
        }

        private bool TryGetContainingVehicle(SyncedItem item, out SyncedItem? vehicle)
        {
            vehicle = null;
            if (item.IsVehicle || item.Body == null) return false;

            float radiusSqr = VehicleInteriorRadius * VehicleInteriorRadius;
            Vector3 position = item.Body.transform.position;
            float bestSqr = float.MaxValue;

            foreach (var candidate in _items.Values)
            {
                if (!candidate.IsVehicle || candidate.Body == null) continue;

                float sqr = (position - candidate.Body.transform.position).sqrMagnitude;
                if (sqr > radiusSqr || sqr >= bestSqr) continue;

                bestSqr = sqr;
                vehicle = candidate;
            }

            return vehicle != null;
        }

        private bool IsPlayerHeldItem(SyncedItem item)
        {
            if (item.Body == null) return false;

            _bridge.FindLocalPlayer();
            return _bridge.LocalPlayer != null && item.Body.transform.IsChildOf(_bridge.LocalPlayer);
        }

        private static Transform GetVehicleSceneRoot(Transform bodyTransform)
        {
            Transform root = bodyTransform;
            while (root.parent != null)
                root = root.parent;
            return root;
        }

        private static float GetRemoteHoldSeconds(SyncedItem item) =>
            ItemTransformPolicy.GetRemoteHoldSeconds(item.RemoteIsDriver, item.RemoteVehicleStream);

        private bool IsLocalVehicleOperator(SyncedItem item)
        {
            if (!item.IsVehicle) return false;
            if (item.LocalDriveActive) return true;
            return IsLocalPlayerDriving(item);
        }

        internal bool IsLocalPlayerDriving(SyncedItem item) => IsLocalPlayerDriving(item.Body, item);

        internal bool IsLocalPlayerDrivingAny()
        {
            _bridge.FindLocalPlayer();
            if (_bridge.LocalPlayer == null) return false;

            foreach (var item in _items.Values)
            {
                if (item.IsVehicle && IsLocalPlayerDriving(item))
                    return true;
            }

            return false;
        }

        internal bool IsLocalPlayerDriving(Rigidbody vehicleBody, SyncedItem? item = null)
        {
            if (PassengerController.Instance != null && PassengerController.Instance.IsLocalSeated)
                return false;

            _bridge.FindLocalPlayer();
            if (_bridge.LocalPlayer == null || vehicleBody == null) return false;

            if (item?.PlayerInVar != null && item.PlayerInVar.Value)
                return true;

            Transform vehicleRoot = GetVehicleSceneRoot(vehicleBody.transform);
            if (_bridge.LocalPlayer.IsChildOf(vehicleRoot))
                return true;

            if (_bridge.LocalPlayer.IsChildOf(vehicleBody.transform))
                return true;

            // Enter-seat race: hierarchy may lag one frame; MassDriver is the
            // in-cabin physics anchor the game uses while driving.
            if (item != null)
            {
                EnsureSeat(item);
                if (item.DriverAnchorTransform != null)
                {
                    float distSq = (_bridge.LocalPlayer.position - item.DriverAnchorTransform.position).sqrMagnitude;
                    if (distSq <= 2.25f)
                        return true;
                }
            }

            return false;
        }

        private static bool LocalDriverOutClaimsRemote(SessionManager session, SyncedItem item, bool remoteDriven)
        {
            return ItemTransformPolicy.ShouldSeatDriverOutClaimRemote(
                remoteDriven,
                item.RemoteIsDriver,
                item.RemoteVehicleStream,
                item.LastRemoteAt,
                Time.unscaledTime,
                session.LocalPlayerId,
                item.RemoteOwner);
        }

        // ------------------------------------------------------------------ seats

        /// <summary>
        /// Vehicles carry a "DriveTrigger"/"DriveTriggerX" child whose collider is
        /// the get-in interaction. Found lazily, once per vehicle.
        /// </summary>
        private static void EnsureSeat(SyncedItem item)
        {
            if (item.SeatSearched || item.Body == null) return;
            item.SeatSearched = true;

            foreach (var transform in item.Body.GetComponentsInChildren<Transform>(true))
            {
                string name = transform.name;
                if (item.SeatTransform == null && name.StartsWith("DriveTrigger", StringComparison.Ordinal))
                {
                    item.SeatTransform = transform;
                    item.SeatCollider = transform.GetComponent<Collider>();
                }

                if (item.DriverAnchorTransform == null && name == "MassDriver")
                    item.DriverAnchorTransform = transform;
            }
        }

        /// <summary>
        /// Disable the get-in trigger while a remote driver occupies the vehicle —
        /// otherwise the seat looks free here (remote avatars are visual only) and
        /// a second player could "enter" an already-driven car.
        /// </summary>
        private static void SetSeatBlocked(SyncedItem item, bool blocked)
        {
            if (!item.IsVehicle || item.SeatBlocked == blocked) return;
            EnsureSeat(item);
            item.SeatBlocked = blocked;
            if (item.SeatCollider != null)
                item.SeatCollider.enabled = !blocked;
        }

        /// <summary>Registered vehicle handle for the passenger-seat layer.</summary>
        // VehicleInfo moved to WorldSyncTypes
        public void CollectVehicles(List<WorldSyncManager.VehicleInfo> results)
        {
            results.Clear();
            foreach (var pair in _items)
            {
                if (pair.Value.IsVehicle && pair.Value.Body != null)
                    results.Add(new WorldSyncManager.VehicleInfo { Id = pair.Key, Body = pair.Value.Body });
            }
        }

        /// <summary>
        /// The vehicle (and its seat) currently driven by the given remote player,
        /// if any — PlayerSync anchors that player's avatar to it so the body rides
        /// in the cabin instead of trailing the smoothed world stream.
        /// </summary>
        public bool TryGetDriverAnchor(byte playerId, out Transform? seat, out Transform? vehicle)
        {
            float now = Time.unscaledTime;
            foreach (var item in _items.Values)
            {
                if (!item.IsVehicle || item.RemoteOwner != playerId) continue;
                if (item.Body == null
                    || !ItemTransformPolicy.IsRemoteStreamLive(
                        item.LastRemoteAt, now, item.RemoteIsDriver, item.RemoteVehicleStream))
                    continue;
                // Only actual drivers ride the cabin — players pushing the car
                // (FlagVehicle stream) stay on their own world-space pose.
                if (!item.RemoteIsDriver) continue;

                EnsureSeat(item);
                vehicle = item.Body.transform;
                seat = item.SeatTransform != null ? item.SeatTransform : vehicle;
                return true;
            }

            seat = null;
            vehicle = null;
            return false;
        }

        private void SendItem(SessionManager session, SyncedItem item, Rigidbody body, bool final)
        {
            bool isVehicle = item.IsVehicle;
            bool isDriver = isVehicle && IsLocalVehicleOperator(item);
            byte flags = 0;
            if (final) flags |= ItemTransform.FlagFinal;
            if (isDriver) flags |= ItemTransform.FlagDriver;
            if (isVehicle && !final) flags |= ItemTransform.FlagVehicle;

            var message = new ItemTransform
            {
                ItemId = item.Id,
                OwnerPlayerId = session.LocalPlayerId,
                Sequence = ++item.OutSequence,
                Flags = flags,
                Position = body.transform.position.ToNet(),
                Rotation = body.transform.rotation.ToNet(),
            };

            session.SendWorldMessage(message, ItemTransformPolicy.SelectSendChannel(final, isVehicle));

            if (final && isVehicle)
                item.LocalDriveActive = false;
        }


    }
}
