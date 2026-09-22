using System;
using System.Collections.Generic;
using System.Linq;
using WinterMP.Core;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using Xunit;
using Xunit.Abstractions;

namespace WorldSyncCallbacks.Tests
{
    public sealed class RetryTests : IDisposable
    {
        private static readonly string[] Names = { "VehicleWorldSync", "TractorTrailerSync", "VenttiSync" };
        private readonly ITestOutputHelper _output;
        public RetryTests(ITestOutputHelper output)
        {
            _output = output;
            SessionManager.Instance = new SessionManager { State = SessionState.Hosting };
            SyncEventLog.Clear(); SyncEventLog.Dumps = 0;
            WinterMPPlugin.Log.Warnings.Clear(); WinterMPPlugin.Log.Errors.Clear();
        }
        public void Dispose() { SessionManager.Instance = null; }
        private static WorldSyncManager Bind(Action<int> call)
        {
            var m = new WorldSyncManager();
            m.Bind(now => { Assert.Equal(UnityEngine.Time.unscaledTime, now); call(0); }, () => call(1), s => call(2));
            return m;
        }
        private static void Disable(WorldSyncManager m)
        {
            m.OnWorldUpdate = () => throw new Exception("global update failure");
            for (int i = 0; i < 8; i++) m.TickUpdate(10 + i);
            Assert.True(m.Disabled);
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void OnlyFailingCallbackRetriesAtExactDeadlinesThenStaysQuarantined(int failing)
        {
            int[] calls = new int[3];
            var error = new InvalidOperationException("original " + Names[failing], new Exception("inner cause"));
            var m = Bind(i => { calls[i]++; if (i == failing) throw error; });
            m.TickLate(10); Assert.Equal(new[] { 1, 1, 1 }, calls);
            m.TickLate(10.999f); Assert.Equal(1, calls[failing]);
            m.TickLate(11); Assert.Equal(2, calls[failing]);
            m.TickLate(12.999f); Assert.Equal(2, calls[failing]);
            m.TickLate(13); Assert.Equal(3, calls[failing]);
            for (int i = 0; i < 100; i++) { m.TickUpdate(14 + i); m.TickLate(14 + i); }
            Assert.Equal(3, calls[failing]);
            for (int i = 0; i < 3; i++) if (i != failing) Assert.Equal(105, calls[i]);
            Assert.Equal(100, m.WorldUpdates); Assert.Equal(3, m.ErrorCount);
            Assert.False(m.Disabled); Assert.Equal(0f, m.GlobalBackoff); Assert.Equal(0, m.Releases);
            Assert.Equal(3, SyncEventLog.Entries.Count); Assert.Equal(3, WinterMPPlugin.Log.Warnings.Count);
            Assert.All(SyncEventLog.Entries, text => { Assert.Contains("LateUpdate." + Names[failing], text); Assert.Contains(error.ToString(), text); });
            Assert.Contains("retrying only this callback in 1s", WinterMPPlugin.Log.Warnings[0]);
            Assert.Contains("retrying only this callback in 2s", WinterMPPlugin.Log.Warnings[1]);
            Assert.Contains("quarantined", WinterMPPlugin.Log.Warnings[2]);
            Assert.Empty(WinterMPPlugin.Log.Errors); Assert.Equal(0, SyncEventLog.Dumps);
            _output.WriteLine("PORTABLE " + Names[failing] + ": attempts at 10/11/13s only; two independent callbacks each ran 105 times; 100 Update calls; three complete original exceptions logged, no global pause/disable.");
        }

        [Fact]
        public void SuccessfulRetriesResumeImmediatelyButDoNotRefundIntermittentFailureBudget()
        {
            bool fail = true; int vehicles = 0;
            var m = Bind(i => { if (i != 0) return; vehicles++; if (fail) throw new Exception("flapping"); });
            m.TickLate(10); fail = false;
            m.TickLate(10.5f); Assert.Equal(1, vehicles);
            m.TickLate(11); m.TickLate(11.1f); Assert.Equal(3, vehicles);
            Assert.Single(SyncEventLog.Entries);
            fail = true; m.TickLate(12); Assert.Equal(4, vehicles);
            m.TickLate(13.99f); Assert.Equal(4, vehicles);
            fail = false; m.TickLate(14); Assert.Equal(5, vehicles);
            fail = true; m.TickLate(14.1f); Assert.Equal(6, vehicles);
            fail = false; m.TickLate(200); Assert.Equal(6, vehicles);
            Assert.Equal(3, m.ErrorCount); Assert.False(m.Disabled);
        }

        [Fact]
        public void MultipleFailingSubsystemsHaveIndependentDeadlines()
        {
            float now = 0; var calls = new List<int>();
            var m = Bind(i => { calls.Add(i); if (i == 0 || i == 1 && now >= 10.5f) throw new Exception("failure " + i); });
            now = 10; m.TickLate(now);
            now = 10.5f; m.TickLate(now);
            calls.Clear(); now = 11; m.TickLate(now); Assert.Equal(new[] { 0, 2 }, calls);
            calls.Clear(); now = 11.5f; m.TickLate(now); Assert.Equal(new[] { 1, 2 }, calls);
            calls.Clear(); now = 13; m.TickLate(now); Assert.Equal(new[] { 0, 2 }, calls);
            calls.Clear(); now = 13.5f; m.TickLate(now); Assert.Equal(new[] { 1, 2 }, calls);
            calls.Clear(); m.TickLate(100); Assert.Equal(new[] { 2 }, calls);
            Assert.Equal(6, m.ErrorCount); Assert.False(m.Disabled);
        }

        [Fact]
        public void AggregateFallbackDoesNotSkipHealthySiblingsInItsLastAdmittedFrame()
        {
            var calls = new List<int>();
            var m = Bind(i => { calls.Add(i); if (i == 0) throw new Exception("last vehicle error"); });
            m.OnWorldUpdate = () => throw new Exception("earlier Update error");
            for (int i = 0; i < 7; i++) m.TickUpdate(10 + i);
            m.TickLate(20);
            Assert.Equal(new[] { 0, 1, 2 }, calls); Assert.True(m.Disabled); Assert.Equal(8, m.ErrorCount);
            Assert.Contains("LateUpdate.VehicleWorldSync", WinterMPPlugin.Log.Errors.Single());
            Assert.Contains("last vehicle error", WinterMPPlugin.Log.Errors.Single()); Assert.Equal(1, SyncEventLog.Dumps);
            calls.Clear(); m.TickLate(100); m.TickUpdate(100);
            Assert.Empty(calls); Assert.Equal(7, m.WorldUpdates); Assert.Equal(0, m.Releases);
        }

        [Fact]
        public void WidespreadLateFailuresStillReachOriginalGlobalBudgetAndRetainEveryException()
        {
            int[] calls = new int[3]; var m = Bind(i => { calls[i]++; throw new Exception("failure " + Names[i]); });
            m.TickLate(10); m.TickLate(11); m.TickLate(13);
            Assert.True(m.Disabled); Assert.Equal(9, m.ErrorCount);
            Assert.Equal(new[] { 3, 3, 3 }, calls); Assert.Equal(9, SyncEventLog.Entries.Count);
            // Error 8 crosses the budget, and error 9 is still captured in that admitted frame.
            Assert.Equal(2, SyncEventLog.Dumps);
            foreach (string name in Names) Assert.Equal(3, SyncEventLog.Entries.Count(s => s.Contains("LateUpdate." + name)));
            m.TickLate(100); Assert.Equal(new[] { 3, 3, 3 }, calls);
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void SessionEndDuringLocalBackoffResetsBudgetAndRetryClock(int failing)
        {
            int[] calls = new int[3]; var m = Bind(i => { calls[i]++; if (i == failing) throw new Exception("late"); });
            m.TickLate(10);
            SessionManager.Instance = null; m.TickUpdate(10.1f);
            Assert.Equal(1, m.Releases); Assert.Equal(0, m.ErrorCount); Assert.Empty(SyncEventLog.Entries);
            m.TickLate(10.2f); Assert.Equal(new[] { 1, 1, 1 }, calls);
            SessionManager.Instance = new SessionManager { State = SessionState.Connected };
            m.TickUpdate(10.3f); m.TickLate(10.3f);
            Assert.Equal(new[] { 2, 2, 2 }, calls); Assert.Equal(1, m.ErrorCount);
            Assert.Contains("subsystem 1/3", WinterMPPlugin.Log.Warnings.Last());
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void SessionEndClearsQuarantineAndSubsequentSessionCanRetry(int failing)
        {
            int[] calls = new int[3]; var m = Bind(i => { calls[i]++; if (i == failing) throw new Exception("late"); });
            m.TickLate(10); m.TickLate(11); m.TickLate(13); m.TickLate(14);
            Assert.Equal(3, calls[failing]);
            SessionManager.Instance!.State = SessionState.Failed; m.TickUpdate(14.1f);
            Assert.Equal(1, m.Releases); Assert.Equal(0, m.ErrorCount);
            SessionManager.Instance = new SessionManager { State = SessionState.Connected };
            m.TickUpdate(14.2f); m.TickLate(14.2f);
            Assert.Equal(4, calls[failing]); Assert.Equal(1, m.ErrorCount); Assert.False(m.Disabled);
        }

        [Fact]
        public void ResetIsIdempotentAndClearsAllQuarantineAndRetryState()
        {
            int[] calls = new int[3]; var m = Bind(i => { calls[i]++; throw new Exception("late"); });
            m.TickLate(10); m.TickLate(11); m.TickLate(13);
            Assert.True(m.Disabled);
            m.ResetErrors(); m.ResetErrors();
            Assert.False(m.Disabled); Assert.Equal(0, m.ErrorCount); Assert.Equal(0f, m.GlobalBackoff);
            Assert.Empty(SyncEventLog.Entries);
            // A smaller clock deliberately exposes any retained retry timestamp.
            m.TickLate(1); Assert.Equal(new[] { 4, 4, 4 }, calls); Assert.Equal(3, m.ErrorCount);
        }

        [Theory]
        [InlineData(SessionState.Idle)] [InlineData(SessionState.Failed)] [InlineData(SessionState.Connecting)] [InlineData(null)]
        public void DisabledCallbackRestoresOnEveryInactiveSessionState(SessionState? state)
        {
            var m = new WorldSyncManager(); Disable(m);
            SessionManager.Instance = state.HasValue ? new SessionManager { State = state.Value } : null;
            m.TickUpdate(20); m.TickUpdate(20.1f);
            Assert.Equal(1, m.Releases); Assert.False(m.Disabled); Assert.Equal(0, m.ErrorCount);
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void WorldCleanupClearsEachLocalQuarantineButPreservesSessionErrorBudgetAndLog(int failing)
        {
            int[] calls = new int[3]; var m = Bind(i => { calls[i]++; if (i == failing) throw new Exception("late"); });
            m.TickLate(10); m.TickLate(11); m.TickLate(13);
            m.ResetWorldErrors(); m.ResetWorldErrors();
            Assert.Equal(3, m.ErrorCount); Assert.Equal(3, SyncEventLog.Entries.Count);
            m.TickLate(1); Assert.Equal(new[] { 4, 4, 4 }, calls); Assert.Equal(4, m.ErrorCount);
            Assert.Contains("subsystem 1/3", WinterMPPlugin.Log.Warnings.Last());
        }

        [Fact]
        public void WorldCleanupCannotBypassGlobalFallbackAndNewManagerDoesNotInheritIt()
        {
            var calls = new List<int>(); var m = Bind(calls.Add); Disable(m);
            m.ResetWorldErrors(); m.TickLate(100);
            Assert.True(m.Disabled); Assert.Equal(8, m.ErrorCount); Assert.Empty(calls);
            var next = Bind(calls.Add); next.TickLate(100);
            Assert.Equal(new[] { 0, 1, 2 }, calls); Assert.False(next.Disabled); Assert.Equal(0, next.ErrorCount);
        }

        [Fact]
        public void FailedCleanupIsLoggedAndRetriedAtMostOnceASecondEvenAfterGlobalDisable()
        {
            var m = new WorldSyncManager(); Disable(m);
            SessionManager.Instance = null;
            var error = new InvalidOperationException("cleanup original error");
            m.OnRelease = () => throw error;
            m.TickUpdate(20); Assert.Equal(1, m.Releases); Assert.True(m.Disabled);
            for (int i = 0; i < 10; i++) m.TickUpdate(20.5f);
            Assert.Equal(1, m.Releases); Assert.Equal(9, m.ErrorCount);
            Assert.Contains(error.ToString(), SyncEventLog.Entries.Last());
            m.OnRelease = () => { }; m.TickUpdate(21);
            Assert.Equal(2, m.Releases); Assert.False(m.Disabled); Assert.Equal(0, m.ErrorCount);
        }

        [Fact]
        public void OriginalUpdateBackoffStillSuppressesCallbacksUntilDeadlineThenAllowsCleanup()
        {
            var calls = new List<int>(); var m = Bind(calls.Add);
            m.OnWorldUpdate = () => throw new Exception("Update"); m.TickUpdate(10);
            SessionManager.Instance = null;
            m.TickLate(10.5f); m.TickUpdate(10.999f);
            Assert.Empty(calls); Assert.Equal(0, m.Releases);
            m.TickUpdate(11); Assert.Equal(1, m.Releases); Assert.Equal(0f, m.GlobalBackoff);
        }

        [Theory]
        [InlineData(false, true, true)] [InlineData(true, false, true)] [InlineData(true, true, false)]
        public void ExistingWorldAdmissionGatesDoNotCallOrLog(bool ready, bool active, bool gameLevel)
        {
            var calls = new List<int>(); var m = Bind(calls.Add); m.SetAdmission(ready, active); m.GameLevel = gameLevel;
            m.TickLate(10); Assert.Empty(calls); Assert.Empty(SyncEventLog.Entries); Assert.Equal(0, m.ErrorCount);
        }

        [Fact]
        public void OriginalSessionLookupRemainsAfterTrailerAndNullSessionSkipsOnlyVentti()
        {
            var replacement = new SessionManager { State = SessionState.Connected };
            int vehicles = 0, trailers = 0, ventti = 0;
            var m = new WorldSyncManager();
            m.Bind(now => vehicles++, () => { trailers++; SessionManager.Instance = replacement; }, s => { ventti++; Assert.Same(replacement, s); });
            m.TickLate(10); Assert.Equal(1, ventti);
            m.Bind(now => vehicles++, () => { trailers++; SessionManager.Instance = null; }, s => ventti++);
            m.TickLate(11); Assert.Equal(2, vehicles); Assert.Equal(2, trailers); Assert.Equal(1, ventti);
            Assert.Equal(0, m.ErrorCount);
        }
    }
}
