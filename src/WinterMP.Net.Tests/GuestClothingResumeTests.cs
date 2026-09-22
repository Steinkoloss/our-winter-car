using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class GuestClothingResumeTests
    {
        private static GuestSpawn Offer(byte player = 2, ulong admission = 71) => new GuestSpawn {
            ClothingPlayerId = player, ClothingAdmission = admission, HasSavedClothing = true,
            ClothingStage = 4, ClothingType = 2, WinterGarment = 1 };

        [Fact]
        public void OnlyMatchingAdmittedGuestRestoresOnceAndDoesNotOverwriteNativeRead()
        {
            var resume = new GuestClothingResume();
            var offer = Offer();
            Assert.False(resume.TryRestore(offer, false, 2, 71));
            Assert.False(resume.TryRestore(Offer(3), true, 2, 71));
            Assert.False(resume.TryRestore(Offer(2, 70), true, 2, 71));
            Assert.True(resume.TryRestore(offer, true, 2, 71));
            Assert.False(resume.TryRestore(offer, true, 2, 71));
            var native = new GuestProfile.ClothingSnapshot { Valid = true, ClothingStage = 1 };
            Assert.Equal(4, resume.Observe(native).ClothingStage);
            Assert.Equal(4, resume.Observe(native).ClothingStage);
            Assert.Equal(1, native.ClothingStage);
            native.ClothingStage = 2;
            Assert.Equal(2, resume.Observe(native).ClothingStage);
            Assert.False(resume.TryRestore(offer, true, 2, 71));
        }

        [Fact]
        public void AbsentAndInvalidOffersDoNotConsumeValidRestore()
        {
            var resume = new GuestClothingResume();
            var offer = Offer(); offer.HasSavedClothing = false;
            Assert.False(resume.TryRestore(offer, true, 2, 71));
            offer.HasSavedClothing = true; offer.WinterGarment = 3;
            Assert.False(resume.TryRestore(offer, true, 2, 71));
            Assert.False(resume.TryRestore(Offer(), true, 0, 71));
            Assert.False(resume.TryRestore(Offer(), true, 2, 0));
            Assert.True(resume.TryRestore(Offer(), true, 2, 71));
        }

        [Fact]
        public void ReconnectResetRejectsOldAdmissionAndAcceptsNewOne()
        {
            var resume = new GuestClothingResume();
            Assert.True(resume.TryRestore(Offer(), true, 2, 71));
            resume.Reset();
            Assert.False(resume.TryRestore(Offer(), true, 2, 72));
            Assert.True(resume.TryRestore(Offer(2, 72), true, 2, 72));
        }

        [Fact]
        public void OwnerReportsRejectDuplicatesInvalidStaleAndWrongAdmissionWithoutPoisoningHighWater()
        {
            var state = new ClothingReportGate();
            var good = new PlayerClothingState { PlayerId = 2, Admission = 71, Sequence = 8, ClothingStage = 4, WinterGarment = 1 };
            Assert.False(state.TryAccept(good, 3, 71));
            Assert.False(state.TryAccept(good, 2, 72));
            Assert.True(state.TryAccept(good, 2, 71));
            Assert.False(state.TryAccept(good, 2, 71));
            good.Sequence = 7; Assert.False(state.TryAccept(good, 2, 71));
            good.Sequence = 100; good.WinterGarment = 3;
            Assert.False(state.TryAccept(good, 2, 71));
            good.Sequence = 9; good.WinterGarment = 2;
            Assert.True(state.TryAccept(good, 2, 71));
        }
    }
}
