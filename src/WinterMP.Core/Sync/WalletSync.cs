using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Host-authoritative shared wallet (M5). The game stores cash in a PlayMaker
    /// global float (typically "Money"); the HUD reads GUI/HUD/Money/HUDValue :: Data.
    /// </summary>
    internal sealed class WalletSync
    {
        private const float SyncIntervalSeconds = 2f;
        private const float ChangeEpsilon = 0.5f;

        private HutongGames.PlayMaker.FsmFloat? _moneyVar;
        private HutongGames.PlayMaker.FsmFloat? _hudMoneyVar;
        private PlayMakerFSM? _moneyChangeFsm;
        private float _nextProbeAt;
        private float _nextSyncAt;
        private float _lastSentMoney = float.NaN;
        private float _lastAppliedMoney = float.NaN;
        private ushort _outSequence;
        private bool _loggedReady;

        public bool Ready => _moneyVar != null;

        public bool TryGetMoney(out float money)
        {
            Locate();
            if (_moneyVar == null)
            {
                money = 0f;
                return false;
            }

            money = _moneyVar.Value;
            return true;
        }

        public void Reset()
        {
            _moneyVar = null;
            _hudMoneyVar = null;
            _moneyChangeFsm = null;
            _nextProbeAt = 0f;
            _nextSyncAt = 0f;
            _lastSentMoney = float.NaN;
            _lastAppliedMoney = float.NaN;
            _outSequence = 0;
            _loggedReady = false;
        }

        public void Locate()
        {
            if (Time.unscaledTime < _nextProbeAt) return;
            _nextProbeAt = Time.unscaledTime + 5f;
            if (_moneyVar != null && _hudMoneyVar != null && _moneyChangeFsm != null) return;

            if (_moneyVar == null)
            {
                _moneyVar = FsmVariables.GlobalVariables.FindFsmFloat("Money")
                    ?? FsmVariables.GlobalVariables.FindFsmFloat("money");
            }

            var fsms = Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM));
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
            if (_moneyVar == null) return null;

            return new WalletState
            {
                Money = _moneyVar.Value,
                Sequence = ++_outSequence,
            };
        }

        public bool ShouldBroadcast(WalletState message, float now)
        {
            if (now < _nextSyncAt && !float.IsNaN(_lastSentMoney)
                && Mathf.Abs(message.Money - _lastSentMoney) < ChangeEpsilon)
            {
                return false;
            }

            _nextSyncAt = now + SyncIntervalSeconds;
            _lastSentMoney = message.Money;
            return true;
        }

        public void Apply(WalletState message)
        {
            Locate();
            if (_moneyVar == null) return;

            if (!float.IsNaN(_lastAppliedMoney)
                && Mathf.Abs(message.Money - _lastAppliedMoney) < ChangeEpsilon)
            {
                return;
            }

            WinterMPPlugin.Log.LogInfo(
                $"WalletSync: money {_moneyVar.Value:0} -> {message.Money:0} mk.");

            _moneyVar.Value = message.Money;
            _lastAppliedMoney = message.Money;

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
