using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class VehicleWorldSync
    {
        private const float ConditionProbeIntervalSeconds = 5f;
        private const float ConditionKeepAliveSeconds = 20f;
        private readonly VehicleConditionStreamPolicy _vehicleConditionStreams = new VehicleConditionStreamPolicy();

        // Wheel index order used on the wire (FL, FR, RL, RR).
        private static readonly string[] WheelSuffixes = { "FL", "FR", "RL", "RR" };
        private static readonly byte[] WheelPunctureFlags =
        {
            VehicleCondition.FlagPunctureFL, VehicleCondition.FlagPunctureFR,
            VehicleCondition.FlagPunctureRL, VehicleCondition.FlagPunctureRR,
        };
        private static readonly byte[] WheelRimFlags =
        {
            VehicleCondition.FlagRimFL, VehicleCondition.FlagRimFR,
            VehicleCondition.FlagRimRL, VehicleCondition.FlagRimRR,
        };

        /// <summary>
        /// Streams delegated vehicle condition. Native driver inputs and physical
        /// flat/rim reconciliation remain partial (COVERAGE-ROADMAP 2.2).
        /// </summary>
        public void UpdateVehicleCondition(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            float now = Time.unscaledTime;

            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || item.Body == null) continue;
                try
                {
                    ApplyApprovedConditionRelease(item);
                    UpdateConditionBindings(item);
                    if (item.LocallyOwned) OwnerBroadcastCondition(session, item, now);
                    else ApplyNativeTirePressure(item);
                }
                catch (System.Exception error)
                {
                    WinterMPPlugin.Log.LogDebug("VehicleWorldSync: condition update unavailable for " + item.Path + ": " + error.Message);
                }
            }
        }

        /// <summary>
        /// Host repairs use the accepted current owner's report while delegated.
        /// A snapshot never borrows a driver's live sequence or samples competing local state.
        /// </summary>
        internal VehicleCondition? TryBuildConditionSnapshot(SyncedItem item, byte ownerPlayerId)
        {
            if (!item.IsVehicle || item.Body == null) return null;
            if (_bridge.Session?.IsHost == true && !item.LocallyOwned && item.RemoteOwner != WorldSyncIds.NoOwner)
            {
                var accepted = item.AcceptedVehicleCondition;
                if (accepted == null || accepted.OwnerPlayerId != item.RemoteOwner) return null;
                var copy = VehicleConditionStreamPolicy.Copy(accepted);
                copy.OwnerPlayerId = ownerPlayerId; copy.Sequence = VehicleCondition.SnapshotSequence;
                return copy;
            }
            var state = ReadHostParkedCondition(item) ?? TryReadConditionState(item);
            if (state != null)
            {
                state.OwnerPlayerId = ownerPlayerId;
                state.Sequence = VehicleCondition.SnapshotSequence;
            }
            return state;
        }

        internal void SendFinalVehicleCondition(SessionManager session, SyncedItem item)
        {
            item.SentFinalCondition = null;
            if (!item.IsVehicle || item.Body == null || !item.LocallyOwned) return;
            OwnerBroadcastCondition(session, item, Time.unscaledTime, true);
        }

        private void OwnerBroadcastCondition(SessionManager session, SyncedItem item, float now, bool final = false)
        {
            var message = TryReadConditionState(item);
            if (message == null) return;

            bool keepAlive = now >= item.NextConditionKeepAliveAt;
            bool changed = !item.HasSentCondition
                || item.LastCondPressure != message.TirePressure || item.LastCondDrivetrain != message.DrivetrainDamage
                || item.LastCondFlags != message.Flags || item.LastCondAvailability != message.Availability
                || item.LastCondHFL != message.HealthFL || item.LastCondHFR != message.HealthFR
                || item.LastCondHRL != message.HealthRL || item.LastCondHRR != message.HealthRR;
            if (!final && !changed && !keepAlive) return;
            if (!final && now < item.NextConditionTickAt && !changed) return;

            item.NextConditionTickAt = now + 1f;
            if (keepAlive) item.NextConditionKeepAliveAt = now + ConditionKeepAliveSeconds;
            item.HasSentCondition = true;
            item.LastCondAvailability = message.Availability;
            item.LastCondPressure = message.TirePressure; item.LastCondDrivetrain = message.DrivetrainDamage; item.LastCondFlags = message.Flags;
            item.LastCondHFL = message.HealthFL; item.LastCondHFR = message.HealthFR; item.LastCondHRL = message.HealthRL; item.LastCondHRR = message.HealthRR;

            message.OwnerPlayerId = session.LocalPlayerId;
            message.Sequence = item.OutConditionSequence = VehicleStateStreamPolicy.NextSequence(item.OutConditionSequence);
            if (final && !session.IsHost) item.SentFinalCondition = VehicleConditionStreamPolicy.Copy(message);
            session.SendWorldMessage(message, Channel.ReliableOrdered);
        }

        /// <summary>Host gate: only the authenticated current owner may drive a vehicle's condition.</summary>
        public bool TryAcceptGuestVehicleCondition(VehicleCondition message, byte playerId)
        {
            if (!VehicleConditionStreamPolicy.IsValid(message) || message.OwnerPlayerId != playerId
                || playerId == 0 || message.Sequence == VehicleCondition.SnapshotSequence
                || !_items.Items.TryGetValue(message.VehicleId, out var item)
                || !item.IsVehicle || item.Body == null || item.LocallyOwned || _items.IsLocalPlayerDriving(item))
                return false;
            return item.RemoteOwner == playerId;
        }

        /// <summary>Apply an owner's condition onto a locally non-owned vehicle.</summary>
        public bool ApplyVehicleCondition(VehicleCondition message)
        {
            var session = _bridge.Session;
            if (session == null || (session.IsHost ? session.State != SessionState.Hosting : session.State != SessionState.Connected)
                || !_items.Items.TryGetValue(message.VehicleId, out var item) || !item.IsVehicle || item.Body == null) return false;
            if (!_vehicleConditionStreams.Receive(message, session.IsHost,
                    item.LocallyOwned || _items.IsLocalPlayerDriving(item), item.RemoteOwner)) return false;

            var previousApply = item.ApplyingVehicleCondition;
            try
            {
                EnsureConditionProbe(item);
                // PUNCTURE may synchronously enter a native health reader before
                // the successful application is committed to accepted state.
                item.ApplyingVehicleCondition = VehicleConditionStreamPolicy.Copy(message);
                ApplyConditionValues(item, message);
                item.AcceptedVehicleCondition = VehicleConditionStreamPolicy.Copy(message);
                ClearConditionRelease(item);
                ClearParkedCondition(item);
                ApplyNativeTirePressure(item, received: true);
                return true;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("VehicleWorldSync: condition apply failed for " + item.Path + ": " + e.Message);
                return false;
            }
            finally { item.ApplyingVehicleCondition = previousApply; }
        }

        internal bool TryReadWheelHealthInput(PlayMakerFSM fsm, int wheel, out byte health)
        {
            health = 0;
            var session = _bridge.Session;
            if (!GuestSaveGuard.ProtectWorld || session == null || session.IsHost || session.State != SessionState.Connected) return false;
            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || item.Body == null || wheel < 0 || wheel > 3 || item.WheelConditionFsms == null
                    || !ReferenceEquals(item.WheelConditionFsms[wheel], fsm) || !fsm.transform.IsChildOf(item.Body.transform)) continue;
                bool localDriver = item.LocallyOwned || _items.IsLocalPlayerDriving(item);
                if (VehicleConditionClaimPolicy.TryGetHealth(ReadClaimedCondition(item), wheel, out health)) return true;
                if (VehicleConditionStreamPolicy.TryGetObserverHealth(item.ApplyingVehicleCondition ?? item.AcceptedVehicleCondition,
                    item.Id, localDriver, item.RemoteOwner, wheel, out health)) return true;
                return item.ApplyingVehicleCondition == null && ReferenceEquals(item.ParkedConditionBody, item.Body)
                    && VehicleConditionStreamPolicy.TryGetParkedObserverHealth(item.ParkedVehicleCondition,
                        item.Id, localDriver, item.RemoteOwner, wheel, out health);
            }
            return false;
        }

        internal bool TryReadGearboxConditionInput(PlayMakerFSM fsm, out byte damage)
        {
            damage = 0;
            var session = _bridge.Session;
            if (!GuestSaveGuard.ProtectWorld || session == null || session.IsHost || session.State != SessionState.Connected) return false;
            foreach (var item in _items.Items.Values)
            {
                if (!item.IsVehicle || !ReferenceEquals(item.DrivetrainDamageFsm, fsm) || !ConditionFsmLive(item, fsm)) continue;
                bool local = item.LocallyOwned || _items.IsLocalPlayerDriving(item);
                if (VehicleConditionClaimPolicy.TryGetDrivetrain(ReadClaimedCondition(item), out damage)) return true;
                if (VehicleConditionStreamPolicy.TryGetObserverDrivetrainDamage(item.ApplyingVehicleCondition ?? item.AcceptedVehicleCondition,
                    item.Id, local, item.RemoteOwner, out damage)) return true;
                return item.ApplyingVehicleCondition == null && ReferenceEquals(item.ParkedConditionBody, item.Body)
                    && VehicleConditionStreamPolicy.TryGetParkedObserverDrivetrainDamage(item.ParkedVehicleCondition,
                        item.Id, local, item.RemoteOwner, out damage);
            }
            return false;
        }

        private void ForgetConditionPlayer(byte playerId)
        {
            _vehicleConditionStreams.ForgetPlayer(playerId);
            _wheelPuncturePolicy.ForgetPlayer(playerId);
            foreach (var item in _items.Items.Values)
                if (item.AcceptedVehicleCondition?.OwnerPlayerId == playerId) item.AcceptedVehicleCondition = null;
        }

        internal void ClearConditionStreams()
        {
            _vehicleConditionStreams.Clear();
            foreach (var item in _items.Items.Values)
            {
                item.AcceptedVehicleCondition = null; item.OutConditionSequence = 0; item.HasSentCondition = false;
                item.NextConditionTickAt = 0; item.NextConditionKeepAliveAt = 0;
                ClearParkedCondition(item);
                ClearClaimedCondition(item);
                ClearConditionRelease(item);
                item.NativeTirePressure = null; item.NextTirePressureProbeAt = 0;
                item.ConditionProbeBody = null; item.NextConditionProbeAt = 0;
                item.ConditionReadyMask = 0; item.ConditionNeedsApply = false;
                System.Array.Clear(item.NextWheelRimRetryAt, 0, item.NextWheelRimRetryAt.Length);
            }
        }

        internal static void ClearParkedCondition(SyncedItem item)
        {
            item.ParkedVehicleCondition = null;
            item.ParkedConditionBody = null;
        }

        internal void OnConditionMotionAccepted(SyncedItem item, byte previousOwner, ItemTransform motion)
        {
            if (!item.IsVehicle) return;
            ClearClaimedCondition(item);
            ClearConditionRelease(item);
            if (!motion.IsFinal)
            {
                ClearParkedCondition(item);
                if (previousOwner != motion.OwnerPlayerId) item.AcceptedVehicleCondition = null;
                return;
            }
            // An unowned final (including resync poses) cannot approve an older
            // driver's report. Only the established owner's accepted release can.
            if (previousOwner != motion.OwnerPlayerId) return;
            ClearParkedCondition(item);
            if (item.Body == null || item.LocallyOwned || _items.IsLocalPlayerDriving(item)) return;
            var parked = VehicleConditionStreamPolicy.CaptureReleasedCondition(item.AcceptedVehicleCondition, previousOwner, motion);
            if (parked == null) return;
            item.ParkedVehicleCondition = parked;
            item.ParkedConditionBody = item.Body;
            SyncEventLog.Record("vehicle-condition-parked", item.Id.ToString("X8") + " owner " + motion.OwnerPlayerId);
        }

        private bool ApplyWheelDiscrete(SyncedItem item, int wheel, byte desiredFlags)
        {
            var fsm = item.WheelConditionFsms![wheel];
            if (fsm == null) return false;

            bool wantPuncture = (desiredFlags & WheelPunctureFlags[wheel]) != 0;
            bool wantRim = (desiredFlags & WheelRimFlags[wheel]) != 0;
            if (wantRim) return ApplyNativeWheelRim(item, wheel, fsm);
            // Diff against the wheel FSM's ACTUAL state, not our apply bookkeeping: a local
            // FSM that drifted (or a joiner whose own save already has a flat) would satisfy
            // stale bookkeeping and every keepalive would be a no-op — the drift never heals.
            string current = ReadWheelState(fsm);
            bool hadPuncture = current == "Flat friction";
            bool hadRim = current == "Rim friction";

            try
            {
                if (wantPuncture && !hadPuncture) fsm.SendEvent("PUNCTURE");
                else if (!wantPuncture && (hadPuncture || hadRim)) fsm.SendEvent("FIXED");
                return true;
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug($"VehicleWorldSync: wheel {WheelSuffixes[wheel]} event failed on {item.Path}: {e.Message}");
                return false;
            }
        }

        private static string ReadWheelState(PlayMakerFSM? fsm)
        {
            if (fsm == null) return string.Empty;
            try { return fsm.Fsm != null ? (fsm.Fsm.ActiveStateName ?? string.Empty) : string.Empty; }
            catch { return string.Empty; }
        }

        private static byte ClampByte(float value) => (byte)Mathf.Clamp(value, 0f, 255f);
    }
}
