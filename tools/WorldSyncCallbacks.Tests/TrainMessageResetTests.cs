using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using Xunit;

namespace WinterMP.Core.Sync
{
    public sealed partial class WorldSyncManager
    {
        internal void QuarantineTrainMessages()
        { for (int i = 0; i < 3; i++) { _trainReceiveFailure.Record(10); _trainSnapshotFailure.Record(10); } }
        internal bool TrainMessagesQuarantined => _trainReceiveFailure.Quarantined && _trainSnapshotFailure.Quarantined;
        internal bool TrainMessagesReady => _trainReceiveFailure.CanRun(0) && _trainSnapshotFailure.CanRun(0);
        internal void ResetTrainMessages() => ResetTrainMessageErrors();
    }
}
namespace WorldSyncCallbacks.Tests
{
    public sealed class TrainMessageResetTests
    {
        [Fact]
        public void BothNewRecordsResetOnExistingSessionReleasePath()
        {
            var world = new WorldSyncManager();
            SessionManager.Instance = new SessionManager { State = SessionState.Hosting };
            world.QuarantineTrainMessages();
            Assert.True(world.TrainMessagesQuarantined);
            SessionManager.Instance.State = SessionState.Idle;
            world.TickUpdate(10);
            Assert.Equal(1, world.Releases);
            Assert.True(world.TrainMessagesReady);
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LocalAndFullResetAreIdempotent(bool full)
        {
            var world = new WorldSyncManager();
            world.QuarantineTrainMessages();
            for (int i = 0; i < 2; i++)
            {
                if (full) world.ResetErrors(); else world.ResetTrainMessages();
                Assert.True(world.TrainMessagesReady);
            }
        }
    }
}
