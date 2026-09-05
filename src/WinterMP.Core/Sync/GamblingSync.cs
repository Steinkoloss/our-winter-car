using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>Host slot ledgers with leased controls and deterministic local reel presentation.</summary>
    internal sealed partial class GamblingSync
    {
        private sealed class Machine
        {
            public uint Id;
            public string Path = "";
            public Transform? Root;
            public readonly PlayMakerFSM?[] Buttons = new PlayMakerFSM?[7];
            public PlayMakerFSM? Start => Buttons[SlotMachineIntent.Spin];
            public FsmFloat? Credit, Winnings, LastWin, Bet;
            public FsmBool? CanHold;
            public FsmInt? Roll;
            public readonly FsmString?[] Reels = new FsmString?[3];
            public readonly FsmBool?[] Holds = new FsmBool?[3];
            public readonly FsmGameObject?[] ReelObjects = new FsmGameObject?[3];
            public readonly TextMesh?[] Texts = new TextMesh?[4];
            public readonly List<InstalledHook> Hooks = new List<InstalledHook>();
            public readonly List<RandomAction> RandomActions = new List<RandomAction>();
            public readonly Queue<SlotMachineIntent> Pending = new Queue<SlotMachineIntent>();
            public readonly Dictionary<byte, float> RequestTimes = new Dictionary<byte, float>();
            public SlotMachineLedger? Ledger;
            public SlotMachineState? View, Animation;
            public bool HooksReady, Failed, Animating, HasRevision, HasPresentedRound;
            public uint PresentedRound;
            public ushort OutSequence;
            public float RetryAt, InputAt, BroadcastAt;
        }

        private sealed class InstalledHook
        {
            public FsmState State = null!;
            public FsmStateAction Action = null!;
        }

        private sealed class RandomAction
        {
            public FsmStateAction Action = null!;
            public bool Enabled;
        }

        private readonly Dictionary<uint, Machine> _machines = new Dictionary<uint, Machine>();
        private readonly System.Random _random = new System.Random();
        private SlotMachineData? _config;
        private FsmFloat? _cash, _statsIn, _statsOut;
        private float _probeAt;
        private bool _clearing, _discoveryFailed;

        public void Update(SessionManager session)
        {
            if (session.PlayerCount == 0) return;
            EnsureBuilt();
            if (_config == null) return;
            if (Time.unscaledTime >= _probeAt)
            {
                _probeAt = Time.unscaledTime + 5f;
                try { Locate(); }
                catch (Exception e) { WinterMPPlugin.Log.LogWarning("Slot discovery will retry: " + e.Message); }
            }
            foreach (var machine in _machines.Values)
            {
                if (machine.Failed) continue;
                try
                {
                    BindVariables(machine);
                    if (machine.Start != null && machine.Start.Fsm.Active) InstallHooks(machine);
                    if (session.IsHost)
                    {
                        EnsureLedger(machine);
                        if (machine.Ledger != null)
                        {
                            bool changed = machine.Ledger.Advance(Time.unscaledTime);
                            if (changed || Time.unscaledTime >= machine.BroadcastAt) Broadcast(session, machine);
                        }
                    }
                    if (machine.Pending.Count > 0 && Time.unscaledTime >= machine.RetryAt)
                    {
                        machine.RetryAt = Time.unscaledTime + 1f;
                        var request = machine.Pending.Peek();
                        if (session.IsHost) OnIntent(request);
                        else session.SendWorldMessage(request, Channel.ReliableOrdered);
                    }
                    ApplyPresentation(machine);
                }
                catch (Exception e) { Disable(machine, e); }
            }
        }

        private void EnsureBuilt()
        {
            if (_config != null || _discoveryFailed) return;
            try
            {
                SyncCatalog.EnsureLoaded();
                var config = SyncCatalog.SlotMachines;
                if (config == null) return;
                foreach (string path in config.Paths)
                {
                    uint id = StableHash.Fnv1a32(path);
                    _machines.Add(id, new Machine { Id = id, Path = path });
                }
                _config = config;
            }
            catch (Exception e)
            {
                _discoveryFailed = true;
                _machines.Clear();
                WinterMPPlugin.Log.LogError("Slot sync unavailable: " + e);
            }
        }

        private void EnsureLedger(Machine machine)
        {
            if (machine.Ledger != null || _config == null || machine.Credit == null || machine.Winnings == null
                || machine.Bet == null || machine.LastWin == null || machine.CanHold == null) return;
            // Let a solo spin already underway when hosting began finish naturally.
            // Inactive scene objects can be read without activating the host's UI.
            if (NativeSpinning(machine)) return;
            int bet = ReadAmount(machine.Bet);
            if (bet < 1 || bet > 5) throw new InvalidOperationException("Invalid slot bet.");
            var initial = new SlotMachineState
            {
                MachineId = machine.Id, Credit = ReadAmount(machine.Credit), Winnings = ReadAmount(machine.Winnings),
                Bet = (byte)bet, LastWin = ReadAmount(machine.LastWin), CanHold = machine.CanHold.Value,
                Reel1 = ReadReel(machine.Reels[0]), Reel2 = ReadReel(machine.Reels[1]), Reel3 = ReadReel(machine.Reels[2]),
            };
            for (int i = 0; i < 3; i++)
                if (machine.Holds[i] != null && machine.Holds[i]!.Value) initial.HoldMask |= (byte)(1 << i);
            machine.Ledger = new SlotMachineLedger(initial, _config.Reels, _config.Payouts, _random.Next);
        }

        public void OnIntent(SlotMachineIntent request)
        {
            var session = SessionManager.Instance;
            if (session == null || !session.IsHost) return;
            EnsureBuilt();
            if (!_machines.TryGetValue(request.MachineId, out var machine) || machine.Failed) return;
            try
            {
                float now = Time.unscaledTime;
                if (machine.RequestTimes.TryGetValue(request.PlayerId, out float next) && now < next) return;
                machine.RequestTimes[request.PlayerId] = now + 0.1f;
                EnsureLedger(machine);
                if (machine.Ledger == null || _cash == null) return;
                bool repeated = machine.Ledger.TryGetReceipt(request, out byte result, out int cashDelta);
                if (!repeated)
                {
                    if (machine.Root == null || !TryPlayerPosition(session, request.PlayerId, out var position)) return;
                    float before = _cash.Value;
                    bool nearby = (position - machine.Root.position).sqrMagnitude <= 36f;
                    result = machine.Ledger.Apply(request, before, now, nearby, out float cash);
                    machine.Ledger.TryGetReceipt(request, out _, out cashDelta);
                    _cash.Value = cash;
                    if (result == SlotMachineResult.Accepted && cash != before)
                    {
                        if (cash < before && _statsIn != null) _statsIn.Value += before - cash;
                        if (cash > before && _statsOut != null) _statsOut.Value += cash - before;
                    }
                    SyncEventLog.Record("slot-action", machine.Id.ToString("X8") + " player " + request.PlayerId
                        + " seq " + request.Sequence + " action " + request.Action + " result " + result);
                }
                if (result == SlotMachineResult.Retry || result == SlotMachineResult.Stale) return;
                Broadcast(session, machine);
                var wallet = WorldSyncManager.Instance?.BuildWalletState();
                if (wallet != null) session.SendWorldMessage(wallet, Channel.ReliableOrdered);
                var receipt = new SlotMachineResult
                {
                    MachineId = request.MachineId, PlayerId = request.PlayerId,
                    Sequence = request.Sequence, Result = result, CashDelta = cashDelta,
                };
                session.SendWorldMessage(receipt, Channel.ReliableOrdered);
                if (request.PlayerId == session.LocalPlayerId) OnResult(receipt);
            }
            catch (Exception e) { Disable(machine, e); }
        }

        public void OnState(SlotMachineState state)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || !SlotMachineLedger.IsValid(state)) return;
            EnsureBuilt();
            if (!_machines.TryGetValue(state.MachineId, out var machine) || machine.Failed) return;
            if (machine.HasRevision && machine.View != null)
            {
                uint delta = state.Revision - machine.View.Revision;
                if (delta == 0 || delta > int.MaxValue) return;
            }
            machine.HasRevision = true;
            machine.View = state;
        }

        public void OnResult(SlotMachineResult result)
        {
            var session = SessionManager.Instance;
            if (session == null || result.PlayerId != session.LocalPlayerId
                || !_machines.TryGetValue(result.MachineId, out var machine) || machine.Pending.Count == 0
                || machine.Pending.Peek().Sequence != result.Sequence || result.Result > SlotMachineResult.Distant) return;
            machine.Pending.Dequeue();
            machine.RetryAt = 0f;
            if (result.Result == SlotMachineResult.Accepted) ApplyCashoutAchievement(result.CashDelta);
            if (result.Result != SlotMachineResult.Accepted)
                session.AddSystemChat(result.Result == SlotMachineResult.Busy
                    ? "* Slot machine is busy. Wait for the current player or spin to finish."
                    : result.Result == SlotMachineResult.Funds
                        ? "* Not enough available money for that slot-machine action."
                        : "* Slot-machine action declined. The machine has been refreshed.");
        }

        private void Queue(Machine machine, byte action, uint round = 0)
        {
            var session = SessionManager.Instance;
            if (_clearing || machine.Failed || !machine.HooksReady || session == null
                || (session.State != SessionState.Hosting && session.State != SessionState.Connected)) return;
            if (action != SlotMachineIntent.Finish)
            {
                if (Time.unscaledTime < machine.InputAt) return;
                machine.InputAt = Time.unscaledTime + 0.15f;
            }
            if (machine.Pending.Count >= 16) return;
            machine.Pending.Enqueue(new SlotMachineIntent
            {
                MachineId = machine.Id, PlayerId = session.LocalPlayerId, Action = action,
                Sequence = ++machine.OutSequence, Round = round,
            });
            if (machine.Pending.Count == 1) machine.RetryAt = 0f;
        }

        private void Broadcast(SessionManager session, Machine machine)
        {
            if (machine.Ledger == null) return;
            var state = machine.Ledger.Snapshot();
            machine.View = state;
            machine.BroadcastAt = Time.unscaledTime + 5f;
            session.SendWorldMessage(state, Channel.ReliableOrdered);
        }

        public void ForceBroadcast()
        {
            foreach (var machine in _machines.Values) machine.BroadcastAt = 0f;
        }

        public void ForgetPlayer(byte playerId)
        {
            foreach (var machine in _machines.Values)
            {
                machine.Ledger?.ForgetPlayer(playerId);
                machine.RequestTimes.Remove(playerId);
                machine.BroadcastAt = 0f;
            }
        }

        internal static bool TryPlayerPosition(SessionManager session, byte id, out Vector3 position)
        {
            position = Vector3.zero;
            if (id == session.LocalPlayerId)
            {
                var world = WorldSyncManager.Instance;
                if (world == null) return false;
                world.FindLocalPlayer();
                if (world.LocalPlayer == null) return false;
                position = world.LocalPlayer.position;
                return true;
            }
            foreach (var player in session.Players)
            {
                if (player.PlayerId != id) continue;
                if (player.IsDead || player.LastTransformTime <= 0f || Time.unscaledTime - player.LastTransformTime > 2f) return false;
                position = player.Position;
                return true;
            }
            return false;
        }

        private static int ReadAmount(FsmFloat value)
        {
            float amount = value.Value;
            if (!BankTransferPolicy.IsFinite(amount) || amount < 0 || amount > SlotMachineLedger.MaximumBalance || amount != (int)amount)
                throw new InvalidOperationException("Invalid slot balance " + value.Name + ".");
            return (int)amount;
        }

        private static byte ReadReel(FsmString? value)
        {
            if (value == null || string.IsNullOrEmpty(value.Value)) return 0;
            if (!byte.TryParse(value.Value, out byte symbol) || symbol > 9)
                throw new InvalidOperationException("Invalid slot reel.");
            return symbol;
        }

        private void Disable(Machine machine, Exception error)
        {
            if (machine.Failed) return;
            machine.Failed = true;
            WinterMPPlugin.Log.LogError("Slot sync disabled for '" + machine.Path + "': " + error);
            try
            {
                machine.Ledger?.FinishSession();
                if (machine.Ledger != null) machine.View = machine.Ledger.Snapshot();
                StopAnimation(machine);
                WriteValues(machine);
            }
            catch (Exception e) { WinterMPPlugin.Log.LogDebug("Slot recovery: " + e.Message); }
        }

        public void Clear()
        {
            _clearing = true;
            foreach (var machine in _machines.Values)
            {
                try
                {
                    machine.Ledger?.FinishSession();
                    if (machine.Ledger != null) machine.View = machine.Ledger.Snapshot();
                    StopAnimation(machine);
                    WriteValues(machine);
                }
                catch (Exception e) { WinterMPPlugin.Log.LogDebug("Slot cleanup: " + e.Message); }
                foreach (var hook in machine.Hooks)
                {
                    try
                    {
                        var actions = new List<FsmStateAction>(hook.State.Actions);
                        actions.Remove(hook.Action);
                        hook.State.Actions = actions.ToArray();
                    }
                    catch (Exception e) { WinterMPPlugin.Log.LogDebug("Slot hook cleanup: " + e.Message); }
                }
                foreach (var action in machine.RandomActions) action.Action.Enabled = action.Enabled;
            }
            _machines.Clear();
            _config = null;
            _cash = _statsIn = _statsOut = null;
            _probeAt = 0f;
            _clearing = _discoveryFailed = false;
        }
    }
}
