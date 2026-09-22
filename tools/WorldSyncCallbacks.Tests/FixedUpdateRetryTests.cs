using System;
using System.Collections.Generic;
using System.Linq;
using WinterMP.Core;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using Xunit;

namespace WinterMP.Core.Sync
{
    public sealed partial class WorldSyncManager
    {
        // Production local reset body. Python wiring assertions separately pin
        // its scene/destruction callers; native teardown is NOT executed here.
        internal void ResetFixedWorldErrors() => ResetFixedUpdateErrors();
    }
}

namespace WorldSyncCallbacks.Tests
{
    public sealed class FixedUpdateRetryTests : IDisposable
    {
        public FixedUpdateRetryTests()
        {
            SyncEventLog.Clear(); SyncEventLog.Dumps = 0;
            WinterMPPlugin.Log.Warnings.Clear(); WinterMPPlugin.Log.Errors.Clear();
            GuestSaveGuard.ProtectWorld = false; WinterMPPlugin.DevKeysEnabled.Value = false;
        }
        public void Dispose() { SessionManager.Instance = null; GuestSaveGuard.ProtectWorld = false; }
        private static SessionManager Session(SessionState role)
        {
            var session = new SessionManager { State = role };
            SessionManager.Instance = session; return session;
        }

        [Theory]
        [InlineData(SessionState.Hosting)] [InlineData(SessionState.Connected)]
        public void HealthyPhysicsStepsPreserveAdmissionAndNeverInventRoleRestrictions(SessionState role)
        {
            var session = Session(role); var manager = new WorldSyncManager(); int calls = 0;
            manager.BindTrainFixed(() => { Assert.Same(session, SessionManager.Instance); calls++; });
            manager.TickFixed(0); manager.TickFixed(0); manager.TickFixed(.02f);
            Assert.Equal(3, calls);
            manager.SetAdmission(false, true); manager.TickFixed(1); Assert.Equal(3, calls);
            manager.SetAdmission(true, false); manager.TickFixed(2); Assert.Equal(3, calls);
            manager.SetAdmission(true, true); manager.GameLevel = false; manager.TickFixed(3); Assert.Equal(3, calls);
            manager.GameLevel = true; manager.TickFixed(4); Assert.Equal(4, calls);
            Assert.Equal(0, manager.ErrorCount); Assert.Equal(0f, manager.GlobalBackoff);
            Assert.Empty(SyncEventLog.Entries); Assert.Empty(session.Sent); Assert.Equal(0, manager.Releases);
        }

        [Theory]
        [InlineData(SessionState.Hosting)] [InlineData(SessionState.Connected)]
        public void SuccessfulRetriesUseCurrentCallAndDoNotRefundFlappingBudget(SessionState role)
        {
            Session(role); var manager = new WorldSyncManager(); bool fail = true;
            string current = "first"; var observed = new List<string>();
            manager.BindTrainFixed(() => { observed.Add(current); if (fail) throw new Exception(current); });
            manager.TickFixed(10);
            current = "live second"; fail = false;
            manager.TickFixed(10.999f); Assert.Single(observed);
            manager.TickFixed(11); manager.TickFixed(11); Assert.Equal(3, observed.Count);
            current = "second error"; fail = true; manager.TickFixed(12);
            current = "live third"; fail = false;
            manager.TickFixed(13.999f); Assert.Equal(4, observed.Count);
            manager.TickFixed(14);
            current = "third error"; fail = true; manager.TickFixed(14.01f);
            current = "must not run"; fail = false; manager.TickFixed(1000);
            Assert.Equal(new[] { "first", "live second", "live second", "second error", "live third", "third error" }, observed);
            Assert.Equal(3, manager.ErrorCount); Assert.False(manager.Disabled);
            Assert.Equal(3, SyncEventLog.Entries.Count);
        }

        [Theory]
        [InlineData(SessionState.Hosting)] [InlineData(SessionState.Connected)]
        public void FixedUpdateAndVehicleUpdateAndLateUpdateHaveIndependentRetryClocks(SessionState role)
        {
            Session(role); var manager = new WorldSyncManager(); int train = 0, update = 0, late = 0, healthy = 0;
            manager.BindTrainFixed(() => { train++; throw new Exception("fixed"); });
            manager.BindVehicleUpdates((name, s) => {
                if (name == UpdateTests.Names[0]) { update++; throw new Exception("update"); }
            });
            manager.Bind(_ => { late++; if (late == 1) throw new Exception("late"); }, () => healthy++, _ => healthy++);
            manager.TickFixed(10); manager.TickUpdate(10.25f); manager.TickLate(10.5f);
            manager.TickFixed(10.999f); manager.TickUpdate(11); manager.TickLate(11);
            Assert.Equal(1, train); Assert.Equal(1, update); Assert.Equal(1, late);
            manager.TickFixed(11); manager.TickUpdate(11.25f); manager.TickLate(11.5f);
            Assert.Equal(2, train); Assert.Equal(2, update); Assert.Equal(2, late);
            manager.TickFixed(12.999f); manager.TickUpdate(13.249f);
            Assert.Equal(2, train); Assert.Equal(2, update);
            manager.TickFixed(13); manager.TickUpdate(13.25f);
            manager.TickFixed(100); manager.TickUpdate(100); manager.TickLate(100);
            Assert.Equal(3, train); Assert.Equal(3, update); Assert.Equal(3, late); Assert.Equal(8, healthy);
            Assert.Equal(7, manager.ErrorCount); Assert.False(manager.Disabled); Assert.Equal(0f, manager.GlobalBackoff);
        }

        [Theory]
        [InlineData(SessionState.Hosting)] [InlineData(SessionState.Connected)]
        public void GlobalBackoffDefersPhysicsAndTrainEighthErrorStillDisablesAllGameplay(SessionState role)
        {
            var session = Session(role); var manager = new WorldSyncManager(); int train = 0, late = 0;
            manager.BindTrainFixed(() => { train++; throw new Exception("eighth train", new Exception("root cause")); });
            manager.Bind(_ => late++, () => late++, _ => late++);
            manager.OnWorldUpdate = () => throw new Exception("global prerequisite");
            for (int i = 0; i < 7; i++) manager.TickUpdate(1 + i);
            manager.TickFixed(7.999f); manager.TickLate(7.999f);
            Assert.Equal(0, train); Assert.Equal(0, late); Assert.Equal(7, manager.ErrorCount);
            Assert.Equal(8f, manager.GlobalBackoff);
            manager.OnWorldUpdate = () => { };
            manager.TickFixed(8);
            Assert.Equal(1, train); Assert.True(manager.Disabled); Assert.Equal(8, manager.ErrorCount);
            Assert.Equal(1, SyncEventLog.Dumps); Assert.Equal(8, SyncEventLog.Entries.Count);
            string fatal = Assert.Single(WinterMPPlugin.Log.Errors);
            Assert.Contains("last (FixedUpdate.TrainSync)", fatal);
            Assert.Contains("eighth train", fatal); Assert.Contains("root cause", fatal);
            Assert.Contains("FixedUpdate", fatal); Assert.Contains("#8", SyncEventLog.Entries.Last());
            int updates = manager.WorldUpdates;
            manager.TickFixed(100); manager.TickLate(100); manager.TickUpdate(100);
            Assert.Equal(1, train); Assert.Equal(0, late); Assert.Equal(updates, manager.WorldUpdates);
            session.State = SessionState.Idle; manager.TickUpdate(100.1f);
            Assert.Equal(1, manager.Releases); Assert.False(manager.Disabled); Assert.Equal(0, manager.ErrorCount);
            session.State = role; manager.TickUpdate(100.2f); manager.TickFixed(100.2f);
            Assert.Equal(2, train); Assert.Equal(1, manager.ErrorCount);
        }

        [Theory]
        [InlineData(SessionState.Hosting)] [InlineData(SessionState.Connected)]
        public void LocalWorldResetIsIdempotentClearsDeadlineAndQuarantineButKeepsGlobalHistory(SessionState role)
        {
            Session(role); var manager = new WorldSyncManager(); int calls = 0;
            manager.BindTrainFixed(() => { calls++; throw new Exception("train"); });
            manager.TickFixed(10); manager.ResetFixedWorldErrors(); manager.TickFixed(10.1f);
            Assert.Equal(2, calls); Assert.Contains("subsystem 1/3", WinterMPPlugin.Log.Warnings.Last());
            manager.TickFixed(11.1f); manager.TickFixed(13.1f); manager.TickFixed(100);
            Assert.Equal(4, calls); Assert.Equal(4, manager.ErrorCount);
            manager.ResetFixedWorldErrors(); manager.ResetFixedWorldErrors();
            Assert.Equal(4, manager.ErrorCount); Assert.Equal(4, SyncEventLog.Entries.Count);
            manager.TickFixed(0); Assert.Equal(5, calls);
            Assert.Contains("subsystem 1/3", WinterMPPlugin.Log.Warnings.Last());
            Assert.False(manager.Disabled);
            var fresh = new WorldSyncManager(); int freshCalls = 0;
            fresh.BindTrainFixed(() => freshCalls++); fresh.TickFixed(0);
            Assert.Equal(1, freshCalls); Assert.Equal(0, fresh.ErrorCount);
        }

        [Theory]
        [InlineData(SessionState.Hosting)] [InlineData(SessionState.Connected)]
        public void LocalResetDoesNotRefundGlobalDisableOrEraseDiagnostics(SessionState role)
        {
            Session(role); var manager = new WorldSyncManager();
            manager.OnWorldUpdate = () => throw new Exception("global");
            for (int i = 0; i < 8; i++) manager.TickUpdate(i);
            manager.ResetFixedWorldErrors();
            int calls = 0; manager.BindTrainFixed(() => calls++); manager.TickFixed(100);
            Assert.True(manager.Disabled); Assert.Equal(8, manager.ErrorCount);
            Assert.Equal(8, SyncEventLog.Entries.Count); Assert.Equal(0, calls);
        }

        [Theory]
        [InlineData(SessionState.Hosting)] [InlineData(SessionState.Connected)]
        public void FailedReleaseAfterTrainQuarantineIsRateLimitedAndEventuallyResets(SessionState role)
        {
            var session = Session(role); var manager = new WorldSyncManager(); int train = 0;
            manager.BindTrainFixed(() => { train++; throw new Exception("train"); });
            manager.TickFixed(10); manager.TickFixed(11); manager.TickFixed(13);
            session.State = SessionState.Failed;
            manager.OnRelease = () => throw new Exception("native release double");
            manager.TickUpdate(14); manager.TickUpdate(14.999f); manager.TickFixed(14.999f);
            Assert.Equal(1, manager.Releases); Assert.Equal(4, manager.ErrorCount);
            Assert.Equal(15f, manager.GlobalBackoff); Assert.Equal(3, train);
            manager.OnRelease = () => { }; manager.TickUpdate(15); manager.TickUpdate(16);
            Assert.Equal(2, manager.Releases); Assert.Equal(0, manager.ErrorCount);
            session.State = role; manager.TickUpdate(16.1f); manager.TickFixed(16.1f);
            Assert.Equal(4, train); Assert.Contains("subsystem 1/3", WinterMPPlugin.Log.Warnings.Last());
        }

        [Theory]
        [InlineData(SessionState.Idle)] [InlineData(SessionState.Connecting)] [InlineData(SessionState.Failed)] [InlineData(null)]
        public void InactiveSessionUpdateReleasesBeforeAnyFurtherPhysics(SessionState? state)
        {
            Session(SessionState.Connected); var manager = new WorldSyncManager(); int train = 0;
            manager.BindTrainFixed(() => { train++; throw new Exception("train"); });
            manager.TickFixed(10);
            SessionManager.Instance = state.HasValue ? new SessionManager { State = state.Value } : null;
            manager.TickUpdate(10.01f); manager.TickFixed(100); manager.TickUpdate(100);
            Assert.Equal(1, manager.Releases); Assert.Equal(1, train);
            Assert.Equal(0, manager.ErrorCount); Assert.Equal(0f, manager.GlobalBackoff);
        }
    }
}
