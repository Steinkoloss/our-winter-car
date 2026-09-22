using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class PissAreaSync
    {
        private sealed class Admission
        {
            internal RemotePlayer Player = null!;
            internal ulong Token;
            internal uint Sequence;
            internal float LastAccepted = float.NegativeInfinity;
        }
        private readonly Dictionary<byte, Admission> _admissions = new Dictionary<byte, Admission>();
        private readonly Dictionary<uint, float> _challenges = new Dictionary<uint, float>();
        private uint _epoch, _revision, _guestSequence;
        private ulong _guestAdmission;
        private bool _executing;
        private float _pending, _lastGuestSend;
        private byte _pendingArea;

        private static bool Active(SessionManager? session) => session != null && Application.loadedLevelName == "GAME"
            && (session.State == SessionState.Hosting || session.State == SessionState.Connected);
        private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        private static bool Finite(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
        private static ulong NewToken() { ulong token = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0); return token == 0 ? 1 : token; }
        private static bool ValidAdmissions(PissAreaAdmission[] entries)
        {
            if (entries == null || entries.Length > 254) return false;
            var seen = new bool[256];
            foreach (var entry in entries) {
                if (entry.Actor == 0 || entry.Actor == 255 || entry.Token == 0 || seen[entry.Actor]) return false;
                seen[entry.Actor] = true;
            }
            return true;
        }
        private void RefreshAdmissions(SessionManager session)
        {
            var live = new HashSet<byte>();
            foreach (var player in session.Players) {
                if (player.PlayerId == 0 || player.PlayerId == 255) continue;
                live.Add(player.PlayerId);
                if (!_admissions.TryGetValue(player.PlayerId, out var entry) || !ReferenceEquals(entry.Player, player))
                    _admissions[player.PlayerId] = new Admission { Player = player, Token = NewToken() };
            }
            var departed = new List<byte>();
            foreach (byte actor in _admissions.Keys) if (!live.Contains(actor)) departed.Add(actor);
            foreach (byte actor in departed) _admissions.Remove(actor);
        }
        private PissAreaAdmission[] CaptureAdmissions()
        {
            var entries = new List<PissAreaAdmission>();
            foreach (var pair in _admissions) entries.Add(new PissAreaAdmission {
                Actor = pair.Key, Token = pair.Value.Token, HighWater = pair.Value.Sequence });
            entries.Sort((a, b) => a.Actor.CompareTo(b.Actor));
            return entries.ToArray();
        }
        private void RememberSent(PissAreaState state)
        {
            _hasLast = true;
            byte[] values = { state.Scale1, state.Scale2, state.Scale3, state.Scale4, state.Scale5 };
            for (int i = 0; i < 5; i++) _lastSent[i] = values[i];
            _nextHostTickAt = Time.unscaledTime + HostTickSeconds;
            _nextKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;
        }

        // Transport authentication is performed by SessionManager; the claimed actor
        // must still match that transport identity and this exact connected player.
        internal bool OnIntent(PissAreaIntent request, byte authenticatedActor)
        {
            var session = SessionManager.Instance;
            if (_executing || !Active(session) || !session!.IsHost || request == null || !Ready || !_nativeReady
                || _revision == uint.MaxValue || request.Actor != authenticatedActor || request.Actor == 0 || request.Actor == 255
                || request.Epoch != _epoch || _epoch == 0 || request.Sequence == 0
                || request.Area < 1 || request.Area > 5 || (request.Action != 1 && request.Action != 2)
                || !Finite(request.Contribution) || request.Contribution <= 0 || request.Contribution > .25f
                || !_admissions.TryGetValue(authenticatedActor, out var entry) || request.Admission != entry.Token
                || request.Sequence <= entry.Sequence || !_challenges.TryGetValue(request.Revision, out float issued)) return false;
            float now = Time.unscaledTime;
            if (!Finite(now) || now < issued || now - issued > 1f || now - entry.LastAccepted < .1f) return false;
            bool connected = false;
            foreach (var player in session.Players) if (ReferenceEquals(player, entry.Player)) connected = true;
            var actor = entry.Player;
            float age = now - actor.LastTransformTime;
            if (!connected || actor.IsDead || actor.LastTransformTime <= 0 || !Finite(age) || age < 0 || age > .6f
                || (actor.MoveState & (PlayerMoveState.Driving | PlayerMoveState.Passenger)) != 0
                || !Finite(actor.Position) || NearestArea(actor.Position) != request.Area) return false;
            // PLAYER/Rain::RaycastAndFog supplies RoofCheck via a world-up 100m
            // ray on layer 27. Recompute for the guest, never use the host's roof flag.
            if (!Physics.Raycast(actor.Position, Vector3.up, 100f, 1 << 27)) return false;
            // Do not interrupt vanilla initialization or saving, or publish a mixed snapshot.
            if (_logic!.ActiveStateName == "State 4") return false;
            for (int i = 0; i < 5; i++) {
                var scale = _stains[i]!.transform.localScale;
                if (!Finite(scale) || scale.x < 0 || scale.x > MaximumScales[i]) return false;
            }
            int index = request.Area - 1;
            uint acceptedSequence = request.Sequence;
            var target = _stains[index]!.transform;
            Vector3 before = target.localScale;
            float after = Mathf.Clamp(before.x + request.Contribution, 0, MaximumScales[index]);
            if (after <= before.x) return false;
            var change = _logic.FsmVariables.FindFsmFloat("ChangeScale")!;
            float oldChange = change.Value;
            bool sameHostTarget = _logic.ActiveStateName == "State 2"
                && _logic.FsmVariables.FindFsmGameObject("CurrentArea")?.Value == _stains[index];
            PissAreaState? state;
            _executing = true;
            try {
                target.localScale = new Vector3(after, after, 1);
                // Preserve a host-local SetScale already scheduled for LateUpdate.
                if (sameHostTarget) change.Value = Mathf.Clamp(oldChange + (after - before.x), 0, MaximumScales[index]);
                state = BuildState();
                if (state == null) throw new InvalidOperationException("Stain snapshot unavailable after native apply.");
                entry.Sequence = acceptedSequence; entry.LastAccepted = now;
                state.Admissions = CaptureAdmissions();
            }
            catch (Exception e) {
                try { if (target.localScale.x != before.x) target.localScale = before; }
                catch (Exception rollback) { WinterMPPlugin.Log.LogError("PissArea rollback failed: " + rollback); }
                change.Value = oldChange;
                WinterMPPlugin.Log.LogError("PissArea contribution rejected: " + e);
                return false;
            }
            finally { _executing = false; }
            // A send failure cannot roll back a potentially delivered result. Sequence
            // remains committed; the periodic absolute snapshot repairs delivery.
            try { session.SendWorldMessage(state!, Channel.ReliableOrdered); RememberSent(state!); }
            catch (Exception e) { WinterMPPlugin.Log.LogError("PissArea result send failed: " + e); }
            return true;
        }
        private byte NearestArea(Vector3 position)
        {
            float closest = 400f; byte selected = 0;
            for (int i = 0; i < 5; i++) {
                float distance = (_stains[i]!.transform.position - position).sqrMagnitude;
                if (distance < closest) { closest = distance; selected = (byte)(i + 1); }
            }
            // Vanilla's closest-object list includes four NOPISS points. Those points
            // win over a nearby room stain; they are not additional writable stains.
            foreach (var point in _logic!.gameObject.GetComponentsInChildren<Transform>(true))
                if (point.name == "NOPISS" && (point.position - position).sqrMagnitude <= closest) return 0;
            return selected;
        }
        private void CaptureGuestContribution()
        {
            var session = SessionManager.Instance;
            if (!Active(session) || session!.IsHost || _executing || !Ready || !_nativeReady || _guestAdmission == 0 || _epoch == 0
                || _playerPiss == null || _logic!.ActiveStateName != "State 2") return;
            byte action = _playerPiss.ActiveStateName == "Full power" ? (byte)1 : _playerPiss.ActiveStateName == "State 4" ? (byte)2 : (byte)0;
            if (action == 0) { _pending = 0; return; }
            var current = _logic.FsmVariables.FindFsmGameObject("CurrentArea")?.Value;
            byte area = 0;
            for (int i = 0; i < 5; i++) if (_stains[i] == current) area = (byte)(i + 1);
            if (area == 0) { _pending = 0; return; }
            float amount = _logic.FsmVariables.FindFsmFloat("Addition")!.Value * Time.deltaTime;
            if (!Finite(amount) || amount <= 0 || amount > .25f) { _pending = 0; return; }
            if (area != _pendingArea) { _pending = 0; _pendingArea = area; }
            _pending = Math.Min(.25f, _pending + amount);
            if (Time.unscaledTime - _lastGuestSend < .15f || _guestSequence == uint.MaxValue) return;
            var request = new PissAreaIntent { Epoch = _epoch, Revision = _revision, Admission = _guestAdmission,
                Sequence = ++_guestSequence, Actor = session.LocalPlayerId, Area = area, Action = action, Contribution = _pending };
            _pending = 0; _lastGuestSend = Time.unscaledTime;
            session.SendWorldMessage(request, Channel.ReliableOrdered);
        }
    }
}
