using System;
using System.Collections.Generic;
using System.Linq;
using WinterMP.Core;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using Xunit;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace WorldSyncCallbacks.Tests
{
    public sealed class CallbackTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        public CallbackTests(ITestOutputHelper output)
        {
            _output = output;
            SessionManager.Instance = new SessionManager { State = SessionState.Hosting };
            SyncEventLog.Clear(); SyncEventLog.Dumps = 0;
            WinterMPPlugin.Log.Warnings.Clear(); WinterMPPlugin.Log.Errors.Clear();
        }
        public void Dispose() { SessionManager.Instance = null; }

        private static WorldSyncManager Bind(List<string> calls, int failing, Exception error)
        {
            var manager = new WorldSyncManager();
            manager.Bind(now => { calls.Add("vehicles"); if (failing == 0) throw error; },
                () => { calls.Add("trailer"); if (failing == 1) throw error; },
                session => { Assert.Same(SessionManager.Instance, session); calls.Add("ventti"); if (failing == 2) throw error; });
            return manager;
        }

        [Theory]
        [InlineData(0, SessionState.Hosting)]
        [InlineData(1, SessionState.Hosting)]
        [InlineData(2, SessionState.Hosting)]
        [InlineData(0, SessionState.Connected)]
        [InlineData(1, SessionState.Connected)]
        [InlineData(2, SessionState.Connected)]
        public void EscapingSubsystemErrorDoesNotSkipIndependentCallsOrPauseWorld(int failing, SessionState role)
        {
            var session = SessionManager.Instance!; session.State = role;
            session.Sent.Add("pre-existing-unrelated-message");
            var calls = new List<string>(); var error = new InvalidOperationException("injected late failure " + failing);
            var manager = Bind(calls, failing, error);
            manager.TickLate(10);
            Assert.Equal(new[] { "vehicles", "trailer", "ventti" }, calls);
            Assert.False(manager.Disabled); Assert.Equal(1, manager.ErrorCount);
            Assert.Equal(0f, manager.GlobalBackoff);
            Assert.Equal(0, manager.Releases);
            Assert.Same(session, SessionManager.Instance); Assert.Equal(role, session.State);
            Assert.Equal(new[] { "pre-existing-unrelated-message" }, session.Sent);
            calls.Clear(); manager.TickUpdate(10.25f); manager.TickLate(10.25f);
            Assert.Equal(1, manager.WorldUpdates);
            Assert.Equal(new[] { "vehicles", "trailer", "ventti" }.Where((_, i) => i != failing), calls);
            Assert.Single(SyncEventLog.Entries); Assert.Contains(error.ToString(), SyncEventLog.Entries[0]);
            _output.WriteLine("PORTABLE actual WorldSyncManager callbacks; role=" + role + "; failing=" + failing +
                "; same-frame order preserved; only failing callback cooled down; Update and healthy callbacks continued; session/outbox unchanged. Subsystems/Unity are doubles, NOT native gameplay.");
        }

        [Fact]
        public void GlobalFallbackStillLogsAndDisablesAfterEightUpdateFailures()
        {
            var manager = new WorldSyncManager(); var error = new InvalidOperationException("unrecoverable world update");
            manager.OnWorldUpdate = () => throw error;
            for (int i = 0; i < 8; i++) manager.TickUpdate(10 + i);
            Assert.True(manager.Disabled); Assert.Equal(8, manager.ErrorCount);
            Assert.Equal(7, WinterMPPlugin.Log.Warnings.Count); Assert.Single(WinterMPPlugin.Log.Errors);
            Assert.Equal(8, SyncEventLog.Entries.Count); Assert.Equal(1, SyncEventLog.Dumps);
            Assert.All(SyncEventLog.Entries, text => Assert.Contains(error.ToString(), text));
            manager.TickUpdate(100); Assert.Equal(8, manager.WorldUpdates);
        }

        [Fact]
        public void DisabledWorldStillReachesTeardownAndAllowsSubsequentSession()
        {
            var manager = new WorldSyncManager(); manager.OnWorldUpdate = () => throw new Exception("global failure");
            for (int i = 0; i < 8; i++) manager.TickUpdate(10 + i);
            Assert.True(manager.Disabled);
            SessionManager.Instance!.State = SessionState.Idle;
            manager.TickUpdate(30);
            Assert.Equal(1, manager.Releases); Assert.False(manager.Disabled);
            Assert.Equal(0, manager.ErrorCount); Assert.Equal(0f, manager.GlobalBackoff); Assert.Empty(SyncEventLog.Entries);
            manager.TickUpdate(31); Assert.Equal(1, manager.Releases);
            SessionManager.Instance = new SessionManager { State = SessionState.Connected };
            manager.OnWorldUpdate = () => { };
            manager.TickUpdate(32); Assert.Equal(9, manager.WorldUpdates);
        }
    }
}
