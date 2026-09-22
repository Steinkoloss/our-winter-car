using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    /// <summary>Mod-owned resume snapshot; never a command to the native clothing FSMs.</summary>
    public sealed class GuestClothingResume
    {
        private bool _restored, _hasBaseline, _overlay;
        private GuestProfile.ClothingSnapshot _saved, _baseline;

        public void Reset()
        {
            _restored = _hasBaseline = _overlay = false;
            _saved = _baseline = default(GuestProfile.ClothingSnapshot);
        }

        public bool TryRestore(GuestSpawn offer, bool admittedGuest, byte localPlayer, ulong admission)
        {
            if (_restored || !admittedGuest || localPlayer == 0 || admission == 0
                || offer.ClothingPlayerId != localPlayer || offer.ClothingAdmission != admission
                || !offer.HasSavedClothing || !offer.ValidClothing) return false;
            _saved = new GuestProfile.ClothingSnapshot { Valid = true, ClothingStage = offer.ClothingStage,
                ClothingType = offer.ClothingType, WinterGarment = offer.WinterGarment };
            _restored = _overlay = true;
            return true;
        }

        public GuestProfile.ClothingSnapshot Observe(GuestProfile.ClothingSnapshot native)
        {
            if (!native.Valid || !native.HasValidValues) return default(GuestProfile.ClothingSnapshot);
            if (!_overlay) return native;
            if (!_hasBaseline) { _baseline = native; _hasBaseline = true; }
            // The guest's loaded personal outfit is not a new dressing action. Keep
            // the portable resume value until this owner's native outfit changes.
            else if (!_baseline.SameValues(native)) _overlay = false;
            return _overlay ? _saved : native;
        }
    }

    /// <summary>One authenticated owner's ordered stream in one admission.</summary>
    public sealed class ClothingReportGate
    {
        private uint _sequence;
        public bool TryAccept(PlayerClothingState message, byte player, ulong admission)
        {
            if (message.PlayerId != player || message.Admission != admission
                || (player != 0 && admission == 0) || !message.ValidValues
                || message.Sequence == 0 || message.Sequence <= _sequence) return false;
            _sequence = message.Sequence;
            return true;
        }
    }
}
