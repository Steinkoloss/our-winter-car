using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using UnityEngine;
using WinterMP.Core.Session;

namespace WinterMP.GuestSaveProbe
{
    // Opt-in developer probe. Run only in an isolated game copy/profile. Never ship.
    [BepInPlugin("com.ourwintercar.wintermp.guest-save-probe", "Guest save probe", "0.1.0")]
    [BepInDependency("com.ourwintercar.wintermp")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private readonly List<string> _results = new List<string>();
        private bool _enabled;
        private bool _ran;
        private int _failures;
        private Type _guard = null!;
        private string _root = string.Empty;
        private LivePerformanceProbe? _livePerformance;
        private LiveBagProbe? _liveBags;
        private LiveSleepProbe? _liveSleep;
        private LiveDriveProbe? _liveDrive;

        private void Awake()
        {
            if (Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_DRIVE_TEST") == "1")
            {
                _liveDrive = new LiveDriveProbe();
                _liveDrive.Start();
            }
            if (Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_SLEEP_TEST") == "1")
            {
                _liveSleep = new LiveSleepProbe();
                _liveSleep.Start();
            }
            if (Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_BAG_TEST") == "1")
            {
                _liveBags = new LiveBagProbe();
                try { _liveBags.Start(); }
                catch (Exception error)
                {
                    Logger.LogError("Isolated gameplay probe startup failed: " + error);
                    _liveBags = null;
                    throw;
                }
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-live-performance-probe") >= 0)
            {
                _livePerformance = new LivePerformanceProbe();
                _livePerformance.Start();
            }
            _enabled = Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-guest-save-probe") >= 0
                || Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-performance-probe") >= 0
                || Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-menu-continue-probe") >= 0;
        }

        private void OnLevelWasLoaded(int level) { _liveBags?.TaxiMenuLoaded(); }

        private void Update()
        {
            // Each explicitly enabled probe has its own command channel, allowing
            // one isolated session to cover the complete gameplay journey.
            _liveDrive?.Tick();
            _liveSleep?.Tick();
            _liveBags?.Tick();
            _livePerformance?.Tick();
            if (_liveDrive != null || _liveSleep != null || _liveBags != null || _livePerformance != null) return;
            if (!_enabled || _ran || Time.realtimeSinceStartup < 2f || SessionManager.Instance == null) return;
            _ran = true;
            _root = Path.GetFullPath(Path.Combine(Application.dataPath, "../guest-save-probe"));
            try
            {
                Directory.CreateDirectory(_root);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-menu-continue-probe") >= 0)
                {
                    Require(SessionManager.Instance.State == SessionState.Idle
                        && File.Exists(Path.Combine(BepInEx.Paths.GameRootPath, "wintermp-performance-sandbox.txt")),
                        "Menu probe requires a marked isolated solo boot.");
                    MenuContinueChecks.Run(Check); Finish(); return;
                }
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-performance-probe") >= 0)
                {
                    Require(SessionManager.Instance.State == SessionState.Idle, "Performance probe requires a fresh solo boot.");
                    PerformanceChecks.Run(_root, Check); Finish(); return;
                }
                _guard = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Session.GuestSaveGuard", true);
                Require(!Protected(), "Probe requires a fresh solo boot without join launch flags.");
                RunChecks();
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-head-fitting-probe") >= 0)
                    HeadFittingChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-milk-condition-probe") >= 0)
                    MilkConditionChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-firewood-buyer-probe") >= 0)
                    FirewoodBuyerChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-firewood-payment-probe") >= 0)
                    FirewoodPaymentChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-valve-turn-probe") >= 0)
                    ValveTurnChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-head-attachment-probe") >= 0)
                    HeadAttachmentChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-sleep-consent-probe") >= 0)
                    SleepConsentChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-part-isolation-probe") >= 0)
                    PartIsolationChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-part-adjustment-probe") >= 0)
                    PartAdjustmentChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-bag-probe") >= 0)
                    BagChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-bag-spill-probe") >= 0)
                    BagSpillChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-bag-pickup-probe") >= 0)
                    BagPickupChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-sparkplug-opening-probe") >= 0)
                    SparkplugOpeningChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-sparkplug-fitting-probe") >= 0)
                    SparkplugFittingChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-sparkplug-tool-probe") >= 0)
                    SparkplugFittingChecks.RunTools(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-native-bag-part-probe") >= 0)
                    NativeBagPartChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-hand-screw-probe") >= 0)
                    PartHandScrewChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-belt-visual-probe") >= 0)
                    PartBeltVisualChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-vehicle-damage-probe") >= 0)
                    VehicleDamageChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-distributor-timing-probe") >= 0)
                    PartTimingChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-vehicle-state-probe") >= 0)
                    VehicleStateChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-guest-engine-probe") >= 0)
                    GuestEngineChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-vehicle-rpm-probe") >= 0)
                    VehicleRpmChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-guest-engine-input-probe") >= 0)
                    GuestEngineInputChecks.Run(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-starter-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunStarter(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-waterpump-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunWaterpump(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-fuelpump-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunFuelpump(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-oilpump-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunOilpump(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-camshaft-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunCamshaft(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-rocker-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunRockers(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-alternator-mechanical-input-probe") >= 0)
                    GuestEngineInputChecks.RunAlternatorMechanical(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-radiator-fan-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunRadiatorFan(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-piston-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunPistons(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-bearing-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunBearings(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-fluid-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunFluids(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-powertrain-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunPowertrain(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-timingbelt-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunTimingbelt(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-fanbelt-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunFanbelt(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-alternator-electrical-input-probe") >= 0)
                    GuestEngineInputChecks.RunAlternatorElectrical(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-sparkplug-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunSparkplugs(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-flywheel-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunFlywheel(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-revlimiter-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunRevlimiter(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-ignition-coil-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunIgnitionCoil(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-wiring-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunWiring(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-engine-block-input-probe") >= 0)
                    GuestEngineInputChecks.RunEngineBlock(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-gearbox-starter-input-probe") >= 0)
                    GuestEngineInputChecks.RunGearbox(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-cylinder-head-input-probe") >= 0)
                    GuestEngineInputChecks.RunCylinderHead(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-carburettor-input-probe") >= 0)
                    GuestEngineInputChecks.RunCarburettor(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-intake-input-probe") >= 0)
                    GuestEngineInputChecks.RunIntake(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-exhaust-input-probe") >= 0)
                    GuestEngineInputChecks.RunExhaust(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-coolant-hose-input-probe") >= 0)
                    GuestEngineInputChecks.RunCoolantHoses(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-cooling-airflow-input-probe") >= 0)
                    GuestEngineInputChecks.RunCoolingAirflow(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-cooling-ambient-input-probe") >= 0)
                    GuestEngineInputChecks.RunCoolingAmbient(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-radiator-input-probe") >= 0)
                    GuestEngineInputChecks.RunRadiator(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-rocker-cover-input-probe") >= 0)
                    GuestEngineInputChecks.RunRockerCover(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-oilpan-input-probe") >= 0)
                    GuestEngineInputChecks.RunOilpan(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-valve-adjustment-probe") >= 0)
                    GuestEngineInputChecks.RunValveInputs(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-battery-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunBattery(Check);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-starter-flywheel-engine-input-probe") >= 0)
                    GuestEngineInputChecks.RunStarterFlywheel(Check);
            }
            catch (Exception e)
            {
                _failures++;
                _results.Add("FAIL setup: " + e);
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-sparkplug-lifecycle-probe") >= 0)
            { StartCoroutine(RunLifecycle()); return; }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-join-recovery-probe") >= 0)
            { StartCoroutine(RunJoinRecovery()); return; }
            Finish();
        }

        private void OnDestroy() { _livePerformance?.Dispose(); }

        private IEnumerator RunJoinRecovery()
        {
            var checks = JoinRecoveryChecks.Run(Check);
            try
            {
                while (true)
                {
                    bool next;
                    try { next = checks.MoveNext(); }
                    catch (Exception e) { _failures++; _results.Add("FAIL join recovery setup: " + e); break; }
                    if (!next) break;
                    yield return checks.Current;
                }
            }
            finally { (checks as IDisposable)?.Dispose(); }
            Finish();
        }

        private IEnumerator RunLifecycle()
        {
            var checks = SparkplugFittingChecks.RunLifecycle(Check);
            try
            {
                while (true)
                {
                    bool next;
                    try { next = checks.MoveNext(); }
                    catch (Exception e) { _failures++; _results.Add("FAIL sparkplug lifecycle setup: " + e); break; }
                    if (!next) break;
                    yield return checks.Current;
                }
            }
            finally
            {
                try { (checks as IDisposable)?.Dispose(); }
                catch (Exception e) { _failures++; _results.Add("FAIL sparkplug lifecycle cleanup: " + e); }
            }
            Finish();
        }

        private void Finish()
        {
            _results.Add("Failures: " + _failures);
            File.WriteAllLines(Path.Combine(_root, "result.txt"), _results.ToArray());
            foreach (string line in _results) Logger.LogInfo(line);
            Application.Quit();
        }

        private bool Protected()
        {
            return (bool)_guard.GetProperty("ProtectWorld", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null, null);
        }

        private void Check(string name, Action action)
        {
            try { action(); _results.Add("PASS " + name); }
            catch (Exception e) { _failures++; _results.Add("FAIL " + name + ": " + e); }
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private void RunChecks()
        {
            string raw = Path.Combine(_root, "raw.txt");
            string tagged = Path.Combine(_root, "tagged.txt");
            string source = Path.Combine(_root, "source.txt");
            string destination = Path.Combine(_root, "destination.txt");
            string folder = Path.Combine(_root, "folder");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "keep.txt"), "keep");
            Check("host raw write, append and read", () =>
            {
                ES2.SaveRaw("host", raw); ES2.AppendRaw("+", raw);
                Require(File.ReadAllText(raw) == "host+", "Host raw writes failed.");
            });
            Check("host tagged write, overwrite and read", () =>
            {
                ES2.Save(42, tagged + "?tag=value"); ES2.Save(43, tagged + "?tag=value");
                Require(ES2.Load<int>(tagged + "?tag=value") == 43, "Host tagged write failed.");
            });
            Check("host rename and delete", () =>
            {
                // The guest checks leave this destination populated. Each run must
                // start the host rename with the same absent-destination fixture.
                File.Delete(destination);
                ES2.SaveRaw("source", source); ES2.Rename(source, destination);
                Require(File.Exists(destination) && !File.Exists(source), "Host rename failed.");
                ES2.Delete(destination); Require(!File.Exists(destination), "Host delete failed.");
            });
            string[] deathFiles = { "meshsave.txt", "trophies.txt", "hockeyleague.txt", "speedcam.txt",
                "savefile.txt", "carparts.txt", "items2.txt" };
            foreach (string file in deathFiles) File.WriteAllText(Path.Combine(_root, file), "original");
            File.WriteAllText(source, "source"); File.WriteAllText(destination, "destination");
            byte[] taggedBefore = File.ReadAllBytes(tagged);

            Require((bool)_guard.GetMethod("TryBeginGuest", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, null), "Guest save protection failed to install.");
            Require(Protected(), "Guest protection did not latch.");
            Check("guest raw overwrite and append suppressed", () =>
            {
                ES2.SaveRaw("guest", raw); ES2.AppendRaw("guest", raw);
                Require(File.ReadAllText(raw) == "host+", "Guest changed raw save.");
            });
            Check("guest tagged overwrite and deletion suppressed", () =>
            {
                ES2.Save(99, tagged + "?tag=value"); ES2.Delete(tagged + "?tag=value");
                Require(Convert.ToBase64String(File.ReadAllBytes(tagged)) == Convert.ToBase64String(taggedBefore), "Guest changed tagged bytes.");
                Require(ES2.Load<int>(tagged + "?tag=value") == 43, "Guest changed tagged read.");
            });
            Check("guest low-memory writer preserves temp and world files", () =>
            {
                File.WriteAllText(raw + "tmp", "existing-temp");
                var settings = new ES2Settings(raw) { optimizeMode = ES2Settings.OptimizeMode.LowMemory };
                using (var writer = ES2Writer.Create(settings))
                {
                    Require(writer.writer.BaseStream is MemoryStream, "LowMemory still opened a disk stream.");
                    writer.WriteRaw(new byte[] { 1, 2, 3 }); writer.Save(false); writer.stream.Store();
                }
                Require(File.ReadAllText(raw + "tmp") == "existing-temp", "Guest changed temp file.");
                Require(File.ReadAllText(raw) == "host+", "Direct Store changed save.");
            });
            Check("guest cannot create a new save", () =>
            {
                string fresh = Path.Combine(_root, "fresh.txt");
                ES2.SaveRaw("new", fresh, new ES2Settings { optimizeMode = ES2Settings.OptimizeMode.LowMemory });
                Require(!File.Exists(fresh) && !File.Exists(fresh + "tmp"), "Guest created save or temp.");
            });
            foreach (string file in deathFiles)
                Check("guest permadeath deletion suppressed: " + file, () =>
                {
                    string path = Path.Combine(_root, file);
                    ES2.Delete(path); ES2File.DeleteFile(new ES2Settings(path));
                    Require(File.ReadAllText(path) == "original", "Guest deleted save.");
                });
            Check("guest rename and move suppressed", () =>
            {
                ES2.Rename(source, destination);
                ES2File.MoveFile(new ES2Settings(source), new ES2Settings(destination));
                Require(File.ReadAllText(source) == "source" && File.ReadAllText(destination) == "destination", "Guest moved save.");
            });
            Check("guest folder deletion and rename suppressed", () =>
            {
                ES2.Delete(folder + "/"); ES2.Rename(folder + "/", folder + "-moved/");
                Require(File.ReadAllText(Path.Combine(folder, "keep.txt")) == "keep", "Guest modified folder.");
            });
            Check("guest default-folder deletion suppressed", () =>
            {
                string previous = ES2GlobalSettings.defaultPCDataPath;
                var location = ES2GlobalSettings.defaultSaveLocation;
                try
                {
                    ES2GlobalSettings.defaultPCDataPath = _root;
                    ES2GlobalSettings.defaultSaveLocation = ES2Settings.SaveLocation.File;
                    ES2.DeleteDefaultFolder();
                    Require(File.ReadAllText(raw) == "host+", "Guest deleted default folder.");
                }
                finally
                {
                    ES2GlobalSettings.defaultPCDataPath = previous;
                    ES2GlobalSettings.defaultSaveLocation = location;
                }
            });
            Check("guest ES2 PlayerPrefs persistence suppressed", () =>
            {
                var settings = new ES2Settings("guest-save-probe.txt") { saveLocation = ES2Settings.SaveLocation.PlayerPrefs };
                string key = settings.filenameData.playerPrefsPath;
                PlayerPrefs.SetString(key, "original");
                try
                {
                    using (var writer = ES2Writer.Create(settings))
                    {
                        writer.WriteRaw(new byte[] { 1, 2, 3 }); writer.Save(false); writer.stream.Store();
                    }
                    Require(PlayerPrefs.GetString(key) == "original", "ES2 overwrote PlayerPrefs save.");
                }
                finally { PlayerPrefs.DeleteKey(key); }
            });
            Check("memory serialization still works", () =>
            {
                using (var writer = ES2Writer.Create(new ES2Settings(ES2Settings.SaveLocation.Memory)))
                {
                    writer.WriteRaw(new byte[] { 1, 2, 3 }); writer.Save(false);
                    Require(writer.writer.BaseStream.Length == 3, "Memory writer failed.");
                }
            });
            Check("native PlayerPrefs settings still work", () =>
            {
                const string key = "wintermp-guest-save-probe";
                PlayerPrefs.SetInt(key, 123); PlayerPrefs.Save();
                Require(PlayerPrefs.GetInt(key) == 123, "Native settings blocked.");
                PlayerPrefs.DeleteKey(key); PlayerPrefs.Save();
            });
            Check("shutdown retains protection and prevents hosting", () =>
            {
                var session = SessionManager.Instance!;
                session.Shutdown("Guest save probe teardown");
                Require(Protected(), "Shutdown cleared save protection.");
                session.StartHostLocal(38947);
                Require(!session.IsHost && session.State == SessionState.Idle, "Protected world became host.");
                ES2.SaveRaw("after-shutdown", raw);
                Require(File.ReadAllText(raw) == "host+", "Shutdown reopened persistence.");
            });
        }
    }
}
