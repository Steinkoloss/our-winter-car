using System;
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
        private void ProcessMooseChop(SessionManager session)
        {
            RefreshMooseChop();
            var m = _moose;
            if (m == null || m.Failed || _mooseFailed || m.Root == null) return;
            try
            {
                if (session.IsHost)
                {
                    if (Time.unscaledTime < m.NextSend) return;
                    m.NextSend = Time.unscaledTime + (m.Root.gameObject.activeInHierarchy ? .25f : 1f);
                    var state = new MooseCorpseState { Corpse = m.Epoch, Revision = ++m.Revision, Dead = m.Root.gameObject.activeInHierarchy };
                    if (state.Dead)
                    {
                        state.FrontPieces = checked((byte)m.Sections[0].Pieces.Value); state.RearPieces = checked((byte)m.Sections[1].Pieces.Value);
                        state.Positions = new NetVector3[m.Bodies.Length]; state.Rotations = new NetQuaternion[m.Bodies.Length];
                        for (int i = 0; i < m.Bodies.Length; i++)
                        {
                            state.Positions[i] = m.Bodies[i].Body.position.ToNet(); state.Rotations[i] = m.Bodies[i].Body.rotation.ToNet();
                        }
                    }
                    if (!MooseChopPolicy.Valid(state)) throw new InvalidOperationException("Native corpse state changed.");
                    session.SendWorldMessage(state, Channel.ReliableOrdered);
                    return;
                }
                if (m.Received != null) ApplyMooseCorpse(m);
                if (m.Pending != null)
                {
                    if (Time.unscaledTime > m.PendingUntil || m.Received == null || !m.Received.Dead
                        || m.Received.Corpse != m.Pending.Corpse || m.Sections[m.Pending.Section].Pieces.Value != m.Pending.ExpectedPieces)
                        m.Pending = null;
                    else if (Time.unscaledTime >= m.RetryAt)
                    {
                        m.RetryAt = Time.unscaledTime + .8f;
                        session.SendWorldMessage(m.Pending, Channel.ReliableOrdered);
                    }
                }
                if (Time.unscaledTime < m.DeathUntil && Time.unscaledTime >= m.DeathRetryAt && m.Received?.Dead != true)
                {
                    m.DeathRetryAt = Time.unscaledTime + 1;
                    session.SendWorldMessage(new NpcDeathReport { NetId = StableHash.Fnv1a32("mover:" + SyncCatalog.MooseChop!["mover"]),
                        PlayerId = session.LocalPlayerId }, Channel.ReliableOrdered);
                }
            }
            catch (Exception e) { FailMooseChop(e); }
        }

        private void QueueMooseChop(byte section)
        {
            var m = _moose; var session = SessionManager.Instance;
            if (m == null || session == null || !m.Guest || m.Failed) return;
            try
            {
                if (m.Received?.Dead == true && m.Pending == null)
                {
                    var request = new MooseChopIntent { PlayerId = session.LocalPlayerId, Corpse = m.Received.Corpse,
                        Section = section, ExpectedPieces = (byte)m.Sections[section].Pieces.Value };
                    if (MooseChopPolicy.Valid(request))
                    {
                        m.Pending = request; m.RetryAt = 0; m.PendingUntil = Time.unscaledTime + 3;
                        SyncEventLog.Record("moose-chop-request", "section " + section + " pieces " + request.ExpectedPieces);
                    }
                }
                FsmHook.FireRemoteEntry(m.Sections[section].Fsm, SyncCatalog.MooseChop!["cooldown"]);
            }
            catch (Exception e) { FailMooseChop(e); }
        }

        internal void OnMooseChop(MooseChopIntent request)
        {
            var session = SessionManager.Instance; var m = _moose; var c = SyncCatalog.MooseChop;
            if (session?.IsHost != true || m == null || c == null || m.Failed || _meatFailed || _meatFactory == null
                || m.Root == null || !MooseChopPolicy.Valid(request)) return;
            try
            {
                var section = m.Sections[request.Section];
                bool fresh = GamblingSync.TryPlayerPosition(session, request.PlayerId, out var position);
                bool ready = section.Fsm.ActiveStateName == c["idle"] && section.Fsm.enabled
                    && _meatFactory.ActiveStateName == SyncCatalog.MooseMeat!["factoryIdle"];
                bool accepted = MooseChopPolicy.CanChop(request, m.Epoch, section.Pieces.Value, m.Root.gameObject.activeInHierarchy,
                    ready, fresh, (position - section.Fsm.transform.position).sqrMagnitude);
                SyncEventLog.Record("moose-chop-intent", "player " + request.PlayerId + " section " + request.Section
                    + " expected " + request.ExpectedPieces + " actual " + section.Pieces.Value + " accepted " + accepted);
                if (!accepted) return;
                var old = section.Spawnpoint.Value;
                try
                {
                    // Native Spawnpoint is the HOST axe collider. A guest's meat
                    // belongs at the validated section, never at that distant axe.
                    section.Spawnpoint.Value = section.Fsm.gameObject;
                    FsmHook.FireRemoteEntry(section.Fsm, c["pieces"]);
                }
                finally { section.Spawnpoint.Value = old; }
                if (section.Pieces.Value != request.ExpectedPieces + 1)
                    throw new InvalidOperationException("Native chop did not consume exactly one piece.");
                m.NextSend = 0;
            }
            catch (Exception e) { FailMooseChop(e); }
        }

        internal void OnMooseCorpse(MooseCorpseState state)
        {
            if (SessionManager.Instance?.IsHost != false) return;
            RefreshMooseChop();
            var m = _moose;
            if (m == null || m.Failed || !MooseChopPolicy.CanReceive(m.Received, state)) return;
            m.Received = state;
            if (state.Dead) m.DeathUntil = 0;
        }

        private static void ApplyMooseCorpse(MooseBinding m)
        {
            var state = m.Received!;
            if (m.Root == null) return;
            if (!state.Dead)
            {
                m.Root.gameObject.SetActive(false);
                return;
            }
            // Preserve the guest's native animal for disconnect. Only the host
            // runs CarHit's detach/activate/destroy sequence.
            m.Root.parent = null;
            if (m.Mover != null) m.Mover.gameObject.SetActive(false);
            foreach (var body in m.Bodies) body.Body.isKinematic = true;
            for (int i = 0; i < m.Bodies.Length; i++)
            {
                var body = m.Bodies[i].Body;
                body.position = state.Positions[i].ToUnity(); body.rotation = state.Rotations[i].ToUnity();
            }
            m.Sections[0].Pieces.Value = state.FrontPieces; m.Sections[1].Pieces.Value = state.RearPieces;
            m.Root.gameObject.SetActive(true);
        }

        private void QueueMooseDeath()
        {
            var m = _moose;
            if (m == null || !m.Guest || m.Failed || m.Hit == null) return;
            m.DeathUntil = Time.unscaledTime + 5; m.DeathRetryAt = 0;
            FsmHook.FireRemoteEntry(m.Hit, SyncCatalog.MooseChop!["hitIdle"]);
        }

        internal bool OnMooseDeathReport(SessionManager session, NpcDeathReport request)
        {
            var c = SyncCatalog.MooseChop;
            if (c == null || request.NetId != StableHash.Fnv1a32("mover:" + c["mover"])) return false;
            var m = _moose;
            if (!session.IsHost || m == null || m.Failed || m.Root == null || m.Root.gameObject.activeInHierarchy) return true;
            try
            {
                if (m.Hit == null || !m.Hit.gameObject.activeInHierarchy || !m.Hit.enabled || !m.Hit.Fsm.Started
                    || !GamblingSync.TryPlayerPosition(session, request.PlayerId, out var position)
                    || (position - m.Hit.transform.position).sqrMagnitude > 150 * 150) return true;
                FsmHook.FireRemoteEntry(m.Hit, c["death"]); m.NextSend = 0;
                SyncEventLog.Record("moose-death-report", "player " + request.PlayerId);
            }
            catch (Exception e) { FailMooseChop(e); }
            return true;
        }
    }
}
