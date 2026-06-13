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
        /// <summary>Must be on the cushion — same scale as the in-cabin drive seat.</summary>
        private const float EnterRadius = 0.55f;
        /// <summary>Return is also the game's own enter-car key; never compete with
        /// the drive trigger when the player stands next to the driver's door.</summary>
        private const float DriveTriggerExclusionRadius = 1.0f;
        private const float KeyCooldownSeconds = 0.7f;
        private const float RebroadcastSeconds = 8f;
        private const float VehicleScanIntervalSeconds = 3f;
        private const float PlayerSearchIntervalSeconds = 2f;
        /// <summary>Seat anchors sit at cushion height; the player pivot (feet)
        /// goes below so the camera ends up at seated eye level.</summary>
        private const float SeatPivotDrop = 0.4f;
        private const float SeatHeightOffset = 0.5f;
        private const float ExitLateralMeters = 1.3f;
        private const float RearBenchHalfWidth = 0.35f;
        private const string PlayerObjectName = "PLAYER";

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

        private string? _hint;
        private GUIStyle? _hintStyle;

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
            // Re-pin every frame: player FSMs (and the game's own gravity code)
            // keep writing to the transform.
            if (!_seated || _player == null) return;
            if (!_vehicles.TryGetValue(_seatedVehicleId, out var vehicle) || vehicle.Body == null) return;

            var seat = vehicle.SeatLocal[_seatedIndex];
            _player.position = vehicle.Body.transform.TransformPoint(SeatedLocalOffset(seat));
        }

        private void OnGUI()
        {
            if (_hint == null) return;

            if (_hintStyle == null)
            {
                _hintStyle = new GUIStyle
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 16,
                    fontStyle = FontStyle.Bold,
                };
                _hintStyle.normal.textColor = Color.white;
            }

            var rect = new Rect(Screen.width / 2f - 200f, Screen.height - 110f, 400f, 26f);
            var shadow = rect;
            shadow.x += 1f;
            shadow.y += 1f;
            var color = GUI.color;
            GUI.color = Color.black;
            GUI.Label(shadow, _hint, _hintStyle);
            GUI.color = color;
            GUI.Label(rect, _hint, _hintStyle);
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
            float bestDistanceSqr = EnterRadius * EnterRadius;

            foreach (var vehicle in _vehicles.Values)
            {
                if (vehicle.Body == null) continue;
                var root = vehicle.Body.transform;

                // Standing at the driver's door? That spot belongs to the game's
                // own drive trigger (same key).
                if (vehicle.DriveTrigger != null
                    && (vehicle.DriveTrigger.position - _player.position).sqrMagnitude
                        < DriveTriggerExclusionRadius * DriveTriggerExclusionRadius)
                {
                    continue;
                }

                for (int seat = 0; seat < 3; seat++)
                {
                    if (IsSeatOccupied(vehicle.VehicleId, (byte)seat)) continue;

                    var world = root.TransformPoint(vehicle.SeatLocal[seat]);
                    float distanceSqr = (world - _player.position).sqrMagnitude;
                    if (distanceSqr < bestDistanceSqr)
                    {
                        bestDistanceSqr = distanceSqr;
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
            _nextRebroadcastAt = Time.unscaledTime + RebroadcastSeconds;

            SendSeatState(session, vehicle.VehicleId, (byte)seat);
            WinterMPPlugin.Log.LogInfo($"Passenger: seated in '{vehicle.Body.name}' seat {seat}.");
        }

        private void Exit(SessionManager session)
        {
            if (_vehicles.TryGetValue(_seatedVehicleId, out var vehicle) && vehicle.Body != null && _player != null)
            {
                var seat = vehicle.SeatLocal[_seatedIndex];
                float side = seat.x >= 0f ? 1f : -1f;
                var seated = SeatedLocalOffset(seat);
                var exitLocal = new Vector3(side * (Mathf.Abs(seat.x) + ExitLateralMeters), seated.y + 0.2f, seat.z);
                _player.parent = _originalParent;
                _player.position = vehicle.Body.transform.TransformPoint(exitLocal);
            }
            else if (_player != null)
            {
                _player.parent = _originalParent;
            }

            RestoreController();
            _seated = false;
            SendSeatState(session, 0, PassengerState.SeatNone);
            WinterMPPlugin.Log.LogInfo("Passenger: got out.");
        }

        private void ForceExit(string reason)
        {
            if (_player != null)
                _player.parent = _originalParent;
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

        private static Vector3 SeatedLocalOffset(Vector3 seatLocal) =>
            new Vector3(seatLocal.x, seatLocal.y - SeatPivotDrop + SeatHeightOffset, seatLocal.z);

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
            var driverLocal = root.InverseTransformPoint(driveTrigger.position);

            float rearZ = benchAnchor != null
                ? root.InverseTransformPoint(benchAnchor.position).z
                : driverLocal.z - 0.85f;
            float rearY = benchAnchor != null
                ? root.InverseTransformPoint(benchAnchor.position).y
                : front.y + 0.02f;

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
