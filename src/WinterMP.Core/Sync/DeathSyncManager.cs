using System;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core;
using WinterMP.Core.Session;
using WinterMP.Net;
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
        private bool _permadeathApplied;
        private bool _respawnWatchScheduled;
        private float _respawnWatchUntil;
        private PlayMakerFSM? _deathFsm;

        internal bool IsLocalDead => _deathHook.LocalDeathActive;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void OnSceneChanged()
        {
            _deathHook.Reset();
            _deathFsm = null;
            _respawnWatchScheduled = false;
            _respawnWatchUntil = 0f;
            RefreshHostPermadeathFromSave();
            TryApplyGuestPermadeath();
        }

        public void ScheduleRespawnWatch()
        {
            _respawnWatchScheduled = true;
            _respawnWatchUntil = Time.unscaledTime + RespawnWatchSeconds;
        }

        private void Update()
        {
            var session = SessionManager.Instance;
            if (session == null) return;
            if (session.State != SessionState.Hosting && session.State != SessionState.Connected)
            {
                // Between sessions. The next connection may be a different host (or the
                // same host with permadeath toggled), so the one-shot flag must re-arm —
                // this component lives on the persistent plugin root and outlives sessions.
                _permadeathApplied = false;
                return;
            }

            try
            {
                _deathHook.Probe(session);
                // Retried here (idempotent) because the loopback dev flow connects with no
                // scene change afterwards, so OnSceneChanged alone would never apply it.
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

        public void OnRemoteRespawn(PlayerRespawn respawn)
        {
            var session = SessionManager.Instance;
            if (session == null) return;

            if (respawn.PlayerId == session.LocalPlayerId) return;

            session.SetPlayerDead(respawn.PlayerId, dead: false);
            session.ApplyPlayerRespawnPose(respawn.PlayerId, respawn.Position.ToUnity(), respawn.Rotation.ToUnity());

            string name = session.ResolvePlayerName(respawn.PlayerId);
            session.AddSystemChat("* " + name + " respawned");
        }

        private void HandleDeathReport(byte playerId, byte cause)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return;

            bool wipe = session.PermanentDeathEnabled;
            byte flags = wipe ? PlayerDeathEventFlags.PermadeathWipe : (byte)0;

            session.SetPlayerDead(playerId, dead: true);
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
            bool isSelf = evt.PlayerId == session.LocalPlayerId;

            if (!isSelf)
                session.SetPlayerDead(evt.PlayerId, dead: true);

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
            _deathHook.NotifyGroupDeathStarted();

            try
            {
                if (!fsm.gameObject.activeSelf)
                    fsm.gameObject.SetActive(true);
                fsm.enabled = true;

                SetCauseBool(fsm, cause);
                string evt = CauseToEvent(cause);
                if (FsmHook.EnsureRemoteEntry(fsm, "State 3"))
                    FsmHook.FireRemoteEntry(fsm, "State 3");
                fsm.SendEvent(evt);
                WinterMPPlugin.Log.LogInfo("DeathSync: triggered local group death (" + evt + ").");
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
            if (!_respawnWatchScheduled || session.PermanentDeathEnabled) return;
            if (Time.unscaledTime > _respawnWatchUntil)
            {
                // Watch expired without the death FSM ever idling (patched FSM or a missed
                // idle window). Report the respawn anyway: leaving the hook flagged "dead"
                // would silently drop every FUTURE death report from this player, and the
                // next real death re-marks us dead if this guess is wrong.
                _respawnWatchScheduled = false;
                session.SetPlayerDead(session.LocalPlayerId, dead: false);
                _deathHook.NotifyRespawned(session);
                WinterMPPlugin.Log.LogWarning("DeathSync: respawn watch timed out — reported respawn anyway.");
                return;
            }

            var fsm = _deathFsm ?? LocateDeathFsm();
            if (fsm == null) return;

            if (fsm.enabled && !string.IsNullOrEmpty(fsm.ActiveStateName))
                return;

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
            if (session == null || session.IsHost || _permadeathApplied) return;
            if (session.State != SessionState.Connected) return;

            _permadeathApplied = true;
            bool hostPermadeath = session.PermanentDeathEnabled;

            if (PermadeathSettings.TryRead(out bool local) && local == hostPermadeath)
                return;

            if (PermadeathSettings.TryWrite(hostPermadeath))
                PermadeathSettings.SyncAchievementFsm(hostPermadeath);

            session.AddSystemChat(hostPermadeath
                ? "* Host uses PERMADEATH — your save flag was set to match"
                : "* Host disabled permadeath — your save flag was set to match");
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
                    if (path.IndexOf(DeathObjectPath, StringComparison.OrdinalIgnoreCase) >= 0)
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
                "Drown", "DrunkDrown", "Fire", "Gasolinefire", "Electrocute", "Hypothermia",
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

        // Events are State 3's transition set (dump-23268598). State 3 has no crash/vehicle
        // transition (the CORRIS/FITTAN/... events enter elsewhere), so Accident approximates
        // as RUNOVER — the closest vehicular death screen reachable from here. Burn has a
        // cause bool but no event of its own → FIRE. Smoking has neither → default.
        private static string CauseToEvent(byte cause)
        {
            switch (cause)
            {
                case DeathCause.Hunger: return "HUNGER";
                case DeathCause.Thirst: return "THIRST";
                case DeathCause.Urine: return "URINE";
                case DeathCause.Stress: return "STRESS";
                case DeathCause.RunOver: return "RUNOVER";
                case DeathCause.Drown: return "DROWN";
                case DeathCause.Fire: return "FIRE";
                case DeathCause.Electrocute: return "ELECTROCUTE";
                case DeathCause.Hypothermia: return "HYPOTHERMIA";
                case DeathCause.Murder: return "MURDER";
                case DeathCause.Train: return "TRAIN";
                case DeathCause.Accident: return "RUNOVER";
                case DeathCause.Sewage: return "SEWAGE";
                case DeathCause.Carbon: return "CARBON";
                case DeathCause.Pto: return "PTO";
                case DeathCause.CutterBlade: return "CUTTERBLADE";
                case DeathCause.InJail: return "INJAIL";
                case DeathCause.PissTv: return "TV";
                case DeathCause.Burn: return "FIRE";
                default: return "FATIGUE";
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
                case DeathCause.Fire: return "Fire";
                case DeathCause.Electrocute: return "Electrocute";
                case DeathCause.Hypothermia: return "Hypothermia";
                case DeathCause.Murder: return "Murder";
                case DeathCause.Train: return "Train";
                case DeathCause.Accident: return "Crash";
                case DeathCause.Sewage: return "Sewage";
                case DeathCause.Carbon: return "Carbon";
                case DeathCause.Pto: return "PTO";
                case DeathCause.CutterBlade: return "CutterBlade";
                case DeathCause.InJail: return "InJail";
                case DeathCause.PissTv: return "PissTV";
                case DeathCause.Burn: return "Burn";
                case DeathCause.Smoking: return "Smoking";
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
