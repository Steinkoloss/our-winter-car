using System;
using UnityEngine;
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

        private static readonly string[] DeathStartStates = { "State 3", "Take photo" };
        private static readonly string[] RespawnStates = { "State 2" };

        private readonly System.Collections.Generic.HashSet<PlayMakerFSM> _hooked =
            new System.Collections.Generic.HashSet<PlayMakerFSM>();

        private float _nextProbeAt;
        private bool _localDeathActive;
        private bool _deathReported;
        private byte _reportSequence;

        public bool LocalDeathActive => _localDeathActive;

        public void Reset(bool preserveDeath = false, bool clearHooks = true)
        {
            if (clearHooks) _hooked.Clear();
            _nextProbeAt = 0f;
            if (!preserveDeath)
            {
                _localDeathActive = false;
                _deathReported = false;
                _reportSequence = 0;
            }
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
            var session = SessionManager.Instance;
            session?.RetirePassengerSeat(session.LocalPlayerId);
        }

        public void NotifyRespawned(SessionManager session)
        {
            if (!_localDeathActive) return;

            var player = GameObject.Find("PLAYER");
            if (player == null) return;

            session.RetirePassengerSeat(session.LocalPlayerId);
            _localDeathActive = false;
            _deathReported = false;

            var controller = player.GetComponent<CharacterController>();
            Vector3 feet = PlayerPoseReader.ReadFeetPosition(player.transform, controller);
            Quaternion rot = PlayerPoseReader.ReadLookRotation(player.transform);
            // Peers stay dead until a real player is ready; a timeout or an inactive
            // cached death-screen pose cannot establish a respawn.
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

            if (!string.Equals(path, DeathObjectPath, StringComparison.Ordinal))
                return;

            // This graph starts inactive. Deserialize its actions before activation
            // so State 3 can release the seat before native controller destruction.
            if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);

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
            WinterMPPlugin.Log.LogInfo("DeathSync: hooked Activate Dead Body at " + path + ".");
            if (fsm.gameObject.activeInHierarchy && fsm.enabled && fsm.Fsm.Started
                && !string.IsNullOrEmpty(fsm.ActiveStateName))
                OnDeathStarted(fsm, session);
        }

        private void OnDeathStarted(PlayMakerFSM fsm, SessionManager session)
        {
            if (DeathSyncManager.Instance != null && DeathSyncManager.Instance.SuppressLocalDeathReport)
                return;

            _localDeathActive = true;
            session.RetirePassengerSeat(session.LocalPlayerId);
            if (_deathReported) return;

            _deathReported = true;
            if (!session.PermanentDeathEnabled) DeathSyncManager.Instance?.ScheduleRespawnWatch();
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
            if (!session.CanAcceptRespawn) return;
            if (!_localDeathActive) return;

            // State 2 is the newspaper, followed by save and MainMenu. Keep watching
            // through scene changes until a live GAME player has movement again.
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
            if (TryBool(fsm, "Sewage")) return DeathCause.Sewage;
            if (TryBool(fsm, "Carbon")) return DeathCause.Carbon;
            if (TryBool(fsm, "PTO")) return DeathCause.Pto;
            if (TryBool(fsm, "CutterBlade")) return DeathCause.CutterBlade;
            if (TryBool(fsm, "InJail")) return DeathCause.InJail;
            if (TryBool(fsm, "PissTV")) return DeathCause.PissTv;
            if (TryBool(fsm, "Burn")) return DeathCause.Burn;
            if (TryBool(fsm, "Smoking")) return DeathCause.Smoking;
            return DeathCause.Unknown;
        }

        private static bool TryBool(PlayMakerFSM fsm, string name)
        {
            var variable = fsm.FsmVariables.FindFsmBool(name);
            return variable != null && variable.Value;
        }
    }
}
