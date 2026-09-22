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
    public sealed class FixedUpdateTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        public FixedUpdateTests(ITestOutputHelper output)
        {
            _output = output;
            SyncEventLog.Clear(); SyncEventLog.Dumps = 0;
            WinterMPPlugin.Log.Warnings.Clear(); WinterMPPlugin.Log.Errors.Clear();
            GuestSaveGuard.ProtectWorld = false; WinterMPPlugin.DevKeysEnabled.Value = false;
        }
        public void Dispose() { SessionManager.Instance = null; GuestSaveGuard.ProtectWorld = false; }

        [Theory]
        [InlineData(SessionState.Hosting)] [InlineData(SessionState.Connected)]
        public void TrainEscapeIsBoundedAndDoesNotPauseHealthyFramesOrSessionRelease(SessionState role)
        {
            var session = new SessionManager { State = role };
            SessionManager.Instance = session;
            session.Sent.Add("unrelated pending result");
            var manager = new WorldSyncManager();
            var attempts = new List<float>(); var frameCalls = new List<string>(); int vehicleCalls = 0, lateCalls = 0;
            var error = new InvalidOperationException("train coordinator escape", new ArgumentException("original inner cause"));
            manager.BindTrainFixed(() => { attempts.Add(UnityEngine.Time.unscaledTime); throw error; });
            manager.BindVehicleUpdates((name, s) => { Assert.Same(session, s); vehicleCalls++; });
            manager.BindSurroundingUpdates((name, s) => { Assert.Same(session, s); frameCalls.Add(name); });
            manager.Bind(_ => lateCalls++, () => lateCalls++, s => { Assert.Same(session, s); lateCalls++; });

            // This call executes the actual production FixedUpdate, not a model of it.
            Assert.Null(Record.Exception(() => manager.TickFixed(10)));
            Assert.Equal(1, manager.ErrorCount); Assert.False(manager.Disabled);
            Assert.Equal(0f, manager.GlobalBackoff); Assert.Equal(0, manager.Releases);
            Assert.Equal("error: FixedUpdate.TrainSync #1: " + error, Assert.Single(SyncEventLog.Entries));
            Assert.Contains(nameof(InvalidOperationException), error.ToString());
            Assert.Contains(nameof(ArgumentException), error.ToString());
            Assert.Contains("FixedUpdate", error.StackTrace!);
            Assert.Contains(error.ToString(), Assert.Single(WinterMPPlugin.Log.Warnings));
            Assert.Contains("retrying only this callback in 1s", WinterMPPlugin.Log.Warnings[0]);

            // Multiple physics steps at the same time, one second, then two seconds;
            // no replay of captured work and no per-frame log/error storm.
            foreach (float now in new[] { 10f, 10.99f, 11f, 11f, 12.99f, 13f, 13f, 100f })
                Assert.Null(Record.Exception(() => manager.TickFixed(now)));
            Assert.Equal(new[] { 10f, 11f, 13f }, attempts);
            Assert.Equal(3, manager.ErrorCount); Assert.False(manager.Disabled);
            Assert.Equal(3, SyncEventLog.Entries.Count); Assert.Equal(3, WinterMPPlugin.Log.Warnings.Count);
            Assert.Contains("retrying only this callback in 2s", WinterMPPlugin.Log.Warnings[1]);
            Assert.Contains("quarantined until world/session cleanup", WinterMPPlugin.Log.Warnings[2]);
            Assert.Equal(0, SyncEventLog.Dumps); Assert.Empty(WinterMPPlugin.Log.Errors);

            manager.TickUpdate(100); manager.TickLate(100);
            Assert.Equal(new[] { "bags", "train", "clothing", "ventti", "trailer", "carRadio" }, frameCalls);
            Assert.Equal(10, vehicleCalls); Assert.Equal(3, lateCalls);
            Assert.Same(session, SessionManager.Instance); Assert.Equal(role, session.State);
            Assert.Equal(new[] { "unrelated pending result" }, session.Sent);
            Assert.Equal(0, manager.Releases);

            session.State = SessionState.Idle;
            manager.TickUpdate(100.01f); manager.TickFixed(100.01f); manager.TickLate(100.01f);
            Assert.Equal(1, manager.Releases); Assert.Equal(3, attempts.Count);
            Assert.Equal(0, manager.ErrorCount); Assert.Empty(SyncEventLog.Entries);
            session.State = role;
            manager.TickUpdate(100.02f); manager.TickFixed(100.02f);
            Assert.Equal(4, attempts.Count); Assert.Equal(1, manager.ErrorCount);
            Assert.Contains("subsystem 1/3", WinterMPPlugin.Log.Warnings.Last());
            _output.WriteLine("PORTABLE production FixedUpdate; role=" + role +
                "; escaping error isolated; attempts=10,11,13; 1s/2s retry then quarantine; full inner error and stack retained;" +
                " original Update order/LateUpdate/session-outbox preserved; inactive Update release reached and gate reset." +
                " Train/engine/session/native cleanup are doubles, NOT native gameplay or rejoin acceptance.");
        }
    }
}
