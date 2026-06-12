using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Shared session context for world-sync subsystems (hook echo guard, dev/self-test flags,
    /// player lookup). Keeps subsystems off the <see cref="MonoBehaviour"/> type itself.
    /// </summary>
    internal sealed class WorldSyncBridge
    {
        public const float PlayerSearchIntervalSeconds = 2f;
        public const string PlayerObjectName = "PLAYER";

        private readonly WorldSyncManager _owner;
        private ItemWorldSync _items = null!;

        public WorldSyncBridge(WorldSyncManager owner, Dictionary<PlayMakerFSM, bool> hookedFsms)
        {
            _owner = owner;
            HookedFsms = hookedFsms;
        }

        public Dictionary<PlayMakerFSM, bool> HookedFsms { get; }

        public void BindItems(ItemWorldSync items) => _items = items;

        private WalletSync _wallet = null!;
        public void BindWallet(WalletSync wallet) => _wallet = wallet;
        public void RestoreGuestMoney() => _wallet.RestoreGuestMoney();
        public void NotifyMoneyChanged() => _wallet.NotifyMoneyChanged();

        public bool ApplyingRemote
        {
            get => _owner.ApplyingRemote;
            set => _owner.ApplyingRemote = value;
        }

        public bool SelfTest => _owner.SelfTest;

        public float FirstDoorRegisteredAt
        {
            get => _owner.FirstDoorRegisteredAt;
            set => _owner.FirstDoorRegisteredAt = value;
        }

        public float DoorTestDelay => _owner.DoorTestDelay;

        public int DoorTestStep
        {
            get => _owner.DoorTestStep;
            set => _owner.DoorTestStep = value;
        }

        public Transform? LocalPlayer
        {
            get => _owner.LocalPlayer;
            set => _owner.LocalPlayer = value;
        }

        public float NextPlayerSearchAt
        {
            get => _owner.NextPlayerSearchAt;
            set => _owner.NextPlayerSearchAt = value;
        }

        public SessionManager? Session => SessionManager.Instance;

        public void FindLocalPlayer() => _owner.FindLocalPlayer();

        public void RequestObjectState(uint netId) => _owner.RequestObjectState(netId);

        public bool IsLocalPlayerDriving(SyncedItem item) => _items.IsLocalPlayerDriving(item);

        public bool IsLocalPlayerDrivingAny() => _items.IsLocalPlayerDrivingAny();
    }
}
