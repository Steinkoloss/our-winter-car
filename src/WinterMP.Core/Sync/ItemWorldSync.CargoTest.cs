using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Dev self-test for cargo pose streaming (<c>-wintermp-cargotest &lt;delay&gt;</c>,
    /// local 2P harness). HOST: once a guest is present plus the delay, drops the
    /// loose items nearest the nearest vehicle onto its body, claims and pushes the
    /// vehicle for a few seconds, then lets it settle — exercising claim → vehicle
    /// stream + velocity → cargo membership → exit finals, end to end. Both roles
    /// emit CARGOTEST log lines with vehicle-local poses so the two instance logs
    /// can be diffed offline: the guest's composed local pose must track the pose
    /// the host's physics is simulating.
    /// </summary>
    internal sealed partial class ItemWorldSync
    {
        private const float CargoTestPushSeconds = 6f;
        private const float CargoTestPushSpeed = 2.5f;
        private const float CargoTestLogPeriod = 0.5f;
        private const float CargoTestSettleSeconds = 14f;
        private const int CargoTestItemCount = 3;

        private int _cargoTestPhase; // 0 wait+arm, 1 push, 2 settle, 3 done
        private float _cargoTestArmedAt = -1f;
        private float _cargoTestPushUntil;
        private float _cargoTestEndAt;
        private float _cargoTestNextLogAt;
        private int _cargoTestLastPinCount = -1;
        private SyncedItem? _cargoTestVehicle;
        private readonly List<SyncedItem> _cargoTestItems = new List<SyncedItem>();

        // The game physics-locks parked cars (constraints / kinematic); the test lifts
        // that for the push the way entering the car would, and restores it at the end.
        private bool _cargoTestPhysicsPatched;
        private bool _cargoTestSavedKinematic;
        private RigidbodyConstraints _cargoTestSavedConstraints;

        public void RunCargoTest(SessionManager session, float delaySeconds)
        {
            float now = Time.unscaledTime;

            if (!session.IsHost)
            {
                LogGuestCargoPins(now);
                return;
            }

            switch (_cargoTestPhase)
            {
                case 0:
                    if (session.PlayerCount == 0)
                    {
                        _cargoTestArmedAt = -1f;
                        return;
                    }

                    if (_cargoTestArmedAt < 0f)
                    {
                        _cargoTestArmedAt = now;
                        WinterMPPlugin.Log.LogInfo(
                            $"CARGOTEST host armed — starting in {delaySeconds:0}s.");
                    }

                    if (now < _cargoTestArmedAt + delaySeconds) return;
                    if (!SetupCargoTest(session, now))
                    {
                        _cargoTestPhase = 3;
                        return;
                    }

                    _cargoTestPhase = 1;
                    _cargoTestPushUntil = now + CargoTestPushSeconds;
                    _cargoTestEndAt = now + CargoTestPushSeconds + CargoTestSettleSeconds;
                    return;

                case 1:
                    if (_cargoTestVehicle == null || _cargoTestVehicle.Body == null)
                    {
                        RestoreCargoTestPhysics();
                        _cargoTestPhase = 3;
                        return;
                    }

                    var body = _cargoTestVehicle.Body;
                    var push = body.transform.forward * CargoTestPushSpeed;
                    body.WakeUp();
                    body.velocity = new Vector3(push.x, body.velocity.y, push.z);

                    LogHostCargoPoses(now);
                    if (now >= _cargoTestPushUntil)
                    {
                        _cargoTestPhase = 2;
                        WinterMPPlugin.Log.LogInfo("CARGOTEST host push ended — settling.");
                    }

                    return;

                case 2:
                    LogHostCargoPoses(now);
                    if (now >= _cargoTestEndAt)
                    {
                        RestoreCargoTestPhysics();
                        _cargoTestPhase = 3;
                        WinterMPPlugin.Log.LogInfo("CARGOTEST host done.");
                    }

                    return;
            }
        }

        private void RestoreCargoTestPhysics()
        {
            if (!_cargoTestPhysicsPatched) return;
            _cargoTestPhysicsPatched = false;
            if (_cargoTestVehicle == null || _cargoTestVehicle.Body == null) return;
            _cargoTestVehicle.Body.isKinematic = _cargoTestSavedKinematic;
            _cargoTestVehicle.Body.constraints = _cargoTestSavedConstraints;
        }

        /// <summary>Nearest vehicle to the player, nearest loose items to that vehicle,
        /// dropped onto its body from vehicle-local slots so real contacts form.</summary>
        private bool SetupCargoTest(SessionManager session, float now)
        {
            _bridge.FindLocalPlayer();
            if (_bridge.LocalPlayer == null)
            {
                // Player not spawned yet — retry next frame instead of consuming the arm.
                _cargoTestArmedAt = now;
                _cargoTestPhase = 0;
                return false;
            }

            Vector3 playerPos = _bridge.LocalPlayer.position;
            SyncedItem? vehicle = null;
            float bestVehicleSqr = float.MaxValue;
            foreach (var candidate in _items.Values)
            {
                if (!candidate.IsVehicle || candidate.Body == null) continue;
                float dSqr = (candidate.Body.transform.position - playerPos).sqrMagnitude;
                if (dSqr >= bestVehicleSqr) continue;
                bestVehicleSqr = dSqr;
                vehicle = candidate;
            }

            if (vehicle == null)
            {
                WinterMPPlugin.Log.LogWarning("CARGOTEST host: no vehicle found — aborting.");
                return false;
            }

            Vector3 vehiclePos = vehicle.Body.transform.position;
            var nearest = new List<SyncedItem>();
            foreach (var candidate in _items.Values)
            {
                if (candidate.IsVehicle || candidate.Body == null || candidate.DespawnSent) continue;
                if (IsPlayerHeldItem(candidate)) continue;
                if ((candidate.Body.transform.position - vehiclePos).sqrMagnitude > 20f * 20f) continue;
                nearest.Add(candidate);
            }

            nearest.Sort((a, b) =>
                (a.Body.transform.position - vehiclePos).sqrMagnitude
                    .CompareTo((b.Body.transform.position - vehiclePos).sqrMagnitude));
            if (nearest.Count > CargoTestItemCount)
                nearest.RemoveRange(CargoTestItemCount, nearest.Count - CargoTestItemCount);

            if (nearest.Count == 0)
            {
                WinterMPPlugin.Log.LogWarning("CARGOTEST host: no loose items near the vehicle — aborting.");
                return false;
            }

            var vehicleTransform = vehicle.Body.transform;
            Vector3[] slots =
            {
                new Vector3(0f, 1.5f, 0.3f),
                new Vector3(0.35f, 1.6f, -0.6f),
                new Vector3(-0.35f, 1.7f, 0.9f),
            };

            _cargoTestItems.Clear();
            for (int i = 0; i < nearest.Count; i++)
            {
                var item = nearest[i];
                var itemBody = item.Body;
                itemBody.transform.position = vehicleTransform.TransformPoint(slots[i % slots.Length]);
                itemBody.velocity = Vector3.zero;
                itemBody.angularVelocity = Vector3.zero;
                itemBody.WakeUp();
                _cargoTestItems.Add(item);
            }

            _cargoTestVehicle = vehicle;

            var vehicleBody = vehicle.Body;
            _cargoTestSavedKinematic = vehicleBody.isKinematic;
            _cargoTestSavedConstraints = vehicleBody.constraints;
            _cargoTestPhysicsPatched = true;
            vehicleBody.isKinematic = false;
            vehicleBody.constraints = RigidbodyConstraints.None;

            ClaimItem(session, vehicle, vehicleBody, now);

            var ids = new System.Text.StringBuilder();
            foreach (var item in _cargoTestItems)
                ids.Append($"{item.Id:X8}('{item.Path}') ");
            Vector3 rootPos = GetVehicleSceneRoot(vehicleBody.transform).position;
            Vector3 bodyPos = vehicleBody.transform.position;
            WinterMPPlugin.Log.LogInfo(
                $"CARGOTEST host setup: vehicle {vehicle.Id:X8}('{vehicle.Path}'), items: {ids}" +
                $"| wasKinematic={_cargoTestSavedKinematic} constraints={_cargoTestSavedConstraints} " +
                $"body=({bodyPos.x:F1},{bodyPos.y:F1},{bodyPos.z:F1}) root=({rootPos.x:F1},{rootPos.y:F1},{rootPos.z:F1})");
            return true;
        }

        private void LogHostCargoPoses(float now)
        {
            if (now < _cargoTestNextLogAt || _cargoTestVehicle == null || _cargoTestVehicle.Body == null)
                return;
            _cargoTestNextLogAt = now + CargoTestLogPeriod;

            var vehicleTransform = _cargoTestVehicle.Body.transform;
            Vector3 vehiclePos = vehicleTransform.position;
            WinterMPPlugin.Log.LogInfo(
                $"CARGOTEST host veh={_cargoTestVehicle.Id:X8} " +
                $"world=({vehiclePos.x:F1},{vehiclePos.y:F1},{vehiclePos.z:F1}) " +
                $"v={_cargoTestVehicle.Body.velocity.magnitude:F2} activeVehicles={_activeCargoVehicles.Count}");
            foreach (var item in _cargoTestItems)
            {
                if (item.Body == null) continue;
                Vector3 local = vehicleTransform.InverseTransformPoint(item.Body.transform.position);
                WinterMPPlugin.Log.LogInfo(
                    $"CARGOTEST host item={item.Id:X8} veh={_cargoTestVehicle.Id:X8} " +
                    $"local=({local.x:F2},{local.y:F2},{local.z:F2}) inSet={item.LocalCargoVehicleId != 0}");
            }
        }

        private void LogGuestCargoPins(float now)
        {
            if (now < _cargoTestNextLogAt) return;
            _cargoTestNextLogAt = now + CargoTestLogPeriod;

            int pins = 0;
            foreach (var item in _items.Values)
            {
                if (item.RemoteCargoVehicleId == 0 || item.IsVehicle) continue;
                pins++;
                WinterMPPlugin.Log.LogInfo(
                    $"CARGOTEST guest item={item.Id:X8} veh={item.RemoteCargoVehicleId:X8} " +
                    $"local=({item.RemoteCargoPos.x:F2},{item.RemoteCargoPos.y:F2},{item.RemoteCargoPos.z:F2}) " +
                    $"target=({item.RemoteCargoTargetPos.x:F2},{item.RemoteCargoTargetPos.y:F2},{item.RemoteCargoTargetPos.z:F2})");
            }

            if (pins != _cargoTestLastPinCount)
            {
                _cargoTestLastPinCount = pins;
                WinterMPPlugin.Log.LogInfo($"CARGOTEST guest pins={pins}");
            }
        }
    }
}
