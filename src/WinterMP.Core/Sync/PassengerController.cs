using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Passenger seats for the SORBET and the CORRIS (the project car): front
    /// passenger seat plus two rear bench spots, so a full car carries 4 players.
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
    public sealed class PassengerController : MonoBehaviour
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
        }

        private readonly Dictionary<uint, VehicleSeats> _vehicles = new Dictionary<uint, VehicleSeats>();
        private readonly Dictionary<byte, SeatRef> _remoteSeats = new Dictionary<byte, SeatRef>();
        private readonly List<WorldSyncManager.VehicleInfo> _vehicleScratch = new List<WorldSyncManager.VehicleInfo>();
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
        private bool _controllerWasEnabled;
        private float _nextKeyAt;
        private float _nextRebroadcastAt;
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

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
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

            if (_seated)
                UpdateSeated(session!);
            else
                UpdateOnFoot(session!);
        }

        private void LateUpdate()
        {
            // Apply after every Update() so our text wins the frame over the
            // game's interaction raycast when both target the shared indicator.
            ApplyInteractionHint();

            // Re-pin every frame: player FSMs (and the game's own gravity code)
            // keep writing to the transform.
            if (!_seated || _player == null) return;
            if (!_vehicles.TryGetValue(_seatedVehicleId, out var vehicle) || vehicle.Body == null) return;

            var seat = vehicle.SeatLocal[_seatedIndex];
            _player.position = vehicle.Body.transform.TransformPoint(LocalSeatedOffset(seat));
            // Pin rotation too, or the seated body keeps its world-fixed facing while the
            // car turns (the player FSM rewrites rotation each frame) and visibly slides
            // relative to the seat (#32). Pin only the vehicle's own turning, not the
            // player's mouse-look: recover just the look the FSM added this frame by diffing
            // against the CURRENT vehicle heading plus our accumulated look. Diffing against
            // last frame's pinned yaw used a STALE vehicle heading, so the car's own turn
            // (V_now - V_lastframe) leaked into the offset and the camera over-rotated.
            float vehicleYaw = vehicle.Body.transform.eulerAngles.y;
            float lookDelta = Mathf.DeltaAngle(vehicleYaw + _seatYawOffset, _player.eulerAngles.y);
            _seatYawOffset += lookDelta;
            _player.rotation = vehicle.Body.transform.rotation * Quaternion.Euler(0f, _seatYawOffset, 0f);
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
            if (!_vehicles.TryGetValue(_seatedVehicleId, out var vehicle) || vehicle.Body == null)
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
                    if (IsSeatOccupied(vehicle.VehicleId, (byte)seat)) continue;

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
            _originalParent = _player!.parent;
            _playerController = _player.GetComponent<CharacterController>();
            _controllerWasEnabled = _playerController != null && _playerController.enabled;
            if (_playerController != null)
                _playerController.enabled = false;

            _player.parent = vehicle.Body.transform;

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
            if (_player != null)
                _player.parent = _originalParent;

            LevelPlayer();
            RestoreController();
            _seated = false;
            SendSeatState(session, 0, PassengerState.SeatNone);
            WinterMPPlugin.Log.LogInfo("Passenger: got out.");
        }

        private void ForceExit(string reason)
        {
            if (_player != null)
                _player.parent = _originalParent;
            LevelPlayer();
            RestoreController();
            _seated = false;

            var session = SessionManager.Instance;
            if (session != null)
                SendSeatState(session, 0, PassengerState.SeatNone);
            WinterMPPlugin.Log.LogInfo($"Passenger: force exit ({reason}).");
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

        private static void SendSeatState(SessionManager session, uint vehicleId, byte seat)
        {
            session.SendWorldMessage(new PassengerState
            {
                PlayerId = session.LocalPlayerId,
                VehicleId = vehicleId,
                SeatIndex = seat,
            }, Channel.ReliableOrdered);
            session.RecordPassengerState(new PassengerState
            {
                PlayerId = session.LocalPlayerId,
                VehicleId = vehicleId,
                SeatIndex = seat,
            });
        }

        // ------------------------------------------------------------------ remote occupancy

        public void OnRemotePassengerState(PassengerState message)
        {
            var session = SessionManager.Instance;
            if (session == null || message.PlayerId == session.LocalPlayerId) return;

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
            }
        }

        private void ClearRemoteSeats()
        {
            if (_remoteSeats.Count == 0) return;
            foreach (var seatRef in _remoteSeats.Values)
                DestroyAnchor(seatRef);
            _remoteSeats.Clear();
        }

        // ------------------------------------------------------------------ seat discovery

        private void ScanVehicles()
        {
            if (Time.unscaledTime < _nextVehicleScanAt) return;
            _nextVehicleScanAt = Time.unscaledTime + VehicleScanIntervalSeconds;

            var world = WorldSyncManager.Instance;
            if (world == null) return;

            world.CollectVehicles(_vehicleScratch);
            foreach (var info in _vehicleScratch)
            {
                if (_vehicles.ContainsKey(info.Id) || info.Body == null) continue;

                string name = info.Body.name;
                if (!name.StartsWith("SORBET", System.StringComparison.Ordinal)
                    && !name.StartsWith("CORRIS", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var seats = ResolveSeats(info.Id, info.Body);
                if (seats != null)
                {
                    _vehicles[info.Id] = seats;
                    WinterMPPlugin.Log.LogInfo($"Passenger: 3 seats registered on '{name}'.");
                }
            }
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
            _vehicles.Clear();
            _remoteSeats.Clear();
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
