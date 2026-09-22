using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Three passenger seats for the Sorbet/Corris; two for the taxi, with its
    /// rear-right seat reserved for the native fare customer.
    ///
    /// The game has no passenger mechanic for these cars, so this is hand-rolled
    /// (the proven MSC-multiplayer approach): crouch on a free seat cushion and
    /// pressing Return parents PLAYER under the vehicle at the seat position with
    /// its CharacterController disabled; Return again gets out. Entry mirrors drive
    /// mode (Return at the in-cabin seat, not at the exterior door): front seats use
    /// MassPassenger, rear bench spots use the game's bench/rear-seat pivots.
    ///
    /// Occupancy is replicated via <see cref="PassengerState"/> (re-broadcast
    /// while seated so late joiners learn it): occupied seats refuse local entry,
    /// remote passengers' avatars are pinned into the cabin, and simultaneous
    /// entry races are resolved by lowest player id (the loser is re-ejected).
    /// </summary>
    public sealed partial class PassengerController : MonoBehaviour
    {
        /// <summary>
        /// Horizontal reach of the "sit down" prompt, measured on the car's floor
        /// plane (not a 3D sphere). The seat anchors live at cushion height inside
        /// the cabin while the PLAYER pivot is on the ground outside the door, so a
        /// tight 3D sphere never overlapped the seat — the feet-to-cushion height
        /// gap alone exceeded it, which is why riding shotgun was impossible. Entry
        /// parents the player onto the seat, so reaching it from beside the adjacent
        /// door is enough; we don't need them physically on the cushion.
        /// </summary>
        private const float EnterRadius = 1.0f;
        /// <summary>Vertical slack between the ground-level player pivot and the
        /// cushion-height seat anchor; wide enough to always clear that gap, tight
        /// enough to keep the prompt off cars parked on a level above or below.</summary>
        private const float EnterVerticalTolerance = 1.3f;
        private const float KeyCooldownSeconds = 0.7f;
        private const float RebroadcastSeconds = 8f;
        private const float VehicleScanIntervalSeconds = 3f;
        private const float PlayerSearchIntervalSeconds = 2f;
        /// <summary>Host-side maximum age for a guest pose used to validate a seat claim.</summary>
        private const float GuestPoseFreshSeconds = 2f;
        /// <summary>Guest feet are below the seat cushion; this covers a real local entry
        /// while rejecting map-wide vehicle/seat claims.</summary>
        private const float GuestSeatClaimRadius = 2.0f;
        /// <summary>Where a seated body sits relative to the cushion anchor — used for the
        /// REMOTE avatar and the exit spot. Small raise so the body rests on the cushion.</summary>
        private const float SeatBodyRaise = 0.1f;
        /// <summary>How far BELOW the cushion anchor to park the LOCAL PLAYER pivot. The pivot
        /// carries the first-person camera at standing eye height, so parking it at the
        /// cushion floats the view up near the roof — drop it so the camera lands at seated
        /// eye level. Single knob: raise if the view clips the floor, lower if it still floats.</summary>
        private const float LocalCameraDrop = 0.45f;
        /// <summary>Forward nudge (car-local +z, toward the windshield) for the FRONT
        /// passenger seat only. The raw MassPassenger point sits a touch too far back, so
        /// the seated body — and the passenger's own camera — read as sunk into the seat
        /// back and drift past the cabin edge. Applied to SeatLocal[0], so it moves the
        /// remote avatar, the first-person camera and entry detection together. The rear
        /// bench derives its own fore/aft from the bench pivot and is untouched.</summary>
        private const float FrontSeatForwardOffset = 0.15f;
        private const float RearBenchHalfWidth = 0.35f;
        private const string PlayerObjectName = "PLAYER";
        /// <summary>The game's interaction indicator — the on-screen TextMesh the
        /// drive/use prompts write to. Driving its SetText FSM makes the passenger
        /// prompt render exactly like the game's own get-in-car prompt.</summary>
        private const string InteractionObjectPath = "GUI/Indicators/Interaction";
        private const string InteractionFsmName = "SetText";
        private const float InteractionSearchIntervalSeconds = 2f;

        public static PassengerController? Instance { get; private set; }

        public bool IsLocalSeated => _seated;

        public bool IsLocalSeatedInVehicle(uint vehicleId) =>
            _seated && _seatedVehicleId == vehicleId;

        internal bool TryGetLocalPassengerVehicle(out uint vehicleId)
        {
            vehicleId = _seatedVehicleId;
            return !_disabled && _seated && vehicleId != 0 && _seatedIndex >= 0 && _seatedIndex < 3;
        }

        internal bool HasRemotePassengerInVehicle(uint vehicleId)
        {
            var session = SessionManager.Instance;
            if (_disabled || vehicleId == 0 || session == null || session.State != SessionState.Connected || session.IsHost) return false;
            foreach (var player in session.Players)
                if (_remoteSeats.TryGetValue(player.PlayerId, out var seat)
                    && seat.VehicleId == vehicleId && seat.Seat < 3) return true;
            return false;
        }

        // Caller selects a present player from the connected session roster.
        internal bool IsRemotePassengerInVehicle(byte playerId, uint vehicleId)
        {
            var session = SessionManager.Instance;
            return !_disabled && vehicleId != 0 && session != null && !session.IsHost && session.State == SessionState.Connected
                && _remoteSeats.TryGetValue(playerId, out var seat) && seat.VehicleId == vehicleId && seat.Seat < 3;
        }

        private struct SeatRef
        {
            public uint VehicleId;
            public byte Seat;
        }

        private sealed class VehicleSeats
        {
            public uint VehicleId;
            public Rigidbody Body = null!;
            public Transform? DriveTrigger;
            public Vector3[] SeatLocal = new Vector3[3];
            public GameObject?[] RemoteAnchors = new GameObject?[3];
            public byte AvailableSeats = WinterMP.Net.Sync.PassengerSeatPolicy.AllSeats;
            public Transform? UnavailableWhileActive;
        }

        private readonly Dictionary<uint, VehicleSeats> _vehicles = new Dictionary<uint, VehicleSeats>();
        private readonly Dictionary<byte, SeatRef> _remoteSeats = new Dictionary<byte, SeatRef>();
        private readonly Dictionary<byte, ushort> _remoteSeatSequences = new Dictionary<byte, ushort>();

        /// <summary>A reused player slot must not inherit its old seat or claim counter.</summary>
        public void ForgetPlayer(byte playerId)
        {
            _remoteSeatSequences.Remove(playerId);
            if (!_remoteSeats.TryGetValue(playerId, out var seat)) return;
            _remoteSeats.Remove(playerId);
            DestroyAnchor(seat);
        }
        private readonly List<WorldSyncManager.VehicleInfo> _vehicleScratch = new List<WorldSyncManager.VehicleInfo>();
        private readonly HashSet<uint> _liveVehicleIds = new HashSet<uint>();
        private readonly List<uint> _retiredVehicleIds = new List<uint>();
        private readonly List<byte> _purgeScratch = new List<byte>();

        private Transform? _player;
        private CharacterController? _playerController;
        private float _nextPlayerSearchAt;
        private float _nextVehicleScanAt;
        private string _lastLevel = string.Empty;

        // Local seat state.
        private bool _seated;
        private uint _seatedVehicleId;
        private int _seatedIndex;
        private Transform? _originalParent;
        private Transform? _seatedParent;
        private bool _controllerWasEnabled;
        private float _nextKeyAt;
        private float _nextRebroadcastAt;
        private ushort _seatSequence;
        /// <summary>Passenger's own look yaw, accumulated relative to the vehicle
        /// (not the world), so mouse-look survives the rotation pin below.</summary>
        private float _seatYawOffset;

        private string? _hint;

        // The passenger prompt is rendered through the game's own interaction
        // indicator (below) rather than a bespoke OnGUI overlay, so it matches the
        // drive prompt's font, position and styling for free.
        private HutongGames.PlayMaker.FsmString? _interactionText;
        private bool _wroteInteraction;
        private float _nextInteractionSearchAt;
        private bool _disabled;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Crash containment: a fault in passenger sync must disable this subsystem and free the
        /// local player, never escape into Unity's loop or strand the player parented to a seat.
        /// </summary>
        private void DisablePassengerSync(System.Exception e)
        {
            _disabled = true;
            WinterMPPlugin.Log.LogError("Passenger sync disabled after unhandled error: " + e);
            Diagnostics.SyncEventLog.Record("fatal", "Passenger: " + e);
            Diagnostics.SyncEventLog.DumpToFile();
            try
            {
                if (_seated) ForceExit("passenger sync error");
            }
            catch (System.Exception ex)
            {
                WinterMPPlugin.Log.LogError("Passenger ForceExit during disable failed: " + ex);
            }
        }

        private void Update()
        {
            if (_disabled) return;
            try
            {
                WatchLevelChanges();

                var session = SessionManager.Instance;
                bool sessionActive = session != null
                    && (session.State == SessionState.Hosting || session.State == SessionState.Connected);

                _hint = null;

                if (!sessionActive)
                {
                    if (_seated) ForceExit("session ended");
                    ClearRemoteSeats();
                    return;
                }

                FindPlayer();
                ScanVehicles();
                PurgeGonePlayers(session!);

                if (_player == null) return;

                if (DeathSyncManager.Instance != null && DeathSyncManager.Instance.IsLocalDead)
                {
                    if (_seated) ForceExit("local death");
                    return;
                }

                if (_seated)
                    UpdateSeated(session!);
                else
                    UpdateOnFoot(session!);
            }
            catch (System.Exception e)
            {
                DisablePassengerSync(e);
            }
        }

        private void LateUpdate()
        {
            if (_disabled) return;
            try
            {
                // Apply after every Update() so our text wins the frame over the
                // game's interaction raycast when both target the shared indicator.
                ApplyInteractionHint();

                // Re-pin every frame: player FSMs (and the game's own gravity code)
                // keep writing to the transform.
                if (!_seated || _player == null) return;
                var session = SessionManager.Instance;
                if (session == null || (session.State != SessionState.Hosting && session.State != SessionState.Connected)
                    || DeathSyncManager.Instance != null && DeathSyncManager.Instance.IsLocalDead)
                { ForceExit("player/session no longer active"); return; }
                if (!_vehicles.TryGetValue(_seatedVehicleId, out var vehicle) || !SeatAvailable(vehicle, _seatedIndex))
                { ForceExit("seat no longer available"); return; }

                var seat = vehicle.SeatLocal[_seatedIndex];
                _player.position = vehicle.Body.transform.TransformPoint(LocalSeatedOffset(seat));
                // Pin rotation too, or the seated body keeps its world-fixed facing while the
                // car turns (the player FSM rewrites rotation each frame) and visibly slides
                // relative to the seat (#32). Pin only the vehicle's own turning, not the
                // player's mouse-look: recover just the look the FSM added this frame by diffing
                // against the current vehicle heading plus our accumulated look.
                float vehicleYaw = vehicle.Body.transform.eulerAngles.y;
                float lookDelta = Mathf.DeltaAngle(vehicleYaw + _seatYawOffset, _player.eulerAngles.y);
                _seatYawOffset += lookDelta;
                _player.rotation = vehicle.Body.transform.rotation * Quaternion.Euler(0f, _seatYawOffset, 0f);
            }
            catch (System.Exception e)
            {
                DisablePassengerSync(e);
            }
        }

        /// <summary>
        /// Push the current passenger prompt into the game's interaction indicator
        /// (the same TextMesh the drive prompt uses). We only ever own the text
        /// while a hint is showing; when it clears we blank it once and hand the
        /// indicator back to the game — the seat cushions sit away from any native
        /// interactable, so there's nothing to clobber.
        /// </summary>
        private void ApplyInteractionHint()
        {
            var text = ResolveInteractionText();
            if (text == null) return;

            if (_hint != null)
            {
                text.Value = _hint;
                _wroteInteraction = true;
            }
            else if (_wroteInteraction)
            {
                text.Value = string.Empty;
                _wroteInteraction = false;
            }
        }

        private HutongGames.PlayMaker.FsmString? ResolveInteractionText()
        {
            if (_interactionText != null) return _interactionText;
            if (Time.unscaledTime < _nextInteractionSearchAt) return null;
            _nextInteractionSearchAt = Time.unscaledTime + InteractionSearchIntervalSeconds;

            var indicator = GameObject.Find(InteractionObjectPath);
            if (indicator == null) return null;

            foreach (var fsm in indicator.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName != InteractionFsmName) continue;
                _interactionText = fsm.FsmVariables.FindFsmString("Text");
                break;
            }

            return _interactionText;
        }

        // ------------------------------------------------------------------ local seat logic

        private void UpdateSeated(SessionManager session)
        {
            if (!_vehicles.TryGetValue(_seatedVehicleId, out var vehicle) || !SeatAvailable(vehicle, _seatedIndex))
            {
                ForceExit("vehicle gone");
                return;
            }

            _hint = "ENTER - Get out";

            if (Time.unscaledTime >= _nextRebroadcastAt)
            {
                _nextRebroadcastAt = Time.unscaledTime + RebroadcastSeconds;
                SendSeatState(session, _seatedVehicleId, (byte)_seatedIndex);
            }

            if (Input.GetKeyDown(KeyCode.Return) && Time.unscaledTime >= _nextKeyAt)
            {
                _nextKeyAt = Time.unscaledTime + KeyCooldownSeconds;
                Exit(session);
            }
        }

        private void UpdateOnFoot(SessionManager session)
        {
            // Never seat a player who is currently driving (parented under a car).
            if (_player!.parent != null) return;

            VehicleSeats? bestVehicle = null;
            int bestSeat = -1;
            float bestHorizontalSqr = EnterRadius * EnterRadius;

            foreach (var vehicle in _vehicles.Values)
            {
                if (vehicle.Body == null) continue;
                var root = vehicle.Body.transform;

                // Defer to the game's own drive key only where the driver's door is genuinely
                // the nearest thing. The old fixed radius skipped the WHOLE car and, on these
                // small cars, swallowed the rear seat tucked behind the driver's door — so we
                // compare per seat below (trigger closer than the seat → the drive key owns it)
                // instead of bailing on the entire vehicle here.
                float triggerDistSq = vehicle.DriveTrigger != null
                    ? (vehicle.DriveTrigger.position - _player.position).sqrMagnitude
                    : float.MaxValue;

                // Match in the car's local frame: a flat disc on the cabin floor
                // plane plus a vertical band. Comparing world-space 3D distance to
                // the cushion-height anchor never triggered from the ground beside
                // the door (that's where the player actually stands to get in).
                var playerLocal = root.InverseTransformPoint(_player.position);
                for (int seat = 0; seat < 3; seat++)
                {
                    if (!SeatAvailable(vehicle, seat) || IsSeatOccupied(vehicle.VehicleId, (byte)seat)) continue;

                    var seatLocal = vehicle.SeatLocal[seat];
                    if (Mathf.Abs(playerLocal.y - seatLocal.y) > EnterVerticalTolerance) continue;

                    // Closer to the driver's door than to this seat? The drive key owns this
                    // spot (front-left driver entry); leave it to the game.
                    if (triggerDistSq < (root.TransformPoint(seatLocal) - _player.position).sqrMagnitude)
                        continue;

                    float dx = playerLocal.x - seatLocal.x;
                    float dz = playerLocal.z - seatLocal.z;
                    float horizontalSqr = dx * dx + dz * dz;
                    if (horizontalSqr < bestHorizontalSqr)
                    {
                        bestHorizontalSqr = horizontalSqr;
                        bestVehicle = vehicle;
                        bestSeat = seat;
                    }
                }
            }

            if (bestVehicle == null) return;

            _hint = "ENTER - Sit down";

            if (Input.GetKeyDown(KeyCode.Return) && Time.unscaledTime >= _nextKeyAt)
            {
                _nextKeyAt = Time.unscaledTime + KeyCooldownSeconds;
                Enter(session, bestVehicle, bestSeat);
            }
        }

        private void Enter(SessionManager session, VehicleSeats vehicle, int seat)
        {
            if (_disabled || _seated || _player == null || !SeatAvailable(vehicle, seat)
                || IsSeatOccupied(vehicle.VehicleId, (byte)seat)
                || (session.State != SessionState.Hosting && session.State != SessionState.Connected)
                || DeathSyncManager.Instance != null && DeathSyncManager.Instance.IsLocalDead) return;
            _originalParent = _player!.parent;
            _playerController = _player.GetComponent<CharacterController>();
            _controllerWasEnabled = _playerController != null && _playerController.enabled;
            if (_playerController != null)
                _playerController.enabled = false;

            _seatedParent = vehicle.Body.transform;
            _player.parent = _seatedParent;

            _seated = true;
            _seatedVehicleId = vehicle.VehicleId;
            _seatedIndex = seat;
            // First seated frame folds the player's current facing-vs-car into the offset,
            // so they keep looking where they were at entry (see the pin in LateUpdate).
            _seatYawOffset = 0f;
            _nextRebroadcastAt = Time.unscaledTime + RebroadcastSeconds;

            SendSeatState(session, vehicle.VehicleId, (byte)seat);
            WinterMPPlugin.Log.LogInfo($"Passenger: seated in '{vehicle.Body.name}' seat {seat}.");
        }

        private void Exit(SessionManager session)
        {
            // Just hand control back in place — don't teleport the player out to the side of
            // the car. Reparenting preserves world position, so re-enabling the controller
            // simply lets them walk out from the seat, like the game's own get-out.
            ReleaseLocalSeat();
            SendSeatState(session, 0, PassengerState.SeatNone);
            WinterMPPlugin.Log.LogInfo("Passenger: got out.");
        }

        private void ForceExit(string reason)
        {
            ReleaseLocalSeat();

            var session = SessionManager.Instance;
            if (session != null)
                SendSeatState(session, 0, PassengerState.SeatNone);
            WinterMPPlugin.Log.LogInfo($"Passenger: force exit ({reason}).");
        }

        private void ReleaseLocalSeat()
        {
            if (!_seated) return;
            _seated = false;
            // Native death/respawn may already have moved PLAYER. Only undo our
            // own parenting; never pull a revived player out of their new parent.
            if (_player != null && _seatedParent != null && _player.parent == _seatedParent)
            {
                _player.parent = _originalParent;
                LevelPlayer();
            }
            RestoreController();
            _seatedVehicleId = 0; _seatedIndex = -1; _hint = null;
            _originalParent = null; _seatedParent = null; _playerController = null; _controllerWasEnabled = false;
        }

        internal void RetirePlayerSeat(byte playerId)
        {
            try
            {
                if (SessionManager.Instance?.LocalPlayerId == playerId) ReleaseLocalSeat();
                if (_remoteSeats.TryGetValue(playerId, out var seat)) DestroyAnchor(seat);
                _remoteSeats.Remove(playerId);
            }
            catch (System.Exception error) { DisablePassengerSync(error); }
        }

        internal void RetireAllSeats()
        {
            try { ReleaseLocalSeat(); ClearRemoteSeatOccupancy(); }
            catch (System.Exception error) { DisablePassengerSync(error); }
        }

        private void RestoreController()
        {
            if (_playerController != null && _controllerWasEnabled)
                _playerController.enabled = true;
        }

        /// <summary>
        /// Strip the pitch/roll a slanted car baked into the player: the seated pin
        /// copies the FULL vehicle rotation, and the game's mouse-look only ever
        /// writes yaw — it never clears an inherited tilt, so getting out on a slope
        /// left the camera stuck rolled. Keep the heading, zero the tilt.
        /// </summary>
        private void LevelPlayer()
        {
            if (_player == null) return;
            var forward = _player.forward;
            forward.y = 0f;
            // Car on its side/nose leaves forward near-vertical with no usable
            // horizontal part — fall back to the euler yaw as the heading.
            _player.rotation = forward.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(forward)
                : Quaternion.Euler(0f, _player.eulerAngles.y, 0f);
        }

        private void SendSeatState(SessionManager session, uint vehicleId, byte seat)
        {
            ushort sequence = unchecked(++_seatSequence);
            var message = new PassengerState
            {
                PlayerId = session.LocalPlayerId,
                VehicleId = vehicleId,
                SeatIndex = seat,
                Sequence = sequence,
            };
            session.SendWorldMessage(message, Channel.ReliableOrdered);
            session.RecordPassengerState(message);
        }

        // ------------------------------------------------------------------ remote occupancy

        /// <summary>
        /// Validates a guest's requested seat against the host scene. Passenger
        /// seating is cosmetic locally, but an unchecked request could pin that
        /// guest's remote avatar into any tracked car for every peer.
        /// </summary>
        public bool TryValidateGuestPassengerState(PassengerState message, RemotePlayer player, bool continuing)
        {
            if (!message.IsSeated)
                return message.VehicleId == 0;

            if (_disabled || player.IsDead) return false;
            if (message.SeatIndex >= 3) return false;
            // Validation must use the currently registered body, even if an old
            // object with the same network id is still alive between scans.
            ScanVehicles(force: true);
            if (!_vehicles.TryGetValue(message.VehicleId, out var vehicle) || !SeatAvailable(vehicle, message.SeatIndex))
                return false;

            // Once the host accepted this exact seat, occupancy is the proof. Comparing
            // a delayed world-space pose with a moving car every keepalive ejects riders.
            if (continuing) return true;
            if (player.LastTransformTime <= 0f
                || Time.unscaledTime - player.LastTransformTime > GuestPoseFreshSeconds
                || !IsFinite(player.Position)) return false;

            Vector3 seatPosition = vehicle.Body.transform.TransformPoint(vehicle.SeatLocal[message.SeatIndex]);
            return (player.Position - seatPosition).sqrMagnitude <= GuestSeatClaimRadius * GuestSeatClaimRadius;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        public void OnRemotePassengerState(PassengerState message)
        {
            var session = SessionManager.Instance;
            if (session == null) return;

            // The host normally omits a guest from relays of that guest's own
            // valid state. A self-addressed SeatNone is therefore an explicit host
            // correction. A delayed answer to an older claim must not undo a newer
            // local entry; the host will answer that newer request separately.
            if (message.PlayerId == session.LocalPlayerId)
            {
                if (!message.IsSeated && _seated && message.Sequence == _seatSequence)
                    ApplyHostSeatCorrection();
                return;
            }

            if (_remoteSeatSequences.TryGetValue(message.PlayerId, out ushort previous))
            {
                ushort difference = (ushort)(message.Sequence - previous);
                if (difference == 0 || difference > short.MaxValue) return;
            }
            _remoteSeatSequences[message.PlayerId] = message.Sequence;

            foreach (var player in session.Players)
                if (player.PlayerId == message.PlayerId && player.IsDead)
                { RetirePlayerSeat(message.PlayerId); return; }

            if (message.IsSeated)
            {
                // Same player moved to a different seat: tear down their old anchor.
                if (_remoteSeats.TryGetValue(message.PlayerId, out var prev)
                    && (prev.VehicleId != message.VehicleId || prev.Seat != message.SeatIndex))
                {
                    DestroyAnchor(prev);
                }

                // Another remote player already mapped to this (vehicle,seat): evict it
                // so only one avatar resolves to the shared per-seat anchor. Capture the
                // key first — net35/Mono forbids mutating a Dictionary while enumerating.
                byte conflicting = 0;
                bool hasConflict = false;
                foreach (KeyValuePair<byte, SeatRef> kv in _remoteSeats)
                {
                    if (kv.Key != message.PlayerId
                        && kv.Value.VehicleId == message.VehicleId
                        && kv.Value.Seat == message.SeatIndex)
                    {
                        conflicting = kv.Key;
                        hasConflict = true;
                        break;
                    }
                }
                if (hasConflict)
                {
                    // Destroy the evicted occupant's anchor before dropping it, or its
                    // GameObject leaks and TryGetSeatAnchor can still resolve two avatars
                    // to one seat (#26).
                    if (_remoteSeats.TryGetValue(conflicting, out var conflictRef))
                        DestroyAnchor(conflictRef);
                    _remoteSeats.Remove(conflicting);
                }

                _remoteSeats[message.PlayerId] = new SeatRef { VehicleId = message.VehicleId, Seat = message.SeatIndex };

                // Entry race on the same seat: lowest player id keeps it.
                if (_seated && message.VehicleId == _seatedVehicleId && message.SeatIndex == _seatedIndex
                    && message.PlayerId < session.LocalPlayerId)
                {
                    WinterMPPlugin.Log.LogInfo("Passenger: seat taken by a lower player id — getting out.");
                    Exit(session);
                }
            }
            else
            {
                if (_remoteSeats.TryGetValue(message.PlayerId, out var seatRef))
                    DestroyAnchor(seatRef);
                _remoteSeats.Remove(message.PlayerId);
            }
        }

        private void ApplyHostSeatCorrection()
        {
            ReleaseLocalSeat();
            WinterMPPlugin.Log.LogWarning("Passenger: host rejected the seat claim; returned to on-foot state.");
        }

        private bool IsSeatOccupied(uint vehicleId, byte seat)
        {
            foreach (var seatRef in _remoteSeats.Values)
            {
                if (seatRef.VehicleId == vehicleId && seatRef.Seat == seat)
                    return true;
            }

            return _seated && _seatedVehicleId == vehicleId && _seatedIndex == seat;
        }

        /// <summary>
        /// Seat anchor for a remote passenger's avatar (same contract as
        /// WorldSyncManager.TryGetDriverAnchor) — pinned, not stream-smoothed.
        /// </summary>
        public bool TryGetSeatAnchor(byte playerId, out Transform? seat, out Transform? vehicle)
        {
            seat = null;
            vehicle = null;
            if (!_remoteSeats.TryGetValue(playerId, out var seatRef)) return false;
            if (!_vehicles.TryGetValue(seatRef.VehicleId, out var seats) || seats.Body == null) return false;
            if (seatRef.Seat >= seats.SeatLocal.Length) return false;

            var anchor = seats.RemoteAnchors[seatRef.Seat];
            if (anchor == null)
            {
                anchor = new GameObject($"WinterMP_Seat_{seatRef.VehicleId:X8}_{seatRef.Seat}");
                anchor.transform.parent = seats.Body.transform;
                anchor.transform.localPosition = SeatedLocalOffset(seats.SeatLocal[seatRef.Seat]);
                anchor.transform.localRotation = Quaternion.identity;
                seats.RemoteAnchors[seatRef.Seat] = anchor;
            }

            seat = anchor.transform;
            vehicle = seats.Body.transform;
            return true;
        }

        private void DestroyAnchor(SeatRef seatRef)
        {
            if (!_vehicles.TryGetValue(seatRef.VehicleId, out var seats)) return;
            if (seatRef.Seat >= seats.RemoteAnchors.Length) return;
            var anchor = seats.RemoteAnchors[seatRef.Seat];
            if (anchor != null) Destroy(anchor);
            seats.RemoteAnchors[seatRef.Seat] = null;
        }

        private void PurgeGonePlayers(SessionManager session)
        {
            if (_remoteSeats.Count == 0) return;

            _purgeScratch.Clear();
            foreach (byte playerId in _remoteSeats.Keys)
            {
                bool present = false;
                foreach (var player in session.Players)
                {
                    if (player.PlayerId == playerId)
                    {
                        present = true;
                        break;
                    }
                }
                if (!present) _purgeScratch.Add(playerId);
            }

            foreach (byte playerId in _purgeScratch)
            {
                DestroyAnchor(_remoteSeats[playerId]);
                _remoteSeats.Remove(playerId);
                _remoteSeatSequences.Remove(playerId);
            }
        }

        private void ClearRemoteSeats()
        {
            ClearRemoteSeatOccupancy();
            _remoteSeatSequences.Clear();
        }

        private void ClearRemoteSeatOccupancy()
        {
            if (_remoteSeats.Count == 0) return;
            foreach (var seatRef in _remoteSeats.Values)
                DestroyAnchor(seatRef);
            _remoteSeats.Clear();
        }

        // ------------------------------------------------------------------ seat discovery

        private void ScanVehicles(bool force = false)
        {
            if (!force && Time.unscaledTime < _nextVehicleScanAt) return;
            _nextVehicleScanAt = Time.unscaledTime + VehicleScanIntervalSeconds;

            var world = WorldSyncManager.Instance;
            if (world == null) return;

            world.CollectVehicles(_vehicleScratch);
            _liveVehicleIds.Clear();
            foreach (var info in _vehicleScratch)
                if (info.Body != null) _liveVehicleIds.Add(info.Id);
            _retiredVehicleIds.Clear();
            foreach (var pair in _vehicles)
                if (!_liveVehicleIds.Contains(pair.Key)) _retiredVehicleIds.Add(pair.Key);
            foreach (uint id in _retiredVehicleIds) RemoveCachedVehicle(id);

            foreach (var info in _vehicleScratch)
            {
                if (info.Body == null) continue;
                if (_vehicles.TryGetValue(info.Id, out var existing))
                {
                    if (existing.Body == info.Body) continue;
                    RemoveCachedVehicle(info.Id);
                }

                string name = info.Body.name;
                bool taxi = IsTaxi(info.Body);
                if (!taxi && !name.StartsWith("SORBET", System.StringComparison.Ordinal)
                    && !name.StartsWith("CORRIS", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var seats = taxi ? ResolveTaxiSeats(info.Id, info.Body) : ResolveSeats(info.Id, info.Body);
                if (seats != null)
                {
                    _vehicles[info.Id] = seats;
                    WinterMPPlugin.Log.LogInfo($"Passenger: {(taxi ? 2 : 3)} seats registered on '{name}'.");
                }
            }
        }

        private void RemoveCachedVehicle(uint vehicleId)
        {
            if (!_vehicles.TryGetValue(vehicleId, out var vehicle)) return;
            if (_seated && _seatedVehicleId == vehicleId) ForceExit("vehicle replaced or removed");
            for (byte seat = 0; seat < vehicle.RemoteAnchors.Length; seat++)
                DestroyAnchor(new SeatRef { VehicleId = vehicleId, Seat = seat });
            _vehicles.Remove(vehicleId);
            // Keep accepted remote occupancy and sequence history. Anchors can
            // rebind to the same logical vehicle; only host seat results retire it.
            Diagnostics.SyncEventLog.Record("passenger-vehicle-refresh", vehicleId.ToString("X8"));
        }

        // Body placement (remote avatar + exit spot): rest on the cushion.
        private static Vector3 SeatedLocalOffset(Vector3 seatLocal) =>
            new Vector3(seatLocal.x, seatLocal.y + SeatBodyRaise, seatLocal.z);

        // Local first-person pin: drop well below the cushion so the standing-height camera
        // lands at seated eye level. Kept separate from the body offset above so lowering the
        // local view never sinks how OTHER players see this passenger (they use the seat anchor).
        private static Vector3 LocalSeatedOffset(Vector3 seatLocal) =>
            new Vector3(seatLocal.x, seatLocal.y - LocalCameraDrop, seatLocal.z);

        /// <summary>
        /// Seat layout from the game's own in-cabin anchors. Front passenger uses
        /// MassPassenger (the seated physics point, like MassDriver for the driver);
        /// rear bench spots use the bench pivot ("VINP_SeatReatBench" — sic — on the
        /// CORRIS, "RearSeat" on the SORBET). DriveTrigger is only used to confirm
        /// the vehicle is enterable and to exclude the driver's door from passenger
        /// entry.
        /// </summary>
        private static VehicleSeats? ResolveSeats(uint vehicleId, Rigidbody body)
        {
            var root = body.transform;
            Transform? driveTrigger = null;
            Transform? frontAnchor = null;
            Transform? benchAnchor = null;

            foreach (var child in body.GetComponentsInChildren<Transform>(true))
            {
                string childName = child.name;
                if (driveTrigger == null && childName.StartsWith("DriveTrigger", System.StringComparison.Ordinal))
                    driveTrigger = child;
                else if (frontAnchor == null && childName == "MassPassenger")
                    frontAnchor = child;
                else if (benchAnchor == null
                         && (childName == "VINP_SeatReatBench" || childName == "VINP_SeatRearBench" || childName == "RearSeat"))
                    benchAnchor = child;
            }

            if (driveTrigger == null || frontAnchor == null) return null;

            var front = root.InverseTransformPoint(frontAnchor.position);
            // Seat the passenger forward into the cabin (see FrontSeatForwardOffset). +z is
            // toward the windshield here — the rear bench below sits at driverLocal.z - 0.85.
            front.z += FrontSeatForwardOffset;
            var driverLocal = root.InverseTransformPoint(driveTrigger.position);

            float rearZ = benchAnchor != null
                ? root.InverseTransformPoint(benchAnchor.position).z
                : driverLocal.z - 0.85f;
            // Take the rear seat's HEIGHT from the front mass point, never from the bench
            // pivot: that pivot's origin is a mesh/object anchor, not a seated point — on the
            // CORRIS it sits ~1.1 m above cushion level, which parked the passenger camera up
            // by the roof. The rear cushion is at essentially the same height as the front
            // seat, so front.y is both reliable and correct. The pivot still gives fore/aft.
            float rearY = front.y;

            var seats = new VehicleSeats
            {
                VehicleId = vehicleId,
                Body = body,
                DriveTrigger = driveTrigger,
            };
            seats.SeatLocal[0] = front;
            seats.SeatLocal[1] = new Vector3(RearBenchHalfWidth, rearY, rearZ);
            seats.SeatLocal[2] = new Vector3(-RearBenchHalfWidth, rearY, rearZ);
            return seats;
        }

        // ------------------------------------------------------------------ housekeeping

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

            // Scene swap destroyed the player, the vehicles and our anchors.
            _seated = false;
            _player = null;
            _playerController = null;
            _originalParent = null;
            _seatedParent = null;
            _vehicles.Clear();
            _remoteSeats.Clear();
            _remoteSeatSequences.Clear();
            _nextPlayerSearchAt = 0f;
            _nextVehicleScanAt = 0f;

            // The old scene's interaction indicator is gone; re-resolve it lazily.
            _interactionText = null;
            _wroteInteraction = false;
            _nextInteractionSearchAt = 0f;
        }

        private void FindPlayer()
        {
            if (_player != null || Time.unscaledTime < _nextPlayerSearchAt) return;
            _nextPlayerSearchAt = Time.unscaledTime + PlayerSearchIntervalSeconds;

            var playerObject = GameObject.Find(PlayerObjectName);
            if (playerObject != null)
                _player = playerObject.transform;
        }

    }
}
