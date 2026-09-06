using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Host-authoritative shared wallet (M5). The game stores cash in a PlayMaker
    /// PlayerMoney global. PlayerBankAccount and PlayerNetIncome are separate globals;
    /// all three bindings are verified against the installed game's serialized actions.
    /// </summary>
    internal sealed partial class WalletSync
    {
        private const float SyncIntervalSeconds = 2f;
        private const float ChangeEpsilon = 0.5f;

        private HutongGames.PlayMaker.FsmFloat? _moneyVar;
        private FsmFloat? _bankVar;
        private FsmFloat? _incomeVar;
        private HutongGames.PlayMaker.FsmFloat? _hudMoneyVar;
        private PlayMakerFSM? _moneyChangeFsm;
        private float _nextProbeAt;
        private float _nextSyncAt;
        private float _lastSentMoney = float.NaN;
        private float _lastAppliedMoney = float.NaN;
        private WalletState? _lastSent;
        private WalletState? _lastReceived;
        private ushort _lastRemoteSequence;
        private bool _hasRemoteSequence;
        private ushort _outSequence;
        private bool _loggedReady;

        public bool Ready => _moneyVar != null;

        public void Reset()
        {
            ResetBanking();
            _moneyVar = null;
            _bankVar = _incomeVar = null;
            _hudMoneyVar = null;
            _moneyChangeFsm = null;
            _nextProbeAt = 0f;
            _nextSyncAt = 0f;
            _lastSentMoney = float.NaN;
            _lastAppliedMoney = float.NaN;
            _outSequence = 0;
            _lastSent = _lastReceived = null;
            _hasRemoteSequence = false;
            _loggedReady = false;
        }

        public void Locate()
        {
            if (Time.unscaledTime < _nextProbeAt) return;
            _nextProbeAt = Time.unscaledTime + 5f;
            if (_moneyVar != null && _bankVar != null && _incomeVar != null
                && _hudMoneyVar != null && _moneyChangeFsm != null) return;

            SyncCatalog.EnsureLoaded();
            var bindings = SyncCatalog.Banking;
            if (bindings == null) return;
            if (_moneyVar == null) _moneyVar = FsmVariables.GlobalVariables.FindFsmFloat(bindings.CashGlobal);
            if (_bankVar == null) _bankVar = FsmVariables.GlobalVariables.FindFsmFloat(bindings.BankGlobal);
            if (_incomeVar == null) _incomeVar = FsmVariables.GlobalVariables.FindFsmFloat(bindings.IncomeGlobal);

            var fsms = ScenePath.ScanFsms();
            foreach (var obj in fsms)
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null) continue;

                try
                {
                    if (_hudMoneyVar == null
                        && fsm.FsmName == "Text"
                        && fsm.gameObject.name == "HUDValue")
                    {
                        string parent = fsm.transform.parent != null ? fsm.transform.parent.name : string.Empty;
                        if (parent == "Money")
                            _hudMoneyVar = fsm.FsmVariables.FindFsmFloat("Data");
                    }

                    if (_moneyChangeFsm == null
                        && fsm.FsmName == "MoneyChange"
                        && fsm.gameObject.name == "Money")
                    {
                        string parent = fsm.transform.parent != null ? fsm.transform.parent.name : string.Empty;
                        if (parent == "Statistics")
                            _moneyChangeFsm = fsm;
                    }
                }
                catch
                {
                    // Destroyed-but-listed objects throw on access; skip them.
                }
            }

            if (_moneyVar != null && !_loggedReady)
            {
                _loggedReady = true;
                WinterMPPlugin.Log.LogInfo("WalletSync: found money global"
                    + (_hudMoneyVar != null ? " and HUD." : "."));
            }
        }

        public WalletState? BuildMessage()
        {
            Locate();
            if (_moneyVar == null || !BankTransferPolicy.IsFinite(_moneyVar.Value)) return null;

            return new WalletState
            {
                Money = _moneyVar.Value,
                Sequence = ++_outSequence,
                BankBalance = _bankVar != null ? _bankVar.Value : 0f,
                NetIncome = _incomeVar != null ? _incomeVar.Value : 0f,
                Flags = (byte)((_bankVar != null && BankTransferPolicy.IsFinite(_bankVar.Value)
                        ? WalletState.FlagBankBalance : 0)
                    | (_incomeVar != null && BankTransferPolicy.IsFinite(_incomeVar.Value)
                        ? WalletState.FlagNetIncome : 0)),
            };
        }

        public bool ShouldBroadcast(WalletState message, float now)
        {
            if (now < _nextSyncAt && !float.IsNaN(_lastSentMoney)
                && Mathf.Abs(message.Money - _lastSentMoney) < ChangeEpsilon
                && _lastSent != null && _lastSent.Flags == message.Flags
                && _lastSent.BankBalance == message.BankBalance && _lastSent.NetIncome == message.NetIncome)
            {
                return false;
            }

            _nextSyncAt = now + SyncIntervalSeconds;
            _lastSentMoney = message.Money;
            _lastSent = message;
            return true;
        }

        public void Apply(WalletState message)
        {
            if (!BankTransferPolicy.IsFinite(message.Money)
                || ((message.Flags & WalletState.FlagBankBalance) != 0 && !BankTransferPolicy.IsFinite(message.BankBalance))
                || ((message.Flags & WalletState.FlagNetIncome) != 0 && !BankTransferPolicy.IsFinite(message.NetIncome))) return;
            ushort delta = (ushort)(message.Sequence - _lastRemoteSequence);
            if (_hasRemoteSequence && (delta == 0 || delta > short.MaxValue)) return;
            _lastRemoteSequence = message.Sequence;
            _hasRemoteSequence = true;
            _lastReceived = message;
            ApplyBalances(message);
        }

        private void ApplyBalances(WalletState message)
        {
            Locate();
            _lastAppliedMoney = message.Money;
            if (_bankVar != null && (message.Flags & WalletState.FlagBankBalance) != 0)
                _bankVar.Value = message.BankBalance;
            if (_incomeVar != null && (message.Flags & WalletState.FlagNetIncome) != 0)
                _incomeVar.Value = message.NetIncome;
            if (_moneyVar == null) return;

            // Dedup against the LIVE local balance, not the last value we applied. A checksum-
            // driven wallet resync (WorldResyncRequest.FlagWallet) re-sends the SAME authoritative
            // amount to correct a drifted guest; comparing against _lastAppliedMoney would skip
            // that correction (host value unchanged since our last apply) and the drift would
            // never heal — the guest would just keep re-requesting resync every checksum round.
            // Comparing against the current global still suppresses the redundant ~2 s keepalive
            // writes whenever nothing has actually drifted.
            // Even sub-mark drift can straddle the rounded wallet CRC boundary.
            // Keep the send throttle tolerant, but reconcile received money exactly.
            if (message.Money == _moneyVar.Value)
                return;

            WinterMPPlugin.Log.LogInfo(
                $"WalletSync: money {_moneyVar.Value:0} -> {message.Money:0} mk.");

            _moneyVar.Value = message.Money;

            if (_hudMoneyVar != null)
                _hudMoneyVar.Value = message.Money;

            RefreshPresentation();
        }

        /// <summary>Revert a guest's optimistic spend before the host authorizes it.</summary>
        public void RestoreGuestMoney()
        {
            Locate();
            if (_moneyVar == null || float.IsNaN(_lastAppliedMoney)) return;

            _moneyVar.Value = _lastAppliedMoney;
            if (_hudMoneyVar != null)
                _hudMoneyVar.Value = _lastAppliedMoney;
        }

        /// <summary>Force the next host broadcast even if the balance barely moved.</summary>
        public void NotifyMoneyChanged()
        {
            _lastSentMoney = float.NaN;
        }

        public uint ComputeCrc()
        {
            Locate();
            if (_moneyVar == null || !BankTransferPolicy.IsFinite(_moneyVar.Value)) return 0;
            uint hash = StableHash.Combine(StableHash.OffsetBasis, (uint)Mathf.RoundToInt(_moneyVar.Value));
            if (_bankVar != null) hash = StableHash.Combine(hash, (uint)Mathf.RoundToInt(_bankVar.Value));
            if (_incomeVar != null) hash = StableHash.Combine(hash, (uint)Mathf.RoundToInt(_incomeVar.Value));
            return hash;
        }

        private void RefreshPresentation()
        {
            if (_moneyChangeFsm == null) return;

            try
            {
                _moneyChangeFsm.SendEvent("CHANGED");
            }
            catch
            {
                // FSM not ready yet.
            }
        }
    }
}
