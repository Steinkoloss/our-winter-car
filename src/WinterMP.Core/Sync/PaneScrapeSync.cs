using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    /// <summary>One-pane adapter. Native equipment, contact and glass execution stay on the game thread.</summary>
    internal sealed partial class PaneScrapeSync : MonoBehaviour, IPaneScrapeHost
    {
        internal static PaneScrapeSync? Instance { get; private set; }
        private PaneScrapeAuthority? _authority;
        private PaneScrapeReplica? _replica;
        private ScraperLease? _lease;
        private PaneScrapeUpdate? _received;
        private uint _epoch, _sequence;
        private float _nextScan, _nextPublish, _nextKeepAlive;
        private bool _failed, _admitted, _handlingAction;
        private ScraperAction? _request;
        private uint _pickupSequence;
        private bool _resumePickup;
        private string _lastAction = "none";

        private SessionManager? Session => SessionManager.Instance;
        private bool Active => Session != null && (Session.State == SessionState.Hosting || Session.State == SessionState.Connected)
            && Application.loadedLevelName == "GAME";
        private void Awake()
        {
            Instance = this;
            if (Session != null) Session.PlayerLeft += OnPlayerLeft;
        }
        private void OnPlayerLeft(RemotePlayer player) { _lease?.Revoke(player.PlayerId); }
        private void OnDestroy()
        {
            if (Session != null) Session.PlayerLeft -= OnPlayerLeft;
            ResetSession(); if (Instance == this) Instance = null;
        }
        internal void ResetSession()
        {
            bool cancelPickup = _pickupSequence != 0 || _resumePickup;
            _authority = null; _replica = null; _lease = null; _received = null;
            _epoch = 0; _sequence = 0; _pickupSequence = 0; _resumePickup = false;
            _request = null; _failed = false; _admitted = false;
            _nextScan = _nextPublish = _nextKeepAlive = 0;
            // A gated hand has selected the tool but has not run native pickup.
            // Cancel before removing its remote entry, or the next session sees
            // a phantom local holder and rejects an otherwise valid guest pickup.
            try { if (cancelPickup) CancelPickup(); }
            catch (Exception e) { WinterMPPlugin.Log.LogError("PaneScrape pickup cleanup: " + e); }
            finally { RestoreBindings(); }
        }
        private void Fail(Exception e)
        {
            if (!_failed) WinterMPPlugin.Log.LogError("PaneScrape disabled: " + e);
            _failed = true; _lease?.Revoke(_lease.Holder);
            // Retain the stroke replacement while connected: failure must not enable guest speculation.
        }
        private void Update()
        {
            try
            {
                if (!Active) { if (_pane != null) ResetSession(); return; }
                if (_failed) return;
                if (_pane == null)
                {
                    if (Time.unscaledTime < _nextScan) return;
                    _nextScan = Time.unscaledTime + 1;
                    if (!Bind()) return;
                }
                if (Session!.IsHost && _authority == null)
                {
                    _epoch = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0);
                    if (_epoch == 0) _epoch = 1;
                    _lease = new ScraperLease(_epoch, _tool!.Id);
                    _authority = new PaneScrapeAuthority(_vehicle!.Id, _epoch, this);
                    _replica = new PaneScrapeReplica(_vehicle.Id, _epoch, Session.LocalPlayerId);
                    _admitted = true;
                }
                if (_resumePickup)
                {
                    _resumePickup = false;
                    if (WaitingForPickup && _received?.Holder == Session!.LocalPlayerId)
                    {
                        _pickupGate!.AllowOnce = true;
                        FsmHook.FireRemoteEntry(_hand!, _c!["pickup"]);
                    }
                    else { Send(ScraperOperation.Drop); CancelPickup(); }
                }
                if (Session.IsHost && _lease != null)
                {
                    if (!ReadActor(_lease.Holder, out _, out _, out _, out _)) _lease.Revoke(_lease.Holder);
                    _lease.Expire(Time.unscaledTime);
                    if (Time.unscaledTime >= _nextPublish)
                    {
                        _nextPublish = Time.unscaledTime + .25f;
                        Publish(255, 0, false, PaneScrapeStatus.Accepted);
                        foreach (var player in Session.Players) Publish(player.PlayerId, 0, false, PaneScrapeStatus.Accepted);
                    }
                }
                if (_epoch != 0 && Time.unscaledTime >= _nextKeepAlive && LocalHasTool())
                {
                    _nextKeepAlive = Time.unscaledTime + .15f;
                    Send(ScraperOperation.KeepAlive);
                }
            }
            catch (Exception e) { Fail(e); }
        }
        private void LateUpdate()
        {
            if (!Active || _failed || Session!.IsHost || _replica?.Current == null || _cutoff == null) return;
            try { _cutoff.Value = _replica.Current.Cutoff; _material!.SetFloat("_Cutoff", _cutoff.Value); }
            catch (Exception e) { Fail(e); }
        }
        internal bool OwnsPane(uint id) => Active && !_failed && _vehicle != null && _vehicle.Id == id
            && (Session!.IsHost ? _authority != null : _replica?.Current != null);
        internal bool AllowsToolMotion(uint id, byte actor) => Active && !_failed && _tool != null && _tool.Id == id
            && actor != 255 && _lease != null && _lease.Holder == actor
            && _lease.Age(Time.unscaledTime) >= 0 && _lease.Age(Time.unscaledTime) <= PaneScrapePolicy.MaximumObservationAgeSeconds;

        private bool LocalHasTool() => _picked?.Value != null && _tool?.Body != null
            && (_picked.Value == _tool.Body.gameObject || _picked.Value.transform.IsChildOf(_tool.Body.transform));
        private bool WaitingForPickup => LocalHasTool() && _hand != null && _c != null && _hand.ActiveStateName == _c["pickup"];
        private bool Parked => _vehicle?.Body != null && _vehicle.Body.velocity.sqrMagnitude <= .01f
            && _vehicle.Body.angularVelocity.sqrMagnitude <= .01f;
        private uint Send(ScraperOperation operation)
        {
            if (!Active || _failed || !_admitted || _epoch == 0 || _tool == null || _vehicle == null || _eye == null
                || (!LocalHasTool() && !(operation == ScraperOperation.Drop && _received?.Holder == Session!.LocalPlayerId))
                || _sequence == uint.MaxValue) return 0;
            var request = new ScraperAction { Epoch = _epoch, Actor = Session!.LocalPlayerId, Sequence = ++_sequence,
                VehicleId = _vehicle.Id, Pane = PaneScrapeIntent.Windshield, ToolId = _tool.Id,
                Operation = operation, Eye = _eye.position.ToNet(), Direction = _eye.forward.ToNet() };
            if (operation == ScraperOperation.Pickup) _pickupSequence = request.Sequence;
            if (Session.IsHost) OnAction(request, Session.LocalPlayerId);
            else Session.SendWorldMessage(request, Channel.ReliableOrdered);
            return request.Sequence;
        }
        internal void OnAction(ScraperAction action, byte actor)
        {
            if (!Active || !Session!.IsHost || _failed || _authority == null || _lease == null) return;
            // Glass actions, actor-local effects and result transport may all
            // reenter. Keep the outer lease/request/result isolated until its
            // publication returns; a denied nested attempt cannot become valid
            // on replay. The authority guard also covers direct native entry.
            if (_handlingAction || _authority.IsExecuting) { _lease.RejectAttempt(actor, action); return; }
            _handlingAction = true;
            try
            {
                bool live = ReadActor(actor, out var feet, out var yaw, out _, out bool outside);
                bool eye = live && ValidEye(action, feet, yaw);
                RaycastHit hit = default;
                bool rayHit = eye && FirstHit(action, 1f, out hit);
                bool pickup = eye && rayHit && IsTool(hit.collider);
                if (actor != Session.LocalPlayerId && LocalHasTool()) pickup = false;
                bool target = action.VehicleId == _vehicle!.Id && action.Pane == PaneScrapeIntent.Windshield;
                bool granted = false;
                if (target) granted = _lease.Apply(actor, action, live && eye && outside && Parked, pickup, Time.unscaledTime);
                else _lease.RejectAttempt(actor, action);
                if (action.Operation != ScraperOperation.KeepAlive)
                {
                    _lastAction = action.Operation + " actor=" + actor + " seq=" + action.Sequence
                        + " live=" + live + " eye=" + eye + " pickup=" + pickup + " granted=" + granted
                        + " firstHit=" + (rayHit && hit.collider != null ? ScenePath.Of(hit.collider.transform) : "none")
                        + " feet=" + feet + " eyePos=" + action.Eye + " dir=" + action.Direction;
                    Diagnostics.SyncEventLog.Record("pane-action", _lastAction);
                    WinterMPPlugin.Log.LogInfo("PaneScrape " + _lastAction);
                }

                var status = target ? PaneScrapeStatus.InvalidEquipment : PaneScrapeStatus.WrongPane;
                if (action.Operation == ScraperOperation.Stroke)
                {
                    // Even a lease-denied stroke reaches the seam, whose own authenticated
                    // epoch/sequence checks consume the attempt without native mutation.
                    _request = granted ? action : null;
                    var decision = _authority.Decide(actor, action.Intent());
                    status = decision.Status;
                    _request = null;
                    if (_authority.Faulted) throw new InvalidOperationException("Native pane authority faulted.");
                }
                else if (granted) status = PaneScrapeStatus.Accepted;
                Publish(actor, action.Sequence, action.Operation == ScraperOperation.Stroke, status);
            }
            catch (Exception e) { _request = null; Fail(e); }
            finally { _handlingAction = false; }
        }
        private void Publish(byte actor, uint sequence, bool decision, PaneScrapeStatus status)
        {
            var snapshot = _authority!.Capture();
            var update = new PaneScrapeUpdate { Epoch = snapshot.Epoch, VehicleId = snapshot.VehicleId,
                Revision = snapshot.Revision, Cutoff = snapshot.Cutoff, ToolId = _tool!.Id,
                Holder = _lease!.Holder, Equipped = _lease.Equipped, Actor = actor, Sequence = sequence,
                HighWater = _lease.Seen(actor), IsDecision = decision, Status = status };
            OnUpdate(update);
            Session!.SendWorldMessage(update, Channel.ReliableOrdered);
        }
        // Only the selected, handshaken host reaches this method through SessionManager.
        internal void OnUpdate(PaneScrapeUpdate update)
        {
            if (!Active || _failed) return;
            try
            {
                if (_pane == null && !Bind()) return; // periodic snapshot retries discovery
                if (_vehicle!.Id != update.VehicleId || _tool!.Id != update.ToolId || update.Epoch == 0) return;
                if (_epoch != 0 && _epoch != update.Epoch) return; // epoch changes only at session/world reset
                // A rejected initial snapshot must not pin its epoch and block valid admission.
                var replica = _replica ?? new PaneScrapeReplica(_vehicle.Id, update.Epoch, Session!.LocalPlayerId);
                bool effect = false;
                bool accepted = update.IsDecision ? replica.ReceiveDecision(true, update.Decision(), out effect)
                    : replica.ReceiveSnapshot(true, update.Snapshot());
                if (!accepted) return;
                _epoch = update.Epoch; _replica = replica;
                _received = update;
                if (update.Actor == Session!.LocalPlayerId)
                {
                    _sequence = Math.Max(_sequence, update.HighWater);
                    if (!_admitted && update.Sequence == 0 && !update.IsDecision)
                    {
                        _replica!.ResumePersonalEffectsAfter(update.HighWater);
                        _admitted = true;
                    }
                    if (_pickupSequence != 0 && update.Sequence == _pickupSequence && !update.IsDecision)
                    {
                        _pickupSequence = 0;
                        if (update.Status == PaneScrapeStatus.Accepted && update.Holder == Session.LocalPlayerId && WaitingForPickup)
                            _resumePickup = true;
                        else
                        {
                            if (update.Status == PaneScrapeStatus.Accepted && update.Holder == Session.LocalPlayerId) Send(ScraperOperation.Drop);
                            CancelPickup();
                        }
                    }
                }
                if (!Session!.IsHost) { _cutoff!.Value = _replica!.Current!.Cutoff; _material!.SetFloat("_Cutoff", _cutoff.Value); }
                if (effect && _admitted) ApplyPersonalEffects();
            }
            catch (Exception e) { Fail(e); }
        }
        public float ReadWindshieldCutoff() => _cutoff!.Value;
        public void ApplyWindshieldDelta()
        {
            // Execute only the two validated native actions, never transition into Sound.
            _glassActions![0].OnEnter(); _glassActions[1].OnEnter();
        }
        public PaneScrapeHostContext ReadContext(byte actor)
        {
            bool live = ReadActor(actor, out var feet, out var yaw, out float age, out bool outside);
            RaycastHit hit = default;
            bool contact = live && _request != null && ValidEye(_request, feet, yaw)
                && FirstHit(_request, PaneScrapePolicy.ContactMetres, out hit) && hit.collider == _pane;
            float distance = contact ? hit.distance : float.PositiveInfinity;
            return new PaneScrapeHostContext { ActorPresent = live, ActorAlive = live, ActorOutside = outside,
                PoseAgeSeconds = age, PaneAvailable = _pane != null && _pane.enabled && _pane.gameObject.activeInHierarchy,
                VehicleParked = Parked,
                EquipmentActor = _lease!.Holder, EquipmentEpoch = _epoch,
                EquippedToolId = _lease.Equipped ? _tool!.Id : 0, IsIceScraper = _lease.Equipped && _tool?.Body != null,
                EquipmentAgeSeconds = _lease.Age(Time.unscaledTime), ContactVehicleId = _vehicle!.Id,
                ContactPane = 1, ContactAgeSeconds = 0, ContactDistance = distance, Unobstructed = contact };
        }
        private bool ReadActor(byte actor, out Vector3 feet, out Quaternion yaw, out float age, out bool outside)
        {
            feet = Vector3.zero; yaw = Quaternion.identity; age = float.PositiveInfinity; outside = false;
            if (actor == 255 || Session == null) return false;
            if (actor == Session.LocalPlayerId)
            {
                if (DeathSyncManager.Instance?.IsLocalDead == true || PlayerSyncManager.Instance == null
                    || !PlayerSyncManager.Instance.TryReadLocalPose(out feet, out yaw)) return false;
                age = 0;
                outside = _inside == null || !_inside.Value;
            }
            else
            {
                bool found = false;
                foreach (var player in Session.Players)
                {
                    if (player.PlayerId != actor) continue;
                    age = Time.unscaledTime - player.LastTransformTime;
                    if (player.IsDead || player.LastTransformTime <= 0 || age < 0 || age > .6f) return false;
                    feet = player.Position; yaw = player.Rotation;
                    outside = (player.MoveState & (PlayerMoveState.Driving | PlayerMoveState.Passenger)) == 0
                        && !Session.IsPassengerInVehicle(actor, _vehicle!.Id);
                    found = true; break;
                }
                if (!found) return false;
            }
            if (_insideCollider != null && _insideCollider.bounds.Contains(feet + Vector3.up * .4f)) outside = false;
            return Finite(feet) && PaneScrapePolicy.Finite(age);
        }
        private static bool Finite(Vector3 v) => PaneScrapePolicy.Finite(v.x) && PaneScrapePolicy.Finite(v.y) && PaneScrapePolicy.Finite(v.z);
        private static bool ValidEye(ScraperAction action, Vector3 feet, Quaternion yaw)
        {
            var eye = action.Eye.ToUnity(); var direction = action.Direction.ToUnity();
            if (!Finite(eye) || !Finite(direction)) return false;
            var offset = eye - feet;
            if (offset.y < .2f || offset.y > 2.1f || offset.x * offset.x + offset.z * offset.z > .36f
                || Mathf.Abs(direction.sqrMagnitude - 1) > .02f) return false;
            var horizontal = new Vector3(direction.x, 0, direction.z);
            return horizontal.sqrMagnitude < .01f || Vector3.Angle(yaw * Vector3.forward, horizontal) <= 45;
        }
        private static bool FirstHit(ScraperAction action, float distance, out RaycastHit hit)
            => Physics.Raycast(action.Eye.ToUnity(), action.Direction.ToUnity(), out hit, distance, Physics.DefaultRaycastLayers);
        private bool IsTool(Collider? collider) => collider != null && _tool?.Body != null
            && (collider.attachedRigidbody == _tool.Body || collider.transform.IsChildOf(_tool.Body.transform));
    }
}
