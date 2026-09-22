using System;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Host-authoritative death / respawn (PLAN.md §4.4). Permadeath sessions wipe
    /// everyone when one player dies; normal sessions respawn the dead player locally.
    /// </summary>
    public sealed class DeathSyncManager : MonoBehaviour
    {
        private const string DeathObjectPath = "Systems/Death";
        private const string DeathFsmName = "Activate Dead Body";
        private const float RespawnWatchSeconds = 120f;

        public static DeathSyncManager? Instance { get; private set; }

        /// <summary>True while applying a remote permadeath wipe locally.</summary>
        public bool SuppressLocalDeathReport { get; private set; }

        private readonly PlayerDeathHook _deathHook = new PlayerDeathHook();
        private bool _respawnWatchScheduled;
        private float _respawnWatchUntil;
        private bool _respawnWatchWarned;
        private PlayMakerFSM? _deathFsm;

        internal bool IsLocalDead => SessionManager.Instance?.PermadeathWipeActive == true || _deathHook.LocalDeathActive;

        private void Awake()
        {
            Instance = this;
            PermadeathSettings.Initialize();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        internal void ResetSession()
        {
            _deathHook.Reset(clearHooks: false);
            _respawnWatchScheduled = false;
            _respawnWatchUntil = 0;
            _respawnWatchWarned = false;
        }

        public void OnSceneChanged()
        {
            bool awaitingRecovery = _respawnWatchScheduled && _deathHook.LocalDeathActive;
            _deathHook.Reset(preserveDeath: awaitingRecovery || SessionManager.Instance?.PermadeathWipeActive == true);
            _deathFsm = null;
            if (!awaitingRecovery)
            {
                _respawnWatchScheduled = false;
                _respawnWatchUntil = 0f;
                _respawnWatchWarned = false;
            }
            RefreshHostPermadeathFromSave();
            TryApplyGuestPermadeath();
        }

        public void ScheduleRespawnWatch()
        {
            _respawnWatchScheduled = true;
            _respawnWatchUntil = Time.unscaledTime + RespawnWatchSeconds;
            _respawnWatchWarned = false;
        }

        private void Update()
        {
            var session = SessionManager.Instance;
            if (session == null) return;
            if (session.State != SessionState.Hosting && session.State != SessionState.Connected)
            {
                return;
            }

            try
            {
                _deathHook.Probe(session);
                // Loading can replace globals while the session remains connected.
                TryApplyGuestPermadeath();
                PollRespawn(session);
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning("DeathSync: " + e.Message);
            }
        }

        public void OnLocalDeath(byte playerId, byte cause)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return;

            HandleDeathReport(playerId, cause);
        }

        public void OnRemoteDeathReport(PlayerDeathReport report)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return;

            HandleDeathReport(report.PlayerId, report.Cause);
        }

        public void OnRemoteDeathEvent(PlayerDeathEvent evt)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;

            ApplyRemoteDeath(evt);
        }

        public bool OnRemoteRespawn(PlayerRespawn respawn)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.CanAcceptRespawn) return false;

            if (respawn.PlayerId == session.LocalPlayerId) return false;

            session.SetPlayerDead(respawn.PlayerId, dead: false);
            session.ApplyPlayerRespawnPose(respawn.PlayerId, respawn.Position.ToUnity(), respawn.Rotation.ToUnity());

            string name = session.ResolvePlayerName(respawn.PlayerId);
            session.AddSystemChat("* " + name + " respawned");
            return true;
        }

        private void HandleDeathReport(byte playerId, byte cause)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return;

            if (session.PermadeathWipeActive) return;
            bool wipe = session.PermanentDeathEnabled;
            if (wipe && !session.TryBeginPermadeathWipe()) return;
            byte flags = wipe ? PlayerDeathEventFlags.PermadeathWipe : (byte)0;

            session.SetPlayerDead(playerId, dead: true);
            if (wipe) session.RetirePassengersForGroupDeath();
            session.BroadcastProfileMessage(new PlayerDeathEvent
            {
                PlayerId = playerId,
                Cause = cause,
                Flags = flags,
            });

            string name = session.ResolvePlayerName(playerId);
            session.AddSystemChat(wipe
                ? "* " + name + " died — PERMADEATH (everyone dies)"
                : "* " + name + " died (" + DescribeCause(cause) + ")");

            if (wipe)
                ApplyRemoteDeath(new PlayerDeathEvent
                {
                    PlayerId = playerId,
                    Cause = cause,
                    Flags = PlayerDeathEventFlags.PermadeathWipe,
                });
        }

        private void ApplyRemoteDeath(PlayerDeathEvent evt)
        {
            var session = SessionManager.Instance;
            if (session == null) return;

            bool wipe = (evt.Flags & PlayerDeathEventFlags.PermadeathWipe) != 0;
            if (wipe && !session.PermadeathWipeActive && !session.TryBeginPermadeathWipe()) return;
            session.SetPlayerDead(evt.PlayerId, dead: true);
            if (wipe) session.RetirePassengersForGroupDeath();

            if (wipe && !_deathHook.LocalDeathActive)
            {
                session.AddSystemChat("* PERMADEATH — your run ends too");
                TriggerLocalGroupDeath(evt.Cause);
            }
        }

        private void TriggerLocalGroupDeath(byte cause)
        {
            var fsm = LocateDeathFsm();
            if (fsm == null)
            {
                WinterMPPlugin.Log.LogWarning("DeathSync: could not locate death FSM for group wipe.");
                return;
            }

            SuppressLocalDeathReport = true;
            try
            {
                if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
                if (fsm.Fsm.StartState != "Permadeath 2" || !FsmHook.HasState(fsm, "Delete saves 2")
                    || !FsmHook.HasState(fsm, "State 3") || !FsmHook.HasState(fsm, "Delete saves"))
                    throw new InvalidOperationException("Native permadeath startup graph changed.");
                // OnEnable starts the graph, including both the first delete stage
                // and State 3. Set the cause BEFORE activation and never enter it twice.
                SetCauseBool(fsm, cause);
                _deathHook.NotifyGroupDeathStarted();
                fsm.enabled = true;
                if (!fsm.gameObject.activeSelf) fsm.gameObject.SetActive(true);
                WinterMPPlugin.Log.LogInfo("DeathSync: triggered native group death (cause " + cause + ").");
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning("DeathSync: group death trigger failed: " + e.Message);
            }
            finally
            {
                SuppressLocalDeathReport = false;
            }
        }

        private void PollRespawn(SessionManager session)
        {
            if (!_respawnWatchScheduled || !session.CanAcceptRespawn) return;
            if (!_respawnWatchWarned && Time.unscaledTime > _respawnWatchUntil)
            {
                _respawnWatchWarned = true;
                WinterMPPlugin.Log.LogWarning("DeathSync: still waiting for native player recovery; keeping the player dead.");
            }

            if (Application.loadedLevelName != "GAME") return;
            var fsm = _deathFsm ?? LocateDeathFsm();
            if (fsm == null) return;

            if (fsm.gameObject.activeInHierarchy && fsm.enabled && !string.IsNullOrEmpty(fsm.ActiveStateName))
                return;

            var player = GameObject.Find("PLAYER");
            if (player == null) return;
            var controller = player.GetComponent<CharacterController>();
            var motor = player.GetComponent("CharacterMotor") as Behaviour;
            var input = player.GetComponent("FPSInputController") as Behaviour;
            if (controller == null || !controller.enabled || motor == null || !motor.enabled
                || input == null || !input.enabled) return;
            if (!session.IsHost && (PlayerSyncManager.Instance == null || !PlayerSyncManager.Instance.IsLocalSpawnReady)) return;

            _respawnWatchScheduled = false;
            session.SetPlayerDead(session.LocalPlayerId, dead: false);
            _deathHook.NotifyRespawned(session);
        }

        private void RefreshHostPermadeathFromSave()
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return;

            if (PermadeathSettings.TryRead(out bool enabled))
                session.SetPermanentDeathEnabled(enabled);
        }

        private void TryApplyGuestPermadeath()
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.State != SessionState.Connected) return;
            PermadeathSettings.ApplyGuest(session.PermanentDeathEnabled);
        }

        private PlayMakerFSM? LocateDeathFsm()
        {
            if (_deathFsm != null) return _deathFsm;

            PlayMakerFSM[] fsms;
            try
            {
                fsms = Resources.FindObjectsOfTypeAll<PlayMakerFSM>();
            }
            catch
            {
                return null;
            }

            for (int i = 0; i < fsms.Length; i++)
            {
                var fsm = fsms[i];
                if (fsm == null || fsm.FsmName != DeathFsmName) continue;

                try
                {
                    string path = ScenePath.Of(fsm.transform);
                    if (path == DeathObjectPath)
                    {
                        _deathFsm = fsm;
                        return fsm;
                    }
                }
                catch
                {
                    // skip destroyed objects
                }
            }

            return null;
        }

        private static void SetCauseBool(PlayMakerFSM fsm, byte cause)
        {
            ClearCauseBools(fsm);
            string? name = CauseToBoolName(cause);
            if (name == null) return;

            var variable = fsm.FsmVariables.FindFsmBool(name);
            if (variable != null)
                variable.Value = true;
        }

        private static void ClearCauseBools(PlayMakerFSM fsm)
        {
            // Full BoolVariables set of Systems/Death :: Activate Dead Body (dump-23268598) —
            // a stale bool left over from an earlier local death would misroute the wipe screen.
            string[] names =
            {
                "Fatigue", "Hunger", "Thirst", "Urine", "Stress", "RunOver", "RunOverRally",
                "Drown", "DrunkDrown", "Gasolinefire", "Electrocute", "Hypothermia",
                "Murder", "Train", "Crash", "Sewage", "Carbon", "PTO", "CutterBlade",
                "InJail", "PissTV", "Burn", "Smoking",
            };

            for (int i = 0; i < names.Length; i++)
            {
                var variable = fsm.FsmVariables.FindFsmBool(names[i]);
                if (variable != null)
                    variable.Value = false;
            }
        }

        private static string? CauseToBoolName(byte cause)
        {
            switch (cause)
            {
                case DeathCause.Fatigue: return "Fatigue";
                case DeathCause.Hunger: return "Hunger";
                case DeathCause.Thirst: return "Thirst";
                case DeathCause.Urine: return "Urine";
                case DeathCause.Stress: return "Stress";
                case DeathCause.RunOver: return "RunOver";
                case DeathCause.Drown: return "Drown";
                // Native Burn routes to FIRE; Gasolinefire selects the separate explosion screen.
                case DeathCause.Fire: return "Burn";
                case DeathCause.Electrocute: return "Electrocute";
                case DeathCause.Hypothermia: return "Hypothermia";
                case DeathCause.Murder: return "Murder";
                case DeathCause.Train: return "Train";
                // Remote vehicle context belongs to the dying player, not this peer.
                case DeathCause.Accident: return "RunOver";
                case DeathCause.Sewage: return "Sewage";
                case DeathCause.Carbon: return "Carbon";
                case DeathCause.Pto: return "PTO";
                case DeathCause.CutterBlade: return "CutterBlade";
                case DeathCause.InJail: return "InJail";
                case DeathCause.PissTv: return "PissTV";
                case DeathCause.Burn: return "Burn";
                case DeathCause.Smoking: return "Fatigue";
                default: return "Fatigue";
            }
        }

        private static string DescribeCause(byte cause)
        {
            switch (cause)
            {
                case DeathCause.Fatigue: return "fatigue";
                case DeathCause.Hunger: return "hunger";
                case DeathCause.Thirst: return "thirst";
                case DeathCause.Urine: return "bladder";
                case DeathCause.Stress: return "stress";
                case DeathCause.RunOver: return "run over";
                case DeathCause.Drown: return "drowned";
                case DeathCause.Fire: return "fire";
                case DeathCause.Electrocute: return "electrocution";
                case DeathCause.Hypothermia: return "hypothermia";
                case DeathCause.Murder: return "murder";
                case DeathCause.Train: return "train";
                case DeathCause.Accident: return "accident";
                case DeathCause.Sewage: return "sewage";
                case DeathCause.Carbon: return "carbon monoxide";
                case DeathCause.Pto: return "tractor PTO";
                case DeathCause.CutterBlade: return "cutter blade";
                case DeathCause.InJail: return "jail";
                case DeathCause.PissTv: return "TV accident";
                case DeathCause.Burn: return "burns";
                case DeathCause.Smoking: return "smoking";
                default: return "unknown";
            }
        }
    }
}
