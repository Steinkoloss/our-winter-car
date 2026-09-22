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
        // Invoke production reset bodies; native Clear methods remain static-only evidence.
        internal void ResetAllWorldCallbackErrors() { ResetLateUpdateErrors(); ResetVehicleUpdateErrors(); }
    }
}

namespace WorldSyncCallbacks.Tests
{
    public sealed class UpdateRetryTests : IDisposable
    {
        public UpdateRetryTests()
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
        public void SuccessfulLiveRetryDoesNotRefundFlappingBudgetOrReplayEarlierCallbacks(SessionState role)
        {
            Session(role); var manager = new WorldSyncManager();
            bool fail = true; int attempts = 0, earlier = 0, later = 0;
            manager.BindVehicleUpdates((name, s) => {
                if (name == UpdateTests.Names[0]) earlier++;
                if (name == UpdateTests.Names[1]) { attempts++; if (fail) throw new Exception("flapping"); }
                if (name == UpdateTests.Names[2]) later++;
            });
            manager.TickUpdate(10); Assert.Equal(1, earlier); Assert.Equal(1, attempts); Assert.Equal(1, later);
            fail = false; manager.TickUpdate(10.99f); Assert.Equal(1, attempts);
            manager.TickUpdate(11); manager.TickUpdate(11.01f); Assert.Equal(3, attempts);
            fail = true; manager.TickUpdate(12); Assert.Equal(4, attempts);
            fail = false; manager.TickUpdate(13.999f); Assert.Equal(4, attempts);
            manager.TickUpdate(14); Assert.Equal(5, attempts);
            fail = true; manager.TickUpdate(14.1f); Assert.Equal(6, attempts);
            fail = false; manager.TickUpdate(200); Assert.Equal(6, attempts);
            Assert.Equal(9, earlier); Assert.Equal(9, later); Assert.Equal(3, manager.ErrorCount);
            Assert.False(manager.Disabled);
        }

        [Theory]
        [InlineData(SessionState.Hosting)] [InlineData(SessionState.Connected)]
        public void StaggeredVehicleFailuresAndLateFailuresHaveIndependentClocks(SessionState role)
        {
            Session(role); var manager = new WorldSyncManager();
            var calls = new List<string>(); int lateAttempts = 0;
            manager.BindVehicleUpdates((name, s) => {
                calls.Add(name);
                if (name == UpdateTests.Names[0] || name == UpdateTests.Names[1] && UnityEngine.Time.unscaledTime >= 10.5f)
                    throw new Exception(name);
            });
            manager.Bind(_ => { lateAttempts++; if (lateAttempts == 1) throw new Exception("late"); }, () => { }, _ => { });
            manager.TickUpdate(10); manager.TickLate(10.25f);
            manager.TickUpdate(10.5f);
            calls.Clear(); manager.TickUpdate(11);
            Assert.Contains(UpdateTests.Names[0], calls); Assert.DoesNotContain(UpdateTests.Names[1], calls);
            manager.TickLate(11.24f); Assert.Equal(1, lateAttempts);
            manager.TickLate(11.25f); Assert.Equal(2, lateAttempts);
            calls.Clear(); manager.TickUpdate(11.5f);
            Assert.DoesNotContain(UpdateTests.Names[0], calls); Assert.Contains(UpdateTests.Names[1], calls);
            manager.TickUpdate(13); manager.TickUpdate(13.5f);
            calls.Clear(); manager.TickUpdate(100); manager.TickLate(100);
            Assert.Equal(UpdateTests.Names.Skip(2), calls);
            Assert.Equal(7, manager.ErrorCount); Assert.False(manager.Disabled); Assert.Equal(0f, manager.GlobalBackoff);
        }

        [Theory]
        [InlineData(SessionState.Hosting)] [InlineData(SessionState.Connected)]
        public void WidespreadUpdateFailuresRetainAllErrorsAndFinishAlreadyAdmittedSiblings(SessionState role)
        {
            var session = Session(role); var manager = new WorldSyncManager();
            var calls = new List<string>(); var healthy = new List<string>(); int late = 0;
            manager.BindVehicleUpdates((name, s) => { calls.Add(name); throw new Exception("original " + name); });
            manager.BindSurroundingUpdates((name, s) => healthy.Add(name));
            manager.Bind(_ => late++, () => late++, s => late++);
            manager.TickUpdate(10);
            Assert.Equal(UpdateTests.Names, calls);
            Assert.Equal(new[] { "bags", "train", "clothing", "ventti", "trailer", "carRadio" }, healthy);
            Assert.True(manager.Disabled); Assert.Equal(10, manager.ErrorCount);
            Assert.Equal(10, SyncEventLog.Entries.Count); Assert.Equal(7, WinterMPPlugin.Log.Warnings.Count);
            Assert.Equal(3, WinterMPPlugin.Log.Errors.Count); Assert.Equal(3, SyncEventLog.Dumps);
            foreach (string name in UpdateTests.Names) Assert.Single(SyncEventLog.Entries.Where(text => text.Contains("original " + name)));
            manager.TickLate(10); manager.TickUpdate(100); Assert.Equal(0, late); Assert.Equal(10, calls.Count);
            session.State = SessionState.Failed; manager.TickUpdate(100.1f);
            Assert.Equal(1, manager.Releases); Assert.False(manager.Disabled); Assert.Equal(0, manager.ErrorCount);
            session.State = role; calls.Clear(); manager.TickUpdate(100.2f); Assert.Equal(UpdateTests.Names, calls);
        }

        [Theory]
        [InlineData(SessionState.Hosting)] [InlineData(SessionState.Connected)]
        public void MixedGlobalAndLocalFailuresReachOriginalEightErrorFallback(SessionState role)
        {
            Session(role); var manager = new WorldSyncManager(); int radio = 0;
            manager.OnWorldUpdate = () => throw new Exception("discovery escape");
            for (int i = 0; i < 7; i++) manager.TickUpdate(1 + i);
            manager.OnWorldUpdate = () => { };
            manager.BindVehicleUpdates((name, s) => { if (name == UpdateTests.Names[0]) throw new Exception("eighth"); });
            manager.BindSurroundingUpdates((name, s) => { if (name == "carRadio") radio++; });
            manager.TickUpdate(8); Assert.True(manager.Disabled); Assert.Equal(8, manager.ErrorCount);
            Assert.Equal(1, radio); Assert.Contains("eighth", WinterMPPlugin.Log.Errors.Single());
            manager.TickUpdate(9); Assert.Equal(1, radio);
        }

        [Theory, MemberData(nameof(UpdateTests.Failures), MemberType = typeof(UpdateTests))]
        public void WorldResetClearsEachVehicleBudgetWithoutErasingGlobalHistory(SessionState role, string failing)
        {
            Session(role); var manager = new WorldSyncManager(); int attempts = 0;
            manager.BindVehicleUpdates((name, s) => { if (name == failing) { attempts++; throw new Exception(name); } });
            manager.TickUpdate(10); manager.TickUpdate(11); manager.TickUpdate(13); manager.TickUpdate(100);
            Assert.Equal(3, attempts);
            manager.ResetAllWorldCallbackErrors(); manager.ResetAllWorldCallbackErrors();
            Assert.Equal(3, manager.ErrorCount); Assert.Equal(3, SyncEventLog.Entries.Count);
            manager.TickUpdate(1); Assert.Equal(4, attempts); Assert.Equal(4, manager.ErrorCount);
            Assert.Contains("subsystem 1/3", WinterMPPlugin.Log.Warnings.Last());
            var next = new WorldSyncManager(); int fresh = 0;
            next.BindVehicleUpdates((name, s) => fresh++); next.TickUpdate(0);
            Assert.Equal(10, fresh); Assert.Equal(0, next.ErrorCount);
        }

        [Theory]
        [InlineData(SessionState.Hosting)] [InlineData(SessionState.Connected)]
        public void GuestProtectionAndDiscoveryFailuresRemainGlobalAndDoNotFailOpen(SessionState role)
        {
            Session(role); var manager = new WorldSyncManager(); int calls = 0;
            manager.BindVehicleUpdates((name, s) => calls++);
            GuestSaveGuard.ProtectWorld = true;
            manager.BindProtection(() => throw new Exception("guest protection prerequisite"));
            manager.TickUpdate(10); Assert.Equal(0, calls); Assert.Equal(11f, manager.GlobalBackoff);
            manager.BindProtection(() => { });
            manager.OnDiscovery = () => throw new Exception("discovery prerequisite");
            manager.TickUpdate(11); Assert.Equal(0, calls); Assert.Equal(12f, manager.GlobalBackoff);
            manager.OnDiscovery = () => { }; manager.TickUpdate(11.99f); Assert.Equal(0, calls);
            manager.TickUpdate(12); Assert.Equal(10, calls); Assert.Equal(2, manager.ErrorCount);
        }

        [Theory]
        [InlineData(SessionState.Idle)] [InlineData(SessionState.Connecting)] [InlineData(SessionState.Failed)] [InlineData(null)]
        public void InactiveSessionNeverRunsVehicleCallbacks(SessionState? state)
        {
            SessionManager.Instance = state.HasValue ? new SessionManager { State = state.Value } : null;
            var manager = new WorldSyncManager(); int calls = 0;
            manager.BindVehicleUpdates((name, s) => calls++);
            manager.TickUpdate(10); manager.TickUpdate(11);
            Assert.Equal(0, calls); Assert.Equal(1, manager.Releases); Assert.Equal(0, manager.ErrorCount);
        }

        [Theory]
        [InlineData(SessionState.Hosting)] [InlineData(SessionState.Connected)]
        public void OriginalSceneInitAndSnapshotAdmissionRemainInProductionUpdate(SessionState role)
        {
            var session = Session(role); var manager = new WorldSyncManager(); int calls = 0;
            manager.BindVehicleUpdates((name, s) => { Assert.Same(session, s); calls++; });
            manager.GameLevel = false; manager.TickUpdate(10); Assert.Equal(0, calls);
            manager.GameLevel = true; manager.SetAdmission(false, false); manager.SetDoorCount(1);
            manager.TickUpdate(11); manager.TickUpdate(12); Assert.Equal(20, calls);
            Assert.Equal(role == SessionState.Connected ? new[] { "WorldSnapshotRequest" } : new string[0], session.Sent);
            Assert.Equal(0, manager.ErrorCount);
        }

        [Theory]
        [InlineData(SessionState.Hosting)] [InlineData(SessionState.Connected)]
        public void FailedSessionReleaseAfterLocalQuarantineRemainsRateLimited(SessionState role)
        {
            var session = Session(role); var manager = new WorldSyncManager();
            manager.BindVehicleUpdates((name, s) => { if (name == UpdateTests.Names[0]) throw new Exception("vehicle"); });
            manager.TickUpdate(10); manager.TickUpdate(11); manager.TickUpdate(13);
            session.State = SessionState.Idle;
            manager.OnRelease = () => throw new Exception("release");
            manager.TickUpdate(14); manager.TickUpdate(14.99f); Assert.Equal(1, manager.Releases);
            Assert.Equal(4, manager.ErrorCount); Assert.Equal(15f, manager.GlobalBackoff);
            manager.OnRelease = () => { }; manager.TickUpdate(15); manager.TickUpdate(16);
            Assert.Equal(2, manager.Releases); Assert.Equal(0, manager.ErrorCount); Assert.False(manager.Disabled);
        }
    }
}
