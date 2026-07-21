using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Shared slot machines (pub + Peräpörtti station) as host-owned world state
    /// (COVERAGE-ROADMAP 1.1). Stake, reel RNG and cashout all mutate the single shared
    /// wallet, so per-client play desyncs: each peer rolls its own reels and the host's
    /// ~2 s <see cref="WalletState"/> erases a guest's local win. Here the <b>host</b> is
    /// authoritative — a guest's button press is relayed as a <see cref="GamblingIntent"/>,
    /// the host forces the matching button state on its own FSM (so the real
    /// stake/RNG/payout runs against the host wallet), and the resolved reels/credit
    /// broadcast back via <see cref="GamblingState"/> while the money rides WalletState.
    ///
    /// Mirrors <see cref="HeatSourceSync"/> (anyone-triggers intent + host scalar-state
    /// broadcast). All FSM/var lookups are best-effort and crash-contained.
    /// </summary>
    internal sealed class GamblingSync
    {
        private const float ProbeIntervalSeconds = 5f;
        private const float HostTickSeconds = 1f;
        private const float KeepAliveSeconds = 20f;
        private const float IntentCooldownSeconds = 0.4f;
        private const float IntentPlayerPoseMaxAgeSeconds = 2f;
        private const float IntentPlayerMaxDistance = 6f;

        // Button -> the FSM state that its USE press transitions into. Forcing this state on
        // the host (via FsmHook.FireRemoteEntry) runs the button's real actions (add credit,
        // roll, pay out) without a local player aiming at it.
        private const string StatePay = "Check money";
        private const string StateBet = "Compare";
        private const string StateStart = "Check added";
        private const string StateCashout = "State 2";
        private const string StateLock = "State 1";

        private sealed class Machine
        {
            public uint Id;
            public string ContainerPath = string.Empty;
            public bool LoggedFound;
            public Transform? Anchor;

            public PlayMakerFSM? PayMoney;   // Buttons/PayMoney :: Use
            public PlayMakerFSM? Bet;        // Buttons/Bet :: Use
            public PlayMakerFSM? Start;      // Buttons/Start :: Use  (reel RNG + payout)
            public PlayMakerFSM? Cashout;    // Buttons/Cashout :: Use
            public PlayMakerFSM? Lock1;      // LockButtos/LockRoll1 :: Use
            public PlayMakerFSM? Lock2;
            public PlayMakerFSM? Lock3;

            // Cached authoritative vars (from the Start FSM unless noted).
            public FsmFloat? Credit;         // AddedMoney
            public FsmFloat? Winnings;       // Win
            public FsmFloat? BetLevel;       // Bet FSM :: Bet
            public FsmBool? Roll1Locked;
            public FsmBool? Roll2Locked;
            public FsmBool? Roll3Locked;
            public FsmString? Reel1;         // Roll1
            public FsmString? Reel2;         // Roll2
            public FsmString? Reel3;         // Roll3

            public bool HostEntriesReady;
            public bool HooksInstalled;
            public float NextIntentAt;
            public ushort OutIntentSequence;

            // Change detection.
            public bool HasLast;
            public byte LastFlags;
            public ushort LastCredit;
            public byte LastBet;
            public byte LastR1, LastR2, LastR3;
            public int LastPayout;

            public bool AnyFsm => Start != null || PayMoney != null || Cashout != null;
        }

        private readonly List<Machine> _machines = new List<Machine>();
        private readonly Dictionary<uint, Machine> _byId = new Dictionary<uint, Machine>();
        private readonly Dictionary<byte, ushort> _lastIntentSequences = new Dictionary<byte, ushort>();
        private bool _built;
        private float _nextProbeAt;
        private float _nextHostTickAt;
        private float _nextKeepAliveAt;

        public void Clear()
        {
            _machines.Clear();
            _byId.Clear();
            _lastIntentSequences.Clear();
            _built = false;
            _nextProbeAt = 0f;
            _nextHostTickAt = 0f;
            _nextKeepAliveAt = 0f;
        }

        public void Update(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            EnsureBuilt();

            if (Time.unscaledTime >= _nextProbeAt)
            {
                _nextProbeAt = Time.unscaledTime + ProbeIntervalSeconds;
                for (int i = 0; i < _machines.Count; i++) LocateMachine(_machines[i]);
            }

            if (!session.IsHost) return;
            if (Time.unscaledTime < _nextHostTickAt) return;
            _nextHostTickAt = Time.unscaledTime + HostTickSeconds;

            bool keepAlive = Time.unscaledTime >= _nextKeepAliveAt;
            if (keepAlive) _nextKeepAliveAt = Time.unscaledTime + KeepAliveSeconds;

            for (int i = 0; i < _machines.Count; i++)
                HostBroadcastIfChanged(session, _machines[i], keepAlive);
        }

        /// <summary>Host: re-broadcast every machine on the next tick (slot state isn't in the join snapshot).</summary>
        public void ForceBroadcast()
        {
            _nextHostTickAt = 0f;
            _nextKeepAliveAt = 0f;
        }

        // ---- Guest: apply host state -----------------------------------------

        public void OnRemoteState(GamblingState message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost) return;
            if (message.Kind != GamblingState.KindSlot) return;

            EnsureBuilt();
            if (!_byId.TryGetValue(message.MachineId, out var machine)) return;
            LocateMachine(machine);

            try
            {
                WriteFloat(machine.Credit, message.Credit);
                WriteFloat(machine.BetLevel, message.Bet);
                WriteFloat(machine.Winnings, message.Payout);
                WriteReel(machine.Reel1, message.V1);
                WriteReel(machine.Reel2, message.V2);
                WriteReel(machine.Reel3, message.V3);
                WriteBool(machine.Roll1Locked, (message.Flags & GamblingState.FlagReel1Locked) != 0);
                WriteBool(machine.Roll2Locked, (message.Flags & GamblingState.FlagReel2Locked) != 0);
                WriteBool(machine.Roll3Locked, (message.Flags & GamblingState.FlagReel3Locked) != 0);
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("GamblingSync: apply failed for " + machine.ContainerPath + ": " + e.Message);
            }
        }

        // ---- Host: apply a guest's anyone-triggers intent --------------------

        public bool TryAcceptIntent(GamblingIntent intent)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return false;

            EnsureBuilt();
            if (!_byId.TryGetValue(intent.MachineId, out var machine)) return false;
            LocateMachine(machine);

            if (machine.Anchor == null
                || !TryGetActionTarget(machine, intent.Action, out var fsm, out var stateName)
                || fsm == null
                || !IsGuestNear(session, intent.PlayerId, machine.Anchor.position))
            {
                WinterMPPlugin.Log.LogWarning(
                    $"GamblingSync: dropped invalid or distant intent {intent.MachineId:X8} action {intent.Action} from player {intent.PlayerId}.");
                return false;
            }

            if (_lastIntentSequences.TryGetValue(intent.PlayerId, out ushort previous))
            {
                ushort difference = (ushort)(intent.Sequence - previous);
                if (difference == 0 || difference > short.MaxValue)
                {
                    WinterMPPlugin.Log.LogWarning(
                        $"GamblingSync: dropped stale intent sequence {intent.Sequence} from player {intent.PlayerId}.");
                    return false;
                }
            }

            _lastIntentSequences[intent.PlayerId] = intent.Sequence;
            FsmHook.EnsureRemoteEntry(fsm, stateName);
            FsmHook.FireRemoteEntry(fsm, stateName);
            SyncEventLog.Record("gamble-intent", $"{intent.MachineId:X8} action {intent.Action} player {intent.PlayerId}");

            // The wallet moved — force the next slot broadcast promptly. WalletSync already
            // broadcasts a >=epsilon money change on its own next tick (no manual notify needed).
            _nextHostTickAt = 0f;
            return true;
        }

        // ---- Host broadcast ---------------------------------------------------

        private void HostBroadcastIfChanged(SessionManager session, Machine machine, bool keepAlive)
        {
            if (!machine.AnyFsm) return;

            byte flags = 0, bet, r1, r2, r3;
            ushort credit;
            int payout;

            try
            {
                if (ReadBool(machine.Roll1Locked)) flags |= GamblingState.FlagReel1Locked;
                if (ReadBool(machine.Roll2Locked)) flags |= GamblingState.FlagReel2Locked;
                if (ReadBool(machine.Roll3Locked)) flags |= GamblingState.FlagReel3Locked;
                if (IsSpinning(machine)) flags |= GamblingState.FlagActive;

                credit = ClampUShort(ReadFloat(machine.Credit));
                bet = ClampByte(ReadFloat(machine.BetLevel));
                payout = (int)ReadFloat(machine.Winnings);
                r1 = ReadReel(machine.Reel1);
                r2 = ReadReel(machine.Reel2);
                r3 = ReadReel(machine.Reel3);
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("GamblingSync: read failed for " + machine.ContainerPath + ": " + e.Message);
                return;
            }

            bool changed = !machine.HasLast
                || machine.LastFlags != flags
                || machine.LastCredit != credit
                || machine.LastBet != bet
                || machine.LastR1 != r1 || machine.LastR2 != r2 || machine.LastR3 != r3
                || machine.LastPayout != payout;

            if (!changed && !keepAlive) return;

            machine.HasLast = true;
            machine.LastFlags = flags;
            machine.LastCredit = credit;
            machine.LastBet = bet;
            machine.LastR1 = r1; machine.LastR2 = r2; machine.LastR3 = r3;
            machine.LastPayout = payout;

            session.SendWorldMessage(
                new GamblingState
                {
                    MachineId = machine.Id,
                    Kind = GamblingState.KindSlot,
                    Flags = flags,
                    Credit = credit,
                    Bet = bet,
                    V1 = r1,
                    V2 = r2,
                    V3 = r3,
                    Payout = payout,
                },
                Channel.ReliableOrdered);
        }

        // ---- Discovery --------------------------------------------------------

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            Add("STORE_AREA/Stuff/LOD/GFX_Pub/SlotMachinePub");
            Add("PERAPORTTI/Building/LOD100/SlotMachine");
        }

        private void Add(string containerPath)
        {
            var machine = new Machine
            {
                Id = StableHash.Fnv1a32(containerPath),
                ContainerPath = containerPath,
            };
            _machines.Add(machine);
            _byId[machine.Id] = machine;
        }

        private void LocateMachine(Machine machine)
        {
            GameObject? container;
            try { container = GameObject.Find(machine.ContainerPath); }
            catch { return; }
            if (container == null) return;

            var root = container.transform;
            machine.Anchor = root;

            if (machine.PayMoney == null) machine.PayMoney = FindChildFsm(root, "Buttons/PayMoney", "Use");
            if (machine.Bet == null) machine.Bet = FindChildFsm(root, "Buttons/Bet", "Use");
            if (machine.Start == null) machine.Start = FindChildFsm(root, "Buttons/Start", "Use");
            if (machine.Cashout == null) machine.Cashout = FindChildFsm(root, "Buttons/Cashout", "Use");
            if (machine.Lock1 == null) machine.Lock1 = FindChildFsm(root, "LockButtos/LockRoll1", "Use");
            if (machine.Lock2 == null) machine.Lock2 = FindChildFsm(root, "LockButtos/LockRoll2", "Use");
            if (machine.Lock3 == null) machine.Lock3 = FindChildFsm(root, "LockButtos/LockRoll3", "Use");

            if (machine.Start != null)
            {
                var v = machine.Start.FsmVariables;
                if (machine.Credit == null) machine.Credit = v.FindFsmFloat("AddedMoney");
                if (machine.Winnings == null) machine.Winnings = v.FindFsmFloat("Win");
                if (machine.Roll1Locked == null) machine.Roll1Locked = v.FindFsmBool("Roll1Locked");
                if (machine.Roll2Locked == null) machine.Roll2Locked = v.FindFsmBool("Roll2Locked");
                if (machine.Roll3Locked == null) machine.Roll3Locked = v.FindFsmBool("Roll3Locked");
                if (machine.Reel1 == null) machine.Reel1 = v.FindFsmString("Roll1");
                if (machine.Reel2 == null) machine.Reel2 = v.FindFsmString("Roll2");
                if (machine.Reel3 == null) machine.Reel3 = v.FindFsmString("Roll3");
            }

            if (machine.BetLevel == null && machine.Bet != null)
                machine.BetLevel = machine.Bet.FsmVariables.FindFsmFloat("Bet");

            if (!machine.LoggedFound && machine.AnyFsm)
            {
                machine.LoggedFound = true;
                WinterMPPlugin.Log.LogInfo($"GamblingSync: located slot machine '{machine.ContainerPath}' (id {machine.Id:X8}).");
            }

            var session = SessionManager.Instance;
            if (session == null) return;
            if (session.IsHost) EnsureHostEntries(machine);
            else InstallGuestHooks(machine);
        }

        // Host: pre-register the synthetic MP_* transitions so a relayed intent can force
        // each button's action state regardless of where the FSM currently sits.
        private void EnsureHostEntries(Machine machine)
        {
            if (machine.HostEntriesReady) return;
            bool all = true;
            all &= EnsureEntry(machine.PayMoney, StatePay);
            all &= EnsureEntry(machine.Bet, StateBet);
            all &= EnsureEntry(machine.Start, StateStart);
            all &= EnsureEntry(machine.Cashout, StateCashout);
            all &= EnsureEntry(machine.Lock1, StateLock);
            all &= EnsureEntry(machine.Lock2, StateLock);
            all &= EnsureEntry(machine.Lock3, StateLock);
            machine.HostEntriesReady = all;
        }

        private static bool EnsureEntry(PlayMakerFSM? fsm, string stateName)
        {
            if (fsm == null) return false;
            try { return FsmHook.EnsureRemoteEntry(fsm, stateName); }
            catch { return false; }
        }

        // Guest: relay the local button press to the host as an intent.
        private void InstallGuestHooks(Machine machine)
        {
            if (machine.HooksInstalled) return;
            bool all = true;
            all &= HookOnce(machine.PayMoney, StatePay, machine, GamblingIntent.ActionPay);
            all &= HookOnce(machine.Bet, StateBet, machine, GamblingIntent.ActionBet);
            all &= HookOnce(machine.Start, StateStart, machine, GamblingIntent.ActionStart);
            all &= HookOnce(machine.Cashout, StateCashout, machine, GamblingIntent.ActionCashout);
            all &= HookOnce(machine.Lock1, StateLock, machine, GamblingIntent.ActionLock1);
            all &= HookOnce(machine.Lock2, StateLock, machine, GamblingIntent.ActionLock2);
            all &= HookOnce(machine.Lock3, StateLock, machine, GamblingIntent.ActionLock3);
            machine.HooksInstalled = all;
        }

        private bool HookOnce(PlayMakerFSM? fsm, string stateName, Machine machine, byte action)
        {
            if (fsm == null) return false;
            var captured = machine;
            var capturedAction = action;
            return FsmHook.OnStateEnter(fsm, stateName, () => EmitIntent(captured, capturedAction));
        }

        private void EmitIntent(Machine machine, byte action)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.PlayerCount == 0) return;
            if (Time.unscaledTime < machine.NextIntentAt) return;
            machine.NextIntentAt = Time.unscaledTime + IntentCooldownSeconds;

            session.SendWorldMessage(
                new GamblingIntent
                {
                    MachineId = machine.Id,
                    Action = action,
                    PlayerId = session.LocalPlayerId,
                    Sequence = ++machine.OutIntentSequence,
                },
                Channel.ReliableOrdered);
            SyncEventLog.Record("gamble-intent-out", $"{machine.Id:X8} action {action}");
        }

        private static bool TryGetActionTarget(Machine machine, byte action, out PlayMakerFSM? fsm, out string stateName)
        {
            switch (action)
            {
                case GamblingIntent.ActionPay: fsm = machine.PayMoney; stateName = StatePay; return true;
                case GamblingIntent.ActionBet: fsm = machine.Bet; stateName = StateBet; return true;
                case GamblingIntent.ActionStart: fsm = machine.Start; stateName = StateStart; return true;
                case GamblingIntent.ActionCashout: fsm = machine.Cashout; stateName = StateCashout; return true;
                case GamblingIntent.ActionLock1: fsm = machine.Lock1; stateName = StateLock; return true;
                case GamblingIntent.ActionLock2: fsm = machine.Lock2; stateName = StateLock; return true;
                case GamblingIntent.ActionLock3: fsm = machine.Lock3; stateName = StateLock; return true;
                default: fsm = null; stateName = string.Empty; return false;
            }
        }

        private static bool IsSpinning(Machine machine)
        {
            if (machine.Start == null) return false;
            try
            {
                string s = machine.Start.ActiveStateName;
                return !string.IsNullOrEmpty(s) && s != "Wait player" && s != "Wait button" && s != "Reset game";
            }
            catch { return false; }
        }

        private static PlayMakerFSM? FindChildFsm(Transform root, string childPath, string fsmName)
        {
            try
            {
                var child = root.Find(childPath);
                if (child == null) return null;
                foreach (var fsm in child.GetComponents<PlayMakerFSM>())
                {
                    if (fsm != null && fsm.FsmName == fsmName) return fsm;
                }
                return null;
            }
            catch { return null; }
        }

        private static bool IsGuestNear(SessionManager session, byte playerId, Vector3 targetPosition)
        {
            float now = Time.unscaledTime;
            foreach (var player in session.Players)
            {
                if (player.PlayerId != playerId) continue;
                if (player.LastTransformTime <= 0f || now - player.LastTransformTime > IntentPlayerPoseMaxAgeSeconds)
                    return false;
                return (player.Position - targetPosition).sqrMagnitude
                    <= IntentPlayerMaxDistance * IntentPlayerMaxDistance;
            }
            return false;
        }

        // ---- var helpers ------------------------------------------------------

        private static bool ReadBool(FsmBool? v) => v != null && v.Value;
        private static float ReadFloat(FsmFloat? v) => v != null ? v.Value : 0f;
        private static void WriteBool(FsmBool? v, bool value) { if (v != null) v.Value = value; }
        private static void WriteFloat(FsmFloat? v, float value) { if (v != null) v.Value = value; }

        // Reel symbols read as short digit strings ("0".."9"); carry them as a byte.
        private static byte ReadReel(FsmString? v)
        {
            if (v == null || string.IsNullOrEmpty(v.Value)) return 0;
            return int.TryParse(v.Value, out int n) ? (byte)Mathf.Clamp(n, 0, 255) : (byte)0;
        }

        private static void WriteReel(FsmString? v, byte symbol)
        {
            if (v != null) v.Value = symbol.ToString();
        }

        private static byte ClampByte(float value) => (byte)Mathf.Clamp(value, 0f, 255f);
        private static ushort ClampUShort(float value) => (ushort)Mathf.Clamp(value, 0f, 65535f);
    }
}
