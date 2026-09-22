using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    public sealed partial class PlayerSyncManager
    {
        private readonly GuestClothingResume _clothingResume = new GuestClothingResume();
        internal void ResetClothingSession() => _clothingResume.Reset();
        internal GuestProfile.ClothingSnapshot ResolveLocalClothing(GuestProfile.ClothingSnapshot native)
            => _clothingResume.Observe(native);

        public void OnGuestSpawn(GuestSpawn message)
        {
            var session = SessionManager.Instance;
            if (session == null || session.IsHost || session.State != SessionState.Connected
                || Application.loadedLevelName != "GAME" || !message.ValidClothing
                || message.ClothingPlayerId != session.LocalPlayerId || session.LocalPlayerId == 0
                || message.ClothingAdmission == 0 || message.ClothingAdmission != session.LocalClothingAdmission) return;
            WatchLevelChanges();
            if (!_guestResume.ReceiveOffer(message.HasLastPosition)) return;
            _clothingResume.TryRestore(message, true, session.LocalPlayerId, session.LocalClothingAdmission);
            _pendingSpawnOffer = message;
        }
    }
}
