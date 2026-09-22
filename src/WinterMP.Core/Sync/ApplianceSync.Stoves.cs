using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ApplianceSync
    {
        private sealed class StoveKnob
        {
            internal PlayMakerFSM Fsm = null!;
            internal FsmFloat Rotation = null!, Data = null!, Step = null!;
            internal FsmState Up = null!, Down = null!;
            internal FsmStateAction[] UpActions = null!, DownActions = null!;
            internal FsmStateAction Reset = null!, Render = null!, Read = null!;
            internal float OldRotation, OldData;
        }
        private sealed class StoveBinding
        {
            internal readonly StoveKnob[] Knobs = new StoveKnob[4];
            internal readonly GameObject[] Grills = new GameObject[4], Burns = new GameObject[4];
            internal readonly FsmStateAction[][] Ignitions = new FsmStateAction[4][];
            internal readonly FsmFloat[] FireHazards = new FsmFloat[4];
            internal readonly float[] OldHeat = new float[4], OldFireHazards = new float[4];
            internal readonly bool[] OldGrills = new bool[4], OldBurns = new bool[4];
            internal readonly FsmSuppressor Pause = new FsmSuppressor();
            internal readonly StoveIntentLedger Ledger = new StoveIntentLedger();
            internal bool Guest, OldFuse;
            internal GameObject Light = null!;
            internal EllipsoidParticleEmitter Smoke = null!;
            internal bool OldLight, OldSmoke;
            internal ushort Sequence;
        }

        private static bool StoveWaiting(Oven oven) => SyncCatalog.Stoves?.Paths.Contains(oven.ContainerPath) == true && oven.Stove == null;

        private static void CaptureStove(Oven oven, ApplianceState state)
        {
            var stove = oven.Stove;
            if (stove == null || oven.StoveFailed) return;
            if (stove.Light.activeSelf) state.Flags |= ApplianceState.FlagStoveLight;
            if (stove.Smoke.emit) state.Flags |= ApplianceState.FlagStoveSmoke;
            for (int i = 0; i < 4; i++)
            {
                state.StoveHeat[i] = oven.Heats[i]!.Value;
                state.StoveRotation[i] = stove.Knobs[i].Rotation.Value;
                if (stove.Grills[i].activeSelf) state.GrillMask |= (byte)(1 << i);
                if (stove.Burns[i].activeSelf) state.BurnMask |= (byte)(1 << i);
            }
            if (!StovePolicy.Valid(state)) throw new InvalidOperationException("Invalid native stove state.");
            var old = oven.StoveObserved;
            state.StoveRevision = old == null ? 1 : StovePolicy.Same(old, state) ? old.StoveRevision : unchecked(old.StoveRevision + 1);
            if (state.StoveRevision == 0) state.StoveRevision = 1;
            oven.StoveObserved = state;
        }

        private void ReceiveStove(Oven oven, ApplianceState state)
        {
            if (oven.StoveFailed || !StovePolicy.CanReceive(oven.StoveReceived, state)
                || (oven.StovePending != null && !StovePolicy.CanReceive(oven.StovePending, state))) return;
            if (oven.Stove == null) { oven.StovePending = state; return; }
            try
            {
                var stove = oven.Stove;
                for (int i = 0; i < 4; i++)
                {
                    var knob = stove.Knobs[i]; knob.Rotation.Value = state.StoveRotation[i];
                    knob.Render.OnEnter(); knob.Read.OnEnter();
                    oven.Heats[i]!.Value = state.StoveHeat[i];
                    stove.Grills[i].SetActive((state.GrillMask & (1 << i)) != 0);
                    stove.Burns[i].SetActive((state.BurnMask & (1 << i)) != 0);
                }
                if (oven.Fuse != null) oven.Fuse.Value = (state.Flags & ApplianceState.FlagFuseOk) != 0;
                stove.Light.SetActive((state.Flags & ApplianceState.FlagStoveLight) != 0);
                stove.Smoke.emit = (state.Flags & ApplianceState.FlagStoveSmoke) != 0;
                oven.StoveReceived = state; oven.StovePending = null;
                ApplyIgnition(oven, state);
            }
            catch (Exception e) { FailStove(oven, e); }
        }

        private void QueueStoveTurn(Oven oven, byte plate, byte direction)
        {
            try
            {
                var session = SessionManager.Instance; var stove = oven.Stove;
                if (session == null || session.IsHost || session.PlayerCount == 0 || stove == null || oven.StoveFailed
                    || oven.StoveReceived == null) return;
                session.SendWorldMessage(new StoveKnobIntent { ApplianceId = oven.Id, PlayerId = session.LocalPlayerId,
                    Plate = plate, Direction = direction, Sequence = ++stove.Sequence }, Channel.ReliableOrdered);
            }
            catch (Exception e) { FailStove(oven, e); }
        }

        public void OnStoveKnob(StoveKnobIntent request)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost || !StovePolicy.Valid(request)) return;
            EnsureBuilt();
            foreach (var oven in _ovens)
            {
                if (oven.Id != request.ApplianceId) continue;
                Locate(oven); var stove = oven.Stove;
                if (stove == null || oven.StoveFailed) return;
                try
                {
                    var knob = stove.Knobs[request.Plate];
                    bool nearby = GamblingSync.TryPlayerPosition(session, request.PlayerId, out var position)
                        && (position - knob.Fsm.transform.position).sqrMagnitude <= 9
                        && knob.Fsm.gameObject.activeInHierarchy && knob.Fsm.enabled;
                    if (!stove.Ledger.Accept(request, nearby)) return;
                    ValidateKnob(knob, SyncCatalog.Stoves!);
                    // Execute the native arithmetic, wrapping and render/read actions
                    // without placing the remote host's mouse in the knob's input loop.
                    (request.Direction == 0 ? knob.DownActions : knob.UpActions)[0].OnEnter();
                    if (knob.Rotation.Value < -360 || knob.Rotation.Value > 330) knob.Reset.OnEnter();
                    knob.Render.OnEnter(); knob.Read.OnEnter();
                    HostBroadcastIfChanged(session, oven, true);
                    SyncEventLog.Record("stove-turn", oven.ContainerPath + " plate=" + request.Plate + " player=" + request.PlayerId);
                }
                catch (Exception e) { FailStove(oven, e); }
                return;
            }
        }

        private static void RestoreStove(Oven oven)
        {
            var s = oven.Stove;
            if (s == null || !s.Guest) return;
            try
            {
                foreach (var k in s.Knobs)
                {
                    if (k.Fsm == null) continue;
                    k.Up.Actions = k.UpActions; k.Down.Actions = k.DownActions;
                    k.Rotation.Value = k.OldRotation; k.Render.OnEnter(); k.Data.Value = k.OldData;
                }
                if (oven.Sim != null)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        oven.Heats[i]!.Value = s.OldHeat[i];
                        s.FireHazards[i].Value = s.OldFireHazards[i];
                        if (s.Grills[i] != null) s.Grills[i].SetActive(s.OldGrills[i]);
                        if (s.Burns[i] != null) s.Burns[i].SetActive(s.OldBurns[i]);
                    }
                    if (oven.Fuse != null) oven.Fuse.Value = s.OldFuse;
                    if (s.Light != null) s.Light.SetActive(s.OldLight);
                    if (s.Smoke != null) s.Smoke.emit = s.OldSmoke;
                }
            }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("Stove state restore failed: " + e.Message); }
            finally { s.Pause.Restore(); }
        }

        private static void FailStove(Oven oven, Exception error)
        {
            if (oven.StoveFailed) return;
            oven.StoveFailed = true;
            WinterMPPlugin.Log.LogWarning("Stove sync disabled for " + oven.ContainerPath + ": " + error.Message);
        }
    }
}
