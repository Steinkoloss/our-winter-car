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
    public sealed class UpdateTests : IDisposable
    {
        internal static readonly string[] Names = {
            "UpdateVehicleStates", "UpdateStarterDraws", "UpdateStarterWear", "UpdateVehicleCoolant",
            "UpdateDrivetrainWearStates", "UpdateWheelHealthStates", "UpdateVehicleDamage",
            "UpdateVehicleCondition", "UpdateFuelTransfers", "UpdateVehicleClimate"
        };
        private static readonly string[] Surrounding = { "bags", "train", "clothing", "ventti", "trailer", "carRadio" };
        private readonly ITestOutputHelper _output;
        public UpdateTests(ITestOutputHelper output)
        {
            _output = output;
            SyncEventLog.Clear(); SyncEventLog.Dumps = 0;
            WinterMPPlugin.Log.Warnings.Clear(); WinterMPPlugin.Log.Errors.Clear();
            GuestSaveGuard.ProtectWorld = false; WinterMPPlugin.DevKeysEnabled.Value = false;
        }
        public void Dispose() { SessionManager.Instance = null; GuestSaveGuard.ProtectWorld = false; }
        public static IEnumerable<object[]> Failures()
        {
            foreach (SessionState role in new[] { SessionState.Hosting, SessionState.Connected })
                foreach (string name in Names) yield return new object[] { role, name };
        }

        [Theory, MemberData(nameof(Failures))]
        public void EscapingVehicleUpdateKeepsEverySiblingRunningAndBoundsOnlyItsOwnRetries(SessionState role, string failing)
        {
            var session = new SessionManager { State = role }; SessionManager.Instance = session;
            session.Sent.Add("unrelated-existing-message");
            var error = new InvalidOperationException("injected " + failing, new Exception("original inner detail"));
            var calls = new List<string>(); var attempts = new List<float>();
            var manager = new WorldSyncManager();
            manager.BindVehicleUpdates((name, s) => {
                Assert.Same(session, s); calls.Add(name);
                if (name == failing) { attempts.Add(UnityEngine.Time.unscaledTime); throw error; }
            });
            manager.BindSurroundingUpdates((name, s) => { Assert.Same(session, s); calls.Add(name); });
            int late = 0;
            manager.Bind(_ => late++, () => late++, s => { Assert.Same(session, s); late++; });
            string[] all = Surrounding.Take(2).Concat(Names).Concat(Surrounding.Skip(2)).ToArray();
            foreach (float now in new[] { 10f, 10.999f, 11f, 12.999f, 13f, 14f, 100f })
            {
                calls.Clear(); manager.TickUpdate(now); manager.TickLate(now);
                bool attempt = now == 10 || now == 11 || now == 13;
                Assert.Equal(all.Where(name => attempt || name != failing), calls);
                Assert.False(manager.Disabled); Assert.Equal(0f, manager.GlobalBackoff);
            }
            Assert.Equal(new[] { 10f, 11f, 13f }, attempts);
            Assert.Equal(21, late); Assert.Equal(7, manager.WorldUpdates);
            Assert.Equal(3, manager.ErrorCount); Assert.Equal(0, manager.Releases);
            Assert.Same(session, SessionManager.Instance); Assert.Equal(role, session.State);
            Assert.Equal(new[] { "unrelated-existing-message" }, session.Sent);
            Assert.Equal(3, SyncEventLog.Entries.Count);
            Assert.All(SyncEventLog.Entries, text => {
                Assert.Contains("error: Update.VehicleWorldSync." + failing, text);
                Assert.Contains(error.ToString(), text);
            });
            Assert.Equal(3, WinterMPPlugin.Log.Warnings.Count);
            Assert.Contains("retrying only this callback in 1s", WinterMPPlugin.Log.Warnings[0]);
            Assert.Contains("retrying only this callback in 2s", WinterMPPlugin.Log.Warnings[1]);
            Assert.Contains("quarantined", WinterMPPlugin.Log.Warnings[2]);
            Assert.Empty(WinterMPPlugin.Log.Errors); Assert.Equal(0, SyncEventLog.Dumps);
            _output.WriteLine("PORTABLE production Update -> UpdateWorldSync; " + role + " " + failing +
                ": same-frame and backoff siblings preserved; attempts exactly 10/11/13s, then quarantine; 21 late callbacks; original full errors and session/outbox preserved.");

            session.State = SessionState.Idle; manager.TickUpdate(100.01f);
            Assert.Equal(1, manager.Releases); Assert.Equal(0, manager.ErrorCount);
            Assert.Empty(SyncEventLog.Entries);
            manager.TickUpdate(100.02f); Assert.Equal(1, manager.Releases);
            SessionManager.Instance = session; session.State = role;
            calls.Clear(); manager.TickUpdate(100.03f);
            Assert.Equal(all, calls); Assert.Equal(4, attempts.Count); Assert.Equal(1, manager.ErrorCount);
            _output.WriteLine("Session-end Update used production inactive branch and release double once; callback retried with fresh budget on next portable session. Native restoration NOT_TESTED.");
        }

        [Theory]
        [InlineData(SessionState.Hosting)] [InlineData(SessionState.Connected)]
        public void LocalUpdateCooldownCannotDelaySessionEnd(SessionState role)
        {
            SessionManager.Instance = new SessionManager { State = role };
            var manager = new WorldSyncManager();
            manager.BindVehicleUpdates((name, s) => { if (name == Names[0]) throw new Exception("first failure"); });
            manager.TickUpdate(10); SessionManager.Instance = null; manager.TickUpdate(10.01f);
            Assert.Equal(1, manager.Releases); Assert.Equal(0, manager.ErrorCount);
            Assert.Equal(0f, manager.GlobalBackoff); Assert.False(manager.Disabled);
        }
    }
}
