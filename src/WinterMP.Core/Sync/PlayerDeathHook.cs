using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Observes <c>Systems/Death :: Activate Dead Body</c> (dump-23268598) for local player
    /// death and respawn (PLAN.md §4.4).
    /// </summary>
    internal sealed class PlayerDeathHook
    {
        private const string DeathObjectPath = "Systems/Death";
        private const string DeathFsmName = "Activate Dead Body";

        private static readonly string[] DeathStartStates = { "Take photo" };
        private static readonly string[] RespawnStates = { "State 2" };

        private readonly System.Collections.Generic.HashSet<PlayMakerFSM> _hooked =
            new System.Collections.Generic.HashSet<PlayMakerFSM>();

        private PlayMakerFSM? _deathFsm;
        private float _nextProbeAt;
        private bool _localDeathActive;
        private bool _deathReported;
        private byte _reportSequence;

        public bool LocalDeathActive => _localDeathActive;

        public void Reset()
        {
            _hooked.Clear();
            _deathFsm = null;
            _nextProbeAt = 0f;
            _localDeathActive = false;
            _deathReported = false;
            _reportSequence = 0;
        }

        public void Probe(SessionManager session)
        {
            if (session == null) return;
            if (session.State != SessionState.Hosting && session.State != SessionState.Connected) return;
            if (Time.unscaledTime < _nextProbeAt) return;
            _nextProbeAt = Time.unscaledTime + 3f;

            PlayMakerFSM[] fsms;
            try
            {
                fsms = Resources.FindObjectsOfTypeAll<PlayMakerFSM>();
            }
            catch
            {
                return;
            }

            for (int i = 0; i < fsms.Length; i++)
                TryHookDeathFsm(fsms[i], session);
        }

        public void NotifyGroupDeathStarted()
        {
            _localDeathActive = true;
            _deathReported = true;
        }

        public void NotifyRespawned(SessionManager session)
        {
            if (!_localDeathActive) return;

            _localDeathActive = false;
            _deathReported = false;

            var player = GameObject.Find("PLAYER");
            if (player == null) return;

            var controller = player.GetComponent<CharacterController>();
            var feet = PlayerPoseReader.ReadFeetPosition(player.transform, controller);
            var rot = PlayerPoseReader.ReadLookRotation(player.transform);

            session.SendPlayerProfileMessage(new PlayerRespawn
            {
                PlayerId = session.LocalPlayerId,
                Position = feet.ToNet(),
                Rotation = rot.ToNet(),
                Sequence = ++_reportSequence,
            });

            WinterMPPlugin.Log.LogInfo("DeathSync: local respawn reported.");
        }

        private void TryHookDeathFsm(PlayMakerFSM fsm, SessionManager session)
        {
            if (fsm == null || _hooked.Contains(fsm)) return;
            if (fsm.FsmName != DeathFsmName) return;

            string path;
            try
            {
                path = ScenePath.Of(fsm.transform);
            }
            catch
            {
                return;
            }

            if (path.IndexOf(DeathObjectPath, StringComparison.OrdinalIgnoreCase) < 0)
                return;

            bool hookedAny = false;
            for (int s = 0; s < DeathStartStates.Length; s++)
            {
                string stateName = DeathStartStates[s];
                if (!FsmHook.HasState(fsm, stateName)) continue;

                if (FsmHook.OnStateEnter(fsm, stateName, () => OnDeathStarted(fsm, session)))
                    hookedAny = true;
            }

            for (int s = 0; s < RespawnStates.Length; s++)
            {
                string stateName = RespawnStates[s];
                if (!FsmHook.HasState(fsm, stateName)) continue;

                if (FsmHook.OnStateEnter(fsm, stateName, () => OnRespawnStateEntered(fsm, session)))
                    hookedAny = true;
            }

            if (!hookedAny) return;

            _hooked.Add(fsm);
            _deathFsm = fsm;
            WinterMPPlugin.Log.LogInfo("DeathSync: hooked Activate Dead Body at " + path + ".");
        }

        private void OnDeathStarted(PlayMakerFSM fsm, SessionManager session)
        {
            if (DeathSyncManager.Instance != null && DeathSyncManager.Instance.SuppressLocalDeathReport)
                return;

            _localDeathActive = true;
            if (_deathReported) return;

            _deathReported = true;
            byte cause = ReadCause(fsm);

            if (session.IsHost)
            {
                DeathSyncManager.Instance?.OnLocalDeath(session.LocalPlayerId, cause);
            }
            else
            {
                session.SendPlayerProfileMessage(new PlayerDeathReport
                {
                    PlayerId = session.LocalPlayerId,
                    Cause = cause,
                    Sequence = ++_reportSequence,
                });
            }

            WinterMPPlugin.Log.LogInfo("DeathSync: local death detected (cause " + cause + ").");
        }

        private void OnRespawnStateEntered(PlayMakerFSM fsm, SessionManager session)
        {
            if (session.PermanentDeathEnabled) return;
            if (!_localDeathActive) return;

            // State 2 follows Permadeath SAVE — respawn completes once the death FSM idles.
            DeathSyncManager.Instance?.ScheduleRespawnWatch();
        }

        private static byte ReadCause(PlayMakerFSM fsm)
        {
            if (TryBool(fsm, "Fatigue")) return DeathCause.Fatigue;
            if (TryBool(fsm, "Hunger")) return DeathCause.Hunger;
            if (TryBool(fsm, "Thirst")) return DeathCause.Thirst;
            if (TryBool(fsm, "Urine")) return DeathCause.Urine;
            if (TryBool(fsm, "Stress")) return DeathCause.Stress;
            if (TryBool(fsm, "RunOver") || TryBool(fsm, "RunOverRally")) return DeathCause.RunOver;
            if (TryBool(fsm, "Drown") || TryBool(fsm, "DrunkDrown")) return DeathCause.Drown;
            if (TryBool(fsm, "Fire") || TryBool(fsm, "Gasolinefire")) return DeathCause.Fire;
            if (TryBool(fsm, "Electrocute")) return DeathCause.Electrocute;
            if (TryBool(fsm, "Hypothermia")) return DeathCause.Hypothermia;
            if (TryBool(fsm, "Murder")) return DeathCause.Murder;
            if (TryBool(fsm, "Train")) return DeathCause.Train;
            if (TryBool(fsm, "Crash")) return DeathCause.Accident;
            return DeathCause.Unknown;
        }

        private static bool TryBool(PlayMakerFSM fsm, string name)
        {
            var variable = fsm.FsmVariables.FindFsmBool(name);
            return variable != null && variable.Value;
        }
    }
}
