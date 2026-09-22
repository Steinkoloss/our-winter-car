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
    internal sealed partial class ItemWorldSync
    {
        private readonly WiringReplica _wiringReplica = new WiringReplica();
        private readonly Dictionary<uint, WiringPublication> _wiringPublications = new Dictionary<uint, WiringPublication>();
        private readonly HashSet<uint> _wiringFailures = new HashSet<uint>();
        private float _nextWiringPoll, _nextWiringKeepalive;

        internal WiringState? ReadWiringInputs(uint id) => _wiringReplica.Get(id);

        internal void OnWiringState(WiringState state)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.State != SessionState.Connected) return;
            var old = _wiringReplica.Get(state.SourceId);
            if (!_wiringReplica.Receive(state))
            {
                SyncEventLog.Record("wiring-rejected", state.SourceId + " revision=" + state.Revision);
                return;
            }
            if (old == null || old.Revision != state.Revision)
                SyncEventLog.Record("wiring-received", state.SourceId + " revision=" + state.Revision + " flags=" + state.Flags);
        }

        internal IEnumerable<WiringState> BuildWiringStates()
        {
            var states = new List<WiringState>();
            var session = SessionManager.Instance;
            var profile = SyncCatalog.GuestEngineInputs;
            if (session == null || !session.IsHost || profile == null) return states;
            var lookup = new EngineSourceLookup();
            foreach (var rule in profile.Wires)
            {
                // Native actions are synchronous, but completion is polled. A
                // snapshot or retry receipt must not publish a partial write.
                if (_wireInstalling != null && _wireBefore != null && rule.Id == _wireBefore.SourceId)
                {
                    states.Add(new WiringState { SourceId = _wireBefore.SourceId, Revision = _wireBefore.Revision, Flags = _wireBefore.Flags });
                    continue;
                }
                byte flags = 0;
                try
                {
                    flags = CaptureEngineWire(lookup, rule);
                    if (rule.Connection != null && (flags & WiringState.Available) != 0
                        && _wireConnection != null && WireConnectable(_wireConnection)) flags |= WiringState.Connectable;
                    _wiringFailures.Remove(rule.Id);
                }
                catch (Exception error)
                {
                    if (_wiringFailures.Add(rule.Id))
                    {
                        WinterMPPlugin.Log.LogWarning("WorldSync: wiring input unavailable " + rule.Name + ": " + error.Message);
                        SyncEventLog.Record("wiring-unavailable", rule.Name + " " + error.Message);
                    }
                }
                if (!_wiringPublications.TryGetValue(rule.Id, out var publication))
                {
                    publication = new WiringPublication();
                    _wiringPublications.Add(rule.Id, publication);
                }
                // A join snapshot must leave the update pending for existing guests.
                states.Add(publication.Observe(rule.Id, flags));
            }
            return states;
        }

        private static byte CaptureEngineWire(EngineSourceLookup lookup, EngineWireData rule)
        {
            var source = lookup.Find(rule.Path);
            if (source == null || !source.activeInHierarchy) return 0;
            if (ScenePath.Of(source.transform) != rule.Path) throw new InvalidOperationException("Wire database path changed.");
            PlayMakerFSM? data = null;
            foreach (var candidate in source.GetComponents<PlayMakerFSM>())
                if (candidate.FsmName == rule.Fsm)
                {
                    if (data != null) throw new InvalidOperationException("Ambiguous wire database.");
                    data = candidate;
                }
            if (data == null) throw new InvalidOperationException("Missing wire database.");
            var installed = data.FsmVariables.FindFsmBool("Installed");
            var bolted = data.FsmVariables.FindFsmBool("Bolted");
            if (installed == null || rule.SupportsBolted && bolted == null)
                throw new InvalidOperationException("Missing wire input.");
            // Load game and Tightness? contain intermediate values. Publish only
            // after native saved load and bolt calculation have both completed.
            if (!data.enabled || !data.Fsm.Initialized || !data.Fsm.Started || data.ActiveStateName != rule.SettledState) return 0;
            return (byte)(WiringState.Available | (installed.Value ? WiringState.Installed : 0)
                | (rule.SupportsBolted && bolted!.Value ? WiringState.Bolted : 0));
        }

        private void ProcessWiring(SessionManager session)
        {
            if (Time.unscaledTime < _nextWiringPoll) return;
            _nextWiringPoll = Time.unscaledTime + .2f;
            ProcessWireConnection(session);
            if (!session.IsHost) return;
            bool keepalive = Time.unscaledTime >= _nextWiringKeepalive;
            foreach (var state in BuildWiringStates())
            {
                var publication = _wiringPublications[state.SourceId];
                if (!keepalive && !publication.NeedsBroadcast) continue;
                session.SendWorldMessage(state, Channel.ReliableOrdered);
                if (publication.NeedsBroadcast)
                    SyncEventLog.Record("wiring-published", state.SourceId + " revision=" + state.Revision + " flags=" + state.Flags);
                publication.MarkBroadcast(state.Revision);
            }
            if (keepalive) _nextWiringKeepalive = Time.unscaledTime + 5f;
        }

        private void ClearWiring()
        {
            ClearWireConnection();
            _wiringReplica.Clear(); _wiringPublications.Clear(); _wiringFailures.Clear();
            _nextWiringPoll = _nextWiringKeepalive = 0;
        }
    }
}
