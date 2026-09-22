using System;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Session
{
    public sealed partial class SessionManager
    {
        internal ulong LocalClothingAdmission { get; private set; }
        private uint _clothingSequence;
        internal uint NextClothingSequence()
        {
            // Do not wrap into a previously accepted sequence during a long session.
            if (_clothingSequence == uint.MaxValue) return 0;
            return ++_clothingSequence;
        }

        private static ulong NewClothingAdmission()
        {
            ulong value;
            do { value = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0); } while (value == 0);
            return value;
        }

        private static void PopulateGuestClothing(GuestSpawn offer, RemotePlayer guest)
        {
            offer.ClothingPlayerId = guest.PlayerId;
            offer.ClothingAdmission = guest.ClothingAdmission;
            if (!guest.ReturningGuest || !GuestProfileStore.TryGetClothing(guest.SteamId, out var clothing)) return;
            offer.HasSavedClothing = true;
            offer.ClothingStage = (byte)clothing.ClothingStage;
            offer.ClothingType = (byte)clothing.ClothingType;
            offer.WinterGarment = (byte)clothing.WinterGarment;
        }

        private bool HasClothingAdmission(PlayerSpawn spawn)
        {
            foreach (var player in _playersByPeer.Values)
                if (player.PlayerId == spawn.PlayerId && player.SteamId == spawn.SteamId
                    && player.ClothingAdmission == spawn.ClothingAdmission) return true;
            return false;
        }

        private bool HandlePlayerClothingState(PeerId peer, PlayerClothingState message)
        {
            if ((IsHost ? State != SessionState.Hosting : State != SessionState.Connected)
                || message.PlayerId == LocalPlayerId) return false;
            if (IsHost)
            {
                if (!_playersByPeer.TryGetValue(peer, out var sender) || sender.PlayerId != message.PlayerId) return false;
            }
            else if (!_hostPeer.HasValue || peer != _hostPeer.Value) return false;

            var world = Sync.WorldSyncManager.Instance;
            if (world == null) return false;
            foreach (var player in _playersByPeer.Values)
            {
                if (player.PlayerId != message.PlayerId) continue;
                if (!player.ClothingReports.TryAccept(message, player.PlayerId, player.ClothingAdmission)) return false;
                player.ClothingState = new PlayerClothingState { PlayerId = message.PlayerId,
                    ClothingStage = message.ClothingStage, ClothingType = message.ClothingType,
                    WinterGarment = message.WinterGarment, Admission = message.Admission, Sequence = message.Sequence };
                world.OnRemoteClothingState(player.ClothingState);
                if (IsHost)
                {
                    GuestProfileStore.RememberClothing(player.SteamId, new GuestProfile.ClothingSnapshot {
                        Valid = true, ClothingStage = message.ClothingStage, ClothingType = message.ClothingType,
                        WinterGarment = message.WinterGarment });
                    Broadcast(message, Channel.ReliableOrdered, except: peer);
                }
                return true;
            }
            return false;
        }
    }
}
