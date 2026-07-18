using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Cargo pose streaming (protocol v27). The machine streaming a vehicle lets the
    /// game's own physics simulate the items riding it — sliding, rolling, tumbling —
    /// and broadcasts their vehicle-local poses in <see cref="VehicleCargo"/> packets.
    /// Observers pin listed items kinematically and compose the streamed local pose
    /// against their own smoothed copy of the vehicle, so cargo tracks the car with no
    /// world-space lag and replays the authority's in-car physics one-way (pinned items
    /// can't shove the car back). This replaces the old kinematic weld, which froze
    /// cargo dead and disabled its colliders.
    /// </summary>
    internal sealed partial class ItemWorldSync
    {
        /// <summary>Host gate: only the current vehicle transform owner may stream its cargo set.</summary>
        public bool TryAcceptGuestVehicleCargo(VehicleCargo message, byte playerId)
        {
            return message.OwnerPlayerId == playerId
                && _items.TryGetValue(message.VehicleId, out var vehicle)
                && vehicle.IsVehicle
                && vehicle.Body != null
                && vehicle.RemoteOwner == playerId;
        }

        /// <summary>Local-space smoothing for pinned cargo: snappier than the world-space
        /// item lerp because the vehicle frame already absorbs the big motion — the local
        /// residual is just the item shifting around the cabin.</summary>
        private const float CargoLerpSpeed = 14f;
        private const float CargoSnapDistanceSqr = 3f * 3f;

        /// <summary>Cap-overflow demotions sit out membership this long so they don't
        /// flap between cargo entry and world-space claim every frame.</summary>
        private const float CargoDemoteBlockSeconds = 2f;

        /// <summary>Depenetration clamp for cargo members. PhysX's default response to a
        /// small item squeezed between the seat/floor colliders of a bouncing car is a
        /// violent separating impulse — straight through the thin floor. Clamped, the
        /// overlap resolves as a gentle push instead.</summary>
        private const float CargoMaxDepenetrationVelocity = 2.5f;

        private struct CargoCandidate
        {
            public SyncedItem Item;
            public float DistanceSqr;
        }

        private static readonly System.Comparison<CargoCandidate> CargoByDistance =
            (a, b) => a.DistanceSqr.CompareTo(b.DistanceSqr);

        private readonly Dictionary<uint, List<CargoCandidate>> _cargoCandidates =
            new Dictionary<uint, List<CargoCandidate>>();

        private readonly List<SyncedItem> _activeCargoVehicles = new List<SyncedItem>();

        // ---- authority side: our physics simulates the cargo, we broadcast its poses ----

        /// <summary>Snapshot of the vehicles we stream that are in motion this frame, so
        /// the per-item membership scan touches a handful of entries (usually zero or
        /// one) instead of the whole item table.</summary>
        private void CollectActiveCargoVehicles(float now)
        {
            _activeCargoVehicles.Clear();
            foreach (var candidate in _items.Values)
            {
                if (!candidate.IsVehicle || candidate.Body == null) continue;
                if (!candidate.LocallyOwned) continue;
                if (!IsVehicleInMotion(candidate, now)) continue;
                _activeCargoVehicles.Add(candidate);
            }
        }

        /// <summary>
        /// The locally-streamed, in-motion vehicle this item should ride as cargo, or
        /// null. Hysteresis keeps membership stable at the radius edge. Items pinned by
        /// someone else's cargo stream, held in the local player's hands, or carried by
        /// a live remote item stream (a passenger holding them up) are never ours to
        /// stream.
        /// </summary>
        private SyncedItem? FindLocalCargoVehicle(SyncedItem item, float now, out float distanceSqr)
        {
            distanceSqr = float.MaxValue;
            if (_activeCargoVehicles.Count == 0) return null;
            if (now < item.LocalCargoBlockedUntil) return null;
            if (item.RemoteCargoVehicleId != 0) return null;
            if (item.RemoteOwner != WorldSyncIds.NoOwner
                && ItemTransformPolicy.IsRemoteStreamLive(
                    item.LastRemoteAt, now, item.RemoteIsDriver, item.RemoteVehicleStream))
                return null;
            if (IsPlayerHeldItem(item)) return null;

            Vector3 position = item.Body.transform.position;
            SyncedItem? best = null;
            for (int i = 0; i < _activeCargoVehicles.Count; i++)
            {
                var candidate = _activeCargoVehicles[i];
                if (candidate.Body == null) continue;

                // Anchor on the BODY, not the scene root: MWC vehicle roots are static
                // containers that do not follow the car (the rigidbody child moves), so
                // a root anchor put every riding item permanently out of radius.
                float dSqr = (position - candidate.Body.transform.position).sqrMagnitude;
                bool wasMemberOfThis = item.LocalCargoVehicleId == candidate.Id;
                if (!ItemTransformPolicy.ShouldBeCargoMember(wasMemberOfThis, dSqr)) continue;
                if (dSqr >= distanceSqr) continue;

                distanceSqr = dSqr;
                best = candidate;
            }

            return best;
        }

        private void EnterLocalCargo(SyncedItem item, SyncedItem vehicle, Rigidbody body, float distanceSqr)
        {
            // Taking cargo authority: the item must be under real local physics. A stale
            // remote stream may have left it kinematic-frozen; give it its body back.
            if (item.RemoteOwner != WorldSyncIds.NoOwner)
            {
                if (item.KinematicSaved)
                    body.isKinematic = item.OriginalKinematic;
                item.RemoteOwner = WorldSyncIds.NoOwner;
                item.RemoteIsDriver = false;
                item.RemoteVehicleStream = false;
                item.LastRemoteAt = -999f;
            }

            // The vehicle-local stream supersedes any world-space stream of ours — no
            // final needed: observers switch over when the first cargo packet lands.
            item.LocallyOwned = false;
            item.LocalCargoVehicleId = vehicle.Id;
            ApplyCargoPhysics(item, body, CollisionDetectionMode.ContinuousDynamic);

            List<CargoCandidate> list;
            if (!_cargoCandidates.TryGetValue(vehicle.Id, out list))
            {
                list = new List<CargoCandidate>();
                _cargoCandidates[vehicle.Id] = list;
            }

            list.Add(new CargoCandidate { Item = item, DistanceSqr = distanceSqr });
        }

        /// <summary>
        /// An item left our cargo set (car parked, item flew out of range, someone
        /// grabbed it, cap overflow). Ownership continues seamlessly: a still-moving
        /// item becomes an ordinary locally-owned world stream (its rest final follows
        /// once it settles); a resting one settles everywhere with one reliable final.
        /// </summary>
        private void HandleLocalCargoExit(SessionManager session, SyncedItem item, Rigidbody body, float now, bool demoted)
        {
            item.LocalCargoVehicleId = 0;
            RestoreCargoPhysics(item);
            if (demoted)
                item.LocalCargoBlockedUntil = now + CargoDemoteBlockSeconds;

            if (item.RemoteOwner != WorldSyncIds.NoOwner
                && ItemTransformPolicy.IsRemoteStreamLive(
                    item.LastRemoteAt, now, item.RemoteIsDriver, item.RemoteVehicleStream))
                return; // another player took it (grabbed out of the moving car) — theirs now

            bool moving = now - item.LastMovedAt < item.StillSeconds
                || (!body.isKinematic && body.velocity.sqrMagnitude > RestVelocitySqr);
            if (moving)
            {
                ClaimItem(session, item, body, now);
            }
            else
            {
                SendItem(session, item, body, true);
                item.LocallyOwned = false;
            }
        }

        /// <summary>Runs after the main item pass: caps each vehicle's set (demoting the
        /// farthest extras), sends due cargo packets, and reliably announces the
        /// non-empty -> empty transition (an empty set releases every pin, so observers
        /// must not miss it).</summary>
        private void SendLocalCargo(SessionManager session, float now)
        {
            foreach (var pair in _items)
            {
                var vehicle = pair.Value;
                if (!vehicle.IsVehicle) continue;

                List<CargoCandidate>? list = null;
                bool hasCargo = _cargoCandidates.TryGetValue(pair.Key, out list) && list!.Count > 0;

                if (!hasCargo)
                {
                    if (vehicle.LocalCargoWasStreaming)
                    {
                        vehicle.LocalCargoWasStreaming = false;
                        vehicle.NextCargoSendAt = 0f;
                        RestoreCargoPhysics(vehicle);
                        WinterMPPlugin.Log.LogInfo($"WorldSync: cargo stream ended for '{vehicle.Path}'.");
                        session.SendWorldMessage(new VehicleCargo
                        {
                            VehicleId = pair.Key,
                            OwnerPlayerId = session.LocalPlayerId,
                            Sequence = ++vehicle.OutCargoSequence,
                        }, Channel.ReliableOrdered);
                    }

                    continue;
                }

                if (vehicle.Body == null) continue;

                if (list!.Count > VehicleCargo.MaxEntries)
                {
                    list.Sort(CargoByDistance);
                    for (int i = VehicleCargo.MaxEntries; i < list.Count; i++)
                    {
                        var extra = list[i].Item;
                        if (extra.Body != null)
                            HandleLocalCargoExit(session, extra, extra.Body, now, demoted: true);
                    }

                    list.RemoveRange(VehicleCargo.MaxEntries, list.Count - VehicleCargo.MaxEntries);
                }

                if (!vehicle.LocalCargoWasStreaming)
                {
                    vehicle.LocalCargoWasStreaming = true;
                    ApplyCargoPhysics(vehicle, vehicle.Body, CollisionDetectionMode.Continuous);
                    WinterMPPlugin.Log.LogInfo(
                        $"WorldSync: cargo stream started for '{vehicle.Path}' ({list.Count} items).");
                }

                if (now < vehicle.NextCargoSendAt) continue;
                vehicle.NextCargoSendAt = now + 1f / vehicle.SendRateHz;

                var vehicleTransform = vehicle.Body.transform;
                var inverseRotation = Quaternion.Inverse(vehicleTransform.rotation);
                var entries = new VehicleCargo.Entry[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    var body = list[i].Item.Body;
                    entries[i] = new VehicleCargo.Entry
                    {
                        ItemId = list[i].Item.Id,
                        LocalPosition = vehicleTransform.InverseTransformPoint(body.transform.position).ToNet(),
                        LocalRotation = (inverseRotation * body.transform.rotation).ToNet(),
                    };
                }

                session.SendWorldMessage(new VehicleCargo
                {
                    VehicleId = pair.Key,
                    OwnerPlayerId = session.LocalPlayerId,
                    Sequence = ++vehicle.OutCargoSequence,
                    Entries = entries,
                }, Channel.UnreliableSequenced);
            }

            foreach (var pair in _cargoCandidates)
                pair.Value.Clear();
        }

        // ---- observer side: pin listed items, compose against our smoothed vehicle ----

        public void OnRemoteVehicleCargo(VehicleCargo message)
        {
            if (!_items.TryGetValue(message.VehicleId, out var vehicle)
                || !vehicle.IsVehicle || vehicle.Body == null)
                return;

            // We stream this vehicle ourselves — our physics owns its cargo. (Handoff
            // race: the packet was in flight while we out-claimed the car.)
            if (vehicle.LocallyOwned || IsLocalVehicleOperator(vehicle)) return;

            if (message.OwnerPlayerId == vehicle.LastRemoteCargoOwner
                && ItemTransformPolicy.IsStaleSequence(vehicle.LastRemoteCargoSequence, message.Sequence))
                return;
            vehicle.LastRemoteCargoOwner = message.OwnerPlayerId;
            vehicle.LastRemoteCargoSequence = message.Sequence;

            float now = Time.unscaledTime;
            var vehicleTransform = vehicle.Body.transform;
            var entries = message.Entries;

            for (int i = 0; i < entries.Length; i++)
            {
                if (!_items.TryGetValue(entries[i].ItemId, out var item)
                    || item.IsVehicle || item.Body == null)
                    continue;

                var localPos = entries[i].LocalPosition.ToUnity();
                var localRot = entries[i].LocalRotation.ToUnity();
                if (!IsFinite(localPos) || !TryNormalize(localRot, out localRot)) continue;

                // Hands beat floor physics: an item the local player holds (grabbed off
                // the moving car) is never pinned — the authority drops it from its set
                // once our claim stream reaches it.
                if (IsPlayerHeldItem(item)) continue;

                // Two moving cars side by side: if the item rides OUR streamed vehicle,
                // our cargo stream is its authority, not theirs.
                if (item.LocalCargoVehicleId != 0) continue;

                // A live world stream from a third player (say, a passenger holding the
                // item up) outranks the pin — mirror of the sender-side membership filter.
                if (item.RemoteOwner != WorldSyncIds.NoOwner
                    && item.RemoteOwner != message.OwnerPlayerId
                    && ItemTransformPolicy.IsRemoteStreamLive(
                        item.LastRemoteAt, now, item.RemoteIsDriver, item.RemoteVehicleStream))
                    continue;

                ApplyRemoteCargoPin(item, vehicle, vehicleTransform, message.OwnerPlayerId, localPos, localRot, now);
            }

            // Complete-set semantics: a pinned item missing from this packet was released
            // by the authority (flung out, grabbed, settled, cap-demoted).
            int pinned = 0;
            foreach (var other in _items.Values)
            {
                if (other.RemoteCargoVehicleId != message.VehicleId) continue;

                bool listed = false;
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].ItemId == other.Id)
                    {
                        listed = true;
                        break;
                    }
                }

                if (listed)
                    pinned++;
                else
                    ReleaseRemoteCargo(other, other.Body, now, seedVelocity: true);
            }

            if (pinned > 0 && !vehicle.RemoteCargoAnnounced)
            {
                vehicle.RemoteCargoAnnounced = true;
                WinterMPPlugin.Log.LogInfo(
                    $"WorldSync: riding cargo from player {message.OwnerPlayerId} — {pinned} items on '{vehicle.Path}'.");
            }
            else if (pinned == 0 && vehicle.RemoteCargoAnnounced)
            {
                vehicle.RemoteCargoAnnounced = false;
                WinterMPPlugin.Log.LogInfo($"WorldSync: cargo released on '{vehicle.Path}'.");
            }
        }

        private void ApplyRemoteCargoPin(SyncedItem item, SyncedItem vehicle, Transform vehicleTransform,
            byte ownerPlayerId, Vector3 localPos, Quaternion localRot, float now)
        {
            var body = item.Body;
            bool continuing = item.RemoteCargoVehicleId == vehicle.Id
                && item.RemoteCargoSmoothingInit
                && ItemTransformPolicy.IsCargoStreamFresh(item.RemoteCargoAt, now);

            if (!item.KinematicSaved)
            {
                item.OriginalKinematic = body.isKinematic;
                item.KinematicSaved = true;
            }

            if (!continuing)
            {
                // First pin (or re-pin after a gap): start smoothing from wherever the
                // item is right now, expressed in the vehicle's frame, so it glides to
                // the streamed pose instead of teleporting.
                item.RemoteCargoPos = vehicleTransform.InverseTransformPoint(body.transform.position);
                item.RemoteCargoRot = Quaternion.Inverse(vehicleTransform.rotation) * body.transform.rotation;
                item.RemoteCargoSmoothingInit = true;
                item.RemoteCargoWorldAt = -999f;
                item.RemoteCargoObservedVelocity = Vector3.zero;
            }

            item.RemoteCargoVehicleId = vehicle.Id;
            item.RemoteCargoOwner = ownerPlayerId;
            item.RemoteCargoAt = now;
            item.RemoteCargoTargetPos = localPos;
            item.RemoteCargoTargetRot = localRot;

            // The pin owns the item: scrub per-item stream state so the stale-release
            // path can't yank kinematics from under the compose pass, and drop any local
            // claim (the vehicle authority's physics carries it now).
            item.LocallyOwned = false;
            item.RemoteOwner = WorldSyncIds.NoOwner;
            item.RemoteIsDriver = false;
            item.RemoteVehicleStream = false;
            item.LastRemoteAt = -999f;
            item.HasRemoteVelocity = false;

            body.isKinematic = true;
        }

        /// <summary>
        /// Per-frame keeper for a pinned item. True = still pinned (the compose pass
        /// will place it); false = released to local physics this frame.
        /// </summary>
        private bool HandleRemoteCargoState(SyncedItem item, Rigidbody body, float now)
        {
            if (IsPlayerHeldItem(item))
            {
                ReleaseRemoteCargo(item, body, now, seedVelocity: false);
                return false;
            }

            if (ItemTransformPolicy.IsCargoStreamFresh(item.RemoteCargoAt, now))
                return true;

            // Past the hold. If the same owner is still driving the vehicle the gap is
            // packet loss — keep riding at the last streamed local pose rather than
            // dropping to physics inside a moving car. An owner change or a dead
            // vehicle stream releases for real.
            if (_items.TryGetValue(item.RemoteCargoVehicleId, out var vehicle)
                && vehicle.Body != null
                && vehicle.RemoteOwner == item.RemoteCargoOwner
                && ItemTransformPolicy.IsRemoteStreamLive(
                    vehicle.LastRemoteAt, now, vehicle.RemoteIsDriver, vehicle.RemoteVehicleStream))
                return true;

            ReleaseRemoteCargo(item, body, now, seedVelocity: true);
            return false;
        }

        private void ReleaseRemoteCargo(SyncedItem item, Rigidbody? body, float now, bool seedVelocity)
        {
            if (item.RemoteCargoVehicleId == 0) return;

            Vector3 seed = Vector3.zero;
            if (seedVelocity)
            {
                // The pin's own observed motion beats the car's velocity: a roadside
                // item the car merely drove past (enter-radius physics shield) sat
                // still while pinned and must stay still on release — seeding it with
                // the car's velocity launched it down the road. A genuine rider's
                // composed pose tracks the car, so its sample ≈ the car's velocity
                // (plus any slide), which is exactly the motion to inherit.
                if (ItemTransformPolicy.IsObservedCargoVelocityFresh(item.RemoteCargoWorldAt, now))
                {
                    seed = Vector3.ClampMagnitude(
                        item.RemoteCargoObservedVelocity, ItemTransformPolicy.CargoReleaseSeedMaxSpeed);
                }
                else if (_items.TryGetValue(item.RemoteCargoVehicleId, out var vehicle)
                    && vehicle.HasRemoteVelocity
                    && vehicle.RemoteOwner != WorldSyncIds.NoOwner
                    && ItemTransformPolicy.IsRemoteStreamLive(
                        vehicle.LastRemoteAt, now, vehicle.RemoteIsDriver, vehicle.RemoteVehicleStream))
                {
                    seed = vehicle.RemoteVelocity;
                }
            }

            item.RemoteCargoVehicleId = 0;
            item.RemoteCargoOwner = WorldSyncIds.NoOwner;
            item.RemoteCargoAt = -999f;
            item.RemoteCargoSmoothingInit = false;
            item.RemoteCargoWorldAt = -999f;
            item.RemoteCargoObservedVelocity = Vector3.zero;

            if (body == null) return;
            if (item.KinematicSaved)
                body.isKinematic = item.OriginalKinematic;
            if (!body.isKinematic)
            {
                // Inherit the car's motion: a mid-drive release (lag spike, cap
                // eviction, item thrown off the flatbed) must not dead-stop the item
                // in the car's path.
                body.velocity = seed;
                body.angularVelocity = Vector3.zero;
            }

            item.LastPosition = body.transform.position;
        }

        private void ReleaseRemoteCargoForVehicle(uint vehicleId, float now, bool seedVelocity)
        {
            foreach (var other in _items.Values)
            {
                if (other.RemoteCargoVehicleId == vehicleId)
                    ReleaseRemoteCargo(other, other.Body, now, seedVelocity);
            }
        }

        /// <summary>
        /// Runs after the main item pass so every remote vehicle has already been
        /// smoothed this frame — composing against a pre-smoothing pose would trail
        /// the car by a frame for half the items (dictionary order) and shimmer.
        /// </summary>
        private void ComposeRemoteCargo(float now)
        {
            foreach (var item in _items.Values)
            {
                if (item.RemoteCargoVehicleId == 0 || item.IsVehicle) continue;
                var body = item.Body;
                if (body == null) continue;
                if (!_items.TryGetValue(item.RemoteCargoVehicleId, out var vehicle) || vehicle.Body == null)
                    continue;

                float t = 1f - Mathf.Exp(-CargoLerpSpeed * Time.deltaTime);
                if ((item.RemoteCargoPos - item.RemoteCargoTargetPos).sqrMagnitude > CargoSnapDistanceSqr)
                {
                    item.RemoteCargoPos = item.RemoteCargoTargetPos;
                    item.RemoteCargoRot = item.RemoteCargoTargetRot;
                    item.RemoteCargoWorldAt = -999f; // a teleport must not sample as velocity
                }
                else
                {
                    item.RemoteCargoPos = Vector3.Lerp(item.RemoteCargoPos, item.RemoteCargoTargetPos, t);
                    item.RemoteCargoRot = Quaternion.Slerp(item.RemoteCargoRot, item.RemoteCargoTargetRot, t);
                }

                if (!body.isKinematic)
                    body.isKinematic = true; // game code (pickup scripts) may flip it back

                var vehicleTransform = vehicle.Body.transform;
                Vector3 composedPos = vehicleTransform.TransformPoint(item.RemoteCargoPos);
                body.transform.position = composedPos;
                body.transform.rotation = vehicleTransform.rotation * item.RemoteCargoRot;

                // Sample the pin's apparent world motion for the release seed (see
                // ReleaseRemoteCargo): riders read ~the car's velocity, shielded
                // roadside items read ~zero.
                float sampleDt = now - item.RemoteCargoWorldAt;
                if (sampleDt > 0f
                    && ItemTransformPolicy.IsObservedCargoVelocityFresh(item.RemoteCargoWorldAt, now))
                {
                    item.RemoteCargoObservedVelocity = (composedPos - item.RemoteCargoWorldPos) / sampleDt;
                }
                item.RemoteCargoWorldPos = composedPos;
                item.RemoteCargoWorldAt = now;

                // Riding counts as motion for the bookkeeping heuristics; the pin itself
                // blocks claims via ShouldBlockClaimForVehicleCargo.
                item.LastPosition = composedPos;
                item.LastMovedAt = now;
            }
        }

        // ---- shared helpers ----

        /// <summary>
        /// Cargo tunneling guard (authority side). Members ride under real game physics
        /// with discrete collision detection by default — at driving speed a small item
        /// can pass the thin floor colliders between two steps, or get popped through
        /// them by a depenetration impulse when the suspension bounces. Members sweep
        /// (ContinuousDynamic) with a clamped depenetration response; the streaming
        /// vehicle goes Continuous, which is what makes member sweeps test against its
        /// colliders (ContinuousDynamic alone only sweeps against static geometry).
        /// </summary>
        private static void ApplyCargoPhysics(SyncedItem entry, Rigidbody body, CollisionDetectionMode mode)
        {
            if (entry.CargoPhysicsSaved) return;
            entry.CargoPhysicsSaved = true;
            entry.CargoSavedDetectionMode = body.collisionDetectionMode;
            entry.CargoSavedMaxDepenetration = body.maxDepenetrationVelocity;
            body.collisionDetectionMode = mode;
            if (mode == CollisionDetectionMode.ContinuousDynamic)
                body.maxDepenetrationVelocity = CargoMaxDepenetrationVelocity;
        }

        private static void RestoreCargoPhysics(SyncedItem entry)
        {
            if (!entry.CargoPhysicsSaved) return;
            entry.CargoPhysicsSaved = false;
            var body = entry.Body;
            if (body == null) return;
            body.collisionDetectionMode = entry.CargoSavedDetectionMode;
            body.maxDepenetrationVelocity = entry.CargoSavedMaxDepenetration;
        }

        private bool IsVehicleInMotion(SyncedItem vehicle, float now)
        {
            if (vehicle.Body == null) return false;
            if (vehicle.LocalDriveActive) return true;
            return now - vehicle.LastMovedAt < vehicle.StillSeconds;
        }

        private bool IsPlayerHeldItem(SyncedItem item)
        {
            if (item.Body == null) return false;

            _bridge.FindLocalPlayer();
            return _bridge.LocalPlayer != null && item.Body.transform.IsChildOf(_bridge.LocalPlayer);
        }
    }
}
